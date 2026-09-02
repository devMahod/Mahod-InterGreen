namespace Mahod.Intergreen.Host;

/// <summary>
/// Deterministic runtime resource resolution (r7 fix). Inside a real Autodesk process the
/// ambient state (CurrentDirectory, AppContext.BaseDirectory) belongs to Autodesk, NOT to
/// the installed Intergreen bundle — resolving anything from it is a bug class:
/// Arthur's Civil 3D 2027 GUI hit DirectoryNotFoundException("rules/ not found above
/// C:\Program Files\Autodesk\AutoCAD 2027\") at Analyze. Every runtime asset must be
/// resolved relative to an EXPLICIT anchor: the directory of the installed plugin
/// assembly. No CWD, no upward search.
/// </summary>
public static class RuntimeRoots
{
    /// <summary>rules/ next to the given anchor directory (the installed plugin folder).
    /// Throws with an actionable message naming the exact checked path — never searches
    /// parents and never consults the current directory.</summary>
    public static string RulesRoot(string anchorDirectory)
    {
        string candidate = Path.Combine(anchorDirectory, "rules");
        if (File.Exists(Path.Combine(candidate, "legacy-mahod-v1", "manifest.json")))
            return candidate;
        throw new DirectoryNotFoundException(
            "תיקיית החוקים (rules) לא נמצאה בהתקנה: " + candidate + Environment.NewLine +
            "נראה שההתקנה פגומה — הריצו את המתקין של Mahod Intergreen מחדש.");
    }

    /// <summary>The blank IG_matrix template shipped in the bundle (templates/ next to the anchor).
    /// r14 "new project": the workbook is created from the client's own template, never invented.</summary>
    public const string BlankTemplateFileName = "IG_matrix_template.xlsx";

    public static string BlankTemplatePath(string anchorDirectory)
    {
        string candidate = Path.Combine(anchorDirectory, "templates", BlankTemplateFileName);
        if (File.Exists(candidate))
            return candidate;
        throw new FileNotFoundException(
            "תבנית ה-Excel (templates) לא נמצאה בהתקנה: " + candidate + Environment.NewLine +
            "נראה שההתקנה פגומה — הריצו את המתקין של Mahod Intergreen מחדש.", candidate);
    }

    /// <summary>SHA-256 of a file that may be OPEN AND LOCKED by the host application
    /// (the active DWG inside AutoCAD/Civil denies default sharing). Opens with full
    /// share flags so hashing never depends on how the host holds its own file.</summary>
    public static string Sha256OfOpenFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    /// <summary>Fallback output directory for an unsaved/unnamed drawing — a fixed
    /// per-user location, never the process CurrentDirectory.</summary>
    public static string FallbackOutputDir()
    {
        string p = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mahod", "MahodIntergreen", "output");
        Directory.CreateDirectory(p);
        return p;
    }
}
