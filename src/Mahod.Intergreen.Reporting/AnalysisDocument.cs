using System.Collections.Generic;

namespace Mahod.Intergreen.Reporting;

/// <summary>
/// Deterministic engineering payload (Directive §6 / v3 §41–42).
/// Contains NO absolute paths, machine names, wall-clock times or per-run GUIDs —
/// environment data lives in run_manifest.json only.
/// </summary>
public sealed record AnalysisDocument
{
    public required string SchemaVersion { get; init; }
    public required string EngineVersion { get; init; }
    public required string IntersectionName { get; init; }
    public required SourceGeometryRef SourceGeometry { get; init; }
    public required RulePackSnapshot RulePack { get; init; }
    public required ClassificationSnapshot Classification { get; init; }
    public required IReadOnlyList<MovementRecord> Movements { get; init; }
    public required IReadOnlyList<ConflictRecord> Conflicts { get; init; }
    public required IReadOnlyList<MatrixCellRecord> Matrix { get; init; }
}

public sealed record SourceGeometryRef(string FileName, string Sha256);

public sealed record RulePackSnapshot(
    string Id, string Version, string ContentSha256,
    string GeometryStrategyId, string CalculationStrategyId, string RoundingStrategyId,
    int? MinimumIntergreenSec);

public sealed record ClassificationSnapshot(
    string? RoadType, double? PostedSpeedKph, bool InbarMode,
    IReadOnlyList<KeyValuePair<string, string>> Crossings);

public sealed record MovementRecord(
    string Id, string Mode, string SignalGroup,
    IReadOnlyList<ReferenceStationRecord> ReferenceStations,
    IReadOnlyList<string> SourceHandles);

public sealed record ReferenceStationRecord(string CurveId, double Station, string Method);

public sealed record ConflictRecord(
    string Id, string Clearing, string Entering, string Status,
    IReadOnlyList<ConflictPointRecord> Points,
    string? DefiningPointId, double? RawIntergreenSec, int? FinalIg,
    string CalculationTrace,
    IReadOnlyList<string> FindingCodes);

public sealed record ConflictPointRecord(
    string Id, double Cd, double Ed, double X, double Y,
    string ClearingCurveId, string EnteringCurveId, double? RawIg);

public sealed record MatrixCellRecord(
    string ClearingSignalGroup, string EnteringSignalGroup,
    int? Value, string Status, string? GoverningConflictId);
