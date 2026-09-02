using System.Text.RegularExpressions;

namespace Mahod.Intergreen.Host;

/// <summary>A movement's colour in the drawing (AutoCAD Color Index) and in the workbook (Excel fill).</summary>
public sealed record MovementColour(short Aci, string ExcelRgb);

/// <summary>
/// The Mahod layer / workbook colour model — "Appendix B" of הגדרת פעולה.docx, which never reached us
/// and was recovered from the client's own files on 2026-08-27 (docs/COLOUR_MODEL.md). The layer
/// colours are identical in both reference drawings and the Signal group key fills are identical in
/// both reference workbooks and the blank template, so this is the standard, not a per-project choice.
///
/// Rule as David states it: one colour family per approach, progressing clockwise around the junction
/// (N yellow → E red → S blue → W green); within an approach right = dark shade, straight = normal,
/// left = light. Pedestrian crossings and stop lines are white (ACI 7). Entities are always ByLayer.
///
/// This is a lookup, not an algorithm: the values below are the ones read out of the files. Diagonal
/// approaches (NE, SE, SW, NW) have no colour in any reference file and return null — the caller must
/// ask, never invent.
/// </summary>
public static class ColourModel
{
    public const short WhiteAci = 7;
    public const string StopLineLayer = "intergreen_stopline";

    private static readonly Regex Vehicle = new(@"^(?<approach>[NSEW]{1,2})-(?<turn>[LTRU])(?:-(?<variant>bus|sherut))?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // approach → (Left, Through, Right): recovered values, drawing ACI and workbook fill side by side
    private static readonly IReadOnlyDictionary<string, (MovementColour L, MovementColour T, MovementColour R)> Approaches =
        new Dictionary<string, (MovementColour, MovementColour, MovementColour)>(StringComparer.OrdinalIgnoreCase)
        {
            ["N"] = (new(2,   "FFFF00"), new(52,  "CCCC00"), new(42,  "808000")),
            ["E"] = (new(220, "FF33CC"), new(11,  "FF9999"), new(10,  "FF0000")),
            ["S"] = (new(4,   "00FFFF"), new(150, "0000FF"), new(176, "002060")),
            ["W"] = (new(3,   "00FF00"), new(102, "33CC33"), new(88,  "007635")),
        };

    // the four bus/sherut rows that exist in the template carry their own workbook fills; the
    // reference drawings contain no such layers, so no ACI is known for them
    private static readonly IReadOnlyDictionary<string, string> VariantExcelFills =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["E-L-sherut"] = "FFC000",
            ["N-T-sherut"] = "9999FF",
            ["N-T-bus"] = "FFFFCC",
            ["S-T-bus"] = "FF3300",
        };

    /// <summary>Colour for a movement name (e.g. "S-L", "a", "N-T-bus"); null when the model has no answer.</summary>
    public static MovementColour? For(string movement)
    {
        if (string.IsNullOrWhiteSpace(movement)) return null;
        var name = movement.Trim();
        if (name.Length == 1 && char.IsAsciiLetterLower(name[0]))
            return new MovementColour(WhiteAci, "FFFFFF");                 // pedestrian crossing
        var m = Vehicle.Match(name);
        if (!m.Success) return null;
        if (m.Groups["variant"].Success)
            return VariantExcelFills.TryGetValue(name, out var fill) ? new MovementColour(0, fill) : null;
        if (!Approaches.TryGetValue(m.Groups["approach"].Value, out var family))
            return null;                                                     // diagonal approach — unspecified
        return char.ToUpperInvariant(m.Groups["turn"].Value[0]) switch
        {
            'L' => family.L,
            'T' => family.T,
            'R' => family.R,
            'U' => family.L,                                                 // a U-turn is drawn like the left turn
            _ => null,
        };
    }

    /// <summary>Layer colour for a layer name in the intergreen convention; stop lines and unknowns fall to white.</summary>
    public static short LayerAci(string layerName)
    {
        const string prefix = "intergreen_";
        if (!layerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return WhiteAci;
        var movement = layerName[prefix.Length..];
        if (movement.Equals("stopline", StringComparison.OrdinalIgnoreCase)) return WhiteAci;
        var colour = For(movement);
        return colour is null || colour.Aci == 0 ? WhiteAci : colour.Aci;
    }

    /// <summary>The approaches in clockwise order, as the rainbow progression runs.</summary>
    public static IReadOnlyList<string> ClockwiseApproaches { get; } = new[] { "N", "E", "S", "W" };
}
