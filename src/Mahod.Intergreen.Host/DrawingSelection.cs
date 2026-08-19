namespace Mahod.Intergreen.Host;

/// <summary>
/// DWG selection UX (r10, Lin's feedback): the project drawing is chosen through a normal
/// Windows file picker from the palette instead of being located/typed by hand. The palette
/// stays bound to the ACTIVE drawing (r9 DrawingBinding) — this chooser only decides which
/// drawing becomes active. All decision logic is pure and host-free so it is unit-tested;
/// the AutoCAD host supplies the dialog and the document operations through the seams below.
/// No engineering logic lives here.
/// </summary>
public interface IDrawingPicker
{
    /// <returns>selected raw path, or null when the user cancelled.</returns>
    string? PickDrawing(string? initialDirectory);
}

/// <summary>What the chooser needs from the host's document collection (production wraps
/// AutoCAD's DocumentManager; tests use an in-memory fake).</summary>
public interface IDrawingHost
{
    string? ActiveDrawingPath { get; }
    IReadOnlyList<string> OpenDrawingPaths { get; }
    /// <summary>Make an already-open drawing the active one.</summary>
    void Activate(string normalizedPath);
    /// <summary>Open a drawing from disk (becomes the active one).</summary>
    void Open(string normalizedPath);
}

/// <summary>Remembers the last folder a drawing was chosen from (sensible initial directory).</summary>
public interface IRecentFolderStore
{
    string? Load();
    void Save(string folder);
}

public enum DrawingSelectionStatus
{
    /// <summary>Picker closed without a choice — nothing changes.</summary>
    Cancelled,
    /// <summary>Path does not exist / is not readable.</summary>
    NotFound,
    /// <summary>Exists but is not a .dwg file.</summary>
    NotADrawing,
    /// <summary>Chosen drawing is already the active one — nothing changes.</summary>
    AlreadyActive,
    /// <summary>Chosen drawing was already open in the host and was activated.</summary>
    ActivatedOpen,
    /// <summary>Chosen drawing was opened from disk.</summary>
    Opened,
    /// <summary>The host refused/failed to open the drawing.</summary>
    OpenFailed,
}

public sealed record DrawingSelectionResult(
    DrawingSelectionStatus Status,
    string? NormalizedPath,
    string UserMessageHe,
    string LogDetail)
{
    public bool IsError => Status is DrawingSelectionStatus.NotFound
        or DrawingSelectionStatus.NotADrawing
        or DrawingSelectionStatus.OpenFailed;

    /// <summary>True when the active drawing changed (the palette re-binds through the
    /// host's DocumentActivated path); false means palette state is untouched.</summary>
    public bool ChangedActiveDrawing => Status is DrawingSelectionStatus.ActivatedOpen
        or DrawingSelectionStatus.Opened;
}

public static class DrawingSelection
{
    public const string DrawingExtension = ".dwg";

    /// <summary>Initial directory for the picker: last folder a drawing was chosen from,
    /// else the active drawing's folder, else the user's Documents. Only existing folders.</summary>
    public static string InitialDirectory(
        string? recentFolder,
        string? activeDrawingPath,
        string documentsFolder,
        Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        if (!string.IsNullOrWhiteSpace(recentFolder) && directoryExists(recentFolder!))
            return recentFolder!;
        if (!string.IsNullOrWhiteSpace(activeDrawingPath))
        {
            string? dir = SafeDirectoryName(activeDrawingPath!);
            if (dir is not null && directoryExists(dir))
                return dir;
        }
        return documentsFolder;
    }

    /// <summary>Decide what a picked path means. Pure: no dialogs, no host calls.</summary>
    public static DrawingSelectionResult Evaluate(
        string? picked,
        string? activeDrawingPath,
        IEnumerable<string> openDrawingPaths,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        if (string.IsNullOrWhiteSpace(picked))
            return new(DrawingSelectionStatus.Cancelled, null,
                "הבחירה בוטלה — השרטוט הפעיל וההגדרות הקודמות לא השתנו.", "cancelled");

        string raw = picked!.Trim().Trim('"');
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception ex)
        {
            return new(DrawingSelectionStatus.NotFound, null,
                "הנתיב שנבחר אינו תקין:\n" + raw + "\nבחרי את קובץ ה-DWG דרך חלון הבחירה.",
                $"invalid path: {ex.GetType().Name}");
        }

        if (!string.Equals(Path.GetExtension(full), DrawingExtension, StringComparison.OrdinalIgnoreCase))
            return new(DrawingSelectionStatus.NotADrawing, full,
                "הקובץ שנבחר אינו שרטוט DWG:\n" + Path.GetFileName(full) +
                "\nבחרי קובץ עם סיומת ‎.dwg (שרטוט הבין-ירוקים של הפרויקט).",
                $"not a .dwg: {full}");

        if (!fileExists(full))
            return new(DrawingSelectionStatus.NotFound, full,
                "קובץ השרטוט לא נמצא:\n" + full + "\nבדקי שהקובץ קיים ושיש לך גישה אליו.",
                $"missing: {full}");

        if (SamePath(full, activeDrawingPath))
            return new(DrawingSelectionStatus.AlreadyActive, full,
                "השרטוט הזה כבר פתוח ופעיל: " + Path.GetFileName(full) + "\nאפשר להמשיך ל-Setup.",
                $"already active: {full}");

        if (openDrawingPaths.Any(p => SamePath(full, p)))
            return new(DrawingSelectionStatus.ActivatedOpen, full,
                "עברנו לשרטוט הפתוח: " + Path.GetFileName(full) + "\n→ עכשיו Setup (בחירת קובץ Excel).",
                $"activate open: {full}");

        return new(DrawingSelectionStatus.Opened, full,
            "השרטוט נפתח: " + Path.GetFileName(full) + "\n→ עכשיו Setup (בחירת קובץ Excel).",
            $"open: {full}");
    }

    /// <summary>Palette label showing clearly which drawing the displayed state belongs to.</summary>
    public static string BoundDrawingLabelHe(string? boundDrawingPath)
        => string.IsNullOrWhiteSpace(boundDrawingPath)
            ? "שרטוט: (אין שרטוט פתוח) — לחצי 'שרטוט DWG…' כדי לבחור את שרטוט הפרויקט"
            : "שרטוט: " + Path.GetFileName(boundDrawingPath);

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a!.Trim()), Path.GetFullPath(b!.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string? SafeDirectoryName(string path)
    {
        try { return Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch { return null; }
    }
}

/// <summary>
/// The palette's "שרטוט DWG…" action as a testable unit: picker → decision → host action →
/// recent folder. Cancel and rejections make NO host call and NO store write, so the
/// bound drawing, workflow state and saved project are preserved by construction.
/// </summary>
public sealed class DrawingChooser
{
    private readonly IDrawingPicker _picker;
    private readonly IDrawingHost _host;
    private readonly IRecentFolderStore _recent;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly string _documentsFolder;

    public DrawingChooser(
        IDrawingPicker picker,
        IDrawingHost host,
        IRecentFolderStore recent,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        string? documentsFolder = null)
    {
        _picker = picker;
        _host = host;
        _recent = recent;
        _fileExists = fileExists ?? File.Exists;
        _directoryExists = directoryExists ?? Directory.Exists;
        _documentsFolder = documentsFolder
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public string InitialDirectory()
        => DrawingSelection.InitialDirectory(SafeLoadRecent(), _host.ActiveDrawingPath, _documentsFolder, _directoryExists);

    public DrawingSelectionResult Choose()
    {
        string initial = InitialDirectory();
        string? picked = _picker.PickDrawing(initial);
        var decision = DrawingSelection.Evaluate(picked, _host.ActiveDrawingPath, _host.OpenDrawingPaths, _fileExists);

        switch (decision.Status)
        {
            case DrawingSelectionStatus.ActivatedOpen:
                try { _host.Activate(decision.NormalizedPath!); }
                catch (Exception ex) { return OpenFailed(decision.NormalizedPath!, ex); }
                break;
            case DrawingSelectionStatus.Opened:
                try { _host.Open(decision.NormalizedPath!); }
                catch (Exception ex) { return OpenFailed(decision.NormalizedPath!, ex); }
                break;
        }

        if (decision.ChangedActiveDrawing || decision.Status == DrawingSelectionStatus.AlreadyActive)
        {
            string? dir = Path.GetDirectoryName(decision.NormalizedPath!);
            if (!string.IsNullOrEmpty(dir))
                try { _recent.Save(dir); } catch { /* remembering the folder is best-effort */ }
        }
        return decision;
    }

    private static DrawingSelectionResult OpenFailed(string path, Exception ex)
        => new(DrawingSelectionStatus.OpenFailed, path,
            "לא ניתן לפתוח את השרטוט:\n" + Path.GetFileName(path) +
            "\nייתכן שהקובץ נעול, פגום או פתוח בתוכנה אחרת. נסי לפתוח אותו ידנית ב-Civil 3D ואז הריצי INTERGREEN שוב.",
            $"open failed: {path}: {ex.GetType().Name}: {ex.Message}");

    private string? SafeLoadRecent()
    {
        try { return _recent.Load(); } catch { return null; }
    }
}

/// <summary>Per-user "last DWG folder" (LocalAppData\Mahod\MahodIntergreen\recent-dwg-folder.txt).
/// Best-effort: failures never block the picker.</summary>
public sealed class RecentDrawingFolderStore : IRecentFolderStore
{
    private readonly string _path;

    public RecentDrawingFolderStore(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mahod", "MahodIntergreen", "recent-dwg-folder.txt");

    public string FilePath => _path;

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            string s = File.ReadAllText(_path).Trim();
            return s.Length == 0 ? null : s;
        }
        catch { return null; }
    }

    public void Save(string folder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, folder);
        }
        catch { /* best-effort */ }
    }
}
