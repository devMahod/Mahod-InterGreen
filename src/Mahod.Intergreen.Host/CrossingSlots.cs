using Mahod.Intergreen.Geometry;

namespace Mahod.Intergreen.Host;

/// <summary>Where along a vehicle movement a crossing is met: right after its stop line, or at its end.</summary>
public enum CrossingRole
{
    /// <summary>The crossing lies on the movement's own approach arm — met at the start of the boundary.</summary>
    Entering,
    /// <summary>The crossing lies on the arm the movement exits into — met at the end of the boundary.</summary>
    Exiting,
}

public sealed record CrossingSlotAssignment(string Crossing, IReadOnlyList<int> Slots, IReadOnlyList<string> Notes);

/// <summary>
/// The IG_matrix template's pedestrian slots are not a list — they are a map of the junction. Its
/// 'Input Distances' grid pairs each slot with fixed movements: c1 with N-R/N-T/N-L (the north arm on
/// the entering side), c2 with E-R/S-T/W-L (the north arm on the exit side), c3/c4 the east arm,
/// c5/c6 south, c7/c8 west, and c9..c12 a separate crossing on a channelised right turn (E-R, S-R,
/// W-R, N-R) that replaces the arm crossings for that turn. The letter an engineer gives a crossing is
/// arbitrary; the slot is where the crossing IS. The engineers do this by hand — Example 1 put a..d in
/// c2..c5 and left c1 blank (no north approach); Example 2 wrote each full-width crossing into both
/// halves of its arm (d,d,a,a,b,b,c,c). This type derives the same placement from the drawing: which
/// movements cross the crossing, and whether at their start or their end.
/// </summary>
public static class CrossingSlots
{
    public const int SlotCount = 12;

    private static readonly string[] Arms = { "N", "E", "S", "W" };

    /// <summary>Slot of the crossing met by movements leaving <paramref name="arm"/>'s stop line (c1/c3/c5/c7).</summary>
    public static int ApproachSlot(string arm) => arm switch
    {
        "N" => 1, "E" => 3, "S" => 5, "W" => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "arm must be N, E, S or W"),
    };

    /// <summary>Slot of the crossing met by movements exiting into <paramref name="arm"/> (c2/c4/c6/c8).</summary>
    public static int ExitSlot(string arm) => arm switch
    {
        "N" => 2, "E" => 4, "S" => 6, "W" => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "arm must be N, E, S or W"),
    };

    /// <summary>Slot of a separate crossing on the channelised right turn from <paramref name="approach"/> (c9..c12).</summary>
    public static int RightTurnSlot(string approach) => approach switch
    {
        "E" => 9, "S" => 10, "W" => 11, "N" => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(approach), approach, "approach must be N, E, S or W"),
    };

    /// <summary>
    /// The arm a vehicle movement exits into, right-hand traffic: through → opposite arm, left → the
    /// next arm clockwise (N-L exits east), right → the next arm counter-clockwise (N-R exits west).
    /// Read straight off the template grid (N-R→c8, N-T→c6, N-L→c4, E-R→c2, E-L→c6, …). Null for
    /// crossings, diagonals and anything not named A-D.
    /// </summary>
    public static string? ExitArmOf(string movement)
    {
        var approach = NewProjectDefaults.ApproachOf(movement);
        if (approach is null) return null;
        var dash = movement.IndexOf('-');
        var dir = movement.Length > dash + 1 ? char.ToUpperInvariant(movement[dash + 1]) : '\0';
        var i = Array.IndexOf(Arms, approach);
        return dir switch
        {
            'T' => Arms[(i + 2) % 4],
            'L' => Arms[(i + 1) % 4],
            'R' => Arms[(i + 3) % 4],
            _ => null,
        };
    }

    /// <summary>Hebrew name of a slot for the data form ("זרוע צפון — צד הכניסה (N-R, N-T, N-L)").</summary>
    public static string Describe(int slot)
    {
        static string ArmName(string a) => a switch { "N" => "צפון", "E" => "מזרח", "S" => "דרום", _ => "מערב" };
        if (slot >= 9 && slot <= 12)
        {
            var approach = slot switch { 9 => "E", 10 => "S", 11 => "W", _ => "N" };
            return $"c{slot}: מעבר נפרד בפנייה ימינה {approach}-R";
        }
        if (slot < 1 || slot > 8) throw new ArgumentOutOfRangeException(nameof(slot));
        var arm = Arms[(slot - 1) / 2];
        var entering = slot % 2 == 1;
        var movers = entering
            ? string.Join(", ", new[] { $"{arm}-R", $"{arm}-T", $"{arm}-L" })
            : string.Join(", ", Arms.SelectMany(a => new[] { $"{a}-R", $"{a}-T", $"{a}-L" }).Where(m => ExitArmOf(m) == arm));
        return $"c{slot}: זרוע {ArmName(arm)} — {(entering ? "צד הכניסה" : "צד היציאה")} ({movers})";
    }

    /// <summary>
    /// Slots for every crossing from the movements that cross it and the role each meets it in. Rules,
    /// in the template's own terms: a crossing met only by one right turn is that turn's separate
    /// crossing (c9..c12); otherwise every approach arm met on the entering side contributes its
    /// c1/c3/c5/c7 and every exit arm met on the exiting side its c2/c4/c6/c8. A slot can hold one
    /// letter, so when two crossings claim the same slot the first by name keeps it and the other is
    /// reported for the engineer. A crossing no vehicle meets gets no slot and a note.
    /// </summary>
    public static IReadOnlyList<CrossingSlotAssignment> Assign(
        IReadOnlyDictionary<string, IReadOnlyList<(string Movement, CrossingRole Role)>> crossedBy)
    {
        var owner = new Dictionary<int, string>();
        var result = new List<CrossingSlotAssignment>();
        foreach (var (crossing, hits) in crossedBy.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var notes = new List<string>();
            var wanted = new SortedSet<int>();
            var movements = hits.Select(h => h.Movement).Distinct(StringComparer.Ordinal).ToList();

            if (movements.Count == 0)
            {
                notes.Add($"מעבר {crossing}: אף תנועת רכב לא חוצה אותו — לא הוקצתה משבצת בתבנית");
            }
            else if (movements.Count == 1 && movements[0].EndsWith("-R", StringComparison.OrdinalIgnoreCase)
                     && NewProjectDefaults.ApproachOf(movements[0]) is string rt)
            {
                wanted.Add(RightTurnSlot(rt));
            }
            else
            {
                foreach (var (movement, role) in hits)
                {
                    var arm = role == CrossingRole.Entering ? NewProjectDefaults.ApproachOf(movement) : ExitArmOf(movement);
                    if (arm is null)
                    {
                        notes.Add($"מעבר {crossing}: התנועה {movement} אינה N/E/S/W — לא נלקחה בחשבון במיקום");
                        continue;
                    }
                    wanted.Add(role == CrossingRole.Entering ? ApproachSlot(arm) : ExitSlot(arm));
                }
                if (wanted.Count > 2)
                    notes.Add($"מעבר {crossing}: נחצה מזרועות שונות ({string.Join(", ", wanted.Select(s => "c" + s))}) — בדקו את השרטוט");
            }

            var granted = new List<int>();
            foreach (var slot in wanted)
            {
                if (owner.TryGetValue(slot, out var other))
                {
                    notes.Add($"מעבר {crossing}: המשבצת c{slot} כבר תפוסה על ידי {other} — יש להשלים ידנית בגיליון Pedestrian Xing");
                    continue;
                }
                owner[slot] = crossing;
                granted.Add(slot);
            }
            result.Add(new CrossingSlotAssignment(crossing, granted, notes));
        }
        return result;
    }

    /// <summary>
    /// Which crossings each vehicle movement meets, and where. A crossing met within the half of the
    /// boundary nearer its stop line is on the movement's approach arm; beyond that, on its exit arm —
    /// including the boundary that stops inside the crossing (Directive §21A), which has crossed the
    /// near edge on the way in. Distances are taken from the stop line's station on the boundary, the
    /// same origin the engine measures from, because boundaries are not reliably drawn stop-line-first
    /// (Example 1 has an S-T boundary drawn from the north; Lin's 05293 several). Without a stop line
    /// the curve's own start is the origin.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<(string Movement, CrossingRole Role)>> RolesFromGeometry(
        IEnumerable<(string Movement, IReadOnlyList<PolyCurve2D> Boundaries, PolyCurve2D? StopLine)> vehicles,
        IEnumerable<(string Crossing, IReadOnlyList<PolyCurve2D> Edges)> crossings)
    {
        var crossingList = crossings.ToList();
        var hits = crossingList.ToDictionary(c => c.Crossing, _ => new List<(string, CrossingRole)>(), StringComparer.Ordinal);
        foreach (var (movement, boundaries, stopLine) in vehicles)
        {
            foreach (var (crossing, edges) in crossingList)
            {
                var roles = new HashSet<CrossingRole>();
                foreach (var boundary in boundaries)
                {
                    var length = boundary.TotalLength;
                    if (length <= 0) continue;
                    var origin = stopLine is null ? 0.0
                        : ReferenceStation.Resolve(boundary, stopLine, 0.5)?.Station ?? 0.0;
                    // the boundary runs from the stop line to the far end; the far end is whichever end is further
                    var span = Math.Max(origin, length - origin);
                    double? nearest = null;
                    foreach (var edge in edges)
                        foreach (var x in boundary.IntersectionsWith(edge))
                        {
                            var d = Math.Abs(x.StationA - origin);
                            nearest = nearest is double n ? Math.Min(n, d) : d;
                        }
                    if (nearest is double s)
                        roles.Add(s <= span / 2.0 ? CrossingRole.Entering : CrossingRole.Exiting);
                }
                foreach (var role in roles) hits[crossing].Add((movement, role));
            }
        }
        return hits.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(string, CrossingRole)>)kv.Value, StringComparer.Ordinal);
    }
}
