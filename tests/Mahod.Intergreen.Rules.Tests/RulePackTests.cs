using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Rules;
using Xunit;

namespace Mahod.Intergreen.Rules.Tests;

public class RulePackLoadTests
{
    [Theory]
    [InlineData("legacy-mahod-v1", RulePackStatus.Approved, "legacy-envelope", "mahod-legacy-0.1", null)]
    [InlineData("israel-2025-06", RulePackStatus.Validated, "lane-centreline", "guidelines-ceil", 3)]
    public void Packs_load_with_expected_manifest(string id, RulePackStatus status,
        string geometry, string rounding, int? minimum)
    {
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), id));
        Assert.Equal(id, pack.Manifest.Id);
        Assert.Equal(status, pack.Manifest.Status);
        Assert.Equal(geometry, pack.Manifest.GeometryStrategyId);
        Assert.Equal(rounding, pack.Manifest.RoundingStrategyId);
        Assert.Equal(minimum, pack.Manifest.MinimumIntergreenSec);
        Assert.NotEmpty(pack.ContentSha256);
    }

    [Theory]
    [InlineData("legacy-mahod-v1")]
    [InlineData("israel-2025-06")]
    public void Packs_pass_validation_with_zero_errors(string id)
    {
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), id));
        var findings = RulePackValidator.Validate(pack);
        Assert.Empty(findings.Where(f => f.Severity == Severity.Error));
    }

    [Fact]
    public void Approved_pack_without_approver_is_rejected()
    {
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), "legacy-mahod-v1"));
        var patched = new RulePack
        {
            Manifest = pack.Manifest with { ApprovedBy = null },
            Parameters = pack.Parameters,
            ContentSha256 = pack.ContentSha256,
            SourceDirectory = pack.SourceDirectory,
        };
        Assert.Contains(RulePackValidator.Validate(patched), f => f.Code == "IG-RULE-030");
    }
}

/// <summary>Runs every speed-resolution case stored inside each pack's tests.json (v3 §51).</summary>
public class RulePackSpeedResolutionTests
{
    public static IEnumerable<object[]> AllCases()
    {
        foreach (var id in new[] { "legacy-mahod-v1", "israel-2025-06" })
        {
            var testsPath = Path.Combine(RulePackLoader.RulesRoot(), id, "tests.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(testsPath));
            if (!doc.RootElement.TryGetProperty("speedResolution", out var cases)) continue;
            foreach (var c in cases.EnumerateArray())
            {
                yield return new object[]
                {
                    id,
                    c.GetProperty("id").GetString()!,
                    c.GetProperty("road").GetString()!,
                    c.GetProperty("maneuver").GetString()!,
                    c.GetProperty("which").GetString()!,
                    c.GetProperty("postedKph").GetDouble(),
                    c.GetProperty("expectKph").GetDouble(),
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Speed_rule_cases(string packId, string caseId, string road, string maneuver,
        string which, double postedKph, double expectKph)
    {
        var pack = RulePackLoader.Load(Path.Combine(RulePackLoader.RulesRoot(), packId));
        var rule = pack.VehicleSpeedRule(
            road == "urban" ? RoadEnvironment.Urban : RoadEnvironment.Interurban,
            maneuver == "through" ? Maneuver.Through : Maneuver.Turn,
            slow: which == "slow");
        var resolved = rule.Resolve(postedKph);
        Assert.True(resolved.HasValue, $"{packId}/{caseId}: rule did not resolve");
        Assert.Equal(expectKph, resolved!.Value, 9);
    }
}

public class SpeedRuleSafetyTests
{
    [Fact]
    public void Unknown_rule_kind_is_rejected_not_evaluated()
    {
        using var doc = JsonDocument.Parse("""{"kind":"eval","code":"1+1"}""");
        var rule = SpeedRule.Parse(doc.RootElement);
        Assert.Throws<FormatException>(() => rule.Resolve(50));
    }

    [Fact]
    public void Posted_dependent_rule_without_posted_speed_returns_null_not_default()
    {
        using var doc = JsonDocument.Parse("""{"kind":"max","of":[{"kind":"posted"},{"kind":"const","kph":50}]}""");
        var rule = SpeedRule.Parse(doc.RootElement);
        Assert.Null(rule.Resolve(postedSpeedKph: null));
    }
}
