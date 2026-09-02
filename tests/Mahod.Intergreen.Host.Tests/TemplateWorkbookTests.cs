using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// r14 "new project": a workbook created from the client's blank template, fed with the inputs an
/// engineer would type, must be a workbook the engine accepts and reads exactly like one the engineer
/// filled by hand. Example 1's real workbook is the oracle: lift its inputs, instantiate the template
/// with Example 1's movements and crossings, and the reader must see the same model.
/// </summary>
public class TemplateWorkbookTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("tmpl");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string Template => Path.Combine(TestPaths.MaterialsRoot, "02 Current Templates", "05_intersectionName_IG_matrix_YYYY-MM-DD.xlsx");

    // Example 1 as the engineers drew and filled it
    private static readonly string[] Ex1Movements = { "E-R", "E-T", "S-L", "S-R", "S-T", "W-L", "W-T" };
    private static readonly string[] Ex1Crossings = { "a", "b", "c", "d" };

    /// <summary>The inputs an engineer would give for Example 1, lifted from the real workbook.</summary>
    private static NewProjectInputs Ex1Inputs()
    {
        var model = WorkbookReader.Read(TestPaths.Example1Workbook);
        using var wb = new XLWorkbook(TestPaths.Example1Workbook);
        var p = wb.Worksheet("Parameters");
        var approaches = new Dictionary<string, ApproachInputs>();
        foreach (var (approach, row) in new[] { ("N", 4), ("S", 7), ("E", 10), ("W", 13) })
        {
            var flag = p.Cell(row, 2);
            var fast = p.Cell(row, 3);
            if (fast.TryGetValue<double>(out var kph))
                approaches[approach] = new ApproachInputs(flag.TryGetValue<double>(out var f) && f == 1, kph);
        }
        var sg = model.SignalGroups.Where(kv => Ex1Movements.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var slots = CrossingSlots.Assign(Ex1CrossedBy).ToDictionary(a => a.Crossing, a => a.Slots);
        var crossings = model.PedestrianWidths.ToDictionary(kv => kv.Key,
            kv => new CrossingInputs(kv.Key, kv.Value) { Slots = slots[kv.Key] });
        return new NewProjectInputs(approaches, new Dictionary<string, double>(), sg, crossings);
    }

    /// <summary>
    /// Example 1 as drawn: a T-junction without a north approach. a spans the north arm (only exits reach
    /// it), b is the east arm on the entering side, c the east arm on the exit side, d the south arm on
    /// the entering side — which is why the engineers put a..d in c2..c5 and left c1 blank.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<(string, CrossingRole)>> Ex1CrossedBy =
        new Dictionary<string, IReadOnlyList<(string, CrossingRole)>>
        {
            ["a"] = new[] { ("E-R", CrossingRole.Exiting), ("S-T", CrossingRole.Exiting), ("W-L", CrossingRole.Exiting) },
            ["b"] = new[] { ("E-R", CrossingRole.Entering), ("E-T", CrossingRole.Entering) },
            ["c"] = new[] { ("S-R", CrossingRole.Exiting), ("W-T", CrossingRole.Exiting) },
            ["d"] = new[] { ("S-L", CrossingRole.Entering), ("S-R", CrossingRole.Entering), ("S-T", CrossingRole.Entering) },
        };

    private string Instantiate(out TemplateInstantiation result)
    {
        var out_ = Path.Combine(_dir, "05_TEST_IG_matrix_2026-08-27.xlsx");
        result = TemplateWorkbook.Instantiate(Template, out_, Ex1Inputs(), Ex1Movements, Ex1Crossings);
        return out_;
    }

    [Fact]
    public void Generated_workbook_is_accepted_by_the_same_gate_as_a_hand_filled_one()
    {
        var path = Instantiate(out _);
        var accept = WorkbookAcceptance.Validate(path);
        Assert.True(accept.IsOk, accept.Detail);
    }

    [Fact]
    public void Reader_sees_the_same_signal_groups_widths_and_parameters_as_example_1()
    {
        var path = Instantiate(out _);
        var generated = WorkbookReader.Read(path);
        var real = WorkbookReader.Read(TestPaths.Example1Workbook);

        foreach (var m in Ex1Movements)
            Assert.Equal(real.SignalGroups[m], generated.SignalGroups[m]);
        foreach (var c in Ex1Crossings)
            Assert.Equal(real.PedestrianWidths[c], generated.PedestrianWidths[c], 6);
        foreach (var m in Ex1Movements)
        {
            var r = real.MovementParameters[m];
            var g = generated.MovementParameters[m];
            Assert.Equal(r, g);
        }
        Assert.Equal(real.Constants, generated.Constants);
        Assert.Equal(real.Variant, generated.Variant);
    }

    [Fact]
    public void Only_pairs_present_in_the_drawing_survive_and_no_row_is_deleted()
    {
        var path = Instantiate(out var result);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Input Distances");
        Assert.Equal(110, ws.LastRowUsed()!.RowNumber());                 // the grid is intact

        var live = new List<(string, string)>();
        for (var r = 3; r <= 110; r++)
        {
            var cl = ws.Cell(r, 2).GetString();
            var en = ws.Cell(r, 3).GetString();
            if (cl.Length > 0 && en.Length > 0 && en != "UNUSED") live.Add((cl, en));
        }
        var universe = Ex1Movements.Concat(Ex1Crossings).ToHashSet();
        Assert.All(live, pair => Assert.True(universe.Contains(pair.Item1) && universe.Contains(pair.Item2), $"{pair} should not be live"));
        Assert.Equal(result.PairRowsKept, live.Count);
        Assert.Equal(108, result.PairRowsKept + result.PairRowsBlanked);
        Assert.Equal(40, result.PairRowsKept);                                  // exactly the engineers' 40 rows
    }

    [Fact]
    public void Live_pairs_are_exactly_the_pairs_the_engineers_kept_in_example_1()
    {
        var path = Instantiate(out _);
        static HashSet<(string, string)> Live(string file)
        {
            using var wb = new XLWorkbook(file);
            var ws = wb.Worksheet("Input Distances");
            var set = new HashSet<(string, string)>();
            for (var r = 3; r <= ws.LastRowUsed()!.RowNumber(); r++)
            {
                var cl = ws.Cell(r, 2).GetString();
                var en = ws.Cell(r, 3).GetString();
                if (cl.Length > 0 && en.Length > 0 && en != "UNUSED" && cl != "UNUSED") set.Add((cl, en));
            }
            return set;
        }
        var expected = Live(TestPaths.Example1Workbook);
        var actual = Live(path);
        Assert.Empty(expected.Except(actual));
        Assert.Empty(actual.Except(expected));
    }

    [Fact]
    public void Crossings_sit_in_the_slots_of_their_arms_not_in_letter_order()
    {
        var path = Instantiate(out _);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Pedestrian Xing");
        // engineers' own placement in Example 1: c1 blank, c2..c5 = a..d
        Assert.Equal("", ws.Cell(3, 2).GetString());
        Assert.Equal(new[] { "a", "b", "c", "d" }, new[] { 4, 5, 6, 7 }.Select(r => ws.Cell(r, 2).GetString()));
        Assert.Equal(8.35, ws.Cell(4, 3).GetDouble(), 6);
        for (var r = 8; r <= 14; r++) Assert.Equal("", ws.Cell(r, 2).GetString());
    }

    [Fact]
    public void Derived_parameter_cells_carry_the_values_excel_will_compute()
    {
        var path = Instantiate(out _);
        using var wb = new XLWorkbook(path);
        var p = wb.Worksheet("Parameters");
        // E approach (through-row 10): urban, 50 → turns 50/25, through slow 25, flag copied
        Assert.Equal(0, p.Cell(9, 2).GetDouble());
        Assert.Equal(50, p.Cell(9, 3).GetDouble());
        Assert.Equal(25, p.Cell(9, 4).GetDouble());
        Assert.Equal(25, p.Cell(10, 4).GetDouble());
        Assert.Equal(0, p.Cell(11, 6).GetDouble());
        // N approach has no inputs in Example 1 → the template's own state stays, formulas untouched
        Assert.True(p.Cell(3, 3).HasFormula);
    }

    [Fact]
    public void Not_a_single_formula_is_changed()
    {
        var path = Instantiate(out _);
        static Dictionary<string, string> Formulas(string file)
        {
            using var doc = SpreadsheetDocument.Open(file, false);
            var wb = doc.WorkbookPart!;
            var map = new Dictionary<string, string>();
            foreach (var sheet in wb.Workbook.Sheets!.Elements<Sheet>())
            {
                var ws = ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet;
                foreach (var cell in ws.Descendants<Cell>().Where(c => c.CellFormula is not null))
                    map[sheet.Name + "!" + cell.CellReference] = cell.CellFormula!.Text;
            }
            return map;
        }
        var before = Formulas(Template);
        var after = Formulas(path);
        Assert.Equal(before.Count, after.Count);
        foreach (var (k, v) in before) Assert.Equal(v, after[k]);
    }

    [Fact]
    public void Pedestrian_pair_cells_report_the_letters_that_were_written_not_the_templates_stale_cache()
    {
        var path = Instantiate(out _);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Input Distances");
        var seen = new HashSet<string>();
        for (var r = 3; r <= 110; r++)
        {
            foreach (var col in new[] { 2, 3 })
            {
                var cell = ws.Cell(r, col);
                if (!cell.HasFormula) continue;
                var v = cell.GetString();
                if (v.Length == 1) seen.Add(v);
                Assert.True(v.Length == 0 || v == "UNUSED" || Ex1Crossings.Contains(v), $"row {r} col {col} reads '{v}'");
            }
        }
        Assert.Equal(Ex1Crossings.OrderBy(x => x), seen.OrderBy(x => x));
    }

    [Fact]
    public void Workbook_is_marked_for_full_recalculation_when_excel_opens_it()
    {
        var path = Instantiate(out _);
        using var doc = SpreadsheetDocument.Open(path, false);
        Assert.True(doc.WorkbookPart!.Workbook.CalculationProperties?.FullCalculationOnLoad?.Value == true);
    }

    [Fact]
    public void The_template_itself_is_never_the_output()
        => Assert.Throws<InvalidOperationException>(() =>
            TemplateWorkbook.Instantiate(Template, Template, Ex1Inputs(), Ex1Movements, Ex1Crossings));
}
