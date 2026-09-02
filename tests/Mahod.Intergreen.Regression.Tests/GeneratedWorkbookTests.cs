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
/// short data form must be, to the engine, the same workbook the engineers filled by hand. Example 1 is
/// the oracle — its real drawing, its real workbook. The crossings' template slots are derived from the
/// drawing geometry here (not typed), so this also proves the slot pass on real boundaries.
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
    private static string RealWorkbook => Path.Combine(Materials, "03 Example 1", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx");
    private static string Template => Path.Combine(RepoRoot(), "templates", RuntimeRoots.BlankTemplateFileName);

    private static Dictionary<string, List<(string Handle, PolyCurve2D Curve)>> CurvesByLayer()
    {
        var path = Path.Combine(RepoRoot(), "tests", "fixtures", "geometry", "example1.iggeometry.json");
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

    private static PipelineOutput RunWith(WorkbookModel model)
    {
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(CurvesByLayer(), model.SignalGroups, model.PedestrianWidths,
            ProjectSidecar.Empty, findings);
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        return AnalysisPipeline.Run(new PipelineInput("EXAMPLE1", "example1.dwg", "FIXTURE", pack,
            new ProjectClassification(), model.Constants, model.Variant,
            new Dictionary<string, LegacyMovementParameters>(model.MovementParameters), movements));
    }

    /// <summary>What the engineer types into the form, lifted from the real workbook; slots from the drawing.</summary>
    private (NewProjectInputs Inputs, string[] Movements, string[] Crossings, IReadOnlyList<CrossingSlotAssignment> Slots) FormFromDrawing()
    {
        var real = WorkbookReader.Read(RealWorkbook);
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(CurvesByLayer(), real.SignalGroups, real.PedestrianWidths,
            ProjectSidecar.Empty, findings);
        var vehicles = movements.Where(m => m.Geometry.Mode != MovementMode.Pedestrian)
            .Select(m => (m.Id, (IReadOnlyList<PolyCurve2D>)m.Geometry.Boundaries, m.Geometry.StopLine)).ToList();
        var crossings = movements.Where(m => m.Geometry.Mode == MovementMode.Pedestrian)
            .Select(m => (m.Id, (IReadOnlyList<PolyCurve2D>)m.Geometry.Boundaries)).ToList();

        var slots = CrossingSlots.Assign(CrossingSlots.RolesFromGeometry(vehicles, crossings));
        var slotOf = slots.ToDictionary(s => s.Crossing, s => s.Slots, StringComparer.Ordinal);

        using var wb = new XLWorkbook(RealWorkbook);
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
            real.SignalGroups.Where(kv => vehicleNames.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            crossingNames.ToDictionary(c => c, c => new CrossingInputs(c, real.PedestrianWidths[c]) { Slots = slotOf[c] }));
        return (inputs, vehicleNames, crossingNames, slots);
    }

    [Fact]
    public void Slots_derived_from_the_real_drawing_match_the_engineers_placement()
    {
        var (_, _, _, slots) = FormFromDrawing();
        var byName = slots.ToDictionary(s => s.Crossing, s => s.Slots);
        Assert.Equal(new[] { 2 }, byName["a"]);      // north arm, exit side — no north approach in Example 1
        Assert.Equal(new[] { 3 }, byName["b"]);      // east arm, entering side
        Assert.Equal(new[] { 4 }, byName["c"]);      // east arm, exit side
        Assert.Equal(new[] { 5 }, byName["d"]);      // south arm, entering side
        Assert.All(slots, s => Assert.Empty(s.Notes));
    }

    [Fact]
    public void Workbook_built_from_the_drawing_gives_the_engine_exactly_what_the_hand_filled_one_gives()
    {
        var (inputs, movements, crossings, _) = FormFromDrawing();
        var generated = Path.Combine(_dir, "05_EXAMPLE1_IG_matrix_2026-09-02.xlsx");
        var result = TemplateWorkbook.Instantiate(Template, generated, inputs, movements, crossings);
        Assert.Empty(result.Warnings);
        Assert.Equal(40, result.PairRowsKept);

        var gate = WorkbookAcceptance.Validate(generated);
        Assert.True(gate.IsOk, gate.Detail);

        var fromReal = RunWith(WorkbookReader.Read(RealWorkbook));
        var fromGenerated = RunWith(WorkbookReader.Read(generated));

        // the goldens
        Assert.Equal(50, fromGenerated.Analysis.Conflicts.Count);
        Assert.All(fromGenerated.Analysis.Conflicts, c => Assert.Equal("VALID", c.Status));
        Assert.Equal(24, fromGenerated.Analysis.Matrix.Count(m => m.Status == "VALID"));
        Assert.Equal(5, fromGenerated.Analysis.Conflicts.Single(c => c.Clearing == "W-L" && c.Entering == "S-T").FinalIg);

        // and every single conflict, pair by pair
        var real = fromReal.Analysis.Conflicts.ToDictionary(c => c.Id, c => (c.Status, c.FinalIg, c.DefiningPointId));
        var gen = fromGenerated.Analysis.Conflicts.ToDictionary(c => c.Id, c => (c.Status, c.FinalIg, c.DefiningPointId));
        Assert.Equal(real.Keys.OrderBy(k => k), gen.Keys.OrderBy(k => k));
        foreach (var (id, expected) in real)
            Assert.Equal(expected, gen[id]);
    }
}
