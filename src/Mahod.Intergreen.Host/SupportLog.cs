using System.Text;

namespace Mahod.Intergreen.Host;

/// <summary>
/// Privacy-safe support logging (hardening directive §11). Technical detail lives HERE,
/// not in user dialogs. Logs contain: versions, session id, state transitions, normalized
/// file paths, structured codes, exception type/message — no drawing content, no prompts.
/// Location: %LOCALAPPDATA%\Mahod\MahodIntergreen\logs\intergreen-YYYYMMDD.log
/// </summary>
public static class SupportLog
{
    private static readonly object Lock = new();
    private static string? _sessionId;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mahod", "MahodIntergreen", "logs");

    public static string CurrentLogPath =>
        Path.Combine(LogDirectory, $"intergreen-{DateTime.Now:yyyyMMdd}.log");

    public static void Start(string pluginVersion, string hostVersion)
    {
        _sessionId = Guid.NewGuid().ToString("N")[..12];
        Write("SESSION_START", $"plugin={pluginVersion} host={hostVersion}");
    }

    public static void Write(string code, string detail)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{_sessionId ?? "-"}] {code} | {detail}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch { /* logging must never break the product */ }
    }

    public static void Error(string code, Exception ex)
        => Write(code, $"{ex.GetType().Name}: {ex.Message}");

    /// <summary>Copies today's log next to the given folder for sending to support.</summary>
    public static string? ExportTo(string destinationDir)
    {
        try
        {
            if (!File.Exists(CurrentLogPath)) return null;
            Directory.CreateDirectory(destinationDir);
            string dest = Path.Combine(destinationDir,
                $"MahodIntergreen_SupportLog_{DateTime.Now:yyyyMMdd-HHmm}.log");
            File.Copy(CurrentLogPath, dest, overwrite: true);
            return dest;
        }
        catch { return null; }
    }
}
