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
        // pass 1 — what each crossing claims
        var claims = new List<(string Crossing, SortedSet<int> Wanted, List<string> Notes)>();
        foreach (var (crossing, hits) in crossedBy.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var notes = new List<string>();
            var wanted = new SortedSet<int>();
            var movements = hits.Select(h => h.Movement).Distinct(StringComparer.Ordinal).ToList();

            if (movements.Count == 0)
            {
                notes.Add($"מעבר {crossing}: אף תנועת רכב לא חוצה אותו — לא הוקצתה משבצת בתבנית");
            }
            else
            {
                // A crossing met by a single right turn may be that turn's own crossing on a channelised
                // lane (c9..c12) — or simply an arm crossing on an approach that has only a right turn.
                // Geometry cannot tell the two apart, so the arm slot is proposed and the engineer is told
                // about the alternative; nothing is inferred silently.
                if (movements.Count == 1 && movements[0].EndsWith("-R", StringComparison.OrdinalIgnoreCase)
                    && NewProjectDefaults.ApproachOf(movements[0]) is string rt)
                    notes.Add($"מעבר {crossing}: נחצה רק על ידי {movements[0]} — אם זה מעבר נפרד בנתיב פנייה ימינה מתועל, בחרו c{RightTurnSlot(rt)}");
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
                    notes.Add($"מעבר {crossing}: נחצה מזרועות שונות ({string.Join(", ", wanted.Select(s => "c" + s))}) — ייתכן שאותה אות משמשת ליותר ממעבר אחד; בדקו את השרטוט");
            }
            claims.Add((crossing, wanted, notes));
        }

        // pass 2 — a slot holds one letter. A contested slot goes to the crossing with the more specific claim
        // (fewest slots wanted): a clean two-half crossing beats a letter that sprawls over several arms
        // (Example 2's 'b'). Ties go to the first letter. The loser is told, never silently dropped.
        var owner = new Dictionary<int, string>();
        foreach (var slot in claims.SelectMany(c => c.Wanted).Distinct().OrderBy(s => s))
        {
            var winner = claims.Where(c => c.Wanted.Contains(slot))
                               .OrderBy(c => c.Wanted.Count)
                               .ThenBy(c => c.Crossing, StringComparer.Ordinal)
                               .First();
            owner[slot] = winner.Crossing;
        }
        var result = new List<CrossingSlotAssignment>();
        foreach (var (crossing, wanted, notes) in claims)
        {
            var granted = new List<int>();
            foreach (var slot in wanted)
            {
                if (owner[slot] != crossing)
                {
                    notes.Add($"מעבר {crossing}: המשבצת c{slot} שייכת למעבר {owner[slot]} — יש להשלים ידנית בגיליון Pedestrian Xing");
                    continue;
                }
                granted.Add(slot);
            }
            result.Add(new CrossingSlotAssignment(crossing, granted, notes));
        }
        return result;
    }

    /// <summary>
    /// A crossing on a movement's own approach arm sits just behind its stop line — the stop line is set
    /// back a metre or a few from the crossing, never more than this. Anything the boundary meets
    /// further from its stop line is on the arm it exits into.
    /// </summary>
    public const double ApproachCrossingMaxSetbackMeters = 5.0;

    /// <summary>
    /// Which crossings each vehicle movement meets, and where. A movement meets a crossing when one of its
    /// boundaries intersects an edge of it. The role comes from the junction's topology, not from a
    /// distance along a polyline:
    /// <list type="bullet">
    /// <item>Two or more movements of the same approach meet the crossing → it is that approach's own
    /// crossing, just behind their common stop line (<see cref="CrossingRole.Entering"/>). Two movements
    /// of one approach never exit into the same arm, so nothing else can explain it.</item>
    /// <item>One movement of an approach meets it while the approach has others that do not → it is on
    /// the arm that movement exits into (<see cref="CrossingRole.Exiting"/>). Its siblings would have met
    /// their own approach crossing too.</item>
    /// <item>The approach has that single movement only (a T-junction arm with one turn) → decided by
    /// whether the movement's stop line lies within <see cref="ApproachCrossingMaxSetbackMeters"/> of the
    /// crossing and the crossing is met within the first <see cref="ApproachCrossingMaxStationMeters"/>
    /// from the stop line; without a stop line, by the half of the curve nearer its first vertex.</item>
    /// </list>
    /// This is what the half-length and stop-line-setback heuristics got wrong on the real drawings: a
    /// short right turn reaches its exit-arm crossing within the first half of its length and within a few
    /// metres of its own stop line (Example 2 E-R × a, W-R × c), and boundaries are not reliably drawn
    /// stop-line-first (Example 1 S-T, Lin's 05293, Example 2 W-R).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<(string Movement, CrossingRole Role)>> RolesFromGeometry(
        IEnumerable<(string Movement, IReadOnlyList<PolyCurve2D> Boundaries, PolyCurve2D? StopLine)> vehicles,
        IEnumerable<(string Crossing, IReadOnlyList<PolyCurve2D> Edges)> crossings)
    {
        var vehicleList = vehicles.ToList();
        var crossingList = crossings.ToList();
        var byApproach = vehicleList.GroupBy(v => NewProjectDefaults.ApproachOf(v.Movement) ?? "")
                                    .ToDictionary(g => g.Key, g => g.Select(v => v.Movement).ToList(), StringComparer.Ordinal);
        var hits = crossingList.ToDictionary(c => c.Crossing, _ => new List<(string, CrossingRole)>(), StringComparer.Ordinal);

        foreach (var (crossing, edges) in crossingList)
        {
            // which movements meet this crossing, and the geometry each one would fall back on
            var met = new List<(string Movement, string Approach, PolyCurve2D? StopLine, double StationFromStopLine, double Fraction)>();
            foreach (var (movement, boundaries, stopLine) in vehicleList)
            {
                double? station = null, fraction = null;
                foreach (var boundary in boundaries)
                {
                    if (boundary.TotalLength <= 0) continue;
                    var origin = stopLine is null ? 0.0 : ReferenceStation.Resolve(boundary, stopLine, 0.5)?.Station ?? 0.0;
                    foreach (var edge in edges)
                        foreach (var x in boundary.IntersectionsWith(edge))
                        {
                            var s = Math.Abs(x.StationA - origin);
                            var f = x.StationA / boundary.TotalLength;
                            station = station is double a ? Math.Min(a, s) : s;
                            fraction = fraction is double b ? Math.Min(b, f) : f;
                        }
                }
                if (station is null) continue;
                met.Add((movement, NewProjectDefaults.ApproachOf(movement) ?? "", stopLine, station.Value, fraction!.Value));
            }

            foreach (var group in met.GroupBy(m => m.Approach, StringComparer.Ordinal))
            {
                var approachHas = group.Key.Length > 0 && byApproach.TryGetValue(group.Key, out var all) ? all.Count : 1;
                var metCount = group.Count();
                foreach (var m in group)
                {
                    CrossingRole role;
                    if (m.Approach.Length == 0)
                        role = m.Fraction <= 0.5 ? CrossingRole.Entering : CrossingRole.Exiting;            // diagonals: best effort
                    else if (metCount >= 2)
                        role = CrossingRole.Entering;
                    else if (approachHas >= 2)
                        role = CrossingRole.Exiting;
                    else if (m.StopLine is not null)
                        role = Distance(m.StopLine, edges) <= ApproachCrossingMaxSetbackMeters
                               && m.StationFromStopLine <= ApproachCrossingMaxStationMeters
                            ? CrossingRole.Entering : CrossingRole.Exiting;
                    else
                        role = m.Fraction <= 0.5 ? CrossingRole.Entering : CrossingRole.Exiting;
                    hits[crossing].Add((m.Movement, role));
                }
            }
        }
        return hits.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(string, CrossingRole)>)kv.Value, StringComparer.Ordinal);
    }

    /// <summary>A movement meets its own approach crossing within this distance of its stop line (setback plus crossing depth).</summary>
    public const double ApproachCrossingMaxStationMeters = 8.0;

    /// <summary>Smallest distance between a stop line and any edge of a crossing (sampled at ends and midpoints — enough for a setback test).</summary>
    private static double Distance(PolyCurve2D stopLine, IReadOnlyList<PolyCurve2D> edges)
    {
        static IEnumerable<Point2D> Samples(PolyCurve2D c)
        {
            yield return c.Start;
            yield return c.End;
            yield return c.PointAtStation(c.TotalLength / 2.0);
        }
        var best = double.PositiveInfinity;
        foreach (var edge in edges)
        {
            if (edge.IntersectionsWith(stopLine).Count > 0) return 0.0;
            foreach (var p in Samples(stopLine)) best = Math.Min(best, edge.NearestStation(p).Distance);
            foreach (var p in Samples(edge)) best = Math.Min(best, stopLine.NearestStation(p).Distance);
        }
        return best;
    }
}
