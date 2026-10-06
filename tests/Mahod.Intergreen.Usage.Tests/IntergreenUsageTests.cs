using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Mahod.Intergreen.AutoCAD2026;
using Mahod.Intergreen.Host;
using MahodAI.Civil3D.Plugin.Utilities;

namespace Mahod.Intergreen.Usage.Tests;

/// <summary>
/// r15: Mahod Intergreen's usage records for Mahod Impact must be the SAME records the Intergreen
/// inside MahodAI sends (plugin Ported/Mahod.Intergreen.MahodAI/IntergreenUsage.cs, commit 02d1601)
/// — same recorder, tool id, action names, priced feature and unit key — or Impact would count
/// two different tools.
/// </summary>
public class IntergreenUsageTests
{
    /// <summary>
    /// git blob id of MahodAI.Civil3D.Plugin/Utilities/MahodUsage.cs at plugin a259bed
    /// (`git rev-parse a259bed:MahodAI.Civil3D.Plugin/Utilities/MahodUsage.cs`). The copy here is
    /// byte-for-byte that file; change the recorder in the plugin first, then copy it and this id.
    /// </summary>
    private const string PluginRecorderBlob = "85a350dbd4e266faa9c458b5b813e50acf05da64";

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Mahod.Intergreen.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>git's blob id of a text file as git stores it (line endings normalised to LF).</summary>
    private static string GitBlobId(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
        var content = new UTF8Encoding(false).GetBytes(text);
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var sha1 = SHA1.Create();
        return Convert.ToHexString(sha1.ComputeHash(header.Concat(content).ToArray())).ToLowerInvariant();
    }

    [Fact]
    public void Recorder_is_the_MahodAI_plugins_copy_byte_for_byte()
    {
        var path = Path.Combine(RepoRoot(), "src", "Mahod.Intergreen.AutoCAD2026", "Usage", "MahodUsage.cs");
        Assert.Equal(PluginRecorderBlob, GitBlobId(File.ReadAllBytes(path)));

        // On a machine with the plugin checked out, compare against it directly as well.
        var plugin = Environment.GetEnvironmentVariable("MAHODAI_PLUGIN_ROOT");
        if (!string.IsNullOrWhiteSpace(plugin))
        {
            var theirs = Path.Combine(plugin, "MahodAI.Civil3D.Plugin", "Utilities", "MahodUsage.cs");
            Assert.Equal(GitBlobId(File.ReadAllBytes(theirs)), GitBlobId(File.ReadAllBytes(path)));
        }
    }

    [Fact]
    public void Tool_and_priced_feature_match_the_in_tree_contract()
    {
        Assert.Equal("intergreen", IntergreenUsage.Tool);
        Assert.Equal("junction_intergreen", IntergreenUsage.Feature);
        Assert.Equal("https://dev.mahodeng.co.il/api/v1/usage", MahodUsage.Endpoint);
    }

    [Fact]
    public void Junction_key_is_drawing_fingerprint_plus_workbook_name_lower_case()
    {
        Assert.Equal("{AB-CD}|junction:05_pines_ig_matrix",
            IntergreenUsage.JunctionKey("{AB-CD}", @"C:\Projects\X\05_PINES_IG_matrix.xlsx"));
        Assert.Equal("fp|junction:", IntergreenUsage.JunctionKey("fp", null));

        // Same junction, any folder or letter case → the same priced unit; another junction → another.
        var a = MahodUsage.WorkKey(IntergreenUsage.Tool, IntergreenUsage.JunctionKey("fp", @"C:\a\Junction.xlsx"));
        var b = MahodUsage.WorkKey(IntergreenUsage.Tool, IntergreenUsage.JunctionKey("fp", @"D:\b\JUNCTION.XLSX"));
        var c = MahodUsage.WorkKey(IntergreenUsage.Tool, IntergreenUsage.JunctionKey("fp", @"C:\a\Other.xlsx"));
        var d = MahodUsage.WorkKey(IntergreenUsage.Tool, IntergreenUsage.JunctionKey("fp2", @"C:\a\Junction.xlsx"));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
        Assert.Matches("^[0-9a-f]{32}$", a);
    }

    [Theory]
    [InlineData("Excel חדש מהשרטוט…", null, "intergreen_new_project_from_drawing")]
    [InlineData("מתקדם: נתיב Excel ידני…", "Setup", "intergreen_setup_manual_path")]
    [InlineData("נקודות ייחוס…", null, "intergreen_confirm_references")]
    [InlineData("סף הארכה אוטומטית…", null, "intergreen_auto_extend_threshold")]
    [InlineData("הארך בשרטוט…", null, "intergreen_extend_in_drawing")]
    [InlineData("הצג קו קצר…", null, "intergreen_show_short_boundary")]
    [InlineData("בחירת חוקים…", null, "intergreen_choose_rules")]
    [InlineData("Export Support Log", null, "intergreen_support_log")]
    [InlineData("1. Setup — בחר קובץ Excel…", "Setup", "intergreen_setup")]
    [InlineData("2. Validate", "Validate", "intergreen_validate")]
    [InlineData("3. Analyze", "Analyze", "intergreen_analyze")]
    [InlineData("4. Show in drawing", "Show", "intergreen_show")]
    [InlineData("5. Export Excel", "Export", "intergreen_export")]
    [InlineData("Clear QA", "ClearQa", "intergreen_clearqa")]
    [InlineData("שרטוט DWG…", null, "intergreen_palette")]
    [InlineData("בניית תנועות…", null, "intergreen_palette")]
    public void Palette_buttons_are_named_like_the_in_tree_Intergreen(string label, string? gate, string expected)
    {
        // gate as a string (an enum in InlineData can drop the theory from discovery).
        WorkflowAction? g = gate is null ? null : Enum.Parse<WorkflowAction>(gate);
        Assert.Equal(expected, IntergreenUsage.ButtonAction(g, label));
    }

    [Fact]
    public void Every_known_button_label_is_a_real_palette_button()
    {
        var workflow = Source("src/Mahod.Intergreen.AutoCAD2026/IgWorkflowCommands.cs");
        foreach (var label in new[] { "Excel חדש מהשרטוט…", "מתקדם: נתיב Excel ידני…", "נקודות ייחוס…", "סף הארכה אוטומטית…",
                     "הארך בשרטוט…", "הצג קו קצר…", "בחירת חוקים…", "Export Support Log" })
            Assert.Contains($"(\"{label}\"", workflow);
    }

    [Fact]
    public void Hooks_sit_at_the_in_tree_code_points()
    {
        var workflow = Source("src/Mahod.Intergreen.AutoCAD2026/IgWorkflowCommands.cs");
        Assert.Contains("Guard(gate, onClick, label);", workflow);
        Assert.Contains("IntergreenUsage.Button(gate, usageLabel, usageOk, usageClock.ElapsedMilliseconds)", workflow);
        Assert.Contains("IntergreenUsage.Exported(doc, _workbookPath)", workflow);
        Assert.Contains("IntergreenUsage.Command(\"intergreen\"", workflow);
        Assert.Contains("IntergreenUsage.Command(\"ig_clear_qa\"", workflow);
        // document activation records nothing
        Assert.Contains("DocumentActivated += (_, _) => Guard(null, OnDocumentActivated);", workflow);
        // the unit is recorded only after the export passed its structural check
        int check = workflow.IndexOf("export.StructuralIssues.Count > 0", StringComparison.Ordinal);
        int unit = workflow.IndexOf("IntergreenUsage.Exported(doc, _workbookPath)", StringComparison.Ordinal);
        Assert.True(check > 0 && unit > check);

        var commands = Source("src/Mahod.Intergreen.AutoCAD2026/IgCommands.cs");
        Assert.Equal(2, Count(commands, "IntergreenUsage.Command(\"ig_scan\""));
        Assert.Equal(2, Count(commands, "IntergreenUsage.Command(\"ig_export_geometry\""));

        // The priced export is reached only from the palette's "5. Export Excel" (never from an
        // IG_SMOKE_* harness command): ExportExcel appears once as the button, once as the method.
        Assert.Equal(2, Count(workflow, "ExportExcel"));
        Assert.Contains("Add(\"5. Export Excel\", WorkflowAction.Export, ExportExcel);", workflow);
        // only these files record usage
        var recorders = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Usage{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("IntergreenUsage.", StringComparison.Ordinal))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "IgCommands.cs", "IgWorkflowCommands.cs" }, recorders);
    }

    private static int Count(string text, string what)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    [Fact]
    public void Records_carry_no_person_pc_drawing_or_path()
    {
        var unit = MahodUsage.UnitEvent(IntergreenUsage.Tool, IntergreenUsage.Feature,
            MahodUsage.WorkKey(IntergreenUsage.Tool, IntergreenUsage.JunctionKey("{FP}", @"C:\p\J.xlsx")),
            MahodUsage.Palette, DateTime.UtcNow, "1.0.0");
        var action = MahodUsage.ActionEvent(IntergreenUsage.Tool, "intergreen_export", MahodUsage.Completed, 12,
            MahodUsage.Palette, 0, DateTime.UtcNow, "1.0.0");
        foreach (var ev in new[] { unit, action })
        {
            AssertNoIdentity(ev);
            Assert.Equal("intergreen", ev["tool"]!.GetValue<string>());
        }
        Assert.Equal("junction_intergreen", unit["n"]!.GetValue<string>());
        Assert.DoesNotContain("{FP}", unit.ToJsonString());
        Assert.DoesNotContain("J.xlsx", unit.ToJsonString());
    }

    private static readonly string[] IdentityKeys =
        { "user", "username", "login", "machine", "machinename", "pc", "host", "hostname", "computer", "path", "file", "drawing", "layer" };

    private static void AssertNoIdentity(JsonNode? node)
    {
        if (node is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                Assert.DoesNotContain(k.ToLowerInvariant(), IdentityKeys);
                AssertNoIdentity(v);
            }
        }
        else if (node is JsonArray a)
        {
            foreach (var v in a) AssertNoIdentity(v);
        }
    }

    /// <summary>
    /// End to end without a network: the hooks' real recorder path (queue file → flush → HTTP
    /// POST) against a loopback listener standing in for MahodAI.
    /// </summary>
    [Fact]
    public async Task Hooks_post_intergreen_records_to_the_usage_route_with_no_identity()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var queue = Path.Combine(Path.GetTempPath(), "ig-usage-test-" + Guid.NewGuid().ToString("N"));
        var saved = new Dictionary<string, string?>();
        foreach (var name in new[] { "MAHOD_USAGE_URL", "MAHOD_INSTALL_KEY", "MAHOD_USAGE_OFF", "MAHOD_CAD_KEY" })
            saved[name] = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable("MAHOD_USAGE_URL", $"http://127.0.0.1:{port}/api/v1/usage");
            // A stand-in installation key, so the PC's own MahodAI key is never read by a test.
            Environment.SetEnvironmentVariable("MAHOD_INSTALL_KEY", "test-install-key-not-real");
            Environment.SetEnvironmentVariable("MAHOD_USAGE_OFF", null);
            Environment.SetEnvironmentVariable("MAHOD_CAD_KEY", null);
            MahodUsage.QueueDir = queue;

            MahodUsage.UploadEnabled = false;
            IntergreenUsage.Command("INTERGREEN", ok: true);
            IntergreenUsage.Button(WorkflowAction.Export, "5. Export Excel", ok: true, elapsedMs: 1234);
            IntergreenUsage.Junction("{4F1C-FINGERPRINT}", @"C:\Projects\Secret Street\05_Junction_Alpha.xlsx");
            MahodUsage.UploadEnabled = true;
            IntergreenUsage.Button(null, "Export Support Log", ok: false, elapsedMs: 5);   // triggers the upload

            var accept = listener.AcceptTcpClientAsync();
            var done = await Task.WhenAny(accept, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(done == accept, "no POST reached the loopback listener");
            using var client = await accept;
            var (requestLine, headers, body) = await ReadRequest(client.GetStream());

            Assert.Equal("POST /api/v1/usage HTTP/1.1", requestLine);
            Assert.Equal("test-install-key-not-real", headers["x-install-key"]);
            Assert.False(headers.ContainsKey("x-mahod-product-key"));

            var json = JsonNode.Parse(body)!.AsObject();
            Assert.Equal(1, json["v"]!.GetValue<int>());
            var events = json["events"]!.AsArray().Select(e => e!.AsObject()).ToList();
            Assert.Equal(4, events.Count);
            Assert.All(events, e => Assert.Equal("intergreen", e["tool"]!.GetValue<string>()));

            var unit = Assert.Single(events, e => e["k"]!.GetValue<string>() == "cad_unit");
            Assert.Equal("junction_intergreen", unit["n"]!.GetValue<string>());
            Assert.Equal("palette", unit["p"]!["door"]!.GetValue<string>());
            Assert.Equal(MahodUsage.WorkKey("intergreen", "{4F1C-FINGERPRINT}|junction:05_junction_alpha"),
                unit["p"]!["w"]!.GetValue<string>());

            var actions = events.Where(e => e["k"]!.GetValue<string>() == "cad_action")
                .ToDictionary(e => e["n"]!.GetValue<string>());
            Assert.Equal("command", actions["intergreen"]["p"]!["door"]!.GetValue<string>());
            Assert.Equal("completed", actions["intergreen"]["s"]!.GetValue<string>());
            Assert.Equal("palette", actions["intergreen_export"]["p"]!["door"]!.GetValue<string>());
            Assert.Equal(1234, actions["intergreen_export"]["ms"]!.GetValue<long>());
            Assert.Equal("failed", actions["intergreen_support_log"]["s"]!.GetValue<string>());

            AssertNoIdentity(json);
            foreach (var secret in new[] { "4F1C-FINGERPRINT", "Secret Street", "05_Junction_Alpha", @"C:\\Projects" })
                Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
            if (Environment.UserName.Length >= 4) Assert.DoesNotContain(Environment.UserName, body, StringComparison.OrdinalIgnoreCase);
            if (Environment.MachineName.Length >= 4) Assert.DoesNotContain(Environment.MachineName, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("test-install-key-not-real", body);   // the credential travels only in its header

            // a 200 empties the queue
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Directory.GetFiles(queue).Length > 0 && DateTime.UtcNow < deadline) await Task.Delay(100);
            Assert.Empty(Directory.GetFiles(queue));
        }
        finally
        {
            listener.Stop();
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
            try { Directory.Delete(queue, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task<(string RequestLine, Dictionary<string, string> Headers, string Body)> ReadRequest(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(chunk);
            if (n == 0) throw new IOException("connection closed before headers");
            buffer.AddRange(chunk.Take(n));
            headerEnd = IndexOf(buffer, "\r\n\r\n"u8.ToArray());
        }
        var head = Encoding.ASCII.GetString(buffer.Take(headerEnd).ToArray()).Split("\r\n");
        var headers = head.Skip(1).Select(l => l.Split(':', 2))
            .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
        int length = int.Parse(headers["content-length"]);
        var body = buffer.Skip(headerEnd + 4).ToList();
        while (body.Count < length)
        {
            int n = await stream.ReadAsync(chunk);
            if (n == 0) break;
            body.AddRange(chunk.Take(n));
        }
        var reply = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{}"u8.ToArray();
        await stream.WriteAsync(reply);
        await stream.FlushAsync();
        return (head[0], headers, Encoding.UTF8.GetString(body.ToArray()));
    }

    private static int IndexOf(List<byte> haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Count; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length && ok; j++) ok = haystack[i + j] == needle[j];
            if (ok) return i;
        }
        return -1;
    }
}
