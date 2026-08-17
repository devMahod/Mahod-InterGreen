using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Geometry;
using Xunit;

namespace Mahod.Intergreen.Geometry.Tests;

/// <summary>Gate J — synthetic, hand-computable fixtures (v3 §45).</summary>
public class SegmentIntersectionTests
{
    [Fact]
    public void Line_x_line_crossing()
    {
        var a = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 0), new(10, 0)) });
        var b = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(5, -5), new(5, 5)) });
        var hits = a.IntersectionsWith(b);
        var hit = Assert.Single(hits);
        Assert.Equal(new Point2D(5, 0), hit.Point);
        Assert.Equal(5.0, hit.StationA, 9);
        Assert.Equal(5.0, hit.StationB, 9);
    }

    [Fact]
    public void Line_x_line_parallel_no_intersection()
    {
        var a = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 0), new(10, 0)) });
        var b = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 1), new(10, 1)) });
        Assert.Empty(a.IntersectionsWith(b));
    }

    [Fact]
    public void Line_x_arc_two_points()
    {
        // full upper semicircle r=5 at origin, horizontal line y=3 → x=±4
        var arc = new PolyCurve2D(new[]
        {
            (ISegment2D)new CircularArcSegment2D(new(0, 0), 5, 0, Math.PI),
        });
        var line = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-10, 3), new(10, 3)) });
        var hits = line.IntersectionsWith(arc);
        Assert.Equal(2, hits.Count);
        var xs = hits.Select(h => h.Point.X).OrderBy(x => x).ToArray();
        Assert.Equal(-4.0, xs[0], 9);
        Assert.Equal(4.0, xs[1], 9);
        foreach (var h in hits)
            Assert.Equal(3.0, h.Point.Y, 9);
    }

    [Fact]
    public void Line_x_arc_tangent_single_point()
    {
        var arc = new PolyCurve2D(new[]
        {
            (ISegment2D)new CircularArcSegment2D(new(0, 0), 5, 0, Math.PI),
        });
        var tangent = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-10, 5), new(10, 5)) });
        var hits = tangent.IntersectionsWith(arc);
        var hit = Assert.Single(hits);
        Assert.Equal(0.0, hit.Point.X, 6);
        Assert.Equal(5.0, hit.Point.Y, 6);
    }

    [Fact]
    public void Arc_x_arc_two_points()
    {
        // circles r=5 centred (0,0) and (6,0) → intersections at x=3, y=±4
        var a = new PolyCurve2D(new[] { (ISegment2D)new CircularArcSegment2D(new(0, 0), 5, -Math.PI / 2, Math.PI) });   // right half
        var b = new PolyCurve2D(new[] { (ISegment2D)new CircularArcSegment2D(new(6, 0), 5, Math.PI / 2, Math.PI) });    // left half
        var hits = a.IntersectionsWith(b);
        Assert.Equal(2, hits.Count);
        foreach (var h in hits)
        {
            Assert.Equal(3.0, h.Point.X, 9);
            Assert.Equal(4.0, Math.Abs(h.Point.Y), 9);
        }
    }

    [Fact]
    public void Duplicate_numerical_points_are_deduplicated()
    {
        // two segments of curve A share vertex (5,0); curve B passes through that vertex:
        // both segment pairs report the same point — must come back once
        var a = new PolyCurve2D(new ISegment2D[]
        {
            new LineSegment2D(new(0, 0), new(5, 0)),
            new LineSegment2D(new(5, 0), new(10, 5)),
        });
        var b = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(5, -5), new(5, 5)) });
        var hits = a.IntersectionsWith(b);
        Assert.Single(hits);
    }
}

public class PolylineBulgeTests
{
    [Fact]
    public void Bulge_1_is_a_semicircle_with_exact_arc_length()
    {
        // chord (0,0)→(10,0), bulge 1 → included angle π, radius 5, length 5π.
        // Positive bulge = CCW from start to end (DXF definition): from polar angle π
        // CCW through 3π/2 — the arc passes BELOW the chord, midpoint (5,−5).
        // Verified against ezdxf.math.bulge_to_arc((0,0),(10,0),1) → center (5,0), π→0 CCW.
        var curve = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(0, 0), 1.0),
            (new(10, 0), 0.0),
        });
        Assert.Equal(5 * Math.PI, curve.TotalLength, 9);
        var mid = curve.PointAtStation(curve.TotalLength / 2);
        Assert.Equal(5.0, mid.X, 9);
        Assert.Equal(-5.0, mid.Y, 9);
    }

    [Fact]
    public void Quarter_arc_bulge_geometry_is_exact()
    {
        // bulge = tan(π/8) → 90° arc; chord (0,0)→(10,0) → r = 5√2, centre (5,5)
        var b = Math.Tan(Math.PI / 8);
        var arc = CircularArcSegment2D.FromBulge(new(0, 0), new(10, 0), b);
        Assert.Equal(5 * Math.Sqrt(2), arc.Radius, 9);
        Assert.Equal(5.0, arc.Center.X, 9);
        Assert.Equal(5.0, arc.Center.Y, 9);
        Assert.Equal(Math.PI / 2, arc.SweepRad, 9);
        Assert.Equal(0.0, arc.Start.DistanceTo(new(0, 0)), 9);
        Assert.Equal(0.0, arc.End.DistanceTo(new(10, 0)), 9);
    }

    [Fact]
    public void Negative_bulge_is_clockwise()
    {
        // negative bulge = CW from start to end: from polar angle π CW through π/2 —
        // the arc passes ABOVE the chord (mirror of the positive case).
        var arc = CircularArcSegment2D.FromBulge(new(0, 0), new(10, 0), -1.0);
        var mid = arc.PointAtLength(arc.Length / 2);
        Assert.Equal(5.0, mid.X, 9);
        Assert.Equal(5.0, mid.Y, 9);
    }

    [Fact]
    public void Mixed_line_and_bulge_polyline_length_is_hand_computed()
    {
        // 10 straight + quarter arc (r=5√2 → length 5√2·π/2)
        var curve = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(-10, 0), 0.0),
            (new(0, 0), Math.Tan(Math.PI / 8)),
            (new(10, 0), 0.0),
        });
        Assert.Equal(10 + 5 * Math.Sqrt(2) * Math.PI / 2, curve.TotalLength, 9);
    }

    [Fact]
    public void Json_round_trip_preserves_geometry_exactly()
    {
        var curve = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(-10, 0), 0.0),
            (new(0, 0), Math.Tan(Math.PI / 8)),
            (new(10, 0), 0.0),
        });
        var back = PolyCurve2D.FromJson(curve.ToJson());
        Assert.Equal(curve.TotalLength, back.TotalLength, 12);
        foreach (var s in new[] { 0.0, 3.7, 10.0, curve.TotalLength })
        {
            var p1 = curve.PointAtStation(s);
            var p2 = back.PointAtStation(s);
            Assert.True(p1.DistanceTo(p2) < 1e-12);
        }
    }
}

public class EnvelopeAndInvarianceTests
{
    // two boundary polylines per movement — clean crossing = exactly 4 points (v3 §15)
    private static PolyCurve2D V(double x) => new(new[] { (ISegment2D)new LineSegment2D(new(x, -20), new(x, 20)) });
    private static PolyCurve2D H(double y) => new(new[] { (ISegment2D)new LineSegment2D(new(-20, y), new(20, y)) });

    [Fact]
    public void Two_boundaries_x_two_boundaries_cross_cleanly_at_4_points()
    {
        var a = new[] { V(0), V(3.5) };
        var b = new[] { H(0), H(3.5) };
        var all = a.SelectMany(ba => b.SelectMany(bb => ba.IntersectionsWith(bb))).ToList();
        Assert.Equal(4, all.Count);
    }

    [Fact]
    public void Curved_boundaries_can_intersect_more_than_once_and_all_points_are_kept()
    {
        // S-shaped curve: up-arc then down-arc crossing the x-axis 3 times
        var s = new PolyCurve2D(new ISegment2D[]
        {
            new CircularArcSegment2D(new(0, 0), 5, Math.PI, -Math.PI),   // CW upper semicircle (-5,0)→(5,0)
            new CircularArcSegment2D(new(10, 0), 5, Math.PI, Math.PI),   // CCW lower semicircle (5,0)→(15,0)
        });
        var axis = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-20, 0), new(20, 0)) });
        var hits = s.IntersectionsWith(axis);
        Assert.Equal(3, hits.Count); // (-5,0), (5,0), (15,0) — never truncate
        var xs = hits.Select(h => h.Point.X).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { -5.0, 5.0, 15.0 }, xs.Select(x => Math.Round(x, 9)));
    }

    [Fact]
    public void Reversed_polyline_gives_identical_distances_from_stop_line()
    {
        // movement curve: straight then quarter turn; stop line crosses it at station 5
        var curve = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(0, -5), 0.0),
            (new(0, 5), Math.Tan(Math.PI / 8)),
            (new(10, 15), 0.0),
        });
        var stop = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-3, 0), new(3, 0)) });
        var other = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-10, 8), new(20, 8)) });

        double Cd(PolyCurve2D c)
        {
            var reference = ReferenceStation.Resolve(c, stop, 0.5)!;
            Assert.Equal(ReferenceStation.Method.ExactIntersection, reference.Method);
            var hit = c.IntersectionsWith(other).Single();
            return Math.Abs(hit.StationA - reference.Station);
        }

        var cdForward = Cd(curve);
        var cdReversed = Cd(curve.Reversed());
        Assert.Equal(cdForward, cdReversed, 9);
        Assert.True(cdForward > 0);
    }

    [Fact]
    public void Translation_and_rotation_leave_stations_invariant()
    {
        var curve = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(0, 0), Math.Tan(Math.PI / 8)),
            (new(10, 0), 0.0),
            (new(20, 10), 0.0),
        });
        var cutter = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(5, -20), new(5, 30)) });

        var t = new Transform2D(RotationRad: 0.7, Scale: 1.0, Tx: 123.4, Ty: -56.7);
        var curveT = curve.Transformed(t);
        var cutterT = cutter.Transformed(t);

        var s1 = curve.IntersectionsWith(cutter).Select(h => Math.Round(h.StationA, 9)).OrderBy(x => x).ToArray();
        var s2 = curveT.IntersectionsWith(cutterT).Select(h => Math.Round(h.StationA, 9)).OrderBy(x => x).ToArray();
        Assert.Equal(s1, s2);
        Assert.Equal(curve.TotalLength, curveT.TotalLength, 9);
    }

    [Fact]
    public void Millimetre_representation_scales_to_identical_metre_results()
    {
        var meters = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(0, 0), 1.0),
            (new(10, 0), 0.0),
        });
        var millimetres = PolyCurve2D.FromVertices(new (Point2D, double)[]
        {
            (new(0, 0), 1.0),
            (new(10000, 0), 0.0),
        });
        var scaled = millimetres.Transformed(new Transform2D(0, 0.001, 0, 0));
        Assert.Equal(meters.TotalLength, scaled.TotalLength, 9);
    }

    [Fact]
    public void No_intersection_returns_empty_not_an_error()
    {
        var a = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 0), new(10, 0)) });
        var b = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 5), new(10, 5)) });
        Assert.Empty(a.IntersectionsWith(b));
    }

    [Fact]
    public void Disconnected_polycurve_is_rejected_loudly()
    {
        var ex = Assert.Throws<ArgumentException>(() => new PolyCurve2D(new ISegment2D[]
        {
            new LineSegment2D(new(0, 0), new(5, 0)),
            new LineSegment2D(new(5.1, 0), new(10, 0)), // 0.1 m gap
        }));
        Assert.Contains("BOUNDARY_NOT_CONNECTED", ex.Message);
    }

    [Fact]
    public void Endpoint_fallback_reference_is_reported_as_fallback_not_exact()
    {
        var curve = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 0.2), new(0, 10)) });
        var stop = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-3, 0), new(3, 0)) });
        var r = ReferenceStation.Resolve(curve, stop, endpointToleranceMeters: 0.5);
        Assert.NotNull(r);
        Assert.Equal(ReferenceStation.Method.EndpointFallback, r!.Method);
        Assert.Equal(0.0, r.Station);
    }

    [Fact]
    public void No_reference_resolvable_returns_null()
    {
        var curve = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(0, 5), new(0, 10)) });
        var stop = new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-3, 0), new(3, 0)) });
        Assert.Null(ReferenceStation.Resolve(curve, stop, endpointToleranceMeters: 0.5));
    }
}
