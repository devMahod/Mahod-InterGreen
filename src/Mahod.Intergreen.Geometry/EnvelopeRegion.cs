using System;
using System.Collections.Generic;
using System.Linq;

namespace Mahod.Intergreen.Geometry;

/// <summary>
/// Exact closed envelope region for a Legacy movement strip (Directive §3):
/// boundary A forward + cap(A.End→B.End) + boundary B reversed + cap(B.Start→A.Start).
/// Built on the exact line/arc model — never tessellated.
///
/// AABB may be used only as a cheap pre-filter; region truth comes from exact
/// point-in-region evaluation (ray casting over exact segments, with an on-boundary
/// pre-check so boundary contact is classified deterministically).
/// </summary>
public sealed class EnvelopeRegion
{
    private readonly List<ISegment2D> _loop;

    public PolyCurve2D BoundaryA { get; }
    public PolyCurve2D BoundaryB { get; }

    /// <summary>Null when the region is valid; otherwise the reason it cannot be closed.</summary>
    public string? InvalidReason { get; }

    public bool IsValid => InvalidReason is null;

    private EnvelopeRegion(PolyCurve2D a, PolyCurve2D b, List<ISegment2D> loop, string? invalidReason)
    {
        BoundaryA = a;
        BoundaryB = b;
        _loop = loop;
        InvalidReason = invalidReason;
    }

    public static EnvelopeRegion Build(PolyCurve2D boundaryA, PolyCurve2D boundaryB)
    {
        // envelope sides must not cross each other — a self-intersecting strip is not closable
        var selfHits = boundaryA.IntersectionsWith(boundaryB);
        string? invalid = null;
        if (selfHits.Count > 0)
            invalid = $"ENVELOPE_REGION_INVALID: the two boundaries intersect each other at {selfHits.Count} point(s)";

        var loop = new List<ISegment2D>();
        loop.AddRange(boundaryA.Segments);
        if (boundaryA.End.DistanceTo(boundaryB.End) > Tolerances.NumericEpsilon)
            loop.Add(new LineSegment2D(boundaryA.End, boundaryB.End));
        foreach (var s in boundaryB.Segments.Reverse())
            loop.Add(s.Reversed());
        if (boundaryB.Start.DistanceTo(boundaryA.Start) > Tolerances.NumericEpsilon)
            loop.Add(new LineSegment2D(boundaryB.Start, boundaryA.Start));

        return new EnvelopeRegion(boundaryA, boundaryB, loop, invalid);
    }

    /// <summary>
    /// Exact containment: true when the point is inside the closed region or on its boundary
    /// (within tolerance). Requires a valid region.
    /// </summary>
    public bool Contains(Point2D p, double onBoundaryTolerance = Tolerances.OnCurve * 100)
    {
        if (!IsValid)
            throw new InvalidOperationException(InvalidReason);

        // deterministic on-boundary pre-check
        foreach (var seg in _loop)
        {
            if (seg.LengthAtPoint(p, onBoundaryTolerance) is not null)
                return true;
        }

        // ray cast to +X with a deterministic sub-tolerance vertical offset to avoid
        // vertex/tangency degeneracy (documented; offset far below all engineering tolerances)
        var y = p.Y + 1.0e-7;
        var crossings = 0;
        foreach (var seg in _loop)
            crossings += RayCrossings(seg, p.X, y);
        return crossings % 2 == 1;
    }

    private static int RayCrossings(ISegment2D seg, double px, double y)
    {
        switch (seg)
        {
            case LineSegment2D l:
            {
                var (y1, y2) = (l.A.Y, l.B.Y);
                if ((y1 <= y && y2 <= y) || (y1 > y && y2 > y)) return 0;
                var t = (y - y1) / (y2 - y1);
                var x = l.A.X + (l.B.X - l.A.X) * t;
                return x > px ? 1 : 0;
            }
            case CircularArcSegment2D a:
            {
                var dy = y - a.Center.Y;
                if (Math.Abs(dy) >= a.Radius) return 0;
                var dx = Math.Sqrt(a.Radius * a.Radius - dy * dy);
                var count = 0;
                foreach (var x in new[] { a.Center.X - dx, a.Center.X + dx })
                {
                    if (x <= px) continue;
                    var pt = new Point2D(x, y);
                    if (a.LengthAtPoint(pt, Tolerances.OnCurve * 10) is not null)
                        count++;
                }
                return count;
            }
            default:
                throw new NotSupportedException(seg.GetType().Name);
        }
    }

    /// <summary>
    /// Exact region-overlap evaluation for two valid regions whose boundaries have already
    /// been proven crossing-free against each other. Under that precondition, overlap can
    /// only be containment of one region in the other — probed with exact containment of
    /// representative on-boundary points.
    /// </summary>
    public static bool OverlapWithoutCrossing(EnvelopeRegion a, EnvelopeRegion b, out string evidence)
    {
        foreach (var (probe, name) in Probes(b))
        {
            if (a.Contains(probe))
            {
                evidence = $"point {name} of the second envelope at ({probe.X:F3},{probe.Y:F3}) lies inside the first envelope";
                return true;
            }
        }
        foreach (var (probe, name) in Probes(a))
        {
            if (b.Contains(probe))
            {
                evidence = $"point {name} of the first envelope at ({probe.X:F3},{probe.Y:F3}) lies inside the second envelope";
                return true;
            }
        }
        evidence = "regions are disjoint (no boundary crossing, no containment probe inside)";
        return false;
    }

    private static IEnumerable<(Point2D P, string Name)> Probes(EnvelopeRegion r)
    {
        yield return (r.BoundaryA.Start, "A.start");
        yield return (r.BoundaryA.End, "A.end");
        yield return (r.BoundaryB.Start, "B.start");
        yield return (r.BoundaryB.End, "B.end");
        yield return (r.BoundaryA.PointAtStation(r.BoundaryA.TotalLength / 2), "A.mid");
        yield return (r.BoundaryB.PointAtStation(r.BoundaryB.TotalLength / 2), "B.mid");
    }
}
