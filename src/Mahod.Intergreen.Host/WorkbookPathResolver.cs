namespace Mahod.Intergreen.Host;

public enum WorkbookPathStatus
{
    Ok,
    Empty,
    InvalidPath,
    FolderNotFound,
    NotFound,
    PathIsDirectory,
    UnsupportedExtension,
    EmptyFile,
    MalformedWorkbook,
    AccessDenied,
    Locked,
    CloudUnavailable,
}

public sealed record WorkbookPathResult(
    WorkbookPathStatus Status,
    string? NormalizedPath,
    string UserMessageHe,
    string Detail)
{
    public bool IsOk => Status == WorkbookPathStatus.Ok;
}

/// <summary>
/// The single path/workbook input service (hardening directive §3). Every workbook path —
/// picker result, pasted string, sidecar value — goes through here. It never rewrites the
/// user's chosen filename into a different file: only surrounding whitespace and ONE
/// legitimate surrounding quote pair are removed.
/// </summary>
public static class WorkbookPathResolver
{
    public static readonly string[] SupportedExtensions = { ".xlsx" };

    // Windows cloud-placeholder attributes (OneDrive/SharePoint files-on-demand).
    private const int RecallOnOpen = 0x00040000;
    private const int RecallOnDataAccess = 0x00400000;
    private const int Offline = 0x00001000;

    public static WorkbookPathResult Resolve(string? raw, bool probeRead = true)
    {
        string s = (raw ?? string.Empty).Trim();
        // exactly one legitimate surrounding quote pair ("..." or '...') — never inner quotes
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
        {
            s = s[1..^1].Trim();
        }
        if (string.IsNullOrWhiteSpace(s))
            return Fail(WorkbookPathStatus.Empty, null,
                "לא נבחר קובץ Excel.",
                "empty input");

        string full;
        try
        {
            full = Path.GetFullPath(s);
        }
        catch (Exception ex)
        {
            return Fail(WorkbookPathStatus.InvalidPath, null,
                "הנתיב שנבחר אינו נתיב Windows תקין.\nבחרי את הקובץ מחדש דרך כפתור Setup.",
                ex.GetType().Name + ": " + ex.Message);
        }

        if (Directory.Exists(full))
            return Fail(WorkbookPathStatus.PathIsDirectory, full,
                "הנתיב שנבחר הוא תיקייה, לא קובץ Excel.\nבחרי את קובץ ה-IG_matrix עצמו (‎.xlsx).",
                "path is a directory");

        string ext = Path.GetExtension(full);
        if (!SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return Fail(WorkbookPathStatus.UnsupportedExtension, full,
                $"סוג הקובץ '{ext}' אינו נתמך.\nיש לבחור חוברת Excel מסוג ‎.xlsx (קובץ ה-IG_matrix של הפרויקט).",
                "unsupported extension " + ext);

        if (!File.Exists(full))
        {
            string? dir = Path.GetDirectoryName(full);
            bool folderMissing = dir is not null && !Directory.Exists(dir);
            return Fail(folderMissing ? WorkbookPathStatus.FolderNotFound : WorkbookPathStatus.NotFound, full,
                "קובץ ה-Excel שנבחר לא נמצא.\n" +
                $"נתיב: {full}\n" +
                (folderMissing ? "התיקייה בנתיב אינה קיימת. " : "") +
                "בדקי שהקובץ קיים (אולי הוזז או נמחק) או בחרי קובץ אחר דרך Setup.",
                folderMissing ? "folder not found" : "file not found");
        }

        FileInfo fi;
        try { fi = new FileInfo(full); }
        catch (Exception ex)
        {
            return Fail(WorkbookPathStatus.InvalidPath, full,
                "לא ניתן לקרוא את פרטי הקובץ שנבחר.\nבחרי את הקובץ מחדש דרך Setup.",
                ex.GetType().Name + ": " + ex.Message);
        }

        int attrs = (int)fi.Attributes;
        if ((attrs & (RecallOnOpen | RecallOnDataAccess | Offline)) != 0)
            return Fail(WorkbookPathStatus.CloudUnavailable, full,
                "הקובץ שנבחר הוא קובץ ענן (OneDrive/SharePoint) שאינו זמין במחשב כרגע.\n" +
                "פתחי את התיקייה, ודאי שהקובץ ירד למחשב (סימון ✓ ירוק), ונסי שוב.",
                "cloud placeholder attributes: 0x" + attrs.ToString("X"));

        if (fi.Length == 0)
            return Fail(WorkbookPathStatus.EmptyFile, full,
                "קובץ ה-Excel שנבחר ריק (0 בתים) ואינו חוברת תקינה.\nבחרי את קובץ ה-IG_matrix המקורי.",
                "zero-byte file");

        if (probeRead)
        {
            try
            {
                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Span<byte> head = stackalloc byte[2];
                int n = fs.Read(head);
                if (n < 2 || head[0] != (byte)'P' || head[1] != (byte)'K')
                    return Fail(WorkbookPathStatus.MalformedWorkbook, full,
                        "הקובץ שנבחר אינו חוברת Excel תקינה (ייתכן שהוא פגום או ששמו שונה מקובץ אחר).\n" +
                        "בחרי את קובץ ה-IG_matrix המקורי של הפרויקט.",
                        "missing OOXML PK header");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail(WorkbookPathStatus.AccessDenied, full,
                    "אין הרשאת קריאה לקובץ שנבחר.\n" +
                    $"נתיב: {full}\nבדקי הרשאות או העתיקי את הקובץ לתיקייה רגילה ונסי שוב.",
                    ex.Message);
            }
            catch (IOException ex)
            {
                return Fail(WorkbookPathStatus.Locked, full,
                    "לא ניתן לפתוח את הקובץ — ייתכן שהוא נעול על ידי תוכנית אחרת.\n" +
                    "אם הקובץ פתוח ב-Excel אפשר להשאיר אותו פתוח לקריאה, אך אם החיבור נכשל — סגרי אותו ונסי שוב.",
                    ex.Message);
            }
        }

        return new WorkbookPathResult(WorkbookPathStatus.Ok, full, "", "ok");
    }

    private static WorkbookPathResult Fail(WorkbookPathStatus st, string? path, string he, string detail)
        => new(st, path, he, detail);
}
