using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Excel;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>
/// Directive §21A termination candidates, over the real extracted geometry.
///
/// The rule contributes a vehicle boundary's drawn end as a measurement extremum when the boundary
/// stops inside a crossing. It used to take the end at station TotalLength and assume that was the
/// far end, which only holds when the polyline is drawn from the stop line outwards. Lin's 05293 has
/// boundaries drawn the other way, so the "termination" landed on the movement's own stop line —
/// 34 m away from the crossing, with a distance of zero. Zero distance maximises the intergreen, so
/// that phantom always governed: b→S-L came out 6 where the engineer had 3.
///
/// The invariant below is the one that would have caught it, and it is checked against both example
/// drawings rather than against a remembered number.
/// </summary>
public class TerminationCandidateTests
{
    private static string GeometryPath(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "tests", "fixtures", "geometry", name + ".iggeometry.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("geometry fixture not found above " + AppContext.BaseDirectory, name);
    }

    private static Dictionary<string, List<(string Handle, PolyCurve2D Curve)>> CurvesByLayer(string example)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GeometryPath(example)));
        var byLayer = new Dictionary<string, List<(string, PolyCurve2D)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.RootElement.GetProperty("curves").EnumerateArray())
        {
            var layer = c.GetProperty("layer").GetString()!;
            if (!byLayer.TryGetValue(layer, out var list))
                byLayer[layer] = list = new List<(string, PolyCurve2D)>();
            list.Add((c.GetProperty("handle").GetString()!,
                      PolyCurve2D.FromJson(c.GetProperty("geometry").GetRawText())));
        }
        return byLayer;
    }

    /// <summary>The project's own workbook — signal groups and the authoritative crossing widths.</summary>
    private static string WorkbookPath(string example)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) && Directory.Exists(Path.Combine(d.FullName, "rules")))
            {
                var materials = Path.GetFullPath(Path.Combine(d.FullName, "..", "materials", "Inter-green Automation"));
                return example == "example1"
                    ? Path.Combine(materials, "03 Example 1", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx")
                    : Path.Combine(materials, "04 Example 2", "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx");
            }
            d = d.Parent!;
        }
        throw new InvalidOperationException("repo root not found");
    }

    private static (PipelineOutput Output, Dictionary<string, List<PolyCurve2D>> Crossings) Run(
        string example, params string[] confirmed)
    {
        var golden = GoldenFixture.Load(example);
        var model = WorkbookReader.Read(WorkbookPath(example));
        var sidecar = ProjectSidecar.Empty with
        {
            ConfirmedEndpointReferences = new HashSet<string>(confirmed, StringComparer.Ordinal),
        };
        var findings = new List<ValidationFinding>();
        var curves = CurvesByLayer(example);
        var movements = ProjectAssembly.BuildMovements(curves,
            model.SignalGroups, model.PedestrianWidths, sidecar, findings);
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        var output = AnalysisPipeline.Run(new PipelineInput(
            example.ToUpperInvariant(), example + ".dwg", "FIXTURE", pack,
            new ProjectClassification(),
            golden.Constants, golden.Variant, golden.Parameters, movements));

        var crossings = movements
            .Where(m => m.Geometry.Mode == MovementMode.Pedestrian)
            .ToDictionary(m => m.Id, m => m.Geometry.Boundaries.ToList(), StringComparer.Ordinal);
        return (output, crossings);
    }

    /// <summary>
    /// A termination candidate claims "this boundary stops inside the crossing", so its point has to
    /// be on the crossing. This is what the old rule violated by tens of metres.
    /// </summary>
    [Theory]
    [InlineData("example1")]
    [InlineData("example2")]
    public void Every_termination_candidate_lies_on_the_crossing_it_claims_to_stop_in(string example)
    {
        var (output, crossings) = example == "example2"
            ? Run(example, "E-L.b1", "S-L.b2")
            : Run(example);

        var offenders = new List<string>();
        foreach (var conflict in output.Analysis.Conflicts)
        {
            foreach (var p in conflict.Points)
            {
                var ids = p.ClearingCurveId + "|" + p.EnteringCurveId;
                if (!ids.Contains('@')) continue;                       // not a termination candidate
                var crossingId = conflict.Clearing is var c && crossings.ContainsKey(c) ? c
                    : crossings.ContainsKey(conflict.Entering) ? conflict.Entering : null;
                if (crossingId is null) continue;                        // vehicle × vehicle rule
                var pt = new Point2D(p.X, p.Y);
                var gap = crossings[crossingId].Min(e => e.NearestStation(pt).Distance);
                if (gap > 1.0)
                    offenders.Add($"{conflict.Id} {p.Id} is {gap:F2} m from crossing '{crossingId}'");
            }
        }
        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    [Fact]
    public void Example1_a_to_S_T_agrees_with_the_engineer()
    {
        // The one cell where Example 1 ever disagreed with the completed workbook: manual 6,
        // engine 7, caused by a phantom termination candidate on the vehicle's own stop line.
        var (output, _) = Run("example1");
        var conflict = output.Analysis.Conflicts.Single(c => c.Clearing == "a" && c.Entering == "S-T");
        Assert.Equal(6, conflict.FinalIg);
    }

    [Fact]
    public void Example1_headline_numbers_are_unchanged_by_the_fix()
    {
        var (output, _) = Run("example1");
        Assert.Equal(50, output.Analysis.Conflicts.Count);
        Assert.All(output.Analysis.Conflicts, c => Assert.Equal("VALID", c.Status));
        Assert.Equal(24, output.Analysis.Matrix.Count(m => m.Status == "VALID"));
        Assert.Equal(5, output.Analysis.Conflicts
            .Single(c => c.Clearing == "W-L" && c.Entering == "S-T").FinalIg);
    }

    [Fact]
    public void Example2_headline_numbers_are_unchanged_by_the_fix()
    {
        var (output, _) = Run("example2", "E-L.b1", "S-L.b2");
        Assert.Equal(168, output.Analysis.Conflicts.Count);
        Assert.Equal(157, output.Analysis.Conflicts.Count(c => c.Status == "VALID"));
        Assert.Equal(11, output.Analysis.Conflicts.Count(c => c.Status == "REVIEW_REQUIRED"));
        Assert.Equal(37, output.Analysis.Matrix.Count(m => m.Status == "VALID"));
        Assert.Equal(7, output.Analysis.Matrix.Count(m => m.Status.StartsWith("REVIEW")));
    }
}
