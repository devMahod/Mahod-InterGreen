using System.Text.Json;
using ClosedXML.Excel;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

public class WorkbookInputTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("wb");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Original_example1_accepted_with_model()
    {
        var r = WorkbookAcceptance.Validate(TestPaths.Example1Workbook);
        Assert.True(r.IsOk, r.Detail);
        Assert.NotNull(r.Model);
    }

    [Fact]
    public void Original_example2_accepted()
        => Assert.True(WorkbookAcceptance.Validate(TestPaths.Example2Workbook).IsOk);

    [Fact]
    public void Copied_renamed_hebrew_workbook_accepted()
    {
        string p = Path.Combine(_dir, "עותק בדיקה של IG_matrix.xlsx");
        File.Copy(TestPaths.Example1Workbook, p);
        var r = WorkbookAcceptance.Validate($"\"{p}\"");
        Assert.True(r.IsOk, r.Detail);
    }

    [Fact]
    public void ReadOnly_source_workbook_is_fine()
    {
        string p = Path.Combine(_dir, "readonly.xlsx");
        File.Copy(TestPaths.Example1Workbook, p);
        File.SetAttributes(p, FileAttributes.ReadOnly);
        try
        {
            Assert.True(WorkbookAcceptance.Validate(p).IsOk);
        }
        finally { File.SetAttributes(p, FileAttributes.Normal); }
    }

    [Fact]
    public void Workbook_missing_required_sheet_is_unsupported_template()
    {
        string p = Path.Combine(_dir, "no-input-distances.xlsx");
        File.Copy(TestPaths.Example1Workbook, p);
        using (var wb = new XLWorkbook(p))
        {
            wb.Worksheet("Input Distances").Delete();
            wb.Save();
        }
        var r = WorkbookAcceptance.Validate(p);
        Assert.False(r.IsOk);
        Assert.Contains("UNSUPPORTED_TEMPLATE_VERSION", r.UserMessageHe);
    }

    [Fact]
    public void Unrelated_xlsx_is_unsupported_template_not_crash()
    {
        string p = Path.Combine(_dir, "unrelated.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("Sheet1").Cell(1, 1).Value = "hello";
            wb.SaveAs(p);
        }
        var r = WorkbookAcceptance.Validate(p);
        Assert.False(r.IsOk);
        Assert.True(r.Status is WorkbookAcceptStatus.UnsupportedTemplate or WorkbookAcceptStatus.UnreadableWorkbook);
        Assert.DoesNotContain("StackTrace", r.UserMessageHe);
    }

    [Fact]
    public void Corrupt_zip_reports_path_rejected_or_unreadable()
    {
        string p = Path.Combine(_dir, "corrupt.xlsx");
        var bytes = File.ReadAllBytes(TestPaths.Example1Workbook);
        for (int i = 100; i < 4000; i++) bytes[i] = 0; // wreck the archive, keep PK header
        File.WriteAllBytes(p, bytes);
        var r = WorkbookAcceptance.Validate(p);
        Assert.False(r.IsOk);
    }
}

public class SidecarStoreTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("sidecar");
    private string ScPath => Path.Combine(_dir, "drawing.intergreen-project.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string MakeWorkbook(string name = "wb.xlsx")
    {
        string p = Path.Combine(_dir, name);
        File.Copy(TestPaths.Example1Workbook, p, overwrite: true);
        return p;
    }

    [Fact]
    public void First_run_no_sidecar()
        => Assert.Equal(SidecarLoadStatus.NoSidecar, SidecarStore.Load(ScPath).Status);

    [Fact]
    public void Commit_then_load_roundtrip_with_schema_version()
    {
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, "workbook", MakeWorkbook());
        SidecarStore.Commit(ScPath, data);
        var loaded = SidecarStore.Load(ScPath);
        Assert.Equal(SidecarLoadStatus.Ok, loaded.Status);
        Assert.Equal(SidecarStore.SchemaVersion, loaded.Data["schemaVersion"].GetInt32());
        var wb = SidecarStore.ResolveWorkbook(loaded.Data, _dir);
        Assert.Equal(WorkbookRefStatus.Ok, wb.Status);
    }

    [Fact]
    public void Corrupt_json_recovers_with_backup_and_no_crash()
    {
        File.WriteAllText(ScPath, "{ this is ] not json");
        var loaded = SidecarStore.Load(ScPath);
        Assert.Equal(SidecarLoadStatus.Recovered, loaded.Status);
        Assert.NotNull(loaded.RecoveryBackupPath);
        Assert.True(File.Exists(loaded.RecoveryBackupPath));
        Assert.Contains("Setup", loaded.UserMessageHe);
        Assert.Empty(loaded.Data);
    }

    [Fact]
    public void Legacy_quoted_workbook_path_is_normalized_on_resolve()
    {
        string wb = MakeWorkbook();
        File.WriteAllText(ScPath, JsonSerializer.Serialize(new { workbook = $"\"{wb}\"" }));
        var loaded = SidecarStore.Load(ScPath);
        var r = SidecarStore.ResolveWorkbook(loaded.Data, _dir);
        Assert.Equal(WorkbookRefStatus.Ok, r.Status);
        Assert.Equal(Path.GetFullPath(wb), r.Path);
    }

    [Fact]
    public void Relative_workbook_path_resolves_against_drawing_folder()
    {
        MakeWorkbook("near.xlsx");
        File.WriteAllText(ScPath, JsonSerializer.Serialize(new { workbook = "near.xlsx" }));
        var r = SidecarStore.ResolveWorkbook(SidecarStore.Load(ScPath).Data, _dir);
        Assert.Equal(WorkbookRefStatus.Ok, r.Status);
    }

    [Fact]
    public void Missing_workbook_is_reported_not_silently_replaced()
    {
        File.WriteAllText(ScPath, JsonSerializer.Serialize(new { workbook = Path.Combine(_dir, "gone.xlsx") }));
        var r = SidecarStore.ResolveWorkbook(SidecarStore.Load(ScPath).Data, _dir);
        Assert.Equal(WorkbookRefStatus.Missing, r.Status);
        Assert.Contains("לא נמצא", r.UserMessageHe);
    }

    [Fact]
    public void Moved_workbook_with_same_name_next_to_drawing_is_candidate_not_auto_used()
    {
        // sidecar points into a folder that no longer exists; same filename sits next to DWG
        MakeWorkbook("moved.xlsx");
        File.WriteAllText(ScPath, JsonSerializer.Serialize(
            new { workbook = Path.Combine(_dir, "old-location", "moved.xlsx") }));
        var r = SidecarStore.ResolveWorkbook(SidecarStore.Load(ScPath).Data, _dir);
        Assert.Equal(WorkbookRefStatus.CandidateNextToDrawing, r.Status);
        Assert.Contains("Setup", r.UserMessageHe); // user must confirm through Setup
    }

    [Fact]
    public void Failed_new_setup_preserves_previous_valid_config()
    {
        // valid old config
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, "workbook", MakeWorkbook());
        SidecarStore.Set(data, "unitsConfirmed", "meters");
        SidecarStore.Commit(ScPath, data);
        byte[] before = File.ReadAllBytes(ScPath);

        // "new Setup" flow: user picks an invalid workbook — acceptance fails,
        // so the caller never commits. Simulate exactly that policy:
        var bad = WorkbookAcceptance.Validate(Path.Combine(_dir, "does-not-exist.xlsx"));
        Assert.False(bad.IsOk);
        // (no Commit call — the transactional contract)

        Assert.Equal(before, File.ReadAllBytes(ScPath)); // old config byte-identical
        var again = SidecarStore.ResolveWorkbook(SidecarStore.Load(ScPath).Data, _dir);
        Assert.Equal(WorkbookRefStatus.Ok, again.Status);
    }

    [Fact]
    public void Unversioned_old_sidecar_gets_version_stamped_on_next_commit_without_inventing_values()
    {
        File.WriteAllText(ScPath, JsonSerializer.Serialize(new { workbook = MakeWorkbook() }));
        var loaded = SidecarStore.Load(ScPath);
        Assert.False(loaded.Data.ContainsKey("schemaVersion"));
        SidecarStore.Commit(ScPath, loaded.Data);
        var re = SidecarStore.Load(ScPath);
        Assert.Equal(SidecarStore.SchemaVersion, re.Data["schemaVersion"].GetInt32());
        // nothing else appeared
        Assert.Equal(2, re.Data.Count); // workbook + schemaVersion
    }

    [Fact]
    public void Atomic_commit_keeps_bak_of_previous_version()
    {
        var d1 = new Dictionary<string, JsonElement>();
        SidecarStore.Set(d1, "workbook", MakeWorkbook());
        SidecarStore.Commit(ScPath, d1);
        var d2 = SidecarStore.Load(ScPath).Data;
        SidecarStore.Set(d2, "unitsConfirmed", "meters");
        SidecarStore.Commit(ScPath, d2);
        Assert.True(File.Exists(ScPath + ".bak"));
    }
}
