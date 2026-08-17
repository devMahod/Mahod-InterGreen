using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Mahod.Intergreen.AutoCAD2026;

[assembly: CommandClass(typeof(IgCommands))]
[assembly: ExtensionApplication(typeof(IgApp))]

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>Resolves this plugin's own dependency DLLs from its directory under NETLOAD.</summary>
public class IgApp : IExtensionApplication
{
    public void Initialize()
        => AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new System.Reflection.AssemblyName(args.Name).Name;
            var dir = Path.GetDirectoryName(typeof(IgApp).Assembly.Location);
            if (name is null || dir is null) return null;
            var path = Path.Combine(dir, name + ".dll");
            return File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
        };

    public void Terminate() { }
}

/// <summary>
/// Headless-safe commands (no UI, no palettes — they run in accoreconsole).
/// The adapter is a thin host: no engineering formulas live here (Directive §7).
/// Source geometry is never modified; the drawing is never saved by these commands.
/// </summary>
public class IgCommands
{
    private const string LayerPrefix = "intergreen";

    private static string OutputDir(Database db)
    {
        var env = Environment.GetEnvironmentVariable("IG_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            Directory.CreateDirectory(env);
            return env;
        }
        return Path.GetDirectoryName(db.Filename) ?? Environment.CurrentDirectory;
    }

    private static void Log(Editor ed, string msg) => ed.WriteMessage("\n" + msg);

    /// <summary>
    /// Inventory scan: every layer, entity counts by type, intergreen_* detection,
    /// drawing units. Writes &lt;dwg&gt;.igscan.json. Read-only.
    /// </summary>
    [CommandMethod("IG_SCAN", CommandFlags.Modal)]
    public void Scan()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            using var tr = db.TransactionManager.StartTransaction();

            var layerEntities = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                if (!layerEntities.TryGetValue(ent.Layer, out var counts))
                    layerEntities[ent.Layer] = counts = new Dictionary<string, int>();
                var type = ent.GetType().Name;
                counts[type] = counts.GetValueOrDefault(type) + 1;
            }

            var layers = new List<object>();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lid in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                layerEntities.TryGetValue(ltr.Name, out var counts);
                layers.Add(new
                {
                    name = ltr.Name,
                    colorIndex = ltr.Color.ColorIndex,
                    isIntergreen = ltr.Name.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase),
                    entities = counts?.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, int>(),
                });
            }

            var (unitsName, toMeters) = GeometryExtraction.Units(db);
            var scan = new
            {
                schemaVersion = "1.0",
                sourceFile = Path.GetFileName(db.Filename),
                units = new { insunits = unitsName, toMeters },
                layerCount = layers.Count,
                intergreenLayerCount = layers.Count(l => (bool)l.GetType().GetProperty("isIntergreen")!.GetValue(l)!),
                layers = layers.OrderBy(l => (string)l.GetType().GetProperty("name")!.GetValue(l)!,
                    StringComparer.Ordinal).ToList(),
            };

            var outPath = Path.Combine(OutputDir(db),
                Path.GetFileNameWithoutExtension(db.Filename) + ".igscan.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(scan,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

            tr.Commit();
            Log(ed, $"IG_SCAN OK → {outPath}");
        }
        catch (System.Exception ex)
        {
            Log(ed, $"IG_SCAN ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// Canonical geometry export (Directive §8): exact line/arc/bulge geometry + handles for
    /// every intergreen_* layer, plus any layers named in env IG_EXTRA_LAYERS
    /// (semicolon-separated; used for stop-line layers once identified).
    /// Output reproduces the engine run without reopening AutoCAD.
    /// </summary>
    [CommandMethod("IG_EXPORT_GEOMETRY", CommandFlags.Modal)]
    public void ExportGeometry()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var extra = (Environment.GetEnvironmentVariable("IG_EXTRA_LAYERS") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var tr = db.TransactionManager.StartTransaction();
            var result = GeometryExtraction.ExtractLayers(db, tr,
                layer => layer.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase)
                         || extra.Contains(layer));
            tr.Commit();

            var outPath = Path.Combine(OutputDir(db),
                Path.GetFileNameWithoutExtension(db.Filename) + ".iggeometry.json");
            File.WriteAllText(outPath,
                GeometryExtraction.ToJson(Path.GetFileName(db.Filename), result),
                new UTF8Encoding(false));

            Log(ed, $"IG_EXPORT_GEOMETRY OK → {outPath}");
            Log(ed, $"  curves: {result.Curves.Count}  unsupported: {result.Unsupported.Count}  units: {result.InsUnits}");
            foreach (var u in result.Unsupported.Take(20))
                Log(ed, $"  UNSUPPORTED {u.EntityType} on '{u.Layer}' ({u.Handle}): {u.Reason}");
        }
        catch (System.Exception ex)
        {
            Log(ed, $"IG_EXPORT_GEOMETRY ERROR: {ex.Message}");
        }
    }

    [CommandMethod("IG_ABOUT", CommandFlags.Modal)]
    public void About()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        Log(doc.Editor, $"Mahod Intergreen adapter — engine {Mahod.Intergreen.Reporting.AnalysisPipeline.EngineVersion}. " +
            "Commands: IG_SCAN, IG_EXPORT_GEOMETRY, IG_ABOUT. This host contains no engineering formulas.");
    }
}
