using System;
using Mahod.Intergreen.Core.Legacy;

namespace Mahod.Intergreen.Core.Israel2025;

/// <summary>
/// June 2025 calculation engine — §5.5–§5.6, transcribed from the official document
/// (source-reference IDs point into rules/israel-2025-06/source-references.json).
/// Completely separate from the Legacy engine (v3 §27). All inputs carry explicit units.
/// </summary>
public sealed class Israel2025CalculationService
{
    private readonly double _reactionSec;
    private readonly double _decelMps2;

    public Israel2025CalculationService(double reactionTimeSec = 1.0, double decelerationMps2 = 3.5)
    {
        _reactionSec = reactionTimeSec;
        _decelMps2 = decelerationMps2;
    }

    /// <summary>§5.5.1(c) — slow-clearing acceleration a1Y = 1.5 − 1.5·SY/50 (SY in km/h). SR-5.5.1.</summary>
    public static double SlowAccelerationMps2(double syKph)
    {
        var a1 = 1.5 - 1.5 * syKph / 50.0;
        if (a1 <= 0)
            throw new NotSupportedException(
                $"a1Y = 1.5-1.5*{syKph}/50 is non-positive. Table 5.1 caps SY at 35 km/h; " +
                "a slow speed of 50+ is outside the guidelines' model. See OPEN_QUESTIONS.md.");
        return a1;
    }

    /// <summary>§5.5.1 fast case (SR-5.5.1): T2X = t + LX/Sx, LX = L1X + L2 + l, L1X = Sx²/2a.</summary>
    public double VehicleClearingFastSec(double sxKph, double l2Meters, double vehicleLengthMeters)
    {
        var sx = sxKph / 3.6;
        if (sx <= 0) throw new ArgumentOutOfRangeException(nameof(sxKph));
        var l1x = sx * sx / (2.0 * _decelMps2);
        var lx = l1x + l2Meters + vehicleLengthMeters;
        return _reactionSec + lx / sx;
    }

    /// <summary>§5.5.1 slow case (SR-5.5.1): quadratic acceleration model with a1Y.</summary>
    public double VehicleClearingSlowSec(double syKph, double l2Meters, double vehicleLengthMeters)
    {
        var sy = syKph / 3.6;
        if (sy <= 0) throw new ArgumentOutOfRangeException(nameof(syKph));
        var a1 = SlowAccelerationMps2(syKph);
        var l1y = sy * sy / (2.0 * _decelMps2);
        var ly = l1y + l2Meters + vehicleLengthMeters;
        return _reactionSec + (-sy + Math.Sqrt(sy * sy + 2.0 * ly * a1)) / a1;
    }

    /// <summary>§5.5.1(d): T2 = max(T2X, T2Y).</summary>
    public double VehicleClearingSec(double sxKph, double syKph, double l2Meters, double vehicleLengthMeters)
        => Math.Max(
            VehicleClearingFastSec(sxKph, l2Meters, vehicleLengthMeters),
            VehicleClearingSlowSec(syKph, l2Meters, vehicleLengthMeters));

    /// <summary>§5.5.2 (SR-5.5.2): T3 = L3 / Sz. Entering is evaluated at fast speed only.</summary>
    public double EnteringSec(double l3Meters, double szKph)
    {
        var sz = szKph / 3.6;
        if (sz <= 0) throw new ArgumentOutOfRangeException(nameof(szKph));
        return l3Meters / sz;
    }

    /// <summary>§5.5.3 (SR-5.5.3): pedestrian clearing Tw = W / Sp. Pedestrian entering ≡ 0 (§5.5.4).</summary>
    public double PedestrianClearingSec(double crossingWidthMeters, double pedestrianSpeedMps)
    {
        if (pedestrianSpeedMps <= 0) throw new ArgumentOutOfRangeException(nameof(pedestrianSpeedMps));
        return crossingWidthMeters / pedestrianSpeedMps;
    }

    /// <summary>§5.5.5 (SR-5.5.5): bicycles clear at constant speed — T2 = t + (L1 + L2 + l)/S.</summary>
    public double BicycleClearingSec(double speedKph, double l2Meters, double bicycleLengthMeters)
    {
        var s = speedKph / 3.6;
        if (s <= 0) throw new ArgumentOutOfRangeException(nameof(speedKph));
        var l1 = s * s / (2.0 * _decelMps2);
        return _reactionSec + (l1 + l2Meters + bicycleLengthMeters) / s;
    }

    /// <summary>§5.5.7 case I (SR-5.5.7): LRT clearing at constant speed (a = 1.2 m/s² for L1).</summary>
    public double LrtClearingMovingSec(double sxKph, double syKph, double l2Meters, double lrtLengthMeters,
        double lrtDecelMps2 = 1.2)
    {
        var sx = sxKph / 3.6;
        var sy = syKph / 3.6;
        if (sx <= 0 || sy <= 0) throw new ArgumentOutOfRangeException();
        var t2x = (sx * sx / (2.0 * lrtDecelMps2) + l2Meters + lrtLengthMeters) / sx + _reactionSec;
        var t2y = (sy * sy / (2.0 * lrtDecelMps2) + l2Meters + lrtLengthMeters) / sy + _reactionSec;
        return Math.Max(t2x, t2y);
    }

    /// <summary>§5.5.7 case II (SR-5.5.7): LRT clearing from a stopped state, start acceleration a1 = 1.2 m/s².</summary>
    public double LrtClearingFromStopSec(double sxKph, double l2Meters, double lrtLengthMeters,
        double startAccelMps2 = 1.2)
    {
        var sx = sxKph / 3.6;
        if (sx <= 0) throw new ArgumentOutOfRangeException(nameof(sxKph));
        var d = l2Meters + lrtLengthMeters;
        var accelRange = sx * sx / (2.0 * startAccelMps2);
        return d <= accelRange
            ? Math.Sqrt(2.0 * d / startAccelMps2)
            : sx / startAccelMps2 + (d - accelRange) / sx;
    }

    // ---- §5.6 final intergreen (SR-5.6) ----

    /// <summary>§5.6.1: vehicle/bike/LRT clears vs vehicle/bike/LRT enters — T = max(3, ceil(T2−T3)).</summary>
    public int FinalVehicleVsVehicle(double t2Sec, double t3Sec)
        => FinalIgPolicy.Guidelines2025.Resolve(t2Sec - t3Sec)!.Value;

    /// <summary>§5.6.2: vehicle clears vs pedestrian enters — T = max(3, ceil(T2)). T3 is NOT subtracted.</summary>
    public int FinalVehicleClearsPedestrianEnters(double t2Sec)
        => FinalIgPolicy.Guidelines2025.Resolve(t2Sec)!.Value;

    /// <summary>§5.6.3: pedestrian clears vs vehicle enters — T = max(3, ceil(Tw−T3)).</summary>
    public int FinalPedestrianClearsVehicleEnters(double twSec, double t3Sec)
        => FinalIgPolicy.Guidelines2025.Resolve(twSec - t3Sec)!.Value;
}
