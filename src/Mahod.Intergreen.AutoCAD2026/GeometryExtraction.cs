using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Mahod.Intergreen.Geometry;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>One extracted curve with full CAD provenance.</summary>
public sealed record ExtractedCurve(
    string Handle,
    string EntityType,
    string Layer,
    int ColorIndex,
    PolyCurve2D Geometry);

public sealed record UnsupportedEntity(string Handle, string EntityType, string Layer, string Reason);

public sealed record ExtractionResult(
    List<ExtractedCurve> Curves,
    List<UnsupportedEntity> Unsupported,
    string InsUnits,
    double? ToMeters);

/// <summary>
/// Reads exact drawing geometry (Directive §7): Line, Arc, LWPolyline with bulges,
/// 2D Polyline. Never tessellates; anything else is reported UNSUPPORTED_GEOMETRY,
/// never silently skipped. Source geometry is opened for read only.
/// </summary>
public static class GeometryExtraction
{
    public static ExtractionResult ExtractLayers(Database db, Transaction tr, Func<string, bool> layerFilter)
    {
        var curves = new List<ExtractedCurve>();
        var unsupported = new List<UnsupportedEntity>();

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
            if (!layerFilter(ent.Layer)) continue;
            ExtractEntity(tr, ent, ent.Layer, ent.Handle.ToString(), depth: 0, curves, unsupported);
        }

        var (insName, toMeters) = Units(db);
        return new ExtractionResult(curves, unsupported, insName, toMeters);
    }

    /// <summary>
    /// Extracts one entity, recursing into relevant BlockReferences (Directive §10):
    /// nested supported geometry is transformed by the full block transform via AutoCAD's
    /// own exact entity math, provenance is preserved as parentHandle/childHandle path,
    /// and the effective layer follows the ByBlock convention (child on layer "0" inherits
    /// the reference's layer). Recursion is loop-guarded by depth.
    /// </summary>
    private static void ExtractEntity(Transaction tr, Entity ent, string effectiveLayer,
        string handlePath, int depth, List<ExtractedCurve> curves, List<UnsupportedEntity> unsupported)
    {
        const int maxDepth = 8;
        var handle = handlePath;
        var color = ent.ColorIndex;

        if (ent is BlockReference br)
        {
            if (depth >= maxDepth)
            {
                unsupported.Add(new(handle, $"BlockReference({br.Name})", effectiveLayer,
                    $"recursion depth {maxDepth} exceeded — possible block loop"));
                return;
            }
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId childId in btr)
            {
                if (tr.GetObject(childId, OpenMode.ForRead) is not Entity child) continue;
                var childLayer = child.Layer == "0" ? effectiveLayer : child.Layer;
                Entity clone;
                try
                {
                    clone = (Entity)child.Clone();
                    clone.TransformBy(br.BlockTransform);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    unsupported.Add(new($"{handle}/{child.Handle}", child.GetType().Name, childLayer,
                        $"block transform failed: {ex.ErrorStatus} (e.g. non-uniform scale)"));
                    continue;
                }
                try
                {
                    ExtractEntity(tr, clone, childLayer, $"{handle}/{child.Handle}", depth + 1, curves, unsupported);
                }
                finally
                {
                    clone.Dispose();
                }
            }
            return;
        }

        switch (ent)
            {
                case AcDb.Line line:
                    if (HasZ(line.StartPoint.Z, line.EndPoint.Z))
                    {
                        unsupported.Add(new(handle, "Line", effectiveLayer, "NON_PLANAR_GEOMETRY"));
                        break;
                    }
                    curves.Add(new(handle, "Line", effectiveLayer, color, new PolyCurve2D(new ISegment2D[]
                    {
                        new LineSegment2D(P(line.StartPoint), P(line.EndPoint)),
                    })));
                    break;

                case AcDb.Arc arc:
                    if (HasZ(arc.Center.Z))
                    {
                        unsupported.Add(new(handle, "Arc", effectiveLayer, "NON_PLANAR_GEOMETRY"));
                        break;
                    }
                    // AutoCAD arcs are always CCW from StartAngle to EndAngle
                    var sweep = arc.EndAngle - arc.StartAngle;
                    if (sweep <= 0) sweep += Math.Tau;
                    curves.Add(new(handle, "Arc", effectiveLayer, color, new PolyCurve2D(new ISegment2D[]
                    {
                        new CircularArcSegment2D(new Point2D(arc.Center.X, arc.Center.Y),
                            arc.Radius, arc.StartAngle, sweep),
                    })));
                    break;

                case Polyline lw:
                {
                    if (Math.Abs(lw.Elevation) > 1e-9)
                    {
                        unsupported.Add(new(handle, "LWPolyline", effectiveLayer, "NON_PLANAR_GEOMETRY"));
                        break;
                    }
                    var vertices = new List<(Point2D, double)>();
                    for (var i = 0; i < lw.NumberOfVertices; i++)
                    {
                        var p = lw.GetPoint2dAt(i);
                        vertices.Add((new Point2D(p.X, p.Y), lw.GetBulgeAt(i)));
                    }
                    try
                    {
                        curves.Add(new(handle, "LWPolyline", effectiveLayer, color,
                            PolyCurve2D.FromVertices(vertices, lw.Closed)));
                    }
                    catch (ArgumentException ex)
                    {
                        unsupported.Add(new(handle, "LWPolyline", effectiveLayer, "DEGENERATE_GEOMETRY_EXCLUDED: " + ex.Message));
                    }
                    break;
                }

                case Polyline2d p2d:
                {
                    var vertices = new List<(Point2D, double)>();
                    foreach (ObjectId vid in p2d)
                    {
                        if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                            vertices.Add((new Point2D(v.Position.X, v.Position.Y), v.Bulge));
                    }
                    try
                    {
                        if (vertices.Count >= 2)
                            curves.Add(new(handle, "Polyline2d", effectiveLayer, color,
                                PolyCurve2D.FromVertices(vertices, p2d.Closed)));
                        else
                            unsupported.Add(new(handle, "Polyline2d", effectiveLayer, "DEGENERATE_GEOMETRY_EXCLUDED: fewer than 2 distinct vertices"));
                    }
                    catch (ArgumentException ex)
                    {
                        unsupported.Add(new(handle, "Polyline2d", effectiveLayer, "DEGENERATE_GEOMETRY_EXCLUDED: " + ex.Message));
                    }
                    break;
                }


                default:
                    unsupported.Add(new(handle, ent.GetType().Name, effectiveLayer, "UNSUPPORTED_GEOMETRY"));
                    break;
            }
    }

    private static bool HasZ(params double[] zs) => zs.Any(z => Math.Abs(z) > 1e-6);

    private static Point2D P(Autodesk.AutoCAD.Geometry.Point3d p) => new(p.X, p.Y);

    /// <summary>INSUNITS → meters factor. Unitless/unknown → null (blocks engineering analysis, v3 §13).</summary>
    public static (string Name, double? ToMeters) Units(Database db) => db.Insunits switch
    {
        UnitsValue.Meters => ("Meters", 1.0),
        UnitsValue.Millimeters => ("Millimeters", 0.001),
        UnitsValue.Centimeters => ("Centimeters", 0.01),
        UnitsValue.Decimeters => ("Decimeters", 0.1),
        UnitsValue.Kilometers => ("Kilometers", 1000.0),
        UnitsValue.Undefined => ("Unitless", null),
        var other => (other.ToString(), null),
    };

    /// <summary>Canonical geometry export (Directive §8) — exact segments + provenance, JSON.</summary>
    public static string ToJson(string sourceFile, ExtractionResult result)
    {
        var doc = new
        {
            schemaVersion = "1.0",
            sourceFile,
            units = new { insunits = result.InsUnits, toMeters = result.ToMeters },
            curves = result.Curves
                .OrderBy(c => c.Layer, StringComparer.Ordinal)
                .ThenBy(c => c.Handle, StringComparer.Ordinal)
                .Select(c => new
                {
                    handle = c.Handle,
                    entityType = c.EntityType,
                    layer = c.Layer,
                    colorIndex = c.ColorIndex,
                    lengthDrawingUnits = Math.Round(c.Geometry.TotalLength, 9),
                    geometry = JsonSerializer.Deserialize<JsonElement>(c.Geometry.ToJson()),
                }).ToList(),
            unsupported = result.Unsupported
                .OrderBy(u => u.Layer, StringComparer.Ordinal)
                .ThenBy(u => u.Handle, StringComparer.Ordinal)
                .Select(u => new { handle = u.Handle, entityType = u.EntityType, layer = u.Layer, reason = u.Reason })
                .ToList(),
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }
}
