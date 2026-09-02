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
    PolyCurve2D? StopLine)
{
    /// <summary>
    /// Pedestrian crossing width W [m] — the authoritative Legacy project input
    /// (Pedestrian Xing sheet / project sidecar). NEVER derived from boundary averaging
    /// (Directive §5–§6). Null for vehicles and for unconfigured crossings.
    /// </summary>
    public double? PedestrianWidthMeters { get; init; }

    /// <summary>
    /// Stop/reference mappings explicitly confirmed by the user (project sidecar, §14):
    /// curve ids (e.g. "E-L.b1") whose endpoint reference has been confirmed.
    /// </summary>
    public IReadOnlySet<string> ConfirmedEndpointReferences { get; init; } =
        new HashSet<string>();
}

/// <summary>One candidate conflict point with exact stations from each movement's stop line.</summary>
public sealed record ConflictPoint(
    Point2D Location,
    double ClearingDistanceMeters,
    double EnteringDistanceMeters,
    string ClearingCurveId,
    string EnteringCurveId,
    string Origin = "boundary-intersection");

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
/// Legacy Mahod geometry (hardened per Directive §§3, 5–7, 14, 21A):
///  - candidate points: ALL boundary×boundary intersections PLUS boundary-termination
///    candidates (a boundary endpoint lying inside/on the opposing envelope region);
///  - envelope overlap is proven by exact region evaluation — AABB is only a pre-filter;
///  - pedestrian crossings carry N edges and an authoritative project width W;
///  - endpoint stop-line fallbacks require explicit user confirmation (no hidden 0.5 m guess).
/// </summary>
public sealed class LegacyEnvelopeConflictStrategy : IConflictPointStrategy
{
    public const string CodeWrongBoundaryCount = "IG-GEO-001";
    public const string CodeNoStopLine = "IG-GEO-002";
    public const string CodeReferenceUnresolved = "IG-GEO-003";
    public const string CodeReferenceEndpointUnconfirmed = "IG-GEO-004";
    public const string CodePossibleUnresolvedConflict = "IG-GEO-005";
    public const string CodeEnvelopeRegionInvalid = "IG-GEO-006";
    public const string CodePedestrianWidthMissing = "IG-GEO-007";
    public const string CodePedestrianEdgeCoverageGap = "IG-GEO-008";

    /// <summary>Gap [m] within which a non-intersecting crossing edge is flagged as a coverage gap (§7).</summary>
    public const double PedestrianCoverageGapMeters = 1.0;

    private readonly double _endpointSuggestionToleranceMeters;

    public LegacyEnvelopeConflictStrategy(double endpointSuggestionToleranceMeters = 0.5)
        => _endpointSuggestionToleranceMeters = endpointSuggestionToleranceMeters;

    public string Id => "legacy-envelope";

    public ConflictPointResult FindConflictPoints(MovementGeometry clearing, MovementGeometry entering)
    {
        if (clearing.Mode == MovementMode.Pedestrian && entering.Mode == MovementMode.Pedestrian)
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), Array.Empty<ValidationFinding>());

        if (clearing.Mode == MovementMode.Pedestrian || entering.Mode == MovementMode.Pedestrian)
            return VehiclePedestrian(clearing, entering);

        return VehicleVehicle(clearing, entering);
    }

    // ---------------- vehicle × vehicle ----------------

    private ConflictPointResult VehicleVehicle(MovementGeometry clearing, MovementGeometry entering)
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

        var refA = ResolveReferences(clearing, findings);
        var refB = ResolveReferences(entering, findings);
        if (refA is null || refB is null)
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), findings);

        var points = new List<ConflictPoint>();

        // 1. all boundary×boundary intersections
        for (var i = 0; i < 2; i++)
        for (var j = 0; j < 2; j++)
        {
            foreach (var hit in clearing.Boundaries[i].IntersectionsWith(entering.Boundaries[j]))
            {
                points.Add(new ConflictPoint(hit.Point,
                    Math.Abs(hit.StationA - refA[i]),
                    Math.Abs(hit.StationB - refB[j]),
                    $"{clearing.MovementId}.b{i + 1}",
                    $"{entering.MovementId}.b{j + 1}"));
            }
        }

        // 2. exact envelope regions (invalid strip → REVIEW, never a guess)
        var regionA = EnvelopeRegion.Build(clearing.Boundaries[0], clearing.Boundaries[1]);
        var regionB = EnvelopeRegion.Build(entering.Boundaries[0], entering.Boundaries[1]);
        foreach (var (r, m) in new[] { (regionA, clearing), (regionB, entering) })
        {
            if (!r.IsValid)
                findings.Add(new ValidationFinding(CodeEnvelopeRegionInvalid, Severity.ReviewRequired,
                    m.MovementId, $"{r.InvalidReason} — movement '{m.MovementId}'.",
                    SourceReference: "Directive §3"));
        }

        // 3. boundary-termination candidates (Directive §21A): an endpoint of one movement's
        //    boundary that lies inside/on the opposing envelope is a legitimate Legacy
        //    conflict-zone extremum that pure intersections miss.
        if (regionA.IsValid && regionB.IsValid)
        {
            AddTerminationCandidates(points, owner: clearing, ownerRefs: refA, other: entering,
                otherRefs: refB, otherRegion: regionB, ownerIsClearing: true);
            AddTerminationCandidates(points, owner: entering, ownerRefs: refB, other: clearing,
                otherRefs: refA, otherRegion: regionA, ownerIsClearing: false);
        }

        var dedup = Deduplicate(points);

        // 4. exact overlap evaluation — only when nothing was found and AABB pre-filter passes
        if (dedup.Count == 0 && regionA.IsValid && regionB.IsValid
            && AabbOverlap(clearing, entering)
            && EnvelopeRegion.OverlapWithoutCrossing(regionA, regionB, out var evidence))
        {
            findings.Add(new ValidationFinding(CodePossibleUnresolvedConflict, Severity.ReviewRequired,
                $"{clearing.MovementId} × {entering.MovementId}",
                $"Envelope regions overlap without any boundary crossing (POSSIBLE_UNRESOLVED_CONFLICT). Evidence: {evidence}.",
                "A silently missing conflict is worse than a wrong number — review the drawing.",
                SourceReference: "v3 §15; Directive §3"));
        }

        return new ConflictPointResult(dedup, findings);
    }

    private static void AddTerminationCandidates(List<ConflictPoint> points,
        MovementGeometry owner, double[] ownerRefs,
        MovementGeometry other, double[] otherRefs,
        EnvelopeRegion otherRegion, bool ownerIsClearing)
    {
        for (var i = 0; i < 2; i++)
        {
            var boundary = owner.Boundaries[i];
            foreach (var (pt, stationOnOwner, endName) in new[]
            {
                (boundary.Start, 0.0, "start"),
                (boundary.End, boundary.TotalLength, "end"),
            })
            {
                if (!otherRegion.Contains(pt)) continue;

                var ownerStation = Math.Abs(stationOnOwner - ownerRefs[i]);
                // project onto BOTH opposing boundaries — near and far edges of the conflict
                // zone are distinct legitimate candidates (David's manual practice).
                for (var j = 0; j < 2; j++)
                {
                    var (projStation, _) = other.Boundaries[j].NearestStation(pt);
                    var otherStation = Math.Abs(projStation - otherRefs[j]);
                    points.Add(ownerIsClearing
                        ? new ConflictPoint(pt, ownerStation, otherStation,
                            $"{owner.MovementId}.b{i + 1}@{endName}",
                            $"{other.MovementId}.b{j + 1}~proj",
                            Origin: "boundary-termination")
                        : new ConflictPoint(pt, otherStation, ownerStation,
                            $"{other.MovementId}.b{j + 1}~proj",
                            $"{owner.MovementId}.b{i + 1}@{endName}",
                            Origin: "boundary-termination"));
                }
            }
        }
    }

    // ---------------- vehicle × pedestrian ----------------

    private ConflictPointResult VehiclePedestrian(MovementGeometry clearing, MovementGeometry entering)
    {
        var findings = new List<ValidationFinding>();
        var ped = clearing.Mode == MovementMode.Pedestrian ? clearing : entering;
        var veh = clearing.Mode == MovementMode.Pedestrian ? entering : clearing;
        var pedIsClearing = clearing.Mode == MovementMode.Pedestrian;

        if (veh.Boundaries.Count != 2)
            findings.Add(new ValidationFinding(CodeWrongBoundaryCount, Severity.Error, veh.MovementId,
                $"Movement '{veh.MovementId}' has {veh.Boundaries.Count} envelope boundaries (expected 2)."));
        if (veh.StopLine is null)
            findings.Add(new ValidationFinding(CodeNoStopLine, Severity.Error, veh.MovementId,
                $"Movement '{veh.MovementId}' has no stop/reference line."));
        if (ped.Boundaries.Count < 2)
            findings.Add(new ValidationFinding(CodeWrongBoundaryCount, Severity.Error, ped.MovementId,
                $"Pedestrian crossing '{ped.MovementId}' has {ped.Boundaries.Count} edges (needs at least 2)."));
        if (pedIsClearing && ped.PedestrianWidthMeters is null)
            findings.Add(new ValidationFinding(CodePedestrianWidthMissing, Severity.Error, ped.MovementId,
                $"Pedestrian crossing '{ped.MovementId}' has no project width W (Pedestrian Xing / sidecar). " +
                "W is authoritative project data and is never derived from edge-length averaging.",
                SourceReference: "Directive §5–§6"));
        if (findings.Any(f => f.Severity == Severity.Error))
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), findings);

        var vehRefs = ResolveReferences(veh, findings);
        if (vehRefs is null)
            return new ConflictPointResult(Array.Empty<ConflictPoint>(), findings);

        var points = new List<ConflictPoint>();
        // The strip the pedestrians actually walk in: of the crossing's N drawn edges, the two that
        // are furthest apart are its outer sides. Crossings are regularly drawn in more than two
        // pieces (Lin's crossing b has three), so picking edges 1 and 2 would describe a sliver.
        var crossingStrips = CrossingStrips(ped.Boundaries);
        for (var i = 0; i < 2; i++)
        {
            var boundary = veh.Boundaries[i];
            var intersectedEdges = 0;
            var missedEdges = 0;
            var maxHitStation = double.MinValue;
            for (var e = 0; e < ped.Boundaries.Count; e++)
            {
                var edge = ped.Boundaries[e];
                var hits = boundary.IntersectionsWith(edge);
                if (hits.Count > 0) intersectedEdges++; else missedEdges++;
                foreach (var hit in hits)
                {
                    maxHitStation = Math.Max(maxHitStation, hit.StationA);
                    var vehStation = Math.Abs(hit.StationA - vehRefs[i]);
                    points.Add(pedIsClearing
                        ? new ConflictPoint(hit.Point, ped.PedestrianWidthMeters!.Value, vehStation,
                            $"{ped.MovementId}.e{e + 1}", $"{veh.MovementId}.b{i + 1}")
                        : new ConflictPoint(hit.Point, vehStation, 0.0,
                            $"{veh.MovementId}.b{i + 1}", $"{ped.MovementId}.e{e + 1}"));
                }

                // §7 coverage-gap diagnostic: edge does not reach this boundary but its
                // endpoint terminates nearby — never silently extended.
                if (hits.Count == 0)
                {
                    foreach (var end in new[] { edge.Start, edge.End })
                    {
                        var (_, dist) = boundary.NearestStation(end);
                        if (dist > Tolerances.OnCurve * 100 && dist <= PedestrianCoverageGapMeters)
                        {
                            findings.Add(new ValidationFinding(CodePedestrianEdgeCoverageGap,
                                Severity.ReviewRequired, $"{ped.MovementId} × {veh.MovementId}",
                                $"Crossing edge {ped.MovementId}.e{e + 1} ends {dist:F2} m short of boundary " +
                                $"{veh.MovementId}.b{i + 1} (PEDESTRIAN_EDGE_COVERAGE_GAP). No silent extension is applied.",
                                RecommendedAction: "Correct the drawing or confirm the intended edge in Project Setup.",
                                SourceReference: "Directive §7"));
                            break;
                        }
                    }
                }
            }

            // Directive §21A applied to crossings: a vehicle boundary that enters the crossing
            // (intersects some edges) but terminates before the remaining edge(s) ends INSIDE
            // the crossing band. Its drawn end is a legitimate Legacy measurement extremum —
            // David's manual practice measures to the end of the drawn path
            // (Example 1, E-R→a: manual CD 18.95 ≈ boundary full length 18.93).
            // The drawn far end is the end the vehicle travels TOWARDS — the one further from its
            // reference station. It is at TotalLength only when the polyline was drawn from the
            // stop line outwards, which is the documented convention but not something every
            // drawing honours: a reversed polyline carries its stop line at TotalLength, and
            // taking that as the "termination" put a candidate on the stop line at distance zero,
            // which maximises the intergreen and therefore always governed (Lin, 05293 b→S-L).
            // Either drawn end may be the one that stops inside the crossing — which one it is
            // depends on the direction the polyline happens to be drawn in, and that convention is
            // not honoured by every drawing. What decides it is whether the end actually lies in
            // the crossing: a stop line that sits inside the crossing legitimately gives distance 0
            // (Example 2, b→S-R, the engineer's own 14), while an end tens of metres away is not a
            // termination in the crossing at all (Lin 05293, b→S-L, where it wrongly governed).
            //
            // ED-015 (David, 2026-08-27): the conflict with a crossing ends only after the crossing.
            // So for the CLEARING vehicle the drawn end is not the measurement point — the boundary
            // is carried straight on and measured to where it leaves the crossing (a longer CD, the
            // conservative direction). For the ENTERING vehicle the conflict begins where the boundary
            // first meets the crossing, which the intersection candidates already hold; its drawn end
            // stays as a traceable candidate but can never govern.
            if (intersectedEdges > 0 && missedEdges > 0 && crossingStrips.Count > 0)
            {
                foreach (var (station, endName, atStart) in new[] { (0.0, "start", true), (boundary.TotalLength, "end", false) })
                {
                    var endPoint = boundary.PointAtStation(station);
                    // Inside a strip, or sitting exactly on a drawn edge — a boundary that stops on
                    // the crossing line has stopped in the crossing (Example 2, W-R: 0.000 m).
                    var onCrossing = crossingStrips.Any(strip => strip.Contains(endPoint))
                        || ped.Boundaries.Any(e => e.NearestStation(endPoint).Distance <= Tolerances.PointDeduplication);
                    if (!onCrossing) continue;
                    var endStation = Math.Abs(station - vehRefs[i]);

                    if (pedIsClearing)
                    {
                        points.Add(new ConflictPoint(endPoint, ped.PedestrianWidthMeters!.Value, endStation,
                            $"{ped.MovementId}.band", $"{veh.MovementId}.b{i + 1}@{endName}",
                            Origin: "boundary-termination"));
                        findings.Add(new ValidationFinding(CodePedestrianEdgeCoverageGap,
                            Severity.ReviewRequired, $"{veh.MovementId} × {ped.MovementId}",
                            $"Vehicle boundary {veh.MovementId}.b{i + 1} terminates inside crossing '{ped.MovementId}' " +
                            $"(intersects {intersectedEdges} of {intersectedEdges + missedEdges} edges). Its drawn " +
                            $"{endName} was added as a termination candidate; the drawing may be incomplete.",
                            RecommendedAction: "Verify the boundary reaches the far crossing edge, or confirm the drawn extent.",
                            SourceReference: "Directive §21A"));
                        continue;
                    }

                    // clearing vehicle: carry the boundary straight on and find where it leaves the crossing
                    var exit = CrossingExit(boundary, atStart, endPoint, ped.Boundaries);
                    if (exit is null)
                    {
                        points.Add(new ConflictPoint(endPoint, endStation, 0.0,
                            $"{veh.MovementId}.b{i + 1}@{endName}", $"{ped.MovementId}.band",
                            Origin: "boundary-termination"));
                        // Two different situations look the same to the extension: the end already
                        // sits on the far edge (Example 1, E-R→a: 2.6 cm — nothing to extend to, the
                        // drawn end IS the exit, and the engineer measured exactly that), or the far
                        // edge genuinely is not where the boundary is heading. Tell them apart.
                        var toNearestEdge = ped.Boundaries.Min(e => e.NearestStation(endPoint).Distance);
                        if (toNearestEdge <= EndsAtCrossingEdgeMeters)
                            findings.Add(new ValidationFinding(CodePedestrianEdgeCoverageGap,
                                Severity.Warning, $"{veh.MovementId} × {ped.MovementId}",
                                $"Vehicle boundary {veh.MovementId}.b{i + 1} ends at an edge of crossing '{ped.MovementId}' " +
                                $"({toNearestEdge * 100:F0} cm from it); its drawn {endName} is the crossing exit (ED-015).",
                                SourceReference: "Directive §21A; ED-015"));
                        else
                            findings.Add(new ValidationFinding(CodePedestrianEdgeCoverageGap,
                                Severity.ReviewRequired, $"{veh.MovementId} × {ped.MovementId}",
                                $"Vehicle boundary {veh.MovementId}.b{i + 1} terminates inside crossing '{ped.MovementId}' " +
                                $"and no far crossing edge lies on its continuation; its drawn {endName} was used (ED-015 fallback).",
                                RecommendedAction: "Check the crossing's far edge is drawn where the movement leaves it; the clearing distance may be short.",
                                SourceReference: "Directive §21A; ED-015"));
                        continue;
                    }
                    var (exitPoint, beyondEnd) = exit.Value;
                    points.Add(new ConflictPoint(exitPoint, endStation + beyondEnd, 0.0,
                        $"{veh.MovementId}.b{i + 1}@{endName}+exit", $"{ped.MovementId}.band",
                        Origin: "boundary-termination"));
                    findings.Add(new ValidationFinding(CodePedestrianEdgeCoverageGap,
                        Severity.ReviewRequired, $"{veh.MovementId} × {ped.MovementId}",
                        $"Vehicle boundary {veh.MovementId}.b{i + 1} terminates inside crossing '{ped.MovementId}' " +
                        $"(intersects {intersectedEdges} of {intersectedEdges + missedEdges} edges). Measured to the " +
                        $"crossing exit, {beyondEnd:F2} m beyond its drawn {endName} (ED-015).",
                        RecommendedAction: "Confirm the drawn boundary should reach the far crossing edge.",
                        SourceReference: "Directive §21A; ED-015"));
                }
            }
        }

        return new ConflictPointResult(Deduplicate(points), findings);
    }

    /// <summary>
    /// The ground a crossing actually covers, as every strip spanned by a pair of its drawn edges.
    /// Crossings are regularly drawn in more than two pieces — Example 2's crossing b has five, and
    /// Lin's crossing b three — so no single pair describes the crossing, and the pair whose
    /// midpoints lie furthest apart can be two segments of the SAME side. Used only to test whether
    /// a vehicle boundary terminates inside the crossing (§21A); never a source of measurement,
    /// where W remains the authoritative project width (§5–§6).
    /// </summary>
    private static IReadOnlyList<EnvelopeRegion> CrossingStrips(IReadOnlyList<PolyCurve2D> edges)
    {
        var strips = new List<EnvelopeRegion>();
        for (var a = 0; a < edges.Count; a++)
        for (var b = a + 1; b < edges.Count; b++)
        {
            var region = EnvelopeRegion.Build(edges[a], edges[b]);
            if (region.IsValid) strips.Add(region);
        }
        return strips;
    }

    /// <summary>A drawn end this close to a crossing edge already sits on the exit; there is nothing to extend to.</summary>
    private const double EndsAtCrossingEdgeMeters = 0.5;

    /// <summary>How far to carry a boundary past its drawn end when looking for the crossing's far edge.</summary>
    private const double CrossingExitSearchMeters = 60.0;

    /// <summary>
    /// ED-015: where a boundary that stops inside a crossing would leave it if drawn on. The boundary
    /// is continued along the geometry of its last segment — a straight segment goes straight on, an
    /// arc keeps its centre, radius and sense of rotation (turns are drawn as fillets, Appendix A, so
    /// a straight tangent would miss the far edge: Example 1, E-R→a). The farthest crossing edge the
    /// continuation meets is the exit. Returns the exit point and the distance beyond the drawn end,
    /// or null when no crossing edge lies ahead (the caller then keeps the drawn end and says so).
    /// </summary>
    private static (Point2D Point, double BeyondEnd)? CrossingExit(
        PolyCurve2D boundary, bool atStart, Point2D endPoint, IReadOnlyList<PolyCurve2D> crossingEdges)
    {
        var continuation = Continuation(boundary, atStart, endPoint);
        if (continuation is null) return null;
        CurveIntersection? farthest = null;
        foreach (var edge in crossingEdges)
        foreach (var hit in continuation.IntersectionsWith(edge))
        {
            if (hit.StationA <= Tolerances.PointDeduplication) continue;     // the drawn end itself
            if (farthest is null || hit.StationA > farthest.Value.StationA) farthest = hit;
        }
        return farthest is null ? null : (farthest.Value.Point, farthest.Value.StationA);
    }

    /// <summary>The boundary carried on past its drawn end, along the last segment's own geometry.</summary>
    private static PolyCurve2D? Continuation(PolyCurve2D curve, bool atStart, Point2D endPoint)
    {
        var seg = atStart ? curve.Segments[0] : curve.Segments[^1];
        switch (seg)
        {
            case LineSegment2D l:
            {
                var t = atStart ? l.A - l.B : l.B - l.A;                 // direction of leaving the curve
                if (t.Length < Tolerances.NumericEpsilon) return null;
                var dir = t.Scaled(1.0 / t.Length);
                return new PolyCurve2D(new ISegment2D[]
                {
                    new LineSegment2D(endPoint, endPoint + dir.Scaled(CrossingExitSearchMeters)),
                });
            }
            case CircularArcSegment2D a:
            {
                // keep turning the same way: leaving through the end continues the sweep's sign,
                // leaving through the start runs the sweep backwards
                var sweepSign = atStart ? -Math.Sign(a.SweepRad) : Math.Sign(a.SweepRad);
                if (sweepSign == 0) return null;
                var startAngle = atStart ? a.StartAngleRad : a.StartAngleRad + a.SweepRad;
                var sweep = sweepSign * Math.Min(Math.PI, CrossingExitSearchMeters / Math.Max(a.Radius, Tolerances.NumericEpsilon));
                return new PolyCurve2D(new ISegment2D[]
                {
                    new CircularArcSegment2D(a.Center, a.Radius, startAngle, sweep),
                });
            }
            default:
                return null;
        }
    }

    // ---------------- shared helpers ----------------

    /// <summary>
    /// §14 reference policy: exact intersection → valid; explicitly confirmed endpoint →
    /// valid with provenance; anything else → ERROR (blocked until resolved). The 0.5 m
    /// endpoint proximity is only a SUGGESTION surfaced in the finding text.
    /// </summary>
    private double[]? ResolveReferences(MovementGeometry m, List<ValidationFinding> findings)
    {
        var refs = new double[m.Boundaries.Count];
        for (var i = 0; i < m.Boundaries.Count; i++)
        {
            var curveId = $"{m.MovementId}.b{i + 1}";
            var r = ReferenceStation.Resolve(m.Boundaries[i], m.StopLine!, _endpointSuggestionToleranceMeters);
            if (r is null)
            {
                findings.Add(new ValidationFinding(CodeReferenceUnresolved, Severity.Error, m.MovementId,
                    $"Boundary {curveId} cannot be referenced to its stop line (no intersection, no nearby endpoint).",
                    SourceReference: "v3 §14; Directive §14"));
                return null;
            }
            if (r.Method == ReferenceStation.Method.EndpointFallback
                && !m.ConfirmedEndpointReferences.Contains(curveId))
            {
                findings.Add(new ValidationFinding(CodeReferenceEndpointUnconfirmed, Severity.Error, m.MovementId,
                    $"Boundary {curveId} does not intersect its stop line. A nearby endpoint exists at station " +
                    $"{r.Station:F3} (within {_endpointSuggestionToleranceMeters} m) but an endpoint may not become an " +
                    "engineering reference without explicit confirmation.",
                    RecommendedAction: "Confirm the endpoint reference in Project Setup (persisted in the sidecar) or fix the drawing.",
                    SourceReference: "Directive §14"));
                return null;
            }
            refs[i] = r.Station;
        }
        return refs;
    }

    private static bool AabbOverlap(MovementGeometry a, MovementGeometry b)
    {
        var (aMin, aMax) = BoundingBox(a.Boundaries);
        var (bMin, bMax) = BoundingBox(b.Boundaries);
        return aMin.X <= bMax.X && bMin.X <= aMax.X && aMin.Y <= bMax.Y && bMin.Y <= aMax.Y;
    }

    private static List<ConflictPoint> Deduplicate(List<ConflictPoint> points)
    {
        var result = new List<ConflictPoint>();
        foreach (var p in points.OrderBy(p => p.ClearingDistanceMeters).ThenBy(p => p.EnteringDistanceMeters))
        {
            if (!result.Any(e => e.Location.DistanceTo(p.Location) < Tolerances.PointDeduplication
                                 && Math.Abs(e.ClearingDistanceMeters - p.ClearingDistanceMeters) < Tolerances.PointDeduplication
                                 && Math.Abs(e.EnteringDistanceMeters - p.EnteringDistanceMeters) < Tolerances.PointDeduplication))
                result.Add(p);
        }
        return result;
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
            if (m.Mode != MovementMode.Pedestrian && m.LaneCentrelines.Count == 0)
                findings.Add(new ValidationFinding(CodeGeometryMissing, Severity.Error, m.MovementId,
                    $"2025_GEOMETRY_MISSING: movement '{m.MovementId}' ({role}) has no registered lane centrelines. " +
                    "The 2025 method requires a travel-path centreline per lane (§5.4); envelope boundaries must not be used.",
                    RecommendedAction: "Register centrelines with IG_REGISTER_CENTERLINES or draw them.",
                    SourceReference: "SR-5.4.2; v3 §16"));
            if (m.Mode != MovementMode.Pedestrian && m.StopLine is null)
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
