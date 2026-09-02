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

    /// <summary>The boundary end that is short of the stop line, in the engine's metres — so the palette can show the spot instead of a handle.</summary>
    public Mahod.Intergreen.Geometry.Point2D? NearEnd { get; init; }
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
    /// Off until the engineer turns it on. David asked for a threshold "the user defines (e.g. 10 cm)";
    /// a project that never set one keeps r12/r13 behaviour — every near-miss waits for a person — so
    /// no existing project changes its numbers by upgrading (ED-016, Codex review 2026-09-02).
    /// </summary>
    public const double DefaultAutoConfirmToleranceMeters = 0.0;

    /// <summary>
    /// David's own example — 10 cm — offered by the dialog as the value to type. Drafting imprecision
    /// of this size is invisible at drawing scale (Lin's two cases were 4.7 cm and 6.3 cm on 40–60 m
    /// lines); at 50 km/h it is 0.007 s, which can still cross a rounding boundary, so every application
    /// is logged with its gap.
    /// </summary>
    public const double SuggestedAutoConfirmToleranceMeters = 0.10;

    /// <summary>The project setting as the engine must see it: never negative, never above the ceiling.</summary>
    public static double Clamp(double toleranceMeters)
        => double.IsFinite(toleranceMeters) ? Math.Clamp(toleranceMeters, 0.0, MaxAutoConfirmToleranceMeters) : 0.0;

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

    /// <summary>Status clause for the whole tolerance pass: what was extended, what was confirmed beside the stop line's end.</summary>
    public static string ResolutionSummary(IReadOnlyList<ReferenceResolution> resolved, double toleranceMeters)
    {
        var extended = resolved.Where(r => r.Kind == ReferenceResolutionKind.Extended).Select(r => r.Issue.MovementId).Distinct(StringComparer.Ordinal).ToList();
        var confirmed = resolved.Where(r => r.Kind == ReferenceResolutionKind.Confirmed).Select(r => r.Issue.MovementId).Distinct(StringComparer.Ordinal).ToList();
        if (extended.Count == 0 && confirmed.Count == 0) return "";
        var cm = Math.Round(toleranceMeters * 100.0);
        var parts = new List<string>();
        if (extended.Count > 0)
            parts.Add($"{(extended.Count == 1 ? "קו גבול אחד" : extended.Count + " קווי גבול")} ({string.Join(", ", extended)}) הוארך וירטואלית עד קו העצירה");
        if (confirmed.Count > 0)
            parts.Add($"{(confirmed.Count == 1 ? "קו גבול אחד" : confirmed.Count + " קווי גבול")} ({string.Join(", ", confirmed)}) עובר לצד קצה קו העצירה ואושר בקצהו");
        return string.Join("; ", parts) + $" (סף {cm:F0} ס\"מ; השרטוט לא שונה).";
    }

    /// <summary>Status clause for boundaries the tolerance extended virtually — names the movements, says the DWG is untouched.</summary>
    public static string ExtendedSummary(IReadOnlyList<ReferenceIssue> extended, double toleranceMeters)
    {
        if (extended.Count == 0) return "";
        var names = string.Join(", ", extended.Select(i => i.MovementId).Distinct(StringComparer.Ordinal));
        var cm = Math.Round(toleranceMeters * 100.0);
        return extended.Count == 1
            ? $"קו גבול אחד ({names}) הוארך וירטואלית עד קו העצירה (סף {cm:F0} ס\"מ; השרטוט לא שונה)."
            : $"{extended.Count} קווי גבול ({names}) הוארכו וירטואלית עד קו העצירה (סף {cm:F0} ס\"מ; השרטוט לא שונה).";
    }

    /// <summary>Widest gap first — that is the one most likely to be a real drawing defect.</summary>
    public static IReadOnlyList<ReferenceIssue> Sorted(IEnumerable<ReferenceIssue> issues) =>
        issues.OrderByDescending(i => i.GapMeters)
              .ThenBy(i => i.CurveId, StringComparer.Ordinal)
              .ToList();

    public static string Line(ReferenceIssue issue) =>
        $"\u202A{issue.MovementId} — {issue.CurveId}\u202C: חסרים {issue.GapCentimetres:F1} ס\"מ עד קו העצירה " +
        $"(handle \u202A{issue.Handle}\u202C)";

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
        "הפערים קטנים מכדי להיראות בשרטוט — \"הצג קו קצר…\" בפלטה מתקרב לקצה ומסמן אותו; ה-handle של הקו מופיע כאן לתמיכה.\n\n" +
        "אישור פירושו: להשתמש בקצה הקו המשורטט כנקודת ההתחלה של המדידה, במקום בחיתוך עם קו העצירה.\n" +
        "האישור נשמר בפרויקט הזה בלבד ונרשם ביומן. אפשר במקום זאת לתקן את השרטוט ולהריץ שוב.";
}
