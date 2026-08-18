namespace Mahod.Intergreen.Host;

/// <summary>Dialog abstraction (hardening directive §18) — production wires real Windows
/// dialogs; tests inject deterministic fakes. No engineering logic.</summary>
public interface IWorkbookPicker
{
    /// <returns>selected raw path, or null when the user cancelled.</returns>
    string? PickWorkbook(string? initialDirectory);
}

public interface IMessageService
{
    void Info(string messageHe);
    void Error(string messageHe);
    /// <returns>true = user confirmed.</returns>
    bool Confirm(string messageHe);
}

/// <summary>
/// Optional manual full-path entry (Lin's request, r6). Secondary to the file picker;
/// both methods hand their raw string to the SAME SetupService pipeline — there is no
/// second path/workbook implementation anywhere.
/// </summary>
public interface IWorkbookPathPrompt
{
    /// <returns>raw text as typed/pasted by the engineer, or null when cancelled/cleared.</returns>
    string? PromptForPath(string? initialValue);
}
