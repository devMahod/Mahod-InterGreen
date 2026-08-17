using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
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
}
