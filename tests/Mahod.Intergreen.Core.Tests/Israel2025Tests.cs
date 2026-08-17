using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core;
using Mahod.Intergreen.Core.Israel2025;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Core.Tests;

public class Israel2025CalculationTests
{
    private readonly Israel2025CalculationService _svc = new();

    [Fact]
    public void Vehicle_clearing_cases_from_rule_pack_tests_json()
    {
        var testsPath = Path.Combine(RulePackLoader.RulesRoot(), "israel-2025-06", "tests.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(testsPath));
        foreach (var c in doc.RootElement.GetProperty("vehicleClearing").EnumerateArray())
        {
            var l2 = c.GetProperty("l2").GetDouble();
            var len = c.GetProperty("lengthM").GetDouble();
            if (c.TryGetProperty("expectT2X", out var ex))
            {
                var got = _svc.VehicleClearingFastSec(c.GetProperty("sxKph").GetDouble(), l2, len);
                Assert.Equal(ex.GetDouble(), got, 9);
            }
            if (c.TryGetProperty("expectT2Y", out var ey))
            {
                var got = _svc.VehicleClearingSlowSec(c.GetProperty("syKph").GetDouble(), l2, len);
                Assert.Equal(ey.GetDouble(), got, 9);
            }
        }
    }

    [Fact]
    public void Slow_acceleration_formula_uses_denominator_50_not_80()
    {
        // §5.5.1(c): a1Y = 1.5 - 1.5*SY/50 — deliberately different from the legacy /80 rule.
        Assert.Equal(0.75, Israel2025CalculationService.SlowAccelerationMps2(25), 12);
        Assert.Equal(0.45, Israel2025CalculationService.SlowAccelerationMps2(35), 12);
    }

    [Fact]
    public void Slow_speed_at_or_above_50_is_outside_the_model_and_throws()
    {
        Assert.Throws<NotSupportedException>(() => Israel2025CalculationService.SlowAccelerationMps2(50));
        Assert.Throws<NotSupportedException>(() => Israel2025CalculationService.SlowAccelerationMps2(60));
    }

    [Fact]
    public void Entering_time_is_distance_over_fast_speed()
    {
        Assert.Equal(5.02 / (50.0 / 3.6), _svc.EnteringSec(5.02, 50), 12);
    }

    [Fact]
    public void Pedestrian_clearing_is_width_over_walking_speed()
    {
        Assert.Equal(8.35 / 1.2, _svc.PedestrianClearingSec(8.35, 1.2), 12);
        Assert.Equal(8.35 / 1.0, _svc.PedestrianClearingSec(8.35, 1.0), 12);
    }

    [Fact]
    public void Bicycle_clearing_is_constant_speed_with_braking_distance()
    {
        // S=25kph=6.944m/s, L1=S²/7=6.8893298..., l=2, L2=10 → T = 1 + (6.8893+10+2)/6.9444
        var s = 25.0 / 3.6;
        var expected = 1.0 + (s * s / 7.0 + 10.0 + 2.0) / s;
        Assert.Equal(expected, _svc.BicycleClearingSec(25, 10.0, 2.0), 12);
    }

    [Fact]
    public void Lrt_from_stop_within_acceleration_range()
    {
        // Sx=50kph=13.888..; accel range = Sx²/(2*1.2) = 80.375m; d=30+15=45 < range
        // T2II = sqrt(2*45/1.2) = sqrt(75) = 8.660254...
        Assert.Equal(Math.Sqrt(75.0), _svc.LrtClearingFromStopSec(50, 30.0, 15.0), 12);
    }

    [Fact]
    public void Lrt_from_stop_beyond_acceleration_range()
    {
        // Sx=25kph=6.944m/s; accel range = 20.094m; d=45 → T = Sx/1.2 + (45-20.094)/Sx
        var sx = 25.0 / 3.6;
        var range = sx * sx / 2.4;
        var expected = sx / 1.2 + (45.0 - range) / sx;
        Assert.Equal(expected, _svc.LrtClearingFromStopSec(25, 30.0, 15.0), 12);
    }

    [Theory]
    [InlineData(2.0, 0.5, 3)]   // small difference → minimum 3
    [InlineData(3.4, 0.0, 4)]   // ceil(3.4) = 4
    [InlineData(1.0, 4.0, 3)]   // negative difference → minimum 3
    [InlineData(6.042, 0.0, 7)] // §5.7 ceiling (workbook would say 6 — that is the Legacy rule, not this one)
    public void Final_vehicle_vs_vehicle(double t2, double t3, int expected)
        => Assert.Equal(expected, _svc.FinalVehicleVsVehicle(t2, t3));

    [Fact]
    public void Final_vehicle_clears_pedestrian_enters_does_not_subtract_t3()
    {
        Assert.Equal(6, _svc.FinalVehicleClearsPedestrianEnters(5.2));
        Assert.Equal(3, _svc.FinalVehicleClearsPedestrianEnters(1.1));
    }

    [Fact]
    public void Final_pedestrian_clears_vehicle_enters()
    {
        Assert.Equal(5, _svc.FinalPedestrianClearsVehicleEnters(6.958, 2.5));
        Assert.Equal(3, _svc.FinalPedestrianClearsVehicleEnters(2.0, 1.0));
    }
}

public class ParameterResolverTests
{
    private static RulePack Pack(string id)
        => RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), id));

    [Fact]
    public void Missing_road_classification_blocks_with_IG_CLS_001()
    {
        var resolver = new ParameterResolver(Pack("israel-2025-06"));
        var (p, findings) = resolver.ResolveVehicle(Maneuver.Through, VehicleLengthClass.Standard12,
            new ProjectClassification());
        Assert.Null(p);
        Assert.Contains(findings, f => f.Code == ParameterResolver.CodeMissingClassification && f.Severity == Severity.Error);
    }

    [Fact]
    public void Interurban_without_posted_speed_blocks_with_IG_CLS_002()
    {
        var resolver = new ParameterResolver(Pack("israel-2025-06"));
        var (p, findings) = resolver.ResolveVehicle(Maneuver.Through, VehicleLengthClass.Standard12,
            new ProjectClassification { RoadType = RoadEnvironment.Interurban });
        Assert.Null(p);
        Assert.Contains(findings, f => f.Code == ParameterResolver.CodeMissingPostedSpeed && f.Severity == Severity.Error);
    }

    [Fact]
    public void Urban_through_resolves_with_source_references()
    {
        var resolver = new ParameterResolver(Pack("israel-2025-06"));
        var (p, findings) = resolver.ResolveVehicle(Maneuver.Through, VehicleLengthClass.Standard12,
            new ProjectClassification { RoadType = RoadEnvironment.Urban, PostedSpeedKph = 70 });
        Assert.Empty(findings.Where(f => f.Severity == Severity.Error));
        Assert.NotNull(p);
        Assert.Equal(70, p!.FastClearingKph.Value);   // max(posted, 50)
        Assert.Equal(25, p.SlowClearingKph.Value);
        Assert.Equal(12, p.VehicleLengthMeters.Value);
        Assert.Contains("Table 5.1", p.FastClearingKph.Source);
    }

    [Fact]
    public void Unclassified_crossing_blocks_with_IG_CLS_003()
    {
        var resolver = new ParameterResolver(Pack("israel-2025-06"));
        var (speed, findings) = resolver.ResolvePedestrianSpeed("a", new ProjectClassification());
        Assert.Null(speed);
        Assert.Contains(findings, f => f.Code == ParameterResolver.CodeMissingCrossingContext);
    }

    [Fact]
    public void Crossing_context_changes_speed_1_2_vs_1_0()
    {
        var resolver = new ParameterResolver(Pack("israel-2025-06"));
        var cls = new ProjectClassification
        {
            Crossings = new System.Collections.Generic.Dictionary<string, CrossingContext>
            {
                ["a"] = CrossingContext.Standard,
                ["b"] = CrossingContext.HighDemand,
            },
        };
        Assert.Equal(1.2, resolver.ResolvePedestrianSpeed("a", cls).Speed!.Value);
        Assert.Equal(1.0, resolver.ResolvePedestrianSpeed("b", cls).Speed!.Value);
    }
}
