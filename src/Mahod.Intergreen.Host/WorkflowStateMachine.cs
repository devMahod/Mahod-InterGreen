namespace Mahod.Intergreen.Host;

public enum WorkflowAction { Setup, Validate, Analyze, Show, Export, ClearQa }

/// <summary>
/// Deterministic user-journey gate (hardening directive §9). The palette consults this
/// before every action; out-of-order actions get a clear Hebrew message instead of a
/// crash, and stale results are never presented as current.
/// </summary>
public sealed class WorkflowStateMachine
{
    public bool ProjectConfigured { get; private set; }
    public bool Validated { get; private set; }
    public bool Analyzed { get; private set; }
    public bool HasSelection { get; set; }

    /// <summary>null = allowed; otherwise the user-facing Hebrew gate message.</summary>
    public string? Gate(WorkflowAction a) => a switch
    {
        WorkflowAction.Setup => null,
        WorkflowAction.ClearQa => null,
        WorkflowAction.Validate when !ProjectConfigured =>
            "עדיין לא הוגדר פרויקט לשרטוט הזה.\nלחצי קודם על Setup ובחרי את קובץ ה-Excel של הפרויקט.",
        WorkflowAction.Validate => null,
        WorkflowAction.Analyze when !ProjectConfigured =>
            "עדיין לא הוגדר פרויקט לשרטוט הזה.\nלחצי קודם על Setup ובחרי את קובץ ה-Excel של הפרויקט.",
        WorkflowAction.Analyze => null, // Analyze runs Validate internally (same pipeline)
        WorkflowAction.Show when !Analyzed =>
            "אין עדיין תוצאות ניתוח.\nהריצי Analyze, בחרי שורה ברשימת הקונפליקטים ואז Show.",
        WorkflowAction.Show when !HasSelection =>
            "לא נבחרה שורה.\nבחרי קונפליקט ברשימה ואז לחצי Show.",
        WorkflowAction.Show => null,
        WorkflowAction.Export when !Analyzed =>
            "אין עדיין תוצאות לייצוא.\nהריצי Analyze ואחר כך Export Excel.",
        WorkflowAction.Export => null,
        _ => null,
    };

    public void OnSetupCommitted()
    {
        ProjectConfigured = true;
        // a new/changed Setup invalidates any previous results — never show stale output
        Validated = false;
        Analyzed = false;
        HasSelection = false;
    }

    public void OnProjectLoadedFromSidecar() => ProjectConfigured = true;
    public void OnValidateSucceeded() => Validated = true;
    public void OnAnalyzeSucceeded() { Validated = true; Analyzed = true; }
    public void OnPipelineFailed() { /* keep prior flags: results already shown remain valid */ }
    public void OnProjectInvalidated()
    {
        ProjectConfigured = false;
        Validated = false;
        Analyzed = false;
        HasSelection = false;
    }
}
