using Mahod.Intergreen.Core.Legacy;
using Xunit;

namespace Mahod.Intergreen.Core.Tests;

/// <summary>
/// Addendum §B.3: no golden row falls below 3 s, so an engine that omits max(3, …)
/// passes all 148 rows green. These synthetic tests cover exactly what the golden data cannot.
/// </summary>
public class MinimumIntergreenTests
{
    [Theory]
    [InlineData(0.5, 3)]   // ceil = 1 → minimum 3
    [InlineData(1.2, 3)]   // ceil = 2 → minimum 3
    [InlineData(2.2, 3)]   // ceil = 3 → 3
    [InlineData(2.95, 3)]
    [InlineData(3.0, 3)]   // exactly 3 stays 3
    [InlineData(3.001, 4)] // above 3 → ceiling wins
    [InlineData(4.029, 5)] // §5.7 plain ceiling — no 0.1 grace
    public void Guidelines2025_policy_applies_ceiling_then_3s_minimum(double raw, int expected)
    {
        Assert.Equal(expected, FinalIgPolicy.Guidelines2025.Resolve(raw));
    }

    [Theory]
    [InlineData(2.05, 2)]  // legacy floor rule may go below 3 — no minimum is applied (OPEN QUESTION, Addendum §B.5)
    [InlineData(2.5, 3)]
    public void Legacy_policy_has_no_3s_minimum_by_design(double raw, int expected)
    {
        Assert.Equal(expected, FinalIgPolicy.MahodLegacy.Resolve(raw));
    }

    [Fact]
    public void Legacy_policy_is_undefined_below_1s()
    {
        Assert.Null(FinalIgPolicy.MahodLegacy.Resolve(0.4)); // workbook blank (MOD by zero)
        Assert.Null(FinalIgPolicy.MahodLegacy.Resolve(0.999999));
    }
}

public class RoundingStrategyTests
{
    private readonly MahodLegacyRounding _legacy = new();
    private readonly GuidelinesCeilingRounding _ceil = new();

    [Theory]
    [InlineData(4.0, 4)]    // exact integer → floor (fraction 0 < 0.1)
    [InlineData(4.029, 4)]  // fraction below 0.1 → round DOWN (the deviation)
    [InlineData(4.099, 4)]
    // NOTE: the literal 4.1 is 4.0999999999999996 in IEEE doubles, so its fraction IS below
    // 0.1 and Excel itself rounds it DOWN — the engine is bit-compatible with the workbook.
    [InlineData(4.15, 5)]   // fraction clearly above 0.1 → up
    [InlineData(4.5, 5)]
    [InlineData(6.042, 6)]
    [InlineData(1.05, 1)]
    public void Mahod_legacy_rounding(double raw, int expected)
        => Assert.Equal(expected, _legacy.Round(raw));

    [Theory]
    [InlineData(4.0, 4)]
    [InlineData(4.029, 5)]
    [InlineData(6.042, 7)]
    [InlineData(0.5, 1)]
    public void Guidelines_ceiling_rounding(double raw, int expected)
        => Assert.Equal(expected, _ceil.Round(raw));
}

/// <summary>Hand-computed clearing-time cases — independent of the golden workbooks.</summary>
public class ClearingTimeTests
{
    private static LegacyWorkbookCompatibilityCalculator Calc() => new(
        new LegacyConstants(),
        LegacyTemplateVariant.V2GlobalVehicleLength,
        new System.Collections.Generic.Dictionary<string, LegacyMovementParameters>());

    [Fact]
    public void Pedestrian_clearing_is_distance_over_walking_speed()
    {
        // v == pedSpeed → dist / 1.2, vehicle length NOT added
        var t = Calc().ClearingTimeSeconds(8.35, 1.2, 12.0);
        Assert.Equal(8.35 / 1.2, t, 12);
    }

    [Fact]
    public void Above_80kph_uses_constant_speed_model()
    {
        // v = 90 km/h = 25 m/s: T = reaction + v/(2*decel) + (d + len)/v
        //   = 1 + 25/7 + (30+12)/25 = 6.251428571...
        var t = Calc().ClearingTimeSeconds(30.0, 25.0, 12.0);
        Assert.Equal(1.0 + 25.0 / 7.0 + 42.0 / 25.0, t, 12);
    }

    [Fact]
    public void At_50kph_uses_acceleration_model()
    {
        // v = 50/3.6, coeff = 1.5 - 1.5*50/80 = 0.5625, brake = v²/7
        var v = 50.0 / 3.6;
        var coeff = 1.5 - 1.5 * 50.0 / 80.0;
        var brake = v * v / 7.0;
        var d = 24.35;
        var len = 12.0;
        var expected = (-v + System.Math.Sqrt(v * v + 2.0 * (brake + d + len) * coeff)) / coeff + 1.0;
        Assert.Equal(expected, Calc().ClearingTimeSeconds(d, v, len), 12);
    }

    [Fact]
    public void Zero_speed_returns_error_sentinel()
    {
        Assert.Equal(-1.0, Calc().ClearingTimeSeconds(10.0, 0.0, 12.0));
    }

    [Fact]
    public void Blank_distance_is_zero_in_compatibility_mode_only()
    {
        // Addendum §C.3 — compatibility behaviour; production blocks this upstream.
        var withBlank = Calc().ClearingTimeSeconds(null, 25.0, 12.0);
        var withZero = Calc().ClearingTimeSeconds(0.0, 25.0, 12.0);
        Assert.Equal(withZero, withBlank);
    }
}
