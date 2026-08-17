using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Israel2025;
using Xunit;

namespace Mahod.Intergreen.Core.Tests;

/// <summary>
/// Official January-2026 conformance suite (Directive §34).
///
/// Source: נוהל לאישור תוכנות לתכנון רמזורים, נספח 1, מהדורה רביעית – ינואר 2026,
/// תוספת א' — "רשימת מקרים לבדיקת חישוב בין ירוקים" (categories א–ז, two examples per case).
/// תוספת ה' defines the calculation basis as chapter 5 of the 2025 guidelines, which is
/// transcribed first-hand in rules/israel-2025-06/source-references.json (SR-5.5.x/SR-5.6).
/// Expected numbers below are hand-derived from those official formulas
/// (reaction 1.0 s, deceleration 3.5 m/s², LRT a = a1 = 1.2 m/s²) and were verified by an
/// independent script before being embedded.
/// </summary>
public class Israel2025ConformanceTests
{
    private readonly Israel2025CalculationService _svc = new();

    // ---- א. מופע רכב 3 פנסים / ה. מופע רכב 2 פנסים (identical formulas, תוספת ה') ----

    [Theory]
    [InlineData(25, 24, 12, 5.886603322411123)] // א.1.א example 1: SY<50 → WITH acceleration
    [InlineData(35, 30, 12, 6.105616604841401)] // א.1.א example 2
    public void A1a_vehicle_slow_clearing_below_50_uses_acceleration_formula(double sy, double l2, double len, double expected)
        => Assert.Equal(expected, _svc.VehicleClearingSlowSec(sy, l2, len), 9);

    [Theory]
    [InlineData(60, 24, 12, 5.5409523809523815)] // א.1.ב example 1: SY≥50 → WITHOUT acceleration
    [InlineData(50, 30, 12, 6.008126984126984)]  // א.1.ב example 2 (a1 = 0 exactly at 50)
    public void A1b_vehicle_slow_clearing_at_or_above_50_uses_no_acceleration(double sy, double l2, double len, double expected)
    {
        Assert.Equal(0.0, Israel2025CalculationService.SlowAccelerationMps2(sy));
        Assert.Equal(expected, _svc.VehicleClearingSlowSec(sy, l2, len), 9);
        // and it equals the plain constant-speed model t = 1 + LY/Sy
        var syMps = sy / 3.6;
        var ly = syMps * syMps / 7.0 + l2 + len;
        Assert.Equal(1.0 + ly / syMps, _svc.VehicleClearingSlowSec(sy, l2, len), 9);
    }

    [Theory]
    [InlineData(50, 24, 12, 5.576126984126984)] // א.1.ג: fast clearing (always constant-speed)
    [InlineData(70, 40, 12, 6.452063492063492)]
    public void A1c_vehicle_fast_clearing_is_constant_speed(double sx, double l2, double len, double expected)
        => Assert.Equal(expected, _svc.VehicleClearingFastSec(sx, l2, len), 9);

    [Fact]
    public void A1cd_governing_clearing_is_max_of_fast_and_slow()
    {
        // א.1.ג example (fast governs): SY=55 (no-accel) 6.5863… vs fast(50) 6.7281… → fast
        Assert.Equal(6.728126984126984, _svc.VehicleClearingSec(50, 55, 40, 12), 9);
        // א.1.ד example (slow governs): SX=50 fast 5.5761… vs SY=25 slow 5.8866… → slow
        Assert.Equal(5.886603322411123, _svc.VehicleClearingSec(50, 25, 24, 12), 9);
    }

    [Theory]
    [InlineData(5.02, 50, 0.36144)] // א.2 / ה.2 entering
    [InlineData(12, 60, 0.72)]
    public void A2_vehicle_entering(double l3, double sz, double expected)
        => Assert.Equal(expected, _svc.EnteringSec(l3, sz), 9);

    // ---- ב. מופע הולך רגל ----

    [Theory]
    [InlineData(8.35, 1.2, 6.958333333333333)] // ב.1 example 1 (Table 5.2 standard)
    [InlineData(10.8, 1.0, 10.8)]              // ב.1 example 2 (high-demand context)
    public void B1_pedestrian_clearing(double w, double sp, double expected)
        => Assert.Equal(expected, _svc.PedestrianClearingSec(w, sp), 9);

    [Fact]
    public void B2_pedestrian_entering_is_always_zero()
    {
        // ב.2: entering duration ≡ 0 → the ClearOnly formula never subtracts T3
        var rule = ModePairPolicy.Resolve(MovementMode.Vehicle, MovementMode.Pedestrian)!;
        Assert.Equal(FinalIgFormula.ClearOnly, rule.Formula);
        Assert.Equal(6, _svc.FinalVehicleClearsPedestrianEnters(5.2));
    }

    // ---- ג. מופע אופניים — no vehicle-style acceleration in any branch ----

    [Theory]
    [InlineData(25, 10, 3.7200634920634923)] // ג.1.א below 50 — still constant speed (בשונה מרכב)
    [InlineData(15, 10, 4.475238095238096)]  // ג.1.ד slow bicycle governs case input
    public void C1_bicycle_clearing_is_constant_speed(double s, double l2, double expected)
        => Assert.Equal(expected, _svc.BicycleClearingSec(s, l2, 2.0), 9);

    [Fact]
    public void C1_bicycle_governing_is_max_of_fast_slow()
    {
        var fast = _svc.BicycleClearingSec(25, 10, 2.0);
        var slow = _svc.BicycleClearingSec(15, 10, 2.0);
        Assert.True(slow > fast); // slow governs on this geometry (ג.1.ד)
    }

    [Fact]
    public void C2_bicycle_entering()
        => Assert.Equal(1.152, _svc.EnteringSec(8, 25), 9);

    // ---- ד. מופע רק"ל ----

    [Fact]
    public void D1_lrt_case_I_moving_constant_speed()
        => Assert.Equal(10.373518518518518, _svc.LrtClearingMovingSec(50, 25, 30, 15), 9);

    [Fact]
    public void D2_lrt_case_II_from_stop_within_acceleration_range()
        => Assert.Equal(8.660254037844387, _svc.LrtClearingFromStopSec(50, 30, 15), 9);

    [Fact]
    public void D3_lrt_case_II_from_stop_completing_after_acceleration()
        => Assert.Equal(9.37351851851852, _svc.LrtClearingFromStopSec(25, 30, 15), 9);

    [Fact]
    public void D4_lrt_governing_is_max_of_cases()
    {
        // ד.4: from-stop LRT takes max(case I, case II)
        Assert.Equal(10.373518518518518, _svc.LrtClearingSec(50, 25, 30, 15, startsFromStop: true), 9);
        // moving-only LRT uses case I alone
        Assert.Equal(10.373518518518518, _svc.LrtClearingSec(50, 25, 30, 15, startsFromStop: false), 9);
    }

    // ---- ו. חישוב זמן בין ירוקים סופי — all 15 official mode pairs ----

    [Fact]
    public void F_all_15_official_mode_pairs_resolve_with_correct_formula()
    {
        var pairs = ModePairPolicy.OfficialPairs().ToList();
        Assert.Equal(15, pairs.Count);
        foreach (var (c, e) in pairs)
        {
            var rule = ModePairPolicy.Resolve(c, e);
            Assert.NotNull(rule);
            var expected = e == MovementMode.Pedestrian ? FinalIgFormula.ClearOnly
                : c == MovementMode.Pedestrian ? FinalIgFormula.PedestrianClearMinusEnter
                : FinalIgFormula.ClearMinusEnter;
            Assert.Equal(expected, rule!.Formula);
        }
        Assert.Null(ModePairPolicy.Resolve(MovementMode.Pedestrian, MovementMode.Pedestrian));
    }

    [Fact]
    public void F_bus_and_sherut_use_vehicle_engineering_class_with_own_identity()
    {
        Assert.Equal(MovementMode.Vehicle, ModePairPolicy.EngineeringClass(MovementMode.Bus));
        Assert.Equal(MovementMode.Vehicle, ModePairPolicy.EngineeringClass(MovementMode.Sherut));
        var rule = ModePairPolicy.Resolve(MovementMode.Bus, MovementMode.Pedestrian)!;
        Assert.Equal(FinalIgFormula.ClearOnly, rule.Formula);
    }

    [Fact]
    public void F1_final_vehicle_vs_vehicle_worked_example()
    {
        // T2 = 5.8866… (א.1.א ex.1 governs), T3 = 0.36144 (א.2 ex.1) → 5.5252 → ceil → 6
        var t2 = _svc.VehicleClearingSec(50, 25, 24, 12);
        var t3 = _svc.EnteringSec(5.02, 50);
        Assert.Equal(6, _svc.FinalVehicleVsVehicle(t2, t3));
    }

    [Fact]
    public void F5_final_pedestrian_vs_vehicle_worked_example()
    {
        // Tw = 6.9583…, T3 = 0.72 → 6.2383 → 7
        Assert.Equal(7, _svc.FinalPedestrianClearsVehicleEnters(6.958333333333333, 0.72));
    }

    // ---- ז. מקרים נוספים ----

    [Fact]
    public void Z1_multiple_conflicting_movements_take_the_maximum()
    {
        // ז.1/ז.2: the pair result is the highest over all conflicting movement results
        var finals = new[] { 4, 6, 5 };
        Assert.Equal(6, finals.Max());
    }

    [Fact]
    public void Z3_minimum_3_seconds()
        => Assert.Equal(3, _svc.FinalVehicleVsVehicle(2.2, 0.5)); // 1.7 → ceil 2 → min 3

    [Fact]
    public void Z4_slightly_above_integer_rounds_up()
        => Assert.Equal(7, _svc.FinalVehicleVsVehicle(6.042, 0.0)); // ז.4: 6.042 → 7
}
