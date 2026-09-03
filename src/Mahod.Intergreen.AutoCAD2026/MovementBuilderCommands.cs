using System.IO;
using System.Text.Json;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Mahod.Intergreen.Host;
using Mahod.Intergreen.Reporting;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// r14 WP6 — the assisted movement builder of <c>הגדרת פעולה.docx</c> Appendix A (David 2026-08-27, items 3
/// and 5). The traffic planner's curbs and lane markings stay exactly where and how they are: the engineer
/// points at a line, the tool COPIES it onto the movement's <c>intergreen_&lt;approach&gt;-&lt;turn&gt;</c>
/// layer in the approach colour (Appendix B / <see cref="ColourModel"/>), asks which end is the stop-line
/// end and turns the copy to start there (David's convention). Two boundaries and the stop line per
/// movement; a crossing is two edges on <c>intergreen_&lt;letter&gt;</c>. Source entities inside blocks or
/// xrefs are refused, never edited. Every movement is one Undo. What was picked (source handles and
/// source layers) is written to the project sidecar so the next project can be OFFERED the same layers —
/// never given them silently.
/// </summary>
public partial class IgWorkflowCommands
{
    private const string BuiltMovementsSidecarKey = "builtMovements";
    private const string BaseLayersSidecarKey = "baseLayers";

    /// <summary>Palette action "בניית תנועות…": Appendix A, movement after movement, until Enter/Done.</summary>
    private static void BuildMovements()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument
            ?? throw new UserFacingException("אין שרטוט פתוח.", "no active document");
        if (string.IsNullOrEmpty(doc.Database.Filename) || !File.Exists(doc.Database.Filename))
            throw new UserFacingException("השרטוט עדיין לא נשמר.\nשמרי את ה-DWG ואז נסי שוב.", "unsaved drawing");
        var ed = doc.Editor;
        var scPath = SidecarPath(doc.Database);
        var previous = SidecarStore.GetStrings(SidecarStore.Load(scPath).Data, BaseLayersSidecarKey).ToList();
        if (previous.Count > 0)
            ed.WriteMessage("\n[INTERGREEN] בפרויקט הזה נלקחו קווים מהשכבות: " + string.Join(", ", previous) + " (מידע בלבד — כל קו נבחר מחדש).");

        var built = 0;
        while (true)
        {
            var approach = Keyword(ed, "\nגישת המוצא של התנועה [N/E/S/W] או Enter לסיום", "N E S W Done", "Done");
            if (approach is null || approach == "Done") break;
            var turn = Keyword(ed, "\nסוג התנועה [R=ימינה / T=ישר / L=שמאלה / P=מעבר חצייה]", "R T L P", null);
            if (turn is null) break;

            string layer;
            string movementId;
            if (turn == "P")
            {
                var letter = ed.GetString(new PromptStringOptions("\nאות המעבר (a..l, אות קטנה)") { AllowSpaces = false });
                if (letter.Status != PromptStatus.OK) break;
                var name = letter.StringResult.Trim().ToLowerInvariant();
                if (name.Length != 1 || name[0] < 'a' || name[0] > 'z')
                { ed.WriteMessage("\n[INTERGREEN] אות אחת קטנה בלבד (a..z)."); continue; }
                movementId = name;
                layer = ProjectAssembly.LayerPrefix + name;
            }
            else
            {
                movementId = $"{approach}-{turn}";
                layer = ProjectAssembly.LayerPrefix + movementId;
            }

            var first = PickBoundary(ed, turn == "P" ? "\nהקצה הראשון של מעבר החצייה — בחרי את הקו" : "\nגבול התנועה הראשון — בחרי את הקו המשורטט");
            if (first is null) break;
            var second = PickBoundary(ed, turn == "P" ? "\nהקצה השני של מעבר החצייה — בחרי את הקו" : "\nגבול התנועה השני — בחרי את הקו המשורטט");
            if (second is null) break;
            ObjectId stopLine = ObjectId.Null;
            if (turn != "P")
            {
                var stop = PickBoundary(ed, "\nקו העצירה של הגישה — בחרי את הקו (או Enter אם כבר קיים בשכבת intergreen_stopline)", allowEmpty: true);
                if (stop is ObjectId s && !s.IsNull) stopLine = s;
            }

            var result = BuildOne(doc, movementId, layer, first.Value, second.Value, stopLine, askStartEnds: turn != "P");
            if (result.Error is string err) { ed.WriteMessage("\n[INTERGREEN] " + err); continue; }
            built++;
            RecordBuilt(scPath, movementId, result);
            SupportLog.Write("MOVEMENT_BUILT",
                $"{movementId} layer={layer} aci={result.Aci} copies={string.Join(",", result.CopiedHandles)} sources={string.Join(",", result.SourceHandles)} sourceLayers={string.Join(",", result.SourceLayers)}");
            ed.WriteMessage($"\n[INTERGREEN] {movementId}: נבנה בשכבה {layer} (צבע {result.Aci}). Undo מבטל את התנועה הזו.");
        }
        SetStatus(built == 0
            ? "בניית התנועות הסתיימה — לא נבנתה תנועה."
            : (built == 1 ? "נבנתה תנועה אחת." : $"נבנו {built} תנועות/מעברים.") +
              " עכשיו: \"Excel חדש מהשרטוט…\" (פרויקט חדש) או Setup עם Excel קיים, ואז Validate.");
    }

    private static string? Keyword(Editor ed, string message, string keywords, string? defaultKeyword)
    {
        var opts = new PromptKeywordOptions(message) { AllowNone = defaultKeyword is not null };
        foreach (var k in keywords.Split(' ')) opts.Keywords.Add(k);
        if (defaultKeyword is not null) opts.Keywords.Default = defaultKeyword;
        var r = ed.GetKeywords(opts);
        if (r.Status == PromptStatus.None) return defaultKeyword;
        return r.Status == PromptStatus.OK ? r.StringResult : null;
    }

    /// <summary>A Line, Arc or LWPolyline in model space — not inside a block or an xref (those are the planner's, never edited).</summary>
    private static ObjectId? PickBoundary(Editor ed, string message, bool allowEmpty = false)
    {
        while (true)
        {
            var opts = new PromptEntityOptions(message) { AllowNone = allowEmpty };
            opts.SetRejectMessage("\n[INTERGREEN] רק Line / Arc / Polyline.");
            opts.AddAllowedClass(typeof(AcDb.Line), true);
            opts.AddAllowedClass(typeof(AcDb.Arc), true);
            opts.AddAllowedClass(typeof(AcDb.Polyline), true);
            var r = ed.GetEntity(opts);
            if (r.Status == PromptStatus.None && allowEmpty) return ObjectId.Null;
            if (r.Status != PromptStatus.OK) return null;
            // GetEntity on a block/xref returns the reference itself, which is not an allowed class — refused above.
            return r.ObjectId;
        }
    }

    private sealed record BuildOneResult(short Aci, List<string> CopiedHandles, List<string> SourceHandles, List<string> SourceLayers, string? Error);

    /// <summary>
    /// The write: ensure the layer (approach colour), copy each picked entity onto it, orient each copy to
    /// start at the end the engineer names (David's convention — the stop-line end first), copy the stop
    /// line onto <c>intergreen_stopline</c>. One document lock, one transaction, one Undo. Sources untouched.
    /// </summary>
    private static BuildOneResult BuildOne(Autodesk.AutoCAD.ApplicationServices.Document doc, string movementId, string layer,
        ObjectId first, ObjectId second, ObjectId stopLine, bool askStartEnds)
    {
        var db = doc.Database;
        var ed = doc.Editor;
        var aci = ColourModel.LayerAci(layer);
        var copied = new List<string>();
        var sources = new List<string>();
        var sourceLayers = new List<string>();
        using (doc.LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            EnsureLayer(db, tr, layer, aci);
            if (!stopLine.IsNull) EnsureLayer(db, tr, ColourModel.StopLineLayer, ColourModel.WhiteAci);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            foreach (var (id, index) in new[] { (first, 1), (second, 2) })
            {
                var src = (Entity)tr.GetObject(id, OpenMode.ForRead);
                sources.Add(src.Handle.ToString());
                sourceLayers.Add(src.Layer);
                var copy = (Entity)src.Clone();
                copy.Layer = layer;
                copy.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                space.AppendEntity(copy);
                tr.AddNewlyCreatedDBObject(copy, true);
                copied.Add(copy.Handle.ToString());

                if (askStartEnds && copy is Curve curve)
                {
                    var pick = ed.GetPoint(new PromptPointOptions($"\n{movementId} גבול {index}: הקישי ליד הקצה שעל קו העצירה (נקודת ההתחלה)") { AllowNone = true });
                    if (pick.Status == PromptStatus.OK)
                    {
                        var p = pick.Value;
                        var dStart = p.DistanceTo(curve.StartPoint);
                        var dEnd = p.DistanceTo(curve.EndPoint);
                        if (dEnd < dStart)
                        {
                            try { curve.ReverseCurve(); }
                            catch (System.Exception ex) { SupportLog.Write("REVERSE_FAILED", $"{movementId} b{index} {copy.Handle}: {ex.Message}"); }
                        }
                    }
                }
            }

            if (!stopLine.IsNull)
            {
                var src = (Entity)tr.GetObject(stopLine, OpenMode.ForRead);
                sources.Add(src.Handle.ToString());
                sourceLayers.Add(src.Layer);
                var copy = (Entity)src.Clone();
                copy.Layer = ColourModel.StopLineLayer;
                copy.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                space.AppendEntity(copy);
                tr.AddNewlyCreatedDBObject(copy, true);
                copied.Add(copy.Handle.ToString());
            }
            tr.Commit();
        }
        ed.UpdateScreen();
        return new BuildOneResult(aci, copied, sources, sourceLayers.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), null);
    }

    private static void EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
        {
            var existing = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            existing.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);   // the Appendix B colour, even on a layer the engineer made earlier
            return;
        }
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
    }

    /// <summary>What was built and where it came from — the next project is OFFERED these layers, never given them.</summary>
    private static void RecordBuilt(string scPath, string movementId, BuildOneResult r)
    {
        var store = SidecarStore.Load(scPath);
        SidecarStore.SetStrings(store.Data, BuiltMovementsSidecarKey,
            SidecarStore.GetStrings(store.Data, BuiltMovementsSidecarKey)
                .Concat(new[] { $"{movementId}:{string.Join("+", r.CopiedHandles)}<-{string.Join("+", r.SourceHandles)}" }));
        SidecarStore.SetStrings(store.Data, BaseLayersSidecarKey,
            SidecarStore.GetStrings(store.Data, BaseLayersSidecarKey).Concat(r.SourceLayers).Distinct(StringComparer.OrdinalIgnoreCase));
        SidecarStore.Commit(scPath, store.Data);
    }

    /// <summary>
    /// Headless proof of the builder without picks: "movementId layer? handle1 handle2 [stopHandle]" per line, then an
    /// empty line. Builds each, then re-extracts the intergreen_* layers and reports what the engine now sees.
    /// Writes ig_build.json next to the drawing.
    /// </summary>
    [CommandMethod("IG_SMOKE_BUILD", CommandFlags.Modal)]
    public void SmokeBuild()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc!.Editor;
        var dir = Path.GetDirectoryName(doc.Database.Filename)!;
        var outPath = Path.Combine(dir, "ig_build.json");
        var payload = new Dictionary<string, object?> { ["release_id"] = HostBuild.ReleaseId };
        var results = new List<Dictionary<string, object?>>();
        try
        {
            while (true)
            {
                var line = ed.GetString(new PromptStringOptions("\nmovement handle1 handle2 [stopHandle] (empty = done)") { AllowSpaces = true });
                if (line.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(line.StringResult)) break;
                var parts = line.StringResult.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var movementId = parts[0];
                var layer = ProjectAssembly.LayerPrefix + movementId;
                ObjectId Id(string h) => doc.Database.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) ? id : ObjectId.Null;
                var r = BuildOne(doc, movementId, layer, Id(parts[1]), Id(parts[2]), parts.Length > 3 ? Id(parts[3]) : ObjectId.Null, askStartEnds: false);
                results.Add(new Dictionary<string, object?>
                {
                    ["movement"] = movementId, ["layer"] = layer, ["aci"] = r.Aci,
                    ["copied"] = r.CopiedHandles, ["sources"] = r.SourceHandles, ["sourceLayers"] = r.SourceLayers, ["error"] = r.Error,
                });
                RecordBuilt(SidecarPath(doc.Database), movementId, r);
            }
            payload["built"] = results;
            var (curvesByLayer, units) = ExtractProjectCurves(doc.Database, SidecarPath(doc.Database));
            payload["units"] = units;
            payload["layers_after"] = curvesByLayer.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            var findings = new List<Mahod.Intergreen.Contracts.ValidationFinding>();
            var movements = ProjectAssembly.BuildMovements(curvesByLayer, new Dictionary<string, string>(), new Dictionary<string, double>(),
                ProjectSidecar.Load(SidecarPath(doc.Database)), findings);
            payload["movements_after"] = movements.Select(m => new Dictionary<string, object?>
            {
                ["id"] = m.Id, ["mode"] = m.Mode.ToString(), ["boundaries"] = m.Geometry.Boundaries.Count, ["stopLine"] = m.Geometry.StopLine is not null,
            }).ToList();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                var colours = new Dictionary<string, int>();
                foreach (ObjectId lid in lt)
                {
                    var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    if (ltr.Name.StartsWith(ProjectAssembly.LayerPrefix, StringComparison.OrdinalIgnoreCase))
                        colours[ltr.Name] = ltr.Color.ColorIndex;
                }
                payload["layer_colours"] = colours;
                tr.Commit();
            }
        }
        catch (System.Exception ex)
        {
            payload["error"] = ex.Message;
            payload["exception_full_detail"] = ex.ToString();
        }
        File.WriteAllText(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
        ed.WriteMessage($"\nIG_SMOKE_BUILD → {outPath}");
    }
}
