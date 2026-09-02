using System.Text;
using System.Text.Json;

namespace Mahod.Intergreen.Host;

public enum SidecarLoadStatus { NoSidecar, Ok, Recovered, }
public enum WorkbookRefStatus { Ok, NotConfigured, Missing, CandidateNextToDrawing }

public sealed record SidecarLoadResult(
    SidecarLoadStatus Status,
    Dictionary<string, JsonElement> Data,
    string? RecoveryBackupPath,
    string UserMessageHe);

public sealed record WorkbookRefResult(
    WorkbookRefStatus Status,
    string? Path,
    string UserMessageHe);

/// <summary>
/// Transactional project-sidecar persistence (hardening directive §7/§8).
/// - schemaVersion stamped on save; older/unversioned sidecars are migrated (stamp only —
///   no engineering values are ever invented).
/// - corrupt JSON never crashes: the broken file is preserved as *.corrupt-*.bak and the
///   user is told to run Setup again.
/// - saves are atomic (temp + File.Replace with .bak) — a failed Setup can never leave a
///   half-written sidecar, and committing is the CALLER's last step after validation.
/// - workbook reference resolution never silently substitutes a different file.
/// </summary>
public static class SidecarStore
{
    public const int SchemaVersion = 1;

    public static SidecarLoadResult Load(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
            return new(SidecarLoadStatus.NoSidecar, new(), null, "");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            var dict = new Dictionary<string, JsonElement>();
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.Clone();
            return new(SidecarLoadStatus.Ok, dict, null, "");
        }
        catch (Exception)
        {
            string backup = sidecarPath + ".corrupt-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            try { File.Copy(sidecarPath, backup, overwrite: true); } catch { backup = "(backup failed)"; }
            return new(SidecarLoadStatus.Recovered, new(), backup,
                "קובץ הגדרות הפרויקט (sidecar) נמצא פגום ולא נטען.\n" +
                "העותק הפגום נשמר בצד; הריצי Setup מחדש כדי להגדיר את הפרויקט (דקה אחת).");
        }
    }

    /// <summary>Atomic commit — the ONLY way project state is written.</summary>
    public static void Commit(string sidecarPath, Dictionary<string, JsonElement> data)
    {
        var payload = new Dictionary<string, object?>();
        foreach (var (k, v) in data) payload[k] = v;
        payload["schemaVersion"] = SchemaVersion;
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        string tmp = sidecarPath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        if (File.Exists(sidecarPath))
            File.Replace(tmp, sidecarPath, sidecarPath + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tmp, sidecarPath);
    }

    public static void Set(Dictionary<string, JsonElement> data, string key, string value)
    {
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(value));
        data[key] = d.RootElement.Clone();
    }

    /// <summary>
    /// Store a string array (confirmed endpoint references). Values are de-duplicated and ordered
    /// so a re-confirmation never rewrites the file with the same set in a different order.
    /// </summary>
    public static void SetStrings(Dictionary<string, JsonElement> data, string key, IEnumerable<string> values)
    {
        var ordered = values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(ordered));
        data[key] = d.RootElement.Clone();
    }

    /// <summary>Store a numeric project setting (e.g. a tolerance in metres).</summary>
    public static void SetDouble(Dictionary<string, JsonElement> data, string key, double value)
    {
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(value));
        data[key] = d.RootElement.Clone();
    }

    /// <summary>Read a numeric project setting; missing or malformed values yield null, never a guess.</summary>
    public static double? GetDouble(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v)) return v;
        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    /// <summary>Read back a string array; missing or malformed values yield an empty set.</summary>
    public static IReadOnlyList<string> GetStrings(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return el.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    /// <summary>
    /// Resolve the stored workbook reference. Legacy/bad-build values (quoted, relative)
    /// are normalized through the central resolver. If the stored file is gone but a file
    /// with the SAME name sits next to the drawing, it is offered as a CANDIDATE — never
    /// silently used (directive §8: no silent use of a wrong workbook).
    /// </summary>
    public static WorkbookRefResult ResolveWorkbook(Dictionary<string, JsonElement> data, string drawingDir)
    {
        if (!data.TryGetValue("workbook", out var el) || el.ValueKind != JsonValueKind.String)
            return new(WorkbookRefStatus.NotConfigured, null,
                "עדיין לא הוגדר קובץ Excel לפרויקט — הריצי Setup.");
        string raw = el.GetString() ?? "";
        // relative paths (legacy) resolve against the drawing folder
        string candidateRaw = raw.Trim().Trim('"');
        if (!Path.IsPathRooted(candidateRaw) && candidateRaw.Length > 0)
            candidateRaw = Path.Combine(drawingDir, candidateRaw);
        var r = WorkbookPathResolver.Resolve(candidateRaw);
        if (r.IsOk)
            return new(WorkbookRefStatus.Ok, r.NormalizedPath, "");

        // same filename next to the drawing? offer, never auto-use
        string name = "";
        try { name = Path.GetFileName(candidateRaw); } catch { }
        if (!string.IsNullOrEmpty(name))
        {
            string near = Path.Combine(drawingDir, name);
            if (File.Exists(near) && WorkbookPathResolver.Resolve(near).IsOk)
                return new(WorkbookRefStatus.CandidateNextToDrawing, near,
                    "קובץ ה-Excel השמור בפרויקט לא נמצא במקומו המקורי, אך קובץ באותו שם קיים ליד השרטוט:\n" +
                    near + "\nהריצי Setup ובחרי אותו במפורש כדי לאשר שזה הקובץ הנכון.");
        }
        return new(WorkbookRefStatus.Missing, r.NormalizedPath,
            "קובץ ה-Excel השמור בפרויקט לא נמצא.\n" + r.UserMessageHe);
    }
}
