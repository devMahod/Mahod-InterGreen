using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;
using Mahod.Intergreen.Reporting;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// r14 "new project" (David 2026-08-27, item 4): create the project's IG_matrix workbook from the
/// drawing and a one-screen form, then hand it to the very same Setup pipeline a hand-filled workbook
/// goes through (SetupService.Validate → Commit). The drawing is read exactly as Validate reads it;
/// the form shows what was found and asks only for what a drawing cannot hold. Nothing here computes
/// an intergreen.
/// </summary>
public partial class IgWorkflowCommands
{
    /// <summary>Palette action: "Excel חדש מהשרטוט…".</summary>
    private static void NewProjectFromDrawing()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.\nפתחי את שרטוט הבין-ירוקים ואז נסי שוב.", "no active document");
        var db = doc.Database;
        if (string.IsNullOrEmpty(db.Filename) || !File.Exists(db.Filename))
            throw new UserFacingException("השרטוט עדיין לא נשמר.\nשמרי את ה-DWG (קובץ ה-Excel נוצר לצידו) ואז נסי שוב.", "unsaved drawing");
        var scPath = SidecarPath(db);
        var drawingDir = Path.GetDirectoryName(db.Filename)!;

        var extras = ConfirmUnitsForNewProject(doc, scPath);
        var (curvesByLayer, unitsName) = ExtractProjectCurves(db, scPath);
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(curvesByLayer,
            new Dictionary<string, string>(), new Dictionary<string, double>(), ProjectSidecar.Load(scPath), findings);
        if (movements.Count == 0)
            throw new UserFacingException(
                "לא נמצאו תנועות בשרטוט.\nהתנועות צריכות להיות בשכבות intergreen_<שם> (למשל intergreen_N-T, intergreen_a) וקווי העצירה בשכבה intergreen_stopline.",
                "no intergreen_ layers");

        var prefill = NewProjectPrefill.FromMovements(movements.Select(m =>
            new MovementSummary(m.Id, m.Mode, m.Geometry.Boundaries, m.Geometry.StopLine)));
        var defaultPath = Path.Combine(drawingDir, NewProjectPrefill.DefaultWorkbookFileName(db.Filename, DateTime.Today));
        SupportLog.Write("NEW_PROJECT_FORM_OPEN",
            $"approaches={string.Join(",", prefill.Approaches)} movements={prefill.Movements.Count} crossings={prefill.Crossings.Count} " +
            $"slots={string.Join(";", prefill.Crossings.Select(c => c.Letter + ":" + SlotText.Format(c.Slots)))} units={unitsName}");

        var answer = new WpfNewProjectForm().Show(prefill, defaultPath);
        if (answer is null)
        {
            SupportLog.Write("NEW_PROJECT_CANCELLED", "");
            SetStatus("יצירת ה-Excel בוטלה — לא נכתב דבר.");
            return;
        }

        CreateProjectWorkbook(answer.Value.Inputs, answer.Value.OutputPath,
            movements.Where(m => m.Mode != MovementMode.Pedestrian).Select(m => m.Id).ToList(),
            movements.Where(m => m.Mode == MovementMode.Pedestrian).Select(m => m.Id).ToList(),
            scPath, drawingDir, extras, db.Filename);
    }

    /// <summary>
    /// Units, as Setup handles them (Directive §13): INSUNITS resolves the scale, a project that already
    /// confirmed metres is trusted, anything else is asked before a single curve is read.
    /// </summary>
    private static Dictionary<string, string> ConfirmUnitsForNewProject(Autodesk.AutoCAD.ApplicationServices.Document doc, string scPath)
    {
        var extras = new Dictionary<string, string>();
        var (unitsName, toMeters) = GeometryExtraction.Units(doc.Database);
        if (toMeters is not null) return extras;
        var sidecar = ProjectSidecar.Load(scPath);
        if (string.Equals(sidecar.UnitsConfirmed, "meters", StringComparison.OrdinalIgnoreCase)) return extras;
        var keep = doc.Editor.GetKeywords($"\nDrawing INSUNITS is '{unitsName}'. Confirm the drawing unit", "Meters Abort");
        if (keep.Status != PromptStatus.OK || keep.StringResult != "Meters")
            throw new UserFacingException(
                "ההגדרה לא הושלמה: יש לאשר שהשרטוט במטרים.\nלא נכתב דבר.", "units not confirmed");
        extras["unitsConfirmed"] = "meters";
        extras["unitsConfirmedBy"] = Environment.UserName + " via INTERGREEN new project";
        return extras;
    }

    /// <summary>The intergreen_* curves, scaled to metres — the same extraction Validate runs.</summary>
    private static (Dictionary<string, List<(string Handle, PolyCurve2D Curve)>> Curves, string UnitsName) ExtractProjectCurves(Database db, string scPath)
    {
        var (unitsName, toMeters) = GeometryExtraction.Units(db);
        var scale = toMeters ?? 1.0;                       // 1.0 only after ConfirmUnitsForNewProject accepted metres
        ExtractionResult extraction;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            extraction = GeometryExtraction.ExtractLayers(db, tr,
                layer => layer.StartsWith(ProjectAssembly.LayerPrefix, StringComparison.OrdinalIgnoreCase));
            tr.Commit();
        }
        var curves = extraction.Curves
            .GroupBy(c => c.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => g.Select(c => (c.Handle, scale == 1.0 ? c.Geometry : c.Geometry.Transformed(new Transform2D(0, scale, 0, 0)))).ToList(),
                StringComparer.OrdinalIgnoreCase);
        return (curves, unitsName);
    }

    /// <summary>
    /// Instantiate the template, then Setup it exactly like a hand-filled workbook: acceptance, sidecar
    /// commit, state machine. A refused workbook is deleted again so nothing half-made stays next to the DWG.
    /// </summary>
    private static void CreateProjectWorkbook(NewProjectInputs inputs, string outputPath,
        IReadOnlyList<string> vehicleMovements, IReadOnlyList<string> crossings,
        string scPath, string drawingDir, Dictionary<string, string> extras, string drawingFile)
    {
        var template = RuntimeRoots.BlankTemplatePath(PluginDir);
        TemplateInstantiation made;
        try
        {
            made = TemplateWorkbook.Instantiate(template, outputPath, inputs, vehicleMovements, crossings);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            throw new UserFacingException($"הקובץ כבר קיים ולא יוחלף:\n{outputPath}\nבחרי שם אחר.", ex.Message);
        }
        foreach (var w in made.Warnings) SupportLog.Write("NEW_PROJECT_WARNING", w);
        SupportLog.Write("NEW_PROJECT_WORKBOOK",
            $"{outputPath} pairs kept={made.PairRowsKept} blanked={made.PairRowsBlanked} " +
            $"movements={made.MovementsWritten.Count} crossings={made.CrossingsWritten.Count} template={template}");

        var accept = SetupService.Validate(outputPath);
        if (!accept.IsOk)
        {
            SupportLog.Write("NEW_PROJECT_WORKBOOK_REJECTED", accept.Detail);
            try { File.Delete(outputPath); } catch { }
            throw new UserFacingException(
                "קובץ ה-Excel שנוצר לא עבר את בדיקת הקבלה ולכן נמחק:\n" + accept.UserMessageHe, accept.Detail);
        }
        var commit = SetupService.Commit(accept, scPath, drawingDir, extras);
        if (!commit.Committed)
            throw new UserFacingException(commit.UserMessageHe, $"commit reload={commit.ReloadStatus} ref={commit.ReloadRef}");

        _workbookPath = accept.NormalizedPath;
        _lastModel = accept.Model;
        _lastOutput = null;
        _boundDrawingPath = drawingFile;
        RefreshDrawingLabel();
        State.OnSetupCommitted();
        RefreshRulesLabel();
        SupportLog.Write("SETUP_COMMITTED[NewProject]", accept.NormalizedPath!);
        var warn = made.Warnings.Count > 0 ? $" ({made.Warnings.Count} הערות ביומן התמיכה)" : "";
        SetStatus($"נוצר {Path.GetFileName(outputPath)} מהשרטוט — {made.PairRowsKept} זוגות, " +
                  $"{made.CrossingsWritten.Count} מעברי חצייה{warn}. Setup נשמר → עכשיו Validate.");
    }

    /// <summary>
    /// The one-screen data form. Everything the drawing knows is already filled in; the engineer confirms
    /// the crossing lengths and slots, types speeds and signal-group numbers, and chooses the file name.
    /// The form refuses to close on a state the template cannot hold: a crossing without a slot, two
    /// crossings on one slot, a non-number, an existing file.
    /// </summary>
    private sealed class WpfNewProjectForm
    {
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public (NewProjectInputs Inputs, string OutputPath)? Show(NewProjectPrefill prefill, string defaultPath)
        {
            var win = new Window
            {
                Title = "פרויקט חדש — יצירת קובץ Excel מהשרטוט",
                Width = 760, Height = 700, FlowDirection = System.Windows.FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "נמצאו בשרטוט " + prefill.Movements.Count + " תנועות ו-" + prefill.Crossings.Count + " מעברי חצייה. " +
                       "הקובץ נוצר מהתבנית של המחלקה (IG_matrix); הנוסחאות אינן משתנות. " +
                       "מה שהשרטוט יודע כבר מולא — יש לאשר את אורכי המעברים ומיקומם בתבנית, ולהזין מהירויות ומספרי קבוצות אות.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
            foreach (var w in prefill.Warnings)
                root.Children.Add(new TextBlock { Text = "⚠ " + w, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkOrange });

            // ---- constants ----
            root.Children.Add(Header("קבועים (ברירות המחדל של התבנית)"));
            var ped = Field(root, "מהירות הולכי רגל (מ'/שנ'):", NewProjectDefaults.PedestrianSpeedMps);
            var react = Field(root, "זמן תגובה (שנ'):", NewProjectDefaults.ReactionTimeSec);
            var decel = Field(root, "תאוטה (מ'/שנ'²):", NewProjectDefaults.DecelerationMps2);
            var vehLen = Field(root, "אורך רכב (מ'):", NewProjectDefaults.VehicleLengthMeters);
            var inbar = new CheckBox { Content = "מצב ענבר (y בגיליון Parameters)", Margin = new Thickness(0, 2, 0, 6) };
            root.Children.Add(inbar);

            // ---- approaches ----
            root.Children.Add(Header("גישות — מהירות הרכב המהיר ואופי הדרך"));
            var approachRows = new Dictionary<string, (CheckBox Interurban, TextBox Speed)>();
            foreach (var a in prefill.Approaches)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new TextBlock { Text = "זרוע " + ArmName(a) + $" ({a}):", Width = 130, Margin = new Thickness(0, 4, 8, 0) });
                var speed = new TextBox { Width = 70, Text = NewProjectDefaults.UrbanFastSpeedKph.ToString(Inv) };
                row.Children.Add(new TextBlock { Text = "מהירות (קמ\"ש):", Margin = new Thickness(0, 4, 6, 0) });
                row.Children.Add(speed);
                var inter = new CheckBox { Content = "בינעירוני", Margin = new Thickness(14, 4, 0, 0) };
                row.Children.Add(inter);
                root.Children.Add(row);
                approachRows[a] = (inter, speed);
            }

            // ---- movements → signal groups ----
            root.Children.Add(Header("תנועות — מספר קבוצת אות (ריק = שם התנועה)"));
            var sgBoxes = new Dictionary<string, TextBox>();
            var sgPanel = new WrapPanel();
            foreach (var m in prefill.Movements)
            {
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 16, 2) };
                cell.Children.Add(new TextBlock { Text = m + ":", Width = 70, Margin = new Thickness(0, 4, 4, 0) });
                var box = new TextBox { Width = 50 };
                cell.Children.Add(box);
                sgPanel.Children.Add(cell);
                sgBoxes[m] = box;
            }
            root.Children.Add(sgPanel);

            // ---- crossings ----
            root.Children.Add(Header("מעברי חצייה — אורך (מ') ומיקום בתבנית"));
            root.Children.Add(new TextBlock
            {
                Text = "האורך נמדד מהשרטוט (ממוצע שני הקצוות) — יש לאשר. המיקום הוא המשבצת בגיליון Pedestrian Xing לפי הזרוע: " +
                       "c1/c3/c5/c7 = צד הכניסה של צפון/מזרח/דרום/מערב, c2/c4/c6/c8 = צד היציאה, c9..c12 = מעבר נפרד בפנייה ימינה מתועלת. " +
                       "מעבר ברוחב מלא תופס את שתי המשבצות של הזרוע שלו (למשל c5, c6).",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 6),
            });
            var crossingRows = new Dictionary<string, (TextBox Length, TextBox Slots)>();
            foreach (var c in prefill.Crossings)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new TextBlock { Text = "מעבר " + c.Letter + ":", Width = 80, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 8, 0) });
                row.Children.Add(new TextBlock { Text = "אורך:", Margin = new Thickness(0, 4, 6, 0) });
                var len = new TextBox { Width = 70, Text = c.LengthGuessMeters.ToString("0.00", Inv) };
                row.Children.Add(len);
                row.Children.Add(new TextBlock { Text = "משבצות:", Margin = new Thickness(14, 4, 6, 0) });
                var slots = new TextBox { Width = 110, Text = SlotText.Format(c.Slots) };
                slots.ToolTip = string.Join("\n", c.Slots.Select(CrossingSlots.Describe));
                row.Children.Add(slots);
                row.Children.Add(new TextBlock
                {
                    Text = c.Slots.Count > 0 ? "  " + string.Join(" · ", c.Slots.Select(s => CrossingSlots.Describe(s).Split(':')[1].Trim())) : "  ללא משבצת — יש לבחור",
                    Opacity = 0.8, Margin = new Thickness(6, 4, 0, 0),
                });
                root.Children.Add(row);
                foreach (var n in c.Notes)
                    root.Children.Add(new TextBlock { Text = "   ⚠ " + n, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkOrange });
                crossingRows[c.Letter] = (len, slots);
            }

            // ---- output ----
            root.Children.Add(Header("קובץ ה-Excel שייווצר"));
            var pathBox = new TextBox { Text = defaultPath, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(pathBox);

            var error = new TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(error);
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "צור Excel והמשך ל-Validate", Width = 200, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            (NewProjectInputs, string)? result = null;
            ok.Click += (_, _) =>
            {
                var problems = new List<string>();
                double Num(TextBox box, string label, double min)
                {
                    if (!double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float, Inv, out var v) || v < min)
                        problems.Add($"{label}: יש להזין מספר{(min > 0 ? " גדול מ-0" : "")}");
                    return v;
                }
                var pedV = Num(ped, "מהירות הולכי רגל", 0.01);
                var reactV = Num(react, "זמן תגובה", 0.0);
                var decelV = Num(decel, "תאוטה", 0.01);
                var vehLenV = Num(vehLen, "אורך רכב", 0.01);

                var approaches = new Dictionary<string, ApproachInputs>();
                foreach (var (a, (inter, speed)) in approachRows)
                    approaches[a] = new ApproachInputs(inter.IsChecked == true, Num(speed, "מהירות זרוע " + a, 1.0));

                var sg = new Dictionary<string, string>();
                foreach (var (m, box) in sgBoxes)
                    sg[m] = string.IsNullOrWhiteSpace(box.Text) ? m : box.Text.Trim();

                var crossings = new Dictionary<string, CrossingInputs>();
                var owner = new Dictionary<int, string>();
                foreach (var (letter, (lenBox, slotBox)) in crossingRows)
                {
                    var lenV = Num(lenBox, "אורך מעבר " + letter, 0.01);
                    if (!SlotText.TryParse(slotBox.Text, out var slots, out var slotError))
                        problems.Add($"מעבר {letter}: {slotError}");
                    else if (slots.Count == 0)
                        problems.Add($"מעבר {letter}: אין משבצת בתבנית — יש לבחור לפי הזרוע (או להסיר את המעבר מהשרטוט)");
                    foreach (var s in slots)
                    {
                        if (owner.TryGetValue(s, out var other))
                            problems.Add($"המשבצת c{s} נבחרה גם למעבר {other} וגם למעבר {letter} — משבצת מחזיקה אות אחת");
                        else owner[s] = letter;
                    }
                    crossings[letter] = new CrossingInputs(letter, lenV) { Slots = slots };
                }

                var path = pathBox.Text.Trim();
                if (path.Length == 0 || !path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    problems.Add("שם הקובץ חייב להסתיים ב-.xlsx");
                else if (!Directory.Exists(Path.GetDirectoryName(path) ?? ""))
                    problems.Add("התיקייה של קובץ ה-Excel אינה קיימת");
                else if (File.Exists(path))
                    problems.Add("הקובץ כבר קיים — בחרי שם אחר (קובץ קיים לעולם לא מוחלף)");

                if (problems.Count > 0) { error.Text = string.Join("\n", problems); return; }
                result = (new NewProjectInputs(approaches, new Dictionary<string, double>(), sg, crossings,
                    pedV, reactV, decelV, inbar.IsChecked == true, vehLenV), path);
                win.DialogResult = true;
            };
            return win.ShowDialog() == true ? result : null;
        }

        private static TextBlock Header(string text) => new()
        {
            Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4),
        };

        private static TextBox Field(Panel parent, string label, double value)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label, Width = 200, Margin = new Thickness(0, 4, 8, 0) });
            var box = new TextBox { Width = 70, Text = value.ToString(Inv) };
            row.Children.Add(box);
            parent.Children.Add(row);
            return box;
        }

        private static string ArmName(string a) => a switch { "N" => "צפון", "E" => "מזרח", "S" => "דרום", _ => "מערב" };
    }

    /// <summary>
    /// Headless proof of the new-project path (accoreconsole, no WPF): the form's answers are lifted
    /// from a reference workbook given on the command line — the one the engineers filled by hand for the
    /// same drawing — so the generated workbook can be held against the golden numbers. Writes
    /// ig_new_project.json next to the drawing.
    /// </summary>
    [CommandMethod("IG_SMOKE_NEW_PROJECT", CommandFlags.Modal)]
    public void SmokeNewProject()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc!.Editor;
        var res = ed.GetString(new PromptStringOptions("\nReference workbook path") { AllowSpaces = true });
        if (res.Status != PromptStatus.OK) return;
        var db = doc.Database;
        var dir = Path.GetDirectoryName(db.Filename)!;
        var outPath = Path.Combine(dir, "ig_new_project.json");
        var payload = new Dictionary<string, object?> { ["release_id"] = HostBuild.ReleaseId };
        try
        {
            var scPath = SidecarPath(db);
            var reference = WorkbookReader.Read(res.StringResult.Trim());
            var (curvesByLayer, unitsName) = ExtractProjectCurves(db, scPath);
            var findings = new List<ValidationFinding>();
            var movements = ProjectAssembly.BuildMovements(curvesByLayer,
                new Dictionary<string, string>(), new Dictionary<string, double>(), ProjectSidecar.Load(scPath), findings);
            var prefill = NewProjectPrefill.FromMovements(movements.Select(m =>
                new MovementSummary(m.Id, m.Mode, m.Geometry.Boundaries, m.Geometry.StopLine)));
            payload["units"] = unitsName;
            payload["prefill"] = new Dictionary<string, object?>
            {
                ["approaches"] = prefill.Approaches,
                ["movements"] = prefill.Movements,
                ["crossings"] = prefill.Crossings.Select(c => new Dictionary<string, object?>
                {
                    ["letter"] = c.Letter, ["length_guess_m"] = c.LengthGuessMeters,
                    ["slots"] = c.Slots, ["notes"] = c.Notes,
                }).ToList(),
                ["warnings"] = prefill.Warnings,
            };

            // the form's answers, as the engineer would type them, lifted from the reference workbook
            var approaches = new Dictionary<string, ApproachInputs>();
            foreach (var a in prefill.Approaches)
            {
                var any = prefill.Movements.FirstOrDefault(m => NewProjectDefaults.ApproachOf(m) == a
                    && reference.MovementParameters.ContainsKey(m));
                var p = any is null ? null : reference.MovementParameters[any];
                var fast = p?.FastClearingKph ?? NewProjectDefaults.UrbanFastSpeedKph;
                var interurban = p?.SlowClearingKph is double slow && Math.Abs(slow - 25.0) > 1e-9;
                approaches[a] = new ApproachInputs(interurban, fast);
            }
            var sg = prefill.Movements.ToDictionary(m => m, m => reference.SignalGroups.TryGetValue(m, out var s) ? s : m);
            var crossings = prefill.Crossings.ToDictionary(c => c.Letter, c =>
                new CrossingInputs(c.Letter, reference.PedestrianWidths.TryGetValue(c.Letter, out var w) ? w : c.LengthGuessMeters)
                { Slots = c.Slots });
            var inputs = new NewProjectInputs(approaches, new Dictionary<string, double>(), sg, crossings);

            var output = Path.Combine(dir, NewProjectPrefill.DefaultWorkbookFileName(db.Filename, DateTime.Today));
            if (File.Exists(output)) File.Delete(output);           // smoke re-runs in the same folder
            CreateProjectWorkbook(inputs, output,
                movements.Where(m => m.Mode != MovementMode.Pedestrian).Select(m => m.Id).ToList(),
                movements.Where(m => m.Mode == MovementMode.Pedestrian).Select(m => m.Id).ToList(),
                scPath, dir, new Dictionary<string, string>
                {
                    ["unitsConfirmed"] = "meters",
                    ["unitsConfirmedBy"] = "IG_SMOKE_NEW_PROJECT deterministic confirmation",
                }, db.Filename);
            payload["workbook"] = output;
            payload["workbook_sha256"] = RuntimeRoots.Sha256OfOpenFile(output);
            payload["setup_state"] = State.ProjectConfigured ? "configured" : "not configured";

            RunPipeline(analyzeOnly: false);
            payload["validate_pending_references"] = _referenceIssues.Count;
            RunPipeline(analyzeOnly: true);
            payload["analyze"] = AnalyzePayload();
        }
        catch (System.Exception ex)
        {
            payload["error"] = ex.Message;
            payload["exception_full_detail"] = ex.ToString();
        }
        File.WriteAllText(outPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
        ed.WriteMessage($"\nIG_SMOKE_NEW_PROJECT → {outPath}");
    }
}
