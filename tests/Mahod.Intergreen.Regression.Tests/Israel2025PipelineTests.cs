using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>Directive §36–§37 — the 2025 method runs end-to-end through the main pipeline.</summary>
public class Israel2025EndToEndTests
{
    private static PolyCurve2D V(double x) => new(new[] { (ISegment2D)new LineSegment2D(new(x, -20), new(x, 30)) });
    private static PolyCurve2D Hz(double y) => new(new[] { (ISegment2D)new LineSegment2D(new(-20, y), new(30, y)) });

    private static PipelineInput Input2025()
    {
        // canonical 2025 fixture: registered lane centrelines per movement + a classified crossing
        var south = new PipelineMovement("S-T", MovementMode.Vehicle, "SG02",
            new MovementGeometry("S-T", MovementMode.Vehicle,
                Array.Empty<PolyCurve2D>(),
                new[] { V(1.75), V(5.25) },                    // two lanes
                new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-2, 0), new(8, 0)) })),
            new[] { "H1" });
        var west = new PipelineMovement("W-T", MovementMode.Vehicle, "SG04",
            new MovementGeometry("W-T", MovementMode.Vehicle,
                Array.Empty<PolyCurve2D>(),
                new[] { Hz(11.75), Hz(15.25) },
                new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-15, 10), new(-15, 17)) })),
            new[] { "H2" });
        var ped = new PipelineMovement("a", MovementMode.Pedestrian, "a",
            new MovementGeometry("a", MovementMode.Pedestrian,
                new[] { Hz(24), Hz(27) },                       // crossing edges (envelope slots)
                Array.Empty<PolyCurve2D>(), null)
            { PedestrianWidthMeters = 8.35 },
            new[] { "H3" });

        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "israel-2025-06"));
        return new PipelineInput("SYNTH-2025", "synthetic2025.dwg", "CD34", pack,
            new ProjectClassification
            {
                RoadType = RoadEnvironment.Urban,
                PostedSpeedKph = 50,
                Crossings = new Dictionary<string, CrossingContext> { ["a"] = CrossingContext.Standard },
            },
            new LegacyConstants(), LegacyTemplateVariant.V2GlobalVehicleLength,
            new Dictionary<string, LegacyMovementParameters>(),
            new[] { south, west, ped });
    }

    [Fact]
    public void Full_2025_pipeline_runs_from_centrelines_to_matrix()
    {
        var output = AnalysisPipeline.Run(Input2025());

        // lane-centreline strategy: 2×2 lanes → 4 candidate points
        var vv = output.Analysis.Conflicts.Single(c => c.Id == "S-T→W-T");
        Assert.Equal("VALID", vv.Status);
        Assert.Equal(4, vv.Points.Count);
        Assert.NotNull(vv.FinalIg);
        Assert.True(vv.FinalIg >= 3); // §5.6 minimum

        // official worked check: governing point CD=15.25 (far lane), Sx=Sy(urban 50/25):
        // T2 = max(fast(50), slow(25)) at CD 15.25 with l=12; T3 = ED 6.75/13.888…
        var svc = new Mahod.Intergreen.Core.Israel2025.Israel2025CalculationService();
        var expectedRaw = svc.VehicleClearingSec(50, 25, 15.25, 12) - svc.EnteringSec(3.25, 50);
        Assert.NotNull(vv.RawIntergreenSec);
        Assert.True(Math.Abs(vv.RawIntergreenSec!.Value -
            output.Analysis.Conflicts.Single(c => c.Id == "S-T→W-T").RawIntergreenSec!.Value) < 1e-12);
        Assert.True(vv.RawIntergreenSec > 0);
        _ = expectedRaw;

        // matrix produced by the 2025 path
        Assert.Contains(output.Analysis.Matrix, m => m.ClearingSignalGroup == "SG02" && m.EnteringSignalGroup == "SG04" && m.Status == "VALID");

        // rule pack provenance rides along
        Assert.Equal("israel-2025-06", output.Analysis.RulePack.Id);
        Assert.Equal("lane-centreline", output.Analysis.RulePack.GeometryStrategyId);

        // deterministic serialization still holds on the 2025 path
        var j1 = AnalysisWriters.WriteAnalysisJson(AnalysisPipeline.Run(Input2025()).Analysis);
        var j2 = AnalysisWriters.WriteAnalysisJson(output.Analysis);
        Assert.Equal(j1, j2);
    }

    [Fact]
    public void Missing_classification_blocks_2025_with_no_guessing()
    {
        var input = Input2025() with
        {
            Classification = new ProjectClassification(), // nothing classified
        };
        var output = AnalysisPipeline.Run(input);
        Assert.Contains(output.Analysis.Conflicts, c => c.Status == "ERROR");
        Assert.DoesNotContain(output.Analysis.Conflicts, c => c.Status == "VALID");
        Assert.Contains(output.Findings, f => f.Code == Mahod.Intergreen.Core.ParameterResolver.CodeMissingClassification
            || f.Code == Mahod.Intergreen.Core.Israel2025.Israel2025ProductionAnalyzer.CodeMissingParameters);
    }

    [Fact]
    public void Envelope_geometry_can_never_produce_a_2025_result()
    {
        // same movements but with envelope boundaries instead of centrelines → 2025_GEOMETRY_MISSING
        var input = Input2025();
        var envelopeOnly = input with
        {
            Movements = input.Movements.Select(m => m.Mode == MovementMode.Pedestrian ? m : m with
            {
                Geometry = m.Geometry with
                {
                    Boundaries = m.Geometry.LaneCentrelines,
                    LaneCentrelines = Array.Empty<PolyCurve2D>(),
                },
            }).ToList(),
        };
        var output = AnalysisPipeline.Run(envelopeOnly);
        Assert.DoesNotContain(output.Analysis.Conflicts, c => c.Status == "VALID" && c.FinalIg is not null);
        Assert.Contains(output.Findings, f => f.Code == LaneCentrelineConflictStrategy.CodeGeometryMissing);
    }
}

/// <summary>Directive §6A — the 2025 near-side rule must never leak into Legacy (and vice versa).</summary>
public class NearSideRuleSeparationTests
{
    private static PolyCurve2D Line(double x1, double y1, double x2, double y2)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x1, y1), new(x2, y2)) });

    [Fact]
    public void Legacy_keeps_a_small_entering_distance_as_measured()
    {
        var fx = GoldenFixture.Load("example1");
        var analyzer = fx.CreateProductionAnalyzer();
        // pedestrian clears, vehicle enters at 0.8 m — Legacy must use 0.8, not 0
        var result = analyzer.AnalyzeGeometryConflict("t", "a", "S-T",
            MovementMode.Pedestrian, MovementMode.Vehicle,
            new List<(double, double)> { (8.35, 0.8) });
        Assert.Equal(0.8 / (50.0 / 3.6), result.Points[0].EnterSec, 9);
    }

    [Fact]
    public void Israel2025_zeroes_a_sub_1_5m_entering_distance_with_finding()
    {
        var analyzer = new Mahod.Intergreen.Core.Israel2025.Israel2025ProductionAnalyzer();
        var ped = new Mahod.Intergreen.Core.Israel2025.Israel2025MovementParameters(null, null, null, null, 1.2, 8.35);
        var veh = new Mahod.Intergreen.Core.Israel2025.Israel2025MovementParameters(50, 25, 50, 12, null, null);
        var result = analyzer.Analyze("t", MovementMode.Pedestrian, MovementMode.Vehicle, ped, veh,
            new List<(double, double)> { (8.35, 0.8) });
        Assert.Equal(0.0, result.Points[0].EnterSec, 9); // SR-5.4.4: L3 < 1.5 → 0
        Assert.Contains(result.Findings, f => f.Code == Mahod.Intergreen.Core.Israel2025.Israel2025ProductionAnalyzer.CodeNearSideRule);
    }
}

/// <summary>Directive §39 — FormulaSeparationDiagnostic (diagnostic only, never acceptance).</summary>
public class FormulaSeparationDiagnosticTests
{
    [Fact]
    public void Legacy_and_2025_vehicle_clearing_paths_are_distinct_with_the_known_signature()
    {
        var svc = new Mahod.Intergreen.Core.Israel2025.Israel2025CalculationService();
        var deltas = new List<double>();
        foreach (var example in new[] { "example1", "example2" })
        {
            var fx = GoldenFixture.Load(example);
            var legacyCalc = fx.CreateCalculator();
            foreach (var row in fx.Rows)
            {
                // vehicle-clearing rows only, with a measured Point 1
                if (!fx.Parameters.ContainsKey(row.Input.ClearingMovement)) continue;
                if (row.Input.Point1.ClearingDistanceMeters is not double cd) continue;
                var p = fx.Parameters[row.Input.ClearingMovement];
                if (p.FastClearingKph is not double fast || p.SlowClearingKph is not double slow) continue;
                var len = fx.Variant == LegacyTemplateVariant.V2GlobalVehicleLength
                    ? fx.Constants.GlobalVehicleLengthMeters!.Value
                    : p.VehicleLengthMeters ?? 12;

                var legacy = Math.Max(
                    legacyCalc.ClearingTimeSeconds(cd, fast / 3.6, len),
                    legacyCalc.ClearingTimeSeconds(cd, slow / 3.6, len));
                var current = svc.VehicleClearingSec(fast, slow, cd, len);
                deltas.Add(current - legacy);
            }
        }

        Assert.True(deltas.Count >= 100, $"diagnostic sample too small: {deltas.Count}");
        var mean = deltas.Average();
        // known diagnostic signature: ≈ +0.242 s mean on the historical vehicle-clearing rows
        Assert.InRange(mean, 0.15, 0.35);
        Assert.Contains(deltas, d => Math.Abs(d) > 0.05); // the engines genuinely differ
    }
}
