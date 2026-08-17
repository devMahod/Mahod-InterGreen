using System;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Geometry;
using Xunit;

namespace Mahod.Intergreen.Geometry.Tests;

/// <summary>Directive §3 — exact envelope-region tests (AABB is only a pre-filter).</summary>
public class EnvelopeRegionTests
{
    private static PolyCurve2D Line(double x1, double y1, double x2, double y2)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x1, y1), new(x2, y2)) });

    private static MovementGeometry Veh(string id, PolyCurve2D b1, PolyCurve2D b2, PolyCurve2D stop)
        => new(id, MovementMode.Vehicle, new[] { b1, b2 }, Array.Empty<PolyCurve2D>(), stop);

    [Fact]
    public void Aabb_overlap_with_disjoint_regions_produces_no_finding_and_no_points()
    {
        // A: vertical strip x∈[0,3.5], y∈[0,30]. B: diagonal strip passing above A's top —
        // AABBs overlap, exact regions are disjoint.
        var a = Veh("A", Line(0, 0, 0, 30), Line(3.5, 0, 3.5, 30), Line(-2, 0, 6, 0));
        var b = Veh("B", Line(2, 35, 30, 7), Line(3.5, 38, 31.5, 10), Line(1, 34, 5, 39));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(a, b);
        Assert.Empty(result.Points);
        Assert.DoesNotContain(result.Findings,
            f => f.Code == LegacyEnvelopeConflictStrategy.CodePossibleUnresolvedConflict);
    }

    [Fact]
    public void True_containment_without_crossing_is_review_required_with_evidence()
    {
        var outer = Veh("OUT", Line(0, -30, 0, 30), Line(10, -30, 10, 30), Line(-2, -30, 12, -30));
        var inner = Veh("IN", Line(4, -40, 4, 40), Line(6, -40, 6, 40), Line(3, -40, 7, -40));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(outer, inner);
        Assert.Empty(result.Points);
        var finding = Assert.Single(result.Findings,
            f => f.Code == LegacyEnvelopeConflictStrategy.CodePossibleUnresolvedConflict);
        Assert.Contains("Evidence", finding.Message);
    }

    [Fact]
    public void Arc_bounded_envelope_containment_works()
    {
        // quarter-turn strip via two concentric arcs; probe point between them
        var inner = new PolyCurve2D(new[] { (ISegment2D)new CircularArcSegment2D(new(0, 0), 10, 0, Math.PI / 2) });
        var outer = new PolyCurve2D(new[] { (ISegment2D)new CircularArcSegment2D(new(0, 0), 14, 0, Math.PI / 2) });
        var region = EnvelopeRegion.Build(inner, outer);
        Assert.True(region.IsValid);
        Assert.True(region.Contains(new Point2D(0, 12)));    // mid-strip at 90°
        Assert.True(region.Contains(new Point2D(12, 0)));    // mid-strip at 0°
        Assert.False(region.Contains(new Point2D(0, 8)));    // inside the inner radius
        Assert.False(region.Contains(new Point2D(0, 15)));   // outside the outer radius
        Assert.False(region.Contains(new Point2D(-5, -5)));  // opposite quadrant
    }

    [Fact]
    public void Self_crossing_boundaries_are_invalid_and_reported_review_required()
    {
        var a = Veh("X", Line(0, 0, 10, 10), Line(0, 10, 10, 0), Line(0, -2, 0, 12)); // boundaries cross at (5,5)
        var b = Veh("Y", Line(20, 0, 20, 10), Line(23, 0, 23, 10), Line(19, 0, 24, 0));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(a, b);
        Assert.Contains(result.Findings,
            f => f.Code == LegacyEnvelopeConflictStrategy.CodeEnvelopeRegionInvalid
                 && f.Severity == Severity.ReviewRequired);
    }

    [Fact]
    public void Overlap_verdict_is_rotation_and_translation_invariant()
    {
        var t = new Transform2D(0.6, 1.0, 100, -50);
        var outer = Veh("OUT", Line(0, -30, 0, 30), Line(10, -30, 10, 30), Line(-2, -30, 12, -30));
        var inner = Veh("IN", Line(4, -40, 4, 40), Line(6, -40, 6, 40), Line(3, -40, 7, -40));
        MovementGeometry T(MovementGeometry m) => m with
        {
            Boundaries = m.Boundaries.Select(b => b.Transformed(t)).ToList(),
            StopLine = m.StopLine?.Transformed(t),
        };
        var plain = new LegacyEnvelopeConflictStrategy().FindConflictPoints(outer, inner);
        var moved = new LegacyEnvelopeConflictStrategy().FindConflictPoints(T(outer), T(inner));
        Assert.Equal(
            plain.Findings.Count(f => f.Code == LegacyEnvelopeConflictStrategy.CodePossibleUnresolvedConflict),
            moved.Findings.Count(f => f.Code == LegacyEnvelopeConflictStrategy.CodePossibleUnresolvedConflict));
        Assert.Equal(plain.Points.Count, moved.Points.Count);
    }
}

/// <summary>Directive §21A — boundary-termination candidates.</summary>
public class BoundaryTerminationTests
{
    private static PolyCurve2D Line(double x1, double y1, double x2, double y2)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x1, y1), new(x2, y2)) });

    [Fact]
    public void Boundary_ending_inside_opposing_envelope_becomes_a_candidate()
    {
        // clearing A: vertical strip x∈[0,3.5], stop at y=0, boundaries end at y=20 —
        // *inside* entering B's horizontal strip y∈[18,21.5]. No boundary of A crosses
        // B's far boundary (y=21.5) because A stops at 20.
        var a = new MovementGeometry("A", MovementMode.Vehicle,
            new[] { Line(0, -5, 0, 20), Line(3.5, -5, 3.5, 20) },
            Array.Empty<PolyCurve2D>(), Line(-2, 0, 6, 0));
        var b = new MovementGeometry("B", MovementMode.Vehicle,
            new[] { Line(-20, 18, 30, 18), Line(-20, 21.5, 30, 21.5) },
            Array.Empty<PolyCurve2D>(), Line(-15, 16, -15, 23));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(a, b);

        Assert.False(result.HasBlockingError);
        // crossings with the near boundary y=18 give 2 intersection points;
        // the terminations at y=20 add candidates with CD = 20 (full station from stop line)
        Assert.Contains(result.Points, p => p.Origin == "boundary-termination"
            && Math.Abs(p.ClearingDistanceMeters - 20.0) < 1e-9);
        // termination candidates project onto BOTH entering boundaries (near/far zone edges)
        var term = result.Points.Where(p => p.Origin == "boundary-termination").ToList();
        Assert.True(term.Count >= 2);
    }

    [Fact]
    public void Termination_candidates_are_reversal_invariant()
    {
        var a = new MovementGeometry("A", MovementMode.Vehicle,
            new[] { Line(0, -5, 0, 20), Line(3.5, -5, 3.5, 20) },
            Array.Empty<PolyCurve2D>(), Line(-2, 0, 6, 0));
        var b = new MovementGeometry("B", MovementMode.Vehicle,
            new[] { Line(-20, 18, 30, 18), Line(-20, 21.5, 30, 21.5) },
            Array.Empty<PolyCurve2D>(), Line(-15, 16, -15, 23));
        var aRev = a with { Boundaries = a.Boundaries.Select(x => x.Reversed()).ToList() };

        var s = new LegacyEnvelopeConflictStrategy();
        var p1 = s.FindConflictPoints(a, b).Points
            .Select(p => (Math.Round(p.ClearingDistanceMeters, 6), Math.Round(p.EnteringDistanceMeters, 6)))
            .OrderBy(x => x).ToArray();
        var p2 = s.FindConflictPoints(aRev, b).Points
            .Select(p => (Math.Round(p.ClearingDistanceMeters, 6), Math.Round(p.EnteringDistanceMeters, 6)))
            .OrderBy(x => x).ToArray();
        Assert.Equal(p1, p2);
    }
}

/// <summary>Directive §5–§9 — pedestrian N-edge model with authoritative project W.</summary>
public class PedestrianModelTests
{
    private static PolyCurve2D Line(double x1, double y1, double x2, double y2)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x1, y1), new(x2, y2)) });

    private static MovementGeometry Veh() => new("S-T", MovementMode.Vehicle,
        new[] { Line(0, -20, 0, 30), Line(3.5, -20, 3.5, 30) },
        Array.Empty<PolyCurve2D>(), Line(-2, 0, 6, 0));

    private static MovementGeometry Ped(int edges, double? w) => new("a", MovementMode.Pedestrian,
        Enumerable.Range(0, edges).Select(i => Line(-10, 10 + 1.5 * i, 20, 10 + 1.5 * i)).ToArray(),
        Array.Empty<PolyCurve2D>(), null)
    { PedestrianWidthMeters = w };

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void N_edge_crossings_are_supported_without_IG_GEO_001(int edges)
    {
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Ped(edges, 8.35), Veh());
        Assert.False(result.HasBlockingError);
        Assert.Equal(2 * edges, result.Points.Count); // every edge × both vehicle boundaries
        Assert.All(result.Points, p => Assert.Equal(8.35, p.ClearingDistanceMeters, 9));
    }

    [Fact]
    public void Pedestrian_clearing_uses_project_W_not_edge_average()
    {
        // edges are 30 m long — if the old averaging bug returns, CD would be 30, not 8.35
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Ped(2, 8.35), Veh());
        Assert.All(result.Points, p => Assert.Equal(8.35, p.ClearingDistanceMeters, 9));
    }

    [Fact]
    public void Missing_W_blocks_pedestrian_clearing_with_explicit_finding()
    {
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Ped(2, null), Veh());
        Assert.True(result.HasBlockingError);
        Assert.Contains(result.Findings,
            f => f.Code == LegacyEnvelopeConflictStrategy.CodePedestrianWidthMissing);
    }

    [Fact]
    public void Vehicle_clearing_vs_pedestrian_entering_has_ED_zero_and_no_W_requirement()
    {
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Veh(), Ped(4, null));
        Assert.False(result.HasBlockingError);
        Assert.All(result.Points, p => Assert.Equal(0.0, p.EnteringDistanceMeters));
    }

    [Fact]
    public void Short_edge_near_boundary_raises_coverage_gap_not_silent_extension()
    {
        // edge ends 0.4 m short of the far vehicle boundary (x=3.5)
        var ped = new MovementGeometry("b", MovementMode.Pedestrian,
            new[] { Line(-10, 10, 3.1, 10), Line(-10, 12, 20, 12) },
            Array.Empty<PolyCurve2D>(), null)
        { PedestrianWidthMeters = 7.25 };
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(ped, Veh());
        Assert.Contains(result.Findings,
            f => f.Code == LegacyEnvelopeConflictStrategy.CodePedestrianEdgeCoverageGap
                 && f.Severity == Severity.ReviewRequired);
        // and no candidate was fabricated on the missing stretch: edge1 × far boundary absent
        Assert.DoesNotContain(result.Points,
            p => p.ClearingCurveId == "b.e1" && p.EnteringCurveId == "S-T.b2");
    }

    [Fact]
    public void Vehicle_boundary_terminating_inside_crossing_adds_end_candidate()
    {
        // Directive §21A / Example-1 E-R→a: the vehicle boundary crosses the near edge (y=10)
        // but ends at y=12 — before the far edge (y=13.5). Its drawn end must become a
        // termination candidate so the far-side clearing distance is not silently lost.
        var veh = new MovementGeometry("E-R", MovementMode.Vehicle,
            new[] { Line(0, -20, 0, 12), Line(3.5, -20, 3.5, 12) },
            Array.Empty<PolyCurve2D>(), Line(-2, 0, 6, 0));
        var ped = new MovementGeometry("a", MovementMode.Pedestrian,
            new[] { Line(-10, 10, 20, 10), Line(-10, 13.5, 20, 13.5) },
            Array.Empty<PolyCurve2D>(), null);

        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(veh, ped);
        Assert.False(result.HasBlockingError);
        // near-edge intersections at station 10 + termination candidates at station 12 (boundary end)
        Assert.Contains(result.Points, p => p.Origin == "boundary-termination"
            && Math.Abs(p.ClearingDistanceMeters - 12.0) < 1e-9);
        Assert.Contains(result.Findings, f =>
            f.Code == LegacyEnvelopeConflictStrategy.CodePedestrianEdgeCoverageGap
            && f.Message.Contains("terminates inside crossing"));
        // the termination candidate governs veh→ped (max CD, ED=0)
        var maxCd = result.Points.Max(p => p.ClearingDistanceMeters);
        Assert.Equal(12.0, maxCd, 9);
    }

    [Fact]
    public void Changing_W_changes_clearing_distance_but_nothing_else()
    {
        var r1 = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Ped(2, 8.35), Veh());
        var r2 = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Ped(2, 10.0), Veh());
        Assert.Equal(r1.Points.Count, r2.Points.Count);
        Assert.All(r2.Points, p => Assert.Equal(10.0, p.ClearingDistanceMeters, 9));
        Assert.Equal(
            r1.Points.Select(p => p.EnteringDistanceMeters).OrderBy(x => x),
            r2.Points.Select(p => p.EnteringDistanceMeters).OrderBy(x => x));
    }
}

/// <summary>Directive §14 — endpoint references need explicit confirmation.</summary>
public class ReferenceConfirmationTests
{
    private static PolyCurve2D Line(double x1, double y1, double x2, double y2)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x1, y1), new(x2, y2)) });

    [Fact]
    public void Unconfirmed_endpoint_fallback_blocks_with_IG_GEO_004()
    {
        // boundaries start 0.2 m above the stop line — no exact intersection
        var a = new MovementGeometry("E-L", MovementMode.Vehicle,
            new[] { Line(0, 0.2, 0, 30), Line(3.5, 0.2, 3.5, 30) },
            Array.Empty<PolyCurve2D>(), Line(-2, 0, 6, 0));
        var b = new MovementGeometry("W-T", MovementMode.Vehicle,
            new[] { Line(-20, 10, 30, 10), Line(-20, 13.5, 30, 13.5) },
            Array.Empty<PolyCurve2D>(), Line(-15, 8, -15, 16));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(a, b);
        Assert.True(result.HasBlockingError);
        Assert.Contains(result.Findings,
            f => f.Code == LegacyEnvelopeConflictStrategy.CodeReferenceEndpointUnconfirmed);
    }

    [Fact]
    public void Confirmed_endpoint_reference_computes_with_provenance()
    {
        var a = new MovementGeometry("E-L", MovementMode.Vehicle,
            new[] { Line(0, 0.2, 0, 30), Line(3.5, 0.2, 3.5, 30) },
            Array.Empty<PolyCurve2D>(), Line(-2, 0, 6, 0))
        { ConfirmedEndpointReferences = new HashSet<string> { "E-L.b1", "E-L.b2" } };
        var b = new MovementGeometry("W-T", MovementMode.Vehicle,
            new[] { Line(-20, 10, 30, 10), Line(-20, 13.5, 30, 13.5) },
            Array.Empty<PolyCurve2D>(), Line(-15, 8, -15, 16));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(a, b);
        Assert.False(result.HasBlockingError);
        Assert.Equal(4, result.Points.Count(p => p.Origin == "boundary-intersection"));
    }
}
