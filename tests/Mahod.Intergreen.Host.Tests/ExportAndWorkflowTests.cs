using System.Security.Cryptography;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

public class ExportPlannerTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("export");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Src()
    {
        string p = Path.Combine(_dir, "src", "05_PINES IG_matrix.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.Copy(TestPaths.Example1Workbook, p, overwrite: true);
        return p;
    }

    [Fact]
    public void Normal_destination_ok_with_expected_name()
    {
        var plan = ExportPlanner.Plan(Src(), Path.Combine(_dir, "out"));
        Assert.Equal(ExportPlanStatus.Ok, plan.Status);
        Assert.EndsWith("05_PINES IG_matrix_MAHOD_INTERGREEN.xlsx", plan.DestinationPath);
    }

    [Fact]
    public void Destination_folder_is_created_when_missing()
    {
        string dest = Path.Combine(_dir, "brand", "new", "folder");
        var plan = ExportPlanner.Plan(Src(), dest);
        Assert.Equal(ExportPlanStatus.Ok, plan.Status);
        Assert.True(Directory.Exists(dest));
    }

    [Fact]
    public void Hebrew_and_spaces_in_destination_ok()
    {
        var plan = ExportPlanner.Plan(Src(), Path.Combine(_dir, "תוצאות ייצוא", "גרסה 1"));
        Assert.Equal(ExportPlanStatus.Ok, plan.Status);
    }

    [Fact]
    public void Existing_destination_needs_explicit_confirm_with_unique_fallback()
    {
        string src = Src();
        var p1 = ExportPlanner.Plan(src, _dir);
        File.WriteAllText(p1.DestinationPath!, "existing");
        var p2 = ExportPlanner.Plan(src, _dir);
        Assert.Equal(ExportPlanStatus.NeedsConfirmOverwrite, p2.Status);
        Assert.NotNull(p2.UniqueAlternativePath);
        Assert.NotEqual(p2.DestinationPath, p2.UniqueAlternativePath);
        Assert.False(File.Exists(p2.UniqueAlternativePath));
    }

    [Fact]
    public void Source_equals_destination_refused()
    {
        string src = Src();
        // craft a source that already carries the export suffix, exported into its own folder
        string tricky = Path.Combine(Path.GetDirectoryName(src)!, "X_MAHOD_INTERGREEN.xlsx");
        File.Copy(src, tricky);
        var plan = ExportPlanner.Plan(tricky, Path.GetDirectoryName(tricky)!, baseName: "X");
        Assert.Equal(ExportPlanStatus.RefusedSourceEqualsDestination, plan.Status);
        Assert.Contains("המקור", plan.UserMessageHe);
    }

    [Fact]
    public void Real_export_never_touches_source_and_output_opens()
    {
        string src = Src();
        string before = Sha(src);
        var geometry = GoldenPipeline.Run(); // shared fixture (Example 1 golden)
        var plan = ExportPlanner.Plan(src, Path.Combine(_dir, "out"));
        var result = Mahod.Intergreen.Excel.WorkbookWriter.Export(
            src, plan.DestinationPath!, geometry.Analysis, geometry.Model);
        Assert.Empty(result.StructuralIssues);
        Assert.Equal(before, Sha(src));                       // source hash unchanged
        using var wb = new ClosedXML.Excel.XLWorkbook(plan.DestinationPath!); // opens
        Assert.Contains(wb.Worksheets, s => s.Name == "MAHOD Engine Results");
    }

    private static string Sha(string p)
    {
        using var s = File.OpenRead(p);
        return Convert.ToHexString(SHA256.HashData(s));
    }
}

public class WorkflowStateMachineTests
{
    [Fact]
    public void Validate_and_analyze_blocked_before_setup()
    {
        var m = new WorkflowStateMachine();
        Assert.NotNull(m.Gate(WorkflowAction.Validate));
        Assert.NotNull(m.Gate(WorkflowAction.Analyze));
        Assert.Contains("Setup", m.Gate(WorkflowAction.Analyze));
    }

    [Fact]
    public void Show_blocked_before_analyze_and_without_selection()
    {
        var m = new WorkflowStateMachine();
        m.OnSetupCommitted();
        Assert.NotNull(m.Gate(WorkflowAction.Show));      // before analyze
        m.OnAnalyzeSucceeded();
        Assert.NotNull(m.Gate(WorkflowAction.Show));      // nothing selected
        m.HasSelection = true;
        Assert.Null(m.Gate(WorkflowAction.Show));
    }

    [Fact]
    public void Export_blocked_before_analyze()
    {
        var m = new WorkflowStateMachine();
        m.OnSetupCommitted();
        Assert.NotNull(m.Gate(WorkflowAction.Export));
        m.OnAnalyzeSucceeded();
        Assert.Null(m.Gate(WorkflowAction.Export));
    }

    [Fact]
    public void Setup_and_clearqa_always_allowed()
    {
        var m = new WorkflowStateMachine();
        Assert.Null(m.Gate(WorkflowAction.Setup));
        Assert.Null(m.Gate(WorkflowAction.ClearQa));
    }

    [Fact]
    public void New_setup_invalidates_stale_results()
    {
        var m = new WorkflowStateMachine();
        m.OnSetupCommitted();
        m.OnAnalyzeSucceeded();
        m.HasSelection = true;
        m.OnSetupCommitted(); // Setup again after Analyze
        Assert.False(m.Analyzed);          // stale results never shown as current
        Assert.False(m.HasSelection);
        Assert.NotNull(m.Gate(WorkflowAction.Export));
    }

    [Fact]
    public void Repeated_and_out_of_order_clicks_never_throw()
    {
        var m = new WorkflowStateMachine();
        foreach (var a in new[] { WorkflowAction.Export, WorkflowAction.Show, WorkflowAction.Analyze,
                                  WorkflowAction.Validate, WorkflowAction.ClearQa, WorkflowAction.Export })
            _ = m.Gate(a); // messages, not exceptions
        m.OnProjectLoadedFromSidecar();
        Assert.Null(m.Gate(WorkflowAction.Validate)); // reopen flow: Validate without new Setup
    }
}
