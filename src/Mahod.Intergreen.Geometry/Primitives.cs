using System;

namespace Mahod.Intergreen.Geometry;

/// <summary>Geometry tolerances (v3 §49 / Addendum): separate, documented, never one magic number.</summary>
public static class Tolerances
{
    /// <summary>Pure numerical epsilon for solving intersections.</summary>
    public const double NumericEpsilon = 1e-9;

    /// <summary>Two intersection points closer than this [m] are numerical duplicates.</summary>
    public const double PointDeduplication = 1e-3;

    /// <summary>Max gap [m] between consecutive polycurve segments before the curve counts as disconnected.</summary>
    public const double SegmentConnection = 1e-6;

    /// <summary>Max distance [m] from a point to a curve for station computation.</summary>
    public const double OnCurve = 1e-6;
}

public readonly record struct Point2D(double X, double Y)
{
    public double DistanceTo(Point2D other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static Point2D operator +(Point2D p, Vector2D v) => new(p.X + v.X, p.Y + v.Y);
    public static Vector2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
}

public readonly record struct Vector2D(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vector2D Scaled(double f) => new(X * f, Y * f);
    public static double Cross(Vector2D a, Vector2D b) => a.X * b.Y - a.Y * b.X;
    public static double Dot(Vector2D a, Vector2D b) => a.X * b.X + a.Y * b.Y;
}

/// <summary>Rigid transform + uniform scale: rotate by angle, scale, then translate.</summary>
public sealed record Transform2D(double RotationRad, double Scale, double Tx, double Ty)
{
    public static Transform2D Identity { get; } = new(0, 1, 0, 0);

    public Point2D Apply(Point2D p)
    {
        var c = Math.Cos(RotationRad);
        var s = Math.Sin(RotationRad);
        return new Point2D(
            Scale * (p.X * c - p.Y * s) + Tx,
            Scale * (p.X * s + p.Y * c) + Ty);
    }
}
