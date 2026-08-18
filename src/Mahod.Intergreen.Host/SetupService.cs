using System.Text.Json;

namespace Mahod.Intergreen.Host;

/// <summary>How the engineer supplied the workbook — for logging only. It must never
/// change behaviour: both methods run the identical pipeline below.</summary>
public enum SetupInputMethod { Browse, ManualPath }

public sealed record SetupCommitResult(
    bool Committed,
    string? SidecarPath,
    SidecarLoadStatus ReloadStatus,
    WorkbookRefStatus ReloadRef,
    string UserMessageHe);

/// <summary>
/// THE Setup pipeline (r6). Whatever the input method — Browse (file picker) or manual
/// paste/typed full path — the raw string converges here immediately:
///
///     WorkbookPathResolver → WorkbookAcceptance → WorkbookReader
///                          → validated transactional sidecar commit
///
/// Validation and commit are deliberately two phases so a rejected input can never touch
/// a previously valid project configuration.
/// </summary>
public static class SetupService
{
    /// <summary>Phase 1 — resolve + accept. Never writes anything.</summary>
    public static WorkbookAcceptResult Validate(string? rawPath)
        => WorkbookAcceptance.Validate(rawPath);

    /// <summary>Phase 2 — commit atomically, then reload and re-resolve as proof.</summary>
    public static SetupCommitResult Commit(
        WorkbookAcceptResult accepted,
        string sidecarPath,
        string drawingDir,
        IReadOnlyDictionary<string, string>? extraValues = null)
    {
        if (!accepted.IsOk || accepted.NormalizedPath is null)
            return new(false, null, SidecarLoadStatus.NoSidecar, WorkbookRefStatus.NotConfigured,
                accepted.UserMessageHe);

        var loaded = SidecarStore.Load(sidecarPath);
        var data = loaded.Data;
        if (extraValues is not null)
            foreach (var kv in extraValues)
                SidecarStore.Set(data, kv.Key, kv.Value);
        SidecarStore.Set(data, "workbook", accepted.NormalizedPath);
        SidecarStore.Commit(sidecarPath, data);

        var reload = SidecarStore.Load(sidecarPath);
        var wbRef = SidecarStore.ResolveWorkbook(reload.Data, drawingDir);
        bool ok = reload.Status == SidecarLoadStatus.Ok && wbRef.Status == WorkbookRefStatus.Ok;
        return new(ok, sidecarPath, reload.Status, wbRef.Status,
            ok ? "" : "ההגדרה נשמרה אך האימות החוזר נכשל — הריצי Setup שוב.");
    }
}
