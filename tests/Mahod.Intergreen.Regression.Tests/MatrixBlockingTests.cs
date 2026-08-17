using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Core;
using Mahod.Intergreen.Core.Legacy;
using Mahod.Intergreen.Excel;
using Xunit;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>Final Hotfix §17 — synthetic aggregation-failure tests A/B/C.</summary>
public class MatrixAggregationTests
{
    private static ConflictContribution C(string id, string clear, string enter, string sgC, string sgE,
        MatrixContributionStatus status, int? ig)
        => new(id, clear, enter, sgC, sgE, status, ig, Array.Empty<ValidationFinding>());

    [Fact]
    public void TestA_one_missing_cd_blocks_the_cell_not_max_of_valid()
    {
        var svc = new SignalGroupMatrixService();
        var result = svc.Build(new[]
        {
            C("C1", "N-T", "S-L", "SG02", "SG05", MatrixContributionStatus.Valid, 4),
            C("C2", "N-R", "S-L", "SG02", "SG05", MatrixContributionStatus.Valid, 5),
            C("C3", "N-T", "S-T", "SG02", "SG05", MatrixContributionStatus.Valid, 6),
            C("C4", "N-L", "S-T", "SG02", "SG05", MatrixContributionStatus.Error, null), // missing CD
        });
        var cell = Assert.Single(result.Cells);
        Assert.Equal(MatrixCellStatus.Blocked, cell.Status);
        Assert.Null(cell.Value); // NOT 6
        Assert.Contains(cell.Findings, f => f.Code == SignalGroupMatrixService.CodeCellBlocked);
    }

    [Fact]
    public void TestB_unrelated_cells_still_calculate_while_affected_cell_blocks()
    {
        var svc = new SignalGroupMatrixService();
        var result = svc.Build(new[]
        {
            C("C1", "N-T", "S-L", "SG02", "SG05", MatrixContributionStatus.Error, null),
            C("C2", "E-T", "W-L", "SG01", "SG03", MatrixContributionStatus.Valid, 5),
        });
        Assert.Equal(2, result.Cells.Count);
        Assert.Equal(MatrixCellStatus.Blocked, result.Cells.Single(c => c.ClearingSignalGroup == "SG02").Status);
        var ok = result.Cells.Single(c => c.ClearingSignalGroup == "SG01");
        Assert.Equal(MatrixCellStatus.Valid, ok.Status);
        Assert.Equal(5, ok.Value);
    }

    [Fact]
    public void TestC_review_required_policy_is_explicit_both_ways()
    {
        var contributions = new[]
        {
            C("C1", "N-T", "S-L", "SG02", "SG05", MatrixContributionStatus.ReviewRequired, 4),
        };

        var include = new SignalGroupMatrixService(ReviewRequiredPolicy.IncludeValueWithReviewStatus)
            .Build(contributions).Cells.Single();
        Assert.Equal(MatrixCellStatus.ReviewRequired, include.Status);
        Assert.Equal(4, include.Value);

        var block = new SignalGroupMatrixService(ReviewRequiredPolicy.BlockCell)
            .Build(contributions).Cells.Single();
        Assert.Equal(MatrixCellStatus.Blocked, block.Status);
        Assert.Null(block.Value);
    }

    [Fact]
    public void Same_signal_group_pairs_are_excluded_with_audit_trail()
    {
        var svc = new SignalGroupMatrixService();
        var result = svc.Build(new[]
        {
            C("C1", "S-R", "S-T", "SG02", "SG02", MatrixContributionStatus.Valid, 4), // same approach → normal
            C("C2", "N-T", "S-T", "SG02", "SG02", MatrixContributionStatus.Valid, 5), // different approaches → review
        });
        Assert.Empty(result.Cells);                       // no matrix entries for same-SG pairs
        Assert.Equal(2, result.ExcludedSameGroup.Count);  // evidence kept
        var finding = Assert.Single(result.Findings, f => f.Code == SignalGroupMatrixService.CodeSameGroupConflict);
        Assert.Equal("C2", finding.ConflictRef);
    }
}

/// <summary>
/// Final Hotfix §17A — real-data acceptance: Example 2 production must block exactly
/// 3 matrix cells (SG4→SG2, SG2→SG4, SG1→a) — no under- and no over-propagation.
/// </summary>
public class Example2MatrixAcceptanceTests
{
    private static string Example2Path()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "materials", "Inter-green Automation",
                "04 Example 2", "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Example 2 workbook not found");
    }

    [Fact]
    public void Exactly_three_cells_are_blocked_no_more_no_less()
    {
        var model = WorkbookReader.Read(Example2Path());
        var analyzer = new LegacyProductionAnalyzer(model.Constants, model.Variant, model.MovementParameters);

        MovementMode ModeOf(string m) => model.MovementParameters.ContainsKey(m)
            ? MovementMode.Vehicle
            : MovementMode.Pedestrian;

        string SgOf(string movement) =>
            model.SignalGroups.TryGetValue(movement, out var sg) ? sg : movement;

        var contributions = new List<ConflictContribution>();
        var incompleteDetected = 0;
        foreach (var row in model.Rows)
        {
            var r = analyzer.AnalyzeRow(row.Input, ModeOf(row.Input.ClearingMovement), ModeOf(row.Input.EnteringMovement));
            if (r.Findings.Any(f => f.Code is LegacyProductionAnalyzer.CodeMissingMeasurement
                    or LegacyProductionAnalyzer.CodeMissingClearingMeasurement))
                incompleteDetected++;
            contributions.Add(new ConflictContribution(
                $"C{row.Input.ConflictNo:D3}-{row.Input.ClearingMovement}-{row.Input.EnteringMovement}",
                row.Input.ClearingMovement, row.Input.EnteringMovement,
                SgOf(row.Input.ClearingMovement), SgOf(row.Input.EnteringMovement),
                r.Status switch
                {
                    RowStatus.Error => MatrixContributionStatus.Error,
                    RowStatus.ReviewRequired => MatrixContributionStatus.ReviewRequired,
                    _ => MatrixContributionStatus.Valid,
                },
                r.FinalIg,
                r.Findings));
        }

        Assert.Equal(4, incompleteDetected); // Incomplete source conflicts detected: 4 / 4

        var matrix = new SignalGroupMatrixService().Build(contributions);
        var blocked = matrix.Cells.Where(c => c.Status == MatrixCellStatus.Blocked)
            .Select(c => $"{c.ClearingSignalGroup}→{c.EnteringSignalGroup}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        // Required acceptance (Final Hotfix §17A): exactly these three, not fewer, not more
        Assert.Equal(new[] { "2→4", "4→2", "1→a" }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), blocked);

        // unrelated cells still calculate
        Assert.Contains(matrix.Cells, c => c.Status == MatrixCellStatus.Valid && c.Value is not null);

        // write the required report
        var report = $"""
            # EXAMPLE2_MATRIX_ACCEPTANCE

            Incomplete source conflicts detected: {incompleteDetected} / 4
            Expected blocked matrix cells:         3
            Actual blocked matrix cells:           {blocked.Length}
            Blocked cells:                         {string.Join(", ", blocked)}
            Unexpected blocked matrix cells:       {blocked.Except(new[] { "2→4", "4→2", "1→a" }).Count()}
            Missing expected blocked cells:        {new[] { "2→4", "4→2", "1→a" }.Except(blocked).Count()}
            Valid cells:                           {matrix.Cells.Count(c => c.Status == MatrixCellStatus.Valid)}
            Review-required cells:                 {matrix.Cells.Count(c => c.Status == MatrixCellStatus.ReviewRequired)}
            Same-SG excluded pairs (audit):        {matrix.ExcludedSameGroup.Count}
            """;
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "TASK_LEDGER.md")))
            root = Path.GetDirectoryName(root);
        if (root is not null)
            File.WriteAllText(Path.Combine(root, "test-results", "EXAMPLE2_MATRIX_ACCEPTANCE.md"), report);
    }
}
