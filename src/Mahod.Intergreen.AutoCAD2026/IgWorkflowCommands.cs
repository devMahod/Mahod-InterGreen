using System.IO;
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
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;

[assembly: CommandClass(typeof(IgWorkflowCommands))]

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// The Pilot workflow: one professional entry point — INTERGREEN — backed by a WPF
/// palette: Setup → Validate → Analyze → Review → Export Excel. This host contains NO
/// engineering formulas; every number comes from the shared deterministic pipeline.
/// User-journey behavior (path handling, workflow gating, sidecar transactionality,
/// export policy, logging) lives in Mahod.Intergreen.Host and is unit-tested there.
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
    private static readonly WorkflowStateMachine State = new();

    /// <summary>Test seam (§18): production uses the WPF dialogs below.</summary>
    internal static IWorkbookPicker Picker = new WpfWorkbookPicker();
    internal static IMessageService Messages = new WpfMessageService();
    internal static IWorkbookPathPrompt PathPrompt = new WpfPathPrompt();

    private sealed class WpfWorkbookPicker : IWorkbookPicker
    {
        public string? PickWorkbook(string? initialDirectory)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "בחרי את קובץ ה-IG_matrix של הפרויקט (Excel)",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true,
            };
            if (initialDirectory is not null && Directory.Exists(initialDirectory))
                dlg.InitialDirectory = initialDirectory;
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }

    /// <summary>Manual full-path entry (r6). Optional, secondary to Browse; the text it
    /// returns goes straight into the same SetupService pipeline.</summary>
    private sealed class WpfPathPrompt : IWorkbookPathPrompt
    {
        public string? PromptForPath(string? initialValue)
        {
            var win = new System.Windows.Window
            {
                Title = "הדבק/הקלד נתיב מלא לקובץ ה-Excel",
                Width = 640, Height = 190, FlowDirection = System.Windows.FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "אפשר להדביק נתיב שהועתק עם \"Copy as path\" (כולל מרכאות) — הן יוסרו אוטומטית.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            });
            var box = new TextBox
            {
                Text = initialValue ?? "", FlowDirection = System.Windows.FlowDirection.LeftToRight,
                Padding = new Thickness(4), FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            };
            root.Children.Add(box);
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "אישור", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
            row.Children.Add(ok); row.Children.Add(cancel);
            root.Children.Add(row);
            win.Content = root;
            string? result = null;
            ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
            box.Focus(); box.SelectAll();
            return win.ShowDialog() == true ? result : null;
        }
    }

    private sealed class WpfMessageService : IMessageService
    {
        public void Info(string m) => MessageBox.Show(m, "Mahod Intergreen",
            MessageBoxButton.OK, MessageBoxImage.Information,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        public void Error(string m) => MessageBox.Show(m, "Mahod Intergreen",
            MessageBoxButton.OK, MessageBoxImage.Warning,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        public bool Confirm(string m) => MessageBox.Show(m, "Mahod Intergreen",
            MessageBoxButton.OKCancel, MessageBoxImage.Question,
            MessageBoxResult.Cancel, MessageBoxOptions.RtlReading) == MessageBoxResult.OK;
    }

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
            SupportLog.Start(HostBuild.ReleaseId, AcadApp.Version.ToString());
            // Host capability record (§10): which Autodesk product/year/runtime we run in.
            string product = "?", acadver = "?";
            try { product = AcadApp.GetSystemVariable("PRODUCT")?.ToString() ?? "?"; } catch { }
            try { acadver = AcadApp.GetSystemVariable("ACADVER")?.ToString() ?? "?"; } catch { }
            SupportLog.Write("HOST_INFO",
                $"product={product} acadver={acadver} runtime=net{Environment.Version} " +
                $"hostBuild={HostBuild.Year} release={HostBuild.ReleaseId}");
        }
        _palette.Visible = true;
        TryAdoptExistingProject();
        SetStatus("Setup → Validate → Analyze → Review → Export Excel.");
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
        void Add(string label, WorkflowAction? gate, Action onClick)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4) };
            b.Click += (_, _) => Guard(gate, onClick);
            buttons.Children.Add(b);
        }
        Add("1. Setup — בחר קובץ Excel…", WorkflowAction.Setup, () => SetupCore(SetupInputMethod.Browse));
        Add("או הדבק/הקלד נתיב מלא", WorkflowAction.Setup, () => SetupCore(SetupInputMethod.ManualPath));
        Add("2. Validate", WorkflowAction.Validate, () => RunPipeline(analyzeOnly: false));
        Add("3. Analyze", WorkflowAction.Analyze, () => RunPipeline(analyzeOnly: true));
        Add("4. Show in drawing", WorkflowAction.Show, ShowSelected);
        Add("5. Export Excel", WorkflowAction.Export, ExportExcel);
        Add("Clear QA", WorkflowAction.ClearQa, ClearTransients);
        Add("Export Support Log", null, ExportSupportLog);
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
        _list.SelectionChanged += (_, _) => State.HasSelection = _list.SelectedItem is not null;
        root.Children.Add(_list);
        return root;
    }

    /// <summary>Workflow gate + safety net: gate message instead of crash for out-of-order
    /// actions; unexpected exceptions go to the log with a clean user message.</summary>
    private static void Guard(WorkflowAction? gate, Action a)
    {
        if (gate is WorkflowAction g && State.Gate(g) is string blocked)
        {
            SupportLog.Write("GATE_BLOCKED", $"{g}");
            SetStatus(blocked);
            return;
        }
        try
        {
            a();
        }
        catch (UserFacingException ux)
        {
            SupportLog.Write("USER_ERROR", ux.LogDetail);
            SetStatus(ux.Message);
        }
        catch (System.Exception ex)
        {
            SupportLog.Error("UNEXPECTED", ex);
            SetStatus("אירעה שגיאה לא צפויה. הפרטים הטכניים נשמרו בלוג — " +
                      "לחצי Export Support Log ושלחי את הקובץ לארתור.\n" +
                      $"({ex.GetType().Name})");
        }
    }

    private sealed class UserFacingException : System.Exception
    {
        public string LogDetail { get; }
        public UserFacingException(string userMessageHe, string logDetail) : base(userMessageHe)
            => LogDetail = logDetail;
    }

    private static void SetStatus(string s)
    {
        if (_status is not null) _status.Text = s;
        AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[INTERGREEN] " + s.Replace('\n', ' '));
    }

    private static Database ActiveDb() =>
        (AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.\nפתחי את שרטוט הבין-ירוקים ואז נסי שוב.", "no active document"))
        .Database;

    private static string SidecarPath(Database db)
        => Path.ChangeExtension(db.Filename, null) + ".intergreen-project.json";

    /// <summary>On palette open: adopt a valid saved project so Validate works directly.</summary>
    private static void TryAdoptExistingProject()
    {
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc is null) return;
            var sc = SidecarStore.Load(SidecarPath(doc.Database));
            if (sc.Status == SidecarLoadStatus.Recovered)
            {
                SupportLog.Write("SIDECAR_RECOVERED", sc.RecoveryBackupPath ?? "");
                SetStatus(sc.UserMessageHe);
                return;
            }
            if (sc.Status != SidecarLoadStatus.Ok) return;
            var wb = SidecarStore.ResolveWorkbook(sc.Data, Path.GetDirectoryName(doc.Database.Filename)!);
            if (wb.Status == WorkbookRefStatus.Ok)
            {
                _workbookPath = wb.Path;
                State.OnProjectLoadedFromSidecar();
                SupportLog.Write("PROJECT_ADOPTED", wb.Path!);
            }
            else if (wb.Status is WorkbookRefStatus.Missing or WorkbookRefStatus.CandidateNextToDrawing)
            {
                SetStatus(wb.UserMessageHe);
            }
        }
        catch (System.Exception ex) { SupportLog.Error("ADOPT_FAILED", ex); }
    }

    // ---- 1. Setup: Browse (default) OR manual full path — ONE pipeline (r6) ----
    // Both entry points differ only in how the RAW string is obtained. From the moment it
    // exists it goes through SetupService (resolver -> acceptance -> reader -> validated
    // transactional commit). No duplicated path/workbook logic exists anywhere.
    private static void SetupCore(SetupInputMethod method)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.\nפתחי את שרטוט הבין-ירוקים ואז נסי שוב.", "no active document");
        var ed = doc.Editor;
        var db = doc.Database;
        var scPath = SidecarPath(db);
        string drawingDir = Path.GetDirectoryName(db.Filename)!;

        var loaded = SidecarStore.Load(scPath);
        if (loaded.Status == SidecarLoadStatus.Recovered)
            SetStatus(loaded.UserMessageHe);

        // starting point: last valid workbook, else drawing folder, else Documents
        var prev = SidecarStore.ResolveWorkbook(loaded.Data, drawingDir);
        string? prevPath = prev.Status == WorkbookRefStatus.Ok ? prev.Path : null;
        string? initialDir = prevPath is not null ? Path.GetDirectoryName(prevPath) : null;
        initialDir ??= drawingDir;
        if (!Directory.Exists(initialDir))
            initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string? raw;
        if (method == SetupInputMethod.Browse)
        {
            SupportLog.Write("SETUP_PICKER_OPEN", initialDir);
            raw = Picker.PickWorkbook(initialDir);
        }
        else
        {
            SupportLog.Write("SETUP_MANUAL_PROMPT", prevPath ?? initialDir);
            raw = PathPrompt.PromptForPath(prevPath);
        }

        if (raw is null || string.IsNullOrWhiteSpace(raw))
        {
            SupportLog.Write("SETUP_CANCELLED", method.ToString());
            SetStatus("הבחירה בוטלה — ההגדרות הקודמות של הפרויקט לא השתנו.");
            return; // transactional: nothing written
        }

        var accept = SetupService.Validate(raw);
        if (!accept.IsOk)
        {
            SupportLog.Write($"SETUP_WORKBOOK_REJECTED[{method}]", accept.Detail);
            Messages.Error(accept.UserMessageHe);
            SetStatus("קובץ ה-Excel לא התקבל — ההגדרות הקודמות לא השתנו. אפשר לנסות שוב.");
            return; // previous valid configuration intact
        }

        // units confirmation (Directive §13) — host-specific, still before any commit
        var extras = new Dictionary<string, string>();
        var (unitsName, toMeters) = GeometryExtraction.Units(db);
        if (toMeters is null &&
            !(loaded.Data.TryGetValue("unitsConfirmed", out var uc) && uc.ValueKind == JsonValueKind.String
              && string.Equals(uc.GetString(), "meters", StringComparison.OrdinalIgnoreCase)))
        {
            var keep = ed.GetKeywords(
                $"\nDrawing INSUNITS is '{unitsName}'. Confirm the drawing unit", "Meters Abort");
            if (keep.Status != PromptStatus.OK || keep.StringResult != "Meters")
                throw new UserFacingException(
                    "ההגדרה לא הושלמה: יש לאשר שהשרטוט במטרים (הניתוח נשאר חסום עד האישור).\n" +
                    "ההגדרות הקודמות לא השתנו.",
                    "units not confirmed");
            extras["unitsConfirmed"] = "meters";
            extras["unitsConfirmedBy"] = Environment.UserName + " via INTERGREEN Setup";
        }

        var commit = SetupService.Commit(accept, scPath, drawingDir, extras);
        if (!commit.Committed)
            throw new UserFacingException(commit.UserMessageHe,
                $"commit reload={commit.ReloadStatus} ref={commit.ReloadRef}");

        _workbookPath = accept.NormalizedPath;
        _lastModel = accept.Model;
        _lastOutput = null;
        State.OnSetupCommitted();
        SupportLog.Write($"SETUP_COMMITTED[{method}]", accept.NormalizedPath!);
        SetStatus($"Setup נשמר ({Path.GetFileName(accept.NormalizedPath)}) → עכשיו Validate.");
    }

    // ---- 2/3. Validate + Analyze — the SAME shared pipeline the CLI runs ----
    private static void RunPipeline(bool analyzeOnly)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.\nפתחי את שרטוט הבין-ירוקים ואז נסי שוב.", "no active document");
        var db = doc.Database;
        var scPath = SidecarPath(db);
        var sidecar = ProjectSidecar.Load(scPath);

        if (_workbookPath is null)
        {
            var sc = SidecarStore.Load(scPath);
            var wb = SidecarStore.ResolveWorkbook(sc.Data, Path.GetDirectoryName(db.Filename)!);
            if (wb.Status != WorkbookRefStatus.Ok)
                throw new UserFacingException(
                    wb.Status == WorkbookRefStatus.NotConfigured
                        ? "עדיין לא הוגדר פרויקט — לחצי Setup ובחרי את קובץ ה-Excel."
                        : wb.UserMessageHe,
                    $"workbook ref {wb.Status}");
            _workbookPath = wb.Path;
        }
        var recheck = WorkbookPathResolver.Resolve(_workbookPath);
        if (!recheck.IsOk)
            throw new UserFacingException(recheck.UserMessageHe, $"workbook recheck {recheck.Status}");

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
        else throw new UserFacingException(
            $"יחידות השרטוט ('{unitsName}') לא אושרו.\nהריצי Setup ואשרי שהשרטוט במטרים.",
            "units unresolved");

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

        if (_lastModel is null)
        {
            var accept = WorkbookAcceptance.Validate(_workbookPath);
            if (!accept.IsOk)
                throw new UserFacingException(accept.UserMessageHe, accept.Detail);
            _lastModel = accept.Model;
        }
        var movements = ProjectAssembly.BuildMovements(curvesByLayer,
            _lastModel!.SignalGroups, _lastModel.PedestrianWidths, sidecar, findings);

        if (!analyzeOnly)
        {
            var errors = findings.Count(x => x.Severity == Severity.Error);
            var warns = findings.Count(x => x.Severity == Severity.Warning);
            State.OnValidateSucceeded();
            SupportLog.Write("VALIDATE_OK", $"movements={movements.Count} errors={errors} warnings={warns}");
            SetStatus($"VALIDATE: {movements.Count} movements " +
                      $"({movements.Count(m => m.Mode == MovementMode.Pedestrian)} crossings), " +
                      $"units={unitsName}, errors={errors}, warnings={warns}. " +
                      (errors > 0 ? "Resolve errors via Setup/drawing before Analyze." : "Ready to Analyze."));
            Populate(findings.Select(x => new ConflictRow(x.ConflictRef ?? "-", x.Severity.ToString(), "", "", "", x.Code)).ToList());
            return;
        }

        // r7: rules ship inside the bundle next to THIS assembly. RulesRoot() walks up
        // from AppContext.BaseDirectory, which inside Autodesk is the Autodesk install
        // dir — the exact Civil 3D 2027 Analyze failure. Anchor explicitly instead.
        string pluginDir = Path.GetDirectoryName(typeof(IgWorkflowCommands).Assembly.Location)!;
        var pack = RulePackLoader.Load(Path.Combine(RuntimeRoots.RulesRoot(pluginDir), "legacy-mahod-v1"));
        var input = new PipelineInput(
            Path.GetFileNameWithoutExtension(db.Filename), Path.GetFileName(db.Filename),
            AnalysisWriters.Sha256OfFile(db.Filename), pack,
            new ProjectClassification { RoadType = sidecar.RoadType, PostedSpeedKph = sidecar.PostedSpeedKph },
            _lastModel.Constants, _lastModel.Variant, _lastModel.MovementParameters, movements);
        _lastOutput = AnalysisPipeline.Run(input);
        var all = _lastOutput.Findings.Concat(findings).ToList();

        var outDir = Path.GetDirectoryName(db.Filename)!;
        AnalysisWriters.WriteAll(outDir, input.IntersectionName, _lastOutput.Analysis, all,
            Guid.NewGuid().ToString("N"), DateTimeOffset.Now, TimeSpan.Zero,
            $"Mahod.Intergreen.AutoCAD {HostBuild.ReleaseId} (net{Environment.Version.Major})");

        State.OnAnalyzeSucceeded();
        SupportLog.Write("ANALYZE_OK", $"conflicts={_lastOutput.Analysis.Conflicts.Count}");
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
        State.HasSelection = false;
    }

    // ---- 4. Review: zoom + non-destructive transient highlight ----
    private static void ShowSelected()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        if (_lastOutput is null || _list?.SelectedItem is not ConflictRow row)
            throw new UserFacingException("בחרי שורה ברשימת הקונפליקטים ואז Show.", "no selection");
        var conflict = _lastOutput.Analysis.Conflicts.FirstOrDefault(c => c.Id == row.Id);
        if (conflict is null || conflict.Points.Count == 0)
            throw new UserFacingException(
                "לשורה שנבחרה אין נקודות מועמד להצגה (ראי את הממצאים ברשימה).",
                $"conflict {row.Id}: no points");

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

    /// <summary>
    /// Failure-closure §7: deterministic Setup-acceptance smoke INSIDE the real Autodesk
    /// process, calling the SAME production services used after File Picker selection:
    /// resolver → acceptance → sidecar transactional commit (to a test copy) → reload.
    /// Emits machine-readable JSON next to the drawing. Never mutates the workbook.
    /// </summary>
    [CommandMethod("IG_SMOKE_SETUP_ACCEPT", CommandFlags.Modal)]
    public void SmokeSetupAccept()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc!.Editor;
        var res = ed.GetString(new PromptStringOptions("\nWorkbook path") { AllowSpaces = true });
        if (res.Status != PromptStatus.OK) return;
        string outPath = Path.Combine(Path.GetDirectoryName(doc.Database.Filename)!, "ig_setup_accept.json");

        string product = "?", acadver = "?";
        try { product = AcadApp.GetSystemVariable("PRODUCT")?.ToString() ?? "?"; } catch { }
        try { acadver = AcadApp.GetSystemVariable("ACADVER")?.ToString() ?? "?"; } catch { }
        var payload = new Dictionary<string, object?>
        {
            ["host_product"] = product,
            ["host_year"] = HostBuild.Year,
            ["release_id"] = HostBuild.ReleaseId,
            ["release_revision"] = HostBuild.ReleaseRevision,
            ["git_sha"] = HostBuild.GitSha,
            ["acadver"] = acadver,
            ["runtime"] = Environment.Version.ToString(),
            ["workbook_input"] = res.StringResult,
        };
        try
        {
            // r6: prove BOTH production input methods inside the real Autodesk process.
            // Browse = the clean absolute path a file picker returns.
            // Manual = the same file pasted Windows-"Copy as path" style (quoted + padded).
            string browseRaw = res.StringResult;
            string manualRaw = "  \"" + res.StringResult.Trim().Trim('"') + "\"  ";
            string dir = Path.GetDirectoryName(doc.Database.Filename)!;

            var browse = SetupService.Validate(browseRaw);
            var manual = SetupService.Validate(manualRaw);
            payload["path_resolution"] = WorkbookPathResolver.Resolve(browseRaw).Status.ToString();
            payload["acceptance_status"] = browse.Status.ToString();
            payload["acceptance_status_manual"] = manual.Status.ToString();
            payload["acceptance_detail"] = browse.IsOk ? "ok" : browse.Detail;

            if (browse.IsOk && manual.IsOk)
            {
                payload["template_variant"] = browse.Model!.Variant.ToString();
                payload["model_counts"] = new Dictionary<string, int>
                {
                    ["signalGroups"] = browse.Model.SignalGroups.Count,
                    ["pedestrianWidths"] = browse.Model.PedestrianWidths.Count,
                    ["movementParameters"] = browse.Model.MovementParameters.Count,
                };
                string scB = Path.Combine(dir, "smoke-browse.intergreen-project.json");
                string scM = Path.Combine(dir, "smoke-manual.intergreen-project.json");
                var cB = SetupService.Commit(browse, scB, dir);
                var cM = SetupService.Commit(manual, scM, dir);
                payload["sidecar_commit"] = cB.ReloadStatus.ToString();
                payload["sidecar_reload"] = cB.ReloadRef.ToString();
                payload["sidecar_commit_manual"] = cM.ReloadStatus.ToString();
                payload["sidecar_reload_manual"] = cM.ReloadRef.ToString();
                payload["input_method_parity"] = new Dictionary<string, object?>
                {
                    ["normalized_path_equal"] = browse.NormalizedPath == manual.NormalizedPath,
                    ["template_equal"] = browse.Model.Variant == manual.Model!.Variant,
                    ["model_counts_equal"] =
                        browse.Model.SignalGroups.Count == manual.Model.SignalGroups.Count &&
                        browse.Model.PedestrianWidths.Count == manual.Model.PedestrianWidths.Count &&
                        browse.Model.MovementParameters.Count == manual.Model.MovementParameters.Count,
                    ["sidecar_identical"] = File.ReadAllText(scB) == File.ReadAllText(scM),
                    ["setup_state_equal"] = cB.Committed == cM.Committed,
                };

                // r7 REGRESSION (Arthur's confirmed GUI blocker): run the FULL production
                // Validate + Analyze — the exact palette code path, including rule-pack
                // resolution — inside the real Autodesk process. The process CWD and
                // AppContext.BaseDirectory here belong to Autodesk and contain no rules/
                // anywhere above them; r6 failed exactly at this point with
                // DirectoryNotFoundException. The payload records the ambient dirs as
                // evidence the run reproduced the hostile environment.
                // The palette Setup asks the user to confirm meters when INSUNITS is
                // Unitless (ex1.dwg is); the smoke supplies that confirmation
                // deterministically — same extras, same pipeline.
                var smokeExtras = new Dictionary<string, string>
                {
                    ["unitsConfirmed"] = "meters",
                    ["unitsConfirmedBy"] = "IG_SMOKE_SETUP_ACCEPT deterministic confirmation",
                };
                var cD = SetupService.Commit(browse, SidecarPath(doc.Database),
                    Path.GetDirectoryName(doc.Database.Filename)!, smokeExtras);
                payload["sidecar_commit_drawing"] = cD.ReloadStatus.ToString();
                _workbookPath = browse.NormalizedPath;
                _lastModel = browse.Model;
                RunPipeline(analyzeOnly: false);
                RunPipeline(analyzeOnly: true);
                var an = _lastOutput!.Analysis;
                var wlst = an.Conflicts.FirstOrDefault(c => c.Clearing == "W-L" && c.Entering == "S-T");
                payload["analyze"] = new Dictionary<string, object?>
                {
                    ["process_cwd"] = Environment.CurrentDirectory,
                    ["base_directory"] = AppContext.BaseDirectory,
                    ["movements"] = an.Movements.Count,
                    ["crossings"] = an.Movements.Count(m => m.Mode.Contains("edestrian")),
                    ["conflicts"] = an.Conflicts.Count,
                    ["matrix_valid"] = an.Matrix.Count(m => m.Status == "VALID"),
                    ["matrix_review"] = an.Matrix.Count(m => m.Status.StartsWith("REVIEW")),
                    ["matrix_blocked"] = an.Matrix.Count(m => m.Status == "BLOCKED"),
                    ["wl_st_final_ig"] = wlst?.FinalIg,
                };
            }
        }
        catch (System.Exception ex)
        {
            payload["exception_full_detail"] = ExceptionDetail.Full(ex);
        }
        payload["loaded_assemblies"] = ExceptionDetail.LoadedAssemblyReport()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        File.WriteAllText(outPath, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
        ed.WriteMessage($"\nIG_SMOKE_SETUP_ACCEPT → {outPath}");
    }

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

    // ---- 5. Export Excel — planner + explicit confirm (hardening §12) ----
    private static void ExportExcel()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        if (_lastOutput is null || _lastModel is null || _workbookPath is null)
            throw new UserFacingException("אין תוצאות לייצוא — הריצי Analyze קודם.", "no analysis output");

        var plan = ExportPlanner.Plan(_workbookPath, Path.GetDirectoryName(doc.Database.Filename)!);
        string outPath;
        switch (plan.Status)
        {
            case ExportPlanStatus.Ok:
                outPath = plan.DestinationPath!;
                break;
            case ExportPlanStatus.NeedsConfirmOverwrite:
                outPath = Messages.Confirm(plan.UserMessageHe)
                    ? plan.DestinationPath!
                    : plan.UniqueAlternativePath!;
                break;
            case ExportPlanStatus.RefusedSourceEqualsDestination:
            case ExportPlanStatus.DestinationError:
            default:
                throw new UserFacingException(plan.UserMessageHe, $"export plan {plan.Status}");
        }

        SupportLog.Write("EXPORT_START", outPath);
        var export = WorkbookWriter.Export(_workbookPath, outPath, _lastOutput.Analysis, _lastModel);
        SupportLog.Write("EXPORT_DONE", $"{outPath} rows={export.RowsPopulated} issues={export.StructuralIssues.Count}");
        SetStatus(export.StructuralIssues.Count == 0
            ? $"Excel exported → {Path.GetFileName(outPath)} ({export.RowsPopulated} rows, {export.MoreThanFourPointRows} rows with >4 points)."
            : $"EXPORT VERIFICATION FAILED: {export.StructuralIssues.First()}");
    }

    private static void ExportSupportLog()
    {
        string? dest = SupportLog.ExportTo(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        SetStatus(dest is null
            ? "אין עדיין קובץ לוג להפעלה הנוכחית."
            : $"קובץ הלוג נשמר בשולחן העבודה: {Path.GetFileName(dest)} — אפשר לשלוח אותו לארתור.");
    }
}
