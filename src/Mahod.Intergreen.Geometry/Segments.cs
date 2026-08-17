using System;

namespace Mahod.Intergreen.Geometry;

/// <summary>
/// Exact 2D curve segment (v3 §12 — never lose curve information; no silent tessellation).
/// </summary>
public interface ISegment2D
{
    Point2D Start { get; }
    Point2D End { get; }
    double Length { get; }
    Point2D PointAtLength(double lengthFromStart);

    /// <summary>Arc length from Start to the given on-segment point; null if the point is off-segment.</summary>
    double? LengthAtPoint(Point2D point, double tolerance);

    ISegment2D Reversed();
    ISegment2D Transformed(Transform2D t);
}

public sealed record LineSegment2D(Point2D A, Point2D B) : ISegment2D
{
    public Point2D Start => A;
    public Point2D End => B;
    public double Length => A.DistanceTo(B);

    public Point2D PointAtLength(double lengthFromStart)
    {
        var l = Length;
        if (l == 0) return A;
        var f = lengthFromStart / l;
        return new Point2D(A.X + (B.X - A.X) * f, A.Y + (B.Y - A.Y) * f);
    }

    public double? LengthAtPoint(Point2D p, double tolerance)
    {
        var d = B - A;
        var l = Length;
        if (l == 0) return p.DistanceTo(A) <= tolerance ? 0.0 : null;
        var t = Vector2D.Dot(p - A, d) / (l * l);
        if (t < -tolerance / l || t > 1 + tolerance / l) return null;
        var proj = PointAtLength(Math.Clamp(t, 0, 1) * l);
        return proj.DistanceTo(p) <= tolerance ? Math.Clamp(t, 0, 1) * l : null;
    }

    public ISegment2D Reversed() => new LineSegment2D(B, A);

    public ISegment2D Transformed(Transform2D t) => new LineSegment2D(t.Apply(A), t.Apply(B));
}

/// <summary>
/// Circular arc. SweepRad is signed: positive = counter-clockwise. |SweepRad| ≤ 2π.
/// </summary>
public sealed record CircularArcSegment2D(Point2D Center, double Radius, double StartAngleRad, double SweepRad)
    : ISegment2D
{
    public Point2D Start => PointAtAngle(StartAngleRad);
    public Point2D End => PointAtAngle(StartAngleRad + SweepRad);
    public double Length => Math.Abs(SweepRad) * Radius;

    private Point2D PointAtAngle(double a)
        => new(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));

    public Point2D PointAtLength(double lengthFromStart)
    {
        var a = StartAngleRad + Math.Sign(SweepRad) * (lengthFromStart / Radius);
        return PointAtAngle(a);
    }

    public double? LengthAtPoint(Point2D p, double tolerance)
    {
        if (Math.Abs(p.DistanceTo(Center) - Radius) > tolerance) return null;
        var angle = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
        // signed angular offset from start, in sweep direction
        var delta = NormalizeSigned(angle - StartAngleRad);
        var sweepSign = Math.Sign(SweepRad);
        var along = sweepSign >= 0 ? NormalizePositive(delta) : NormalizePositive(-delta);
        var angTol = tolerance / Math.Max(Radius, tolerance) + Tolerances.NumericEpsilon;
        if (along > Math.Abs(SweepRad) + angTol)
        {
            // maybe just before start (wrapped): treat near-2π as 0
            if (along >= 2 * Math.PI - angTol) along = 0;
            else return null;
        }
        along = Math.Min(along, Math.Abs(SweepRad));
        return along * Radius;
    }

    public ISegment2D Reversed()
        => new CircularArcSegment2D(Center, Radius, StartAngleRad + SweepRad, -SweepRad);

    public ISegment2D Transformed(Transform2D t)
        => new CircularArcSegment2D(t.Apply(Center), Radius * t.Scale, StartAngleRad + t.RotationRad, SweepRad);

    internal static double NormalizeSigned(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    internal static double NormalizePositive(double a)
    {
        while (a < 0) a += 2 * Math.PI;
        while (a >= 2 * Math.PI) a -= 2 * Math.PI;
        return a;
    }

    /// <summary>
    /// Builds the exact arc for an AutoCAD polyline vertex pair with bulge b:
    /// included angle = 4·atan(b), positive bulge = counter-clockwise.
    /// </summary>
    public static CircularArcSegment2D FromBulge(Point2D p1, Point2D p2, double bulge)
    {
        if (bulge == 0) throw new ArgumentException("bulge 0 is a line", nameof(bulge));
        var theta = 4.0 * Math.Atan(bulge);           // signed included angle
        var chord = p1.DistanceTo(p2);
        var radius = Math.Abs(chord / (2.0 * Math.Sin(theta / 2.0)));
        // centre: chord midpoint + signed apothem along the left-perpendicular of p1→p2.
        // sign(theta)·cos(theta/2) puts a CCW centre left / CW centre right, and for
        // |theta| > π the negative cosine automatically flips to the far side.
        var mid = new Point2D((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
        var d = p2 - p1;
        var len = d.Length;
        var perp = new Vector2D(-d.Y / len, d.X / len); // unit left-perpendicular
        var center = mid + perp.Scaled(Math.Sign(theta) * radius * Math.Cos(theta / 2.0));
        var startAngle = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
        return new CircularArcSegment2D(center, radius, startAngle, theta);
    }
}
