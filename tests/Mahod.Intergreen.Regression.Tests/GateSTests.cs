using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Geometry;
using Mahod.Intergreen.Reporting;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>Gate S — deterministic outputs, environment separation, serialization invariants.</summary>
public class GateSTests
{
    private static PolyCurve2D Vertical(double x) => new(new[] { (ISegment2D)new LineSegment2D(new(x, -20), new(x, 30)) });
    private static PolyCurve2D Horizontal(double y) => new(new[] { (ISegment2D)new LineSegment2D(new(-20, y), new(30, y)) });

    private static PipelineInput SyntheticInput()
    {
        var parameters = new Dictionary<string, LegacyMovementParameters>
        {
            ["S-T"] = new(50, 25, 12, null),
            ["W-T"] = new(50, 25, 12, null),
            ["W-L"] = new(50, 25, 12, null),
        };
        var south = new PipelineMovement("S-T", MovementMode.Vehicle, "SG02",
            new MovementGeometry("S-T", MovementMode.Vehicle,
                new[] { Vertical(0), Vertical(3.5) }, Array.Empty<PolyCurve2D>(),
                new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-2, 0), new(6, 0)) })),
            new[] { "H-1A", "H-1B" });
        var west = new PipelineMovement("W-T", MovementMode.Vehicle, "SG04",
            new MovementGeometry("W-T", MovementMode.Vehicle,
                new[] { Horizontal(10), Horizontal(13.5) }, Array.Empty<PolyCurve2D>(),
                new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-5, 8), new(-5, 16)) })),
            new[] { "H-2A", "H-2B" });
        // a movement whose boundaries never reach its stop line → blocking geometry error
        var broken = new PipelineMovement("W-L", MovementMode.Vehicle, "SG04",
            new MovementGeometry("W-L", MovementMode.Vehicle,
                new[] { Horizontal(100), Horizontal(103.5) }, Array.Empty<PolyCurve2D>(),
                new PolyCurve2D(new[] { (ISegment2D)new LineSegment2D(new(-5, 8), new(-5, 16)) })),
            new[] { "H-3A" });

        var pack = RulePackLoader.Load(System.IO.Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        return new PipelineInput(
            "SYNTH-1", "synthetic.dwg", "AB12", pack,
            new ProjectClassification { RoadType = RoadEnvironment.Urban, PostedSpeedKph = 50 },
            new LegacyConstants(GlobalVehicleLengthMeters: 12),
            LegacyTemplateVariant.V2GlobalVehicleLength,
            parameters,
            new[] { south, west, broken });
    }

    [Fact]
    public void Same_input_twice_produces_byte_identical_analysis_and_validation()
    {
        var a = AnalysisPipeline.Run(SyntheticInput());
        var b = AnalysisPipeline.Run(SyntheticInput());
        Assert.Equal(AnalysisWriters.WriteAnalysisJson(a.Analysis), AnalysisWriters.WriteAnalysisJson(b.Analysis));
        Assert.Equal(AnalysisWriters.WriteValidationJson(a.Findings), AnalysisWriters.WriteValidationJson(b.Findings));
    }

    [Fact]
    public void Analysis_json_contains_no_environment_noise()
    {
        var output = AnalysisPipeline.Run(SyntheticInput());
        var json = AnalysisWriters.WriteAnalysisJson(output.Analysis);
        Assert.DoesNotContain(":\\\\", json);                       // no windows paths
        Assert.DoesNotContain(Environment.MachineName, json);
        Assert.DoesNotContain("analyzedAt", json);                  // no wall-clock in the payload
        Assert.Contains("\"synthetic.dwg\"", json);                 // source by name+hash only
        Assert.Contains("\"AB12\"", json);
    }

    [Fact]
    public void Blocked_geometry_propagates_to_blocked_matrix_cell_in_document()
    {
        var output = AnalysisPipeline.Run(SyntheticInput());
        // W-L cannot resolve its stop line → conflicts involving it are ERROR
        Assert.Contains(output.Analysis.Conflicts, c => c.Status == "ERROR" && c.Id.Contains("W-L"));
        // SG04↔SG02 cells depend on W-L and W-T; W-L errors must block them
        var cell = output.Analysis.Matrix.Single(m => m.ClearingSignalGroup == "SG04" && m.EnteringSignalGroup == "SG02");
        Assert.Equal("BLOCKED", cell.Status);
        Assert.Null(cell.Value);
        // the valid direction stays valid only if none of its contributors errored — S-T→W-L errors too,
        // so SG02→SG04 must equally be blocked. Nothing may silently compute from survivors.
        var reverse = output.Analysis.Matrix.Single(m => m.ClearingSignalGroup == "SG02" && m.EnteringSignalGroup == "SG04");
        Assert.Equal("BLOCKED", reverse.Status);
    }

    [Fact]
    public void NaN_reaching_serialization_fails_with_internal_invariant_failure()
    {
        var output = AnalysisPipeline.Run(SyntheticInput());
        var doc = output.Analysis with
        {
            Conflicts = output.Analysis.Conflicts
                .Select(c => c.Points.Count > 0
                    ? c with { Points = c.Points.Select(p => p with { Cd = double.NaN }).ToList() }
                    : c)
                .ToList(),
        };
        var ex = Assert.Throws<InternalNumericInvariantException>(() => AnalysisWriters.WriteAnalysisJson(doc));
        Assert.Contains("INTERNAL_NUMERIC_INVARIANT_FAILURE", ex.Message);
    }

    [Fact]
    public void Negative_distance_reaching_serialization_fails()
    {
        var output = AnalysisPipeline.Run(SyntheticInput());
        var doc = output.Analysis with
        {
            Conflicts = output.Analysis.Conflicts
                .Select(c => c.Points.Count > 0
                    ? c with { Points = c.Points.Select(p => p with { Ed = -1.0 }).ToList() }
                    : c)
                .ToList(),
        };
        Assert.Throws<InternalNumericInvariantException>(() => AnalysisWriters.WriteAnalysisJson(doc));
    }

    [Fact]
    public void Valid_conflict_distances_and_matrix_appear_in_document()
    {
        var output = AnalysisPipeline.Run(SyntheticInput());
        var st = output.Analysis.Conflicts.Single(c => c.Id == "S-T→W-T");
        Assert.Equal("VALID", st.Status);
        Assert.Equal(4, st.Points.Count);
        Assert.Equal(new[] { 10.0, 10.0, 13.5, 13.5 },
            st.Points.Select(p => p.Cd).OrderBy(x => x).ToArray());
        Assert.NotNull(st.FinalIg);
        Assert.NotNull(st.DefiningPointId);
    }
}
