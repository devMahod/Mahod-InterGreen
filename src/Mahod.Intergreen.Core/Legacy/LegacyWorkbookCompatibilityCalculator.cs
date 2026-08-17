using System;
using System.Collections.Generic;

namespace Mahod.Intergreen.Core.Legacy;

/// <summary>
/// Exact reimplementation of David's workbook Legacy calculation (Addendum §C — transcribed,
/// not invented). This class intentionally reproduces HISTORICAL workbook behaviour, including
/// blank-distance == 0.0, for regression against the 148 golden rows.
///
/// It must NEVER be used as production analysis on raw input without prior validation —
/// that is <see cref="LegacyProductionAnalyzer"/>'s job (v3 §25, Addendum §C.3).
/// </summary>
public sealed class LegacyWorkbookCompatibilityCalculator
{
    private readonly LegacyConstants _c;
    private readonly LegacyTemplateVariant _variant;
    private readonly IReadOnlyDictionary<string, LegacyMovementParameters> _params;
    private readonly FinalIgPolicy _policy;

    public LegacyWorkbookCompatibilityCalculator(
        LegacyConstants constants,
        LegacyTemplateVariant variant,
        IReadOnlyDictionary<string, LegacyMovementParameters> movementParameters,
        FinalIgPolicy? policy = null)
    {
        _c = constants;
        _variant = variant;
        _params = movementParameters;
        _policy = policy ?? FinalIgPolicy.MahodLegacy;
    }

    /// <summary>
    /// Workbook clearing-time formula (columns P/Q/T/U/X/Y/AB/AC).
    /// Returns -1.0 on arithmetic error — that is the workbook's IFERROR fallback.
    /// </summary>
    public double ClearingTimeSeconds(double? distanceMeters, double speedMps, double vehicleLengthMeters)
    {
        var d = distanceMeters ?? 0.0; // COMPATIBILITY behaviour only (Addendum §C.3)
        try
        {
            if (speedMps == _c.PedestrianSpeedMps)
                return d / _c.PedestrianSpeedMps; // clearing movement is a pedestrian

            var kph = speedMps * 3.6;
            var coeff = (kph > 80.0 || speedMps == _c.PedestrianSpeedMps || speedMps == 0.0)
                ? -1.0
                : 1.5 - 1.5 * kph / 80.0; // acceleration [m/s²]; -1 == "no acceleration model"

            if (coeff == -1.0)
            {
                if (speedMps == 0.0)
                    return -1.0; // division by zero → IFERROR → -1
                return _c.ReactionTimeSec + speedMps / (2.0 * _c.DecelerationMps2)
                       + (d + vehicleLengthMeters) / speedMps;
            }

            var brake = speedMps * speedMps / (2.0 * _c.DecelerationMps2);
            var discriminant = speedMps * speedMps + 2.0 * (brake + d + vehicleLengthMeters) * coeff;
            if (discriminant < 0)
                return -1.0;
            return (-speedMps + Math.Sqrt(discriminant)) / coeff + _c.ReactionTimeSec;
        }
        catch (Exception)
        {
            return -1.0;
        }
    }

    /// <summary>Workbook entering-time formula (columns R/V/Z/AD).</summary>
    public double EnteringTimeSeconds(double? enteringDistanceMeters, double enteringSpeedMps)
        => enteringSpeedMps == 0.0 ? 0.0 : (enteringDistanceMeters ?? 0.0) / enteringSpeedMps;

    /// <summary>
    /// Speed resolution per workbook columns L/M/N:
    /// VLOOKUP into the Parameters sheet; non-numeric / missing → pedestrian-speed fallback
    /// for clearing, 0 for entering.
    /// </summary>
    public (double FastMps, double SlowMps, double EnterMps) ResolveSpeeds(string clearing, string entering)
    {
        double fast = _c.PedestrianSpeedMps, slow = _c.PedestrianSpeedMps, enter = 0.0;
        if (_params.TryGetValue(clearing, out var pc))
        {
            if (pc.FastClearingKph is double f) fast = f / 3.6;
            if (pc.SlowClearingKph is double s) slow = s / 3.6;
        }
        if (_params.TryGetValue(entering, out var pe) && pe.FastClearingKph is double ef)
            enter = ef / 3.6;
        return (fast, slow, enter);
    }

    /// <summary>Vehicle-length resolution per template variant (Addendum §C.2).</summary>
    public double? ResolveVehicleLengthMeters(string clearingMovement)
    {
        if (_variant == LegacyTemplateVariant.V2GlobalVehicleLength)
            return _c.GlobalVehicleLengthMeters;
        return _params.TryGetValue(clearingMovement, out var p) ? p.VehicleLengthMeters : null;
    }

    public LegacyRowResult ComputeRow(ConflictRowInput row)
    {
        var (vFast, vSlow, vEnter) = ResolveSpeeds(row.ClearingMovement, row.EnteringMovement);
        var vehLen = ResolveVehicleLengthMeters(row.ClearingMovement);
        var lenForCalc = vehLen ?? 0.0; // only ever used on vehicle branches, where it is present

        var points = row.Points;
        var results = new PointTimes?[4];
        var igs = new double?[4];

        for (var i = 0; i < 4; i++)
        {
            var pt = points[i];
            // Points 2–4: workbook computes only when CD cell is populated (ISBLANK guard).
            // Point 1 has no ISBLANK guard — always computed, blank == 0.
            if (i > 0 && pt.ClearingDistanceMeters is null)
                continue;

            var tFast = ClearingTimeSeconds(pt.ClearingDistanceMeters, vFast, lenForCalc);
            var tSlow = ClearingTimeSeconds(pt.ClearingDistanceMeters, vSlow, lenForCalc);
            var tEnter = EnteringTimeSeconds(pt.EnteringDistanceMeters, vEnter);

            if (vFast == 0.0 && vSlow == 0.0)
                continue; // workbook S column: IF(AND(L=0,M=0),"",...)

            var ig = Math.Max(tFast, tSlow) - tEnter;
            results[i] = new PointTimes(tFast, tSlow, tEnter, ig);
            igs[i] = ig;
        }

        double? rawMax = null;
        int? defining = null;
        for (var i = 0; i < 4; i++)
        {
            if (igs[i] is not double v) continue;
            if (rawMax is null || v > rawMax)
            {
                rawMax = v;
                defining = i; // Excel's IF-cascade prefers the earliest point on ties
            }
        }

        int? finalIg = rawMax is double rm ? _policy.Resolve(rm) : null;
        var manualFlag = finalIg is int f && rawMax is double r && f < r;

        return new LegacyRowResult(vFast, vSlow, vEnter, vehLen, results, rawMax, defining, finalIg, manualFlag);
    }
}
