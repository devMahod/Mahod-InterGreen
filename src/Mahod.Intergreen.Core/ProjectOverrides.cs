using System;
using System.Collections.Generic;
using System.Linq;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Core;

/// <summary>Scope of a project-approved exception (Directive §40).</summary>
public enum OverrideScope { Project, Movement, Crossing, Conflict }

/// <summary>
/// Minimal typed approved-exception model (Directive §40–§41). Rule Packs stay immutable;
/// EffectiveRules = BaseRulePack + ProjectOverrideSet. The base value is never erased.
/// Only whitelisted typed targets may be overridden — never executable formulas.
/// </summary>
public sealed record ProjectOverride(
    string Id,
    OverrideScope Scope,
    string Target,            // whitelisted target key, e.g. "finalIg", "pedestrianWidthMeters", "postedSpeedKph"
    double BaseValue,
    double EffectiveValue,
    string? Reason,
    string? ApprovalReference,
    string? Approver,
    string? ApprovalDate);

public sealed record OverrideResolution(
    double EffectiveValue,
    ProjectOverride? Applied,
    IReadOnlyList<ValidationFinding> Findings);

public static class ProjectOverrideService
{
    public const string CodeSafetyReducing = "IG-OVR-001"; // SAFETY_REDUCING_OVERRIDE
    public const string CodeIncompleteApproval = "IG-OVR-002";
    public const string CodeUnknownTarget = "IG-OVR-003";

    /// <summary>Whitelisted override targets (Directive §40 — typed values only).</summary>
    public static readonly IReadOnlySet<string> AllowedTargets = new HashSet<string>
    {
        "finalIg",
        "pedestrianWidthMeters",
        "postedSpeedKph",
        "vehicleLengthMeters",
    };

    /// <summary>
    /// Resolves the effective value for a target. Safety-reducing overrides
    /// (smaller final IG than the engine's) demand complete approval metadata,
    /// otherwise the override is refused and the base value stays in force.
    /// </summary>
    public static OverrideResolution Resolve(
        string scopeTarget, double baseValue, IEnumerable<ProjectOverride> overrides,
        bool smallerIsSaferForThisTarget = false)
    {
        var findings = new List<ValidationFinding>();
        var match = overrides.FirstOrDefault(o => o.Target == scopeTarget
            || $"{o.Scope}:{o.Target}" == scopeTarget);
        if (match is null)
            return new OverrideResolution(baseValue, null, findings);

        if (!AllowedTargets.Contains(match.Target))
        {
            findings.Add(new ValidationFinding(CodeUnknownTarget, Severity.Error, match.Id,
                $"Override target '{match.Target}' is not whitelisted; the base value stays in force.",
                SourceReference: "Directive §40"));
            return new OverrideResolution(baseValue, null, findings);
        }

        var reducesSafety = smallerIsSaferForThisTarget
            ? match.EffectiveValue > baseValue
            : match.EffectiveValue < baseValue;

        if (reducesSafety)
        {
            var complete = !string.IsNullOrWhiteSpace(match.Reason)
                && !string.IsNullOrWhiteSpace(match.ApprovalReference)
                && !string.IsNullOrWhiteSpace(match.Approver)
                && !string.IsNullOrWhiteSpace(match.ApprovalDate);
            findings.Add(new ValidationFinding(CodeSafetyReducing,
                complete ? Severity.ReviewRequired : Severity.Error, match.Id,
                $"SAFETY_REDUCING_OVERRIDE on '{match.Target}': base {baseValue} → effective {match.EffectiveValue}." +
                (complete ? " Approval metadata complete; the engine's base result remains permanently visible."
                          : " Approval metadata INCOMPLETE — the override is refused."),
                RecommendedAction: complete ? null : "Provide reason, approval reference, approver and approval date.",
                SourceReference: "Directive §41"));
            if (!complete)
                return new OverrideResolution(baseValue, null, findings);
        }
        else if (string.IsNullOrWhiteSpace(match.Reason))
        {
            findings.Add(new ValidationFinding(CodeIncompleteApproval, Severity.Warning, match.Id,
                $"Override on '{match.Target}' has no recorded reason — audit metadata is expected even for conservative changes.",
                SourceReference: "Directive §41"));
        }

        return new OverrideResolution(match.EffectiveValue, match, findings);
    }
}
