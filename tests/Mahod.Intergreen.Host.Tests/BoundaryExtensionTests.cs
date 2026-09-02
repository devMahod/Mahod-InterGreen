using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// David's item 6b as asked: extend the line to the stop line, along its own tangent, only when that is
/// unambiguous and within the threshold measured along the tangent. Lin's two cases (4.7 cm, 6.3 cm)
/// are the motivating geometry.
/// </summary>
public class BoundaryExtensionTests
{
    private static PolyCurve2D Line(params (double X, double Y)[] pts)
        => PolyCurve2D.FromVertices(pts.Select(p => (new Point2D(p.X, p.Y), 0.0)).ToList());

    private static readonly PolyCurve2D StopLine = Line((-6, 0), (6, 0));

    [Fact]
    public void A_boundary_that_stops_short_of_its_stop_line_is_carried_on_to_it()
    {
        // drawn from y=0.047 (4.7 cm short) northwards — Lin's E-T handle 4EBA, in spirit
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, 0.047), (1.5, 40)), StopLine, 0.10);
        Assert.True(r.Applied, r.Reason);
        Assert.True(r.AtStart);
        Assert.Equal(0.047, r.LengthMeters, 6);
        Assert.Equal(new Point2D(1.5, 0), r.To);
        Assert.Single(r.Extended!.IntersectionsWith(StopLine));                    // now exact
        Assert.Equal(40.0, r.Extended.TotalLength, 6);
    }

    [Fact]
    public void A_boundary_drawn_towards_its_stop_line_is_extended_at_its_end()
    {
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, 40), (1.5, 0.063)), StopLine, 0.10);
        Assert.True(r.Applied, r.Reason);
        Assert.False(r.AtStart);
        Assert.Equal(0.063, r.LengthMeters, 6);
        Assert.Equal(1.5, r.Extended!.End.X, 9);
        Assert.Equal(0.0, r.Extended.End.Y, 9);
    }

    [Fact]
    public void The_threshold_is_measured_along_the_tangent_not_across_the_gap()
    {
        // a boundary at 30° to the stop line, its near end 5 cm above it: 10 cm along the line to reach it
        const double gap = 0.05;
        var boundary = Line((0, gap), (40 * Math.Cos(Math.PI / 6), gap + 40 * Math.Sin(Math.PI / 6)));
        var along = gap / Math.Sin(Math.PI / 6);                                   // 0.10 m
        Assert.False(BoundaryExtension.TryExtendToStopLine(boundary, StopLine, 0.09).Applied);
        var r = BoundaryExtension.TryExtendToStopLine(boundary, StopLine, 0.11);
        Assert.True(r.Applied, r.Reason);
        Assert.Equal(along, r.LengthMeters, 6);
    }

    [Fact]
    public void A_boundary_that_already_meets_its_stop_line_is_left_alone()
    {
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, -0.5), (1.5, 40)), StopLine, 0.10);
        Assert.False(r.Applied);
        Assert.Contains("already meets", r.Reason);
    }

    [Fact]
    public void A_gap_wider_than_the_threshold_is_not_extended()
    {
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, 0.16), (1.5, 40)), StopLine, 0.10);
        Assert.False(r.Applied);
        Assert.Contains("10 cm", r.Reason);
    }

    [Fact]
    public void A_tangent_that_does_not_lead_to_the_stop_line_is_not_extended()
    {
        // the near end runs parallel to the stop line 5 cm above it; the far end points away
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, 0.05), (5, 0.05), (5, 40)), StopLine, 0.10);
        Assert.False(r.Applied);
        Assert.Contains("no end", r.Reason);
    }

    [Fact]
    public void Both_ends_short_of_a_stop_line_is_ambiguous_and_goes_to_the_engineer()
    {
        // a U-shaped "stop line" with parts at y=0 and y=10; the boundary is 5 cm short of both
        var twoStops = Line((-6, 0), (6, 0), (6, 10), (-6, 10));
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, 0.05), (1.5, 9.95)), twoStops, 0.10);
        Assert.False(r.Applied);
        Assert.Contains("both ends", r.Reason);
    }

    [Fact]
    public void An_arc_leaves_along_its_tangent()
    {
        // quarter arc, centre (-5, 0.08), radius 10, from 90° (point (-5,10.08)) clockwise to 0° (point (5,0.08)):
        // travelling clockwise the end tangent points -y, straight at the stop line 8 cm below.
        var arc = new PolyCurve2D(new ISegment2D[] { new CircularArcSegment2D(new Point2D(-5, 0.08), 10, Math.PI / 2, -Math.PI / 2) });
        var r = BoundaryExtension.TryExtendToStopLine(arc, StopLine, 0.20);
        Assert.True(r.Applied, r.Reason);
        Assert.False(r.AtStart);
        Assert.Equal(0.08, r.LengthMeters, 6);
        Assert.Equal(5.0, r.To.X, 6);
        Assert.Equal(0.0, r.To.Y, 6);
    }

    [Fact]
    public void Tolerance_zero_means_off()
    {
        var r = BoundaryExtension.TryExtendToStopLine(Line((1.5, 0.047), (1.5, 40)), StopLine, 0.0);
        Assert.False(r.Applied);
        Assert.Contains("off", r.Reason);
    }
}
