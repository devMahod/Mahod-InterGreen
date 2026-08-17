using System;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Geometry;
using Xunit;

namespace Mahod.Intergreen.Geometry.Tests;

/// <summary>Gates K+L — the two conflict-point strategies over hand-computable intersections.</summary>
public class LegacyEnvelopeStrategyTests
{
    private static PolyCurve2D Vertical(double x, double y0 = -20, double y1 = 30)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x, y0), new(x, y1)) });

    private static PolyCurve2D Horizontal(double y, double x0 = -20, double x1 = 30)
        => new(new[] { (ISegment2D)new LineSegment2D(new(x0, y), new(x1, y)) });

    /// <summary>Northbound movement (travelling +Y): boundaries x=0 and x=3.5, stop line y=0.</summary>
    private static MovementGeometry Northbound() => new("S-T", MovementMode.Vehicle,
        new[] { Vertical(0), Vertical(3.5) },
        Array.Empty<PolyCurve2D>(),
        Horizontal(0, -2, 6));

    /// <summary>Westbound-ish movement (travelling +X): boundaries y=10 and y=13.5, stop line x=-5.</summary>
    private static MovementGeometry Eastbound() => new("W-T", MovementMode.Vehicle,
        new[] { Horizontal(10), Horizontal(13.5) },
        Array.Empty<PolyCurve2D>(),
        Vertical(-5, 8, 16));

    [Fact]
    public void Clean_crossing_yields_4_points_with_hand_computed_distances()
    {
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Northbound(), Eastbound());
        Assert.False(result.HasBlockingError);
        Assert.Equal(4, result.Points.Count);

        // clearing distances: from stop line y=0 to y=10 / y=13.5 → 10 and 13.5
        var cds = result.Points.Select(p => Math.Round(p.ClearingDistanceMeters, 9)).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 10.0, 10.0, 13.5, 13.5 }, cds);
        // entering distances: from stop line x=-5 to x=0 / x=3.5 → 5 and 8.5
        var eds = result.Points.Select(p => Math.Round(p.EnteringDistanceMeters, 9)).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 5.0, 5.0, 8.5, 8.5 }, eds);
    }

    [Fact]
    public void Reversed_boundary_polylines_change_nothing()
    {
        var reversedNorth = Northbound() with
        {
            Boundaries = new[] { Vertical(0).Reversed(), Vertical(3.5).Reversed() },
        };
        var normal = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Northbound(), Eastbound());
        var reversed = new LegacyEnvelopeConflictStrategy().FindConflictPoints(reversedNorth, Eastbound());

        var a = normal.Points.Select(p => (Math.Round(p.ClearingDistanceMeters, 9), Math.Round(p.EnteringDistanceMeters, 9)))
            .OrderBy(x => x).ToArray();
        var b = reversed.Points.Select(p => (Math.Round(p.ClearingDistanceMeters, 9), Math.Round(p.EnteringDistanceMeters, 9)))
            .OrderBy(x => x).ToArray();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Overlapping_envelopes_without_boundary_crossing_raise_review_required()
    {
        // entering movement fully inside the clearing movement's x-range, parallel — no crossings
        var inside = new MovementGeometry("S-R", MovementMode.Vehicle,
            new[] { Vertical(1.0), Vertical(2.0) },
            Array.Empty<PolyCurve2D>(),
            Horizontal(0, -2, 6));
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(Northbound(), inside);
        Assert.Empty(result.Points);
        Assert.Contains(result.Findings, f =>
            f.Code == LegacyEnvelopeConflictStrategy.CodePossibleUnresolvedConflict
            && f.Severity == Severity.ReviewRequired);
    }

    [Fact]
    public void Wrong_boundary_count_is_a_blocking_error()
    {
        var oneBoundary = Northbound() with { Boundaries = new[] { Vertical(0) } };
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(oneBoundary, Eastbound());
        Assert.True(result.HasBlockingError);
        Assert.Contains(result.Findings, f => f.Code == LegacyEnvelopeConflictStrategy.CodeWrongBoundaryCount);
    }

    [Fact]
    public void Missing_stop_line_is_a_blocking_error()
    {
        var noStop = Northbound() with { StopLine = null };
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(noStop, Eastbound());
        Assert.True(result.HasBlockingError);
        Assert.Contains(result.Findings, f => f.Code == LegacyEnvelopeConflictStrategy.CodeNoStopLine);
    }

    [Fact]
    public void Curved_boundary_multiple_intersections_are_all_kept()
    {
        // clearing boundary 1 is an S-curve crossing entering boundary y=10 twice… build:
        // arc bulging right then left across the horizontal boundary
        var sCurve = new PolyCurve2D(new ISegment2D[]
        {
            new LineSegment2D(new(1, -20), new(1, 8)),
            CircularArcSegment2D.FromBulge(new(1, 8), new(1, 16), 0.6),
            new LineSegment2D(new(1, 16), new(1, 30)),
        });
        var clearing = Northbound() with { Boundaries = new[] { sCurve, Vertical(3.5) } };
        var result = new LegacyEnvelopeConflictStrategy().FindConflictPoints(clearing, Eastbound());
        Assert.False(result.HasBlockingError);
        // the bulged segment crosses y=10 and y=13.5 off-axis: still ≥4 points, none dropped
        Assert.True(result.Points.Count >= 4, $"got {result.Points.Count}");
    }
}

public class LaneCentrelineStrategyTests
{
    private static PolyCurve2D Vertical(double x) => new(new[] { (ISegment2D)new LineSegment2D(new(x, -20), new(x, 30)) });
    private static PolyCurve2D Horizontal(double y) => new(new[] { (ISegment2D)new LineSegment2D(new(-20, y), new(30, y)) });

    private static MovementGeometry North(int lanes) => new("S-T", MovementMode.Vehicle,
        Array.Empty<PolyCurve2D>(),
        Enumerable.Range(0, lanes).Select(i => Vertical(1.75 + 3.5 * i)).ToArray(),
        new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-2, 0), new(10, 0)) }));

    private static MovementGeometry East(int lanes) => new("W-T", MovementMode.Vehicle,
        Array.Empty<PolyCurve2D>(),
        Enumerable.Range(0, lanes).Select(i => Horizontal(11.75 + 3.5 * i)).ToArray(),
        new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-5, 8), new(-5, 22)) }));

    [Theory]
    [InlineData(2, 2, 4)] // §5.4 example א': 2×2 lanes → 4 candidate points
    [InlineData(1, 2, 2)] // example ב'-style: fewer lanes → fewer points
    [InlineData(1, 3, 3)] // example ג': turn that may enter any of the destination lanes
    public void Lane_counts_drive_candidate_point_counts(int clearingLanes, int enteringLanes, int expected)
    {
        var result = new LaneCentrelineConflictStrategy().FindConflictPoints(North(clearingLanes), East(enteringLanes));
        Assert.False(result.HasBlockingError);
        Assert.Equal(expected, result.Points.Count);
    }

    [Fact]
    public void Centreline_distances_are_measured_from_stop_lines()
    {
        var result = new LaneCentrelineConflictStrategy().FindConflictPoints(North(1), East(1));
        var p = Assert.Single(result.Points);
        Assert.Equal(11.75, p.ClearingDistanceMeters, 9); // y=0 → y=11.75
        Assert.Equal(6.75, p.EnteringDistanceMeters, 9);  // x=-5 → x=1.75
    }

    [Fact]
    public void Missing_centrelines_block_with_2025_GEOMETRY_MISSING()
    {
        // an envelope-only movement must never produce a 2025 result (v3 §16)
        var envelopeOnly = new MovementGeometry("S-T", MovementMode.Vehicle,
            new[] { Vertical(0), Vertical(3.5) },     // envelopes present…
            Array.Empty<PolyCurve2D>(),               // …but no centrelines
            new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-2, 0), new(10, 0)) }));
        var result = new LaneCentrelineConflictStrategy().FindConflictPoints(envelopeOnly, East(2));
        Assert.True(result.HasBlockingError);
        Assert.Empty(result.Points);
        Assert.Contains(result.Findings, f => f.Code == LaneCentrelineConflictStrategy.CodeGeometryMissing);
    }
}
