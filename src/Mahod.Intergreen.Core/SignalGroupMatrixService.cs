using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Core;

/// <summary>Matrix cell status (Final Hotfix §10 — blank must never be ambiguous).</summary>
public enum MatrixCellStatus { Valid, NotApplicable, Blocked, ReviewRequired }

/// <summary>Explicit policy for REVIEW_REQUIRED contributions (Final Hotfix §17 Test C — never silent).</summary>
public enum ReviewRequiredPolicy
{
    /// <summary>The cell carries its value and the REVIEW_REQUIRED status.</summary>
    IncludeValueWithReviewStatus,
    /// <summary>The cell is blocked until the review is resolved.</summary>
    BlockCell,
}

/// <summary>One directed conflict contribution entering the matrix aggregation.</summary>
public sealed record ConflictContribution(
    string ConflictId,
    string ClearingMovement,
    string EnteringMovement,
    string ClearingSignalGroup,
    string EnteringSignalGroup,
    MatrixContributionStatus Status,
    int? FinalIg,
    IReadOnlyList<ValidationFinding> Findings);

public enum MatrixContributionStatus { Valid, ReviewRequired, Error }

public sealed record MatrixCell(
    string ClearingSignalGroup,
    string EnteringSignalGroup,
    int? Value,
    MatrixCellStatus Status,
    string? GoverningConflictId,
    IReadOnlyList<ValidationFinding> Findings);

public sealed record MatrixResult(
    IReadOnlyList<MatrixCell> Cells,
    IReadOnlyList<ValidationFinding> Findings,
    IReadOnlyList<ConflictContribution> ExcludedSameGroup); // audit trail, never deleted

/// <summary>
/// Signal-group matrix aggregation with mandatory BLOCKED propagation (Final Hotfix §9):
/// if any required contributing conflict is invalid, the affected cell is BLOCKED —
/// the maximum of only the valid contributions is never issued, because the unknown
/// result may be governing.
/// </summary>
public sealed class SignalGroupMatrixService
{
    public const string CodeCellBlocked = "IG-MTX-001";
    public const string CodeSameGroupConflict = "IG-MTX-002"; // SAME_SIGNAL_GROUP_GEOMETRIC_CONFLICT

    private readonly ReviewRequiredPolicy _reviewPolicy;

    public SignalGroupMatrixService(ReviewRequiredPolicy reviewPolicy = ReviewRequiredPolicy.IncludeValueWithReviewStatus)
        => _reviewPolicy = reviewPolicy;

    public MatrixResult Build(IReadOnlyList<ConflictContribution> contributions)
    {
        var findings = new List<ValidationFinding>();
        var excluded = new List<ConflictContribution>();
        var byCell = new Dictionary<(string Clear, string Enter), List<ConflictContribution>>();

        foreach (var c in contributions)
        {
            if (c.ClearingSignalGroup == c.EnteringSignalGroup)
            {
                // same signal group → movements run together; no intergreen between them.
                // Same approach → normal; different approaches with a geometric conflict → review.
                excluded.Add(c);
                var appA = ApproachOf(c.ClearingMovement);
                var appB = ApproachOf(c.EnteringMovement);
                if (appA is not null && appB is not null && appA != appB)
                {
                    findings.Add(new ValidationFinding(CodeSameGroupConflict, Severity.ReviewRequired,
                        c.ConflictId,
                        $"Movements {c.ClearingMovement} and {c.EnteringMovement} share signal group " +
                        $"'{c.ClearingSignalGroup}' but come from different approaches and conflict geometrically " +
                        "(SAME_SIGNAL_GROUP_GEOMETRIC_CONFLICT).",
                        SourceReference: "Addendum §E.3 / OQ-004"));
                }
                continue;
            }

            var key = (c.ClearingSignalGroup, c.EnteringSignalGroup);
            if (!byCell.TryGetValue(key, out var list))
                byCell[key] = list = new List<ConflictContribution>();
            list.Add(c);
        }

        var cells = new List<MatrixCell>();
        foreach (var ((clear, enter), list) in byCell.OrderBy(kv => kv.Key.Clear, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Enter, StringComparer.Ordinal))
        {
            var cellFindings = list.SelectMany(c => c.Findings).ToList();

            if (list.Any(c => c.Status == MatrixContributionStatus.Error))
            {
                var broken = string.Join(", ", list.Where(c => c.Status == MatrixContributionStatus.Error)
                    .Select(c => c.ConflictId));
                cellFindings.Add(new ValidationFinding(CodeCellBlocked, Severity.Error, $"{clear}→{enter}",
                    $"Matrix cell {clear}→{enter} is BLOCKED: contributing conflict(s) {broken} have no valid result. " +
                    "The unknown result may be governing.",
                    SourceReference: "Final Hotfix §9"));
                cells.Add(new MatrixCell(clear, enter, null, MatrixCellStatus.Blocked, null, cellFindings));
                continue;
            }

            if (_reviewPolicy == ReviewRequiredPolicy.BlockCell
                && list.Any(c => c.Status == MatrixContributionStatus.ReviewRequired))
            {
                cells.Add(new MatrixCell(clear, enter, null, MatrixCellStatus.Blocked, null, cellFindings));
                continue;
            }

            var governing = list.Where(c => c.FinalIg is not null)
                .OrderByDescending(c => c.FinalIg)
                .ThenBy(c => c.ConflictId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (governing is null)
            {
                cells.Add(new MatrixCell(clear, enter, null, MatrixCellStatus.Blocked, null, cellFindings));
                continue;
            }

            var status = list.Any(c => c.Status == MatrixContributionStatus.ReviewRequired)
                ? MatrixCellStatus.ReviewRequired
                : MatrixCellStatus.Valid;
            cells.Add(new MatrixCell(clear, enter, governing.FinalIg, status, governing.ConflictId, cellFindings));
        }

        return new MatrixResult(cells, findings, excluded);
    }

    /// <summary>Approach = the compass prefix of a vehicle movement code; pedestrians have none.</summary>
    private static string? ApproachOf(string movement)
    {
        var idx = movement.IndexOf('-');
        return idx > 0 ? movement[..idx] : null;
    }
}
