namespace Mahod.Intergreen.Host;

/// <summary>
/// Palette-to-drawing binding (r9). The palette is a singleton while drawings come and
/// go; without an explicit binding the previous drawing's results stayed on screen after
/// a document switch and could be mistaken for current. The palette records which
/// drawing its displayed state belongs to; when the active document changes, displayed
/// results are cleared and the workflow state machine is reset before the new drawing's
/// saved project (sidecar) is adopted. Pure path logic lives here so it is testable.
/// </summary>
public static class DrawingBinding
{
    /// <summary>True when the displayed state belongs to a DIFFERENT drawing than the
    /// newly active one and must therefore be reset. Null/empty bound path means nothing
    /// is bound yet (first activation) — reset then too, so state is never inherited.</summary>
    public static bool RequiresReset(string? boundDrawingPath, string? activeDrawingPath)
    {
        if (string.IsNullOrWhiteSpace(activeDrawingPath))
            return true; // no active drawing: never keep another drawing's results visible
        if (string.IsNullOrWhiteSpace(boundDrawingPath))
            return true;
        return !string.Equals(
            System.IO.Path.GetFullPath(boundDrawingPath!),
            System.IO.Path.GetFullPath(activeDrawingPath!),
            StringComparison.OrdinalIgnoreCase);
    }
}
