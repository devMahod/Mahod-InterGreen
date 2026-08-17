using System;
using System.IO;
using System.Linq;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Xunit;

namespace Mahod.Intergreen.Excel.Tests;

/// <summary>
/// Gate N — the C# reader reads the ORIGINAL workbooks (source evidence, read-only)
/// and must agree with everything the extraction fixtures assert (ED-006 follow-up),
/// closing the Final Hotfix §13 requirement for a fully local, C#-side reproduction.
/// </summary>
public class WorkbookReaderTests
{
    public static string? MaterialsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "materials", "Inter-green Automation");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string Example1Path() => Path.Combine(MaterialsDir()!,
        "03 Example 1", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx");

    private static string Example2Path() => Path.Combine(MaterialsDir()!,
        "04 Example 2", "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx");

    [Fact]
    public void Example1_detects_variant_V1_with_inbar_block()
    {
        Assert.NotNull(MaterialsDir());
        var model = WorkbookReader.Read(Example1Path());
        Assert.Equal(LegacyTemplateVariant.V1PerMovementVehicleLength, model.Variant);
        Assert.Equal(1.2, model.Constants.PedestrianSpeedMps);
        Assert.Equal(1.0, model.Constants.ReactionTimeSec);
        Assert.Equal(3.5, model.Constants.DecelerationMps2);
        Assert.False(model.Constants.InbarMode); // flag is 'n' in the source
        Assert.Equal(12, model.Constants.InbarDefaultVehicleLengthMeters);
        Assert.Equal(40, model.Rows.Count);
    }

    [Fact]
    public void Example2_detects_variant_V2_with_global_length()
    {
        Assert.NotNull(MaterialsDir());
        var model = WorkbookReader.Read(Example2Path());
        Assert.Equal(LegacyTemplateVariant.V2GlobalVehicleLength, model.Variant);
        Assert.Equal(12, model.Constants.GlobalVehicleLengthMeters);
        Assert.Equal(108, model.Rows.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Reader_plus_engine_reproduces_every_cached_final_ig(int example)
    {
        Assert.NotNull(MaterialsDir());
        var model = WorkbookReader.Read(example == 1 ? Example1Path() : Example2Path());
        var calc = new LegacyWorkbookCompatibilityCalculator(model.Constants, model.Variant, model.MovementParameters);

        var mismatches = model.Rows
            .Select(row => (row, result: calc.ComputeRow(row.Input)))
            .Where(x => x.result.FinalIg != (x.row.CachedFinalIg is double d ? (int?)(int)d : null))
            .Select(x => $"conflict {x.row.Input.ConflictNo}: cached {x.row.CachedFinalIg}, got {x.result.FinalIg}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Reader_plus_engine_reproduces_every_cached_intermediate_ig(int example)
    {
        Assert.NotNull(MaterialsDir());
        var model = WorkbookReader.Read(example == 1 ? Example1Path() : Example2Path());
        var calc = new LegacyWorkbookCompatibilityCalculator(model.Constants, model.Variant, model.MovementParameters);

        var mismatches = 0;
        foreach (var row in model.Rows)
        {
            var result = calc.ComputeRow(row.Input);
            for (var i = 0; i < 4; i++)
            {
                var expected = row.CachedPointIgs[i];
                var got = result.PointResults[i]?.IntergreenSec;
                if (expected is null && got is null) continue;
                if (expected is null || got is null || Math.Abs(expected.Value - got.Value) > 1e-8)
                    mismatches++;
            }
        }
        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void Example1_reports_preexisting_ref_errors_in_autoadjusted_sheet()
    {
        // FINDINGS.md F-006: the AutoAdjusted sheet is all #REF! — must surface as
        // SOURCE_WORKBOOK_EXISTING_ERROR, never be hidden or confused with our own output.
        Assert.NotNull(MaterialsDir());
        var model = WorkbookReader.Read(Example1Path());
        Assert.Contains(model.SourceErrors, e => e.Contains("AutoAdjusted"));
    }

    [Fact]
    public void Signal_groups_are_read_as_engineering_data()
    {
        Assert.NotNull(MaterialsDir());
        var model = WorkbookReader.Read(Example2Path());
        Assert.True(model.SignalGroups.Count > 0);
        // conflict 18's movements (N-L → S-L) must have SG mappings — the matrix-blocking
        // acceptance test (Final Hotfix §17A) depends on them
        Assert.True(model.SignalGroups.ContainsKey("N-L"));
        Assert.True(model.SignalGroups.ContainsKey("S-L"));
    }

    [Fact]
    public void Garbage_workbook_is_rejected_with_unsupported_template()
    {
        Assert.NotNull(MaterialsDir());
        // the small "Relevant Pages.xlsx" is a real xlsx that is NOT an IG matrix
        var path = Path.Combine(MaterialsDir()!, "01 Guidelines", "Relevant Pages.xlsx");
        Assert.Throws<UnsupportedTemplateException>(() => WorkbookReader.Read(path));
    }
}
