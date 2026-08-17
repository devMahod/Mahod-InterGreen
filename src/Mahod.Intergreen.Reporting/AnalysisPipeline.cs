using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Rules;

namespace Mahod.Intergreen.Reporting;

/// <summary>One movement entering the pipeline: identity + geometry + CAD provenance.</summary>
public sealed record PipelineMovement(
    string Id,
    MovementMode Mode,
    string SignalGroup,
    MovementGeometry Geometry,
    IReadOnlyList<string> SourceHandles);

public sealed record PipelineInput(
    string IntersectionName,
    string SourceFileName,
    string SourceSha256,
    RulePack RulePack,
    ProjectClassification Classification,
    LegacyConstants Constants,
    LegacyTemplateVariant Variant,
    IReadOnlyDictionary<string, LegacyMovementParameters> MovementParameters,
    IReadOnlyList<PipelineMovement> Movements);

public sealed record PipelineOutput(
    AnalysisDocument Analysis,
    IReadOnlyList<ValidationFinding> Findings);

/// <summary>
/// Deterministic orchestration: movements → conflict strategy → production analysis →
/// signal-group matrix → analysis document. Ordering is explicit everywhere; the same
/// input yields a byte-identical analysis.json (Directive §6).
/// </summary>
public static class AnalysisPipeline
{
    public const string SchemaVersion = "1.0";
    public const string EngineVersion = "0.1.0";

    public static PipelineOutput Run(PipelineInput input)
    {
        var findings = new List<ValidationFinding>();
        var strategy = CreateStrategy(input.RulePack.Manifest.GeometryStrategyId);

        // §36: calculation selection follows the Rule Pack's CalculationStrategyId — a 2025
        // pack must never route into the Legacy calculators (and vice versa).
        var is2025 = input.RulePack.Manifest.CalculationStrategyId == "israel-2025-06";
        var analyzer = new LegacyProductionAnalyzer(
            input.Constants, input.Variant, input.MovementParameters,
            input.RulePack.Manifest.RoundingStrategyId == "guidelines-ceil"
                ? FinalIgPolicy.Guidelines2025
                : FinalIgPolicy.MahodLegacy);
        var analyzer2025 = is2025 ? new Mahod.Intergreen.Core.Israel2025.Israel2025ProductionAnalyzer(
            input.RulePack.ReactionTimeSec, input.RulePack.DecelerationMps2) : null;
        var resolver2025 = is2025 ? new ParameterResolver(input.RulePack) : null;
        var params2025 = is2025
            ? input.Movements.ToDictionary(m => m.Id,
                m => Resolve2025Parameters(m, resolver2025!, input.RulePack, input.Classification, findings),
                StringComparer.Ordinal)
            : null;

        var movements = input.Movements.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        var conflicts = new List<ConflictRecord>();
        var contributions = new List<ConflictContribution>();

        foreach (var clearing in movements)
        {
            foreach (var entering in movements)
            {
                if (clearing.Id == entering.Id) continue;
                if (clearing.Mode == MovementMode.Pedestrian && entering.Mode == MovementMode.Pedestrian)
                    continue; // no pedestrian-vs-pedestrian intergreen

                var conflictId = $"{clearing.Id}→{entering.Id}";
                var strat = strategy.FindConflictPoints(clearing.Geometry, entering.Geometry);
                findings.AddRange(strat.Findings);

                if (strat.HasBlockingError)
                {
                    conflicts.Add(new ConflictRecord(conflictId, clearing.Id, entering.Id, "ERROR",
                        Array.Empty<ConflictPointRecord>(), null, null, null,
                        Trace(input), strat.Findings.Select(f => f.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList()));
                    contributions.Add(Contribution(conflictId, clearing, entering,
                        MatrixContributionStatus.Error, null, strat.Findings));
                    continue;
                }

                if (strat.Points.Count == 0)
                {
                    // no geometric conflict — NOT_APPLICABLE unless flagged for review
                    var review = strat.Findings.Any(f => f.Severity == Severity.ReviewRequired);
                    if (review)
                    {
                        conflicts.Add(new ConflictRecord(conflictId, clearing.Id, entering.Id, "REVIEW_REQUIRED",
                            Array.Empty<ConflictPointRecord>(), null, null, null,
                            Trace(input), strat.Findings.Select(f => f.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList()));
                        contributions.Add(Contribution(conflictId, clearing, entering,
                            MatrixContributionStatus.ReviewRequired, null, strat.Findings));
                    }
                    continue;
                }

                var orderedPoints = strat.Points
                    .OrderBy(p => p.ClearingDistanceMeters).ThenBy(p => p.EnteringDistanceMeters)
                    .ToList();
                var pointTuples = orderedPoints
                    .Select(p => (p.ClearingDistanceMeters, p.EnteringDistanceMeters)).ToList();
                var result = is2025
                    ? analyzer2025!.Analyze(conflictId, clearing.Mode, entering.Mode,
                        params2025![clearing.Id] ?? new Mahod.Intergreen.Core.Israel2025.Israel2025MovementParameters(null, null, null, null, null, null),
                        params2025[entering.Id] ?? new Mahod.Intergreen.Core.Israel2025.Israel2025MovementParameters(null, null, null, null, null, null),
                        pointTuples)
                    : analyzer.AnalyzeGeometryConflict(
                        conflictId, clearing.Id, entering.Id, clearing.Mode, entering.Mode, pointTuples);
                findings.AddRange(result.Findings);

                var pointRecords = orderedPoints.Select((p, i) => new ConflictPointRecord(
                    $"P{i + 1}", p.ClearingDistanceMeters, p.EnteringDistanceMeters,
                    Math.Round(p.Location.X, 6), Math.Round(p.Location.Y, 6),
                    p.ClearingCurveId, p.EnteringCurveId,
                    i < result.Points.Count ? result.Points[i].IntergreenSec : null)).ToList();

                var status = result.Status switch
                {
                    RowStatus.Error => "ERROR",
                    RowStatus.ReviewRequired => "REVIEW_REQUIRED",
                    _ => "VALID",
                };
                conflicts.Add(new ConflictRecord(conflictId, clearing.Id, entering.Id, status,
                    pointRecords,
                    result.DefiningPointIndex is int d ? $"P{d + 1}" : null,
                    result.RawMaxIntergreenSec, result.FinalIg,
                    Trace(input),
                    result.Findings.Concat(strat.Findings).Select(f => f.Code).Distinct()
                        .OrderBy(c => c, StringComparer.Ordinal).ToList()));

                contributions.Add(Contribution(conflictId, clearing, entering,
                    result.Status switch
                    {
                        RowStatus.Error => MatrixContributionStatus.Error,
                        RowStatus.ReviewRequired => MatrixContributionStatus.ReviewRequired,
                        _ => MatrixContributionStatus.Valid,
                    },
                    result.FinalIg, result.Findings));
            }
        }

        var matrix = new SignalGroupMatrixService().Build(contributions);
        findings.AddRange(matrix.Findings);
        findings.AddRange(matrix.Cells.SelectMany(c => c.Findings)
            .Where(f => f.Code == SignalGroupMatrixService.CodeCellBlocked));

        var doc = new AnalysisDocument
        {
            SchemaVersion = SchemaVersion,
            EngineVersion = EngineVersion,
            IntersectionName = input.IntersectionName,
            SourceGeometry = new SourceGeometryRef(input.SourceFileName, input.SourceSha256),
            RulePack = new RulePackSnapshot(
                input.RulePack.Manifest.Id, input.RulePack.Manifest.Version, input.RulePack.ContentSha256,
                input.RulePack.Manifest.GeometryStrategyId, input.RulePack.Manifest.CalculationStrategyId,
                input.RulePack.Manifest.RoundingStrategyId, input.RulePack.Manifest.MinimumIntergreenSec),
            Classification = new ClassificationSnapshot(
                input.Classification.RoadType?.ToString(), input.Classification.PostedSpeedKph,
                input.Classification.InbarMode,
                input.Classification.Crossings.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value.ToString())).ToList()),
            Movements = movements.Select(m => new MovementRecord(
                m.Id, m.Mode.ToString(), m.SignalGroup,
                ResolveReferenceStations(m),
                m.SourceHandles.OrderBy(h => h, StringComparer.Ordinal).ToList())).ToList(),
            Conflicts = conflicts.OrderBy(c => c.Id, StringComparer.Ordinal).ToList(),
            Matrix = matrix.Cells
                .Select(c => new MatrixCellRecord(c.ClearingSignalGroup, c.EnteringSignalGroup,
                    c.Value, c.Status.ToString().ToUpperInvariant(), c.GoverningConflictId))
                .OrderBy(c => c.ClearingSignalGroup, StringComparer.Ordinal)
                .ThenBy(c => c.EnteringSignalGroup, StringComparer.Ordinal).ToList(),
        };

        return new PipelineOutput(doc, findings);
    }

    private static ConflictContribution Contribution(string id, PipelineMovement clearing,
        PipelineMovement entering, MatrixContributionStatus status, int? finalIg,
        IReadOnlyList<ValidationFinding> findings)
        => new(id, clearing.Id, entering.Id, clearing.SignalGroup, entering.SignalGroup,
            status, finalIg, findings);

    /// <summary>
    /// Resolves 2025 movement parameters from the Rule Pack + explicit project classification
    /// (Directive §16/§36). Missing classification yields null → the affected conflicts block
    /// with MISSING_ENGINEERING_CLASSIFICATION; nothing is guessed.
    /// </summary>
    private static Mahod.Intergreen.Core.Israel2025.Israel2025MovementParameters? Resolve2025Parameters(
        PipelineMovement m, ParameterResolver resolver, RulePack pack,
        ProjectClassification cls, List<ValidationFinding> findings)
    {
        switch (Mahod.Intergreen.Core.Israel2025.ModePairPolicy.EngineeringClass(m.Mode))
        {
            case MovementMode.Pedestrian:
            {
                var (speed, f) = resolver.ResolvePedestrianSpeed(m.Id, cls);
                findings.AddRange(f);
                if (speed is null || m.Geometry.PedestrianWidthMeters is not double w) return null;
                return new(null, null, null, null, speed.Value, w);
            }
            case MovementMode.Bicycle:
            {
                var b = pack.Parameters.RootElement.GetProperty("bicycle");
                return new(
                    b.GetProperty("fastClearingKph").GetDouble(),
                    b.GetProperty("slowClearingKph").GetDouble(),
                    b.GetProperty("enteringKph").GetDouble(),
                    b.GetProperty("lengthM").GetDouble(),
                    null, null);
            }
            case MovementMode.Lrt:
            {
                if (cls.PostedSpeedKph is not double posted) return null;
                var lrt = pack.Parameters.RootElement.GetProperty("lrt");
                var slow = Math.Min(Math.Max(posted / 2.0, 25.0), posted);
                return new(posted, slow, posted, lrt.GetProperty("lengthM").GetDouble(), null, null);
            }
            default:
            {
                var maneuver = m.Id.Split('-').ElementAtOrDefault(1) == "T" ? Maneuver.Through : Maneuver.Turn;
                var (p, f) = resolver.ResolveVehicle(maneuver, VehicleLengthClass.Standard12, cls);
                findings.AddRange(f);
                if (p is null) return null;
                return new(p.FastClearingKph.Value, p.SlowClearingKph.Value, p.EnteringKph.Value,
                    p.VehicleLengthMeters.Value, null, null);
            }
        }
    }

    private static IConflictPointStrategy CreateStrategy(string id) => id switch
    {
        "legacy-envelope" => new LegacyEnvelopeConflictStrategy(),
        "lane-centreline" => new LaneCentrelineConflictStrategy(),
        _ => throw new NotSupportedException($"unknown geometry strategy '{id}'"),
    };

    private static string Trace(PipelineInput input)
        => $"{input.RulePack.Manifest.CalculationStrategyId}/{input.RulePack.Manifest.GeometryStrategyId}/{input.RulePack.Manifest.RoundingStrategyId}";

    private static IReadOnlyList<ReferenceStationRecord> ResolveReferenceStations(PipelineMovement m)
    {
        var list = new List<ReferenceStationRecord>();
        if (m.Geometry.StopLine is null) return list;
        var curves = m.Geometry.Boundaries.Select((c, i) => (Curve: c, Id: $"{m.Id}.b{i + 1}"))
            .Concat(m.Geometry.LaneCentrelines.Select((c, i) => (Curve: c, Id: $"{m.Id}.lane{i + 1}")));
        foreach (var (curve, id) in curves)
        {
            var r = ReferenceStation.Resolve(curve, m.Geometry.StopLine, 0.5);
            if (r is not null)
                list.Add(new ReferenceStationRecord(id, Math.Round(r.Station, 6), r.Method.ToString()));
        }
        return list.OrderBy(r => r.CurveId, StringComparer.Ordinal).ToList();
    }
}
