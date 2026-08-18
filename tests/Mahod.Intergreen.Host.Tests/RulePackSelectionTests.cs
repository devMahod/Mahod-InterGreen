using System.Text.Json;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>r8: versioned-guideline selection — discovery, per-project persistence,
/// no-silent-migration default, and deliberate-change warning.</summary>
public class RulePackSelectionTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("rulepacks");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string MakeRulesRoot(params (string id, string version, string display)[] packs)
    {
        string root = Path.Combine(_dir, "rules");
        foreach (var (id, version, display) in packs)
        {
            Directory.CreateDirectory(Path.Combine(root, id));
            File.WriteAllText(Path.Combine(root, id, "manifest.json"),
                $$"""{"id":"{{id}}","version":"{{version}}","displayName":"{{display}}"}""");
        }
        return root;
    }

    [Fact]
    public void Discovers_installed_packs_with_identity()
    {
        string root = MakeRulesRoot(("legacy-mahod-v1", "1.0.0", "Legacy"), ("israel-2025-06", "1.0.0", "הנחיות 2025"));
        var found = RulePackSelection.ListInstalled(root);
        Assert.Equal(2, found.Count);
        Assert.Contains(found, x => x.Id == "legacy-mahod-v1" && x.Version == "1.0.0");
        Assert.Contains(found, x => x.Id == "israel-2025-06" && x.DisplayName == "הנחיות 2025");
    }

    [Fact]
    public void Malformed_pack_is_skipped_without_taking_discovery_down()
    {
        string root = MakeRulesRoot(("legacy-mahod-v1", "1.0.0", "Legacy"));
        Directory.CreateDirectory(Path.Combine(root, "broken-pack"));
        File.WriteAllText(Path.Combine(root, "broken-pack", "manifest.json"), "{not json");
        Directory.CreateDirectory(Path.Combine(root, "no-manifest"));
        var found = RulePackSelection.ListInstalled(root);
        Assert.Single(found);
        Assert.Equal("legacy-mahod-v1", found[0].Id);
    }

    [Fact]
    public void Project_without_stored_choice_stays_on_legacy_default_no_silent_migration()
    {
        var data = new Dictionary<string, JsonElement>();
        Assert.Equal("legacy-mahod-v1", RulePackSelection.ProjectPackId(data));
    }

    [Fact]
    public void Stored_choice_persists_through_transactional_sidecar_roundtrip()
    {
        var data = new Dictionary<string, JsonElement>();
        RulePackSelection.ApplySelection(data, "israel-2025-06", "tester");
        string sc = Path.Combine(_dir, "p.intergreen-project.json");
        SidecarStore.Commit(sc, data);
        var reloaded = SidecarStore.Load(sc);
        Assert.Equal(SidecarLoadStatus.Ok, reloaded.Status);
        Assert.Equal("israel-2025-06", RulePackSelection.ProjectPackId(reloaded.Data));
    }

    [Fact]
    public void Change_requires_warning_only_when_effective_pack_actually_changes()
    {
        Assert.False(RulePackSelection.ChangeRequiresWarning("legacy-mahod-v1", "legacy-mahod-v1"));
        Assert.False(RulePackSelection.ChangeRequiresWarning("legacy-mahod-v1", "LEGACY-MAHOD-V1"));
        Assert.True(RulePackSelection.ChangeRequiresWarning("legacy-mahod-v1", "israel-2025-06"));
    }

    [Fact]
    public void Rules_change_invalidates_results_but_keeps_project_configured()
    {
        var sm = new WorkflowStateMachine();
        sm.OnSetupCommitted();
        sm.OnAnalyzeSucceeded();
        sm.HasSelection = true;
        sm.OnRulesChanged();
        Assert.True(sm.ProjectConfigured);
        Assert.False(sm.Validated);
        Assert.False(sm.Analyzed);
        Assert.False(sm.HasSelection);
        Assert.NotNull(sm.Gate(WorkflowAction.Show));   // gated until re-Analyze
        Assert.Null(sm.Gate(WorkflowAction.Validate));  // still configured
    }
}
