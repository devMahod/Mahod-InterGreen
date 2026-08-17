using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Geometry;

namespace Mahod.Intergreen.Reporting;

/// <summary>Minimal typed view of the project sidecar (Directive §12).</summary>
public sealed record ProjectSidecar(
    string? UnitsConfirmed,
    string? UnitsConfirmedBy,
    IReadOnlySet<string> ConfirmedEndpointReferences,
    IReadOnlyDictionary<string, string> MovementModes,
    IReadOnlyDictionary<string, double> PedestrianWidths,
    RoadEnvironment? RoadType,
    double? PostedSpeedKph)
{
    public static ProjectSidecar Empty { get; } = new(null, null,
        new HashSet<string>(), new Dictionary<string, string>(), new Dictionary<string, double>(), null, null);

    public static ProjectSidecar Load(string path)
    {
        if (!System.IO.File.Exists(path)) return Empty;
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        string? Str(params string[] p)
        {
            var el = root;
            foreach (var k in p)
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(k, out el)) return null;
            }
            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }

        var refs = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("confirmedEndpointReferences", out var cer) && cer.ValueKind == JsonValueKind.Array)
            foreach (var e in cer.EnumerateArray())
                if (e.GetString() is string s) refs.Add(s);

        var modes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("movements", out var mv) && mv.ValueKind == JsonValueKind.Object)
            foreach (var p in mv.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("mode", out var md)
                    && md.GetString() is string mode)
                    modes[p.Name] = mode;

        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("pedestrianWidths", out var pw) && pw.ValueKind == JsonValueKind.Object)
            foreach (var p in pw.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number)
                    widths[p.Name] = p.Value.GetDouble();

        RoadEnvironment? road = Str("classification", "roadType") is string rt
            && Enum.TryParse<RoadEnvironment>(rt, true, out var rr) ? rr : null;
        double? posted = null;
        if (root.TryGetProperty("classification", out var cls) && cls.ValueKind == JsonValueKind.Object
            && cls.TryGetProperty("postedSpeedKph", out var pk) && pk.ValueKind == JsonValueKind.Number)
            posted = pk.GetDouble();

        return new ProjectSidecar(
            Str("unitsConfirmed") ?? Str("units", "confirmed"), Str("unitsConfirmedBy"),
            refs, modes, widths, road, posted);
    }
}

/// <summary>
/// Shared assembly of pipeline movements from extracted curves + workbook + sidecar —
/// used identically by the CLI and the AutoCAD host so both run THE SAME logic
/// (Directive §2: hosts are thin; no second engine).
/// </summary>
public static class ProjectAssembly
{
    public const string LayerPrefix = "intergreen_";

    private static readonly Regex VehiclePattern = new("^[NSEW]{1,2}-[LTRU]$", RegexOptions.CultureInvariant);
    private static readonly Regex SuffixPattern = new("^[NSEW]{1,2}-[LTRU]-(bus|sherut)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static (MovementMode Mode, bool Known) ResolveMode(string name, ProjectSidecar sidecar, IReadOnlyDictionary<string, double> pedestrianWidths)
    {
        if (sidecar.MovementModes.TryGetValue(name, out var m0) && Enum.TryParse<MovementMode>(m0, true, out var mm))
            return (mm, true);
        if (pedestrianWidths.ContainsKey(name))
            return (MovementMode.Pedestrian, true);
        if (VehiclePattern.IsMatch(name))
            return (MovementMode.Vehicle, true);
        var sm = SuffixPattern.Match(name);
        if (sm.Success)
            return (sm.Groups[1].Value.Equals("bus", StringComparison.OrdinalIgnoreCase)
                ? MovementMode.Bus : MovementMode.Sherut, true);
        if (name.Length == 1 && char.IsAsciiLetterLower(name[0]))
            return (MovementMode.Pedestrian, true);
        return (MovementMode.Vehicle, false);
    }

    public static List<PipelineMovement> BuildMovements(
        IReadOnlyDictionary<string, List<(string Handle, PolyCurve2D Curve)>> curvesByLayer,
        IReadOnlyDictionary<string, string> signalGroups,
        IReadOnlyDictionary<string, double> pedestrianWidths,
        ProjectSidecar sidecar,
        List<ValidationFinding> findings)
    {
        var stopLines = curvesByLayer.TryGetValue(LayerPrefix + "stopline", out var sl)
            ? sl : new List<(string, PolyCurve2D)>();
        var movements = new List<PipelineMovement>();

        foreach (var (layer, layerCurves) in curvesByLayer.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var curves = layerCurves.ToList();
            if (!layer.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var name = layer[LayerPrefix.Length..];
            if (name.Equals("stopline", StringComparison.OrdinalIgnoreCase)) continue;
            if (curves.Count == 0) continue;

            var (mode, known) = ResolveMode(name, sidecar, pedestrianWidths);
            if (!known)
            {
                findings.Add(new ValidationFinding("IG-MOV-001", Severity.Error, name,
                    $"UNKNOWN_MOVEMENT_MODE: layer '{layer}' does not match any explicit source " +
                    "(sidecar, Pedestrian Xing, validated naming convention). The movement is excluded.",
                    RecommendedAction: "Map the movement in Project Setup.",
                    SourceReference: "Directive §15"));
                continue;
            }

            const double microDebrisMeters = 0.5;
            var debris = curves.Where(c => c.Curve.TotalLength < microDebrisMeters).ToList();
            if (debris.Count > 0)
            {
                curves = curves.Where(c => c.Curve.TotalLength >= microDebrisMeters).ToList();
                foreach (var d in debris)
                    findings.Add(new ValidationFinding("IG-GEO-011", Severity.Warning, name,
                        $"DEGENERATE_GEOMETRY_EXCLUDED: {d.Handle} on '{layer}' (length {d.Curve.TotalLength:F3} m).",
                        SourceReference: "Directive §11; ED-010"));
            }

            PolyCurve2D? stopLine = null;
            if (mode != MovementMode.Pedestrian)
            {
                stopLine = stopLines
                    .Select(s => (Curve: s.Item2, Score: curves.Count(b =>
                        ReferenceStation.Resolve(b.Curve, s.Item2, 0.5)?.Method == ReferenceStation.Method.ExactIntersection)))
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Score > 0 ? x.Curve : null)
                    .FirstOrDefault();
                if (stopLine is null && stopLines.Count > 0)
                    stopLine = stopLines
                        .Select(s => (Curve: s.Item2, Ok: curves.Any(b => ReferenceStation.Resolve(b.Curve, s.Item2, 0.5) is not null)))
                        .Where(x => x.Ok).Select(x => x.Curve).FirstOrDefault();

                if (curves.Count > 2 && stopLine is not null)
                {
                    var referenced = curves
                        .Where(c => ReferenceStation.Resolve(c.Curve, stopLine, 0.5) is not null)
                        .ToList();
                    if (referenced.Count == 2)
                    {
                        var excluded = curves.Except(referenced).ToList();
                        curves = referenced;
                        findings.Add(new ValidationFinding("IG-GEO-012", Severity.Warning, name,
                            $"BOUNDARY_AUTO_SELECTED: '{layer}' — selected {string.Join(",", referenced.Select(r => r.Handle))}, " +
                            $"excluded {string.Join(",", excluded.Select(e => $"{e.Handle}(len {e.Curve.TotalLength:F2}m)"))}.",
                            RecommendedAction: "Review the excluded fragments; delete them or register boundaries explicitly.",
                            SourceReference: "v3 §36; ED-010"));
                    }
                }
            }

            var width = sidecar.PedestrianWidths.TryGetValue(name, out var w0) ? w0
                : pedestrianWidths.TryGetValue(name, out var w1) ? w1 : (double?)null;

            movements.Add(new PipelineMovement(name, mode,
                signalGroups.TryGetValue(name, out var sg) ? sg : name,
                new MovementGeometry(name, mode, curves.Select(c => c.Curve).ToList(),
                    Array.Empty<PolyCurve2D>(), stopLine)
                {
                    PedestrianWidthMeters = mode == MovementMode.Pedestrian ? width : null,
                    ConfirmedEndpointReferences = sidecar.ConfirmedEndpointReferences,
                },
                curves.Select(c => c.Handle).ToList()));
        }
        return movements;
    }
}
