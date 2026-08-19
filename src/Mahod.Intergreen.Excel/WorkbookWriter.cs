using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mahod.Intergreen.Reporting;

namespace Mahod.Intergreen.Excel;

/// <summary>Result of an export, with the structural verification evidence (Directive §23/§55).</summary>
/// <param name="RowsPopulated">Input Distances rows that received the engine's governing point.</param>
/// <param name="MultiPointRows">Populated rows whose conflict has more than one candidate point
/// (all candidates live in 'MAHOD Engine Results'; the compatibility view shows the governing one).</param>
/// <param name="RowsClearedNoEngineResult">Input Distances rows whose movement pair has no engine
/// conflict/points: their manual CD/ED were cleared (never mixed with engine values) and flagged
/// in the QA column. The source workbook is untouched.</param>
/// <param name="PivotCachesReset">Legacy PivotTable caches marked refreshOnLoad + purged (r11).</param>
/// <param name="LegacyPivotNotes">Human-readable notes about the legacy pivot source health.</param>
public sealed record ExportResult(
    string OutputPath,
    IReadOnlyList<string> PreservedSheets,
    IReadOnlyList<string> AddedSheets,
    IReadOnlyList<string> StructuralIssues,
    int RowsPopulated,
    int MultiPointRows,
    int RowsClearedNoEngineResult,
    int PivotCachesReset,
    IReadOnlyList<string> LegacyPivotNotes);

/// <summary>
/// Production Excel exporter (Directive §22–§29), implemented with SURGICAL OpenXML edits:
/// the output is a byte-copy of David's workbook in which only the explicitly targeted
/// parts change (Input Distances CD/ED cells, a MAHOD QA column, two appended sheets,
/// fullCalcOnLoad, the obsolete xl/calcChain.xml dropped, and — r11 — the legacy
/// PivotTable caches reset so Excel rebuilds them from the engine-populated workbook on
/// open). Every other part — formulas, styles, images, print settings, names — remains
/// untouched at package level, so nothing can be silently lost.
///
/// Export contract (re-confirmed r11, Lin's refresh finding): the familiar legacy sheets ARE
/// fed by the engine (compatibility view), but the whole legacy chain must then be internally
/// consistent — one source of truth, no manual/engine mix, no stale cached result:
///   • Input Distances row = the engine's GOVERNING point only (slot 1), exactly as the manual
///     workflow fills one point; every candidate point lives in 'MAHOD Engine Results'.
///     (Writing 4 slots exposed a latent defect in the V2 template's AutoAdjusted sheet —
///     slots 2–4 add the slow-speed column instead of the Inbar addition — which the manual
///     one-point practice never hit; the governing point alone yields the identical FINAL IG.)
///   • rows whose movement pair has no engine conflict/points are CLEARED and flagged, never
///     left with manual numbers underneath engine numbers.
///   • the Matrix PivotTable cache is never shipped stale (refreshOnLoad + purged + cleared).
///
/// (A full-package rewriter such as ClosedXML re-serializes every part; on these real
/// workbooks it corrupts the Matrix pivot cache that already contains historical #REF!
/// values. The surgical approach never rewrites parts it does not edit.)
/// </summary>
public static class WorkbookWriter
{
    public const string EngineResultsSheet = "MAHOD Engine Results";
    public const string MatrixStatusSheet = "MAHOD Matrix Status";
    public const string QaColumnHeader = "MAHOD QA";

    public static ExportResult Export(
        string sourceWorkbookPath,
        string outputPath,
        AnalysisDocument analysis,
        WorkbookModel sourceModel)
    {
        if (Path.GetFullPath(sourceWorkbookPath) == Path.GetFullPath(outputPath))
            throw new InvalidOperationException("output must never overwrite the source workbook");

        File.Copy(sourceWorkbookPath, outputPath, overwrite: true);

        var preserved = new List<string>();
        var rowsPopulated = 0;
        var multiPoint = 0;
        var rowsCleared = 0;
        var replacedFormulaCells = new List<string>();
        var pivotNotes = new List<string>();
        var pivotCachesReset = 0;

        using (var doc = SpreadsheetDocument.Open(outputPath, true))
        {
            var wbPart = doc.WorkbookPart!;
            var sheets = wbPart.Workbook.Sheets!.Elements<Sheet>().ToList();
            preserved.AddRange(sheets.Select(s => s.Name!.Value!));

            // ---- Input Distances: compatibility view (§25) ----
            var inputSheet = sheets.FirstOrDefault(s => s.Name == "Input Distances")
                ?? throw new UnsupportedTemplateException("no 'Input Distances' sheet in source");
            var inputPart = (WorksheetPart)wbPart.GetPartById(inputSheet.Id!);
            var sheetData = inputPart.Worksheet.GetFirstChild<SheetData>()!;

            var conflictsById = analysis.Conflicts.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var shared = wbPart.SharedStringTablePart?.SharedStringTable;

            string CellText(Cell? c)
            {
                if (c?.CellValue is null) return "";
                var v = c.CellValue.InnerText;
                if (c.DataType?.Value == CellValues.SharedString && shared is not null
                    && int.TryParse(v, out var idx))
                    return shared.ElementAt(idx).InnerText;
                return v;
            }

            // QA column: first free column two beyond AL (AN = 40)
            const int qaCol = 40;
            SetCell(sheetData, 2, qaCol, QaColumnHeader, isText: true);

            foreach (var row in sheetData.Elements<Row>().Where(r => r.RowIndex?.Value >= 3).ToList())
            {
                var r = (int)row.RowIndex!.Value;
                var clearing = CellText(GetCell(row, 2));
                var entering = CellText(GetCell(row, 3));
                if (string.IsNullOrWhiteSpace(clearing)) continue;
                var hasConflict = conflictsById.TryGetValue($"{clearing}→{entering}", out var conflict);

                if (!hasConflict || conflict!.Points.Count == 0)
                {
                    // r11: no engine result for this pair → the legacy row must not keep manual
                    // numbers next to engine numbers. Clear all four slots, say why in the QA
                    // column (the SOURCE workbook still holds the manual values, untouched).
                    var hadValue = false;
                    for (var col = 4; col <= 11; col++)
                        hadValue |= ClearCell(row, col, replacedFormulaCells);
                    SetCell(sheetData, r, qaCol,
                        (hasConflict
                            ? $"NO_ENGINE_POINTS — engine found no candidate point for {clearing}→{entering}; "
                            : $"NOT_IN_ENGINE_RESULTS — {clearing}→{entering} is not an engine conflict; ") +
                        (hadValue ? "manual CD/ED removed from this copy (source workbook unchanged). " : "") +
                        $"See '{EngineResultsSheet}'.",
                        isText: true);
                    if (hadValue) rowsCleared++;
                    continue;
                }

                // governing point = the engine's defining point (max raw IG); fall back to the
                // raw-IG ordering only if the engine did not name one.
                var governing = conflict.Points.FirstOrDefault(p => p.Id == conflict.DefiningPointId)
                    ?? conflict.Points
                        .OrderByDescending(p => p.RawIg ?? double.MinValue)
                        .ThenBy(p => p.Cd).ThenBy(p => p.Ed).ThenBy(p => p.Id, StringComparer.Ordinal)
                        .First();

                SetCell(sheetData, r, 4, Math.Round(governing.Cd, 3), false, replacedFormulaCells);
                SetCell(sheetData, r, 5, Math.Round(governing.Ed, 3), false, replacedFormulaCells);
                for (var col = 6; col <= 11; col++)
                    ClearCell(row, col, replacedFormulaCells);
                rowsPopulated++;
                if (conflict.Points.Count > 1) multiPoint++;

                SetCell(sheetData, r, qaCol,
                    $"ENGINE governing point {governing.Id}" +
                    (conflict.Points.Count > 1 ? $" of {conflict.Points.Count} candidates" : "") +
                    $" — all candidates in '{EngineResultsSheet}'",
                    isText: true);
            }
            inputPart.Worksheet.Save();

            // ---- remove stale MAHOD sheets from previous exports, then append fresh ones ----
            foreach (var stale in sheets.Where(s => s.Name == EngineResultsSheet || s.Name == MatrixStatusSheet).ToList())
            {
                var part = (WorksheetPart)wbPart.GetPartById(stale.Id!);
                wbPart.DeletePart(part);
                stale.Remove();
            }

            string SgOf(string m) => sourceModel.SignalGroups.TryGetValue(m, out var sg) ? sg : m;

            // ---- MAHOD Engine Results (§26) — authoritative, no point limit ----
            var engineRows = new List<object?[]>
            {
                new object?[]
                {
                    "Conflict ID", "Clearing", "Entering", "Clearing SG", "Entering SG",
                    "Point ID", "CD [m]", "ED [m]", "X", "Y", "Clearing curve", "Entering curve",
                    "Raw IG [s]", "Defining", "Final IG [s]", "Status", "Rule Pack", "Findings",
                },
            };
            foreach (var c in analysis.Conflicts)
            {
                if (c.Points.Count == 0)
                {
                    engineRows.Add(new object?[]
                    {
                        c.Id, c.Clearing, c.Entering, SgOf(c.Clearing), SgOf(c.Entering),
                        null, null, null, null, null, null, null,
                        null, null, null, c.Status, $"{analysis.RulePack.Id} {analysis.RulePack.Version}",
                        string.Join("; ", c.FindingCodes),
                    });
                    continue;
                }
                foreach (var p in c.Points)
                {
                    engineRows.Add(new object?[]
                    {
                        c.Id, c.Clearing, c.Entering, SgOf(c.Clearing), SgOf(c.Entering),
                        p.Id, Math.Round(p.Cd, 3), Math.Round(p.Ed, 3), p.X, p.Y,
                        p.ClearingCurveId, p.EnteringCurveId,
                        p.RawIg is double rig ? Math.Round(rig, 4) : null,
                        c.DefiningPointId == p.Id ? "YES" : "",
                        c.FinalIg, c.Status, $"{analysis.RulePack.Id} {analysis.RulePack.Version}",
                        string.Join("; ", c.FindingCodes),
                    });
                }
            }
            AppendSheet(wbPart, EngineResultsSheet, engineRows);

            // ---- MAHOD Matrix Status (§26/§28) ----
            var matrixRows = new List<object?[]>
            {
                new object?[] { "Clearing SG", "Entering SG", "Engine Value [s]", "Status", "Governing Conflict" },
            };
            foreach (var cell in analysis.Matrix)
            {
                matrixRows.Add(new object?[]
                {
                    cell.ClearingSignalGroup, cell.EnteringSignalGroup,
                    cell.Value, cell.Status, cell.GoverningConflictId ?? "",
                });
            }
            AppendSheet(wbPart, MatrixStatusSheet, matrixRows);

            // ---- legacy PivotTables (r11, Lin's refresh finding) ----
            // The Matrix sheet's PivotTable caches the legacy results as of the source's LAST
            // MANUAL refresh. A byte-copied cache therefore displays the manual matrix until
            // the user presses Refresh, while the engine-populated sheets underneath already
            // say otherwise ("without Refresh everything matches; after Refresh numbers
            // change"). An export must never ship a stale pivot: every worksheet-sourced cache
            // is marked refreshOnLoad, its cached records are purged and the pivot's rendered
            // cells are cleared, so Excel rebuilds the PivotTable from the engine-populated
            // workbook on open (after fullCalcOnLoad). Refresh stays fully functional and no
            // formula is touched. Proven in real Excel: open == Refresh == Refresh All == reopen.
            pivotCachesReset = ResetLegacyPivots(wbPart, pivotNotes);

            // ---- authoritative-engine discipline (§27): Excel recalculates on open ----
            var calcProps = wbPart.Workbook.GetFirstChild<CalculationProperties>();
            if (calcProps is null)
            {
                calcProps = new CalculationProperties();
                wbPart.Workbook.InsertAfter(calcProps, wbPart.Workbook.Sheets);
            }
            calcProps.FullCalculationOnLoad = true;
            calcProps.ForceFullCalculation = true;

            // ---- calculation chain (r10, Lin finding) ----
            // xl/calcChain.xml is Excel's cached list of formula cells (derived metadata,
            // not content). The export intentionally replaces explicit CD/ED formula cells
            // with engine values, so a byte-copied chain still references cells that no
            // longer hold a formula; Excel then reports "We found a problem with some
            // content… Removed Records: Formula from /xl/calcChain.xml part" and opens the
            // export as [Repaired]. The standards-safe fix is to drop the obsolete chain —
            // DeletePart removes the part, its workbook relationship and its
            // [Content_Types].xml override — and let Excel rebuild it on open, which
            // fullCalcOnLoad/forceFullCalc already require. Worksheet formulas are untouched.
            var calcChain = wbPart.CalculationChainPart;
            if (calcChain is not null)
                wbPart.DeletePart(calcChain);

            wbPart.Workbook.Save();
        }

        var issues = VerifyStructure(sourceWorkbookPath, outputPath, replacedFormulaCells.Count);
        return new ExportResult(outputPath, preserved,
            new[] { EngineResultsSheet, MatrixStatusSheet }, issues, rowsPopulated, multiPoint,
            rowsCleared, pivotCachesReset, pivotNotes);
    }

    // ---------------- legacy PivotTables (r11) ----------------

    /// <summary>
    /// Marks every worksheet-sourced pivot cache refreshOnLoad, purges its cached records and
    /// clears the rendered cells of each PivotTable that uses it. Returns the number of caches
    /// reset. Notes describe pre-existing source-range errors (e.g. #REF! rows) that make part
    /// of the legacy Matrix uncomputable in the SOURCE workbook.
    /// </summary>
    private static int ResetLegacyPivots(WorkbookPart wbPart, List<string> notes)
    {
        var reset = 0;
        var sheets = wbPart.Workbook.Sheets!.Elements<Sheet>().ToList();
        foreach (var cachePart in wbPart.PivotTableCacheDefinitionParts.ToList())
        {
            var def = cachePart.PivotCacheDefinition;
            if (def is null) continue;
            var ws = def.CacheSource?.WorksheetSource;
            if (def.CacheSource?.Type?.Value != SourceValues.Worksheet || ws is null)
                continue; // external/consolidation sources: not ours to refresh

            def.RefreshOnLoad = true;
            def.RecordCount = 0U;
            var recPart = cachePart.PivotTableCacheRecordsPart;
            if (recPart is not null)
            {
                recPart.PivotCacheRecords = new PivotCacheRecords { Count = 0U };
                recPart.PivotCacheRecords.Save();
            }
            def.Save();
            reset++;

            // source-range health (pre-existing errors in the SOURCE workbook)
            if (ws.Sheet?.Value is string srcSheetName && ws.Reference?.Value is string srcRef)
            {
                var srcSheet = sheets.FirstOrDefault(x => x.Name == srcSheetName);
                if (srcSheet is not null && TryParseRange(srcRef, out var r1, out var c1, out var r2, out var c2))
                {
                    var srcPart = (WorksheetPart)wbPart.GetPartById(srcSheet.Id!);
                    int dataRows = 0, errorRows = 0;
                    foreach (var row in srcPart.Worksheet.GetFirstChild<SheetData>()!.Elements<Row>())
                    {
                        var ri = (int)(row.RowIndex?.Value ?? 0);
                        if (ri <= r1 || ri > r2) continue; // r1 = header row
                        var first = GetCell(row, c1);
                        if (first is null || (first.CellValue is null && first.InlineString is null)) continue;
                        dataRows++;
                        if (first.DataType?.Value == CellValues.Error) errorRows++;
                    }
                    if (errorRows > 0)
                        notes.Add($"legacy pivot source '{srcSheetName}'!{srcRef}: {errorRows} of {dataRows} source rows " +
                                  "are #REF!/error cells in the SOURCE workbook (pre-existing) — those rows cannot feed the legacy Matrix; " +
                                  $"'{MatrixStatusSheet}' is authoritative");
                }
            }
        }

        // clear the rendered cells of every PivotTable (Excel re-renders on the on-load refresh)
        foreach (var sheet in sheets)
        {
            var part = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
            foreach (var pt in part.PivotTableParts)
            {
                var loc = pt.PivotTableDefinition?.Location?.Reference?.Value;
                if (loc is null || !TryParseRange(loc, out var r1, out var c1, out var r2, out var c2)) continue;
                var sheetData = part.Worksheet.GetFirstChild<SheetData>();
                if (sheetData is null) continue;
                foreach (var row in sheetData.Elements<Row>())
                {
                    var ri = (int)(row.RowIndex?.Value ?? 0);
                    if (ri < r1 || ri > r2) continue;
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var ci = ColumnIndexOf(cell.CellReference?.Value);
                        if (ci < c1 || ci > c2) continue;
                        cell.CellFormula = null;
                        cell.CellValue = null;
                        cell.InlineString = null;
                        cell.DataType = null; // style kept; value gone
                    }
                }
                part.Worksheet.Save();
            }
        }
        return reset;
    }

    /// <summary>"C3:K12" → 1-based rows/cols; "C3" → single cell.</summary>
    public static bool TryParseRange(string reference, out int r1, out int c1, out int r2, out int c2)
    {
        r1 = c1 = r2 = c2 = 0;
        var parts = reference.Replace("$", "").Split(':');
        if (parts.Length is < 1 or > 2) return false;
        if (!TryParseCell(parts[0], out r1, out c1)) return false;
        if (parts.Length == 1) { r2 = r1; c2 = c1; return true; }
        return TryParseCell(parts[1], out r2, out c2);

        static bool TryParseCell(string cell, out int row, out int col)
        {
            row = col = 0;
            var i = 0;
            while (i < cell.Length && char.IsLetter(cell[i])) { col = col * 26 + (char.ToUpperInvariant(cell[i]) - 'A' + 1); i++; }
            return col > 0 && int.TryParse(cell[i..], out row) && row > 0;
        }
    }

    // ---------------- structural before/after verification (§23) ----------------

    private static List<string> VerifyStructure(string srcPath, string dstPath, int intentionalTargetReplacements)
    {
        var issues = new List<string>();
        using var src = SpreadsheetDocument.Open(srcPath, false);
        using var dst = SpreadsheetDocument.Open(dstPath, false);

        // r10 post-condition: an export must never carry a calculation chain — after CD/ED
        // formula replacement any copied chain is stale and triggers Excel's repair prompt.
        if (dst.WorkbookPart!.CalculationChainPart is not null)
            issues.Add("output still carries xl/calcChain.xml (stale calculation chain → Excel repair prompt)");
        var dstCalcPr = dst.WorkbookPart.Workbook.GetFirstChild<CalculationProperties>();
        if (dstCalcPr?.FullCalculationOnLoad?.Value != true)
            issues.Add("output calcPr lacks fullCalcOnLoad=1 (Excel would not rebuild the calculation chain / recalc on open)");

        // r11 post-conditions: no legacy pivot may ship stale — every worksheet-sourced cache is
        // refreshOnLoad with zero cached records, and no PivotTable was lost.
        foreach (var audit in PivotCacheAudit.Inspect(dst))
        {
            if (!audit.WorksheetSourced) continue;
            if (!audit.RefreshOnLoad)
                issues.Add($"legacy pivot cache ({audit.Source}) is not refreshOnLoad — Excel would display the stale manual matrix until Refresh");
            if (audit.CachedRecords > 0)
                issues.Add($"legacy pivot cache ({audit.Source}) still carries {audit.CachedRecords} cached records from the source's last manual refresh");
        }
        var srcPivots = src.WorkbookPart!.WorksheetParts.Sum(w => w.PivotTableParts.Count());
        var dstPivots = dst.WorkbookPart.WorksheetParts.Sum(w => w.PivotTableParts.Count());
        if (dstPivots != srcPivots)
            issues.Add($"PivotTable count changed {srcPivots} → {dstPivots}");

        var srcSheets = src.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToList();
        var dstSheets = dst.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToList();
        for (var i = 0; i < srcSheets.Count; i++)
        {
            if (i >= dstSheets.Count || dstSheets[i] != srcSheets[i])
                issues.Add($"sheet order/name changed: expected '{srcSheets[i]}' at position {i}");
        }

        foreach (var name in srcSheets)
        {
            var s = Part(src, name);
            var d = Part(dst, name);
            if (d is null) { issues.Add($"sheet '{name}' missing in output"); continue; }

            var srcFormulas = s!.Worksheet.Descendants<CellFormula>().Count();
            var dstFormulas = d.Worksheet.Descendants<CellFormula>().Count();
            var allowed = name == "Input Distances" ? intentionalTargetReplacements : 0;
            if (dstFormulas < srcFormulas - allowed)
                issues.Add($"sheet '{name}': formula count dropped {srcFormulas} → {dstFormulas} (beyond {allowed} intentional CD/ED target replacements)");

            var srcErrors = ErrorCells(s.Worksheet);
            var dstErrors = ErrorCells(d.Worksheet);
            foreach (var e in dstErrors.Except(srcErrors))
                issues.Add($"NEW workbook error introduced: {name}!{e}");

            // images survive because drawing parts are never rewritten
            var srcImages = s.DrawingsPart?.ImageParts.Count() ?? 0;
            var dstImages = d.DrawingsPart?.ImageParts.Count() ?? 0;
            if (dstImages < srcImages)
                issues.Add($"sheet '{name}': image count dropped {srcImages} → {dstImages}");
        }
        return issues;

        static WorksheetPart? Part(SpreadsheetDocument docm, string name)
        {
            var sheet = docm.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().FirstOrDefault(s => s.Name == name);
            return sheet is null ? null : (WorksheetPart)docm.WorkbookPart.GetPartById(sheet.Id!);
        }

        static HashSet<string> ErrorCells(Worksheet ws) => ws.Descendants<Cell>()
            .Where(c => c.DataType?.Value == CellValues.Error && c.CellReference?.Value is not null)
            .Select(c => c.CellReference!.Value!)
            .ToHashSet();
    }

    // ---------------- low-level helpers ----------------

    private static string ColumnLetter(int col)
    {
        var s = "";
        while (col > 0)
        {
            var rem = (col - 1) % 26;
            s = (char)('A' + rem) + s;
            col = (col - 1) / 26;
        }
        return s;
    }

    private static Cell? GetCell(Row row, int col)
    {
        var reference = ColumnLetter(col) + row.RowIndex!.Value;
        return row.Elements<Cell>().FirstOrDefault(c => c.CellReference == reference);
    }

    /// <returns>true when the cell had a value or formula (i.e. something was actually removed).</returns>
    private static bool ClearCell(Row row, int col, List<string>? replacedFormulas = null)
    {
        var cell = GetCell(row, col);
        if (cell is null) return false;
        var had = cell.CellValue is not null || cell.InlineString is not null || cell.CellFormula is not null;
        if (cell.CellFormula is not null)
        {
            replacedFormulas?.Add(cell.CellReference?.Value ?? "");
            cell.CellFormula = null;
        }
        cell.CellValue = null;
        cell.InlineString = null;
        cell.DataType = null;
        return had; // style left in place
    }

    private static void SetCell(SheetData sheetData, int rowIndex, int col, object value, bool isText = false)
        => SetCell(sheetData, rowIndex, col, value, isText, null);

    private static void SetCell(SheetData sheetData, int rowIndex, int col, object value, bool isText, List<string>? replacedFormulas)
    {
        var row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == (uint)rowIndex);
        if (row is null)
        {
            row = new Row { RowIndex = (uint)rowIndex };
            var after = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value > (uint)rowIndex);
            if (after is null) sheetData.Append(row);
            else sheetData.InsertBefore(row, after);
        }
        var reference = ColumnLetter(col) + rowIndex;
        var cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference == reference);
        if (cell is null)
        {
            cell = new Cell { CellReference = reference };
            var after = row.Elements<Cell>().FirstOrDefault(c =>
                ColumnIndexOf(c.CellReference?.Value) > col);
            if (after is null) row.Append(cell);
            else row.InsertBefore(cell, after);
        }
        if (cell.CellFormula is not null)
        {
            // §23: an explicit target cell is intentionally changed — record the replacement
            replacedFormulas?.Add(reference);
            cell.CellFormula = null;
        }
        if (isText)
        {
            cell.DataType = CellValues.InlineString;
            cell.CellValue = null;
            cell.InlineString = new InlineString(new Text(value.ToString()!));
        }
        else
        {
            cell.InlineString = null;
            cell.DataType = null; // number
            cell.CellValue = new CellValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!);
        }
    }

    private static int ColumnIndexOf(string? reference)
    {
        if (reference is null) return int.MaxValue;
        var col = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return col;
    }

    private static void AppendSheet(WorkbookPart wbPart, string name, List<object?[]> rows)
    {
        var part = wbPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new Row { RowIndex = (uint)(r + 1) };
            for (var c = 0; c < rows[r].Length; c++)
            {
                var v = rows[r][c];
                if (v is null) continue;
                var cell = new Cell { CellReference = ColumnLetter(c + 1) + (r + 1) };
                if (v is double d)
                {
                    cell.CellValue = new CellValue(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (v is int i)
                {
                    cell.CellValue = new CellValue(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    cell.DataType = CellValues.InlineString;
                    cell.InlineString = new InlineString(new Text(v.ToString()!));
                }
                row.Append(cell);
            }
            sheetData.Append(row);
        }
        part.Worksheet = new Worksheet(sheetData);
        part.Worksheet.Save();

        var sheets = wbPart.Workbook.Sheets!;
        var maxId = sheets.Elements<Sheet>().Max(s => s.SheetId!.Value);
        sheets.Append(new Sheet
        {
            Id = wbPart.GetIdOfPart(part),
            SheetId = maxId + 1,
            Name = name,
        });
    }
}
