using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Mahod.Intergreen.Host;

/// <summary>What the instantiation did — shown to the engineer and written to the support log.</summary>
public sealed record TemplateInstantiation(
    string OutputPath,
    int PairRowsKept,
    int PairRowsBlanked,
    IReadOnlyList<string> MovementsWritten,
    IReadOnlyList<string> CrossingsWritten,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Creates a project workbook from the client's own blank IG_matrix template (David, 2026-08-27,
/// items 3–4). The template already carries the full 108-row conflict grid with every formula, and
/// its own instruction is "YOU CAN DELETE UNUSED" — but its AutoAdjusted sheet says "DO NOT DELETE ANY
/// ROWS" and mirrors Input Distances row-for-row, and the Matrix pivot reads a fixed range. So rows
/// are never deleted here: a pair that is not in the drawing has its movement and distance cells
/// cleared and goes inert through the template's own IF(SUM(...)=0,"") guards.
///
/// Only cells the engineer would type into are written: Parameters (interurban flag and fast speed
/// on each approach's through-row, vehicle length per movement, the constants block), Signal group
/// key (SG per movement) and Pedestrian Xing (letter and length per crossing). Every formula cell in
/// the template is left exactly as it is — the engine reads the result the same way it reads a
/// workbook the engineer filled by hand.
/// </summary>
public static class TemplateWorkbook
{
    private const string Parameters = "Parameters";
    private const string SignalGroupKey = "Signal group key";
    private const string PedestrianXing = "Pedestrian Xing";
    private const string InputDistances = "Input Distances";

    // Parameters layout of the shipped template (rows 3..14 = N-L,N-T,N-R,S-L,S-T,S-R,E-L,E-T,E-R,W-L,W-T,W-R)
    private static readonly IReadOnlyDictionary<string, int> ThroughRowByApproach =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["N"] = 4, ["S"] = 7, ["E"] = 10, ["W"] = 13 };

    public static TemplateInstantiation Instantiate(
        string templatePath,
        string outputPath,
        NewProjectInputs inputs,
        IReadOnlyCollection<string> movementsInDrawing,
        IReadOnlyCollection<string> crossingsInDrawing)
    {
        if (Path.GetFullPath(templatePath) == Path.GetFullPath(outputPath))
            throw new InvalidOperationException("the template itself must never be written to");
        File.Copy(templatePath, outputPath, overwrite: true);

        var warnings = new List<string>();
        var movementsWritten = new List<string>();
        var crossingsWritten = new List<string>();
        var movements = new HashSet<string>(movementsInDrawing, StringComparer.Ordinal);
        var crossings = new HashSet<string>(crossingsInDrawing, StringComparer.Ordinal);
        int kept = 0, blanked = 0;

        using var doc = SpreadsheetDocument.Open(outputPath, true);
        var wbPart = doc.WorkbookPart!;
        var shared = wbPart.SharedStringTablePart?.SharedStringTable;

        // ---- Parameters ----
        {
            var ws = Sheet(wbPart, Parameters);
            // constants block (column I)
            SetNumber(ws, 3, 9, inputs.PedestrianSpeedMps);
            SetNumber(ws, 4, 9, inputs.ReactionTimeSec);
            SetNumber(ws, 5, 9, inputs.DecelerationMps2);
            SetText(ws, wbPart, 7, 9, inputs.InbarMode ? "y" : "n");
            SetNumber(ws, 8, 9, inputs.InbarDefaultVehicleLengthMeters);
            // per approach: the through-row carries the engineer's inputs, the turns derive by formula
            foreach (var (approach, row) in ThroughRowByApproach)
            {
                if (inputs.Approaches.TryGetValue(approach, out var a))
                {
                    SetNumber(ws, row, 2, a.Interurban ? 1 : 0);
                    SetNumber(ws, row, 3, a.FastSpeedKph);
                }
                else if (movements.Any(m => NewProjectDefaults.ApproachOf(m) == approach))
                {
                    warnings.Add($"approach {approach} has movements in the drawing but no speed inputs; template left as is");
                }
            }
            // vehicle length per movement (column E, rows 3..14) — only where the engineer overrode the default
            for (var r = 3; r <= 14; r++)
            {
                var name = CellText(ws, wbPart, shared, r, 1);
                if (name.Length > 0 && inputs.VehicleLengthByMovement.TryGetValue(name, out var len))
                    SetNumber(ws, r, 5, len);
            }
            RefreshDerivedParameterCaches(ws, wbPart, shared, warnings);
        }

        // ---- Signal group key (rows 3..; A movement, B SG) ----
        {
            var ws = Sheet(wbPart, SignalGroupKey);
            foreach (var row in Rows(ws).Where(r => r.RowIndex!.Value >= 3))
            {
                var name = CellText(ws, wbPart, shared, (int)row.RowIndex!.Value, 1);
                if (name.Length == 0 || !inputs.SignalGroupByMovement.TryGetValue(name, out var sg)) continue;
                SetText(ws, wbPart, (int)row.RowIndex.Value, 2, sg);
                movementsWritten.Add(name);
            }
            foreach (var m in inputs.SignalGroupByMovement.Keys.Except(movementsWritten, StringComparer.Ordinal))
                warnings.Add($"movement {m} has a signal group but no row in 'Signal group key'; not written");
        }

        // ---- Pedestrian Xing (rows 3..14 = slots c1..c12; A slot, B letter, C length) ----
        // The slot is the template's map of the junction (CrossingSlots), never the order of the letters:
        // c1 is the north arm on the entering side whatever the engineer called that crossing. A full-width
        // crossing occupies both halves of its arm with the same letter, exactly as the engineers do by hand
        // (Example 2: d,d,a,a,b,b,c,c). One letter per slot; a second claimant is reported, not written.
        var slotLetter = new Dictionary<int, string>();                       // sheet row (3..14) → letter there
        {
            var ws = Sheet(wbPart, PedestrianXing);
            var slotOwner = new Dictionary<int, string>();
            foreach (var (name, c) in inputs.Crossings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (c.Slots.Count == 0)
                {
                    warnings.Add($"crossing {name}: no template slot (no vehicle movement crosses it); not written — complete 'Pedestrian Xing' by hand");
                    continue;
                }
                var wrote = false;
                foreach (var slot in c.Slots)
                {
                    if (slot < 1 || slot > CrossingSlots.SlotCount)
                    {
                        warnings.Add($"crossing {name}: slot c{slot} is outside the template (c1..c12); skipped");
                        continue;
                    }
                    if (slotOwner.TryGetValue(slot, out var other) && other != name)
                    {
                        warnings.Add($"crossing {name}: slot c{slot} already holds {other}; not written there — complete 'Pedestrian Xing' by hand");
                        continue;
                    }
                    slotOwner[slot] = name;
                    SetText(ws, wbPart, 2 + slot, 2, c.SignalGroup);
                    SetNumber(ws, 2 + slot, 3, c.LengthMeters);
                    slotLetter[2 + slot] = c.SignalGroup;
                    wrote = true;
                }
                if (wrote) crossingsWritten.Add(name);
            }
            for (var slot = 1; slot <= CrossingSlots.SlotCount; slot++)
            {
                if (slotOwner.ContainsKey(slot)) continue;
                ClearCell(ws, 2 + slot, 2);
                ClearCell(ws, 2 + slot, 3);
                slotLetter[2 + slot] = "";
            }
        }

        // ---- Input Distances: keep the pairs the drawing has, make the others inert ----
        {
            var ws = Sheet(wbPart, InputDistances);
            // the pedestrian pair cells are formulas into the Pedestrian Xing slots written above
            foreach (var row in Rows(ws).Where(r => r.RowIndex!.Value >= 3))
            {
                var r = (int)row.RowIndex!.Value;
                var clearing = PairName(ws, wbPart, shared, r, 2, slotLetter);
                var entering = PairName(ws, wbPart, shared, r, 3, slotLetter);
                if (clearing is null && entering is null) continue;             // beyond the grid
                bool present = clearing is not null && entering is not null
                               && (movements.Contains(clearing) || crossings.Contains(clearing))
                               && (movements.Contains(entering) || crossings.Contains(entering));
                if (present) { kept++; continue; }
                blanked++;
                // literal movement names are cleared; formula cells (pedestrian letters) are left to
                // resolve to "" on their own; the distance slots go too so the row is inert either way
                if (!IsFormula(ws, r, 2)) ClearCell(ws, r, 2);
                if (!IsFormula(ws, r, 3)) ClearCell(ws, r, 3);
                for (var c = 4; c <= 11; c++) ClearCell(ws, r, c);
            }
        }

        var calc = wbPart.Workbook.CalculationProperties ??= new CalculationProperties();
        calc.FullCalculationOnLoad = true;
        wbPart.Workbook.Save();
        return new TemplateInstantiation(outputPath, kept, blanked, movementsWritten, crossingsWritten, warnings);
    }

    /// <summary>
    /// The turn rows and the slow speed of every approach block are formulas of the through-row inputs:
    /// <c>=IF(B4="","",B4)</c>, <c>=IF(C4="","",IF(B3=1,MAX(50,C4-20),50))</c>,
    /// <c>=IF(B3="","",IF(B3=1,MIN(35,C4/2),25))</c>, <c>=IF($I$7="y",E3-$I$8,0)</c>. Excel recomputes
    /// them on load, but our reader and the engine read CACHED values from a file Excel has not opened yet
    /// — so the caches are set here to exactly what those formulas give. The formula text is never
    /// touched; a formula whose shape is not the template's is left to Excel and reported.
    /// </summary>
    private static void RefreshDerivedParameterCaches(Worksheet ws, WorkbookPart wb, SharedStringTable? shared, List<string> warnings)
    {
        static bool Num(string t, out double v) =>
            double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        var inbar = CellText(ws, wb, shared, 7, 9).Equals("y", StringComparison.OrdinalIgnoreCase);
        var hasInbarLen = Num(CellText(ws, wb, shared, 8, 9), out var inbarLen);
        foreach (var (_, t) in ThroughRowByApproach)
        {
            var hasFlag = Num(CellText(ws, wb, shared, t, 2), out var flag);
            var hasFast = Num(CellText(ws, wb, shared, t, 3), out var fast);
            var interurban = hasFlag && flag == 1;
            object? slow = !hasFlag ? "" : !interurban ? 25.0 : hasFast ? Math.Min(35.0, fast / 2.0) : null;
            foreach (var r in new[] { t - 1, t + 1 })
            {
                Cache(ws, r, 2, "IF(B", hasFlag ? flag : "", warnings);                                    // flag copied to the turns
                Cache(ws, r, 3, "MAX(50", !hasFast ? "" : interurban ? Math.Max(50.0, fast - 20.0) : 50.0, warnings); // fast for turns
                Cache(ws, r, 4, "MIN(35", slow, warnings);                                                  // slow for turns
            }
            Cache(ws, t, 4, "MIN(35", !hasFast ? "" : slow, warnings);                                      // slow for through
            foreach (var r in new[] { t - 1, t, t + 1 })
            {
                if (!inbar) Cache(ws, r, 6, "$I$7", 0.0, warnings);
                else if (hasInbarLen && Num(CellText(ws, wb, shared, r, 5), out var len)) Cache(ws, r, 6, "$I$7", len - inbarLen, warnings);
            }
        }
    }

    /// <summary>Set the cached value of a FORMULA cell whose formula contains <paramref name="anchor"/>; null = leave to Excel.</summary>
    private static void Cache(Worksheet ws, int row, int col, string anchor, object? value, List<string> warnings)
    {
        if (value is null) return;
        var cell = Find(ws, row, col);
        if (cell?.CellFormula is null) return;                                   // a literal here is the engineer's; not ours to touch
        // a shared-formula child carries no text of its own (<f t="shared" si="0"/>); its master was checked
        var sharedChild = cell.CellFormula.FormulaType?.Value == CellFormulaValues.Shared && cell.CellFormula.Text.Length == 0;
        if (!sharedChild && !cell.CellFormula.Text.Contains(anchor, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Parameters!{Ref(row, col)}: formula is not the template's ({cell.CellFormula.Text}); cached value left to Excel");
            return;
        }
        cell.InlineString = null;
        if (value is double d)
        {
            cell.DataType = null;
            cell.CellValue = new CellValue(d.ToString("R", CultureInfo.InvariantCulture));
        }
        else
        {
            cell.DataType = CellValues.String;
            cell.CellValue = new CellValue((string)value);
        }
    }

    // ---------------- helpers (OpenXml, minimal, no formula ever rewritten) ----------------

    private static Worksheet Sheet(WorkbookPart wb, string name)
    {
        var sheet = wb.Workbook.Sheets!.Elements<Sheet>().FirstOrDefault(s => s.Name == name)
            ?? throw new InvalidOperationException($"template has no '{name}' sheet");
        return ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet;
    }

    private static IEnumerable<Row> Rows(Worksheet ws) => ws.GetFirstChild<SheetData>()!.Elements<Row>();

    private static string Ref(int row, int col)
    {
        var s = "";
        for (var c = col; c > 0; c = (c - 1) / 26)
        {
            s = (char)('A' + (c - 1) % 26) + s;
            if (c <= 26) break;
        }
        return s + row.ToString(CultureInfo.InvariantCulture);
    }

    private static Cell? Find(Worksheet ws, int row, int col)
        => Rows(ws).FirstOrDefault(r => r.RowIndex!.Value == (uint)row)
              ?.Elements<Cell>().FirstOrDefault(c => c.CellReference == Ref(row, col));

    private static Cell Ensure(Worksheet ws, int row, int col)
    {
        var sheetData = ws.GetFirstChild<SheetData>()!;
        var r = sheetData.Elements<Row>().FirstOrDefault(x => x.RowIndex!.Value == (uint)row);
        if (r is null)
        {
            r = new Row { RowIndex = (uint)row };
            var after = sheetData.Elements<Row>().FirstOrDefault(x => x.RowIndex!.Value > (uint)row);
            if (after is null) sheetData.Append(r); else sheetData.InsertBefore(r, after);
        }
        var reference = Ref(row, col);
        var cell = r.Elements<Cell>().FirstOrDefault(c => c.CellReference == reference);
        if (cell is null)
        {
            cell = new Cell { CellReference = reference };
            var after = r.Elements<Cell>().FirstOrDefault(c => ColumnIndex(c.CellReference!) > col);
            if (after is null) r.Append(cell); else r.InsertBefore(cell, after);
        }
        return cell;
    }

    private static int ColumnIndex(string reference)
    {
        var n = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return n;
    }

    private static bool IsFormula(Worksheet ws, int row, int col) => Find(ws, row, col)?.CellFormula is not null;

    private static void SetNumber(Worksheet ws, int row, int col, double value)
    {
        var cell = Ensure(ws, row, col);
        if (cell.CellFormula is not null) throw new InvalidOperationException($"refusing to overwrite formula at {Ref(row, col)}");
        cell.DataType = CellValues.Number;
        cell.CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void SetText(Worksheet ws, WorkbookPart wb, int row, int col, string text)
    {
        var cell = Ensure(ws, row, col);
        if (cell.CellFormula is not null) throw new InvalidOperationException($"refusing to overwrite formula at {Ref(row, col)}");
        cell.DataType = CellValues.InlineString;
        cell.CellValue = null;
        cell.InlineString = new InlineString(new Text(text));
    }

    private static void ClearCell(Worksheet ws, int row, int col)
    {
        var cell = Find(ws, row, col);
        if (cell is null || cell.CellFormula is not null) return;             // formulas are never touched
        cell.CellValue = null;
        cell.InlineString = null;
        cell.DataType = null;
    }

    private static string CellText(Worksheet ws, WorkbookPart wb, SharedStringTable? shared, int row, int col)
    {
        var cell = Find(ws, row, col);
        if (cell is null) return "";
        if (cell.InlineString is not null) return cell.InlineString.InnerText.Trim();
        var v = cell.CellValue?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.SharedString && shared is not null && int.TryParse(v, out var i))
            return shared.Elements<SharedStringItem>().ElementAt(i).InnerText.Trim();
        return v.Trim();
    }

    /// <summary>
    /// The movement or crossing a pair cell refers to. Literal names are read directly. The template's
    /// pedestrian cells are formulas of two shapes — <c>='Pedestrian Xing'!B7</c> and
    /// <c>=IF(ISBLANK('Pedestrian Xing'!B13),'Pedestrian Xing'!B9,"UNUSED")</c> — and are evaluated
    /// here against the slots just written, exactly as Excel will evaluate them on load. The cell's
    /// cached value is refreshed to that result, because our reader and writer read caches and Excel has
    /// not run yet. The formula text itself is never changed.
    /// </summary>
    private static string? PairName(Worksheet ws, WorkbookPart wb, SharedStringTable? shared, int row, int col,
        IReadOnlyDictionary<int, string> slotLetter)
    {
        var cell = Find(ws, row, col);
        if (cell is null) return null;
        if (cell.CellFormula is null)
        {
            var t = CellText(ws, wb, shared, row, col);
            return t.Length == 0 ? null : t;
        }
        var f = cell.CellFormula.Text;
        var refs = SlotRefs(f);
        string result;
        // OpenXml stores formula text without the leading "="
        if (f.TrimStart('=').StartsWith("IF(ISBLANK(", StringComparison.OrdinalIgnoreCase) && refs.Count >= 2)
        {
            var guard = slotLetter.TryGetValue(refs[0], out var g) ? g : "";
            result = guard.Length == 0
                ? (slotLetter.TryGetValue(refs[1], out var v) ? v : "")   // no separate right-turn crossing: the arm crossing
                : refs.Count >= 3 ? guard                                     // =IF(ISBLANK(c9),c3,c9): the right-turn crossing itself
                : "UNUSED";                                                   // =IF(ISBLANK(c9),c2,"UNUSED"): that pair no longer exists
        }
        else if (refs.Count >= 1)
            result = slotLetter.TryGetValue(refs[0], out var v) ? v : "";
        else
            return null;
        // refresh the cache: keep <f>, set <v>
        cell.DataType = CellValues.String;
        cell.CellValue = new CellValue(result);
        cell.InlineString = null;
        return result.Length == 1 && result != "UNUSED" ? result : null;
    }

    /// <summary>Row numbers of every 'Pedestrian Xing'!B&lt;n&gt; reference in a formula, in order.</summary>
    private static List<int> SlotRefs(string formula)
    {
        var list = new List<int>();
        const string marker = "'Pedestrian Xing'!B";
        var idx = 0;
        while ((idx = formula.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            idx += marker.Length;
            var digits = new string(formula[idx..].TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n)) list.Add(n);
        }
        return list;
    }
}
