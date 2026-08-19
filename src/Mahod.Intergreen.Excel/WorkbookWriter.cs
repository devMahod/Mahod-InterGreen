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
public sealed record ExportResult(
    string OutputPath,
    IReadOnlyList<string> PreservedSheets,
    IReadOnlyList<string> AddedSheets,
    IReadOnlyList<string> StructuralIssues,
    int RowsPopulated,
    int MoreThanFourPointRows);

/// <summary>
/// Production Excel exporter (Directive §22–§29), implemented with SURGICAL OpenXML edits:
/// the output is a byte-copy of David's workbook in which only the explicitly targeted
/// parts change (Input Distances CD/ED cells, a MAHOD QA column, two appended sheets,
/// fullCalcOnLoad, and the obsolete xl/calcChain.xml dropped). Every other part — formulas, styles, images, pivot caches, print
/// settings, names — remains untouched at package level, so nothing can be silently lost.
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
        var moreThanFour = 0;
        var replacedFormulaCells = new List<string>();

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
                if (!conflictsById.TryGetValue($"{clearing}→{entering}", out var conflict)
                    || conflict.Points.Count == 0)
                    continue;

                var ordered = conflict.Points
                    .OrderByDescending(p => p.RawIg ?? double.MinValue)
                    .ThenBy(p => p.Cd).ThenBy(p => p.Ed).ThenBy(p => p.Id, StringComparer.Ordinal)
                    .ToList();
                var selected = ordered.Take(4).ToList();
                if (conflict.DefiningPointId is string gov && selected.All(p => p.Id != gov))
                    selected[^1] = conflict.Points.First(p => p.Id == gov);

                for (var p = 0; p < 4; p++)
                {
                    if (p < selected.Count)
                    {
                        SetCell(sheetData, r, 4 + p * 2, Math.Round(selected[p].Cd, 3), false, replacedFormulaCells);
                        SetCell(sheetData, r, 5 + p * 2, Math.Round(selected[p].Ed, 3), false, replacedFormulaCells);
                    }
                    else
                    {
                        ClearCell(row, 4 + p * 2);
                        ClearCell(row, 5 + p * 2);
                    }
                }
                rowsPopulated++;

                if (conflict.Points.Count > 4)
                {
                    moreThanFour++;
                    SetCell(sheetData, r, qaCol,
                        $"MORE_THAN_4_CANDIDATE_POINTS ({conflict.Points.Count}) — REVIEW_REQUIRED, see '{EngineResultsSheet}'",
                        isText: true);
                }
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
            new[] { EngineResultsSheet, MatrixStatusSheet }, issues, rowsPopulated, moreThanFour);
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

    private static void ClearCell(Row row, int col)
    {
        var cell = GetCell(row, col);
        if (cell is null) return;
        cell.CellValue = null;
        cell.DataType = null;
        // raw-input cells carry no formulas; leave style in place
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
