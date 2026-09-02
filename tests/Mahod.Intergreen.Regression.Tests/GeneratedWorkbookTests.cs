using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClosedXML.Excel;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>
/// r14 "new project" (David 2026-08-27, item 4): the workbook the tool creates from the drawing and a
/// short data form must be, to the engine, the same workbook the engineers filled by hand. Both examples
/// are oracles — their real drawings, their real workbooks. The crossings' template slots are derived
/// from the drawing geometry here (not typed), so this also proves the slot pass on real boundaries:
/// Example 1's T-junction (a..d in c2..c5, c1 blank) and Example 2's full-width crossings in both halves
/// of their arms (d,d,a,a,b,b,c,c) — the engineers' own placements.
/// </summary>
public class GeneratedWorkbookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ig-genwb-" + Guid.NewGuid().ToString("N"));
    public GeneratedWorkbookTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) && Directory.Exists(Path.Combine(d.FullName, "rules")))
                return d.FullName;
            d = d.Parent!;
        }
        throw new InvalidOperationException("repo root not found");
    }

    private static string Materials => Path.GetFullPath(Path.Combine(RepoRoot(), "..", "materials", "Inter-green Automation"));
    private static string Template => Path.Combine(RepoRoot(), "templates", RuntimeRoots.BlankTemplateFileName);

    /// <summary>The engineers' own workbook for each example, and the sidecar the accepted golden runs with.</summary>
    private static (string Workbook, string[] Confirmed) Reference(string example) => example == "example1"
        ? (Path.Combine(Materials, "03 Example 1", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx"), Array.Empty<string>())
        : (Path.Combine(Materials, "04 Example 2", "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx"), new[] { "E-L.b1", "S-L.b2" });

    private static Dictionary<string, List<(string Handle, PolyCurve2D Curve)>> CurvesByLayer(string example)
    {
        var path = Path.Combine(RepoRoot(), "tests", "fixtures", "geometry", example + ".iggeometry.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var byLayer = new Dictionary<string, List<(string, PolyCurve2D)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.RootElement.GetProperty("curves").EnumerateArray())
        {
            var layer = c.GetProperty("layer").GetString()!;
            if (!byLayer.TryGetValue(layer, out var list)) byLayer[layer] = list = new List<(string, PolyCurve2D)>();
            list.Add((c.GetProperty("handle").GetString()!, PolyCurve2D.FromJson(c.GetProperty("geometry").GetRawText())));
        }
        return byLayer;
    }

    private static ProjectSidecar Sidecar(string example) => ProjectSidecar.Empty with
    {
        ConfirmedEndpointReferences = new HashSet<string>(Reference(example).Confirmed, StringComparer.Ordinal),
    };

    private static PipelineOutput RunWith(string example, WorkbookModel model)
    {
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(CurvesByLayer(example), model.SignalGroups, model.PedestrianWidths,
            Sidecar(example), findings);
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        return AnalysisPipeline.Run(new PipelineInput(example.ToUpperInvariant(), example + ".dwg", "FIXTURE", pack,
            new ProjectClassification(), model.Constants, model.Variant,
            new Dictionary<string, LegacyMovementParameters>(model.MovementParameters), movements));
    }

    /// <summary>What the engineer types into the form, lifted from the real workbook; slots from the drawing.</summary>
    private static (NewProjectInputs Inputs, string[] Movements, string[] Crossings, IReadOnlyList<CrossingSlotAssignment> Slots) FormFromDrawing(string example)
    {
        var real = WorkbookReader.Read(Reference(example).Workbook);
        var findings = new List<ValidationFinding>();
        // a NEW project knows no workbook yet: names and modes come from the layers alone
        var movements = ProjectAssembly.BuildMovements(CurvesByLayer(example), new Dictionary<string, string>(),
            new Dictionary<string, double>(), ProjectSidecar.Empty, findings);
        var vehicles = movements.Where(m => m.Geometry.Mode != MovementMode.Pedestrian)
            .Select(m => (m.Id, (IReadOnlyList<PolyCurve2D>)m.Geometry.Boundaries, m.Geometry.StopLine)).ToList();
        var crossings = movements.Where(m => m.Geometry.Mode == MovementMode.Pedestrian)
            .Select(m => (m.Id, (IReadOnlyList<PolyCurve2D>)m.Geometry.Boundaries)).ToList();

        var slots = CrossingSlots.Assign(CrossingSlots.RolesFromGeometry(vehicles, crossings));
        var slotOf = slots.ToDictionary(s => s.Crossing, s => s.Slots, StringComparer.Ordinal);

        using var wb = new XLWorkbook(Reference(example).Workbook);
        var p = wb.Worksheet("Parameters");
        var approaches = new Dictionary<string, ApproachInputs>();
        foreach (var (approach, row) in new[] { ("N", 4), ("S", 7), ("E", 10), ("W", 13) })
            if (p.Cell(row, 3).TryGetValue<double>(out var kph))
                approaches[approach] = new ApproachInputs(p.Cell(row, 2).TryGetValue<double>(out var f) && f == 1, kph);

        var vehicleNames = vehicles.Select(v => v.Id).ToArray();
        var crossingNames = crossings.Select(c => c.Id).ToArray();
        var inputs = new NewProjectInputs(
            approaches,
            new Dictionary<string, double>(),
            vehicleNames.ToDictionary(m => m, m => real.SignalGroups.TryGetValue(m, out var sg) ? sg : m),
            crossingNames.ToDictionary(c => c, c => new CrossingInputs(c, real.PedestrianWidths[c]) { Slots = slotOf[c] }));
        return (inputs, vehicleNames, crossingNames, slots);
    }

    [Fact]
    public void Example_1_slots_from_the_real_drawing_match_the_engineers_placement()
    {
        var (_, _, _, slots) = FormFromDrawing("example1");
        var byName = slots.ToDictionary(s => s.Crossing, s => s.Slots);
        Assert.Equal(new[] { 2 }, byName["a"]);      // north arm, exit side — no north approach in Example 1
        Assert.Equal(new[] { 3 }, byName["b"]);      // east arm, entering side
        Assert.Equal(new[] { 4 }, byName["c"]);      // east arm, exit side
        Assert.Equal(new[] { 5 }, byName["d"]);      // south arm, entering side
        Assert.All(slots, s => Assert.Empty(s.Notes));
    }

    [Fact]
    public void Example_2_full_width_crossings_take_both_halves_of_their_arms()
    {
        // Example 2's workbook is a documented REVISION_MISMATCH against its drawing: the sheet has d,d,a,a,b,b,c,c
        // in c1..c8, the drawing letters its arms differently (a = north, b = east, c = south, d = west — read
        // off which movements meet each crossing: a ← N-L/N-R/N-T + E-R/S-T/W-L, d ← W-L/W-R/W-T + E-T/N-R/S-L).
        // The drawing is the truth for the drawing; the engineers' placement rule — a full-width crossing in
        // both halves of its arm — is what is checked. Letter 'b' also owns the right-turn pieces on the south
        // arm (W-R exits into b), so it claims c6 as well and the form says so.
        var (_, _, _, slots) = FormFromDrawing("example2");
        var byName = slots.ToDictionary(s => s.Crossing, s => s);
        Assert.Equal(new[] { 1, 2 }, byName["a"].Slots);
        Assert.Equal(new[] { 7, 8 }, byName["d"].Slots);
        Assert.Superset(new HashSet<int> { 3, 4 }, byName["b"].Slots.ToHashSet());
        Assert.Contains(5, byName["c"].Slots);
        Assert.Empty(byName["a"].Notes);
        Assert.Empty(byName["d"].Notes);
    }

    [Theory]
    [InlineData("example1")]
    [InlineData("example2")]
    public void Workbook_built_from_the_drawing_gives_the_engine_exactly_what_the_hand_filled_one_gives(string example)
    {
        var (inputs, movements, crossings, _) = FormFromDrawing(example);
        var generated = Path.Combine(_dir, $"{example}_IG_matrix_2026-09-02.xlsx");
        var result = TemplateWorkbook.Instantiate(Template, generated, inputs, movements, crossings);
        Assert.Empty(result.Warnings);

        var gate = WorkbookAcceptance.Validate(generated);
        Assert.True(gate.IsOk, gate.Detail);

        var fromReal = RunWith(example, WorkbookReader.Read(Reference(example).Workbook));
        var fromGenerated = RunWith(example, WorkbookReader.Read(generated));

        // the goldens
        if (example == "example1")
        {
            Assert.Equal(40, result.PairRowsKept);
            Assert.Equal(50, fromGenerated.Analysis.Conflicts.Count);
            Assert.All(fromGenerated.Analysis.Conflicts, c => Assert.Equal("VALID", c.Status));
            Assert.Equal(24, fromGenerated.Analysis.Matrix.Count(m => m.Status == "VALID"));
            Assert.Equal(5, fromGenerated.Analysis.Conflicts.Single(c => c.Clearing == "W-L" && c.Entering == "S-T").FinalIg);
        }
        else
        {
            Assert.Equal(168, fromGenerated.Analysis.Conflicts.Count);
            Assert.Equal(157, fromGenerated.Analysis.Conflicts.Count(c => c.Status == "VALID"));
            Assert.Equal(37, fromGenerated.Analysis.Matrix.Count(m => m.Status == "VALID"));
            Assert.Equal(7, fromGenerated.Analysis.Matrix.Count(m => m.Status.StartsWith("REVIEW")));
        }

        // and every single conflict, pair by pair
        var real = fromReal.Analysis.Conflicts.ToDictionary(c => c.Id, c => (c.Status, c.FinalIg, c.DefiningPointId));
        var gen = fromGenerated.Analysis.Conflicts.ToDictionary(c => c.Id, c => (c.Status, c.FinalIg, c.DefiningPointId));
        Assert.Equal(real.Keys.OrderBy(k => k), gen.Keys.OrderBy(k => k));
        foreach (var (id, expected) in real)
            Assert.Equal(expected, gen[id]);
    }
}
