using System.Collections.Generic;
using System.IO;
using Mahod.Intergreen.Host;
using MahodAI.Civil3D.Plugin.Utilities;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// r15 (2026-10-06): Mahod Impact usage records for Mahod Intergreen, through
/// <see cref="MahodUsage"/> (Usage/MahodUsage.cs — a byte-for-byte copy of the MahodAI plugin's
/// Utilities/MahodUsage.cs; contract docs/telemetry.md, "Desktop products", in
/// devMahod/mahod-imapct). The SAME contract as the Intergreen that ships inside MahodAI
/// (plugin Ported/Mahod.Intergreen.MahodAI/IntergreenUsage.cs, commit 02d1601): tool id, action
/// names, priced feature and unit key are identical, so Impact counts both copies as one tool.
/// </summary>
/// <remarks>
/// <para>
/// The catalog prices Intergreen by the junction whose full intergreen matrix was exported
/// (feature <c>junction_intergreen</c>, unit "צומת שחושב"), so the unit is recorded where an
/// export passed its structural check — <c>IgWorkflowCommands.ExportExcel</c>, the palette's
/// "5. Export Excel". It is keyed by the drawing's fingerprint and the junction's workbook name
/// (<see cref="JunctionKey"/>), hashed on the PC: exporting the same junction again in a month
/// is priced once.
/// </para>
/// <para>
/// Actions: every palette button runs through <c>IgWorkflowCommands.Guard</c> and is recorded
/// there (door <c>palette</c>); the typed commands INTERGREEN, IG_CLEAR_QA, IG_SCAN and
/// IG_EXPORT_GEOMETRY record themselves (door <c>command</c>). A button refused by its workflow
/// gate ran nothing and records nothing; document activation passes no label and records
/// nothing; the IG_SMOKE_* harness commands are not counted. Status is completed or failed only.
/// </para>
/// <para>
/// This file is Autodesk-free (the test project compiles it); the one call that needs a
/// drawing is in IntergreenUsage.Drawing.cs.
/// </para>
/// </remarks>
internal static partial class IntergreenUsage
{
    internal const string Tool = "intergreen";

    internal const string Feature = "junction_intergreen";

    // Palette buttons without a workflow gate (and the two Setup buttons), by their label.
    private static readonly Dictionary<string, string> Buttons = new()
    {
        ["Excel חדש מהשרטוט…"] = "new_project_from_drawing",
        ["מתקדם: נתיב Excel ידני…"] = "setup_manual_path",
        ["נקודות ייחוס…"] = "confirm_references",
        ["סף הארכה אוטומטית…"] = "auto_extend_threshold",
        ["הארך בשרטוט…"] = "extend_in_drawing",
        ["הצג קו קצר…"] = "show_short_boundary",
        ["בחירת חוקים…"] = "choose_rules",
        ["Export Support Log"] = "support_log",
    };

    /// <summary>The action name of a palette button: its known label, else its workflow step.</summary>
    internal static string ButtonAction(WorkflowAction? gate, string? label)
    {
        if (label != null && Buttons.TryGetValue(label, out var name)) return "intergreen_" + name;
        return MahodUsage.Name("intergreen_" + (gate?.ToString() ?? "palette"));
    }

    /// <summary>One palette button that ran (completed or failed).</summary>
    public static void Button(WorkflowAction? gate, string? label, bool ok, long elapsedMs)
    {
        MahodUsage.Action(Tool, ButtonAction(gate, label), ok ? MahodUsage.Completed : MahodUsage.Failed,
            elapsedMs, MahodUsage.DoorOr(MahodUsage.Palette));
    }

    /// <summary>One typed command (INTERGREEN, IG_CLEAR_QA, IG_SCAN, IG_EXPORT_GEOMETRY).</summary>
    public static void Command(string command, bool ok, long elapsedMs = 0)
    {
        MahodUsage.Action(Tool, MahodUsage.Name(command), ok ? MahodUsage.Completed : MahodUsage.Failed,
            elapsedMs, MahodUsage.Command);
    }

    /// <summary>
    /// What makes an exported junction THIS junction (hashed by <see cref="MahodUsage.Unit"/>,
    /// never sent): the drawing's FingerprintGuid + "|junction:" + the workbook's file name
    /// without extension, lower-case — the in-tree MahodAI scheme, character for character.
    /// </summary>
    internal static string JunctionKey(string drawingFingerprint, string? workbookPath)
    {
        string junction = Path.GetFileNameWithoutExtension(workbookPath ?? string.Empty).ToLowerInvariant();
        return drawingFingerprint + "|junction:" + junction;
    }

    /// <summary>A junction's intergreen matrix was exported: one priced unit.</summary>
    internal static void Junction(string drawingFingerprint, string? workbookPath)
    {
        MahodUsage.Unit(Tool, Feature, JunctionKey(drawingFingerprint, workbookPath), MahodUsage.DoorOr(MahodUsage.Palette));
    }
}
