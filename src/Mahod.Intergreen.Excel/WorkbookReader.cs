using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using Mahod.Intergreen.Core.Legacy;

namespace Mahod.Intergreen.Excel;

/// <summary>Thrown when a workbook does not match any known template variant (v3 §31).</summary>
public sealed class UnsupportedTemplateException : Exception
{
    public UnsupportedTemplateException(string message) : base("UNSUPPORTED_TEMPLATE_VERSION: " + message) { }
}

/// <summary>Everything read from one IG_matrix workbook.</summary>
public sealed record WorkbookModel(
    LegacyTemplateVariant Variant,
    LegacyConstants Constants,
    IReadOnlyDictionary<string, LegacyMovementParameters> MovementParameters,
    IReadOnlyList<WorkbookConflictRow> Rows,
    IReadOnlyDictionary<string, string> SignalGroups,          // movement → SG label
    IReadOnlyList<string> SourceErrors,                        // pre-existing #REF!/etc (v3 §33)
    IReadOnlyDictionary<string, double> PedestrianWidths,      // crossing SG name → W [m] (authoritative, Directive §6)
    IReadOnlyList<string> PedestrianWidthConflicts);           // SG names with disagreeing W rows

public sealed record WorkbookConflictRow(
    ConflictRowInput Input,
    double? CachedFinalIg,
    IReadOnlyList<double?> CachedPointIgs);

/// <summary>
/// Reads David's IG_matrix workbooks. Variant detection is content-based —
/// constants-block labels + row-1 lookup indices — never filename or hard-coded
/// coordinates (v3 §31, Addendum §C.2). Unknown structure → UNSUPPORTED_TEMPLATE_VERSION.
/// </summary>
public static class WorkbookReader
{
    public static WorkbookModel Read(string path)
    {
        using var wb = new XLWorkbook(path);

        var parameters = wb.Worksheets.FirstOrDefault(ws => ws.Name == "Parameters")
            ?? throw new UnsupportedTemplateException("no 'Parameters' sheet");
        var input = wb.Worksheets.FirstOrDefault(ws => ws.Name == "Input Distances")
            ?? throw new UnsupportedTemplateException("no 'Input Distances' sheet");

        // ---- constants block: locate by Hebrew labels in column H ----
        var labels = new Dictionary<int, string>();
        for (var r = 2; r <= 16; r++)
        {
            var t = parameters.Cell(r, 8).GetString();
            if (!string.IsNullOrWhiteSpace(t)) labels[r] = t;
        }
        int? RowOf(Func<string, bool> pred) =>
            labels.Where(kv => pred(kv.Value)).Select(kv => (int?)kv.Key).FirstOrDefault();

        var vehRow = RowOf(t => t.Contains("אורך רכב"));
        var pedRow = RowOf(t => t.Contains("מהירות ה"));
        var reaRow = RowOf(t => t.Contains("זמן תגובה"));
        var decRow = RowOf(t => t.Contains("תאוט"));
        if (pedRow is null || reaRow is null || decRow is null)
            throw new UnsupportedTemplateException(
                $"constants-block labels not recognised (found: {string.Join(" | ", labels.Values)})");

        double Const(int r) => parameters.Cell(r, 9).GetDouble();

        LegacyTemplateVariant variant;
        LegacyConstants constants;
        if (vehRow is int vr)
        {
            variant = LegacyTemplateVariant.V2GlobalVehicleLength;
            constants = new LegacyConstants(
                PedestrianSpeedMps: Const(pedRow.Value),
                ReactionTimeSec: Const(reaRow.Value),
                DecelerationMps2: Const(decRow.Value),
                GlobalVehicleLengthMeters: Const(vr));
        }
        else
        {
            variant = LegacyTemplateVariant.V1PerMovementVehicleLength;
            var inbarFlag = parameters.Cell(7, 9).GetString();
            var inbarLen = parameters.Cell(8, 9).TryGetValue<double>(out var il) ? il : 12.0;
            constants = new LegacyConstants(
                PedestrianSpeedMps: Const(pedRow.Value),
                ReactionTimeSec: Const(reaRow.Value),
                DecelerationMps2: Const(decRow.Value),
                InbarMode: string.Equals(inbarFlag, "y", StringComparison.OrdinalIgnoreCase),
                InbarDefaultVehicleLengthMeters: inbarLen);
        }

        // ---- movement parameter table (rows 3..14, columns A..F) ----
        var movementParams = new Dictionary<string, LegacyMovementParameters>(StringComparer.Ordinal);
        for (var r = 3; r <= 14; r++)
        {
            var name = parameters.Cell(r, 1).GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            movementParams[name] = new LegacyMovementParameters(
                NumOrNull(parameters.Cell(r, 3)),
                NumOrNull(parameters.Cell(r, 4)),
                NumOrNull(parameters.Cell(r, 5)),
                NumOrNull(parameters.Cell(r, 6)));
        }

        // ---- header sanity on Input Distances (content-based, not coordinates) ----
        if (input.Cell(2, 1).GetString() != "Conflict No."
            || input.Cell(2, 2).GetString() != "Clearing Movement"
            || input.Cell(2, 4).GetString() != "CD"
            || input.Cell(2, 5).GetString() != "ED")
            throw new UnsupportedTemplateException("Input Distances header row not recognised");
        var finalIgCol = 37; // AK
        if (!input.Cell(2, finalIgCol).GetString().Contains("FINAL IG"))
            throw new UnsupportedTemplateException("FINAL IG column not where the known variants place it");

        // ---- conflict rows ----
        var rows = new List<WorkbookConflictRow>();
        var sourceErrors = new List<string>();
        var lastRow = input.LastRowUsed()?.RowNumber() ?? 2;
        for (var r = 3; r <= lastRow; r++)
        {
            var noCell = input.Cell(r, 1);
            if (!noCell.TryGetValue<double>(out var no)) continue;
            var clearing = input.Cell(r, 2).GetString();
            var entering = CellText(input.Cell(r, 3));
            if (string.IsNullOrWhiteSpace(clearing)) continue;

            MeasuredPoint Pt(int cdCol) => new(
                NumOrNull(input.Cell(r, cdCol)), NumOrNull(input.Cell(r, cdCol + 1)));

            var igs = new double?[]
            {
                NumOrNull(input.Cell(r, 19)),  // S
                NumOrNull(input.Cell(r, 23)),  // W
                NumOrNull(input.Cell(r, 27)),  // AA
                NumOrNull(input.Cell(r, 31)),  // AE
            };

            rows.Add(new WorkbookConflictRow(
                new ConflictRowInput((int)no, clearing, entering, Pt(4), Pt(6), Pt(8), Pt(10)),
                NumOrNull(input.Cell(r, finalIgCol)),
                igs));
        }

        // ---- pedestrian crossing widths (Pedestrian Xing sheet — authoritative W, Directive §6) ----
        var pedWidths = new Dictionary<string, double>(StringComparer.Ordinal);
        var pedConflicts = new List<string>();
        var pedSheet = wb.Worksheets.FirstOrDefault(ws => ws.Name == "Pedestrian Xing");
        if (pedSheet is not null)
        {
            var last = pedSheet.LastRowUsed()?.RowNumber() ?? 2;
            for (var r = 3; r <= last; r++)
            {
                var sg = CellText(pedSheet.Cell(r, 2)).Trim();
                var w = NumOrNull(pedSheet.Cell(r, 3));
                if (string.IsNullOrWhiteSpace(sg) || w is not double width) continue;
                if (pedWidths.TryGetValue(sg, out var existing))
                {
                    if (Math.Abs(existing - width) > 1e-9 && !pedConflicts.Contains(sg))
                        pedConflicts.Add(sg); // PEDESTRIAN_WIDTH_CONFLICT — never pick one arbitrarily
                }
                else
                {
                    pedWidths[sg] = width;
                }
            }
        }

        // ---- signal groups ----
        var signalGroups = new Dictionary<string, string>(StringComparer.Ordinal);
        var sgSheet = wb.Worksheets.FirstOrDefault(ws => ws.Name == "Signal group key");
        if (sgSheet is not null)
        {
            var last = sgSheet.LastRowUsed()?.RowNumber() ?? 2;
            for (var r = 3; r <= last; r++)
            {
                var mv = sgSheet.Cell(r, 1).GetString();
                var sg = CellText(sgSheet.Cell(r, 2));
                if (!string.IsNullOrWhiteSpace(mv) && !string.IsNullOrWhiteSpace(sg))
                    signalGroups[mv] = sg;
            }
        }

        // ---- pre-existing source errors (v3 §33) ----
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used is null) continue;
            foreach (var cell in used.Cells().Where(c => c.HasFormula))
            {
                var cv = cell.CachedValue;
                if (cv.IsError)
                    sourceErrors.Add($"SOURCE_WORKBOOK_EXISTING_ERROR: {ws.Name}!{cell.Address} = {cv.GetError()}");
            }
        }

        return new WorkbookModel(variant, constants, movementParams, rows, signalGroups, sourceErrors,
            pedWidths, pedConflicts);
    }

    /// <summary>Cached numeric value or null. Formula cells contribute their cached value.</summary>
    private static double? NumOrNull(IXLCell cell)
    {
        var v = cell.HasFormula ? cell.CachedValue : cell.Value;
        return v.IsNumber ? v.GetNumber() : null;
    }

    /// <summary>Cached text (formula cells contribute their cached string).</summary>
    private static string CellText(IXLCell cell)
    {
        var v = cell.HasFormula ? cell.CachedValue : cell.Value;
        return v.IsText ? v.GetText() : v.ToString() ?? "";
    }
}
