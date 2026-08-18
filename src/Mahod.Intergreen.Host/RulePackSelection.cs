using System.Text.Json;

namespace Mahod.Intergreen.Host;

/// <summary>A rule pack found installed under the bundle's rules/ directory.</summary>
public sealed record InstalledRulePack(string Id, string Version, string DisplayName, string Directory);

/// <summary>
/// Versioned-guideline support (r8). The engineering DLLs interpret whichever rule pack
/// they are handed; WHICH pack a project uses is project state, owned here:
///  - packs are discovered from the installed bundle (rules/&lt;id&gt;/manifest.json);
///  - a project's chosen pack persists in the sidecar (key "rulePackId");
///  - a project with no stored choice stays on the validated legacy default — existing
///    projects are NEVER silently migrated to a newer guideline version;
///  - changing a configured project's pack is a deliberate engineer action that the UI
///    must confirm (ChangeRequiresWarning) and that invalidates prior results.
/// A future guideline update therefore ships as a new rules/&lt;id&gt;/ folder in the
/// bundle — no engineering DLL rebuild, no silent behavior change for old projects.
/// </summary>
public static class RulePackSelection
{
    public const string DefaultPackId = "legacy-mahod-v1";
    public const string SidecarKey = "rulePackId";
    public const string SidecarByKey = "rulePackSelectedBy";

    /// <summary>Discover installed packs. Unreadable/malformed entries are skipped —
    /// discovery must never take the palette down.</summary>
    public static List<InstalledRulePack> ListInstalled(string rulesRoot)
    {
        var found = new List<InstalledRulePack>();
        if (!System.IO.Directory.Exists(rulesRoot)) return found;
        foreach (var dir in System.IO.Directory.EnumerateDirectories(rulesRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var manifest = System.IO.Path.Combine(dir, "manifest.json");
            if (!System.IO.File.Exists(manifest)) continue;
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
                var root = doc.RootElement;
                string? id = root.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;
                string version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "?" : "?";
                string display = root.TryGetProperty("displayName", out var d) ? d.GetString() ?? id! : id!;
                found.Add(new InstalledRulePack(id!, version, display, dir));
            }
            catch { /* skip malformed pack, keep the rest */ }
        }
        return found;
    }

    /// <summary>The pack this project uses: the sidecar's stored choice, else the
    /// validated legacy default (no silent migration).</summary>
    public static string ProjectPackId(IReadOnlyDictionary<string, JsonElement> sidecarData)
        => sidecarData.TryGetValue(SidecarKey, out var el) && el.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()!
            : DefaultPackId;

    /// <summary>True when the engineer is changing an effective choice (including moving
    /// off the implicit default) — the UI must warn and re-run analysis.</summary>
    public static bool ChangeRequiresWarning(string currentEffectiveId, string newId)
        => !string.Equals(currentEffectiveId, newId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Persist a deliberate selection into existing sidecar data (caller commits
    /// via SidecarStore.Commit so the write stays transactional).</summary>
    public static void ApplySelection(Dictionary<string, JsonElement> sidecarData, string packId, string selectedBy)
    {
        SidecarStore.Set(sidecarData, SidecarKey, packId);
        SidecarStore.Set(sidecarData, SidecarByKey, selectedBy);
    }
}
