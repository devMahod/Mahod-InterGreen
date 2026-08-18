using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

public class PathResolverTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("paths");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Fixture(string relative)
    {
        string p = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        // minimal OOXML-looking content: PK header so the malformed probe passes
        File.WriteAllBytes(p, new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4 });
        return p;
    }

    // ---- §4: Lin's EXACT failure, as a permanent regression ----
    // Original failing input: "C:\Users\lins\Desktop\INPUTS\05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx"
    [Fact]
    public void Lin_regression_quoted_and_spaced_variants_resolve_to_same_file()
    {
        string real = Fixture(Path.Combine("INPUTS", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx"));
        string expected = Path.GetFullPath(real); // literal independent expectation

        foreach (string raw in new[]
        {
            real,                        // raw normal path
            $"\"{real}\"",               // surrounded by double quotes — Lin's exact case
            $"  {real}  ",               // leading/trailing spaces
            $"  \"{real}\"  ",           // quoted + spaces
        })
        {
            var r = WorkbookPathResolver.Resolve(raw);
            Assert.True(r.IsOk, $"input <{raw}> => {r.Status}: {r.Detail}");
            Assert.Equal(expected, r.NormalizedPath);
        }
    }

    [Fact]
    public void Quote_in_middle_of_filename_is_not_removed()
    {
        // never "sanitize" arbitrary characters — an inner quote makes the path invalid,
        // and the resolver must not silently rewrite it into another filename
        var r = WorkbookPathResolver.Resolve(Path.Combine(_dir, "a\"b.xlsx"));
        Assert.False(r.IsOk);
    }

    // ---- §5.1 spaces/punctuation + §5.2 Hebrew/Unicode ----
    [Theory]
    [InlineData("folder with spaces/file with  double  spaces.xlsx")]
    [InlineData("hy-phen/under_score/name-with_both (v2).xlsx")]
    [InlineData("brackets [x]/file [final].xlsx")]
    [InlineData("amp & apostrophe's/file & name's.xlsx")]
    [InlineData("תיקייה בעברית/קובץ.xlsx")]
    [InlineData("עברית ו-English/IG_matrix עברית-English-123.xlsx")]
    public void Valid_special_names_resolve(string relative)
    {
        string real = Fixture(relative);
        var r = WorkbookPathResolver.Resolve($"\"{real}\"");
        Assert.True(r.IsOk, $"{r.Status}: {r.Detail}");
        Assert.Equal(Path.GetFullPath(real), r.NormalizedPath);
    }

    // ---- §5.3 path forms ----
    [Fact]
    public void Long_nested_path_resolves()
    {
        string rel = string.Join(Path.DirectorySeparatorChar,
            Enumerable.Repeat("nested folder", 12)) + Path.DirectorySeparatorChar + "workbook.xlsx";
        string real = Fixture(rel);
        Assert.True(real.Length > 200);
        var r = WorkbookPathResolver.Resolve(real);
        Assert.True(r.IsOk, $"{r.Status}: {r.Detail}");
    }

    [Fact]
    public void Nonexistent_unc_path_reports_not_found_without_crashing()
    {
        var r = WorkbookPathResolver.Resolve(@"\\no-such-server-mahod\share\folder\file.xlsx");
        Assert.False(r.IsOk);
        Assert.True(r.Status is WorkbookPathStatus.NotFound or WorkbookPathStatus.FolderNotFound,
            r.Status.ToString());
        Assert.Contains("לא נמצא", r.UserMessageHe);
    }

    [Fact]
    public void Nonexistent_mapped_drive_reports_not_found()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        char free = "PQRSTUVWXYZ".First(c => !used.Contains(c));
        var r = WorkbookPathResolver.Resolve($@"{free}:\intergreen\file.xlsx");
        Assert.False(r.IsOk);
        Assert.True(r.Status is WorkbookPathStatus.NotFound or WorkbookPathStatus.FolderNotFound
            or WorkbookPathStatus.InvalidPath, r.Status.ToString());
    }

    // ---- §5.4 bad paths — each with its expected category + Hebrew message ----
    [Fact]
    public void Nonexistent_file_in_existing_folder() =>
        AssertStatus(Path.Combine(_dir, "missing.xlsx"), WorkbookPathStatus.NotFound);

    [Fact]
    public void Nonexistent_folder() =>
        AssertStatus(Path.Combine(_dir, "no-folder", "missing.xlsx"), WorkbookPathStatus.FolderNotFound);

    [Theory]
    [InlineData("legacy.xls")]
    [InlineData("data.csv")]
    [InlineData("note.txt")]
    public void Unsupported_extensions_rejected(string name)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        AssertStatus(p, WorkbookPathStatus.UnsupportedExtension);
    }

    [Fact]
    public void Directory_instead_of_file()
    {
        string p = Path.Combine(_dir, "iamafolder.xlsx");
        Directory.CreateDirectory(p);
        AssertStatus(p, WorkbookPathStatus.PathIsDirectory);
    }

    [Fact]
    public void Zero_byte_xlsx()
    {
        string p = Path.Combine(_dir, "empty.xlsx");
        File.WriteAllBytes(p, Array.Empty<byte>());
        AssertStatus(p, WorkbookPathStatus.EmptyFile);
    }

    [Fact]
    public void Text_file_renamed_to_xlsx_is_malformed()
    {
        string p = Path.Combine(_dir, "fake.xlsx");
        File.WriteAllText(p, "this is not a workbook at all");
        AssertStatus(p, WorkbookPathStatus.MalformedWorkbook);
    }

    [Fact]
    public void Empty_input_rejected()
    {
        Assert.Equal(WorkbookPathStatus.Empty, WorkbookPathResolver.Resolve("").Status);
        Assert.Equal(WorkbookPathStatus.Empty, WorkbookPathResolver.Resolve("  \"\"  ").Status);
        Assert.Equal(WorkbookPathStatus.Empty, WorkbookPathResolver.Resolve(null).Status);
    }

    [Fact]
    public void Locked_file_reports_locked_not_crash()
    {
        string p = Fixture("locked.xlsx");
        using var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var r = WorkbookPathResolver.Resolve(p);
        Assert.Equal(WorkbookPathStatus.Locked, r.Status);
        Assert.Contains("נעול", r.UserMessageHe);
    }

    [Fact]
    public void No_raw_paths_only_messages_with_guidance()
    {
        var r = WorkbookPathResolver.Resolve(Path.Combine(_dir, "missing.xlsx"));
        // message tells the user what to DO, not a stack trace
        Assert.DoesNotContain("Exception", r.UserMessageHe);
        Assert.Contains("בחרי", r.UserMessageHe);
    }

    private void AssertStatus(string path, WorkbookPathStatus expected)
    {
        var r = WorkbookPathResolver.Resolve(path);
        Assert.Equal(expected, r.Status);
        Assert.False(string.IsNullOrWhiteSpace(r.UserMessageHe));
    }
}
