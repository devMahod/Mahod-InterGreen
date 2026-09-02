using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// The "new project" form is pre-filled from the drawing alone — approaches, movements, crossings with
/// a length to confirm and a proposed template slot. Nothing here decides engineering; it only has to
/// show the engineer the junction as drawn, correctly, before she types a single number.
/// </summary>
public class NewProjectPrefillTests
{
    private static PolyCurve2D Line(double x1, double y1, double x2, double y2)
        => PolyCurve2D.FromVertices(new[] { (new Point2D(x1, y1), 0.0), (new Point2D(x2, y2), 0.0) });

    /// <summary>A south approach going north: stop line at y=0, two boundaries to y=40; a crossing right after the stop line.</summary>
    private static List<MovementSummary> SimpleJunction()
    {
        var stop = Line(-6, 0, 6, 0);
        return new List<MovementSummary>
        {
            new("S-T", MovementMode.Vehicle, new[] { Line(-1.5, 0, -1.5, 40), Line(1.5, 0, 1.5, 40) }, stop),
            new("S-L", MovementMode.Vehicle, new[] { Line(-3, 0, -3, 20), Line(-1.6, 0, -1.6, 20) }, stop),
            new("d", MovementMode.Pedestrian, new[] { Line(-5, 1.0, 5, 1.0), Line(-5, 3.6, 5, 3.6) }, null),
            new("a", MovementMode.Pedestrian, new[] { Line(-5, 35, 5, 35), Line(-5, 38, 5, 38) }, null),
        };
    }

    [Fact]
    public void Approaches_movements_and_crossings_come_out_sorted_and_typed()
    {
        var p = NewProjectPrefill.FromMovements(SimpleJunction());
        Assert.Equal(new[] { "S" }, p.Approaches);
        Assert.Equal(new[] { "S-L", "S-T" }, p.Movements);
        Assert.Equal(new[] { "a", "d" }, p.Crossings.Select(c => c.Letter));
    }

    [Fact]
    public void Crossing_length_is_the_mean_of_the_drawn_edges_to_five_centimetres()
    {
        var p = NewProjectPrefill.FromMovements(SimpleJunction());
        Assert.Equal(10.0, p.Crossings.Single(c => c.Letter == "d").LengthGuessMeters, 6);
        // uneven edges, as real crossings are drawn (Example 1 crossing a: 10.36 and 6.92 → engineer 8.35)
        Assert.Equal(8.65, NewProjectPrefill.GuessCrossingLength(new[] { Line(0, 0, 10.36, 0), Line(0, 1, 6.92, 1) }), 6);
        Assert.Equal(0.0, NewProjectPrefill.GuessCrossingLength(Array.Empty<PolyCurve2D>()));
    }

    [Fact]
    public void Crossings_are_placed_in_the_template_slots_of_their_arms()
    {
        var p = NewProjectPrefill.FromMovements(SimpleJunction());
        Assert.Equal(new[] { 5 }, p.Crossings.Single(c => c.Letter == "d").Slots);   // south arm, entering side
        Assert.Equal(new[] { 2 }, p.Crossings.Single(c => c.Letter == "a").Slots);   // north arm, exit side (S-T exits north)
        Assert.Empty(p.Warnings);
    }

    [Fact]
    public void A_diagonal_movement_is_listed_but_flagged_because_the_template_has_no_row_for_its_speeds()
    {
        var m = SimpleJunction();
        m.Add(new MovementSummary("NE-T", MovementMode.Vehicle, new[] { Line(10, 10, 30, 30) }, null));
        var p = NewProjectPrefill.FromMovements(m);
        Assert.Contains("NE-T", p.Movements);
        Assert.Contains(p.Warnings, w => w.Contains("NE-T"));
    }

    [Fact]
    public void A_crossing_nothing_crosses_carries_its_note_into_the_form_warnings()
    {
        var m = SimpleJunction();
        m.Add(new MovementSummary("z", MovementMode.Pedestrian, new[] { Line(100, 100, 108, 100), Line(100, 103, 108, 103) }, null));
        var p = NewProjectPrefill.FromMovements(m);
        var z = p.Crossings.Single(c => c.Letter == "z");
        Assert.Empty(z.Slots);
        Assert.Single(z.Notes);
        Assert.Contains(p.Warnings, w => w.Contains("z"));
    }

    [Fact]
    public void Default_workbook_name_follows_the_clients_convention()
        => Assert.Equal("05_PINES_IG_matrix_2026-09-02.xlsx",
            NewProjectPrefill.DefaultWorkbookFileName(@"C:\proj\05_PINES.dwg", new DateTime(2026, 9, 2)));

    [Theory]
    [InlineData("c2, c5", new[] { 2, 5 })]
    [InlineData("5 2", new[] { 2, 5 })]
    [InlineData("C12", new[] { 12 })]
    [InlineData("", new int[0])]
    public void Slot_text_round_trips(string text, int[] expected)
    {
        Assert.True(SlotText.TryParse(text, out var slots, out var error), error);
        Assert.Equal(expected, slots);
        Assert.True(SlotText.TryParse(SlotText.Format(slots), out var again, out _));
        Assert.Equal(expected, again);
    }

    [Theory]
    [InlineData("c13")]
    [InlineData("c0")]
    [InlineData("north")]
    public void Slot_text_rejects_what_the_template_has_no_slot_for(string text)
    {
        Assert.False(SlotText.TryParse(text, out _, out var error));
        Assert.Contains("c1..c12", error);
    }
}
