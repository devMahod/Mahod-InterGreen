using System.Security.Cryptography;
using System.Text.Json;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// r6: manual full-path entry must be a first-class INPUT METHOD only — never a second
/// implementation. Every case below drives the SAME production pipeline the file picker
/// uses (SetupService.Validate -> Commit).
/// </summary>
public class ManualPathEntryTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("manual");
    private string Sc => Path.Combine(_dir, "drawing.intergreen-project.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string CopyWorkbook(string relative)
    {
        string p = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.Copy(TestPaths.Example1Workbook, p, overwrite: true);
        return p;
    }

    private static string Sha(string p)
    {
        using var s = File.OpenRead(p);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    // ---- the exact Lin regression, entered MANUALLY (Windows "Copy as path" form) ----
    [Fact]
    public void Lin_quoted_manual_entry_resolves_like_browse()
    {
        string real = CopyWorkbook(Path.Combine("TEST_KIT", "EXAMPLE_1", "INPUTS",
            "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx"));
        var browse = SetupService.Validate(real);                 // what the picker returns
        var manual = SetupService.Validate($"  \"{real}\"  ");    // pasted, quoted, padded
        Assert.True(browse.IsOk && manual.IsOk);
        Assert.Equal(browse.NormalizedPath, manual.NormalizedPath);
        Assert.Equal(Path.GetFullPath(real), manual.NormalizedPath);
    }

    // ---- REQUIRED PARITY: browse-selected == manually-entered, end to end ----
    [Fact]
    public void Browse_and_manual_entry_produce_identical_setup_state()
    {
        string wb = CopyWorkbook(Path.Combine("קלט של הפרויקט", "IG_matrix פינס 2026.xlsx"));
        string scBrowse = Path.Combine(_dir, "browse.intergreen-project.json");
        string scManual = Path.Combine(_dir, "manual.intergreen-project.json");

        var b = SetupService.Validate(wb);
        var m = SetupService.Validate($"\t \"{wb}\" ");
        Assert.True(b.IsOk, b.Detail);
        Assert.True(m.IsOk, m.Detail);

        // normalized path + workbook identity
        Assert.Equal(b.NormalizedPath, m.NormalizedPath);
        Assert.Equal(Sha(b.NormalizedPath!), Sha(m.NormalizedPath!));
        // detected template + loaded model
        Assert.Equal(b.Model!.Variant, m.Model!.Variant);
        Assert.Equal(b.Model.SignalGroups, m.Model.SignalGroups);
        Assert.Equal(b.Model.PedestrianWidths, m.Model.PedestrianWidths);
        Assert.Equal(b.Model.MovementParameters.Keys.OrderBy(k => k),
                     m.Model.MovementParameters.Keys.OrderBy(k => k));
        // setup state + sidecar contents
        var cb = SetupService.Commit(b, scBrowse, _dir);
        var cm = SetupService.Commit(m, scManual, _dir);
        Assert.True(cb.Committed && cm.Committed);
        Assert.Equal(cb.ReloadRef, cm.ReloadRef);
        Assert.Equal(File.ReadAllText(scBrowse), File.ReadAllText(scManual));
    }

    // ---- manual-entry input matrix (all must behave exactly as via the picker) ----
    [Theory]
    [InlineData("plain.xlsx")]
    [InlineData("with spaces/two  spaces file.xlsx")]
    [InlineData("תיקייה בעברית/קובץ עברית-English 12.xlsx")]
    [InlineData("mixed (v2) [final] & co's/wb.xlsx")]
    public void Manual_valid_paths_accepted(string relative)
    {
        string wb = CopyWorkbook(relative);
        foreach (string raw in new[] { wb, $"\"{wb}\"", $"   {wb}   ", $" \"{wb}\" " })
        {
            var r = SetupService.Validate(raw);
            Assert.True(r.IsOk, $"<{raw}> => {r.Status}: {r.Detail}");
            Assert.Equal(Path.GetFullPath(wb), r.NormalizedPath);
        }
    }

    [Fact]
    public void Manual_unc_and_mapped_drive_report_not_found_not_crash()
    {
        foreach (string raw in new[] { @"\no-such-host-mahod\share\ig\wb.xlsx",
                                       "\"" + @"\no-such-host-mahod\share\ig\wb.xlsx" + "\"" })
        {
            var r = SetupService.Validate(raw);
            Assert.False(r.IsOk);
            Assert.Equal(WorkbookAcceptStatus.PathRejected, r.Status);
        }
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        char free = "PQRSTUVWXYZ".First(c => !used.Contains(c));
        var mapped = SetupService.Validate($@"{free}:\intergreen\wb.xlsx");
        Assert.False(mapped.IsOk);
        Assert.False(string.IsNullOrWhiteSpace(mapped.UserMessageHe));
    }

    [Fact]
    public void Manual_invalid_inputs_rejected_with_guidance()
    {
        Assert.False(SetupService.Validate(Path.Combine(_dir, "missing.xlsx")).IsOk);   // nonexistent
        Assert.False(SetupService.Validate(_dir).IsOk);                                  // folder, not file
        string csv = Path.Combine(_dir, "data.csv"); File.WriteAllText(csv, "a,b");
        Assert.False(SetupService.Validate(csv).IsOk);                                   // unsupported ext
        Assert.False(SetupService.Validate("   ").IsOk);                                 // cleared
        Assert.False(SetupService.Validate(null).IsOk);                                  // cancelled
        foreach (var raw in new[] { Path.Combine(_dir, "missing.xlsx"), csv })
            Assert.DoesNotContain("Exception", SetupService.Validate(raw).UserMessageHe);
    }

    // ---- changing to an invalid path must preserve the previous valid Setup ----
    [Fact]
    public void Invalid_manual_change_preserves_previous_valid_setup()
    {
        string good = CopyWorkbook("good.xlsx");
        var ok = SetupService.Validate(good);
        Assert.True(SetupService.Commit(ok, Sc, _dir).Committed);
        byte[] before = File.ReadAllBytes(Sc);

        foreach (string bad in new[] { Path.Combine(_dir, "gone.xlsx"), _dir,
                                       Path.Combine(_dir, "data.csv"), "   " })
        {
            var r = SetupService.Validate(bad);
            Assert.False(r.IsOk);
            var c = SetupService.Commit(r, Sc, _dir);   // production refuses to commit
            Assert.False(c.Committed);
        }
        Assert.Equal(before, File.ReadAllBytes(Sc));     // byte-identical
        var still = SidecarStore.ResolveWorkbook(SidecarStore.Load(Sc).Data, _dir);
        Assert.Equal(WorkbookRefStatus.Ok, still.Status);
        Assert.Equal(Path.GetFullPath(good), still.Path);
    }
}

/// <summary>r7 regression: rule/resource resolution must be CWD-independent.</summary>
public class RuntimeRootsTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("roots");
    private readonly string _savedCwd = Directory.GetCurrentDirectory();
    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Rules_resolve_from_anchor_even_when_cwd_has_no_rules_anywhere()
    {
        // simulate the Autodesk GUI: CWD is a foreign directory tree with NO rules/
        string autodeskLike = Path.Combine(_dir, "Program Files", "Autodesk", "AutoCAD 2027");
        Directory.CreateDirectory(autodeskLike);
        Directory.SetCurrentDirectory(autodeskLike);

        // installed bundle layout: <plugin>\rules\legacy-mahod-v1\manifest.json
        string plugin = Path.Combine(_dir, "bundle", "Contents", "2027");
        Directory.CreateDirectory(Path.Combine(plugin, "rules", "legacy-mahod-v1"));
        File.Copy(Path.Combine(TestPaths.RulesLegacy, "manifest.json"),
                  Path.Combine(plugin, "rules", "legacy-mahod-v1", "manifest.json"));

        string root = RuntimeRoots.RulesRoot(plugin);
        Assert.Equal(Path.Combine(plugin, "rules"), root);
    }

    [Fact]
    public void Missing_rules_fail_with_exact_installed_path_not_cwd_walk()
    {
        Directory.SetCurrentDirectory(_dir); // rules-less CWD
        string plugin = Path.Combine(_dir, "empty-bundle");
        Directory.CreateDirectory(plugin);
        var ex = Assert.Throws<DirectoryNotFoundException>(() => RuntimeRoots.RulesRoot(plugin));
        Assert.Contains(Path.Combine(plugin, "rules"), ex.Message); // names the exact path
        Assert.Contains("המתקין", ex.Message);                       // actionable guidance
    }

    [Fact]
    public void Fallback_output_dir_is_fixed_per_user_location_not_cwd()
    {
        Directory.SetCurrentDirectory(_dir);
        string p = RuntimeRoots.FallbackOutputDir();
        Assert.True(Directory.Exists(p));
        Assert.DoesNotContain(_dir, p);
        Assert.Contains(Path.Combine("Mahod", "MahodIntergreen"), p);
    }
}

/// <summary>r7 regression: hashing must work on a file the host holds locked
/// (AutoCAD denies default sharing on the active DWG).</summary>
public class OpenFileHashTests
{
    [Fact]
    public void Hashes_file_opened_by_another_handle_with_no_read_share()
    {
        string p = Path.Combine(TestPaths.NewTempDir("lockhash"), "held.dwg");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4, 5 });
        // hold the file the hostile way: writable handle, allow ReadWrite share only
        // (File.OpenRead's default FileShare.Read is refused against this handle)
        using var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Throws<IOException>(() => File.OpenRead(p).Dispose());
        string h = RuntimeRoots.Sha256OfOpenFile(p);
        Assert.Equal("74F81FE167D99B4CB41D6D0CCDA82278CAEE9F3E2F25D5E5A3936FF3DCEC60D0", h);
    }
}
