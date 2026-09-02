using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// ED-016 (amended 2026-09-02), the explicit half: the virtual extensions Validate applied can be written
/// into the drawing — only on the engineer's click, one undoable step, never on a run. And a short
/// boundary can be shown in the drawing instead of being named by its handle.
/// </summary>
public partial class IgWorkflowCommands
{
    /// <summary>Drawing units → metres factor of the last pipeline run (1.0 for a metric drawing).</summary>
    private static double _lastScale = 1.0;

    /// <summary>Cycles through the pending references with "הצג קו קצר…".</summary>
    private static int _shortBoundaryCursor;

    /// <summary>Support action "הארך בשרטוט…": write the last run's virtual extensions into the DWG.</summary>
    private static void ExtendInDrawing()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        var extended = _lastResolved.Where(r => r.Kind == ReferenceResolutionKind.Extended && r.Extension is not null).ToList();
        if (extended.Count == 0)
            throw new UserFacingException(
                "אין הארכות וירטואליות מהריצה האחרונה.\nהריצי Validate עם סף הארכה גדול מ-0; קווים שהוארכו וירטואלית יופיעו כאן.",
                "nothing extended in the last run");

        var chosen = new WpfExtensionPicker().Choose(extended);
        if (chosen is null || chosen.Count == 0) { SetStatus("ההארכה בשרטוט בוטלה — השרטוט לא שונה."); return; }

        var (written, skipped) = WriteExtensions(doc, chosen);
        SupportLog.Write("BOUNDARY_EXTENDED_IN_DWG", $"written={string.Join(";", written)} skipped={string.Join(";", skipped)}");
        SetStatus($"{written.Count} קווים הוארכו בשרטוט עד קו העצירה" +
                  (skipped.Count > 0 ? $"; {skipped.Count} לא ניתנים להארכה אוטומטית ({string.Join(", ", skipped)})" : "") +
                  ". ניתן לבטל ב-Undo. מריצה Validate מחדש…");
        _lastOutput = null;
        RunPipeline(analyzeOnly: false);
    }

    /// <summary>
    /// The write itself: Line → its short endpoint moves to the intersection (the extension is collinear);
    /// LWPolyline → a vertex is added at the intersection at the short end (existing vertices and bulges
    /// stay). Arcs and 2D polylines are not touched — an arc extended along its tangent is no longer an
    /// arc — and are reported for the engineer. One document lock, one transaction, one Undo.
    /// </summary>
    private static (List<string> Written, List<string> Skipped) WriteExtensions(
        Autodesk.AutoCAD.ApplicationServices.Document doc, IReadOnlyList<ReferenceResolution> chosen)
    {
        var written = new List<string>();
        var skipped = new List<string>();
        var db = doc.Database;
        using (doc.LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var r in chosen)
            {
                var ext = r.Extension!;
                var to = new Autodesk.AutoCAD.Geometry.Point2d(ext.To.X / _lastScale, ext.To.Y / _lastScale);
                if (!db.TryGetObjectId(new Handle(Convert.ToInt64(r.Issue.Handle, 16)), out var id))
                { skipped.Add(r.Issue.CurveId + " (handle not found)"); continue; }
                var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                switch (ent)
                {
                    case AcDb.Line line:
                    {
                        var p3 = new Autodesk.AutoCAD.Geometry.Point3d(to.X, to.Y, line.StartPoint.Z);
                        if (ext.AtStart) line.StartPoint = p3; else line.EndPoint = p3;
                        written.Add(r.Issue.CurveId);
                        break;
                    }
                    case AcDb.Polyline lw:
                    {
                        if (ext.AtStart) lw.AddVertexAt(0, to, 0, lw.GetStartWidthAt(0), lw.GetEndWidthAt(0));
                        else lw.AddVertexAt(lw.NumberOfVertices, to, 0,
                            lw.GetStartWidthAt(lw.NumberOfVertices - 1), lw.GetEndWidthAt(lw.NumberOfVertices - 1));
                        written.Add(r.Issue.CurveId);
                        break;
                    }
                    default:
                        skipped.Add($"{r.Issue.CurveId} ({ent?.GetType().Name ?? "?"})");
                        break;
                }
            }
            tr.Commit();
        }
        doc.Editor.UpdateScreen();
        return (written, skipped);
    }

    /// <summary>Support action "הצג קו קצר…": zoom to the next pending reference and mark its short end.</summary>
    private static void ShowShortBoundary()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        if (_referenceIssues.Count == 0)
            throw new UserFacingException("אין קווי גבול קצרים הממתינים לטיפול.\nהריצי Validate.", "no pending reference issues");
        var issue = _referenceIssues[_shortBoundaryCursor % _referenceIssues.Count];
        _shortBoundaryCursor++;
        if (issue.NearEnd is not Mahod.Intergreen.Geometry.Point2D p)
            throw new UserFacingException($"{issue.MovementId}: אין נקודת קצה להצגה (handle {issue.Handle}).", "no near end");

        ClearTransients();
        var x = p.X / _lastScale; var y = p.Y / _lastScale;
        using (doc.LockDocument())
        {
            var tm = TransientManager.CurrentTransientManager;
            var circle = new Circle(new Autodesk.AutoCAD.Geometry.Point3d(x, y, 0), Autodesk.AutoCAD.Geometry.Vector3d.ZAxis, 0.6 / _lastScale)
            { ColorIndex = 1 };
            _transients.Add(circle);
            tm.AddTransient(circle, TransientDrawingMode.DirectShortTerm, 128, new Autodesk.AutoCAD.Geometry.IntegerCollection());

            using var view = doc.Editor.GetCurrentView();
            double size = 12.0 / _lastScale;
            double aspect = view.Height > 1e-9 ? view.Width / view.Height : 1.0;
            view.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d(x, y);
            view.Width = size * Math.Max(aspect, 1.0);
            view.Height = size * Math.Max(1.0 / Math.Max(aspect, 1e-9), 1.0);
            doc.Editor.SetCurrentView(view);
        }
        doc.Editor.UpdateScreen();
        SupportLog.Write("SHOW_SHORT_BOUNDARY", $"{issue.CurveId} handle {issue.Handle} gap {issue.GapCentimetres:F1} cm");
        SetStatus($"{issue.MovementId}: הקצה המסומן באדום חסר {issue.GapCentimetres:F1} ס\"מ עד קו העצירה " +
                  $"({_shortBoundaryCursor}/{_referenceIssues.Count}). לחיצה נוספת מציגה את הבא.");
    }

    /// <summary>Which virtual extensions to write — every row shows the movement and the length; all are ticked by default.</summary>
    private sealed class WpfExtensionPicker
    {
        public IReadOnlyList<ReferenceResolution>? Choose(IReadOnlyList<ReferenceResolution> extended)
        {
            var win = new Window
            {
                Title = "הארכת קווי גבול בשרטוט עד קו העצירה",
                Width = 640, Height = 400, FlowDirection = System.Windows.FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "הקווים הבאים הוארכו וירטואלית בחישוב האחרון. סימון ואישור יאריכו אותם גם בשרטוט — " +
                       "לאורך המשיק של הקצה, עד החיתוך עם קו העצירה. פעולה אחת, ניתנת לביטול ב-Undo. " +
                       "קשתות אינן מוארכות אוטומטית ויישארו לטיפול ידני.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
            var list = new ListBox { Height = 190 };
            var boxes = new List<(CheckBox Box, ReferenceResolution R)>();
            foreach (var r in extended)
            {
                var box = new CheckBox
                {
                    Content = $"{r.Issue.MovementId} — {r.Issue.CurveId}: +{r.Extension!.LengthMeters * 100:F1} ס\"מ",
                    IsChecked = true,
                };
                boxes.Add((box, r));
                list.Items.Add(new ListBoxItem { Content = box });
            }
            root.Children.Add(list);
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "הארך את המסומנים בשרטוט", Width = 190, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
            row.Children.Add(ok); row.Children.Add(cancel);
            root.Children.Add(row);
            win.Content = root;
            List<ReferenceResolution>? result = null;
            ok.Click += (_, _) => { result = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.R).ToList(); win.DialogResult = true; };
            return win.ShowDialog() == true ? result : null;
        }
    }

    /// <summary>
    /// Headless proof: Setup with the given workbook, Validate (the tolerance from the pre-placed sidecar
    /// extends virtually), write every virtual extension into the DWG, re-extract and re-run Validate —
    /// the written boundaries must now meet their stop lines exactly (no extension needed any more).
    /// Writes ig_extend_in_dwg.json next to the drawing; the drawing itself is saved by the caller's script.
    /// </summary>
    [CommandMethod("IG_SMOKE_EXTEND_IN_DWG", CommandFlags.Modal)]
    public void SmokeExtendInDwg()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc!.Editor;
        var res = ed.GetString(new PromptStringOptions("\nWorkbook path") { AllowSpaces = true });
        if (res.Status != PromptStatus.OK) return;
        var dir = Path.GetDirectoryName(doc.Database.Filename)!;
        var outPath = Path.Combine(dir, "ig_extend_in_dwg.json");
        var payload = new Dictionary<string, object?> { ["release_id"] = HostBuild.ReleaseId };
        try
        {
            var wb = SetupService.Validate(res.StringResult.Trim());
            payload["acceptance_status"] = wb.Status.ToString();
            if (wb.IsOk)
            {
                SetupService.Commit(wb, SidecarPath(doc.Database), dir, new Dictionary<string, string>
                {
                    ["unitsConfirmed"] = "meters",
                    ["unitsConfirmedBy"] = "IG_SMOKE_EXTEND_IN_DWG deterministic confirmation",
                });
                _workbookPath = wb.NormalizedPath;
                _lastModel = wb.Model;
                RunPipeline(analyzeOnly: false);
                var extended = _lastResolved.Where(r => r.Kind == ReferenceResolutionKind.Extended).ToList();
                payload["virtual_before"] = extended.Select(r => new Dictionary<string, object?>
                {
                    ["curveId"] = r.Issue.CurveId, ["handle"] = r.Issue.Handle, ["extension_cm"] = Math.Round(r.Extension!.LengthMeters * 100, 2),
                }).ToList();
                payload["pending_before"] = _referenceIssues.Select(i => i.CurveId).ToList();

                var (written, skipped) = WriteExtensions(doc, extended);
                payload["written"] = written;
                payload["skipped"] = skipped;

                _lastOutput = null;
                RunPipeline(analyzeOnly: false);
                payload["virtual_after"] = _lastResolved.Where(r => r.Kind == ReferenceResolutionKind.Extended).Select(r => r.Issue.CurveId).ToList();
                payload["pending_after"] = _referenceIssues.Select(i => i.CurveId).ToList();
                RunPipeline(analyzeOnly: true);
                payload["analyze"] = AnalyzePayload();
            }
        }
        catch (System.Exception ex)
        {
            payload["error"] = ex.Message;
            payload["exception_full_detail"] = ex.ToString();
        }
        File.WriteAllText(outPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
        ed.WriteMessage($"\nIG_SMOKE_EXTEND_IN_DWG → {outPath}");
    }
}
