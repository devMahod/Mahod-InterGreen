namespace Mahod.Intergreen.Host;

public enum ExportPlanStatus { Ok, NeedsConfirmOverwrite, RefusedSourceEqualsDestination, DestinationError }

public sealed record ExportPlan(
    ExportPlanStatus Status,
    string? DestinationPath,
    string? UniqueAlternativePath,
    string UserMessageHe);

/// <summary>
/// Export destination policy (hardening directive §12): never the source workbook, never a
/// silent overwrite, destination folder created when missing, unique-name fallback offered.
/// </summary>
public static class ExportPlanner
{
    public static ExportPlan Plan(string sourceWorkbookPath, string destinationDir, string? baseName = null)
    {
        string name = (baseName ?? Path.GetFileNameWithoutExtension(sourceWorkbookPath)) + "_MAHOD_INTERGREEN.xlsx";
        string dest;
        try
        {
            Directory.CreateDirectory(destinationDir);
            dest = Path.GetFullPath(Path.Combine(destinationDir, name));
        }
        catch (Exception ex)
        {
            return new(ExportPlanStatus.DestinationError, null, null,
                "לא ניתן להכין את תיקיית היעד לייצוא.\n" +
                $"תיקייה: {destinationDir}\nבדקי הרשאות כתיבה או בחרי מיקום אחר.\nפרט טכני: {ex.GetType().Name}");
        }

        if (string.Equals(dest, Path.GetFullPath(sourceWorkbookPath), StringComparison.OrdinalIgnoreCase))
            return new(ExportPlanStatus.RefusedSourceEqualsDestination, dest, null,
                "יעד הייצוא זהה לקובץ המקור — הייצוא בוטל כדי להגן על קובץ המקור.\n" +
                "קובץ המקור לעולם אינו נדרס; בחרי שם/תיקייה אחרים.");

        if (File.Exists(dest))
        {
            string unique = UniquePath(dest);
            return new(ExportPlanStatus.NeedsConfirmOverwrite, dest, unique,
                $"קובץ ייצוא בשם הזה כבר קיים:\n{Path.GetFileName(dest)}\n" +
                $"אישור = החלפה של קובץ הייצוא הקודם; ביטול = שמירה בשם חדש ({Path.GetFileName(unique)}).");
        }
        return new(ExportPlanStatus.Ok, dest, null, "");
    }

    public static string UniquePath(string dest)
    {
        string dir = Path.GetDirectoryName(dest)!;
        string stem = Path.GetFileNameWithoutExtension(dest);
        string ext = Path.GetExtension(dest);
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!File.Exists(p)) return p;
        }
    }
}
