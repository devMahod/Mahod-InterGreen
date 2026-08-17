using System;
using System.Collections.Generic;

namespace Mahod.Intergreen.Geometry;

/// <summary>Exact analytic segment-segment intersections (line×line, line×arc, arc×arc).</summary>
public static class Intersections
{
    public static List<Point2D> Of(ISegment2D a, ISegment2D b) => (a, b) switch
    {
        (LineSegment2D la, LineSegment2D lb) => LineLine(la, lb),
        (LineSegment2D la, CircularArcSegment2D ab) => LineArc(la, ab),
        (CircularArcSegment2D aa, LineSegment2D lb) => LineArc(lb, aa),
        (CircularArcSegment2D aa, CircularArcSegment2D ab) => ArcArc(aa, ab),
        _ => throw new NotSupportedException(
            $"UNSUPPORTED_GEOMETRY: {a.GetType().Name} × {b.GetType().Name}"),
    };

    private static List<Point2D> LineLine(LineSegment2D a, LineSegment2D b)
    {
        var result = new List<Point2D>();
        var r = a.B - a.A;
        var s = b.B - b.A;
        var denom = Vector2D.Cross(r, s);
        if (Math.Abs(denom) < Tolerances.NumericEpsilon)
            return result; // parallel (collinear overlap is a validation concern, not a point)
        var qp = b.A - a.A;
        var t = Vector2D.Cross(qp, s) / denom;
        var u = Vector2D.Cross(qp, r) / denom;
        const double e = Tolerances.NumericEpsilon;
        if (t < -e || t > 1 + e || u < -e || u > 1 + e)
            return result;
        result.Add(new Point2D(a.A.X + r.X * Math.Clamp(t, 0, 1), a.A.Y + r.Y * Math.Clamp(t, 0, 1)));
        return result;
    }

    private static List<Point2D> LineArc(LineSegment2D line, CircularArcSegment2D arc)
    {
        var result = new List<Point2D>();
        var d = line.B - line.A;
        var f = line.A - arc.Center;
        var aa = Vector2D.Dot(d, d);
        if (aa < Tolerances.NumericEpsilon) return result;
        var bb = 2 * Vector2D.Dot(f, d);
        var cc = Vector2D.Dot(f, f) - arc.Radius * arc.Radius;
        var disc = bb * bb - 4 * aa * cc;
        // treat a near-tangent as tangent
        if (disc < 0 && disc > -Tolerances.NumericEpsilon * Math.Max(1.0, bb * bb)) disc = 0;
        if (disc < 0) return result;
        var sqrt = Math.Sqrt(disc);
        Span<double> ts = stackalloc double[2];
        var n = disc == 0 ? 1 : 2;
        ts[0] = (-bb - sqrt) / (2 * aa);
        ts[1] = (-bb + sqrt) / (2 * aa);
        const double e = 1e-9;
        for (var i = 0; i < n; i++)
        {
            var t = ts[i];
            if (t < -e || t > 1 + e) continue;
            var p = new Point2D(line.A.X + d.X * t, line.A.Y + d.Y * t);
            if (arc.LengthAtPoint(p, Tolerances.OnCurve * 10) is not null)
                result.Add(p);
        }
        return result;
    }

    private static List<Point2D> ArcArc(CircularArcSegment2D a, CircularArcSegment2D b)
    {
        var result = new List<Point2D>();
        var d = b.Center - a.Center;
        var dist = d.Length;
        if (dist < Tolerances.NumericEpsilon)
            return result; // concentric — overlap is a validation concern
        var r0 = a.Radius;
        var r1 = b.Radius;
        if (dist > r0 + r1 + Tolerances.NumericEpsilon) return result;
        if (dist < Math.Abs(r0 - r1) - Tolerances.NumericEpsilon) return result;

        var x = (dist * dist - r1 * r1 + r0 * r0) / (2 * dist);
        var ySq = r0 * r0 - x * x;
        if (ySq < 0 && ySq > -Tolerances.NumericEpsilon) ySq = 0;
        if (ySq < 0) return result;
        var y = Math.Sqrt(ySq);

        var ux = d.X / dist;
        var uy = d.Y / dist;
        var baseP = new Point2D(a.Center.X + ux * x, a.Center.Y + uy * x);
        var candidates = y < Tolerances.NumericEpsilon
            ? new[] { baseP }
            : new[]
            {
                new Point2D(baseP.X - uy * y, baseP.Y + ux * y),
                new Point2D(baseP.X + uy * y, baseP.Y - ux * y),
            };
        foreach (var p in candidates)
        {
            if (a.LengthAtPoint(p, Tolerances.OnCurve * 10) is not null
                && b.LengthAtPoint(p, Tolerances.OnCurve * 10) is not null)
                result.Add(p);
        }
        return result;
    }
}
