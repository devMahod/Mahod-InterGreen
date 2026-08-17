using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Xunit;

namespace Mahod.Intergreen.Core.Tests;

/// <summary>
/// SYNTHETIC NUMERICAL SAFETY TESTS (Final Hotfix §5–§8).
/// The 148 golden rows exercise none of these paths — golden compatibility alone is insufficient.
/// </summary>
public class ProductionNumericsTests
{
    private readonly LegacyProductionCalculator _calc = new();

    [Fact]
    public void Continuity_around_80_kph()
    {
        var speedsKph = new[] { 79.9, 79.999999, 80.0, 80.000001, 80.1 };
        var times = new List<double>();
        foreach (var kph in speedsKph)
        {
            var o = _calc.VehicleClearingTimeSec(24.0, kph / 3.6, 12.0);
            Assert.Null(o.Finding);
            var t = o.Value!.Value;
            Assert.False(double.IsNaN(t) || double.IsInfinity(t), $"{kph} kph → {t}");
            Assert.True(t > 0, $"{kph} kph → {t}");
            times.Add(t);
        }
        // continuous: adjacent values differ by a hair
        for (var i = 1; i < times.Count; i++)
            Assert.True(Math.Abs(times[i] - times[i - 1]) < 1e-2,
                $"discontinuity between {speedsKph[i - 1]} and {speedsKph[i]}: {times[i - 1]} vs {times[i]}");
        // and the tight neighbourhood of 80 agrees to ~1e-6
        Assert.True(Math.Abs(times[1] - times[2]) < 1e-6);
        Assert.True(Math.Abs(times[3] - times[2]) < 1e-6);
    }

    [Fact]
    public void At_exactly_80_production_equals_constant_speed_model()
    {
        // a = max(0, 1.5-1.5*80/80) = 0 → t = reaction + v/(2*decel) + (d+len)/v
        var v = 80.0 / 3.6;
        var expected = 1.0 + v / 7.0 + (24.0 + 12.0) / v;
        var o = _calc.VehicleClearingTimeSec(24.0, v, 12.0);
        Assert.Equal(expected, o.Value!.Value, 9);
    }

    [Theory]
    [InlineData(25.0)]
    [InlineData(50.0)]
    [InlineData(70.0)]
    [InlineData(79.0)]
    [InlineData(90.0)]
    public void Stable_form_agrees_with_historical_form_away_from_singularity(double kph)
    {
        var historical = new LegacyWorkbookCompatibilityCalculator(
            new LegacyConstants(), LegacyTemplateVariant.V2GlobalVehicleLength,
            new Dictionary<string, LegacyMovementParameters>());
        var v = kph / 3.6;
        var h = historical.ClearingTimeSeconds(24.35, v, 12.0);
        var p = _calc.VehicleClearingTimeSec(24.35, v, 12.0).Value!.Value;
        Assert.Equal(h, p, 9);
    }

    [Fact]
    public void Compatibility_reproduces_the_workbook_singularity_at_exactly_80()
    {
        // Excel: coeff = 0 → #DIV/0! → IFERROR → -1 (LEGACY_SOURCE_FORMULA_SINGULARITY)
        var historical = new LegacyWorkbookCompatibilityCalculator(
            new LegacyConstants(), LegacyTemplateVariant.V2GlobalVehicleLength,
            new Dictionary<string, LegacyMovementParameters>());
        Assert.Equal(-1.0, historical.ClearingTimeSeconds(24.0, 80.0 / 3.6, 12.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Invalid_speed_is_a_blocking_finding_not_a_sentinel(double speedMps)
    {
        var o = _calc.VehicleClearingTimeSec(10.0, speedMps, 12.0);
        Assert.Null(o.Value);
        Assert.Equal(LegacyProductionCalculator.CodeInvalidSpeed, o.Finding!.Code);
        Assert.Equal(Severity.Error, o.Finding.Severity);
    }

    [Fact]
    public void Negative_distance_is_a_blocking_finding()
    {
        var o = _calc.VehicleClearingTimeSec(-1.0, 50.0 / 3.6, 12.0);
        Assert.Equal(LegacyProductionCalculator.CodeInvalidInput, o.Finding!.Code);
    }
}

/// <summary>
/// Pedestrian metamorphic tests (Final Hotfix §5): the algorithm branch follows
/// Movement.Mode; only the time changes with the configured speed.
/// </summary>
public class PedestrianMetamorphicTests
{
    private static readonly Dictionary<string, LegacyMovementParameters> Params = new()
    {
        ["X-T"] = new LegacyMovementParameters(50, 25, 12, null),
    };

    private static LegacyProductionAnalyzer Analyzer() => new(
        new LegacyConstants(GlobalVehicleLengthMeters: 12),
        LegacyTemplateVariant.V2GlobalVehicleLength, Params);

    public static IEnumerable<object[]> Speeds()
    {
        yield return new object[] { 1.2 };  // Table 5.2 standard
        yield return new object[] { 1.0 };  // Table 5.2 high-demand / LRT / institutions
        yield return new object[] { 0.9 };  // SYNTHETIC test speed — NOT an official guideline value
    }

    [Theory]
    [MemberData(nameof(Speeds))]
    public void Pedestrian_branch_is_selected_by_mode_and_time_scales_with_speed(double pedSpeed)
    {
        var row = new ConflictRowInput(1, "p", "X-T",
            new MeasuredPoint(8.0, 5.0), default, default, default);

        var result = Analyzer().AnalyzeRow(row,
            MovementMode.Pedestrian, MovementMode.Vehicle,
            pedestrianSpeedMpsOverride: pedSpeed);

        Assert.False(result.IsBlocked);
        var pt = result.Calculation!.PointResults[0]!;
        // pedestrian algorithm: clearing = distance / speed, no vehicle length, no braking term
        Assert.Equal(8.0 / pedSpeed, pt.ClearFastSec, 12);
        Assert.Equal(8.0 / pedSpeed, pt.ClearSlowSec, 12);
        // vehicle length is not part of a pedestrian calculation
        Assert.Null(result.Calculation.VehicleLengthMeters);
    }

    [Fact]
    public void Pedestrian_at_1_0_must_not_fall_into_the_vehicle_formula()
    {
        // Historical trap: the workbook detects pedestrians via speed == 1.2, so a 1.0 m/s
        // pedestrian silently became a "vehicle". Production branches by mode.
        var row = new ConflictRowInput(2, "p", "X-T",
            new MeasuredPoint(8.0, 0.0), default, default, default);
        var result = Analyzer().AnalyzeRow(row, MovementMode.Pedestrian, MovementMode.Vehicle,
            pedestrianSpeedMpsOverride: 1.0);
        Assert.Equal(8.0, result.Calculation!.PointResults[0]!.ClearFastSec, 12); // 8.0/1.0, not a vehicle formula
    }

    [Fact]
    public void Unknown_vehicle_movement_blocks_with_IG_VAL_004_never_pedestrian_fallback()
    {
        var row = new ConflictRowInput(3, "Z-Q", "X-T",
            new MeasuredPoint(10.0, 0.0), default, default, default);
        var result = Analyzer().AnalyzeRow(row, MovementMode.Vehicle, MovementMode.Vehicle);
        Assert.True(result.IsBlocked);
        Assert.Contains(result.Findings, f => f.Code == LegacyProductionAnalyzer.CodeUnknownMovementParameters);
    }
}

/// <summary>Directive §40–§42 — approved-exception provenance and the Legacy production safety floor.</summary>
public class OverrideAndFloorTests
{
    [Fact]
    public void Safety_reducing_override_without_full_approval_is_refused()
    {
        var o = new Mahod.Intergreen.Core.ProjectOverride("OV1",
            Mahod.Intergreen.Core.OverrideScope.Conflict, "finalIg", 7, 6, "reason", null, null, null);
        var r = Mahod.Intergreen.Core.ProjectOverrideService.Resolve("finalIg", 7, new[] { o });
        Assert.Equal(7, r.EffectiveValue); // base stays in force
        Assert.Null(r.Applied);
        Assert.Contains(r.Findings, f => f.Code == Mahod.Intergreen.Core.ProjectOverrideService.CodeSafetyReducing
            && f.Severity == Severity.Error);
    }

    [Fact]
    public void Fully_approved_safety_reducing_override_applies_with_review_and_visible_base()
    {
        var o = new Mahod.Intergreen.Core.ProjectOverride("OV2",
            Mahod.Intergreen.Core.OverrideScope.Conflict, "finalIg", 7, 6,
            "approved engineering exception", "APPROVAL-123", "D. Suchinsky", "2026-08-01");
        var r = Mahod.Intergreen.Core.ProjectOverrideService.Resolve("finalIg", 7, new[] { o });
        Assert.Equal(6, r.EffectiveValue);
        Assert.NotNull(r.Applied);
        Assert.Equal(7, r.Applied!.BaseValue); // base value permanently retained
        Assert.Contains(r.Findings, f => f.Severity == Severity.ReviewRequired);
    }

    [Fact]
    public void Non_whitelisted_override_target_is_rejected()
    {
        var o = new Mahod.Intergreen.Core.ProjectOverride("OV3",
            Mahod.Intergreen.Core.OverrideScope.Project, "formula", 1, 2, "r", "a", "x", "d");
        var r = Mahod.Intergreen.Core.ProjectOverrideService.Resolve("formula", 1, new[] { o });
        Assert.Equal(1, r.EffectiveValue);
        Assert.Contains(r.Findings, f => f.Code == Mahod.Intergreen.Core.ProjectOverrideService.CodeUnknownTarget);
    }

    [Fact]
    public void Legacy_production_below_3s_is_floored_with_review()
    {
        // synthetic geometry conflict yielding a small raw IG (short CD, long ED)
        var analyzer = new LegacyProductionAnalyzer(
            new LegacyConstants(GlobalVehicleLengthMeters: 12),
            LegacyTemplateVariant.V2GlobalVehicleLength,
            new Dictionary<string, LegacyMovementParameters>
            {
                ["A-T"] = new(50, 25, 12, null),
                ["B-T"] = new(50, 25, 12, null),
            });
        var result = analyzer.AnalyzeGeometryConflict("t", "A-T", "B-T",
            MovementMode.Vehicle, MovementMode.Vehicle,
            new List<(double, double)> { (2.0, 25.0) }); // raw ≈ 2.6 − 1.8 ≈ 1.2 → legacy 2 → floor 3
        Assert.Equal(RowStatus.ReviewRequired, result.Status);
        Assert.Equal(3, result.FinalIg);
        Assert.Contains(result.Findings, f => f.Code == LegacyProductionAnalyzer.CodeBelowSafetyFloor);
    }

    [Fact]
    public void Compatibility_calculator_is_not_floored()
    {
        var calc = new LegacyWorkbookCompatibilityCalculator(
            new LegacyConstants(GlobalVehicleLengthMeters: 12),
            LegacyTemplateVariant.V2GlobalVehicleLength,
            new Dictionary<string, LegacyMovementParameters>
            {
                ["A-T"] = new(50, 25, 12, null),
                ["B-T"] = new(50, 25, 12, null),
            });
        var row = calc.ComputeRow(new ConflictRowInput(1, "A-T", "B-T",
            new MeasuredPoint(2.0, 25.0), default, default, default));
        Assert.True(row.FinalIg < 3); // historical behaviour preserved for regression
    }
}
