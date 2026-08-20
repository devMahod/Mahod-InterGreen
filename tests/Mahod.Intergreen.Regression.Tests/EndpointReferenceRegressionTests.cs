using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>
/// Locks the endpoint-reference behaviour against the two real drawings, from their extracted
/// geometry (tests/fixtures/geometry/*.iggeometry.json — byte-identical to the geometry the
/// accepted release evidence was produced from).
///
/// Why this exists: Example 2's accepted baseline was green only because the gate's project
/// sidecar carried confirmedEndpointReferences = [E-L.b1, S-L.b2], hand-edited during Gate-P.
/// Run the same drawing the way a customer runs it — no sidecar — and two boundaries that stop a
/// few centimetres short of their stop line block a third of the matrix. Nothing recorded that
/// dependency, so the baseline read as "this drawing passes unaided", which it does not. These
/// tests state it in both directions so it can never quietly regress again.
/// </summary>
public class EndpointReferenceRegressionTests
{
    private const string EndpointUnconfirmed = "IG-GEO-004";

    /// <summary>The two boundaries in Example 2 whose measurement origin is a drawn endpoint.</summary>
    private static readonly string[] Example2NearMissBoundaries = { "E-L.b1", "S-L.b2" };

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

    /// <summary>
    /// Curves grouped by layer, exactly as the palette hands them to ProjectAssembly. Both drawings
    /// are Unitless with metres confirmed in project setup, so no scaling is applied here either.
    /// </summary>
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

    private static IReadOnlyList<ValidationFinding> RunWith(string example, params string[] confirmed)
    {
        var golden = GoldenFixture.Load(example);
        var sidecar = ProjectSidecar.Empty with
        {
            ConfirmedEndpointReferences = new HashSet<string>(confirmed, StringComparer.Ordinal),
        };
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(
            CurvesByLayer(example),
            new Dictionary<string, string>(),
            new Dictionary<string, double>(),
            sidecar,
            findings);

        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        var output = AnalysisPipeline.Run(new PipelineInput(
            example.ToUpperInvariant(), example + ".dwg", "FIXTURE", pack,
            new ProjectClassification(),
            golden.Constants, golden.Variant, golden.Parameters, movements));
        return output.Findings.Concat(findings).ToList();
    }

    private static string[] UnconfirmedBoundaries(IEnumerable<ValidationFinding> findings) =>
        findings.Where(f => f.Code == EndpointUnconfirmed)
            .Select(f => f.Message.Split(' ').ElementAtOrDefault(1) ?? "")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Example2_as_a_customer_runs_it_reports_exactly_the_two_near_miss_boundaries()
    {
        var findings = RunWith("example2");
        Assert.Equal(Example2NearMissBoundaries.OrderBy(s => s, StringComparer.Ordinal),
                     UnconfirmedBoundaries(findings));
    }

    [Fact]
    public void Example2_endpoint_findings_are_blocking_errors_not_advisories()
    {
        var findings = RunWith("example2").Where(f => f.Code == EndpointUnconfirmed).ToList();
        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(Severity.Error, f.Severity));
    }

    [Fact]
    public void Example2_message_carries_the_reason_and_an_action_the_engineer_can_take()
    {
        var f = RunWith("example2").First(x => x.Code == EndpointUnconfirmed);
        Assert.Contains("does not intersect its stop line", f.Message);
        Assert.Contains("Confirm the endpoint reference", f.RecommendedAction);
    }

    [Fact]
    public void Confirming_the_two_boundaries_clears_every_endpoint_finding()
        => Assert.Empty(UnconfirmedBoundaries(RunWith("example2", Example2NearMissBoundaries)));

    [Fact]
    public void Confirming_only_one_boundary_leaves_the_other_blocked()
    {
        var findings = RunWith("example2", "E-L.b1");
        Assert.Equal(new[] { "S-L.b2" }, UnconfirmedBoundaries(findings));
    }

    [Fact]
    public void An_unrelated_confirmation_confirms_nothing()
    {
        var findings = RunWith("example2", "W-T.b1", "N-R.b2");
        Assert.Equal(Example2NearMissBoundaries.OrderBy(s => s, StringComparer.Ordinal),
                     UnconfirmedBoundaries(findings));
    }

    [Fact]
    public void Example1_needs_no_confirmation_at_all()
        => Assert.Empty(UnconfirmedBoundaries(RunWith("example1")));
}
