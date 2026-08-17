using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Mahod.Intergreen.AutoCAD2026;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;

[assembly: CommandClass(typeof(IgWorkflowCommands))]

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// The Pilot workflow (Directive §43–§46): one professional entry point — INTERGREEN —
/// backed by a WPF palette: Project → Setup → Validate → Analyze → Review → Export Excel.
/// This host contains NO engineering formulas; every number comes from the shared
/// deterministic pipeline (AnalysisPipeline / ProjectAssembly).
/// </summary>
public class IgWorkflowCommands
{
    private static PaletteSet? _palette;
    private static ListView? _list;
    private static TextBlock? _status;
    private static PipelineOutput? _lastOutput;
    private static WorkbookModel? _lastModel;
    private static string? _workbookPath;
    private static readonly List<Drawable> _transients = new();

    [CommandMethod("INTERGREEN", CommandFlags.Modal)]
    public void Intergreen()
    {
        if (_palette is null)
        {
            _palette = new PaletteSet("MAHOD INTERGREEN",
                new Guid("7A1C4E2B-9D3F-4B4E-A2F3-6C1D2E3F4A5B"));
            var panel = BuildPanel();
            _palette.AddVisual("Pilot", panel);
            _palette.MinimumSize = new System.Drawing.Size(420, 480);
        }
        _palette.Visible = true;
        SetStatus("Open a drawing, then: Setup → Validate → Analyze → Review → Export Excel.");
    }

    private static StackPanel BuildPanel()
    {
        var root = new StackPanel { Margin = new Thickness(8) };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(new TextBlock
        {
            Text = "MAHOD INTERGREEN — Pilot",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 6),
        });
        root.Children.Add(_status);

        var buttons = new WrapPanel();
        void Add(string label, RoutedEventHandler onClick)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4) };
            b.Click += onClick;
            buttons.Children.Add(b);
        }
        Add("1. Setup", (_, _) => Guard(Setup));
        Add("2. Validate", (_, _) => Guard(() => RunPipeline(analyzeOnly: false)));
        Add("3. Analyze", (_, _) => Guard(() => RunPipeline(analyzeOnly: true)));
        Add("4. Show in drawing", (_, _) => Guard(ShowSelected));
        Add("5. Export Excel", (_, _) => Guard(ExportExcel));
        Add("Clear QA", (_, _) => Guard(ClearTransients));
        root.Children.Add(buttons);

        _list = new ListView { Height = 330 };
        var gv = new GridView();
        void Col(string h, string p, double w)
            => gv.Columns.Add(new GridViewColumn { Header = h, Width = w, DisplayMemberBinding = new System.Windows.Data.Binding(p) });
        Col("Conflict", "Id", 110);
        Col("Status", "Status", 90);
        Col("Pts", "Pts", 36);
        Col("CD", "Cd", 55);
        Col("ED", "Ed", 55);
        Col("IG", "Ig", 40);
        _list.View = gv;
        root.Children.Add(_list);
        return root;
    }

    private static void Guard(Action a)
    {
        try { a(); }
        catch (System.Exception ex) { SetStatus("ERROR: " + ex.Message); }
    }

    private static void SetStatus(string s)
    {
        if (_status is not null) _status.Text = s;
        AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[INTERGREEN] " + s);
    }

    private static string SidecarPath(Database db)
        => Path.ChangeExtension(db.Filename, null) + ".intergreen-project.json";

    // ---- 1. Setup (Directive §44): resolve only what automation cannot ----
    private static void Setup()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("open a drawing first");
        var ed = doc.Editor;
        var db = doc.Database;

        var scPath = SidecarPath(db);
        var sidecar = File.Exists(scPath)
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(scPath)) ?? new()
            : new Dictionary<string, object>();

        // workbook selection
        var wbPrompt = new PromptStringOptions("\nProject workbook path (.xlsx)")
        { AllowSpaces = true, DefaultValue = sidecar.TryGetValue("workbook", out var w) ? w.ToString()! : "" };
        var wbRes = ed.GetString(wbPrompt);
        if (wbRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(wbRes.StringResult))
            throw new InvalidOperationException("Setup needs the project workbook");
        if (!File.Exists(wbRes.StringResult))
            throw new FileNotFoundException(wbRes.StringResult);
        sidecar["workbook"] = wbRes.StringResult;
        _workbookPath = wbRes.StringResult;

        // units confirmation (Directive §13) — only when the drawing cannot say
        var (unitsName, toMeters) = GeometryExtraction.Units(db);
        if (toMeters is null)
        {
            var keep = ed.GetKeywords(
                $"\nDrawing INSUNITS is '{unitsName}'. Confirm the drawing unit", "Meters Abort");
            if (keep.Status != PromptStatus.OK || keep.StringResult != "Meters")
                throw new InvalidOperationException("analysis stays BLOCKED until the unit is confirmed (Directive §13)");
            sidecar["unitsConfirmed"] = "meters";
            sidecar["unitsConfirmedBy"] = Environment.UserName + " via INTERGREEN Setup";
        }

        File.WriteAllText(scPath, JsonSerializer.Serialize(sidecar,
            new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        SetStatus($"Setup saved → {Path.GetFileName(scPath)}. Now Validate.");
    }

    // ---- 2/3. Validate + Analyze — the SAME shared pipeline the CLI runs ----
    private static void RunPipeline(bool analyzeOnly)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("open a drawing first");
        var db = doc.Database;
        var scPath = SidecarPath(db);
        var sidecar = ProjectSidecar.Load(scPath);

        _workbookPath ??= File.Exists(scPath)
            ? JsonDocument.Parse(File.ReadAllText(scPath)).RootElement
                .TryGetProperty("workbook", out var wbEl) ? wbEl.GetString() : null
            : null;
        if (_workbookPath is null || !File.Exists(_workbookPath))
            throw new InvalidOperationException("run Setup first (workbook not selected)");

        var (unitsName, toMeters) = GeometryExtraction.Units(db);
        var findings = new List<ValidationFinding>();
        double scale;
        if (toMeters is double f) scale = f;
        else if (string.Equals(sidecar.UnitsConfirmed, "meters", StringComparison.OrdinalIgnoreCase))
        {
            scale = 1.0;
            findings.Add(new ValidationFinding("IG-UNIT-001", Severity.Warning, null,
                $"INSUNITS '{unitsName}' confirmed as meters by {sidecar.UnitsConfirmedBy ?? "project setup"}.",
                SourceReference: "Directive §13"));
        }
        else throw new InvalidOperationException($"INSUNITS '{unitsName}' unresolved — run Setup (Directive §13)");

        ExtractionResult extraction;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            extraction = GeometryExtraction.ExtractLayers(db, tr,
                layer => layer.StartsWith(ProjectAssembly.LayerPrefix, StringComparison.OrdinalIgnoreCase));
            tr.Commit();
        }
        foreach (var u in extraction.Unsupported)
            findings.Add(new ValidationFinding("IG-GEO-010", Severity.Warning, u.Layer,
                $"UNSUPPORTED entity {u.EntityType} ({u.Handle}): {u.Reason}", SourceReference: "Directive §7"));

        var curvesByLayer = extraction.Curves
            .GroupBy(c => c.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => g.Select(c => (c.Handle, scale == 1.0 ? c.Geometry : c.Geometry.Transformed(new Transform2D(0, scale, 0, 0)))).ToList(),
                StringComparer.OrdinalIgnoreCase);

        _lastModel = WorkbookReader.Read(_workbookPath);
        var movements = ProjectAssembly.BuildMovements(curvesByLayer,
            _lastModel.SignalGroups, _lastModel.PedestrianWidths, sidecar, findings);

        if (!analyzeOnly)
        {
            var errors = findings.Count(x => x.Severity == Severity.Error);
            var warns = findings.Count(x => x.Severity == Severity.Warning);
            SetStatus($"VALIDATE: {movements.Count} movements " +
                      $"({movements.Count(m => m.Mode == MovementMode.Pedestrian)} crossings), " +
                      $"units={unitsName}, errors={errors}, warnings={warns}. " +
                      (errors > 0 ? "Resolve errors via Setup/drawing before Analyze." : "Ready to Analyze."));
            Populate(findings.Select(x => new ConflictRow(x.ConflictRef ?? "-", x.Severity.ToString(), "", "", "", x.Code)).ToList());
            return;
        }

        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        var input = new PipelineInput(
            Path.GetFileNameWithoutExtension(db.Filename), Path.GetFileName(db.Filename),
            AnalysisWriters.Sha256OfFile(db.Filename), pack,
            new ProjectClassification { RoadType = sidecar.RoadType, PostedSpeedKph = sidecar.PostedSpeedKph },
            _lastModel.Constants, _lastModel.Variant, _lastModel.MovementParameters, movements);
        _lastOutput = AnalysisPipeline.Run(input);
        var all = _lastOutput.Findings.Concat(findings).ToList();

        var outDir = Path.GetDirectoryName(db.Filename)!;
        AnalysisWriters.WriteAll(outDir, input.IntersectionName, _lastOutput.Analysis, all,
            Guid.NewGuid().ToString("N"), DateTimeOffset.Now, TimeSpan.Zero, "Mahod.Intergreen.AutoCAD2026");

        Populate(_lastOutput.Analysis.Conflicts.Select(c => new ConflictRow(
            c.Id, c.Status, c.Points.Count.ToString(),
            c.Points.Count > 0 ? c.Points.Max(p => p.Cd).ToString("F2") : "",
            c.Points.Count > 0 ? c.Points.Min(p => p.Ed).ToString("F2") : "",
            c.FinalIg?.ToString() ?? "—")).ToList());
        SetStatus($"ANALYZE: {_lastOutput.Analysis.Conflicts.Count} conflicts, " +
                  $"matrix {_lastOutput.Analysis.Matrix.Count(m => m.Status == "VALID")} VALID / " +
                  $"{_lastOutput.Analysis.Matrix.Count(m => m.Status == "BLOCKED")} BLOCKED. " +
                  "Select a conflict → Show in drawing; then Export Excel.");
    }

    private sealed record ConflictRow(string Id, string Status, string Pts, string Cd, string Ed, string Ig);

    private static void Populate(List<ConflictRow> rows)
    {
        if (_list is null) return;
        _list.ItemsSource = rows;
    }

    // ---- 4. Review: zoom + non-destructive transient highlight (Directive §46) ----
    private static void ShowSelected()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("open a drawing first");
        if (_lastOutput is null || _list?.SelectedItem is not ConflictRow row)
            throw new InvalidOperationException("run Analyze and select a conflict row");
        var conflict = _lastOutput.Analysis.Conflicts.FirstOrDefault(c => c.Id == row.Id)
            ?? throw new InvalidOperationException("conflict not found");
        if (conflict.Points.Count == 0)
            throw new InvalidOperationException("this conflict has no candidate points (see findings)");

        ClearTransients();
        var tm = TransientManager.CurrentTransientManager;
        foreach (var p in conflict.Points)
        {
            var isGoverning = conflict.DefiningPointId == p.Id;
            var circle = new Circle(new Autodesk.AutoCAD.Geometry.Point3d(p.X, p.Y, 0), Autodesk.AutoCAD.Geometry.Vector3d.ZAxis,
                isGoverning ? 1.2 : 0.6)
            { ColorIndex = isGoverning ? 1 : 2 };
            _transients.Add(circle);
            tm.AddTransient(circle, TransientDrawingMode.DirectShortTerm, 128, new Autodesk.AutoCAD.Geometry.IntegerCollection());
        }

        var xs = conflict.Points.Select(p => p.X).ToList();
        var ys = conflict.Points.Select(p => p.Y).ToList();
        var margin = 15.0;
        doc.Editor.Command("_.ZOOM", "_W",
            new Autodesk.AutoCAD.Geometry.Point3d(xs.Min() - margin, ys.Min() - margin, 0),
            new Autodesk.AutoCAD.Geometry.Point3d(xs.Max() + margin, ys.Max() + margin, 0));
        SetStatus($"{conflict.Id}: {conflict.Points.Count} candidate points highlighted " +
                  $"(red = governing {conflict.DefiningPointId}); Final IG = {conflict.FinalIg?.ToString() ?? "—"}.");
    }

    [CommandMethod("IG_CLEAR_QA", CommandFlags.Modal)]
    public void ClearQa() => ClearTransients();

    private static void ClearTransients()
    {
        var tm = TransientManager.CurrentTransientManager;
        foreach (var d in _transients)
        {
            tm.EraseTransient(d, new Autodesk.AutoCAD.Geometry.IntegerCollection());
            d.Dispose();
        }
        _transients.Clear();
    }

    // ---- 5. Export Excel (Directive §22–§29) ----
    private static void ExportExcel()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("open a drawing first");
        if (_lastOutput is null || _lastModel is null || _workbookPath is null)
            throw new InvalidOperationException("run Analyze first");
        var outPath = Path.Combine(Path.GetDirectoryName(doc.Database.Filename)!,
            Path.GetFileNameWithoutExtension(_workbookPath) + "_MAHOD_INTERGREEN.xlsx");
        var export = WorkbookWriter.Export(_workbookPath, outPath, _lastOutput.Analysis, _lastModel);
        SetStatus(export.StructuralIssues.Count == 0
            ? $"Excel exported → {Path.GetFileName(outPath)} ({export.RowsPopulated} rows, {export.MoreThanFourPointRows} rows with >4 points)."
            : $"EXPORT VERIFICATION FAILED: {export.StructuralIssues.First()}");
    }
}
