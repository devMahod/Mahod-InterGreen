using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// The template's pedestrian slots are a map of the junction (c1 north arm entering side, c2 north arm
/// exit side, … c9..c12 channelised right turns). These tests pin that map to the engineers' own
/// placements in Example 1 and Example 2, and the geometry pass that derives it from the drawing.
/// </summary>
public class CrossingSlotsTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<(string, CrossingRole)>> Map(
        params (string Crossing, (string, CrossingRole)[] Hits)[] entries)
        => entries.ToDictionary(e => e.Crossing, e => (IReadOnlyList<(string, CrossingRole)>)e.Hits);

    [Theory]
    [InlineData("N-T", "S")] [InlineData("N-L", "E")] [InlineData("N-R", "W")]
    [InlineData("E-T", "W")] [InlineData("E-L", "S")] [InlineData("E-R", "N")]
    [InlineData("S-T", "N")] [InlineData("S-L", "W")] [InlineData("S-R", "E")]
    [InlineData("W-T", "E")] [InlineData("W-L", "N")] [InlineData("W-R", "S")]
    public void Exit_arm_follows_right_hand_traffic_as_the_template_grid_does(string movement, string arm)
        => Assert.Equal(arm, CrossingSlots.ExitArmOf(movement));

    [Fact]
    public void Diagonals_and_crossings_have_no_exit_arm()
    {
        Assert.Null(CrossingSlots.ExitArmOf("a"));
        Assert.Null(CrossingSlots.ExitArmOf("NE-T"));
        Assert.Null(CrossingSlots.ExitArmOf("N-X"));
    }

    [Fact]
    public void Example_1_placement_is_c2_to_c5_with_c1_blank()
    {
        // T-junction, no north approach: a is reached only by exits into the north arm
        var result = CrossingSlots.Assign(Map(
            ("a", new[] { ("E-R", CrossingRole.Exiting), ("S-T", CrossingRole.Exiting), ("W-L", CrossingRole.Exiting) }),
            ("b", new[] { ("E-R", CrossingRole.Entering), ("E-T", CrossingRole.Entering) }),
            ("c", new[] { ("S-R", CrossingRole.Exiting), ("W-T", CrossingRole.Exiting) }),
            ("d", new[] { ("S-L", CrossingRole.Entering), ("S-R", CrossingRole.Entering), ("S-T", CrossingRole.Entering) })));
        var slots = result.ToDictionary(r => r.Crossing, r => r.Slots);
        Assert.Equal(new[] { 2 }, slots["a"]);
        Assert.Equal(new[] { 3 }, slots["b"]);
        Assert.Equal(new[] { 4 }, slots["c"]);
        Assert.Equal(new[] { 5 }, slots["d"]);
        Assert.All(result, r => Assert.Empty(r.Notes));
    }

    [Fact]
    public void Example_2_full_width_crossings_take_both_halves_of_their_arm()
    {
        // the engineers wrote d,d,a,a,b,b,c,c into c1..c8
        var result = CrossingSlots.Assign(Map(
            ("d", new[] { ("N-L", CrossingRole.Entering), ("N-R", CrossingRole.Entering), ("N-T", CrossingRole.Entering),
                          ("E-R", CrossingRole.Exiting), ("S-T", CrossingRole.Exiting), ("W-L", CrossingRole.Exiting) }),
            ("a", new[] { ("E-L", CrossingRole.Entering), ("E-R", CrossingRole.Entering), ("E-T", CrossingRole.Entering),
                          ("N-L", CrossingRole.Exiting), ("S-R", CrossingRole.Exiting), ("W-T", CrossingRole.Exiting) }),
            ("b", new[] { ("S-L", CrossingRole.Entering), ("S-R", CrossingRole.Entering), ("S-T", CrossingRole.Entering),
                          ("E-L", CrossingRole.Exiting), ("N-T", CrossingRole.Exiting), ("W-R", CrossingRole.Exiting) }),
            ("c", new[] { ("W-L", CrossingRole.Entering), ("W-R", CrossingRole.Entering), ("W-T", CrossingRole.Entering),
                          ("E-T", CrossingRole.Exiting), ("S-L", CrossingRole.Exiting), ("N-R", CrossingRole.Exiting) })));
        var slots = result.ToDictionary(r => r.Crossing, r => r.Slots);
        Assert.Equal(new[] { 1, 2 }, slots["d"]);
        Assert.Equal(new[] { 3, 4 }, slots["a"]);
        Assert.Equal(new[] { 5, 6 }, slots["b"]);
        Assert.Equal(new[] { 7, 8 }, slots["c"]);
    }

    [Fact]
    public void A_crossing_met_only_by_one_right_turn_is_that_turns_separate_crossing()
    {
        var result = CrossingSlots.Assign(Map(
            ("e", new[] { ("E-R", CrossingRole.Exiting) }),
            ("f", new[] { ("N-R", CrossingRole.Entering) })));
        Assert.Equal(new[] { 9 }, result.Single(r => r.Crossing == "e").Slots);
        Assert.Equal(new[] { 12 }, result.Single(r => r.Crossing == "f").Slots);
    }

    [Fact]
    public void Two_crossings_on_one_slot_keep_the_first_and_report_the_second()
    {
        var result = CrossingSlots.Assign(Map(
            ("a", new[] { ("N-T", CrossingRole.Entering) }),
            ("b", new[] { ("N-T", CrossingRole.Entering) })));
        Assert.Equal(new[] { 1 }, result.Single(r => r.Crossing == "a").Slots);
        var b = result.Single(r => r.Crossing == "b");
        Assert.Empty(b.Slots);
        Assert.Contains(b.Notes, n => n.Contains("c1") && n.Contains("a"));
    }

    [Fact]
    public void A_crossing_nothing_crosses_gets_no_slot_and_a_note()
    {
        var result = CrossingSlots.Assign(Map(("z", Array.Empty<(string, CrossingRole)>())));
        Assert.Empty(result.Single().Slots);
        Assert.Single(result.Single().Notes);
    }

    [Fact]
    public void Geometry_pass_reads_the_role_from_where_the_boundary_meets_the_crossing()
    {
        // a northbound movement S-T: stop line at y=0, boundary to y=40
        var boundary = PolyCurve2D.FromVertices(new[] { (new Point2D(0, 0), 0.0), (new Point2D(0, 40), 0.0) });
        // crossing right after the stop line (entering side of the south arm) and one at the far end (north arm)
        PolyCurve2D Edge(double y) => PolyCurve2D.FromVertices(new[] { (new Point2D(-5, y), 0.0), (new Point2D(5, y), 0.0) });
        var near = new[] { Edge(1.0), Edge(4.0) };
        var far = new[] { Edge(35.0), Edge(38.0) };
        // a boundary that stops INSIDE the far crossing (Directive §21A) still counts as exiting
        var shortBoundary = PolyCurve2D.FromVertices(new[] { (new Point2D(3, 0), 0.0), (new Point2D(3, 36.5), 0.0) });

        var roles = CrossingSlots.RolesFromGeometry(
            new[] { ("S-T", (IReadOnlyList<PolyCurve2D>)new[] { boundary, shortBoundary }, (PolyCurve2D?)null) },
            new[] { ("d", (IReadOnlyList<PolyCurve2D>)near), ("a", (IReadOnlyList<PolyCurve2D>)far) });

        Assert.Equal(new[] { ("S-T", CrossingRole.Entering) }, roles["d"]);
        Assert.Equal(new[] { ("S-T", CrossingRole.Exiting) }, roles["a"]);

        var slots = CrossingSlots.Assign(roles).ToDictionary(r => r.Crossing, r => r.Slots);
        Assert.Equal(new[] { 5 }, slots["d"]);      // south arm, entering side
        Assert.Equal(new[] { 2 }, slots["a"]);      // north arm, exit side
    }

    [Fact]
    public void A_boundary_drawn_away_from_its_stop_line_is_read_from_the_stop_line_not_from_its_first_vertex()
    {
        // same S-T, but drawn from the north end back to the stop line (Example 1 has one such boundary)
        var reversed = PolyCurve2D.FromVertices(new[] { (new Point2D(0, 40), 0.0), (new Point2D(0, 0), 0.0) });
        var stopLine = PolyCurve2D.FromVertices(new[] { (new Point2D(-6, 0), 0.0), (new Point2D(6, 0), 0.0) });
        PolyCurve2D Edge(double y) => PolyCurve2D.FromVertices(new[] { (new Point2D(-5, y), 0.0), (new Point2D(5, y), 0.0) });
        var near = new[] { Edge(1.0), Edge(4.0) };
        var far = new[] { Edge(35.0), Edge(38.0) };

        var withStopLine = CrossingSlots.RolesFromGeometry(
            new[] { ("S-T", (IReadOnlyList<PolyCurve2D>)new[] { reversed }, (PolyCurve2D?)stopLine) },
            new[] { ("d", (IReadOnlyList<PolyCurve2D>)near), ("a", (IReadOnlyList<PolyCurve2D>)far) });
        Assert.Equal(new[] { ("S-T", CrossingRole.Entering) }, withStopLine["d"]);
        Assert.Equal(new[] { ("S-T", CrossingRole.Exiting) }, withStopLine["a"]);

        // without the stop line the first vertex is all there is — and it is the wrong end here
        var withoutStopLine = CrossingSlots.RolesFromGeometry(
            new[] { ("S-T", (IReadOnlyList<PolyCurve2D>)new[] { reversed }, (PolyCurve2D?)null) },
            new[] { ("d", (IReadOnlyList<PolyCurve2D>)near) });
        Assert.Equal(new[] { ("S-T", CrossingRole.Exiting) }, withoutStopLine["d"]);
    }

    [Fact]
    public void Slot_descriptions_name_the_arm_the_side_and_the_movements()
    {
        Assert.Contains("צפון", CrossingSlots.Describe(1));
        Assert.Contains("N-T", CrossingSlots.Describe(1));
        Assert.Contains("היציאה", CrossingSlots.Describe(2));
        Assert.Contains("E-R", CrossingSlots.Describe(2));       // E-R exits into the north arm
        Assert.Contains("E-R", CrossingSlots.Describe(9));
    }
}
