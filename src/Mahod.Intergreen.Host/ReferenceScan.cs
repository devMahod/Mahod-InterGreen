using Mahod.Intergreen.Geometry;

namespace Mahod.Intergreen.Host;

/// <summary>What the tolerance did with one near-miss boundary.</summary>
public enum ReferenceResolutionKind
{
    /// <summary>Carried along its own tangent to the stop line — the engine now finds an exact intersection.</summary>
    Extended,
    /// <summary>Its end lies beside the stop line (typically past the stop line's own end vertex, within the tolerance):
    /// no extension can reach the line, so the drawn end is confirmed as the measurement origin, as the engineer
    /// would do by hand (r12 mechanism), and recorded as automatic.</summary>
    Confirmed,
    /// <summary>Left for the engineer: tolerance off, gap wider than the tolerance, or ambiguous geometry.</summary>
    Pending,
}

public sealed record ReferenceResolution(ReferenceIssue Issue, ReferenceResolutionKind Kind, ExtensionOutcome? Extension, string? Reason);

/// <summary>
/// Which vehicle boundaries can only be referenced to their stop line through a drawn endpoint
/// (Directive §14) — the pre-check the palette runs at Validate, and the input to the tolerance pass
/// (ED-016, amended 2026-09-02). Lives in Host so the same code is unit-tested on real geometry fixtures
/// and used by the AutoCAD layer; it decides nothing, it asks the engine's own resolver and measures the gap.
/// </summary>
public static class ReferenceScan
{
    /// <summary>Mirrors LegacyEnvelopeConflictStrategy's endpoint-suggestion tolerance, so the pre-check surfaces exactly the boundaries the engine will later reject.</summary>
    public const double ReferenceToleranceMeters = 0.5;

    /// <summary>One movement as the scan needs it: boundaries in the same order as their DWG handles.</summary>
    public sealed record Movement(string Id, IReadOnlyList<PolyCurve2D> Boundaries, PolyCurve2D? StopLine, IReadOnlyList<string> Handles);

    /// <summary>Boundaries whose measurement origin would fall back to a drawn endpoint; already-confirmed ids are omitted. Widest gap first.</summary>
    public static IReadOnlyList<ReferenceIssue> Scan(IEnumerable<Movement> movements, IReadOnlySet<string> confirmed)
    {
        var issues = new List<ReferenceIssue>();
        foreach (var mv in movements)
        {
            if (mv.StopLine is null) continue;
            for (var i = 0; i < mv.Boundaries.Count; i++)
            {
                var curveId = $"{mv.Id}.b{i + 1}";
                if (confirmed.Contains(curveId)) continue;
                var r = ReferenceStation.Resolve(mv.Boundaries[i], mv.StopLine, ReferenceToleranceMeters);
                if (r is null || r.Method != ReferenceStation.Method.EndpointFallback) continue;
                issues.Add(new ReferenceIssue(mv.Id, curveId,
                    i < mv.Handles.Count ? mv.Handles[i] : "-",
                    MeasureGap(mv.Boundaries[i], mv.StopLine)));
            }
        }
        return ReferenceReview.Sorted(issues);
    }

    /// <summary>
    /// Distance from the boundary's nearest endpoint to its stop line. PolyCurve2D exposes no
    /// point-to-curve distance, and reimplementing it here would let the number the engineer sees
    /// drift away from the engine's own decision. So we narrow the very same Resolve() call: the
    /// smallest tolerance that still yields an endpoint fallback is the gap.
    /// </summary>
    public static double MeasureGap(PolyCurve2D boundary, PolyCurve2D stopLine)
    {
        double lo = 0.0, hi = ReferenceToleranceMeters;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            if (ReferenceStation.Resolve(boundary, stopLine, mid)?.Method == ReferenceStation.Method.EndpointFallback)
                hi = mid;
            else
                lo = mid;
        }
        return hi;
    }

    /// <summary>
    /// The tolerance pass (ED-016, amended 2026-09-02). For every scanned issue whose gap is under the
    /// tolerance: extend the boundary along its own tangent to the stop line when that ray meets the line
    /// (<see cref="ReferenceResolutionKind.Extended"/>); when it cannot — the boundary runs past the stop
    /// line's end vertex, Lin's E-T 4EBA — confirm the drawn end as the origin, exactly what the engineer
    /// did by hand in r12 (<see cref="ReferenceResolutionKind.Confirmed"/>). Ambiguous ends and gaps
    /// wider than the tolerance stay <see cref="ReferenceResolutionKind.Pending"/>. Tolerance 0 does nothing.
    /// </summary>
    public static IReadOnlyList<ReferenceResolution> Resolve(
        IReadOnlyList<ReferenceIssue> issues, IReadOnlyList<Movement> movements, double toleranceMeters)
    {
        var result = new List<ReferenceResolution>();
        var byId = movements.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var issue in issues)
        {
            if (toleranceMeters <= 0)
            { result.Add(new(issue, ReferenceResolutionKind.Pending, null, "automatic resolution is off")); continue; }
            if (issue.GapMeters > toleranceMeters)
            { result.Add(new(issue, ReferenceResolutionKind.Pending, null, $"gap {issue.GapCentimetres:F1} cm is wider than the tolerance {toleranceMeters * 100:F0} cm")); continue; }
            if (!byId.TryGetValue(issue.MovementId, out var mv) || mv.StopLine is null)
            { result.Add(new(issue, ReferenceResolutionKind.Pending, null, "movement or stop line not found")); continue; }
            var idx = mv.Handles.ToList().IndexOf(issue.Handle);
            if (idx < 0 || idx >= mv.Boundaries.Count)
            { result.Add(new(issue, ReferenceResolutionKind.Pending, null, "boundary not found by handle")); continue; }

            var outcome = BoundaryExtension.TryExtendToStopLine(mv.Boundaries[idx], mv.StopLine, toleranceMeters);
            if (outcome.Applied)
                result.Add(new(issue, ReferenceResolutionKind.Extended, outcome, null));
            else if (outcome.Reason is string r && r.StartsWith("no end reaches", StringComparison.Ordinal))
                result.Add(new(issue, ReferenceResolutionKind.Confirmed, null,
                    $"the boundary runs beside the stop line ({issue.GapCentimetres:F1} cm from it, past its end) — nothing to extend to; the drawn end is the origin"));
            else
                result.Add(new(issue, ReferenceResolutionKind.Pending, null, outcome.Reason));
        }
        return result;
    }
}
