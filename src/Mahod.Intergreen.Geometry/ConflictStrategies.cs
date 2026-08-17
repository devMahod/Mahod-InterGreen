using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Geometry;

/// <summary>Movement geometry handed to a conflict-point strategy.</summary>
public sealed record MovementGeometry(
    string MovementId,
    MovementMode Mode,
    IReadOnlyList<PolyCurve2D> Boundaries,
    IReadOnlyList<PolyCurve2D> LaneCentrelines,
    PolyCurve2D? StopLine);

/// <summary>One candidate conflict point with exact stations from each movement's stop line.</summary>
public sealed record ConflictPoint(
    Point2D Location,
    double ClearingDistanceMeters,
    double EnteringDistanceMeters,
    string ClearingCurveId,
    string EnteringCurveId);

public sealed record ConflictPointResult(
    IReadOnlyList<ConflictPoint> Points,
    IReadOnlyList<ValidationFinding> Findings)
{
    public bool HasBlockingError => Findings.Any(f => f.Severity == Severity.Error);
}

/// <summary>
/// Strategy interface (v3 §16): exactly two implementations exist —
/// legacy envelopes and 2025 lane centrelines. A rule pack references one by id.
/// </summary>
public interface IConflictPointStrategy
{
    string Id { get; }
    ConflictPointResult FindConflictPoints(MovementGeometry clearing, MovementGeometry entering);
}

/// <summary>
/// Legacy Mahod geometry: each movement = 2 envelope boundary polylines; candidate points are
/// ALL boundary×boundary intersections (never truncated, v3 §15); distances measured from the
/// geometric stop-line reference station (v3 §14) so polyline direction cannot matter.
/// </summary>
public sealed class LegacyEnvelopeConflictStrategy : IConflictPointStrategy
{
    public const string CodeWrongBoundaryCount = "IG-GEO-001";
    public const string CodeNoStopLine = "IG-GEO-002";
    public const string CodeReferenceUnresolved = "IG-GEO-003";
    public const string CodeReferenceEndpointFallback = "IG-GEO-004";
    public const string CodePossibleUnresolvedConflict = "IG-GEO-005";

    private readonly double _endpointToleranceMeters;

    public LegacyEnvelopeConflictStrategy(double endpointToleranceMeters = 0.5)
        => _endpointToleranceMeters = endpointToleranceMeters;

    public string Id => "legacy-envelope";

    public ConflictPointResult FindConflictPoints(MovementGeometry clearing, MovementGeometry entering)
    {
        var findings = new List<ValidationFinding>();

        foreach (var (m, role) in new[] { (clearing, "clearing"), (entering, "entering") })
        {
            if (m.Boundaries.Count != 2)
                findings.Add(new ValidationFinding(CodeWrongBoundaryCount, Severity.Error, m.MovementId,
                    $"Movement '{m.MovementId}' ({role}) has {m.Boundaries.Count} envelope boundaries (expected 2)."));
            if (m.StopLine is null)
                findings.Add(new ValidationFinding(CodeNoStopLine, Severity.Error, m.MovementId,
                    $"Movement '{m.MovementId}' ({role}) has no stop/reference line."));
        }
        if (findings.Any(f => f.Severity == Severity.Error))
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), findings);

        // reference stations per boundary
        var refA = ResolveReferences(clearing, findings);
        var refB = ResolveReferences(entering, findings);
        if (refA is null || refB is null)
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), findings);

        var points = new List<ConflictPoint>();
        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                var ba = clearing.Boundaries[i];
                var bb = entering.Boundaries[j];
                foreach (var hit in ba.IntersectionsWith(bb))
                {
                    points.Add(new ConflictPoint(
                        hit.Point,
                        Math.Abs(hit.StationA - refA[i].Station),
                        Math.Abs(hit.StationB - refB[j].Station),
                        $"{clearing.MovementId}.b{i + 1}",
                        $"{entering.MovementId}.b{j + 1}"));
                }
            }
        }

        // numerical dedup across boundary pairs
        var dedup = new List<ConflictPoint>();
        foreach (var p in points.OrderBy(p => p.ClearingDistanceMeters))
        {
            if (!dedup.Any(e => e.Location.DistanceTo(p.Location) < Tolerances.PointDeduplication))
                dedup.Add(p);
        }

        if (dedup.Count == 0 && EnvelopesOverlap(clearing, entering))
        {
            findings.Add(new ValidationFinding(CodePossibleUnresolvedConflict, Severity.ReviewRequired,
                $"{clearing.MovementId} × {entering.MovementId}",
                "Envelopes overlap but no boundary intersection was found (POSSIBLE_UNRESOLVED_CONFLICT).",
                "A silently missing conflict is worse than a wrong number — review the drawing.",
                SourceReference: "v3 §15"));
        }

        return new ConflictPointResult(dedup, findings);
    }

    private ReferenceStation.Result[]? ResolveReferences(MovementGeometry m, List<ValidationFinding> findings)
    {
        var results = new ReferenceStation.Result[2];
        for (var i = 0; i < 2; i++)
        {
            var r = ReferenceStation.Resolve(m.Boundaries[i], m.StopLine!, _endpointToleranceMeters);
            if (r is null)
            {
                findings.Add(new ValidationFinding(CodeReferenceUnresolved, Severity.Error, m.MovementId,
                    $"Boundary {i + 1} of '{m.MovementId}' cannot be referenced to its stop line " +
                    "(no intersection and no endpoint within tolerance).",
                    SourceReference: "v3 §14"));
                return null;
            }
            if (r.Method == ReferenceStation.Method.EndpointFallback)
            {
                findings.Add(new ValidationFinding(CodeReferenceEndpointFallback, Severity.Warning, m.MovementId,
                    $"Boundary {i + 1} of '{m.MovementId}' does not intersect its stop line; " +
                    $"using the endpoint at station {r.Station:F3} (within {_endpointToleranceMeters} m).",
                    SourceReference: "v3 §14"));
            }
            results[i] = r;
        }
        return results;
    }

    private static bool EnvelopesOverlap(MovementGeometry a, MovementGeometry b)
    {
        var (aMin, aMax) = BoundingBox(a.Boundaries);
        var (bMin, bMax) = BoundingBox(b.Boundaries);
        return aMin.X <= bMax.X && bMin.X <= aMax.X && aMin.Y <= bMax.Y && bMin.Y <= aMax.Y;
    }

    private static (Point2D Min, Point2D Max) BoundingBox(IEnumerable<PolyCurve2D> curves)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var c in curves)
        {
            foreach (var s in c.Segments)
            {
                foreach (var p in ExtremePoints(s))
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            }
        }
        return (new Point2D(minX, minY), new Point2D(maxX, maxY));
    }

    private static IEnumerable<Point2D> ExtremePoints(ISegment2D s)
    {
        yield return s.Start;
        yield return s.End;
        if (s is CircularArcSegment2D a)
        {
            // axis-aligned extremes that fall inside the sweep
            for (var k = 0; k < 4; k++)
            {
                var angle = k * Math.PI / 2.0;
                var delta = CircularArcSegment2D.NormalizeSigned(angle - a.StartAngleRad);
                var along = a.SweepRad >= 0
                    ? CircularArcSegment2D.NormalizePositive(delta)
                    : CircularArcSegment2D.NormalizePositive(-delta);
                if (along <= Math.Abs(a.SweepRad))
                    yield return new Point2D(
                        a.Center.X + a.Radius * Math.Cos(angle),
                        a.Center.Y + a.Radius * Math.Sin(angle));
            }
        }
    }
}

/// <summary>
/// June 2025 geometry (v3 §16): conflict points are intersections of travel-path lane
/// centrelines — one path per relevant lane. NEVER computed from envelope boundaries and
/// NEVER synthesized from them. Missing centreline geometry blocks with 2025_GEOMETRY_MISSING.
/// </summary>
public sealed class LaneCentrelineConflictStrategy : IConflictPointStrategy
{
    public const string CodeGeometryMissing = "IG-GEO-020"; // 2025_GEOMETRY_MISSING
    public const string CodeNoStopLine = "IG-GEO-021";
    public const string CodeReferenceUnresolved = "IG-GEO-022";

    private readonly double _endpointToleranceMeters;

    public LaneCentrelineConflictStrategy(double endpointToleranceMeters = 0.5)
        => _endpointToleranceMeters = endpointToleranceMeters;

    public string Id => "lane-centreline";

    public ConflictPointResult FindConflictPoints(MovementGeometry clearing, MovementGeometry entering)
    {
        var findings = new List<ValidationFinding>();

        foreach (var (m, role) in new[] { (clearing, "clearing"), (entering, "entering") })
        {
            if (m.LaneCentrelines.Count == 0)
                findings.Add(new ValidationFinding(CodeGeometryMissing, Severity.Error, m.MovementId,
                    $"2025_GEOMETRY_MISSING: movement '{m.MovementId}' ({role}) has no registered lane centrelines. " +
                    "The 2025 method requires a travel-path centreline per lane (§5.4); envelope boundaries must not be used.",
                    RecommendedAction: "Register centrelines with IG_REGISTER_CENTERLINES or draw them.",
                    SourceReference: "SR-5.4.2; v3 §16"));
            if (m.StopLine is null)
                findings.Add(new ValidationFinding(CodeNoStopLine, Severity.Error, m.MovementId,
                    $"Movement '{m.MovementId}' ({role}) has no stop/reference line."));
        }
        if (findings.Any(f => f.Severity == Severity.Error))
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), findings);

        var points = new List<ConflictPoint>();
        for (var i = 0; i < clearing.LaneCentrelines.Count; i++)
        {
            var ca = clearing.LaneCentrelines[i];
            var ra = ReferenceStation.Resolve(ca, clearing.StopLine!, _endpointToleranceMeters);
            if (ra is null)
            {
                findings.Add(new ValidationFinding(CodeReferenceUnresolved, Severity.Error, clearing.MovementId,
                    $"Lane centreline {i + 1} of '{clearing.MovementId}' cannot be referenced to its stop line.",
                    SourceReference: "v3 §14"));
                continue;
            }
            for (var j = 0; j < entering.LaneCentrelines.Count; j++)
            {
                var cb = entering.LaneCentrelines[j];
                var rb = ReferenceStation.Resolve(cb, entering.StopLine!, _endpointToleranceMeters);
                if (rb is null)
                {
                    findings.Add(new ValidationFinding(CodeReferenceUnresolved, Severity.Error, entering.MovementId,
                        $"Lane centreline {j + 1} of '{entering.MovementId}' cannot be referenced to its stop line.",
                        SourceReference: "v3 §14"));
                    continue;
                }
                foreach (var hit in ca.IntersectionsWith(cb))
                {
                    points.Add(new ConflictPoint(
                        hit.Point,
                        Math.Abs(hit.StationA - ra.Station),
                        Math.Abs(hit.StationB - rb.Station),
                        $"{clearing.MovementId}.lane{i + 1}",
                        $"{entering.MovementId}.lane{j + 1}"));
                }
            }
        }

        var dedup = new List<ConflictPoint>();
        foreach (var p in points.OrderBy(p => p.ClearingDistanceMeters))
        {
            if (!dedup.Any(e => e.Location.DistanceTo(p.Location) < Tolerances.PointDeduplication))
                dedup.Add(p);
        }
        return new ConflictPointResult(dedup, findings);
    }
}
