using Mahod.Intergreen.Excel;

namespace Mahod.Intergreen.Host;

public enum WorkbookAcceptStatus
{
    Ok,
    PathRejected,
    UnsupportedTemplate,
    UnreadableWorkbook,
}

public sealed record WorkbookAcceptResult(
    WorkbookAcceptStatus Status,
    string? NormalizedPath,
    WorkbookModel? Model,
    string UserMessageHe,
    string Detail)
{
    public bool IsOk => Status == WorkbookAcceptStatus.Ok;
}

/// <summary>
/// Setup-time workbook acceptance: path resolution + enough structural validation
/// (via the shared reader — no engineering logic here) BEFORE any project state is
/// committed (hardening directive §6/§7).
/// </summary>
public static class WorkbookAcceptance
{
    public static WorkbookAcceptResult Validate(string? rawPath)
    {
        var p = WorkbookPathResolver.Resolve(rawPath);
        if (!p.IsOk)
            return new(WorkbookAcceptStatus.PathRejected, p.NormalizedPath, null, p.UserMessageHe, p.Detail);
        try
        {
            var model = WorkbookReader.Read(p.NormalizedPath!);
            return new(WorkbookAcceptStatus.Ok, p.NormalizedPath, model, "", "ok");
        }
        catch (Exception ex)
        {
            // unreadable = the container itself cannot be opened (corrupt zip/IO);
            // anything the reader failed to INTERPRET (missing sheet/range/argument)
            // is a template/structure problem.
            bool unreadable = ex is System.IO.InvalidDataException or IOException
                || (ex.GetType().FullName ?? "").StartsWith("DocumentFormat", StringComparison.Ordinal)
                || ex.GetType().Name.Contains("FileFormat", StringComparison.Ordinal);
            bool structural = !unreadable;
            return new(
                structural ? WorkbookAcceptStatus.UnsupportedTemplate : WorkbookAcceptStatus.UnreadableWorkbook,
                p.NormalizedPath, null,
                structural
                    ? "UNSUPPORTED_TEMPLATE_VERSION — הקובץ נפתח אך אינו תבנית IG_matrix מוכרת.\n" +
                      "ודאי שנבחר קובץ ה-IG_matrix של הפרויקט (עם גיליונות Parameters / Input Distances / Signal group key).\n" +
                      "פרט טכני: " + ex.Message
                    : "לא ניתן לקרוא את חוברת ה-Excel שנבחרה (ייתכן שהיא פגומה).\n" +
                      "נסי לפתוח אותה ב-Excel; אם היא נפתחת שם — שלחי לארתור את קובץ הלוג (Export Support Log).\n" +
                      "פרט טכני: " + ex.GetType().Name,
                ex.GetType().Name + ": " + ex.Message);
        }
    }
}
