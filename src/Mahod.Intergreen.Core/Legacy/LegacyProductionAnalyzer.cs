using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Core.Legacy;

/// <summary>Row status per Final Hotfix §18 semantics.</summary>
public enum RowStatus { Valid, ReviewRequired, Error }

/// <summary>Production result for one conflict row: findings first, numbers only when safe.</summary>
public sealed record LegacyProductionRowResult(
    int ConflictNo,
    RowStatus Status,
    IReadOnlyList<ValidationFinding> Findings,
    LegacyRowResult? Calculation,
    int? FinalIg)
{
    public bool IsBlocked => Status == RowStatus.Error;
}

/// <summary>
/// Production analysis over the Legacy method (v3 §25/§26, Addendum §C, Final Hotfix §1–§8).
///
/// Contract:
///  - the calculation model is selected by <see cref="MovementMode"/>, never by speed equality;
///  - a missing required clearing measurement is an ERROR, never zero;
///  - vehicle numerics use the stable continuous formulation (no 80 km/h singularity);
///  - invalid inputs / NaN / Infinity are blocking findings; independent rows keep processing.
/// </summary>
public sealed class LegacyProductionAnalyzer
{
    public const string CodeMissingMeasurement = "IG-VAL-001";           // no CD and no ED at all
    public const string CodeMissingClearingMeasurement = "IG-VAL-002";   // ED defined, CD absent
    public const string CodeSubOneSecondResult = "IG-VAL-003";           // raw IG < 1 s
    public const string CodeUnknownMovementParameters = "IG-VAL-004";    // vehicle movement absent from the parameter table

    private readonly LegacyConstants _c;
    private readonly LegacyTemplateVariant _variant;
    private readonly IReadOnlyDictionary<string, LegacyMovementParameters> _params;
    private readonly FinalIgPolicy _policy;
    private readonly LegacyProductionCalculator _calc;

    public LegacyProductionAnalyzer(
        LegacyConstants constants,
        LegacyTemplateVariant variant,
        IReadOnlyDictionary<string, LegacyMovementParameters> movementParameters,
        FinalIgPolicy? policy = null)
    {
        _c = constants;
        _variant = variant;
        _params = movementParameters;
        _policy = policy ?? FinalIgPolicy.MahodLegacy;
        _calc = new LegacyProductionCalculator(constants.ReactionTimeSec, constants.DecelerationMps2);
    }

    /// <summary>Result of a geometry-driven production analysis over N candidate points (never truncated).</summary>
    public sealed record GeometryConflictResult(
        string ConflictId,
        RowStatus Status,
        IReadOnlyList<ValidationFinding> Findings,
        IReadOnlyList<GeometryPointResult> Points,
        int? DefiningPointIndex,
        double? RawMaxIntergreenSec,
        int? FinalIg);

    public sealed record GeometryPointResult(
        double ClearingDistanceMeters,
        double EnteringDistanceMeters,
        double ClearFastSec,
        double ClearSlowSec,
        double EnterSec,
        double IntergreenSec);

    /// <summary>
    /// Production analysis over geometry-derived candidate points. Unlike the workbook shape
    /// this path has no 4-point cap — every retained point is evaluated and the governing
    /// point is selected by computed time (v3 §15, Final Hotfix §17).
    /// </summary>
    public GeometryConflictResult AnalyzeGeometryConflict(
        string conflictId,
        string clearingMovement,
        string enteringMovement,
        MovementMode clearingMode,
        MovementMode enteringMode,
        IReadOnlyList<(double CdMeters, double EdMeters)> points,
        double? pedestrianSpeedMpsOverride = null)
    {
        var findings = new List<ValidationFinding>();
        var conflictRef = $"{conflictId} ({clearingMovement} → {enteringMovement})";

        if (points.Count == 0)
        {
            findings.Add(new ValidationFinding(CodeMissingMeasurement, Severity.Error, conflictRef,
                "No candidate conflict points to analyze.", SourceReference: "v3 §26"));
            return new GeometryConflictResult(conflictId, RowStatus.Error, findings,
                Array.Empty<GeometryPointResult>(), null, null, null);
        }

        var pedSpeed = pedestrianSpeedMpsOverride ?? _c.PedestrianSpeedMps;
        double clearFastMps = 0, clearSlowMps = 0, vehLen = 0;
        if (clearingMode != MovementMode.Pedestrian)
        {
            if (!_params.TryGetValue(clearingMovement, out var pc)
                || pc.FastClearingKph is not double fk || pc.SlowClearingKph is not double sk)
            {
                findings.Add(new ValidationFinding(CodeUnknownMovementParameters, Severity.Error, conflictRef,
                    $"Clearing movement '{clearingMovement}' (mode {clearingMode}) has no speed parameters.",
                    SourceReference: "Final Hotfix §4"));
                return new GeometryConflictResult(conflictId, RowStatus.Error, findings,
                    Array.Empty<GeometryPointResult>(), null, null, null);
            }
            clearFastMps = fk / 3.6;
            clearSlowMps = sk / 3.6;
            var len = _variant == LegacyTemplateVariant.V2GlobalVehicleLength
                ? _c.GlobalVehicleLengthMeters
                : pc.VehicleLengthMeters;
            if (len is not double l)
            {
                findings.Add(new ValidationFinding(CodeUnknownMovementParameters, Severity.Error, conflictRef,
                    $"Clearing movement '{clearingMovement}' has no vehicle length.", SourceReference: "Addendum §C.2"));
                return new GeometryConflictResult(conflictId, RowStatus.Error, findings,
                    Array.Empty<GeometryPointResult>(), null, null, null);
            }
            vehLen = l;
        }

        double enterMps = 0;
        if (enteringMode != MovementMode.Pedestrian)
        {
            if (_params.TryGetValue(enteringMovement, out var pe) && pe.FastClearingKph is double ek)
                enterMps = ek / 3.6;
            else
            {
                findings.Add(new ValidationFinding(CodeUnknownMovementParameters, Severity.Error, conflictRef,
                    $"Entering movement '{enteringMovement}' (mode {enteringMode}) has no speed parameters.",
                    SourceReference: "Final Hotfix §4"));
                return new GeometryConflictResult(conflictId, RowStatus.Error, findings,
                    Array.Empty<GeometryPointResult>(), null, null, null);
            }
        }

        var results = new List<GeometryPointResult>();
        foreach (var (cd, ed) in points)
        {
            LegacyProductionCalculator.Outcome fast, slow;
            if (clearingMode == MovementMode.Pedestrian)
                fast = slow = _calc.PedestrianClearingTimeSec(cd, pedSpeed);
            else
            {
                fast = _calc.VehicleClearingTimeSec(cd, clearFastMps, vehLen);
                slow = _calc.VehicleClearingTimeSec(cd, clearSlowMps, vehLen);
            }
            // pedestrian entering distance is 0 by definition (§5.4)
            var enter = _calc.EnteringTimeSec(enteringMode == MovementMode.Pedestrian ? 0.0 : ed, enterMps);

            var bad = new[] { fast.Finding, slow.Finding, enter.Finding }.FirstOrDefault(f => f is not null);
            if (bad is not null)
            {
                findings.Add(bad with { ConflictRef = conflictRef });
                return new GeometryConflictResult(conflictId, RowStatus.Error, findings,
                    Array.Empty<GeometryPointResult>(), null, null, null);
            }

            var ig = Math.Max(fast.Value!.Value, slow.Value!.Value) - enter.Value!.Value;
            results.Add(new GeometryPointResult(cd, ed, fast.Value.Value, slow.Value.Value, enter.Value.Value, ig));
        }

        var definingIdx = 0;
        for (var i = 1; i < results.Count; i++)
            if (results[i].IntergreenSec > results[definingIdx].IntergreenSec)
                definingIdx = i;
        var raw = results[definingIdx].IntergreenSec;

        if (raw < 1.0)
        {
            findings.Add(new ValidationFinding(CodeSubOneSecondResult, Severity.ReviewRequired, conflictRef,
                $"Raw intergreen {raw:F3} s is below 1 s — workbook rounding is undefined here.",
                SourceReference: "Addendum §C.1"));
            return new GeometryConflictResult(conflictId, RowStatus.ReviewRequired, findings,
                results, definingIdx, raw, (int)Math.Ceiling(raw));
        }

        return new GeometryConflictResult(conflictId, RowStatus.Valid, findings,
            results, definingIdx, raw, _policy.Resolve(raw));
    }

    public LegacyProductionRowResult AnalyzeRow(
        ConflictRowInput row,
        MovementMode clearingMode,
        MovementMode enteringMode,
        double? pedestrianSpeedMpsOverride = null)
    {
        var findings = new List<ValidationFinding>();
        var conflictRef = $"conflict {row.ConflictNo} ({row.ClearingMovement} → {row.EnteringMovement})";

        // --- required measurement (Addendum §C.3/§D) ---
        var p1 = row.Point1;
        if (p1.ClearingDistanceMeters is null)
        {
            if (p1.EnteringDistanceMeters is null)
            {
                findings.Add(new ValidationFinding(
                    CodeMissingMeasurement, Severity.Error, conflictRef,
                    "No measured distances at all for this conflict.",
                    "Point 1 CD and ED cells are both blank. Historical workbooks treated blank as 0 and still emitted a final intergreen.",
                    "Measure the conflict or mark it not applicable.",
                    "v3 §26; Addendum §D"));
            }
            else
            {
                findings.Add(new ValidationFinding(
                    CodeMissingClearingMeasurement, Severity.Error, conflictRef,
                    "Entering distance is defined but the required clearing measurement is missing.",
                    $"Point 1 ED = {p1.EnteringDistanceMeters} (a defined value, e.g. pedestrian entering = 0 per §5.4) but CD is blank.",
                    "Measure the clearing distance. Do not allow blank CD to become zero.",
                    "v3 §26; Addendum §D"));
            }
            return new LegacyProductionRowResult(row.ConflictNo, RowStatus.Error, findings, null, null);
        }

        // --- parameter resolution by MODE (Final Hotfix §4) ---
        double clearFastMps, clearSlowMps;
        double vehLen = 0.0;
        var pedSpeed = pedestrianSpeedMpsOverride ?? _c.PedestrianSpeedMps;

        if (clearingMode == MovementMode.Pedestrian)
        {
            clearFastMps = clearSlowMps = pedSpeed; // used only inside the pedestrian branch below
        }
        else
        {
            if (!_params.TryGetValue(row.ClearingMovement, out var pc)
                || pc.FastClearingKph is not double fk || pc.SlowClearingKph is not double sk)
            {
                findings.Add(new ValidationFinding(CodeUnknownMovementParameters, Severity.Error, conflictRef,
                    $"Clearing movement '{row.ClearingMovement}' (mode {clearingMode}) has no speed parameters.",
                    RecommendedAction: "Add the movement to the parameter table. Production never falls back to pedestrian speed.",
                    SourceReference: "Final Hotfix §4"));
                return new LegacyProductionRowResult(row.ConflictNo, RowStatus.Error, findings, null, null);
            }
            clearFastMps = fk / 3.6;
            clearSlowMps = sk / 3.6;
            var len = _variant == LegacyTemplateVariant.V2GlobalVehicleLength
                ? _c.GlobalVehicleLengthMeters
                : (_params.TryGetValue(row.ClearingMovement, out var plen) ? plen.VehicleLengthMeters : null);
            if (len is not double l)
            {
                findings.Add(new ValidationFinding(CodeUnknownMovementParameters, Severity.Error, conflictRef,
                    $"Clearing movement '{row.ClearingMovement}' has no vehicle length.",
                    SourceReference: "Addendum §C.2"));
                return new LegacyProductionRowResult(row.ConflictNo, RowStatus.Error, findings, null, null);
            }
            vehLen = l;
        }

        double enterMps;
        if (enteringMode == MovementMode.Pedestrian)
        {
            enterMps = 0.0; // pedestrian entering time is 0 by definition
        }
        else if (_params.TryGetValue(row.EnteringMovement, out var pe) && pe.FastClearingKph is double ek)
        {
            enterMps = ek / 3.6;
        }
        else
        {
            findings.Add(new ValidationFinding(CodeUnknownMovementParameters, Severity.Error, conflictRef,
                $"Entering movement '{row.EnteringMovement}' (mode {enteringMode}) has no speed parameters.",
                SourceReference: "Final Hotfix §4"));
            return new LegacyProductionRowResult(row.ConflictNo, RowStatus.Error, findings, null, null);
        }

        // --- per-point calculation (structured outcomes, Final Hotfix §8) ---
        var points = row.Points;
        var results = new PointTimes?[4];
        var igs = new double?[4];
        for (var i = 0; i < 4; i++)
        {
            var pt = points[i];
            if (pt.ClearingDistanceMeters is not double cd)
                continue; // unpopulated point (P1 handled above)

            LegacyProductionCalculator.Outcome fast, slow;
            if (clearingMode == MovementMode.Pedestrian)
            {
                fast = slow = _calc.PedestrianClearingTimeSec(cd, pedSpeed);
            }
            else
            {
                fast = _calc.VehicleClearingTimeSec(cd, clearFastMps, vehLen);
                slow = _calc.VehicleClearingTimeSec(cd, clearSlowMps, vehLen);
            }
            var enter = _calc.EnteringTimeSec(pt.EnteringDistanceMeters ?? 0.0, enterMps);

            var bad = new[] { fast.Finding, slow.Finding, enter.Finding }.FirstOrDefault(f => f is not null);
            if (bad is not null)
            {
                findings.Add(bad with { ConflictRef = conflictRef });
                return new LegacyProductionRowResult(row.ConflictNo, RowStatus.Error, findings, null, null);
            }

            var ig = Math.Max(fast.Value!.Value, slow.Value!.Value) - enter.Value!.Value;
            results[i] = new PointTimes(fast.Value.Value, slow.Value.Value, enter.Value.Value, ig);
            igs[i] = ig;
        }

        double? rawMax = null;
        int? defining = null;
        for (var i = 0; i < 4; i++)
        {
            if (igs[i] is not double v) continue;
            if (rawMax is null || v > rawMax) { rawMax = v; defining = i; }
        }

        if (rawMax is not double raw)
        {
            findings.Add(new ValidationFinding(CodeMissingMeasurement, Severity.Error, conflictRef,
                "No computable point on this conflict.", SourceReference: "v3 §26"));
            return new LegacyProductionRowResult(row.ConflictNo, RowStatus.Error, findings, null, null);
        }

        var calcRow = new LegacyRowResult(
            clearingMode == MovementMode.Pedestrian ? pedSpeed : clearFastMps,
            clearingMode == MovementMode.Pedestrian ? pedSpeed : clearSlowMps,
            enterMps,
            clearingMode == MovementMode.Pedestrian ? null : vehLen,
            results, raw, defining, _policy.Resolve(raw),
            ManualRoundingCandidate: false);

        if (raw < 1.0)
        {
            // Addendum §C.1: the workbook yields blank here and the pair silently vanishes.
            // Production always emits the conservative ceiling plus a REVIEW REQUIRED finding.
            var emitted = (int)Math.Ceiling(raw);
            findings.Add(new ValidationFinding(
                CodeSubOneSecondResult, Severity.ReviewRequired, conflictRef,
                $"Raw intergreen {raw:F3} s is below 1 s — workbook rounding is undefined here.",
                "The legacy rounding rule (MOD by INT) is undefined below 1 s; the source workbook leaves the cell blank and the matrix silently omits the pair.",
                "Review the conflict. The emitted value is the conservative ceiling of the raw result.",
                "Addendum §C.1"));
            return new LegacyProductionRowResult(row.ConflictNo, RowStatus.ReviewRequired, findings, calcRow, emitted);
        }

        var status = findings.Any(f => f.Severity == Severity.ReviewRequired)
            ? RowStatus.ReviewRequired : RowStatus.Valid;
        return new LegacyProductionRowResult(row.ConflictNo, status, findings, calcRow, calcRow.FinalIg);
    }
}
