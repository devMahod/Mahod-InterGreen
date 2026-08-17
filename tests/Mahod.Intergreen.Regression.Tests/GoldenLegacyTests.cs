using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core.Legacy;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>
/// Gate E — Legacy golden regression: 40/40 + 108/108 = 148/148 on FINAL IG
/// and on the per-point intermediate intergreen values (workbook columns S/W/AA/AE).
/// Expected values come straight from the completed workbooks' cached cells.
/// </summary>
public class GoldenLegacyTests
{
    private const double IntermediateTolerance = 1e-8;

    public static IEnumerable<object[]> Examples()
    {
        yield return new object[] { "example1", 40 };
        yield return new object[] { "example2", 108 };
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void FinalIg_matches_every_golden_row(string example, int expectedRows)
    {
        var fx = GoldenFixture.Load(example);
        Assert.Equal(expectedRows, fx.Rows.Count);

        var calc = fx.CreateCalculator();
        var mismatches = new List<string>();

        foreach (var row in fx.Rows)
        {
            var result = calc.ComputeRow(row.Input);
            var expected = row.ExpectedFinalIg is double d ? (int?)(int)d : null;
            if (result.FinalIg != expected)
                mismatches.Add($"conflict {row.Input.ConflictNo}: expected {expected?.ToString() ?? "blank"}, got {result.FinalIg?.ToString() ?? "blank"}");
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count}/{fx.Rows.Count} FINAL IG mismatches:\n" + string.Join("\n", mismatches));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Intermediate_point_intergreens_match_every_golden_row(string example, int expectedRows)
    {
        var fx = GoldenFixture.Load(example);
        Assert.Equal(expectedRows, fx.Rows.Count);

        var calc = fx.CreateCalculator();
        var mismatches = new List<string>();

        foreach (var row in fx.Rows)
        {
            var result = calc.ComputeRow(row.Input);
            for (var i = 0; i < 4; i++)
            {
                var expected = row.ExpectedPointIgs[i];
                var got = result.PointResults[i]?.IntergreenSec;
                if (expected is null && got is null) continue;
                if (expected is null || got is null || Math.Abs(expected.Value - got.Value) > IntermediateTolerance)
                {
                    mismatches.Add($"conflict {row.Input.ConflictNo} P{i + 1}: expected {expected?.ToString() ?? "blank"}, got {got?.ToString() ?? "blank"}");
                    continue;
                }

                // Final Hotfix §12: also compare the raw time columns (P/Q/R, T/U/V, X/Y/Z, AB/AC/AD),
                // because integer rounding can conceal compensating errors.
                var (eFast, eSlow, eEnter) = row.ExpectedPointTimes[i];
                var pr = result.PointResults[i]!;
                if (eFast is double f && Math.Abs(f - pr.ClearFastSec) > IntermediateTolerance)
                    mismatches.Add($"conflict {row.Input.ConflictNo} P{i + 1} tFast: expected {f}, got {pr.ClearFastSec}");
                if (eSlow is double s && Math.Abs(s - pr.ClearSlowSec) > IntermediateTolerance)
                    mismatches.Add($"conflict {row.Input.ConflictNo} P{i + 1} tSlow: expected {s}, got {pr.ClearSlowSec}");
                if (eEnter is double en && Math.Abs(en - pr.EnterSec) > IntermediateTolerance)
                    mismatches.Add($"conflict {row.Input.ConflictNo} P{i + 1} tEnter: expected {en}, got {pr.EnterSec}");
            }

            // column AL: manual-rounding flag
            if (result.FinalIg is not null && row.ExpectedManualRoundingFlag != result.ManualRoundingCandidate)
                mismatches.Add($"conflict {row.Input.ConflictNo} AL flag: expected {row.ExpectedManualRoundingFlag}, got {result.ManualRoundingCandidate}");
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} intermediate mismatches:\n" + string.Join("\n", mismatches));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Defining_point_matches_workbook(string example, int expectedRows)
    {
        var fx = GoldenFixture.Load(example);
        Assert.Equal(expectedRows, fx.Rows.Count);
        var calc = fx.CreateCalculator();

        foreach (var row in fx.Rows.Where(r => r.ExpectedDefiningPoint is not null))
        {
            var result = calc.ComputeRow(row.Input);
            Assert.NotNull(result.DefiningPointIndex);
            Assert.Equal(row.ExpectedDefiningPoint, $"Point {result.DefiningPointIndex + 1}");
        }
    }

    [Fact]
    public void Example2_conflict_numbering_has_a_duplicate_8_and_skips_9()
    {
        // Source-workbook data defect (v3 §33 SOURCE_WORKBOOK_EXISTING_ERROR):
        // two rows are numbered 8 (N-T→S-L and N-T→W-R) and number 9 is skipped.
        // Recorded in FINDINGS.md; production scan must assign unique conflict IDs itself.
        var fx = GoldenFixture.Load("example2");
        Assert.Equal(2, fx.Rows.Count(r => r.Input.ConflictNo == 8));
        Assert.DoesNotContain(fx.Rows, r => r.Input.ConflictNo == 9);
        Assert.Equal(108, fx.Rows.Count);
    }

    [Fact]
    public void Multi_point_fixture_conflict8_uses_point_with_larger_time_not_position()
    {
        // Addendum §D: the single historical multi-point row — Example 2, conflict 8, N-T → W-R.
        // (Conflict number 8 is duplicated in the source sheet; select by movement pair.)
        var fx = GoldenFixture.Load("example2");
        var row = fx.Rows.Single(r => r.Input.ConflictNo == 8 && r.Input.EnteringMovement == "W-R");

        Assert.Equal(24.068, row.Input.Point1.ClearingDistanceMeters!.Value, 3);
        Assert.Equal(11.47, row.Input.Point1.EnteringDistanceMeters!.Value, 2);
        Assert.Equal(22.58, row.Input.Point2.ClearingDistanceMeters!.Value, 2);
        Assert.Equal(16.45, row.Input.Point2.EnteringDistanceMeters!.Value, 2);

        var calc = fx.CreateCalculator();
        var result = calc.ComputeRow(row.Input);

        // Point 1 has larger CD *and* smaller ED → larger intergreen → governs.
        Assert.Equal(0, result.DefiningPointIndex);
        Assert.True(result.PointResults[0]!.IntergreenSec > result.PointResults[1]!.IntergreenSec);
        Assert.Equal((int?)row.ExpectedFinalIg!.Value, result.FinalIg);
    }
}

/// <summary>
/// Gate F — production validation over the same golden data:
/// the four known incomplete rows must be caught, correctly classified,
/// and must not silently yield a normal result (v3 §26, Addendum §D).
/// </summary>
public class ProductionValidationRegressionTests
{
    [Fact]
    public void Example2_rows_18_26_34_60_are_blocked_with_correct_classification()
    {
        var fx = GoldenFixture.Load("example2");
        var analyzer = fx.CreateProductionAnalyzer();

        var blocked = new Dictionary<int, string>();
        foreach (var row in fx.Rows)
        {
            var result = analyzer.AnalyzeRow(row.Input,
                fx.ModeOf(row.Input.ClearingMovement), fx.ModeOf(row.Input.EnteringMovement));
            foreach (var f in result.Findings.Where(f => f.Severity == Severity.Error))
                blocked[row.Input.ConflictNo] = f.Code;
        }

        Assert.Equal(new Dictionary<int, string>
        {
            [18] = LegacyProductionAnalyzer.CodeMissingMeasurement,           // no CD, no ED
            [26] = LegacyProductionAnalyzer.CodeMissingClearingMeasurement,   // ED=0 defined, CD missing
            [34] = LegacyProductionAnalyzer.CodeMissingClearingMeasurement,   // ED=0 defined, CD missing
            [60] = LegacyProductionAnalyzer.CodeMissingMeasurement,           // no CD, no ED
        }, blocked);

        // and the blocked rows carry no engineering number
        foreach (var no in new[] { 18, 26, 34, 60 })
        {
            var g = fx.Rows.Single(x => x.Input.ConflictNo == no);
            var r = analyzer.AnalyzeRow(g.Input,
                fx.ModeOf(g.Input.ClearingMovement), fx.ModeOf(g.Input.EnteringMovement));
            Assert.True(r.IsBlocked);
            Assert.Null(r.FinalIg);
        }
    }

    [Fact]
    public void Example1_has_no_blocked_rows_and_production_matches_workbook_final()
    {
        // The stable production formulation is algebraically identical to the historical
        // formula away from the 80 km/h singularity; golden speeds are 50/25, so the
        // production analyzer must reproduce every Example 1 FINAL IG exactly.
        var fx = GoldenFixture.Load("example1");
        var analyzer = fx.CreateProductionAnalyzer();

        foreach (var row in fx.Rows)
        {
            var result = analyzer.AnalyzeRow(row.Input,
                fx.ModeOf(row.Input.ClearingMovement), fx.ModeOf(row.Input.EnteringMovement));
            Assert.False(result.IsBlocked, $"conflict {row.Input.ConflictNo} unexpectedly blocked");
            Assert.Equal((int?)row.ExpectedFinalIg!.Value, result.FinalIg);
        }
    }
}

/// <summary>
/// Addendum §B.4 — the two rounding strategies differ on exactly 13 of the 148 golden rows,
/// always in the direction where the workbook value is shorter (less safe).
/// </summary>
public class RoundingDeltaRegressionTests
{
    private static readonly Dictionary<string, int[]> ExpectedDeltaRows = new()
    {
        ["example1"] = new[] { 22, 26, 82, 92, 93, 97, 98, 99 },
        ["example2"] = new[] { 25, 43, 62, 84, 107 },
    };

    [Theory]
    [InlineData("example1")]
    [InlineData("example2")]
    public void Workbook_vs_ceiling_delta_rows_are_exactly_the_known_set(string example)
    {
        var fx = GoldenFixture.Load(example);
        var calc = fx.CreateCalculator();
        var legacy = new MahodLegacyRounding();
        var ceil = new GuidelinesCeilingRounding();

        var delta = new List<int>();
        foreach (var row in fx.Rows)
        {
            var result = calc.ComputeRow(row.Input);
            if (result.RawMaxIntergreenSec is not double raw) continue;
            var l = legacy.Round(raw);
            var c = ceil.Round(raw);
            if (l is null) continue;
            if (l != c) delta.Add(row.Input.ConflictNo);
            // the deviation is always downward: workbook never exceeds the ceiling
            Assert.True(l <= c, $"conflict {row.Input.ConflictNo}: legacy {l} > ceil {c}");
        }

        Assert.Equal(ExpectedDeltaRows[example], delta.ToArray());
    }

    [Fact]
    public void Total_delta_is_13_rows()
    {
        Assert.Equal(13, ExpectedDeltaRows["example1"].Length + ExpectedDeltaRows["example2"].Length);
    }
}
