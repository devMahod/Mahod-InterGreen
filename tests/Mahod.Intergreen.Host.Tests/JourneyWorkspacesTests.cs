using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>Shared Example-1 golden pipeline runner (engineering values are LOCKED —
/// UX tests must reproduce them, never adapt them).</summary>
public static class GoldenPipeline
{
    public sealed record Result(AnalysisDocument Analysis, WorkbookModel Model, int Movements);

    public static Result Run(string? workbookPath = null)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestPaths.Example1Geometry));
        var curvesByLayer = new Dictionary<string, List<(string Handle, PolyCurve2D Curve)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.RootElement.GetProperty("curves").EnumerateArray())
        {
            string layer = c.GetProperty("layer").GetString()!;
            if (!layer.StartsWith(ProjectAssembly.LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var curve = PolyCurve2D.FromJson(c.GetProperty("geometry").GetRawText());
            if (!curvesByLayer.TryGetValue(layer, out var list)) curvesByLayer[layer] = list = new();
            list.Add((c.GetProperty("handle").GetString()!, curve));
        }
        var model = WorkbookReader.Read(workbookPath ?? TestPaths.Example1Workbook);
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(curvesByLayer,
            model.SignalGroups, model.PedestrianWidths, ProjectSidecar.Empty, findings);
        var pack = RulePackLoader.Load(TestPaths.RulesLegacy);
        var output = AnalysisPipeline.Run(new PipelineInput(
            "EXAMPLE1", "ex1.iggeometry.json", AnalysisWriters.Sha256OfFile(TestPaths.Example1Geometry),
            pack, new ProjectClassification(), model.Constants, model.Variant,
            model.MovementParameters, movements));
        return new(output.Analysis, model, movements.Count);
    }

    public static void AssertGolden(Result r)
    {
        Assert.Equal(11, r.Movements);
        Assert.Equal(50, r.Analysis.Conflicts.Count);
        Assert.Equal(24, r.Analysis.Matrix.Count(m => m.Status == "VALID"));
        Assert.Equal(0, r.Analysis.Matrix.Count(m => m.Status == "REVIEW_REQUIRED"));
        Assert.Equal(0, r.Analysis.Matrix.Count(m => m.Status == "BLOCKED"));
        var row82 = r.Analysis.Conflicts.First(c => c.Clearing == "W-L" && c.Entering == "S-T");
        Assert.Equal(5, row82.FinalIg);
    }
}

/// <summary>§17 real-data workspace simulations: the same golden result must come out of
/// user-realistic workspaces (spaces, Hebrew, quoted pasted paths) via the HOST services
/// (resolver → acceptance → pipeline).</summary>
public class JourneyWorkspacesTests : IDisposable
{
    private readonly List<string> _dirs = new();
    public void Dispose() { foreach (var d in _dirs) try { Directory.Delete(d, true); } catch { } }

    private string Workspace(string name)
    {
        string p = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(p);
        _dirs.Add(p);
        return p;
    }

    private void RunWorkspace(string wsRoot, string subdir, string wbName, bool quoted)
    {
        string dir = Path.Combine(wsRoot, subdir);
        Directory.CreateDirectory(dir);
        string wb = Path.Combine(dir, wbName);
        File.Copy(TestPaths.Example1Workbook, wb);

        string rawInput = quoted ? $"\"{wb}\"" : wb;
        var accept = WorkbookAcceptance.Validate(rawInput);
        Assert.True(accept.IsOk, accept.Detail);
        var result = GoldenPipeline.Run(accept.NormalizedPath);
        GoldenPipeline.AssertGolden(result);
    }

    [Fact] // Workspace A — simple
    public void Workspace_A_simple()
        => RunWorkspace(Workspace("Intergreen-Test"), "INPUTS", "wb.xlsx", quoted: false);

    [Fact] // Workspace B — spaces
    public void Workspace_B_spaces()
        => RunWorkspace(Workspace("Intergreen Test B"), "nested folder with spaces",
            "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx", quoted: false);

    [Fact] // Workspace C — Hebrew
    public void Workspace_C_hebrew()
        => RunWorkspace(Workspace("בדיקת-אינטרגרין"), "קבצי קלט",
            "IG_matrix פינס-בעל שם טוב 2026.xlsx", quoted: false);

    [Fact] // Workspace D — Lin's quoted-paste style
    public void Workspace_D_quoted_paste()
        => RunWorkspace(Workspace("Intergreen Test D"), "INPUTS",
            "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx", quoted: true);

    [Fact] // reopen flow: sidecar written in workspace, adopted, pipeline reruns
    public void Workspace_reopen_with_sidecar_persistence()
    {
        string ws = Workspace("Intergreen Reopen");
        string wb = Path.Combine(ws, "wb.xlsx");
        File.Copy(TestPaths.Example1Workbook, wb);
        string sc = Path.Combine(ws, "drawing.intergreen-project.json");

        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, "workbook", wb);
        SidecarStore.Commit(sc, data);

        // "reopen": load + resolve + run
        var loaded = SidecarStore.Load(sc);
        Assert.Equal(SidecarLoadStatus.Ok, loaded.Status);
        var wbRef = SidecarStore.ResolveWorkbook(loaded.Data, ws);
        Assert.Equal(WorkbookRefStatus.Ok, wbRef.Status);
        GoldenPipeline.AssertGolden(GoldenPipeline.Run(wbRef.Path));
    }
}
