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
/// The Intergreen workflow: one professional entry point — INTERGREEN — backed by a WPF
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

    /// <summary>
    /// Boundaries whose measurement origin fell back to a drawn endpoint at the last Validate/Analyze.
    /// Refreshed on every pipeline run; drives the reference-confirmation dialog.
    /// </summary>
    private static IReadOnlyList<ReferenceIssue> _referenceIssues = Array.Empty<ReferenceIssue>();

    /// <summary>
    /// Mirrors LegacyEnvelopeConflictStrategy's endpoint-suggestion tolerance, so the pre-check
    /// surfaces exactly the boundaries the engine will later reject.
    /// </summary>
    private const double ReferenceToleranceMeters = 0.5;

    /// <summary>What the project tolerance confirmed on the last run — shown in the status line.</summary>
    private static IReadOnlyList<ReferenceIssue> _lastAutoConfirmed = Array.Empty<ReferenceIssue>();

    /// <summary>The project's auto-confirm tolerance, or David's 10 cm default when none is set.</summary>
    private static double AutoConfirmTolerance(string sidecarPath)
        => SidecarStore.GetDouble(SidecarStore.Load(sidecarPath).Data, ReferenceReview.ToleranceSidecarKey)
           ?? ReferenceReview.DefaultAutoConfirmToleranceMeters;

    /// <summary>
    /// Project setting for ED-016: the gap in centimetres under which a near-miss reference is confirmed
    /// automatically. Per project, persisted in the sidecar, capped at 50 cm. Setting it to 0 turns the
    /// automation off and returns every near-miss to the engineer.
    /// </summary>
    private static void SetAutoConfirmTolerance()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        var scPath = SidecarPath(doc.Database);
        var current = AutoConfirmTolerance(scPath);
        var chosen = new WpfToleranceEditor().Choose(current);
        if (chosen is null) { SetStatus("סף האישור האוטומטי לא שונה."); return; }
        var store = SidecarStore.Load(scPath);
        SidecarStore.SetDouble(store.Data, ReferenceReview.ToleranceSidecarKey, chosen.Value);
        SidecarStore.Commit(scPath, store.Data);
        SupportLog.Write("AUTO_CONFIRM_TOLERANCE", $"{chosen.Value * 100:F0} cm (was {current * 100:F0} cm)");
        SetStatus($"סף האישור האוטומטי לפרויקט: {chosen.Value * 100:F0} ס\"מ. הריצי Validate מחדש.");
    }
    private static readonly List<Drawable> _transients = new();
    private static readonly WorkflowStateMachine State = new();

    /// <summary>Test seam (§18): production uses the WPF dialogs below.</summary>
    internal static IWorkbookPicker Picker = new WpfWorkbookPicker();
    internal static IMessageService Messages = new WpfMessageService();
    internal static IWorkbookPathPrompt PathPrompt = new WpfPathPrompt();
    internal static IRulePackChooser RulePackChooser = new WpfRulePackChooser();
    /// <summary>r10 (Lin): the project DWG is chosen through a normal file picker.</summary>
    internal static IDrawingPicker DrawingPicker = new WpfDrawingPicker();

    private static readonly Dictionary<WorkflowAction, Button> _actionButtons = new();
    private static TextBlock? _rulesLabel;
    private static TextBlock? _drawingLabel;

    private static string PluginDir
        => Path.GetDirectoryName(typeof(IgWorkflowCommands).Assembly.Location)!;

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

    /// <summary>r10: normal Windows file picker for the project drawing (*.dwg only).</summary>
    private sealed class WpfDrawingPicker : IDrawingPicker
    {
        public string? PickDrawing(string? initialDirectory)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "בחרי את שרטוט ה-DWG של הפרויקט",
                Filter = "AutoCAD drawing (*.dwg)|*.dwg",
                DefaultExt = ".dwg",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (initialDirectory is not null && Directory.Exists(initialDirectory))
                dlg.InitialDirectory = initialDirectory;
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }

    /// <summary>r10: the AutoCAD document collection behind the testable DrawingChooser.</summary>
    private sealed class AcadDrawingHost : IDrawingHost
    {
        public string? ActiveDrawingPath => AcadApp.DocumentManager.MdiActiveDocument?.Database?.Filename;

        public IReadOnlyList<string> OpenDrawingPaths
            => AcadApp.DocumentManager.Cast<Autodesk.AutoCAD.ApplicationServices.Document>()
                .Select(d => d.Database?.Filename)
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f!)
                .ToList();

        public void Activate(string normalizedPath)
        {
            var target = AcadApp.DocumentManager.Cast<Autodesk.AutoCAD.ApplicationServices.Document>()
                .First(d => DrawingSelection.SamePath(d.Database?.Filename, normalizedPath));
            AcadApp.DocumentManager.MdiActiveDocument = target;
        }

        // Palette clicks run in the application context, where opening a document is allowed.
        public void Open(string normalizedPath)
            => Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension.Open(
                AcadApp.DocumentManager, normalizedPath, false);
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

    /// <summary>
    /// Endpoint-reference confirmation (Directive §14). One checkbox per boundary that stops short
    /// of its stop line, showing the gap in centimetres and the DWG handle — at drawing scale these
    /// gaps are invisible, so the numbers are the only way to find the line. Nothing is pre-ticked:
    /// each confirmation is a deliberate engineering statement. Returns the confirmed curve ids,
    /// or null on cancel.
    /// </summary>
    private sealed class WpfReferenceConfirmer
    {
        public IReadOnlyList<string>? Choose(IReadOnlyList<ReferenceIssue> issues)
        {
            var win = new System.Windows.Window
            {
                Title = "נקודות ייחוס לקו העצירה",
                Width = 640, Height = 420, FlowDirection = System.Windows.FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = ReferenceReview.DialogExplanation(issues),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
            var list = new ListBox { Height = 190 };
            var boxes = new List<CheckBox>();
            foreach (var issue in issues)
            {
                var box = new CheckBox { Content = ReferenceReview.Line(issue), Tag = issue.CurveId };
                boxes.Add(box);
                list.Items.Add(new ListBoxItem { Content = box });
            }
            root.Children.Add(list);
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "אישור המסומנים", Width = 130, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
            row.Children.Add(ok); row.Children.Add(cancel);
            root.Children.Add(row);
            win.Content = root;
            List<string>? result = null;
            ok.Click += (_, _) =>
            {
                result = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
                win.DialogResult = true;
            };
            return win.ShowDialog() == true ? result : null;
        }
    }

    /// <summary>
    /// ED-016 tolerance editor: one number, in centimetres, with the engineering meaning spelled out.
    /// Returns metres, or null on cancel. Values above the ceiling are refused, not clamped silently.
    /// </summary>
    private sealed class WpfToleranceEditor
    {
        public double? Choose(double currentMeters)
        {
            var win = new System.Windows.Window
            {
                Title = "סף אישור אוטומטי לנקודות ייחוס",
                Width = 560, Height = 300, FlowDirection = System.Windows.FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "קו גבול שנעצר לפני קו העצירה בפער קטן מהסף הזה יאושר אוטומטית כנקודת ייחוס, " +
                       "יירשם ביומן, והחישוב ימשיך. פער גדול יותר ימתין לאישורך ב\"נקודות ייחוס…\".\n" +
                       $"ההגדרה נשמרת לפרויקט הזה בלבד. 0 = ללא אישור אוטומטי. מקסימום {ReferenceReview.MaxAutoConfirmToleranceMeters * 100:F0} ס\"מ.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = "סף (ס\"מ):", Margin = new Thickness(0, 4, 8, 0) });
            var box = new TextBox { Width = 80, Text = Math.Round(currentMeters * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            row.Children.Add(box);
            root.Children.Add(row);
            var error = new TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(error);
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "אישור", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            win.Content = root;
            double? result = null;
            ok.Click += (_, _) =>
            {
                if (!double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cm) || cm < 0)
                { error.Text = "יש להזין מספר סנטימטרים, 0 או יותר."; return; }
                if (cm > ReferenceReview.MaxAutoConfirmToleranceMeters * 100)
                { error.Text = $"הסף המרבי הוא {ReferenceReview.MaxAutoConfirmToleranceMeters * 100:F0} ס\"מ."; return; }
                result = cm / 100.0;
                win.DialogResult = true;
            };
            return win.ShowDialog() == true ? result : null;
        }
    }

    /// <summary>Rule-pack chooser (r8): plain list of the packs installed in the bundle;
    /// selection returns the pack id, cancel returns null. No engineering logic.</summary>
    private sealed class WpfRulePackChooser : IRulePackChooser
    {
        public string? Choose(IReadOnlyList<InstalledRulePack> installed, string activePackId)
        {
            var win = new System.Windows.Window
            {
                Title = "בחירת גרסת חוקים (הנחיות) לפרויקט",
                Width = 560, Height = 260, FlowDirection = System.Windows.FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "גרסת החוקים נשמרת בפרויקט הנוכחי בלבד. פרויקטים קיימים לעולם לא עוברים גרסה אוטומטית.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            });
            var list = new ListBox { Height = 110 };
            foreach (var pack in installed)
            {
                var item = new ListBoxItem
                {
                    Content = $"{pack.DisplayName} — {pack.Id} v{pack.Version}" +
                              (string.Equals(pack.Id, activePackId, StringComparison.OrdinalIgnoreCase) ? "  (פעיל)" : ""),
                    Tag = pack.Id,
                };
                list.Items.Add(item);
                if (string.Equals(pack.Id, activePackId, StringComparison.OrdinalIgnoreCase))
                    list.SelectedItem = item;
            }
            root.Children.Add(list);
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "אישור", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
            row.Children.Add(ok); row.Children.Add(cancel);
            root.Children.Add(row);
            win.Content = root;
            string? result = null;
            ok.Click += (_, _) =>
            {
                result = (list.SelectedItem as ListBoxItem)?.Tag as string;
                win.DialogResult = result is not null;
            };
            return win.ShowDialog() == true ? result : null;
        }
    }

    [CommandMethod("INTERGREEN", CommandFlags.Modal)]
    public void Intergreen()
    {
        if (_palette is null)
        {
            _palette = new PaletteSet("MAHOD INTERGREEN",
                new Guid("7A1C4E2B-9D3F-4B4E-A2F3-6C1D2E3F4A5B"));
            var panel = BuildPanel();
            _palette.AddVisual("Intergreen", panel);
            _palette.MinimumSize = new System.Drawing.Size(420, 480);
            SupportLog.Start(HostBuild.ReleaseId, AcadApp.Version.ToString());
            // Host capability record (§10): which Autodesk product/year/runtime we run
            // in. PRODUCT reports "AutoCAD" even inside Civil 3D, so the REAL product is
            // resolved from the loaded AECC modules (r9, HostProductIdentity).
            string acadver = "?";
            try { acadver = AcadApp.GetSystemVariable("ACADVER")?.ToString() ?? "?"; } catch { }
            SupportLog.Write("HOST_INFO",
                $"product={DetectHostProduct()} acadver={acadver} runtime=net{Environment.Version} " +
                $"hostBuild={HostBuild.Year} release={HostBuild.ReleaseId}");
            // r9: bind the palette to the active drawing — on a document switch the
            // previous drawing's results must never look current.
            AcadApp.DocumentManager.DocumentActivated += (_, _) => Guard(null, OnDocumentActivated);
        }
        _palette.Visible = true;
        BindToActiveDrawing();
        SetStatus("Setup → Validate → Analyze → Review → Export Excel.");
    }

    private static StackPanel BuildPanel()
    {
        // Deterministic palette colors: the hosted WPF visual does NOT inherit the
        // AutoCAD theme dictionary, so a bare TextBlock renders with WPF's default BLACK
        // foreground on the palette's black background — title/status/rules text were
        // invisible in the real host. Every text element gets an explicit foreground.
        var textBrush = System.Windows.Media.Brushes.White;
        var root = new StackPanel { Margin = new Thickness(8), Background = System.Windows.Media.Brushes.Black };
        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = textBrush,
        };
        // Official Mahod logo (white variant for the dark palette) — embedded in the
        // plugin assembly, never read from a user folder at runtime. Uniform stretch
        // preserves the aspect ratio; a missing resource silently degrades to text-only.
        var logo = TryLoadLogo();
        if (logo is not null)
            root.Children.Add(new System.Windows.Controls.Image
            {
                Source = logo,
                Height = 34,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10),
            });
        root.Children.Add(new TextBlock
        {
            Text = "Mahod Intergreen",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = textBrush,
        });
        root.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(_status);
        // r10: which drawing the displayed state belongs to — always visible, full path on hover.
        _drawingLabel = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
        };
        root.Children.Add(_drawingLabel);
        RefreshDrawingLabel();

        // r9 layout polish: uniform button metrics, clear hierarchy — the numbered main
        // workflow first, support actions in a separated secondary row. Compact on purpose.
        var buttons = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var support = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        Button Make(string label, WorkflowAction? gate, Action onClick, bool primary)
        {
            var b = new Button
            {
                Content = label,
                Height = primary ? 30 : 26,
                MinWidth = primary ? 108 : 96,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            };
            if (primary) b.FontWeight = FontWeights.SemiBold;
            b.Click += (_, _) => Guard(gate, onClick);
            // stage-gated actions are visually disabled until the state machine allows
            // them (Guard stays as the safety net for every path).
            if (gate is WorkflowAction ga && ga is not WorkflowAction.Setup and not WorkflowAction.ClearQa)
                _actionButtons[ga] = b;
            return b;
        }
        void Add(string label, WorkflowAction? gate, Action onClick)
            => buttons.Children.Add(Make(label, gate, onClick, primary: true));
        void AddSupport(string label, WorkflowAction? gate, Action onClick)
            => support.Children.Add(Make(label, gate, onClick, primary: false));
        Add("שרטוט DWG…", null, ChooseDrawing);
        Add("1. Setup — בחר קובץ Excel…", WorkflowAction.Setup, () => SetupCore(SetupInputMethod.Browse));
        Add("2. Validate", WorkflowAction.Validate, () => RunPipeline(analyzeOnly: false));
        Add("3. Analyze", WorkflowAction.Analyze, () => RunPipeline(analyzeOnly: true));
        Add("4. Show in drawing", WorkflowAction.Show, ShowSelected);
        Add("5. Export Excel", WorkflowAction.Export, ExportExcel);
        root.Children.Add(buttons);
        root.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 8) });

        AddSupport("נקודות ייחוס…", null, ConfirmReferences);
        AddSupport("סף אישור אוטומטי…", null, SetAutoConfirmTolerance);
        AddSupport("בחירת חוקים…", null, ChooseRulePack);
        AddSupport("Clear QA", WorkflowAction.ClearQa, ClearQa);
        AddSupport("Export Support Log", null, ExportSupportLog);
        // r10: typing/pasting the workbook path is an ADVANCED/diagnostic fallback only —
        // the normal way is the file picker (Lin's feedback). Same SetupService pipeline.
        AddSupport("מתקדם: נתיב Excel ידני…", WorkflowAction.Setup, () => SetupCore(SetupInputMethod.ManualPath));
        root.Children.Add(support);

        // active guideline (rule-pack) version — always visible; changeable only as a
        // deliberate engineer action (versioned-guidelines requirement).
        _rulesLabel = new TextBlock
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.85,
            Foreground = textBrush,
        };
        root.Children.Add(_rulesLabel);

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
        _list.SelectionChanged += (_, _) =>
        {
            State.HasSelection = _list.SelectedItem is not null;
            UpdateButtonStates();
        };
        root.Children.Add(_list);
        UpdateButtonStates();
        return root;
    }

    private static void UpdateButtonStates()
    {
        foreach (var kv in _actionButtons)
            kv.Value.IsEnabled = State.Gate(kv.Key) is null;
    }

    private static System.Windows.Media.Imaging.BitmapImage? TryLoadLogo()
    {
        try
        {
            var asm = typeof(IgWorkflowCommands).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("MahodLogoWhite.png", StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return null;
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.StreamSource = stream;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
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
        finally
        {
            UpdateButtonStates();
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

    /// <summary>
    /// Which boundaries can only be referenced to their stop line through a drawn endpoint
    /// (Directive §14). Runs at Validate, before any conflict is computed, so the engineer learns
    /// about it from a Hebrew sentence naming the movements instead of from empty rows later.
    /// Already-confirmed boundaries are omitted — they are no longer a question.
    /// </summary>
    private static IReadOnlyList<ReferenceIssue> ScanReferences(
        IReadOnlyList<PipelineMovement> movements, ProjectSidecar sidecar)
    {
        var issues = new List<ReferenceIssue>();
        foreach (var mv in movements)
        {
            var geom = mv.Geometry;
            if (geom.Mode == MovementMode.Pedestrian || geom.StopLine is null) continue;
            for (var i = 0; i < geom.Boundaries.Count; i++)
            {
                var curveId = $"{mv.Id}.b{i + 1}";
                if (sidecar.ConfirmedEndpointReferences.Contains(curveId)) continue;
                var r = ReferenceStation.Resolve(geom.Boundaries[i], geom.StopLine, ReferenceToleranceMeters);
                if (r is null || r.Method != ReferenceStation.Method.EndpointFallback) continue;
                issues.Add(new ReferenceIssue(mv.Id, curveId,
                    i < mv.SourceHandles.Count ? mv.SourceHandles[i] : "-",
                    MeasureGap(geom.Boundaries[i], geom.StopLine)));
            }
        }
        return ReferenceReview.Sorted(issues);
    }

    /// <summary>
    /// Distance from the boundary's nearest endpoint to its stop line. PolyCurve2D exposes no
    /// point-to-curve distance, and reimplementing it here would let the number the engineer sees
    /// drift away from the engine's own decision. So we narrow the very same Resolve() call: the
    /// smallest tolerance that still yields an endpoint fallback is the gap.
    /// </summary>
    private static double MeasureGap(PolyCurve2D boundary, PolyCurve2D stopLine)
    {
        double lo = 0.0, hi = ReferenceToleranceMeters;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            if (ReferenceStation.Resolve(boundary, stopLine, mid)?.Method == ReferenceStation.Method.EndpointFallback)
                hi = mid;
            else
                lo = mid;
        }
        return hi;
    }

    /// <summary>
    /// Confirm endpoint references (Directive §14). Shows every unconfirmed boundary with its gap
    /// and DWG handle; confirming writes the curve ids to the project sidecar, which is the only
    /// thing the engine reads. Nothing is confirmed implicitly and nothing is confirmed for other
    /// projects. Cancel leaves the drawing and the sidecar untouched.
    /// </summary>
    private static void ConfirmReferences()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        if (_referenceIssues.Count == 0)
            throw new UserFacingException(
                "אין נקודות ייחוס הממתינות לאישור.\nהריצי Validate כדי לבדוק את השרטוט הנוכחי.",
                "no pending reference issues");

        var chosen = new WpfReferenceConfirmer().Choose(_referenceIssues);
        if (chosen is null || chosen.Count == 0)
        {
            SetStatus("אישור נקודות הייחוס בוטל — לא נשמר שינוי.");
            return;
        }

        var scPath = SidecarPath(doc.Database);
        var store = SidecarStore.Load(scPath);
        var merged = SidecarStore.GetStrings(store.Data, ReferenceReview.SidecarKey).Concat(chosen);
        SidecarStore.SetStrings(store.Data, ReferenceReview.SidecarKey, merged);
        SidecarStore.Commit(scPath, store.Data);
        SupportLog.Write("REFERENCES_CONFIRMED", string.Join(";", chosen));

        // The confirmations only take effect through a fresh pipeline run; re-run both stages so
        // the palette never shows results computed under the previous set.
        _lastOutput = null;
        RunPipeline(analyzeOnly: false);
        RunPipeline(analyzeOnly: true);
    }

    private static string? _boundDrawingPath;

    /// <summary>Real host product (r9): PRODUCT says "AutoCAD" even inside Civil 3D, so
    /// the loaded AECC managed modules are the deciding evidence (HostProductIdentity).</summary>
    private static string DetectHostProduct()
    {
        string reported = "AutoCAD";
        try { reported = AcadApp.GetSystemVariable("PRODUCT")?.ToString() ?? "AutoCAD"; } catch { }
        return HostProductIdentity.Detect(
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name), reported);
    }

    private static void RefreshDrawingLabel()
    {
        if (_drawingLabel is null) return;
        _drawingLabel.Text = DrawingSelection.BoundDrawingLabelHe(_boundDrawingPath);
        _drawingLabel.ToolTip = string.IsNullOrWhiteSpace(_boundDrawingPath) ? null : _boundDrawingPath;
    }

    // ---- 0. Choose the project drawing through a normal file picker (r10, Lin) ----
    // Decision logic (cancel / invalid / already open / open) is DrawingChooser in the Host
    // layer and unit-tested there; this method only wires the dialog, the AutoCAD document
    // collection, the support log and the palette status. Cancel/rejection change nothing.
    private static void ChooseDrawing()
    {
        var chooser = new DrawingChooser(DrawingPicker, new AcadDrawingHost(), new RecentDrawingFolderStore());
        SupportLog.Write("DWG_PICKER_OPEN", chooser.InitialDirectory());
        var r = chooser.Choose();
        SupportLog.Write($"DWG_PICK_{r.Status}", r.LogDetail);

        if (r.IsError)
        {
            Messages.Error(r.UserMessageHe);
            SetStatus(r.UserMessageHe.Replace('\n', ' ') + "\nההגדרות הקודמות לא השתנו.");
            return;
        }
        if (r.ChangedActiveDrawing)
        {
            // DocumentActivated normally re-binds already; calling again is idempotent.
            BindToActiveDrawing();
            SetStatus(r.UserMessageHe +
                      (State.ProjectConfigured ? "\nהפרויקט השמור של השרטוט נטען — אפשר ישר Validate." : ""));
            return;
        }
        SetStatus(r.UserMessageHe);
    }

    /// <summary>r9: the palette state belongs to ONE drawing. When the active document
    /// differs from the bound one, displayed results are cleared, the workflow state
    /// machine resets, and the new drawing's saved project (sidecar) is adopted — stale
    /// results from another drawing must never look current or be acted on.</summary>
    private static void BindToActiveDrawing()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        string? active = doc?.Database?.Filename;
        if (!DrawingBinding.RequiresReset(_boundDrawingPath, active))
            return;
        _boundDrawingPath = active;
        RefreshDrawingLabel();
        _lastOutput = null;
        _lastModel = null;
        _workbookPath = null;
        try { ClearTransients(); } catch { /* old document's view may be gone */ }
        State.OnProjectInvalidated();
        Populate(new List<ConflictRow>());
        if (doc is not null)
            TryAdoptExistingProject();
        RefreshRulesLabel();
        UpdateButtonStates();
    }

    private static void OnDocumentActivated()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        string? active = doc?.Database?.Filename;
        if (!DrawingBinding.RequiresReset(_boundDrawingPath, active))
            return;
        BindToActiveDrawing();
        SetStatus("עברת לשרטוט אחר — התוצאות הקודמות נוקו. " +
                  (State.ProjectConfigured
                      ? "הפרויקט השמור של השרטוט נטען; הריצי Validate/Analyze."
                      : "הריצי Setup לשרטוט הזה."));
    }

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
        _boundDrawingPath = db.Filename;
        RefreshDrawingLabel();
        State.OnSetupCommitted();
        RefreshRulesLabel();
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

        // ED-016: a reference that misses its stop line by less than the project tolerance is a
        // drafting rounding, not an engineering question. Confirm it here, write it to the sidecar
        // under its own provenance key, log it, and rebuild the movements so THIS run already
        // measures from it. Anything wider still waits for the engineer in "נקודות ייחוס…".
        var scanned = ScanReferences(movements, sidecar);
        var tolerance = AutoConfirmTolerance(scPath);
        var (autoConfirmed, stillPending) = ReferenceReview.Partition(scanned, tolerance);
        _lastAutoConfirmed = autoConfirmed;
        if (autoConfirmed.Count > 0)
        {
            var store = SidecarStore.Load(scPath);
            SidecarStore.SetStrings(store.Data, ReferenceReview.SidecarKey,
                SidecarStore.GetStrings(store.Data, ReferenceReview.SidecarKey).Concat(autoConfirmed.Select(i => i.CurveId)));
            SidecarStore.SetStrings(store.Data, ReferenceReview.AutoConfirmedSidecarKey,
                SidecarStore.GetStrings(store.Data, ReferenceReview.AutoConfirmedSidecarKey).Concat(autoConfirmed.Select(i => i.CurveId)));
            SidecarStore.Commit(scPath, store.Data);
            foreach (var issue in autoConfirmed)
                SupportLog.Write("REFERENCE_AUTO_CONFIRMED",
                    $"{issue.CurveId} handle {issue.Handle} gap {issue.GapCentimetres:F1} cm <= tolerance {tolerance * 100:F0} cm");
            sidecar = ProjectSidecar.Load(scPath);
            findings.RemoveAll(f => f.Code == "IG-GEO-012");   // BuildMovements re-emits its own findings
            movements = ProjectAssembly.BuildMovements(curvesByLayer,
                _lastModel!.SignalGroups, _lastModel.PedestrianWidths, sidecar, findings);
        }
        _referenceIssues = stillPending;

        if (!analyzeOnly)
        {
            var errors = findings.Count(x => x.Severity == Severity.Error);
            var warns = findings.Count(x => x.Severity == Severity.Warning);
            State.OnValidateSucceeded();
            SupportLog.Write("VALIDATE_OK", $"movements={movements.Count} errors={errors} warnings={warns} " +
                                            $"endpointRefs={_referenceIssues.Count}");
            foreach (var issue in _referenceIssues)
                SupportLog.Write("REFERENCE_ENDPOINT", ReferenceReview.Line(issue));
            var autoNote = _lastAutoConfirmed.Count > 0
                ? " " + ReferenceReview.AutoConfirmedSummary(_lastAutoConfirmed, tolerance)
                : "";
            SetStatus($"VALIDATE: {movements.Count} movements " +
                      $"({movements.Count(m => m.Mode == MovementMode.Pedestrian)} crossings), " +
                      $"units={unitsName}, errors={errors}, warnings={warns}." + autoNote + " " +
                      (_referenceIssues.Count > 0
                          ? ReferenceReview.HebrewSummary(_referenceIssues) + " לחצי \"נקודות ייחוס…\"."
                          : errors > 0 ? "Resolve errors via Setup/drawing before Analyze." : "Ready to Analyze."));
            Populate(findings.Select(x => new ConflictRow(x.ConflictRef ?? "-", x.Severity.ToString(), "", "", "", x.Code)).ToList());
            return;
        }

        // r7: rules ship inside the bundle next to THIS assembly. RulesRoot() walks up
        // from AppContext.BaseDirectory, which inside Autodesk is the Autodesk install
        // dir — the exact Civil 3D 2027 Analyze failure. Anchor explicitly instead.
        // r8: WHICH pack is project state (sidecar); absent = validated legacy default,
        // never a silent migration. A stored-but-uninstalled pack fails closed in Hebrew.
        string packId = RulePackSelection.ProjectPackId(SidecarStore.Load(scPath).Data);
        string packDir = Path.Combine(RuntimeRoots.RulesRoot(PluginDir), packId);
        if (!File.Exists(Path.Combine(packDir, "manifest.json")))
            throw new UserFacingException(
                $"גרסת החוקים שנשמרה בפרויקט ('{packId}') אינה מותקנת.\n" +
                "בחרי גרסת חוקים מותקנת (כפתור \"בחירת חוקים…\") או הריצי את המתקין מחדש.",
                $"rule pack '{packId}' not installed");
        var pack = RulePackLoader.Load(packDir);
        SupportLog.Write("RULEPACK", $"{pack.Manifest.Id} {pack.Manifest.Version} sha256 {pack.ContentSha256}");
        var input = new PipelineInput(
            Path.GetFileNameWithoutExtension(db.Filename), Path.GetFileName(db.Filename),
            // r7: the active DWG is held locked by Autodesk — hash it with full share
            // flags (second ambient-assumption GUI blocker caught by the real-host gate).
            RuntimeRoots.Sha256OfOpenFile(db.Filename), pack,
            new ProjectClassification { RoadType = sidecar.RoadType, PostedSpeedKph = sidecar.PostedSpeedKph },
            _lastModel.Constants, _lastModel.Variant, _lastModel.MovementParameters, movements);
        _lastOutput = AnalysisPipeline.Run(input);
        var all = _lastOutput.Findings.Concat(findings).ToList();

        var outDir = Path.GetDirectoryName(db.Filename)!;
        AnalysisWriters.WriteAll(outDir, input.IntersectionName, _lastOutput.Analysis, all,
            Guid.NewGuid().ToString("N"), DateTimeOffset.Now, TimeSpan.Zero,
            $"Mahod.Intergreen.AutoCAD {HostBuild.ReleaseId} (net{Environment.Version.Major})");

        State.OnAnalyzeSucceeded();
        SupportLog.Write("ANALYZE_OK",
            $"conflicts={_lastOutput.Analysis.Conflicts.Count} " +
            $"matrixValid={_lastOutput.Analysis.Matrix.Count(m => m.Status == "VALID")} " +
            $"matrixReview={_lastOutput.Analysis.Matrix.Count(m => m.Status.StartsWith("REVIEW"))} " +
            $"matrixBlocked={_lastOutput.Analysis.Matrix.Count(m => m.Status == "BLOCKED")} " +
            $"pack={pack.Manifest.Id}@{pack.Manifest.Version}");
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
        var xs = conflict.Points.Select(p => p.X).ToList();
        var ys = conflict.Points.Select(p => p.Y).ToList();
        const double margin = 15.0;

        // r8 FIX (Arthur's GUI: "Exception: eInvalidInput"): Editor.Command cannot run
        // from a modeless palette click — it requires command context. Set the view
        // directly under a document lock instead; the transient QA markers are drawn the
        // same non-destructive way (never database entities, never saved).
        using (doc.LockDocument())
        {
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

            using var view = doc.Editor.GetCurrentView();
            double w = Math.Max(xs.Max() - xs.Min(), 1.0) + 2 * margin;
            double h = Math.Max(ys.Max() - ys.Min(), 1.0) + 2 * margin;
            double aspect = view.Height > 1e-9 ? view.Width / view.Height : 1.0;
            if (aspect > 1e-9)
            {
                if (w / h > aspect) h = w / aspect;
                else w = h * aspect;
            }
            view.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d(
                (xs.Min() + xs.Max()) / 2.0, (ys.Min() + ys.Max()) / 2.0);
            view.Width = w;
            view.Height = h;
            doc.Editor.SetCurrentView(view);
        }
        doc.Editor.UpdateScreen();
        SupportLog.Write("SHOW_OK",
            $"{conflict.Id} points={conflict.Points.Count} governing={conflict.DefiningPointId ?? "-"} finalIg={conflict.FinalIg?.ToString() ?? "-"}");
        SetStatus($"{conflict.Id}: {conflict.Points.Count} candidate points highlighted " +
                  $"(red = governing {conflict.DefiningPointId}); Final IG = {conflict.FinalIg?.ToString() ?? "—"}.");
    }

    [CommandMethod("IG_CLEAR_QA", CommandFlags.Modal)]
    public void ClearQaCommand() => ClearQa();

    /// <summary>Removes ONLY the tool-owned transient QA markers. They are never database
    /// entities, so user geometry cannot be touched by design.</summary>
    private static void ClearQa()
    {
        int n = _transients.Count;
        ClearTransients();
        AcadApp.DocumentManager.MdiActiveDocument?.Editor.UpdateScreen();
        SupportLog.Write("CLEAR_QA", $"removed={n}");
        SetStatus(n == 0
            ? "אין סימוני QA להסרה."
            : "סימוני ה-QA הזמניים הוסרו. גאומטריית השרטוט עצמה לא השתנתה.");
    }

    private static void RefreshRulesLabel()
    {
        if (_rulesLabel is null) return;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            string packId = RulePackSelection.DefaultPackId;
            if (doc is not null)
                packId = RulePackSelection.ProjectPackId(SidecarStore.Load(SidecarPath(doc.Database)).Data);
            var installed = RulePackSelection.ListInstalled(RuntimeRoots.RulesRoot(PluginDir));
            var active = installed.FirstOrDefault(x => string.Equals(x.Id, packId, StringComparison.OrdinalIgnoreCase));
            _rulesLabel.Text = active is null
                ? $"חוקים: {packId} — לא מותקן!"
                : $"חוקים: {active.Id} v{active.Version}";
        }
        catch { _rulesLabel.Text = "חוקים: —"; }
    }

    /// <summary>Deliberate rule-pack change (r8): list installed → engineer picks →
    /// explicit Hebrew warning → transactional sidecar persist → prior results
    /// invalidated. Absent selection keeps the legacy default forever.</summary>
    private static void ChooseRulePack()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.\nפתחי את שרטוט הפרויקט ואז נסי שוב.", "no active document");
        var installed = RulePackSelection.ListInstalled(RuntimeRoots.RulesRoot(PluginDir));
        if (installed.Count == 0)
            throw new UserFacingException("לא נמצאו חבילות חוקים מותקנות.\nהריצי את המתקין של Mahod Intergreen מחדש.",
                "no rule packs installed");

        string scPath = SidecarPath(doc.Database);
        var loaded = SidecarStore.Load(scPath);
        string current = RulePackSelection.ProjectPackId(loaded.Data);

        string? chosen = RulePackChooser.Choose(installed, current);
        if (chosen is null || !RulePackSelection.ChangeRequiresWarning(current, chosen))
        {
            SetStatus($"גרסת החוקים לא שונתה ({current}).");
            return;
        }
        if (!State.ProjectConfigured)
            throw new UserFacingException(
                "בחירת חוקים נשמרת בפרויקט של השרטוט.\nהריצי קודם Setup (בחירת קובץ Excel) ואז בחרי גרסת חוקים.",
                "rules change before setup");

        var target = installed.First(x => string.Equals(x.Id, chosen, StringComparison.OrdinalIgnoreCase));
        if (!Messages.Confirm(
                "החלפת גרסת חוקים לפרויקט הנוכחי:\n" +
                $"מ: {current}\n" +
                $"אל: {target.Id} v{target.Version} — {target.DisplayName}\n\n" +
                "תוצאות ניתוח קודמות יוסרו מהתצוגה ויש להריץ Analyze מחדש.\n" +
                "פרויקטים אחרים אינם מושפעים. להמשיך?"))
        {
            SetStatus("החלפת גרסת החוקים בוטלה.");
            return;
        }

        RulePackSelection.ApplySelection(loaded.Data, target.Id,
            Environment.UserName + " via INTERGREEN rules chooser");
        SidecarStore.Commit(scPath, loaded.Data);
        State.OnRulesChanged();
        _lastOutput = null;
        Populate(new List<ConflictRow>());
        SupportLog.Write("RULEPACK_SELECTED", $"{target.Id} v{target.Version} (was {current}) sidecar={scPath}");
        RefreshRulesLabel();
        SetStatus($"גרסת החוקים הוחלפה ל-{target.Id} v{target.Version}. הריצי Analyze מחדש.");
    }

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

        string acadver = "?";
        try { acadver = AcadApp.GetSystemVariable("ACADVER")?.ToString() ?? "?"; } catch { }
        var payload = new Dictionary<string, object?>
        {
            ["host_product"] = DetectHostProduct(),
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
                payload["analyze"] = AnalyzePayload();

                // r8: production Export Excel path (planner + writer — the same calls the
                // palette button makes), plus proof the SOURCE workbook is untouched.
                string sourceShaBefore = RuntimeRoots.Sha256OfOpenFile(_workbookPath!);
                var plan = ExportPlanner.Plan(_workbookPath!, dir);
                string exportPath = plan.Status switch
                {
                    ExportPlanStatus.Ok => plan.DestinationPath!,
                    ExportPlanStatus.NeedsConfirmOverwrite => plan.UniqueAlternativePath!,
                    _ => throw new InvalidOperationException($"export plan {plan.Status}: {plan.UserMessageHe}"),
                };
                var export = WorkbookWriter.Export(_workbookPath!, exportPath, _lastOutput!.Analysis, _lastModel!);
                payload["export"] = new Dictionary<string, object?>
                {
                    ["path"] = exportPath,
                    ["rows"] = export.RowsPopulated,
                    ["structural_issues"] = export.StructuralIssues.Count,
                    ["source_sha_before"] = sourceShaBefore,
                    ["source_sha_after"] = RuntimeRoots.Sha256OfOpenFile(_workbookPath!),
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

    /// <summary>
    /// Headless proof of the reference-confirmation feature (r12): Setup → Validate → report the
    /// boundaries that fell back to a drawn endpoint → confirm them all through the same
    /// SidecarStore calls the dialog uses → Validate + Analyze again. Exercises every part of the
    /// feature except the WPF dialog itself, so it can be verified in accoreconsole without a
    /// Civil 3D session. Writes ig_reference_review.json next to the drawing.
    /// </summary>
    [CommandMethod("IG_SMOKE_REFERENCE_REVIEW", CommandFlags.Modal)]
    public void SmokeReferenceReview()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc!.Editor;
        var res = ed.GetString(new PromptStringOptions("\nWorkbook path") { AllowSpaces = true });
        if (res.Status != PromptStatus.OK) return;
        var dir = Path.GetDirectoryName(doc.Database.Filename)!;
        var outPath = Path.Combine(dir, "ig_reference_review.json");
        var payload = new Dictionary<string, object?> { ["release_id"] = HostBuild.ReleaseId };
        try
        {
            var wb = SetupService.Validate(res.StringResult);
            payload["acceptance_status"] = wb.Status.ToString();
            if (wb.IsOk)
            {
                SetupService.Commit(wb, SidecarPath(doc.Database), dir, new Dictionary<string, string>
                {
                    ["unitsConfirmed"] = "meters",
                    ["unitsConfirmedBy"] = "IG_SMOKE_REFERENCE_REVIEW deterministic confirmation",
                });
                _workbookPath = wb.NormalizedPath;
                _lastModel = wb.Model;

                RunPipeline(analyzeOnly: false);
                var pending = _referenceIssues;
                payload["pending"] = pending.Select(i => new Dictionary<string, object?>
                {
                    ["movement"] = i.MovementId,
                    ["curveId"] = i.CurveId,
                    ["handle"] = i.Handle,
                    ["gap_cm"] = Math.Round(i.GapCentimetres, 2),
                }).ToList();
                payload["summary_he"] = ReferenceReview.HebrewSummary(pending);
                payload["auto_confirm_tolerance_m"] = AutoConfirmTolerance(SidecarPath(doc.Database));
                payload["auto_confirmed"] = _lastAutoConfirmed.Select(i => new Dictionary<string, object?>
                {
                    ["movement"] = i.MovementId, ["curveId"] = i.CurveId, ["handle"] = i.Handle,
                    ["gap_cm"] = Math.Round(i.GapCentimetres, 2),
                }).ToList();

                RunPipeline(analyzeOnly: true);
                payload["analyze_before"] = AnalyzePayload();

                if (pending.Count > 0)
                {
                    var scPath = SidecarPath(doc.Database);
                    var store = SidecarStore.Load(scPath);
                    SidecarStore.SetStrings(store.Data, ReferenceReview.SidecarKey,
                        SidecarStore.GetStrings(store.Data, ReferenceReview.SidecarKey)
                            .Concat(pending.Select(i => i.CurveId)));
                    SidecarStore.Commit(scPath, store.Data);
                    payload["confirmed"] = pending.Select(i => i.CurveId).ToList();

                    _lastOutput = null;
                    RunPipeline(analyzeOnly: false);
                    payload["pending_after"] = _referenceIssues.Count;
                    RunPipeline(analyzeOnly: true);
                    payload["analyze_after"] = AnalyzePayload();
                }
            }
        }
        catch (System.Exception ex)
        {
            payload["error"] = ex.Message;
        }
        File.WriteAllText(outPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
        ed.WriteMessage($"\nIG_SMOKE_REFERENCE_REVIEW OK → {outPath}");
    }

    private static Dictionary<string, object?> AnalyzePayload()
    {
        var an = _lastOutput!.Analysis;
        var wlst = an.Conflicts.FirstOrDefault(c => c.Clearing == "W-L" && c.Entering == "S-T");
        return new Dictionary<string, object?>
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
            ["rule_pack"] = $"{_lastOutput.Analysis.RulePack.Id} {_lastOutput.Analysis.RulePack.Version} {_lastOutput.Analysis.RulePack.ContentSha256}",
        };
    }

    /// <summary>
    /// r8 persistence gate: a FRESH Autodesk process adopting the previously committed
    /// sidecar (exactly what INTERGREEN does on palette open after close/reopen) must
    /// reproduce the identical production analysis WITHOUT any new Setup.
    /// </summary>
    [CommandMethod("IG_SMOKE_ADOPT_ANALYZE", CommandFlags.Modal)]
    public void SmokeAdoptAnalyze()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc!.Editor;
        string dir = Path.GetDirectoryName(doc.Database.Filename)!;
        string outPath = Path.Combine(dir, "ig_adopt_analyze.json");
        var payload = new Dictionary<string, object?>
        {
            ["host_year"] = HostBuild.Year,
            ["release_id"] = HostBuild.ReleaseId,
        };
        try
        {
            var sc = SidecarStore.Load(SidecarPath(doc.Database));
            payload["sidecar_status"] = sc.Status.ToString();
            var wb = SidecarStore.ResolveWorkbook(sc.Data, dir);
            payload["workbook_ref"] = wb.Status.ToString();
            if (wb.Status != WorkbookRefStatus.Ok)
                throw new InvalidOperationException($"adopt failed: sidecar={sc.Status} workbook={wb.Status}");
            _workbookPath = wb.Path;
            _lastModel = null;
            _lastOutput = null;
            State.OnProjectLoadedFromSidecar();
            payload["rule_pack_id"] = RulePackSelection.ProjectPackId(sc.Data);
            RunPipeline(analyzeOnly: false);
            RunPipeline(analyzeOnly: true);
            payload["analyze"] = AnalyzePayload();
        }
        catch (System.Exception ex)
        {
            payload["exception_full_detail"] = ExceptionDetail.Full(ex);
        }
        File.WriteAllText(outPath, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
        ed.WriteMessage($"\nIG_SMOKE_ADOPT_ANALYZE → {outPath}");
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
        SupportLog.Write("EXPORT_DONE", $"{outPath} rows={export.RowsPopulated} multiPoint={export.MultiPointRows} cleared={export.RowsClearedNoEngineResult} pivotReset={export.PivotCachesReset} issues={export.StructuralIssues.Count}");
        foreach (var note in export.LegacyPivotNotes) SupportLog.Write("EXPORT_LEGACY_PIVOT_NOTE", note);
        SetStatus(export.StructuralIssues.Count == 0
            ? $"Excel exported → {Path.GetFileName(outPath)} ({export.RowsPopulated} rows from the engine" +
              (export.RowsClearedNoEngineResult > 0 ? $", {export.RowsClearedNoEngineResult} rows cleared — no engine result, see QA column" : "") +
              (export.LegacyPivotNotes.Count > 0 ? "; legacy Matrix source has pre-existing #REF! rows — 'MAHOD Matrix Status' is authoritative" : "") +
              "). הקובץ נפתח ב-Excel כשהמטריצה מתרעננת אוטומטית."
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
