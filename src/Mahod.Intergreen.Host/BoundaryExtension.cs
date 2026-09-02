using Mahod.Intergreen.Geometry;

namespace Mahod.Intergreen.Host;

/// <summary>Outcome of trying to carry a boundary on to its stop line. <see cref="Extended"/> is null when nothing was done; <see cref="Reason"/> says why.</summary>
public sealed record ExtensionOutcome(PolyCurve2D? Extended, bool AtStart, double LengthMeters, Point2D From, Point2D To, string? Reason)
{
    public bool Applied => Extended is not null;
}

/// <summary>
/// David 2026-08-27, item 6b, as asked: a movement line that stops short of its stop line is
/// *extended* to it — not "confirmed where it is". The extension runs along the boundary's own end
/// tangent (a straight segment continues straight, an arc leaves along its tangent) to the point where
/// that ray meets the stop line; its length is measured along that ray, which at a shallow angle is
/// longer than the perpendicular gap and is the number the threshold is compared with.
///
/// The extension is virtual by default: it is applied to the geometry handed to the engine, so the
/// engine finds an exact intersection and no sidecar confirmation is needed; the DWG is untouched
/// unless the engineer asks for it explicitly. Anything ambiguous — both ends short of the stop line,
/// a ray that misses or would have to run backwards, an unsupported segment type — is left alone and
/// reported, so it reaches the engineer as a pending reference instead of a guess (ED-016 amendment).
/// </summary>
public static class BoundaryExtension
{
    /// <summary>
    /// Try to extend <paramref name="boundary"/> to <paramref name="stopLine"/> by at most
    /// <paramref name="maxExtensionMeters"/> along the end tangent. Never extends a boundary that already
    /// meets its stop line.
    /// </summary>
    public static ExtensionOutcome TryExtendToStopLine(PolyCurve2D boundary, PolyCurve2D stopLine, double maxExtensionMeters)
    {
        var none = new Point2D(double.NaN, double.NaN);
        if (maxExtensionMeters <= 0)
            return new ExtensionOutcome(null, false, 0, none, none, "tolerance is 0 — automatic extension is off");
        if (boundary.IntersectionsWith(stopLine).Count > 0)
            return new ExtensionOutcome(null, false, 0, none, none, "the boundary already meets its stop line");

        var candidates = new List<(bool AtStart, Point2D From, Point2D To, double Length)>();
        foreach (var atStart in new[] { false, true })
        {
            var seg = atStart ? boundary.Segments[0] : boundary.Segments[^1];
            var from = atStart ? seg.Start : seg.End;
            var dir = OutwardTangent(seg, atStart);
            if (dir is null) continue;                                       // unsupported segment type
            var (dx, dy) = dir.Value;
            var ray = new LineSegment2D(from, new Point2D(from.X + dx * maxExtensionMeters, from.Y + dy * maxExtensionMeters));
            Point2D? best = null;
            foreach (var s in stopLine.Segments)
                foreach (var p in Intersections.Of(ray, s))
                    if (best is null || from.DistanceTo(p) < from.DistanceTo(best.Value)) best = p;
            if (best is Point2D hit && from.DistanceTo(hit) > 1e-6)
                candidates.Add((atStart, from, hit, from.DistanceTo(hit)));
        }

        if (candidates.Count == 0)
            return new ExtensionOutcome(null, false, 0, none, none,
                $"no end reaches the stop line within {maxExtensionMeters * 100:F0} cm along its own tangent");
        if (candidates.Count > 1)
            return new ExtensionOutcome(null, false, 0, none, none,
                "both ends are short of the stop line — which end starts the movement is the engineer's call");

        var (atStartC, fromC, toC, lengthC) = candidates[0];
        var segments = boundary.Segments.ToList();
        if (atStartC) segments.Insert(0, new LineSegment2D(toC, fromC));
        else segments.Add(new LineSegment2D(fromC, toC));
        return new ExtensionOutcome(new PolyCurve2D(segments), atStartC, lengthC, fromC, toC, null);
    }

    /// <summary>Unit vector pointing out of the curve at the given end, along that end's tangent.</summary>
    public static (double X, double Y)? OutwardTangent(ISegment2D segment, bool atStart)
    {
        switch (segment)
        {
            case LineSegment2D l:
            {
                var dx = l.B.X - l.A.X; var dy = l.B.Y - l.A.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-12) return null;
                return atStart ? (-dx / len, -dy / len) : (dx / len, dy / len);
            }
            case CircularArcSegment2D a:
            {
                var theta = atStart ? a.StartAngleRad : a.StartAngleRad + a.SweepRad;
                var sense = Math.Sign(a.SweepRad);
                if (sense == 0) return null;
                // direction of travel along the arc at theta
                var tx = -Math.Sin(theta) * sense; var ty = Math.Cos(theta) * sense;
                return atStart ? (-tx, -ty) : (tx, ty);
            }
            default:
                return null;
        }
    }
}
