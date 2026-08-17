using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Rules;

public enum RulePackStatus { Draft, Validated, Approved, Retired }

/// <summary>Whitelisted strategy ids (v3 §21). Rule packs reference these; never code.</summary>
public static class StrategyRegistry
{
    public static readonly IReadOnlySet<string> CalculationStrategies =
        new HashSet<string> { "legacy-mahod-v1", "israel-2025-06" };

    public static readonly IReadOnlySet<string> GeometryStrategies =
        new HashSet<string> { "legacy-envelope", "lane-centreline" };

    public static readonly IReadOnlySet<string> RoundingStrategies =
        new HashSet<string> { "mahod-legacy-0.1", "guidelines-ceil" };
}

public sealed record RulePackManifest(
    string Id,
    string Version,
    string DisplayName,
    RulePackStatus Status,
    string? EffectiveDate,
    string GeometryStrategyId,
    string CalculationStrategyId,
    string RoundingStrategyId,
    int? MinimumIntergreenSec,
    string? ApprovedAt,
    string? ApprovedBy);

/// <summary>A loaded, immutable rule pack: manifest + parameters + content hash.</summary>
public sealed class RulePack
{
    public required RulePackManifest Manifest { get; init; }
    public required JsonDocument Parameters { get; init; }
    public required string ContentSha256 { get; init; }
    public required string SourceDirectory { get; init; }

    public double ReactionTimeSec => Parameters.RootElement.GetProperty("reactionTimeSec").GetDouble();
    public double DecelerationMps2 => Parameters.RootElement.GetProperty("decelerationMps2").GetDouble();

    public double PedestrianSpeedMps(CrossingContext context)
    {
        var map = Parameters.RootElement.GetProperty("pedestrianSpeedMps");
        var key = context switch
        {
            CrossingContext.Standard => "standard",
            CrossingContext.HighDemand => "highDemand",
            CrossingContext.CrossesLrt => "crossesLrt",
            CrossingContext.NearInstitutions => "nearInstitutions",
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };
        if (!map.TryGetProperty(key, out var v))
            throw new KeyNotFoundException($"rule pack '{Manifest.Id}' has no pedestrian speed for '{key}'");
        return v.GetDouble();
    }

    public SpeedRule VehicleSpeedRule(RoadEnvironment road, Maneuver maneuver, bool slow)
    {
        var env = road == RoadEnvironment.Urban ? "urban" : "interurban";
        var man = maneuver == Maneuver.Through ? "through" : "turn";
        var el = Parameters.RootElement.GetProperty("vehicleSpeeds").GetProperty(env).GetProperty(man)
            .GetProperty(slow ? "slow" : "fast");
        return SpeedRule.Parse(el);
    }

    public double VehicleLengthMeters(VehicleLengthClass cls)
    {
        var root = Parameters.RootElement;
        return cls switch
        {
            VehicleLengthClass.Standard12 => root.GetProperty("vehicleLengthM").GetProperty("standard").GetDouble(),
            VehicleLengthClass.ArticulatedHeavy19 => root.GetProperty("vehicleLengthM").GetProperty("articulatedBusHeavyIndustry").GetDouble(),
            VehicleLengthClass.Bicycle2 => root.GetProperty("bicycle").GetProperty("lengthM").GetDouble(),
            VehicleLengthClass.Lrt15 => root.GetProperty("lrt").GetProperty("lengthM").GetDouble(),
            _ => throw new ArgumentOutOfRangeException(nameof(cls)),
        };
    }
}

public static class RulePackLoader
{
    public static RulePack Load(string directory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var parametersPath = Path.Combine(directory, "parameters.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException(manifestPath);
        if (!File.Exists(parametersPath)) throw new FileNotFoundException(parametersPath);

        using var mdoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var m = mdoc.RootElement;

        var manifest = new RulePackManifest(
            Id: m.GetProperty("id").GetString()!,
            Version: m.GetProperty("version").GetString()!,
            DisplayName: m.GetProperty("displayName").GetString()!,
            Status: Enum.Parse<RulePackStatus>(m.GetProperty("status").GetString()!, ignoreCase: true),
            EffectiveDate: m.TryGetProperty("effectiveDate", out var ed) && ed.ValueKind == JsonValueKind.String ? ed.GetString() : null,
            GeometryStrategyId: m.GetProperty("geometryStrategyId").GetString()!,
            CalculationStrategyId: m.GetProperty("calculationStrategyId").GetString()!,
            RoundingStrategyId: m.GetProperty("roundingStrategyId").GetString()!,
            MinimumIntergreenSec: m.TryGetProperty("minimumIntergreenSec", out var mi) && mi.ValueKind == JsonValueKind.Number ? mi.GetInt32() : null,
            ApprovedAt: m.TryGetProperty("approvedAt", out var aa) && aa.ValueKind == JsonValueKind.String ? aa.GetString() : null,
            ApprovedBy: m.TryGetProperty("approvedBy", out var ab) && ab.ValueKind == JsonValueKind.String ? ab.GetString() : null);

        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(
            File.ReadAllBytes(manifestPath).Concat(File.ReadAllBytes(parametersPath)).ToArray()));

        return new RulePack
        {
            Manifest = manifest,
            Parameters = JsonDocument.Parse(File.ReadAllText(parametersPath)),
            ContentSha256 = hash,
            SourceDirectory = directory,
        };
    }

    /// <summary>Finds the rules/ root by walking up from the app base directory.</summary>
    public static string RulesRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "rules");
            if (File.Exists(Path.Combine(candidate, "legacy-mahod-v1", "manifest.json")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("rules/ not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>Validation of a rule pack before it may be approved/used (v3 §23).</summary>
public static class RulePackValidator
{
    public static IReadOnlyList<ValidationFinding> Validate(RulePack pack)
    {
        var findings = new List<ValidationFinding>();
        var m = pack.Manifest;

        void Error(string code, string msg) =>
            findings.Add(new ValidationFinding(code, Severity.Error, null, msg));

        if (!StrategyRegistry.CalculationStrategies.Contains(m.CalculationStrategyId))
            Error("IG-RULE-001", $"unknown calculation strategy '{m.CalculationStrategyId}'");
        if (!StrategyRegistry.GeometryStrategies.Contains(m.GeometryStrategyId))
            Error("IG-RULE-002", $"unknown geometry strategy '{m.GeometryStrategyId}'");
        if (!StrategyRegistry.RoundingStrategies.Contains(m.RoundingStrategyId))
            Error("IG-RULE-003", $"unknown rounding strategy '{m.RoundingStrategyId}'");

        try
        {
            if (pack.ReactionTimeSec <= 0 || pack.ReactionTimeSec > 5)
                Error("IG-RULE-010", $"reaction time {pack.ReactionTimeSec}s out of sane range");
            if (pack.DecelerationMps2 <= 0 || pack.DecelerationMps2 > 10)
                Error("IG-RULE-011", $"deceleration {pack.DecelerationMps2} m/s² out of sane range");

            // every speed rule must resolve for a representative posted speed
            foreach (var road in new[] { RoadEnvironment.Urban, RoadEnvironment.Interurban })
                foreach (var man in new[] { Maneuver.Through, Maneuver.Turn })
                    foreach (var slow in new[] { false, true })
                    {
                        var v = pack.VehicleSpeedRule(road, man, slow).Resolve(postedSpeedKph: 70);
                        if (v is null or <= 0 or > 130)
                            Error("IG-RULE-012", $"speed rule {road}/{man}/{(slow ? "slow" : "fast")} resolves to {v}");
                    }

            _ = pack.PedestrianSpeedMps(CrossingContext.Standard);
        }
        catch (Exception ex)
        {
            Error("IG-RULE-020", $"parameters.json structural failure: {ex.Message}");
        }

        if (m.Status == RulePackStatus.Approved && m.ApprovedBy is null)
            Error("IG-RULE-030", "APPROVED pack must record approvedBy");

        return findings;
    }
}
