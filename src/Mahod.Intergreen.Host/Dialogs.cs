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
