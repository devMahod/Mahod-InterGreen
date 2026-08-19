using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Reporting;
using Xunit;

namespace Mahod.Intergreen.Excel.Tests;

/// <summary>Directive §22–§29 / §55 — the exporter preserves David's workbook and adds the authoritative sheets.</summary>
public class WorkbookWriterTests
{
    private static string Example1Path()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "materials", "Inter-green Automation",
                "03 Example 1", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Example 1 workbook not found");
    }

    /// <summary>tests/fixtures/calcchain/calcchain-regression.xlsx — minimal workbook WITH an xl/calcChain.xml (r10 Lin case).</summary>
    private static string CalcChainFixturePath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "tests", "fixtures", "calcchain", "calcchain-regression.xlsx");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("calcchain-regression.xlsx fixture not found");
    }

    /// <summary>Empty source model (no SG mapping) — the writer only consults SignalGroups.</summary>
    private static WorkbookModel EmptyModel() => new(
        LegacyTemplateVariant.V1PerMovementVehicleLength, new LegacyConstants(),
        new Dictionary<string, LegacyMovementParameters>(), Array.Empty<WorkbookConflictRow>(),
        new Dictionary<string, string>(), Array.Empty<string>(),
        new Dictionary<string, double>(), Array.Empty<string>());

    /// <summary>Package-level facts about the calculation chain, read straight from the ZIP (no SDK).</summary>
    private static (bool PartPresent, bool RelPresent, bool ContentTypePresent, string CalcPr) CalcChainFacts(string xlsxPath)
    {
        using var zip = ZipFile.OpenRead(xlsxPath);
        var part = zip.GetEntry("xl/calcChain.xml") is not null;
        var rels = ReadEntry(zip, "xl/_rels/workbook.xml.rels");
        var ct = ReadEntry(zip, "[Content_Types].xml");
        var wb = ReadEntry(zip, "xl/workbook.xml");
        var calcPr = XDocument.Parse(wb).Descendants().FirstOrDefault(e => e.Name.LocalName == "calcPr")?.ToString() ?? "";
        return (part, rels.Contains("/relationships/calcChain", StringComparison.Ordinal),
                ct.Contains("/xl/calcChain.xml", StringComparison.Ordinal), calcPr);

        static string ReadEntry(ZipArchive z, string name)
        {
            using var sr = new StreamReader(z.GetEntry(name)!.Open());
            return sr.ReadToEnd();
        }
    }

    private static int FormulaCount(string xlsxPath, string sheetName)
    {
        using var d = SpreadsheetDocument.Open(xlsxPath, false);
        var sheet = d.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().First(s => s.Name == sheetName);
        var part = (WorksheetPart)d.WorkbookPart.GetPartById(sheet.Id!);
        return part.Worksheet.Descendants<CellFormula>().Count();
    }

    private static AnalysisDocument Doc(params ConflictRecord[] conflicts) => new()
    {
        SchemaVersion = "1.0",
        EngineVersion = "0.1.0",
        IntersectionName = "TEST",
        SourceGeometry = new SourceGeometryRef("test.dwg", "AB"),
        RulePack = new RulePackSnapshot("legacy-mahod-v1", "1.0.0", "X", "legacy-envelope", "legacy-mahod-v1", "mahod-legacy-0.1", null),
        Classification = new ClassificationSnapshot(null, null, false, new List<KeyValuePair<string, string>>()),
        Movements = new List<MovementRecord>(),
        Conflicts = conflicts.ToList(),
        Matrix = new List<MatrixCellRecord>
        {
            new("1", "2", 6, "VALID", conflicts.FirstOrDefault()?.Id),
            new("2", "1", null, "BLOCKED", null),
        },
    };

    private static ConflictPointRecord Pt(string id, double cd, double ed, double? rawIg)
        => new(id, cd, ed, 0, 0, "c1", "c2", rawIg);

    [Fact]
    public void Export_preserves_sheets_and_adds_authoritative_sheets()
    {
        var src = Example1Path();
        var outPath = Path.Combine(Path.GetTempPath(), $"wbtest_{Guid.NewGuid():N}.xlsx");
        try
        {
            // S-T→W-L is a real row pair in Example 1
            var conflict = new ConflictRecord("S-T→W-L", "S-T", "W-L", "VALID",
                new[] { Pt("P1", 24.35, 5.02, 5.27), Pt("P2", 20.0, 8.0, 4.1) },
                "P1", 5.27, 6, "trace", Array.Empty<string>());
            var result = WorkbookWriter.Export(src, outPath, Doc(conflict),
                WorkbookReader.Read(src));

            Assert.Empty(result.StructuralIssues);
            Assert.Equal(1, result.RowsPopulated);

            using var wb = new XLWorkbook(outPath);
            // original sheets preserved in order, new sheets appended
            Assert.Equal("Parameters", wb.Worksheet(1).Name);
            Assert.Contains(wb.Worksheets, ws => ws.Name == WorkbookWriter.EngineResultsSheet);
            Assert.Contains(wb.Worksheets, ws => ws.Name == WorkbookWriter.MatrixStatusSheet);
            // Excel must recalculate on open (authoritative-engine discipline §27)
            Assert.True(wb.ForceFullCalculation);
            // formulas on original sheets survive
            Assert.True(wb.Worksheet("Input Distances").RangeUsed()!.Cells().Count(c => c.HasFormula) > 100);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void More_than_four_points_never_silently_truncated()
    {
        var src = Example1Path();
        var outPath = Path.Combine(Path.GetTempPath(), $"wbtest_{Guid.NewGuid():N}.xlsx");
        try
        {
            var points = Enumerable.Range(1, 6)
                .Select(i => Pt($"P{i}", 10 + i, 2 + i, 3.0 + i * 0.3)).ToArray();
            var conflict = new ConflictRecord("S-T→W-L", "S-T", "W-L", "VALID",
                points, "P6", 4.8, 5, "trace", Array.Empty<string>());
            var result = WorkbookWriter.Export(src, outPath, Doc(conflict), WorkbookReader.Read(src));

            Assert.Equal(1, result.MoreThanFourPointRows);
            using var wb = new XLWorkbook(outPath);
            // the complete point set lives in the engine sheet — all 6 rows
            var engine = wb.Worksheet(WorkbookWriter.EngineResultsSheet);
            var engineRows = engine.RangeUsed()!.Rows().Count() - 1;
            Assert.Equal(6, engineRows);
            // the compatibility view holds 4 slots with the governing point included
            var input = wb.Worksheet("Input Distances");
            var row = input.RangeUsed()!.Rows()
                .First(r => r.Cell(2).GetString() == "S-T" &&
                            (r.Cell(3).HasFormula ? r.Cell(3).CachedValue.ToString() : r.Cell(3).GetString()) == "W-L")
                .RowNumber();
            var cds = Enumerable.Range(0, 4).Select(p => input.Cell(row, 4 + p * 2).GetDouble()).ToList();
            Assert.Contains(16.0, cds); // P6 (governing, highest raw IG) present
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Source_workbook_is_never_modified()
    {
        var src = Example1Path();
        var before = File.ReadAllBytes(src);
        var outPath = Path.Combine(Path.GetTempPath(), $"wbtest_{Guid.NewGuid():N}.xlsx");
        try
        {
            WorkbookWriter.Export(src, outPath, Doc(), WorkbookReader.Read(src));
            Assert.Equal(before, File.ReadAllBytes(src));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Export_refuses_to_overwrite_the_source()
    {
        var src = Example1Path();
        Assert.Throws<InvalidOperationException>(() =>
            WorkbookWriter.Export(src, src, Doc(), WorkbookReader.Read(src)));
    }

    [Fact]
    public void Blocked_matrix_cell_is_visible_with_status_never_ambiguous_blank()
    {
        var src = Example1Path();
        var outPath = Path.Combine(Path.GetTempPath(), $"wbtest_{Guid.NewGuid():N}.xlsx");
        try
        {
            WorkbookWriter.Export(src, outPath, Doc(), WorkbookReader.Read(src));
            using var wb = new XLWorkbook(outPath);
            var matrix = wb.Worksheet(WorkbookWriter.MatrixStatusSheet);
            var blockedRow = matrix.RangeUsed()!.Rows().Skip(1)
                .First(r => r.Cell(4).GetString() == "BLOCKED");
            Assert.Equal("", blockedRow.Cell(3).GetString()); // no value…
            Assert.Equal("BLOCKED", blockedRow.Cell(4).GetString()); // …but the status says exactly why the blank exists
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    // ------------------------------------------------------------------------------------
    // r10 — Lin finding: "We found a problem with some content… Removed Records: Formula from
    // /xl/calcChain.xml part". Root cause: the export replaces explicit CD/ED formula cells
    // (Example 1: Input Distances D33–D42, the pedestrian VLOOKUP rows) but byte-copied the
    // source's cached xl/calcChain.xml, which still listed them. These tests pin the fix:
    // the obsolete chain (part + relationship + content-type override) is gone, Excel is told
    // to recalc on open, and every formula that was NOT intentionally replaced survives.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Export_of_calcchain_fixture_drops_stale_chain_and_keeps_untouched_formulas()
    {
        var src = CalcChainFixturePath();
        var srcBytes = File.ReadAllBytes(src);
        var outPath = Path.Combine(Path.GetTempPath(), $"wbtest_{Guid.NewGuid():N}.xlsx");
        try
        {
            // precondition — the fixture really carries a chain that lists the cell we will replace
            var before = CalcChainFacts(src);
            Assert.True(before.PartPresent && before.RelPresent && before.ContentTypePresent);
            using (var zip = ZipFile.OpenRead(src))
            using (var sr = new StreamReader(zip.GetEntry("xl/calcChain.xml")!.Open()))
                Assert.Contains("r=\"D3\"", sr.ReadToEnd());
            Assert.Equal(2, FormulaCount(src, "Input Distances")); // D3 (replaced) + F3 (kept)
            Assert.Equal(1, FormulaCount(src, "Other"));           // A1 (kept)

            // X→Y is the fixture's only conflict row; its D3 is a formula cell → replaced by the engine value
            var conflict = new ConflictRecord("X→Y", "X", "Y", "VALID",
                new[] { Pt("P1", 24.35, 5.02, 5.27) }, "P1", 5.27, 6, "trace", Array.Empty<string>());
            var result = WorkbookWriter.Export(src, outPath, Doc(conflict), EmptyModel());

            Assert.Empty(result.StructuralIssues);
            Assert.Equal(1, result.RowsPopulated);

            // the stale chain is gone at package level: part, workbook relationship AND content-type override
            var after = CalcChainFacts(outPath);
            Assert.False(after.PartPresent, "xl/calcChain.xml must not survive the export");
            Assert.False(after.RelPresent, "workbook.xml.rels must not reference calcChain");
            Assert.False(after.ContentTypePresent, "[Content_Types].xml must not override /xl/calcChain.xml");
            // …and Excel is told to rebuild it / recalc on open
            Assert.Contains("fullCalcOnLoad=\"1\"", after.CalcPr);
            Assert.Contains("forceFullCalc=\"1\"", after.CalcPr);

            // only the intentionally replaced formula is gone; every other formula survives
            Assert.Equal(1, FormulaCount(outPath, "Input Distances")); // F3 kept, D3 now a value
            Assert.Equal(1, FormulaCount(outPath, "Other"));
            using (var wb = new XLWorkbook(outPath))
            {
                var input = wb.Worksheet("Input Distances");
                Assert.False(input.Cell("D3").HasFormula);
                Assert.Equal(24.35, input.Cell("D3").GetDouble(), 3);
                Assert.Equal(5.02, input.Cell("E3").GetDouble(), 3);
                Assert.True(input.Cell("F3").HasFormula);
                Assert.Equal("D3*2", input.Cell("F3").FormulaA1);
                Assert.True(wb.Worksheet("Other").Cell("A1").HasFormula);
                // sheet order preserved, MAHOD sheets appended after the originals
                Assert.Equal(new[] { "Input Distances", "Other", WorkbookWriter.EngineResultsSheet, WorkbookWriter.MatrixStatusSheet },
                    wb.Worksheets.Select(w => w.Name).ToArray());
            }
            // the source is byte-identical
            Assert.Equal(srcBytes, File.ReadAllBytes(src));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Export_of_real_example1_replaces_pedestrian_formula_cells_without_leaving_a_calc_chain()
    {
        // The exact Lin scenario: Example 1 (05_PINES-HABAL- SHEM-TOM) carries a 5,000+ entry
        // calcChain and its pedestrian rows (Input Distances 33–42) hold VLOOKUP formulas in the
        // CD column. 'a→S-T' is row 34: its D34 formula is intentionally replaced by the engine.
        var src = Example1Path();
        var outPath = Path.Combine(Path.GetTempPath(), $"wbtest_{Guid.NewGuid():N}.xlsx");
        try
        {
            var before = CalcChainFacts(src);
            Assert.True(before.PartPresent, "Example 1 must carry a calcChain for this test to be meaningful");
            var srcFormulas = FormulaCount(src, "Input Distances");

            var conflict = new ConflictRecord("a→S-T", "a", "S-T", "VALID",
                new[] { Pt("P1", 8.35, 4.0, 4.96) }, "P1", 4.96, 5, "trace", Array.Empty<string>());
            var result = WorkbookWriter.Export(src, outPath, Doc(conflict), WorkbookReader.Read(src));

            Assert.Empty(result.StructuralIssues);
            Assert.Equal(1, result.RowsPopulated);

            var after = CalcChainFacts(outPath);
            Assert.False(after.PartPresent);
            Assert.False(after.RelPresent);
            Assert.False(after.ContentTypePresent);
            Assert.Contains("fullCalcOnLoad=\"1\"", after.CalcPr);

            // exactly one formula (D34) was replaced; everything else survives
            Assert.Equal(srcFormulas - 1, FormulaCount(outPath, "Input Distances"));
            using var wb = new XLWorkbook(outPath);
            var input = wb.Worksheet("Input Distances");
            Assert.False(input.Cell("D34").HasFormula);
            Assert.Equal(8.35, input.Cell("D34").GetDouble(), 3);
            Assert.True(input.Cell("D33").HasFormula); // neighbouring pedestrian row untouched
            Assert.True(input.Cell("B34").HasFormula); // the clearing-name formula itself is untouched
        }
        finally
        {
            File.Delete(outPath);
        }
    }
}
