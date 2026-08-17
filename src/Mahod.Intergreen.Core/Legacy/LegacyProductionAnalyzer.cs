using System;
using System.Collections.Generic;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Core.Legacy;

/// <summary>Production result for one conflict row: findings first, numbers only when safe.</summary>
public sealed record LegacyProductionRowResult(
    int ConflictNo,
    IReadOnlyList<ValidationFinding> Findings,
    LegacyRowResult? Calculation,
    int? FinalIg)
{
    public bool IsBlocked => Calculation is null;
}

/// <summary>
/// Production analysis over the Legacy method (v3 §25/§26, Addendum §C.1/§C.3).
///
/// Unlike <see cref="LegacyWorkbookCompatibilityCalculator"/>, this analyzer:
///  - treats a missing required clearing measurement as an ERROR, never as zero;
///  - distinguishes MISSING_CLEARING_MEASUREMENT (defined ED present, e.g. pedestrian ED=0)
///    from MISSING_MEASUREMENT (nothing measured at all);
///  - always surfaces sub-1-second results with a finding instead of a silent blank.
/// </summary>
public sealed class LegacyProductionAnalyzer
{
    public const string CodeMissingMeasurement = "IG-VAL-001";           // no CD and no ED at all
    public const string CodeMissingClearingMeasurement = "IG-VAL-002";   // ED defined, CD absent
    public const string CodeSubOneSecondResult = "IG-VAL-003";           // raw IG < 1 s
    public const string CodeUnknownMovementParameters = "IG-VAL-004";    // clearing movement resolves to ped fallback unexpectedly

    private readonly LegacyWorkbookCompatibilityCalculator _calculator;

    public LegacyProductionAnalyzer(LegacyWorkbookCompatibilityCalculator calculator)
        => _calculator = calculator;

    public LegacyProductionRowResult AnalyzeRow(ConflictRowInput row)
    {
        var findings = new List<ValidationFinding>();
        var conflictRef = $"conflict {row.ConflictNo} ({row.ClearingMovement} → {row.EnteringMovement})";

        var p1 = row.Point1;
        if (p1.ClearingDistanceMeters is null)
        {
            if (p1.EnteringDistanceMeters is null)
            {
                findings.Add(new ValidationFinding(
                    CodeMissingMeasurement, Severity.Error, conflictRef,
                    "No measured distances at all for this conflict.",
                    "Point 1 CD and ED cells are both blank. Historical workbooks treated blank as 0 and still emitted a final intergreen.",
                    "Measure the conflict or mark it not applicable. A result must not be issued from blank cells.",
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
            return new LegacyProductionRowResult(row.ConflictNo, findings, null, null);
        }

        var calc = _calculator.ComputeRow(row);

        if (calc.RawMaxIntergreenSec is double raw && raw < 1.0)
        {
            // Addendum §C.1: the workbook silently yields blank here and nothing reaches the
            // matrix. Production must always emit a value and raise a finding. We emit the
            // conservative ceiling (never smaller than the raw value) and flag for review.
            var emitted = (int)Math.Ceiling(raw);
            findings.Add(new ValidationFinding(
                CodeSubOneSecondResult, Severity.ReviewRequired, conflictRef,
                $"Raw intergreen {raw:F3} s is below 1 s — workbook rounding is undefined here.",
                "The legacy rounding rule (MOD by INT) is undefined below 1 s; the source workbook leaves the cell blank and the matrix silently omits the pair.",
                "Review the conflict. The emitted value is the conservative ceiling of the raw result.",
                "Addendum §C.1"));
            return new LegacyProductionRowResult(row.ConflictNo, findings, calc, emitted);
        }

        return new LegacyProductionRowResult(row.ConflictNo, findings, calc, calc.FinalIg);
    }
}
