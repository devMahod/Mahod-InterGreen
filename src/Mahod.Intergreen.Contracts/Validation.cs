namespace Mahod.Intergreen.Contracts;

/// <summary>Severity of a validation finding. ERROR blocks the affected calculation.</summary>
public enum Severity
{
    Pass,
    Warning,
    Error,
    ReviewRequired,
}

/// <summary>A single validation finding. Every finding is traceable and actionable.</summary>
public sealed record ValidationFinding(
    string Code,
    Severity Severity,
    string? ConflictRef,
    string Message,
    string? TechnicalDetails = null,
    string? RecommendedAction = null,
    string? SourceReference = null);
