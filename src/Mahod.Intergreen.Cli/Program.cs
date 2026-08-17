using System.Globalization;
using System.Text;
using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var args_ = Environment.GetCommandLineArgs().Skip(1).ToArray();
string Arg(string name, string? fallback = null)
{
    var i = Array.IndexOf(args_, "--" + name);
    if (i >= 0 && i + 1 < args_.Length) return args_[i + 1];
    return fallback ?? throw new ArgumentException($"missing --{name}");
}

if (args_.Length == 0 || args_[0] != "ig-analyze")
{
    Console.WriteLine("Mahod Intergreen CLI");
    Console.WriteLine("usage: ig-analyze --geometry <iggeometry.json> --workbook <IG_matrix.xlsx> " +
                      "--name <intersection> --units meters --out <dir> [--pack legacy-mahod-v1] [--tolerance 0.25]");
    return 0;
}

var geometryPath = Arg("geometry");
var workbookPath = Arg("workbook");
var intersectionName = Arg("name");
var unitsAssumption = Arg("units");
var outDir = Arg("out");
var packId = Arg("pack", "legacy-mahod-v1");
var tolerance = double.Parse(Arg("tolerance", "0.25"), CultureInfo.InvariantCulture);
Directory.CreateDirectory(outDir);
var started = DateTimeOffset.Now;

// ---------- load canonical geometry export ----------
using var geoDoc = JsonDocument.Parse(File.ReadAllText(geometryPath));
var root = geoDoc.RootElement;
var insUnits = root.GetProperty("units").GetProperty("insunits").GetString()!;
var extraFindings = new List<ValidationFinding>();
double toMeters;
if (root.GetProperty("units").TryGetProperty("toMeters", out var tm) && tm.ValueKind == JsonValueKind.Number)
{
    toMeters = tm.GetDouble();
}
else if (string.Equals(unitsAssumption, "meters", StringComparison.OrdinalIgnoreCase))
{
    toMeters = 1.0;
    extraFindings.Add(new ValidationFinding("IG-UNIT-001", Severity.Warning, null,
        $"Drawing INSUNITS is '{insUnits}'; operator confirmed the drawing is in meters (--units meters).",
        "Unit confirmation is external evidence, cross-checked by the golden CD/ED regression below.",
        SourceReference: "v3 §13"));
}
else
{
    Console.Error.WriteLine($"BLOCKED: INSUNITS='{insUnits}' and no operator unit confirmation (v3 §13).");
    return 2;
}

var curvesByLayer = new Dictionary<string, List<(string Handle, PolyCurve2D Curve)>>(StringComparer.OrdinalIgnoreCase);
foreach (var c in root.GetProperty("curves").EnumerateArray())
{
    var layer = c.GetProperty("layer").GetString()!;
    var handle = c.GetProperty("handle").GetString()!;
    var curve = PolyCurve2D.FromJson(c.GetProperty("geometry").GetRawText());
    if (toMeters != 1.0)
        curve = curve.Transformed(new Transform2D(0, toMeters, 0, 0));
    if (!curvesByLayer.TryGetValue(layer, out var list)) curvesByLayer[layer] = list = new();
    list.Add((handle, curve));
}
foreach (var u in root.GetProperty("unsupported").EnumerateArray())
{
    var uType = u.GetProperty("entityType").GetString();
    var uHandle = u.GetProperty("handle").GetString();
    var uReason = u.GetProperty("reason").GetString();
    extraFindings.Add(new ValidationFinding("IG-GEO-010", Severity.Warning,
        u.GetProperty("layer").GetString(),
        $"UNSUPPORTED entity {uType} ({uHandle}): {uReason}",
        SourceReference: "Directive §7"));
}

// ---------- workbook: parameters, SGs, golden rows ----------
var model = WorkbookReader.Read(workbookPath);
MovementMode ModeOf(string m) => model.MovementParameters.ContainsKey(m)
    ? MovementMode.Vehicle
    : MovementMode.Pedestrian;
string SgOf(string m) => model.SignalGroups.TryGetValue(m, out var sg) ? sg : m;

// ---------- movements from geometry ----------
const string prefix = "intergreen_";
var stopLines = curvesByLayer.TryGetValue(prefix + "stopline", out var sl) ? sl : new();
var movements = new List<PipelineMovement>();
foreach (var (layer, layerCurves) in curvesByLayer.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
{
    var curves = layerCurves.ToList();
    if (!layer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
    var name = layer[prefix.Length..];
    if (name.Equals("stopline", StringComparison.OrdinalIgnoreCase)) continue;
    if (curves.Count == 0) continue;

    var mode = ModeOf(name);

    // ED-010: deterministic debris exclusion. An envelope boundary must span the conflict
    // zone; fragments below 0.5 m are drawing debris. Every exclusion is reported.
    const double microDebrisMeters = 0.5;
    var debris = curves.Where(c => c.Curve.TotalLength < microDebrisMeters).ToList();
    if (debris.Count > 0)
    {
        curves = curves.Where(c => c.Curve.TotalLength >= microDebrisMeters).ToList();
        foreach (var d in debris)
            extraFindings.Add(new ValidationFinding("IG-GEO-011", Severity.Warning, name,
                $"MICRO_DEBRIS_EXCLUDED: {d.Handle} on '{layer}' (length {d.Curve.TotalLength:F3} m < {microDebrisMeters} m).",
                SourceReference: "ENGINEERING_DECISIONS.md ED-010"));
    }

    PolyCurve2D? stopLine = null;
    if (mode != MovementMode.Pedestrian)
    {
        // assign the stop line that gives an exact-intersection reference on the most boundaries
        stopLine = stopLines
            .Select(s => (s.Curve, Score: curves.Count(b =>
                ReferenceStation.Resolve(b.Curve, s.Curve, 0.5)?.Method == ReferenceStation.Method.ExactIntersection)))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Score > 0 ? x.Curve : null)
            .FirstOrDefault();
        if (stopLine is null && stopLines.Count > 0)
            stopLine = stopLines
                .Select(s => (s.Curve, Ok: curves.Any(b => ReferenceStation.Resolve(b.Curve, s.Curve, 0.5) is not null)))
                .Where(x => x.Ok).Select(x => x.Curve).FirstOrDefault();

        // ED-010 second filter: with more than 2 candidates, boundaries are the curves that
        // reference the stop line (Appendix A: every boundary starts at the stop line).
        // Applied only when it resolves to exactly 2; otherwise the strategy reports the error.
        if (curves.Count > 2 && stopLine is not null)
        {
            var referenced = curves
                .Where(c => ReferenceStation.Resolve(c.Curve, stopLine, 0.5) is not null)
                .ToList();
            if (referenced.Count == 2)
            {
                var excluded = curves.Except(referenced).ToList();
                curves = referenced;
                extraFindings.Add(new ValidationFinding("IG-GEO-012", Severity.Warning, name,
                    $"BOUNDARY_AUTO_SELECTED: '{layer}' had {referenced.Count + excluded.Count} candidate curves; " +
                    $"selected {string.Join(",", referenced.Select(r => r.Handle))} (stop-line referenced), " +
                    $"excluded {string.Join(",", excluded.Select(e => $"{e.Handle}(len {e.Curve.TotalLength:F2}m)"))}.",
                    RecommendedAction: "Review the excluded fragments; delete them or register boundaries explicitly.",
                    SourceReference: "v3 §36; Appendix A drawing convention; ED-010"));
            }
        }
    }

    movements.Add(new PipelineMovement(name, mode, SgOf(name),
        new MovementGeometry(name, mode, curves.Select(c => c.Curve).ToList(),
            Array.Empty<PolyCurve2D>(), stopLine),
        curves.Select(c => c.Handle).ToList()));
}

// ---------- pipeline ----------
var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), packId));
var input = new PipelineInput(
    intersectionName,
    Path.GetFileName(root.GetProperty("sourceFile").GetString() ?? geometryPath),
    AnalysisWriters.Sha256OfFile(geometryPath),
    pack,
    new ProjectClassification { RoadType = RoadEnvironment.Urban, PostedSpeedKph = 50 },
    model.Constants, model.Variant, model.MovementParameters, movements);

var output = AnalysisPipeline.Run(input);
var findings = output.Findings.Concat(extraFindings).ToList();

var (analysisPath, validationPath, manifestPath) = AnalysisWriters.WriteAll(
    outDir, intersectionName, output.Analysis, findings,
    runId: Guid.NewGuid().ToString("N"), started, DateTimeOffset.Now - started,
    application: "Mahod.Intergreen.Cli/ig-analyze");

// ---------- golden §10 comparison ----------
var report = new StringBuilder();
report.AppendLine($"# {intersectionName} — real-DWG Legacy regression (Directive §10)");
report.AppendLine();
report.AppendLine($"Geometry: `{Path.GetFileName(geometryPath)}` (INSUNITS={insUnits}, toMeters={toMeters})");
report.AppendLine($"Workbook: `{Path.GetFileName(workbookPath)}`  ·  Rule pack: {packId}  ·  tolerance {tolerance} m");
report.AppendLine();
int match = 0, higherReview = 0, lowerError = 0, notFound = 0, invalidBaseline = 0, additional = 0, comparable = 0;
var lines = new List<string>();

foreach (var row in model.Rows)
{
    var id = $"{row.Input.ClearingMovement}→{row.Input.EnteringMovement}";
    var engine = output.Analysis.Conflicts.FirstOrDefault(c => c.Id == id);
    var manual = row.Input.Point1;

    if (manual.ClearingDistanceMeters is null)
    {
        invalidBaseline++;
        lines.Add($"| {row.Input.ConflictNo} | {id} | INVALID_MANUAL_BASELINE | — | — | incomplete manual measurement |");
        continue;
    }
    comparable++;

    if (engine is null || engine.Status == "ERROR" || engine.Points.Count == 0)
    {
        notFound++;
        var reason = engine is null ? "no engine conflict" : $"engine status {engine.Status}";
        lines.Add($"| {row.Input.ConflictNo} | {id} | MANUAL_POINT_NOT_FOUND | — | — | {reason} |");
        continue;
    }

    var mCd = manual.ClearingDistanceMeters.Value;
    var mEd = manual.EnteringDistanceMeters ?? 0.0;
    var best = engine.Points.OrderBy(p => Math.Abs(p.Cd - mCd) + Math.Abs(p.Ed - mEd)).First();
    var dCd = best.Cd - mCd;
    var dEd = best.Ed - mEd;
    var found = Math.Abs(dCd) <= tolerance && Math.Abs(dEd) <= tolerance;
    if (engine.Points.Count > 1) additional++;

    var manualIg = row.CachedFinalIg is double f ? (int?)(int)f : null;
    string cls;
    if (!found) { cls = "MANUAL_POINT_NOT_FOUND"; notFound++; }
    else if (engine.FinalIg == manualIg) { cls = "MATCH"; match++; }
    else if (engine.FinalIg > manualIg) { cls = "ENGINE_RESULT_HIGHER_REVIEW"; higherReview++; }
    else { cls = "ENGINE_RESULT_LOWER_ERROR"; lowerError++; }

    lines.Add($"| {row.Input.ConflictNo} | {id} | {cls} | ΔCD={dCd:+0.000;-0.000} | ΔED={dEd:+0.000;-0.000} | manual IG={manualIg}, engine IG={engine.FinalIg?.ToString() ?? "—"}, pts={engine.Points.Count} |");
}

report.AppendLine($"""
    ## Summary

    | metric | value |
    |---|---|
    | manual rows | {model.Rows.Count} |
    | comparable (valid manual baseline) | {comparable} |
    | MATCH | {match} |
    | ENGINE_RESULT_HIGHER_REVIEW | {higherReview} |
    | ENGINE_RESULT_LOWER_ERROR | {lowerError} |
    | MANUAL_POINT_NOT_FOUND | {notFound} |
    | INVALID_MANUAL_BASELINE | {invalidBaseline} |
    | rows where automation found additional points | {additional} |
    | engine conflicts total | {output.Analysis.Conflicts.Count} |
    | engine candidate points total | {output.Analysis.Conflicts.Sum(c => c.Points.Count)} |
    | validation errors | {findings.Count(f => f.Severity == Severity.Error)} |
    | validation warnings | {findings.Count(f => f.Severity == Severity.Warning)} |
    | blocked matrix cells | {output.Analysis.Matrix.Count(m => m.Status == "BLOCKED")} |

    ## Rows

    | conflict | pair | classification | ΔCD [m] | ΔED [m] | detail |
    |---|---|---|---|---|---|
    """);
foreach (var l in lines) report.AppendLine(l);

var reportPath = Path.Combine(outDir, intersectionName + "_REPORT.md");
File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));

Console.WriteLine($"ig-analyze OK → {outDir}");
Console.WriteLine($"  movements={movements.Count} conflicts={output.Analysis.Conflicts.Count} " +
                  $"points={output.Analysis.Conflicts.Sum(c => c.Points.Count)} " +
                  $"errors={findings.Count(f => f.Severity == Severity.Error)} warnings={findings.Count(f => f.Severity == Severity.Warning)}");
Console.WriteLine($"  MATCH={match} HIGHER_REVIEW={higherReview} LOWER_ERROR={lowerError} NOT_FOUND={notFound} INVALID_BASELINE={invalidBaseline}");
Console.WriteLine($"  analysis={analysisPath}");
Console.WriteLine($"  report={reportPath}");
return lowerError > 0 ? 1 : 0;
