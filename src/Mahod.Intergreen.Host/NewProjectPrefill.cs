using System.Globalization;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Geometry;

namespace Mahod.Intergreen.Host;

/// <summary>A movement as the drawing shows it — enough for the form, nothing of the engine.</summary>
public sealed record MovementSummary(string Id, MovementMode Mode, IReadOnlyList<PolyCurve2D> Boundaries, PolyCurve2D? StopLine);

/// <summary>One crossing pre-filled from the drawing: its letter, a length to confirm, and its template slots.</summary>
public sealed record CrossingPrefill(string Letter, double LengthGuessMeters, IReadOnlyList<int> Slots, IReadOnlyList<string> Notes);

/// <summary>
/// What the "new project" form shows before the engineer types anything (David 2026-08-27, item 4):
/// every approach, movement and crossing found in the drawing, each crossing already placed in its
/// template slot and measured, so the engineer only confirms and adds what a drawing cannot hold —
/// speeds, signal-group numbers, the constants. Pure data from geometry; no engineering decision.
/// </summary>
public sealed record NewProjectPrefill(
    IReadOnlyList<string> Approaches,            // present approaches, clockwise N,E,S,W
    IReadOnlyList<string> Movements,             // vehicle / bus / sherut movement ids, sorted
    IReadOnlyList<CrossingPrefill> Crossings,    // sorted by letter
    IReadOnlyList<string> Warnings)
{
    public static NewProjectPrefill FromMovements(IEnumerable<MovementSummary> movements)
    {
        var all = movements.ToList();
        var vehicles = all.Where(m => m.Mode != MovementMode.Pedestrian).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        var crossings = all.Where(m => m.Mode == MovementMode.Pedestrian).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        var warnings = new List<string>();

        var approaches = ColourModel.ClockwiseApproaches
            .Where(a => vehicles.Any(v => NewProjectDefaults.ApproachOf(v.Id) == a))
            .ToList();
        foreach (var v in vehicles.Where(v => NewProjectDefaults.ApproachOf(v.Id) is null))
            warnings.Add($"התנועה {v.Id} אינה מזרוע N/E/S/W — המהירויות שלה יוזנו ישירות בגיליון Parameters");

        var roles = CrossingSlots.RolesFromGeometry(
            vehicles.Select(v => (v.Id, v.Boundaries, v.StopLine)),
            crossings.Select(c => (c.Id, c.Boundaries)));
        var slots = CrossingSlots.Assign(roles).ToDictionary(s => s.Crossing, s => s, StringComparer.Ordinal);

        var prefilled = new List<CrossingPrefill>();
        foreach (var c in crossings)
        {
            var s = slots[c.Id];
            prefilled.Add(new CrossingPrefill(c.Id, GuessCrossingLength(c.Boundaries), s.Slots, s.Notes));
            warnings.AddRange(s.Notes);
        }
        return new NewProjectPrefill(approaches, vehicles.Select(v => v.Id).ToList(), prefilled, warnings);
    }

    /// <summary>
    /// The crossing length the sheet wants (what the pedestrian walks), read off the drawn edges:
    /// the mean of the edge lengths, to 5 cm. The two edges of a real crossing differ (Example 1
    /// crossing a: 10.36 and 6.92 m; the engineer wrote 8.35) — this is a starting value the engineer
    /// confirms, and the form says so.
    /// </summary>
    public static double GuessCrossingLength(IReadOnlyList<PolyCurve2D> edges)
    {
        var real = edges.Where(e => e.TotalLength > 0).ToList();
        if (real.Count == 0) return 0.0;
        return Math.Round(Math.Round(real.Average(e => e.TotalLength) / 0.05) * 0.05, 2);
    }

    /// <summary>The client's file convention: <c>05_intersectionName_IG_matrix_YYYY-MM-DD.xlsx</c>, from the drawing's name.</summary>
    public static string DefaultWorkbookFileName(string drawingPath, DateTime date)
        => $"{Path.GetFileNameWithoutExtension(drawingPath)}_IG_matrix_{date:yyyy-MM-dd}.xlsx";
}

/// <summary>Slot lists as the form shows and reads them: "c2, c5" — or "2 5", or empty for none.</summary>
public static class SlotText
{
    public static string Format(IReadOnlyList<int> slots) => string.Join(", ", slots.Select(s => "c" + s));

    public static bool TryParse(string? text, out IReadOnlyList<int> slots, out string? error)
    {
        var result = new SortedSet<int>();
        error = null;
        foreach (var raw in (text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim().TrimStart('c', 'C');
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                || n < 1 || n > CrossingSlots.SlotCount)
            {
                slots = Array.Empty<int>();
                error = $"'{raw}' אינו משבצת חוקית — יש לכתוב c1..c{CrossingSlots.SlotCount}";
                return false;
            }
            result.Add(n);
        }
        slots = result.ToList();
        return true;
    }
}
