using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace MahodIntergreenSetup;

/// <summary>
/// Mahod Intergreen — per-user installer for the validated AutoCAD 2026 plugin bundle.
/// Packaging layer only: the embedded payload is the byte-identical validated bundle.
/// Modes: (default) GUI install · /uninstall · /silent (works with both).
/// Exit codes: 0 OK, 1 failure, 2 cancelled.
/// </summary>
internal static class Program
{
    internal const string ProductName = "Mahod Intergreen";
    internal const string EngineVersion = "0.1.0";
    internal const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MahodIntergreen";

    internal static string PluginsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "ApplicationPlugins");

    internal static string BundleDir => Path.Combine(PluginsDir, "Mahod.Intergreen.bundle");

    internal static string UninstallerDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mahod", "MahodIntergreen");

    internal static string UninstallerExe => Path.Combine(UninstallerDir, "Uninstall_Mahod_Intergreen.exe");

    [STAThread]
    private static int Main(string[] args)
    {
        bool silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                                 || a.Equals("/S", StringComparison.Ordinal));
        bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        // Automated file-level validation only (a hung AutoCAD would otherwise block the
        // whole cycle). Never documented for end users.
        SkipAutoCadCheck = args.Any(a => a.Equals("/skipacadcheck", StringComparison.OrdinalIgnoreCase));

        if (silent)
        {
            try
            {
                if (uninstall) Uninstall();
                else Install(null);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "MahodIntergreenSetup.error.log"),
                        ex.ToString());
                }
                catch { /* best effort */ }
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        if (uninstall)
        {
            var answer = MessageBox.Show(
                "להסיר את Mahod Intergreen מ-AutoCAD 2026?\n\n" +
                "יוסר רק תוסף Mahod Intergreen. שרטוטים, קובצי פרויקט (sidecar) וקובצי Excel לא יימחקו.",
                ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer != DialogResult.Yes) return 2;
            try
            {
                Uninstall();
                MessageBox.Show("Mahod Intergreen הוסר בהצלחה.", ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("ההסרה נכשלה:\n" + ex.Message, ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                return 1;
            }
        }

        Application.Run(new InstallerForm());
        return InstallerForm.ExitCode;
    }

    internal static bool AutoCad2026Detected()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD\R25.1");
            if (key is not null && key.GetSubKeyNames().Length > 0) return true;
        }
        catch { /* registry read best-effort */ }
        return File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Autodesk", "AutoCAD 2026", "acad.exe"));
    }

    internal static bool SkipAutoCadCheck;

    /// <summary>AutoCAD keeps the plugin DLL loaded; install/uninstall must not run under it.</summary>
    internal static bool AutoCadRunning() =>
        !SkipAutoCadCheck &&
        (Process.GetProcessesByName("acad").Length > 0 ||
         Process.GetProcessesByName("accoreconsole").Length > 0);

    internal static void Install(Action<string>? progress)
    {
        if (AutoCadRunning())
            throw new InvalidOperationException(
                "AutoCAD פתוח במחשב זה. סגרו את AutoCAD (כולל חלונות שקרסו) והריצו את ההתקנה שוב.");
        progress?.Invoke("מכין את תיקיית התוספים…");
        Directory.CreateDirectory(PluginsDir);
        if (Directory.Exists(BundleDir))
        {
            // Probe EVERY existing file for exclusive access BEFORE deleting anything —
            // a locked old installation must abort cleanly, never leave a half-removed
            // bundle (user-journey hardening §14).
            string? lockedFile = ProbeForLockedFile(BundleDir);
            if (lockedFile is not null)
                throw new InvalidOperationException(
                    "לא ניתן להחליף את ההתקנה הקודמת — קובץ מתוכה נעול על ידי תוכנית אחרת:\n" +
                    lockedFile + "\n" +
                    "סגרי את AutoCAD (כולל חלונות שקרסו) ונסי שוב. ההתקנה הקיימת לא נפגעה.");
            progress?.Invoke("מסיר גרסה קודמת של התוסף…");
            DeleteDirectoryWithRetry(BundleDir);
        }

        progress?.Invoke("מתקין את Mahod Intergreen…");
        var asm = Assembly.GetExecutingAssembly();
        string resName = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("bundle.zip", StringComparison.OrdinalIgnoreCase));
        long payloadBytes = 0;
        using (var stream = asm.GetManifestResourceStream(resName)!)
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                string dest = Path.GetFullPath(Path.Combine(PluginsDir, entry.FullName));
                if (!dest.StartsWith(Path.GetFullPath(PluginsDir), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unsafe archive path: " + entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                payloadBytes += entry.Length;
            }
        }
        if (!File.Exists(Path.Combine(BundleDir, "PackageContents.xml")))
            throw new InvalidOperationException("PackageContents.xml missing after extraction.");

        progress?.Invoke("רושם את ההסרה ב-Windows…");
        Directory.CreateDirectory(UninstallerDir);
        string? self = Environment.ProcessPath;
        if (self is not null &&
            !string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallerExe), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, UninstallerExe, overwrite: true);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
        {
            key.SetValue("DisplayName", ProductName + " (AutoCAD 2026)");
            key.SetValue("DisplayVersion", EngineVersion);
            key.SetValue("Publisher", "Mahod Engineering");
            key.SetValue("InstallLocation", BundleDir);
            key.SetValue("UninstallString", $"\"{UninstallerExe}\" /uninstall");
            key.SetValue("QuietUninstallString", $"\"{UninstallerExe}\" /uninstall /silent");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)Math.Max(1, payloadBytes / 1024), RegistryValueKind.DWord);
        }
        progress?.Invoke("ההתקנה הושלמה.");
    }

    internal static void Uninstall()
    {
        if (AutoCadRunning())
            throw new InvalidOperationException(
                "AutoCAD פתוח במחשב זה. סגרו את AutoCAD והריצו את ההסרה שוב.");
        if (Directory.Exists(BundleDir))
            DeleteDirectoryWithRetry(BundleDir);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        // If running from the installed uninstaller location, schedule self-directory removal
        // after exit (a process cannot delete its own running executable).
        string? self = Environment.ProcessPath;
        if (self is not null &&
            Path.GetFullPath(self).StartsWith(Path.GetFullPath(UninstallerDir), StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("cmd.exe",
                $"/c ping -n 3 127.0.0.1 >nul & rd /s /q \"{UninstallerDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi);
        }
        else if (Directory.Exists(UninstallerDir))
        {
            DeleteDirectoryWithRetry(UninstallerDir);
        }
    }

    private static string? ProbeForLockedFile(string dir)
    {
        foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(f, FileAttributes.Normal);
                using var fs = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return f; }
            catch (UnauthorizedAccessException) { return f; }
        }
        return null;
    }

    private static void DeleteDirectoryWithRetry(string dir)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // Read-only files (e.g. preserved from a ZIP extraction) make
                // Directory.Delete throw UnauthorizedAccessException — clear attributes first.
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(300 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(300 * attempt);
            }
        }
    }
}
