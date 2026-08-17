using System;
using System.Collections.Generic;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Core.Legacy;

/// <summary>
/// Production Legacy numerics (Final Hotfix §1–§2, §7).
///
/// Differences from <see cref="LegacyWorkbookCompatibilityCalculator"/>:
///  - numerically stable, continuous formulation: a = max(0, 1.5 − 1.5·kph/80) and
///    t = reaction + 2L / (sqrt(v² + 2·L·a) + v), which has the exact constant-speed
///    limit L/v at a = 0 (no singularity at 80 km/h);
///  - invalid inputs are blocking findings, never the workbook's -1 sentinel;
///  - the movement algorithm is chosen by <see cref="MovementMode"/>, NEVER by
///    comparing a speed against the pedestrian speed (Final Hotfix §4).
///
/// This continuous extension is a production correction of a singular historical
/// implementation — not an official guideline formula (ENGINEERING_DECISIONS.md ED-007).
/// </summary>
public sealed class LegacyProductionCalculator
{
    public const string CodeInvalidSpeed = "IG-NUM-001";
    public const string CodeInvalidInput = "IG-NUM-002";
    public const string CodeNumericalSanity = "IG-NUM-003";

    private readonly double _reactionSec;
    private readonly double _decelMps2;

    public LegacyProductionCalculator(double reactionTimeSec = 1.0, double decelerationMps2 = 3.5)
    {
        if (reactionTimeSec <= 0) throw new ArgumentOutOfRangeException(nameof(reactionTimeSec));
        if (decelerationMps2 <= 0) throw new ArgumentOutOfRangeException(nameof(decelerationMps2));
        _reactionSec = reactionTimeSec;
        _decelMps2 = decelerationMps2;
    }

    /// <summary>Structured outcome — a failed calculation never throws away the whole run (Final Hotfix §8).</summary>
    public readonly record struct Outcome(double? Value, ValidationFinding? Finding)
    {
        public static Outcome Ok(double v) => new(v, null);
        public static Outcome Fail(ValidationFinding f) => new(null, f);
    }

    /// <summary>Vehicle-mode clearing time (stable form). Speeds/lengths validated, not sentinel-mapped.</summary>
    public Outcome VehicleClearingTimeSec(double distanceMeters, double speedMps, double vehicleLengthMeters)
    {
        if (speedMps <= 0 || double.IsNaN(speedMps) || double.IsInfinity(speedMps))
            return Outcome.Fail(new ValidationFinding(CodeInvalidSpeed, Severity.Error, null,
                $"Clearing speed {speedMps} m/s is not a valid positive speed.",
                RecommendedAction: "Resolve movement parameters before calculation.",
                SourceReference: "Final Hotfix §7"));
        if (distanceMeters < 0 || vehicleLengthMeters < 0
            || double.IsNaN(distanceMeters) || double.IsNaN(vehicleLengthMeters))
            return Outcome.Fail(new ValidationFinding(CodeInvalidInput, Severity.Error, null,
                $"Invalid clearing input: distance={distanceMeters} m, vehicleLength={vehicleLengthMeters} m.",
                SourceReference: "Final Hotfix §7"));

        var v = speedMps;
        var kph = v * 3.6;
        var a = Math.Max(0.0, 1.5 - 1.5 * kph / 80.0);
        var l = v * v / (2.0 * _decelMps2) + distanceMeters + vehicleLengthMeters;
        var discriminant = v * v + 2.0 * l * a;
        if (discriminant < 0)
            return Outcome.Fail(new ValidationFinding(CodeNumericalSanity, Severity.Error, null,
                $"Square-root domain violation (discriminant {discriminant}).",
                SourceReference: "Final Hotfix §7"));

        var t = _reactionSec + 2.0 * l / (Math.Sqrt(discriminant) + v);
        return Sane(t);
    }

    /// <summary>Pedestrian-mode clearing time: distance / walking speed. Selected by Mode, never by speed value.</summary>
    public Outcome PedestrianClearingTimeSec(double distanceMeters, double pedestrianSpeedMps)
    {
        if (pedestrianSpeedMps <= 0 || double.IsNaN(pedestrianSpeedMps))
            return Outcome.Fail(new ValidationFinding(CodeInvalidSpeed, Severity.Error, null,
                $"Pedestrian speed {pedestrianSpeedMps} m/s is not a valid positive speed.",
                SourceReference: "Final Hotfix §7"));
        if (distanceMeters < 0 || double.IsNaN(distanceMeters))
            return Outcome.Fail(new ValidationFinding(CodeInvalidInput, Severity.Error, null,
                $"Invalid pedestrian clearing distance {distanceMeters} m.",
                SourceReference: "Final Hotfix §7"));
        return Sane(distanceMeters / pedestrianSpeedMps);
    }

    /// <summary>Entering time. Speed 0 is permitted here only as the explicit "no entering component" case.</summary>
    public Outcome EnteringTimeSec(double distanceMeters, double enteringSpeedMps)
    {
        if (enteringSpeedMps < 0 || double.IsNaN(enteringSpeedMps))
            return Outcome.Fail(new ValidationFinding(CodeInvalidSpeed, Severity.Error, null,
                $"Entering speed {enteringSpeedMps} m/s is invalid.",
                SourceReference: "Final Hotfix §7"));
        if (enteringSpeedMps == 0)
            return Outcome.Ok(0.0);
        if (distanceMeters < 0 || double.IsNaN(distanceMeters))
            return Outcome.Fail(new ValidationFinding(CodeInvalidInput, Severity.Error, null,
                $"Invalid entering distance {distanceMeters} m.",
                SourceReference: "Final Hotfix §7"));
        return Sane(distanceMeters / enteringSpeedMps);
    }

    private static Outcome Sane(double t)
    {
        if (double.IsNaN(t) || double.IsInfinity(t) || t < 0)
            return Outcome.Fail(new ValidationFinding(CodeNumericalSanity, Severity.Error, null,
                $"Numerical sanity violation: computed time {t}.",
                TechnicalDetails: "NaN/Infinity/negative time must never reach output (Final Hotfix §8/§11).",
                SourceReference: "Final Hotfix §8"));
        return Outcome.Ok(t);
    }
}
