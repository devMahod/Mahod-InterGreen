using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var args_ = Environment.GetCommandLineArgs().Skip(1).ToArray();
string? ArgOpt(string name)
{
    var i = Array.IndexOf(args_, "--" + name);
    return i >= 0 && i + 1 < args_.Length ? args_[i + 1] : null;
}
string Arg(string name, string? fallback = null)
    => ArgOpt(name) ?? fallback ?? throw new ArgumentException($"missing --{name}");

if (args_.Length == 0 || args_[0] != "ig-analyze")
{
    Console.WriteLine("Mahod Intergreen CLI");
    Console.WriteLine("usage: ig-analyze --geometry <iggeometry.json> --workbook <IG_matrix.xlsx> " +
                      "--name <intersection> --out <dir> [--sidecar <intergreen-project.json>] " +
                      "[--units meters] [--pack legacy-mahod-v1]");
    return 0;
}

var geometryPath = Path.GetFullPath(Arg("geometry"));
var workbookPath = Path.GetFullPath(Arg("workbook"));
var intersectionName = Arg("name");
var outDir = Arg("out");
var packId = Arg("pack", "legacy-mahod-v1");
var sidecarPath = ArgOpt("sidecar")
    ?? Path.Combine(Path.GetDirectoryName(geometryPath)!,
        Path.GetFileNameWithoutExtension(geometryPath).Replace(".iggeometry", "") + ".intergreen-project.json");
Directory.CreateDirectory(outDir);
var started = DateTimeOffset.Now;
var extraFindings = new List<ValidationFinding>();

// ---------- project sidecar (Directive §12) ----------
JsonElement? sidecar = null;
if (File.Exists(sidecarPath))
{
    sidecar = JsonDocument.Parse(File.ReadAllText(sidecarPath)).RootElement.Clone();
    extraFindings.Add(new ValidationFinding("IG-PRJ-001", Severity.Warning, null,
        $"Project sidecar loaded: {Path.GetFileName(sidecarPath)}.",
        SourceReference: "Directive §12"));
}
string? SidecarStr(params string[] path)
{
    if (sidecar is not JsonElement el) return null;
    foreach (var p in path)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(p, out el)) return null;
    }
    return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
var confirmedRefs = new HashSet<string>(StringComparer.Ordinal);
if (sidecar is JsonElement sc && sc.TryGetProperty("confirmedEndpointReferences", out var cer)
    && cer.ValueKind == JsonValueKind.Array)
    foreach (var e in cer.EnumerateArray())
        if (e.GetString() is string s) confirmedRefs.Add(s);

// ---------- geometry ----------
using var geoDoc = JsonDocument.Parse(File.ReadAllText(geometryPath));
var root = geoDoc.RootElement;
var insUnits = root.GetProperty("units").GetProperty("insunits").GetString()!;

// §13 production units policy: known CAD unit → auto; else explicit confirmation
// (sidecar preferred, CLI flag accepted and recorded); otherwise BLOCKED.
double toMeters;
if (root.GetProperty("units").TryGetProperty("toMeters", out var tm) && tm.ValueKind == JsonValueKind.Number)
{
    toMeters = tm.GetDouble();
}
else
{
    var confirmed = SidecarStr("units", "confirmed") ?? SidecarStr("unitsConfirmed") ?? ArgOpt("units");
    if (string.Equals(confirmed, "meters", StringComparison.OrdinalIgnoreCase))
    {
        toMeters = 1.0;
        var provenance = SidecarStr("unitsConfirmed") is not null || SidecarStr("units", "confirmed") is not null
            ? $"project sidecar {Path.GetFileName(sidecarPath)}"
            : "operator CLI flag --units meters";
        extraFindings.Add(new ValidationFinding("IG-UNIT-001", Severity.Warning, null,
            $"Drawing INSUNITS is '{insUnits}'; unit METERS explicitly confirmed via {provenance}.",
            SourceReference: "Directive §13"));
    }
    else
    {
        Console.Error.WriteLine($"BLOCKED: INSUNITS='{insUnits}' and no explicit unit confirmation " +
                                "(sidecar or --units meters). Directive §13.");
        return 2;
    }
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

// ---------- workbook ----------
var model = WorkbookReader.Read(workbookPath);
foreach (var sgConflict in model.PedestrianWidthConflicts)
    extraFindings.Add(new ValidationFinding("IG-PED-001", Severity.Error, sgConflict,
        $"PEDESTRIAN_WIDTH_CONFLICT: crossing '{sgConflict}' has disagreeing W rows in Pedestrian Xing.",
        RecommendedAction: "Fix the workbook — the engine will not pick one arbitrarily.",
        SourceReference: "Directive §6"));

string SgOf(string m) => model.SignalGroups.TryGetValue(m, out var sg) ? sg : m;

// §15 movement-mode resolution: sidecar → Pedestrian Xing data → validated layer convention → UNKNOWN.
var vehiclePattern = new Regex("^[NSEW]{1,2}-[LTRU]$", RegexOptions.CultureInvariant);
var suffixPattern = new Regex("^[NSEW]{1,2}-[LTRU]-(bus|sherut)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
(MovementMode Mode, bool Known) ResolveMode(string name)
{
    var fromSidecar = SidecarStr("movements", name, "mode");
    if (fromSidecar is not null && Enum.TryParse<MovementMode>(fromSidecar, true, out var m0))
        return (m0, true);
    if (model.PedestrianWidths.ContainsKey(name))
        return (MovementMode.Pedestrian, true);
    if (vehiclePattern.IsMatch(name))
        return (MovementMode.Vehicle, true);
    var sm = suffixPattern.Match(name);
    if (sm.Success)
        return (sm.Groups[1].Value.ToLowerInvariant() == "bus" ? MovementMode.Bus : MovementMode.Sherut, true);
    if (name.Length == 1 && char.IsAsciiLetterLower(name[0]))
        return (MovementMode.Pedestrian, true); // recognised drawing convention: single-letter crossing
    return (MovementMode.Vehicle, false);
}

double? WidthOf(string name)
{
    if (sidecar is JsonElement s2 && s2.TryGetProperty("pedestrianWidths", out var pw)
        && pw.ValueKind == JsonValueKind.Object && pw.TryGetProperty(name, out var wv)
        && wv.ValueKind == JsonValueKind.Number)
        return wv.GetDouble();
    return model.PedestrianWidths.TryGetValue(name, out var w) ? w : null;
}

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

    var (mode, known) = ResolveMode(name);
    if (!known)
    {
        extraFindings.Add(new ValidationFinding("IG-MOV-001", Severity.Error, name,
            $"UNKNOWN_MOVEMENT_MODE: layer '{layer}' does not match any explicit source " +
            "(sidecar, Pedestrian Xing, validated naming convention). The movement is excluded.",
            RecommendedAction: "Map the movement in Project Setup.",
            SourceReference: "Directive §15"));
        continue;
    }

    // ED-010: deterministic debris exclusion (reported, never silent)
    const double microDebrisMeters = 0.5;
    var debris = curves.Where(c => c.Curve.TotalLength < microDebrisMeters).ToList();
    if (debris.Count > 0)
    {
        curves = curves.Where(c => c.Curve.TotalLength >= microDebrisMeters).ToList();
        foreach (var d in debris)
            extraFindings.Add(new ValidationFinding("IG-GEO-011", Severity.Warning, name,
                $"DEGENERATE_GEOMETRY_EXCLUDED: {d.Handle} on '{layer}' (length {d.Curve.TotalLength:F3} m).",
                SourceReference: "Directive §11; ED-010"));
    }

    PolyCurve2D? stopLine = null;
    if (mode != MovementMode.Pedestrian)
    {
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

        // ED-010 second filter: boundaries are stop-line-referenced curves when that resolves to exactly 2
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
                    $"BOUNDARY_AUTO_SELECTED: '{layer}' — selected {string.Join(",", referenced.Select(r => r.Handle))} " +
                    $"(stop-line referenced), excluded {string.Join(",", excluded.Select(e => $"{e.Handle}(len {e.Curve.TotalLength:F2}m)"))}.",
                    RecommendedAction: "Review the excluded fragments; delete them or register boundaries explicitly.",
                    SourceReference: "v3 §36; ED-010"));
            }
        }
    }

    movements.Add(new PipelineMovement(name, mode, SgOf(name),
        new MovementGeometry(name, mode, curves.Select(c => c.Curve).ToList(),
            Array.Empty<PolyCurve2D>(), stopLine)
        {
            PedestrianWidthMeters = mode == MovementMode.Pedestrian ? WidthOf(name) : null,
            ConfirmedEndpointReferences = confirmedRefs,
        },
        curves.Select(c => c.Handle).ToList()));
}

// ---------- classification (no hard-coded Urban/50 — Directive §16) ----------
RoadEnvironment? roadType = SidecarStr("classification", "roadType") is string rt
    && Enum.TryParse<RoadEnvironment>(rt, true, out var rr) ? rr : null;
double? postedKph = null;
if (sidecar is JsonElement s3 && s3.TryGetProperty("classification", out var cls3)
    && cls3.ValueKind == JsonValueKind.Object && cls3.TryGetProperty("postedSpeedKph", out var pk)
    && pk.ValueKind == JsonValueKind.Number)
    postedKph = pk.GetDouble();

// ---------- pipeline ----------
var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), packId));
var input = new PipelineInput(
    intersectionName,
    Path.GetFileName(root.GetProperty("sourceFile").GetString() ?? geometryPath),
    AnalysisWriters.Sha256OfFile(geometryPath),
    pack,
    new ProjectClassification { RoadType = roadType, PostedSpeedKph = postedKph },
    model.Constants, model.Variant, model.MovementParameters, movements);

var output = AnalysisPipeline.Run(input);
var findings = output.Findings.Concat(extraFindings).ToList();

var (analysisPath, validationPath, manifestPath) = AnalysisWriters.WriteAll(
    outDir, intersectionName, output.Analysis, findings,
    runId: Guid.NewGuid().ToString("N"), started, DateTimeOffset.Now - started,
    application: "Mahod.Intergreen.Cli/ig-analyze");

// ---------- Excel export (Directive §22–§29) ----------
if (Array.IndexOf(args_, "--export-excel") >= 0)
{
    var excelOut = Path.Combine(outDir,
        Path.GetFileNameWithoutExtension(workbookPath) + "_MAHOD_INTERGREEN.xlsx");
    var export = WorkbookWriter.Export(workbookPath, excelOut, output.Analysis, model);
    Console.WriteLine($"  excel export: {export.OutputPath}");
    Console.WriteLine($"    rows populated={export.RowsPopulated} >4-point rows={export.MoreThanFourPointRows} " +
                      $"structural issues={export.StructuralIssues.Count}");
    foreach (var issue in export.StructuralIssues.Take(10))
        Console.WriteLine($"    STRUCTURAL: {issue}");
    if (export.StructuralIssues.Count > 0)
    {
        Console.Error.WriteLine("EXCEL EXPORT VERIFICATION FAILED (structural issues above)");
        return 3;
    }
}

// ---------- golden comparison: ALL historical points, strict evidence (Directive §17–§19) ----------
var report = new StringBuilder();
report.AppendLine($"# {intersectionName} — real-DWG Legacy regression (Directive §17–§20)");
report.AppendLine();
report.AppendLine($"| provenance | value |");
report.AppendLine($"|---|---|");
report.AppendLine($"| geometry export | `{Path.GetFileName(geometryPath)}` sha256 {AnalysisWriters.Sha256OfFile(geometryPath)[..16]}… |");
report.AppendLine($"| workbook | `{Path.GetFileName(workbookPath)}` sha256 {AnalysisWriters.Sha256OfFile(workbookPath)[..16]}… |");
report.AppendLine($"| rule pack | {pack.Manifest.Id} {pack.Manifest.Version} sha256 {pack.ContentSha256[..16]}… |");
report.AppendLine($"| sidecar | {(File.Exists(sidecarPath) ? Path.GetFileName(sidecarPath) : "none")} |");
report.AppendLine();

// §19 revision check: a manual CD physically impossible for the current clearing geometry
// (longer than every clearing boundary + margin) proves a revision mismatch.
var revisionImpossible = new List<string>();
foreach (var row in model.Rows)
{
    var mv = movements.FirstOrDefault(m => m.Id == row.Input.ClearingMovement);
    if (mv is null || mv.Mode == MovementMode.Pedestrian) continue;
    var maxLen = mv.Geometry.Boundaries.Count > 0 ? mv.Geometry.Boundaries.Max(b => b.TotalLength) : 0;
    foreach (var pt in row.Input.Points)
    {
        if (pt.ClearingDistanceMeters is double cd && cd > maxLen + 1.0)
            revisionImpossible.Add($"conflict {row.Input.ConflictNo} ({row.Input.ClearingMovement}→{row.Input.EnteringMovement}): manual CD {cd:F2} m exceeds longest current boundary {maxLen:F2} m");
    }
}
var revisionStatus = revisionImpossible.Count > 0 ? "REVISION_MISMATCH" : "BASELINE_REVISION_UNVERIFIED";
report.AppendLine($"## Revision provenance: **{revisionStatus}**");
foreach (var e in revisionImpossible.Take(10)) report.AppendLine($"- {e}");
report.AppendLine();

var deltasCd = new List<double>();
var deltasEd = new List<double>();
int ptVeryClose = 0, ptSmall = 0, ptNotReproduced = 0, ptNoCandidates = 0, ptInvalidBaseline = 0, totalPoints = 0;
var unsafeCases = new List<string>();
var lines = new List<string>();

foreach (var row in model.Rows)
{
    var id = $"{row.Input.ClearingMovement}→{row.Input.EnteringMovement}";
    var engine = output.Analysis.Conflicts.FirstOrDefault(c => c.Id == id);
    var manualIg = row.CachedFinalIg is double f ? (int?)(int)f : null;

    for (var pi = 0; pi < 4; pi++)
    {
        var mp = row.Input.Points[pi];
        if (mp.ClearingDistanceMeters is null && mp.EnteringDistanceMeters is null) continue;
        totalPoints++;

        if (mp.ClearingDistanceMeters is null)
        {
            ptInvalidBaseline++;
            lines.Add($"| {row.Input.ConflictNo} | {id} | P{pi + 1} | INVALID_MANUAL_BASELINE | — | — | CD missing in workbook |");
            continue;
        }
        if (engine is null || engine.Points.Count == 0)
        {
            ptNoCandidates++;
            lines.Add($"| {row.Input.ConflictNo} | {id} | P{pi + 1} | NO_ENGINE_CANDIDATES | — | — | engine status {(engine?.Status ?? "absent")} |");
            continue;
        }

        var mCd = mp.ClearingDistanceMeters.Value;
        var mEd = mp.EnteringDistanceMeters ?? 0.0;
        var best = engine.Points.OrderBy(p => Math.Abs(p.Cd - mCd) + Math.Abs(p.Ed - mEd)).First();
        var dCd = best.Cd - mCd;
        var dEd = best.Ed - mEd;
        deltasCd.Add(dCd);
        deltasEd.Add(dEd);
        var worst = Math.Max(Math.Abs(dCd), Math.Abs(dEd));
        string cls;
        if (worst <= 0.01) { cls = "VERY_CLOSE"; ptVeryClose++; }
        else if (worst <= 0.05) { cls = "SMALL_DELTA_REVIEW"; ptSmall++; }
        else { cls = "POINT_NOT_REPRODUCED"; ptNotReproduced++; }

        var governs = engine.DefiningPointId == best.Id ? "governs" : "not-governing";
        lines.Add($"| {row.Input.ConflictNo} | {id} | P{pi + 1} | {cls} | ΔCD={dCd:+0.000;-0.000} | ΔED={dEd:+0.000;-0.000} | nearest={best.Id}({best.ClearingCurveId}×{best.EnteringCurveId},{governs}); manual IG={manualIg}, engine IG={engine.FinalIg?.ToString() ?? "—"} |");

        // §20 safety condition (same-revision only)
        if (revisionStatus != "REVISION_MISMATCH" && worst <= 0.05
            && engine.FinalIg is int eig && manualIg is int mig && eig < mig)
            unsafeCases.Add($"{id} P{pi + 1}: engine {eig} < manual {mig}");
    }
}

static string Dist(List<double> xs)
{
    if (xs.Count == 0) return "n/a";
    var abs = xs.Select(Math.Abs).OrderBy(x => x).ToList();
    var mean = abs.Average();
    var median = abs[abs.Count / 2];
    var std = Math.Sqrt(abs.Select(x => (x - mean) * (x - mean)).Average());
    string B(double t) => abs.Count(x => x <= t).ToString();
    return $"min {abs.First():F3}, max {abs.Last():F3}, mean {mean:F3}, median {median:F3}, std {std:F3} · " +
           $"≤0.01: {B(0.01)}, ≤0.02: {B(0.02)}, ≤0.05: {B(0.05)}, ≤0.10: {B(0.10)}, ≤0.20: {B(0.20)}, ≤0.50: {B(0.50)}, ≤1.00: {B(1.00)} of {abs.Count}";
}

report.AppendLine($"""
    ## Summary

    | metric | value |
    |---|---|
    | historical rows | {model.Rows.Count} |
    | historical measured points (P1–P4) | {totalPoints} |
    | VERY_CLOSE (≤0.01 m) | {ptVeryClose} |
    | SMALL_DELTA_REVIEW (≤0.05 m) | {ptSmall} |
    | POINT_NOT_REPRODUCED (>0.05 m) | {ptNotReproduced} |
    | NO_ENGINE_CANDIDATES | {ptNoCandidates} |
    | INVALID_MANUAL_BASELINE | {ptInvalidBaseline} |
    | unsafe same-revision cases (engine < manual, matched) | {unsafeCases.Count} |
    | engine conflicts | {output.Analysis.Conflicts.Count} |
    | engine candidate points | {output.Analysis.Conflicts.Sum(c => c.Points.Count)} |
    | validation errors | {findings.Count(f => f.Severity == Severity.Error)} |
    | validation warnings | {findings.Count(f => f.Severity == Severity.Warning)} |
    | review-required findings | {findings.Count(f => f.Severity == Severity.ReviewRequired)} |
    | IG-GEO-005 findings | {findings.Count(f => f.Code == "IG-GEO-005")} |
    | matrix VALID / REVIEW / BLOCKED | {output.Analysis.Matrix.Count(m => m.Status == "VALID")} / {output.Analysis.Matrix.Count(m => m.Status == "REVIEWREQUIRED")} / {output.Analysis.Matrix.Count(m => m.Status == "BLOCKED")} |

    ## Delta distributions (|Δ| of nearest candidates)

    - CD: {Dist(deltasCd)}
    - ED: {Dist(deltasEd)}

    ## Unsafe cases

    {(unsafeCases.Count == 0 ? "NONE" : string.Join("\n", unsafeCases.Select(u => "- " + u)))}

    ## Points

    | conflict | pair | point | classification | ΔCD [m] | ΔED [m] | detail |
    |---|---|---|---|---|---|---|
    """);
foreach (var l in lines) report.AppendLine(l);

var reportPath = Path.Combine(outDir, intersectionName + "_REPORT.md");
File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));

Console.WriteLine($"ig-analyze OK → {outDir}");
Console.WriteLine($"  movements={movements.Count} conflicts={output.Analysis.Conflicts.Count} " +
                  $"points={output.Analysis.Conflicts.Sum(c => c.Points.Count)} " +
                  $"errors={findings.Count(f => f.Severity == Severity.Error)} warnings={findings.Count(f => f.Severity == Severity.Warning)} " +
                  $"IG-GEO-005={findings.Count(f => f.Code == "IG-GEO-005")}");
Console.WriteLine($"  points: VERY_CLOSE={ptVeryClose} SMALL={ptSmall} NOT_REPRODUCED={ptNotReproduced} NO_CAND={ptNoCandidates} INVALID={ptInvalidBaseline} of {totalPoints}");
Console.WriteLine($"  revision={revisionStatus} unsafe={unsafeCases.Count}");
Console.WriteLine($"  report={reportPath}");
return unsafeCases.Count > 0 ? 1 : 0;
