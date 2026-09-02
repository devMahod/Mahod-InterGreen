using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Host;
using Mahod.Intergreen.Reporting;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>
/// Lin's 05293_B (r12): two boundaries miss their stop line by 4.7 cm and 6.3 cm — invisible at drawing
/// scale, and enough for the engine to refuse them (Directive §14). ED-016 as amended: under a project
/// tolerance, N-R 4F27 (6.3 cm short along its own line) is extended to the stop line; E-T 4EBA runs past
/// the stop line's end vertex (4.7 cm beside it), so no extension can reach the line and the drawn end
/// is confirmed as the origin — what Lin did by hand in r12. The drawing is not touched either way.
/// </summary>
public class LinExtensionTests
{
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

    private static List<ReferenceScan.Movement> LinMovements()
    {
        var path = Path.Combine(RepoRoot(), "tests", "fixtures", "geometry", "lin05293.iggeometry.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var byLayer = new Dictionary<string, List<(string, PolyCurve2D)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.RootElement.GetProperty("curves").EnumerateArray())
        {
            var layer = c.GetProperty("layer").GetString()!;
            if (!byLayer.TryGetValue(layer, out var list)) byLayer[layer] = list = new List<(string, PolyCurve2D)>();
            list.Add((c.GetProperty("handle").GetString()!, PolyCurve2D.FromJson(c.GetProperty("geometry").GetRawText())));
        }
        var findings = new List<ValidationFinding>();
        var movements = ProjectAssembly.BuildMovements(byLayer, new Dictionary<string, string>(), new Dictionary<string, double>(),
            ProjectSidecar.Empty, findings);
        return movements.Where(m => m.Geometry.Mode != MovementMode.Pedestrian)
            .Select(m => new ReferenceScan.Movement(m.Id, m.Geometry.Boundaries, m.Geometry.StopLine, m.SourceHandles))
            .ToList();
    }

    [Fact]
    public void The_scan_finds_exactly_her_two_boundaries_with_their_gaps()
    {
        var issues = ReferenceScan.Scan(LinMovements(), new HashSet<string>());
        Assert.Equal(2, issues.Count);
        var byHandle = issues.ToDictionary(i => i.Handle);
        Assert.Equal("N-R", byHandle["4F27"].MovementId);
        Assert.Equal(6.3, byHandle["4F27"].GapCentimetres, 0.15);
        Assert.Equal("E-T", byHandle["4EBA"].MovementId);
        Assert.Equal(4.7, byHandle["4EBA"].GapCentimetres, 0.15);
        Assert.Equal("4F27", issues[0].Handle);                                  // widest first

        // the short end is known, so the palette can show the spot instead of quoting a handle
        var movements = LinMovements();
        foreach (var issue in issues)
        {
            Assert.NotNull(issue.NearEnd);
            var stop = movements.Single(m => m.Id == issue.MovementId).StopLine!;
            Assert.Equal(issue.GapMeters, stop.NearestStation(issue.NearEnd!.Value).Distance, 3);
        }
    }

    [Fact]
    public void Under_davids_ten_centimetres_one_is_extended_one_is_confirmed_and_nothing_stays_pending()
    {
        var movements = LinMovements();
        var issues = ReferenceScan.Scan(movements, new HashSet<string>());
        var resolved = ReferenceScan.Resolve(issues, movements, ReferenceReview.SuggestedAutoConfirmToleranceMeters);
        Assert.DoesNotContain(resolved, r => r.Kind == ReferenceResolutionKind.Pending);

        var nR = resolved.Single(r => r.Issue.Handle == "4F27");
        Assert.Equal(ReferenceResolutionKind.Extended, nR.Kind);
        Assert.InRange(nR.Extension!.LengthMeters * 100, 6.2, 6.5);                // 6.3 cm gap → 6.25 cm along the line
        var stop = movements.Single(m => m.Id == "N-R").StopLine!;
        Assert.Equal(ReferenceStation.Method.ExactIntersection,
            ReferenceStation.Resolve(nR.Extension.Extended!, stop, ReferenceScan.ReferenceToleranceMeters)!.Method);

        var eT = resolved.Single(r => r.Issue.Handle == "4EBA");
        Assert.Equal(ReferenceResolutionKind.Confirmed, eT.Kind);
        Assert.Contains("beside the stop line", eT.Reason);

        // a second scan — extended geometry in place, the confirmed id recorded — has nothing left to report
        var after = movements.Select(m =>
        {
            if (m.Id != "N-R") return m;
            var boundaries = m.Boundaries.ToList();
            boundaries[m.Handles.ToList().IndexOf("4F27")] = nR.Extension.Extended!;
            return m with { Boundaries = boundaries };
        }).ToList();
        Assert.Empty(ReferenceScan.Scan(after, new HashSet<string> { eT.Issue.CurveId }));
    }

    [Fact]
    public void With_the_tolerance_off_everything_waits_for_the_engineer()
    {
        var movements = LinMovements();
        var issues = ReferenceScan.Scan(movements, new HashSet<string>());
        var resolved = ReferenceScan.Resolve(issues, movements, ReferenceReview.DefaultAutoConfirmToleranceMeters);
        Assert.All(resolved, r => Assert.Equal(ReferenceResolutionKind.Pending, r.Kind));
        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public void A_tolerance_below_a_gap_leaves_that_boundary_pending()
    {
        var movements = LinMovements();
        var issues = ReferenceScan.Scan(movements, new HashSet<string>());
        var resolved = ReferenceScan.Resolve(issues, movements, 0.05);               // 5 cm: only the 4.7 cm case
        Assert.Equal(ReferenceResolutionKind.Confirmed, resolved.Single(r => r.Issue.Handle == "4EBA").Kind);
        var pending = resolved.Single(r => r.Issue.Handle == "4F27");
        Assert.Equal(ReferenceResolutionKind.Pending, pending.Kind);
        Assert.Contains("wider than the tolerance", pending.Reason);
    }
}
