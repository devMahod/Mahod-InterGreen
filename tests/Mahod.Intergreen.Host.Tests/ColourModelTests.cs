using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// The recovered Appendix B (docs/COLOUR_MODEL.md). Every value here was read out of the client's own
/// reference drawings (layer ACI) and workbooks (Signal group key fills); the tests pin the lookup to
/// those files so nobody "improves" a colour by hand.
/// </summary>
public class ColourModelTests
{
    [Theory]
    // approach N — yellow family: left light, through normal, right dark
    [InlineData("N-L", 2, "FFFF00")]
    [InlineData("N-T", 52, "CCCC00")]
    [InlineData("N-R", 42, "808000")]
    // approach E — red family
    [InlineData("E-L", 220, "FF33CC")]
    [InlineData("E-T", 11, "FF9999")]
    [InlineData("E-R", 10, "FF0000")]
    // approach S — blue family
    [InlineData("S-L", 4, "00FFFF")]
    [InlineData("S-T", 150, "0000FF")]
    [InlineData("S-R", 176, "002060")]
    // approach W — green family
    [InlineData("W-L", 3, "00FF00")]
    [InlineData("W-T", 102, "33CC33")]
    [InlineData("W-R", 88, "007635")]
    public void Vehicle_movements_carry_the_recovered_layer_and_workbook_colours(string movement, int aci, string rgb)
    {
        var c = ColourModel.For(movement);
        Assert.NotNull(c);
        Assert.Equal((short)aci, c!.Aci);
        Assert.Equal(rgb, c.ExcelRgb);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("h")]
    public void Pedestrian_crossings_are_white(string crossing)
        => Assert.Equal(ColourModel.WhiteAci, ColourModel.For(crossing)!.Aci);

    [Theory]
    [InlineData("E-L-sherut", "FFC000")]
    [InlineData("N-T-sherut", "9999FF")]
    [InlineData("N-T-bus", "FFFFCC")]
    [InlineData("S-T-bus", "FF3300")]
    public void Bus_and_sherut_rows_keep_their_template_fills(string movement, string rgb)
        => Assert.Equal(rgb, ColourModel.For(movement)!.ExcelRgb);

    [Theory]
    [InlineData("NE-L")]
    [InlineData("SW-T")]
    public void Diagonal_approaches_are_unspecified_and_return_null_rather_than_a_guess(string movement)
        => Assert.Null(ColourModel.For(movement));

    [Theory]
    [InlineData("intergreen_S-L", 4)]
    [InlineData("intergreen_W-R", 88)]
    [InlineData("intergreen_a", 7)]
    [InlineData("intergreen_stopline", 7)]
    [InlineData("intergreen_NE-L", 7)]     // unspecified → white, visibly neutral, never invented
    [InlineData("SomeOtherLayer", 7)]
    public void Layer_names_map_to_the_layer_colour_index(string layer, int aci)
        => Assert.Equal((short)aci, ColourModel.LayerAci(layer));

    // Note: David describes the shades as right = dark, straight = normal, left = light. That is his
    // gloss, not a luminance rule the recovered values obey (E-L is pink FF33CC, darker than E-T's
    // FF9999; S-R 002060 vs S-T 0000FF nearly tie). The files are authoritative; the wording is not
    // asserted.

    [Fact]
    public void Clockwise_order_is_north_east_south_west()
        => Assert.Equal(new[] { "N", "E", "S", "W" }, ColourModel.ClockwiseApproaches);
}
