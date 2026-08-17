using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Mahod.Intergreen.Geometry;

/// <summary>One intersection between two polycurves, with the exact station on each.</summary>
public readonly record struct CurveIntersection(Point2D Point, double StationA, double StationB);

/// <summary>
/// A connected chain of exact line/arc segments (v3 §12). Stations are arc lengths from
/// the curve start. Reversing the source curve never changes measured distances between
/// two on-curve points (v3 §14 invariant) because stations are re-derived geometrically.
/// </summary>
public sealed class PolyCurve2D
{
    private readonly List<ISegment2D> _segments;
    private readonly double[] _cumulative; // cumulative length before segment i

    public PolyCurve2D(IEnumerable<ISegment2D> segments)
    {
        _segments = segments.ToList();
        if (_segments.Count == 0)
            throw new ArgumentException("polycurve needs at least one segment");
        for (var i = 1; i < _segments.Count; i++)
        {
            var gap = _segments[i - 1].End.DistanceTo(_segments[i].Start);
            if (gap > Tolerances.SegmentConnection)
                throw new ArgumentException(
                    $"polycurve disconnected at segment {i}: gap {gap:E3} m (BOUNDARY_NOT_CONNECTED)");
        }
        _cumulative = new double[_segments.Count];
        double acc = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            _cumulative[i] = acc;
            acc += _segments[i].Length;
        }
        TotalLength = acc;
    }

    public IReadOnlyList<ISegment2D> Segments => _segments;
    public double TotalLength { get; }
    public Point2D Start => _segments[0].Start;
    public Point2D End => _segments[^1].End;

    public Point2D PointAtStation(double station)
    {
        if (station < -Tolerances.NumericEpsilon || station > TotalLength + Tolerances.NumericEpsilon)
            throw new ArgumentOutOfRangeException(nameof(station), $"{station} outside [0, {TotalLength}]");
        station = Math.Clamp(station, 0, TotalLength);
        for (var i = _segments.Count - 1; i >= 0; i--)
        {
            if (station >= _cumulative[i] - Tolerances.NumericEpsilon)
                return _segments[i].PointAtLength(Math.Min(station - _cumulative[i], _segments[i].Length));
        }
        return _segments[0].PointAtLength(station);
    }

    /// <summary>Station of an on-curve point; null when the point is not on the curve.</summary>
    public double? StationOf(Point2D point, double tolerance = Tolerances.OnCurve)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            var local = _segments[i].LengthAtPoint(point, tolerance);
            if (local is double l)
                return _cumulative[i] + l;
        }
        return null;
    }

    /// <summary>
    /// Every intersection with another polycurve — all of them, deduplicated numerically,
    /// never truncated (v3 §15).
    /// </summary>
    public List<CurveIntersection> IntersectionsWith(PolyCurve2D other)
    {
        var raw = new List<CurveIntersection>();
        foreach (var sa in _segments)
        {
            foreach (var sb in other._segments)
            {
                foreach (var p in Intersections.Of(sa, sb))
                {
                    var stA = StationOf(p, Tolerances.OnCurve * 100);
                    var stB = other.StationOf(p, Tolerances.OnCurve * 100);
                    if (stA is double a && stB is double b)
                        raw.Add(new CurveIntersection(p, a, b));
                }
            }
        }
        var dedup = new List<CurveIntersection>();
        foreach (var c in raw)
        {
            if (!dedup.Any(existing => existing.Point.DistanceTo(c.Point) < Tolerances.PointDeduplication))
                dedup.Add(c);
        }
        return dedup;
    }

    /// <summary>
    /// Nearest on-curve station to an arbitrary point (exact projection per segment).
    /// Used for boundary-termination candidates (Directive §21A) — never for silently
    /// extending geometry.
    /// </summary>
    public (double Station, double Distance) NearestStation(Point2D p)
    {
        var bestStation = 0.0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _segments.Count; i++)
        {
            var (local, dist) = NearestOnSegment(_segments[i], p);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestStation = _cumulative[i] + local;
            }
        }
        return (bestStation, bestDist);
    }

    private static (double LocalStation, double Distance) NearestOnSegment(ISegment2D seg, Point2D p)
    {
        switch (seg)
        {
            case LineSegment2D l:
            {
                var d = l.B - l.A;
                var len2 = Vector2D.Dot(d, d);
                var t = len2 < Tolerances.NumericEpsilon ? 0.0 : Math.Clamp(Vector2D.Dot(p - l.A, d) / len2, 0, 1);
                var proj = new Point2D(l.A.X + d.X * t, l.A.Y + d.Y * t);
                return (t * l.Length, proj.DistanceTo(p));
            }
            case CircularArcSegment2D a:
            {
                var angle = Math.Atan2(p.Y - a.Center.Y, p.X - a.Center.X);
                var delta = CircularArcSegment2D.NormalizeSigned(angle - a.StartAngleRad);
                var along = a.SweepRad >= 0
                    ? CircularArcSegment2D.NormalizePositive(delta)
                    : CircularArcSegment2D.NormalizePositive(-delta);
                if (along <= Math.Abs(a.SweepRad))
                {
                    var radial = Math.Abs(p.DistanceTo(a.Center) - a.Radius);
                    return (along * a.Radius, radial);
                }
                var dStart = p.DistanceTo(a.Start);
                var dEnd = p.DistanceTo(a.End);
                return dStart <= dEnd ? (0.0, dStart) : (a.Length, dEnd);
            }
            default:
                throw new NotSupportedException(seg.GetType().Name);
        }
    }

    public PolyCurve2D Reversed()
        => new(_segments.AsEnumerable().Reverse().Select(s => s.Reversed()));

    public PolyCurve2D Transformed(Transform2D t)
        => new(_segments.Select(s => s.Transformed(t)));

    /// <summary>
    /// Builds an exact polycurve from AutoCAD-style vertices with bulges.
    /// Bulge b on vertex i describes the arc from vertex i to vertex i+1 (angle 4·atan(b)).
    /// </summary>
    public static PolyCurve2D FromVertices(IReadOnlyList<(Point2D Point, double Bulge)> vertices, bool closed = false)
    {
        if (vertices.Count < 2)
            throw new ArgumentException("need at least 2 vertices");
        var segs = new List<ISegment2D>();
        var count = closed ? vertices.Count : vertices.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var (p1, bulge) = vertices[i];
            var p2 = vertices[(i + 1) % vertices.Count].Point;
            if (p1.DistanceTo(p2) < Tolerances.NumericEpsilon) continue;
            segs.Add(bulge == 0
                ? new LineSegment2D(p1, p2)
                : CircularArcSegment2D.FromBulge(p1, p2, bulge));
        }
        return new PolyCurve2D(segs);
    }

    // ---- deterministic JSON serialization (round-trip must not change geometry) ----

    public string ToJson()
    {
        var items = _segments.Select(object (s) => s switch
        {
            LineSegment2D l => new Dictionary<string, object>
            {
                ["type"] = "line",
                ["a"] = new[] { l.A.X, l.A.Y },
                ["b"] = new[] { l.B.X, l.B.Y },
            },
            CircularArcSegment2D a => new Dictionary<string, object>
            {
                ["type"] = "arc",
                ["center"] = new[] { a.Center.X, a.Center.Y },
                ["radius"] = a.Radius,
                ["startAngleRad"] = a.StartAngleRad,
                ["sweepRad"] = a.SweepRad,
            },
            _ => throw new NotSupportedException(s.GetType().Name),
        }).ToList();
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["segments"] = items });
    }

    public static PolyCurve2D FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var segs = new List<ISegment2D>();
        foreach (var el in doc.RootElement.GetProperty("segments").EnumerateArray())
        {
            var type = el.GetProperty("type").GetString();
            if (type == "line")
            {
                var a = el.GetProperty("a");
                var b = el.GetProperty("b");
                segs.Add(new LineSegment2D(
                    new Point2D(a[0].GetDouble(), a[1].GetDouble()),
                    new Point2D(b[0].GetDouble(), b[1].GetDouble())));
            }
            else if (type == "arc")
            {
                var c = el.GetProperty("center");
                segs.Add(new CircularArcSegment2D(
                    new Point2D(c[0].GetDouble(), c[1].GetDouble()),
                    el.GetProperty("radius").GetDouble(),
                    el.GetProperty("startAngleRad").GetDouble(),
                    el.GetProperty("sweepRad").GetDouble()));
            }
            else throw new NotSupportedException($"UNSUPPORTED_GEOMETRY segment type '{type}'");
        }
        return new PolyCurve2D(segs);
    }
}

/// <summary>
/// Reference-station resolution (v3 §14): exact intersection with the assigned stop/reference
/// line wins; a near-endpoint fallback is a WARNING; anything else is an ERROR.
/// </summary>
public static class ReferenceStation
{
    public enum Method { ExactIntersection, EndpointFallback }

    public sealed record Result(double Station, Method Method, Point2D At);

    /// <summary>
    /// Resolves the measurement origin of <paramref name="movementCurve"/> against its
    /// stop/reference line. Returns null when no intersection exists and no endpoint is
    /// within <paramref name="endpointToleranceMeters"/> of the reference line.
    /// </summary>
    public static Result? Resolve(PolyCurve2D movementCurve, PolyCurve2D referenceLine,
        double endpointToleranceMeters)
    {
        var hits = movementCurve.IntersectionsWith(referenceLine);
        if (hits.Count > 0)
        {
            // if the curve crosses the stop line more than once, the measurement origin is
            // the crossing nearest the curve's physical start-or-end closest to the line
            var best = hits.OrderBy(h => h.StationA).First();
            return new Result(best.StationA, Method.ExactIntersection, best.Point);
        }

        // endpoint fallback: nearest curve endpoint whose distance to the reference line
        // is within tolerance (WARNING at call site — never silent)
        foreach (var (pt, station) in new[] { (movementCurve.Start, 0.0), (movementCurve.End, movementCurve.TotalLength) })
        {
            var d = DistanceToCurve(referenceLine, pt);
            if (d <= endpointToleranceMeters)
                return new Result(station, Method.EndpointFallback, pt);
        }
        return null;
    }

    private static double DistanceToCurve(PolyCurve2D curve, Point2D p)
    {
        var best = double.MaxValue;
        foreach (var seg in curve.Segments)
        {
            switch (seg)
            {
                case LineSegment2D l:
                {
                    var d = l.B - l.A;
                    var len2 = Vector2D.Dot(d, d);
                    var t = len2 < Tolerances.NumericEpsilon ? 0 : Math.Clamp(Vector2D.Dot(p - l.A, d) / len2, 0, 1);
                    var proj = new Point2D(l.A.X + d.X * t, l.A.Y + d.Y * t);
                    best = Math.Min(best, proj.DistanceTo(p));
                    break;
                }
                case CircularArcSegment2D a:
                {
                    // radial distance applies only when the point's polar angle falls
                    // inside the arc's angular range; otherwise the nearest endpoint wins
                    var angle = Math.Atan2(p.Y - a.Center.Y, p.X - a.Center.X);
                    var delta = CircularArcSegment2D.NormalizeSigned(angle - a.StartAngleRad);
                    var along = a.SweepRad >= 0
                        ? CircularArcSegment2D.NormalizePositive(delta)
                        : CircularArcSegment2D.NormalizePositive(-delta);
                    var inRange = along <= Math.Abs(a.SweepRad);
                    var radial = Math.Abs(p.DistanceTo(a.Center) - a.Radius);
                    best = Math.Min(best, inRange ? radial
                        : Math.Min(p.DistanceTo(a.Start), p.DistanceTo(a.End)));
                    break;
                }
            }
        }
        return best;
    }
}
