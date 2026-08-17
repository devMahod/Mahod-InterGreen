using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;

namespace Mahod.Intergreen.Core.Israel2025;

/// <summary>Final-IG formula selector per §5.6 (SR-5.6) / conformance list ו.1–ו.15.</summary>
public enum FinalIgFormula
{
    /// <summary>§5.6.1: T = max(3, ceil(T2 − T3)).</summary>
    ClearMinusEnter,
    /// <summary>§5.6.2 (pedestrian enters): T = max(3, ceil(T2)) — T3 is never subtracted.</summary>
    ClearOnly,
    /// <summary>§5.6.3 (pedestrian clears): T = max(3, ceil(Tw − T3)).</summary>
    PedestrianClearMinusEnter,
}

public sealed record ModePairRule(
    MovementMode ClearingClass,
    MovementMode EnteringClass,
    FinalIgFormula Formula,
    bool IncludeInMatrix,
    string SourceRef);

/// <summary>
/// One explicit typed dispatcher for the 15 official clearing→entering combinations
/// (נספח 1 תוספת א' ו.1–ו.15; Directive §35). Bus/Sherut use the vehicle engineering
/// class while keeping their own mode identity. Pedestrian→pedestrian has no intergreen.
/// Two-lamp vehicle phases share the vehicle formulas (תוספת ה'); their exclusion from the
/// normal matrix is phase metadata handled at aggregation, not a separate calculator.
/// </summary>
public static class ModePairPolicy
{
    public static MovementMode EngineeringClass(MovementMode m) => m switch
    {
        MovementMode.Bus or MovementMode.Sherut => MovementMode.Vehicle,
        _ => m,
    };

    public static ModePairRule? Resolve(MovementMode clearing, MovementMode entering)
    {
        var c = EngineeringClass(clearing);
        var e = EngineeringClass(entering);
        if (c == MovementMode.Pedestrian && e == MovementMode.Pedestrian)
            return null; // no pedestrian-vs-pedestrian intergreen

        var formula = e == MovementMode.Pedestrian
            ? FinalIgFormula.ClearOnly
            : c == MovementMode.Pedestrian
                ? FinalIgFormula.PedestrianClearMinusEnter
                : FinalIgFormula.ClearMinusEnter;

        var srcIndex = (c, e) switch
        {
            (MovementMode.Vehicle, MovementMode.Vehicle) => "ו.1",
            (MovementMode.Vehicle, MovementMode.Pedestrian) => "ו.2",
            (MovementMode.Vehicle, MovementMode.Bicycle) => "ו.3",
            (MovementMode.Vehicle, MovementMode.Lrt) => "ו.4",
            (MovementMode.Pedestrian, MovementMode.Vehicle) => "ו.5",
            (MovementMode.Pedestrian, MovementMode.Bicycle) => "ו.6",
            (MovementMode.Pedestrian, MovementMode.Lrt) => "ו.7",
            (MovementMode.Bicycle, MovementMode.Vehicle) => "ו.8",
            (MovementMode.Bicycle, MovementMode.Pedestrian) => "ו.9",
            (MovementMode.Bicycle, MovementMode.Bicycle) => "ו.10",
            (MovementMode.Bicycle, MovementMode.Lrt) => "ו.11",
            (MovementMode.Lrt, MovementMode.Vehicle) => "ו.12",
            (MovementMode.Lrt, MovementMode.Pedestrian) => "ו.13",
            (MovementMode.Lrt, MovementMode.Bicycle) => "ו.14",
            (MovementMode.Lrt, MovementMode.Lrt) => "ו.15",
            _ => "ו",
        };
        return new ModePairRule(c, e, formula, IncludeInMatrix: true, $"נספח 1 תוספת א' {srcIndex}; SR-5.6");
    }

    /// <summary>All 15 official combinations, for coverage tests.</summary>
    public static IEnumerable<(MovementMode Clearing, MovementMode Entering)> OfficialPairs()
    {
        var modes = new[] { MovementMode.Vehicle, MovementMode.Pedestrian, MovementMode.Bicycle, MovementMode.Lrt };
        foreach (var c in modes)
            foreach (var e in modes)
                if (!(c == MovementMode.Pedestrian && e == MovementMode.Pedestrian))
                    yield return (c, e);
    }
}

/// <summary>Resolved 2025 parameters for one movement (all with source provenance upstream).</summary>
public sealed record Israel2025MovementParameters(
    double? FastKph,               // SX (vehicle/bike/LRT)
    double? SlowKph,               // SY
    double? EnteringKph,           // SZ
    double? VehicleLengthMeters,   // l (12/19/2/15 per Table 5.5)
    double? PedestrianSpeedMps,    // Sp (crossing context, Table 5.2)
    double? PedestrianWidthMeters, // W
    bool LrtStartsFromStop = false);

/// <summary>
/// 2025 production conflict analysis (Directive §36): lane-centreline points +
/// ParameterResolver-resolved parameters + ModePairPolicy + current rounding/minimum.
/// Completely separate from the Legacy engines — no call path reaches the Legacy
/// clearing-time calculator (§33/§39).
/// </summary>
public sealed class Israel2025ProductionAnalyzer
{
    public const string CodeMissingParameters = "IG-25-001";
    public const string CodeNearSideRule = "IG-25-002"; // informational: L3 < 1.5 → 0 applied

    private readonly Israel2025CalculationService _svc;

    public Israel2025ProductionAnalyzer(double reactionTimeSec = 1.0, double decelerationMps2 = 3.5)
        => _svc = new Israel2025CalculationService(reactionTimeSec, decelerationMps2);

    public LegacyProductionAnalyzer.GeometryConflictResult Analyze(
        string conflictId,
        MovementMode clearingMode,
        MovementMode enteringMode,
        Israel2025MovementParameters clearing,
        Israel2025MovementParameters entering,
        IReadOnlyList<(double CdMeters, double EdMeters)> points)
    {
        var findings = new List<ValidationFinding>();
        var rule = ModePairPolicy.Resolve(clearingMode, enteringMode);
        if (rule is null || points.Count == 0)
        {
            return new LegacyProductionAnalyzer.GeometryConflictResult(conflictId, RowStatus.Error,
                new[]
                {
                    new ValidationFinding(CodeMissingParameters, Severity.Error, conflictId,
                        rule is null ? "no intergreen is defined for this mode pair" : "no candidate points"),
                },
                Array.Empty<LegacyProductionAnalyzer.GeometryPointResult>(), null, null, null);
        }

        var results = new List<LegacyProductionAnalyzer.GeometryPointResult>();
        foreach (var (cd, ed) in points)
        {
            double t2;
            switch (rule.ClearingClass)
            {
                case MovementMode.Vehicle:
                    if (clearing.FastKph is not double fx || clearing.SlowKph is not double sy
                        || clearing.VehicleLengthMeters is not double len)
                        return Blocked(conflictId, findings, "vehicle clearing parameters missing");
                    t2 = _svc.VehicleClearingSec(fx, sy, cd, len);
                    break;
                case MovementMode.Bicycle:
                    if (clearing.FastKph is not double bfx || clearing.SlowKph is not double bsy
                        || clearing.VehicleLengthMeters is not double blen)
                        return Blocked(conflictId, findings, "bicycle clearing parameters missing");
                    t2 = Math.Max(_svc.BicycleClearingSec(bfx, cd, blen), _svc.BicycleClearingSec(bsy, cd, blen));
                    break;
                case MovementMode.Lrt:
                    if (clearing.FastKph is not double lfx || clearing.SlowKph is not double lsy
                        || clearing.VehicleLengthMeters is not double llen)
                        return Blocked(conflictId, findings, "LRT clearing parameters missing");
                    t2 = _svc.LrtClearingSec(lfx, lsy, cd, llen, clearing.LrtStartsFromStop);
                    break;
                default: // pedestrian clearing: Tw = W / Sp
                    if (clearing.PedestrianWidthMeters is not double w || clearing.PedestrianSpeedMps is not double sp)
                        return Blocked(conflictId, findings, "pedestrian W / speed missing");
                    t2 = _svc.PedestrianClearingSec(w, sp);
                    break;
            }

            double t3;
            if (rule.EnteringClass == MovementMode.Pedestrian)
            {
                t3 = 0.0; // §5.5.4
            }
            else
            {
                if (entering.EnteringKph is not double sz)
                    return Blocked(conflictId, findings, "entering speed missing");
                var l3 = ed;
                // §5.4.4 near-side rule (2025 ONLY — never applied in Legacy, Directive §6A):
                // pedestrian clears vs vehicle enters with L3 < 1.5 m → L3 = 0.
                if (rule.ClearingClass == MovementMode.Pedestrian && l3 < 1.5)
                {
                    l3 = 0.0;
                    findings.Add(new ValidationFinding(CodeNearSideRule, Severity.Warning, conflictId,
                        $"2025 near-side rule applied: entering distance {ed:F2} m < 1.5 m → 0 (SR-5.4.4).",
                        SourceReference: "SR-5.4.4"));
                }
                t3 = _svc.EnteringSec(l3, sz);
            }

            var raw = rule.Formula switch
            {
                FinalIgFormula.ClearOnly => t2,
                _ => t2 - t3,
            };
            results.Add(new LegacyProductionAnalyzer.GeometryPointResult(cd, ed, t2, t2, t3, raw));
        }

        var definingIdx = 0;
        for (var i = 1; i < results.Count; i++)
            if (results[i].IntergreenSec > results[definingIdx].IntergreenSec)
                definingIdx = i;
        var rawMax = results[definingIdx].IntergreenSec;
        var final = FinalIgPolicy.Guidelines2025.Resolve(rawMax)!.Value;

        return new LegacyProductionAnalyzer.GeometryConflictResult(conflictId, RowStatus.Valid,
            findings, results, definingIdx, rawMax, final);
    }

    private static LegacyProductionAnalyzer.GeometryConflictResult Blocked(
        string conflictId, List<ValidationFinding> findings, string reason)
    {
        findings.Add(new ValidationFinding(CodeMissingParameters, Severity.Error, conflictId,
            $"MISSING_ENGINEERING_CLASSIFICATION: {reason}.", SourceReference: "Directive §16/§36"));
        return new LegacyProductionAnalyzer.GeometryConflictResult(conflictId, RowStatus.Error,
            findings, Array.Empty<LegacyProductionAnalyzer.GeometryPointResult>(), null, null, null);
    }
}
