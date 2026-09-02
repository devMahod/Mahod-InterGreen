using System.Text;

namespace Mahod.Intergreen.Host;

/// <summary>
/// One movement boundary whose measurement origin could only be resolved by falling back to a
/// drawn endpoint: the polyline stops short of its stop line instead of crossing it. Directive §14
/// forbids the engine from adopting such an endpoint by itself, so the engineer confirms it per
/// boundary. <see cref="CurveId"/> is the key the engine reads back from the sidecar; the DWG
/// <see cref="Handle"/> and the gap exist so the engineer can find the line in the drawing —
/// the gaps are far too small to see at drawing scale.
/// </summary>
public sealed record ReferenceIssue(string MovementId, string CurveId, string Handle, double GapMeters)
{
    public double GapCentimetres => GapMeters * 100.0;
}

/// <summary>
/// Presentation and persistence around <see cref="ReferenceIssue"/>. Contains no geometry and no
/// engineering decision: the caller asks the engine's own reference resolver which boundaries fell
/// back to an endpoint, and this type only orders, phrases and stores the answer.
/// </summary>
public static class ReferenceReview
{
    /// <summary>Sidecar key read by the engine (ProjectSidecar.ConfirmedEndpointReferences).</summary>
    public const string SidecarKey = "confirmedEndpointReferences";

    /// <summary>
    /// Provenance: the subset of <see cref="SidecarKey"/> that was confirmed automatically because the
    /// gap fell under the project tolerance (ED-016, David 2026-08-27 item 6b). Kept separately so a
    /// reviewer can always tell an engineer's decision from a tolerance rule.
    /// </summary>
    public const string AutoConfirmedSidecarKey = "autoConfirmedEndpointReferences";

    /// <summary>Project setting: gap [m] under which a near-miss reference is confirmed automatically.</summary>
    public const string ToleranceSidecarKey = "autoConfirmEndpointToleranceMeters";

    /// <summary>
    /// David's own suggested threshold — 10 cm. Drafting imprecision of this size is invisible at
    /// drawing scale (Lin's two cases were 4.7 cm and 6.3 cm on 40–60 m lines) and cannot change an
    /// intergreen: at 50 km/h, 10 cm is 0.007 s. Anything above still needs the engineer.
    /// </summary>
    public const double DefaultAutoConfirmToleranceMeters = 0.10;

    /// <summary>Hard ceiling for the project setting, so a typo cannot silently wave through a real gap.</summary>
    public const double MaxAutoConfirmToleranceMeters = 0.50;

    /// <summary>Split pending issues into those the tolerance confirms and those that still need a person.</summary>
    public static (IReadOnlyList<ReferenceIssue> Auto, IReadOnlyList<ReferenceIssue> Pending) Partition(
        IEnumerable<ReferenceIssue> issues, double toleranceMeters)
    {
        var tol = Math.Clamp(toleranceMeters, 0.0, MaxAutoConfirmToleranceMeters);
        var auto = new List<ReferenceIssue>();
        var pending = new List<ReferenceIssue>();
        foreach (var i in Sorted(issues))
            (i.GapMeters <= tol ? auto : pending).Add(i);
        return (auto, pending);
    }

    /// <summary>Status clause for what the tolerance took care of — names the movements, like everything else here.</summary>
    public static string AutoConfirmedSummary(IReadOnlyList<ReferenceIssue> auto, double toleranceMeters)
    {
        if (auto.Count == 0) return "";
        var names = string.Join(", ", auto.Select(i => i.MovementId).Distinct(StringComparer.Ordinal));
        var cm = Math.Round(toleranceMeters * 100.0);
        return auto.Count == 1
            ? $"קו גבול אחד ({names}) אושר אוטומטית — פער עד {cm:F0} ס\"מ מקו העצירה."
            : $"{auto.Count} קווי גבול ({names}) אושרו אוטומטית — פער עד {cm:F0} ס\"מ מקו העצירה.";
    }

    /// <summary>Widest gap first — that is the one most likely to be a real drawing defect.</summary>
    public static IReadOnlyList<ReferenceIssue> Sorted(IEnumerable<ReferenceIssue> issues) =>
        issues.OrderByDescending(i => i.GapMeters)
              .ThenBy(i => i.CurveId, StringComparer.Ordinal)
              .ToList();

    public static string Line(ReferenceIssue issue) =>
        $"{issue.MovementId} — {issue.CurveId} (handle {issue.Handle}): " +
        $"חסרים {issue.GapCentimetres:F1} ס\"מ עד קו העצירה";

    /// <summary>
    /// The palette status line. Deliberately names the movements: "N errors" tells the engineer
    /// nothing she can act on, and this condition is invisible in the drawing.
    /// </summary>
    public static string HebrewSummary(IReadOnlyList<ReferenceIssue> issues)
    {
        if (issues.Count == 0) return "";
        var names = string.Join(", ", Sorted(issues).Select(i => i.MovementId).Distinct(StringComparer.Ordinal));
        var sb = new StringBuilder();
        sb.Append(issues.Count == 1
            ? "קו גבול אחד אינו מגיע עד קו העצירה"
            : $"{issues.Count} קווי גבול אינם מגיעים עד קו העצירה");
        sb.Append($" ({names}). ");
        sb.Append("כל הקונפליקטים של התנועות האלה לא יחושבו עד שהמרחקים יאושרו או שהשרטוט יתוקן.");
        return sb.ToString();
    }

    /// <summary>Body of the confirmation dialog — states what confirming means, in engineering terms.</summary>
    public static string DialogExplanation(IReadOnlyList<ReferenceIssue> issues) =>
        "הקווים הבאים אינם חותכים את קו העצירה שלהם, ולכן אי אפשר למדוד מהם מרחק פינוי.\n" +
        "הפערים קטנים מכדי להיראות בשרטוט — הם מופיעים כאן בסנטימטרים עם ה-handle של הקו.\n\n" +
        "אישור פירושו: להשתמש בקצה הקו המשורטט כנקודת ההתחלה של המדידה, במקום בחיתוך עם קו העצירה.\n" +
        "האישור נשמר בפרויקט הזה בלבד ונרשם ביומן. אפשר במקום זאת לתקן את השרטוט ולהריץ שוב.";
}
