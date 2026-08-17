using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Mahod.Intergreen.Rules;

/// <summary>
/// Typed, whitelisted speed rule (v3 §21 — rule packs may not contain executable code).
/// Kinds: const | posted | halfPosted | postedMinus | max | min | operationalProfile.
/// </summary>
public sealed record SpeedRule(
    string Kind,
    double? Kph = null,
    IReadOnlyList<SpeedRule>? Of = null)
{
    public static SpeedRule Parse(JsonElement el)
    {
        var kind = el.GetProperty("kind").GetString()
                   ?? throw new FormatException("speed rule missing 'kind'");
        double? kph = el.TryGetProperty("kph", out var k) && k.ValueKind == JsonValueKind.Number
            ? k.GetDouble() : null;
        List<SpeedRule>? of = null;
        if (el.TryGetProperty("of", out var arr) && arr.ValueKind == JsonValueKind.Array)
            of = arr.EnumerateArray().Select(Parse).ToList();
        return new SpeedRule(kind, kph, of);
    }

    /// <summary>
    /// Resolves the rule to km/h. postedSpeedKph / operationalProfileKph may be null —
    /// a rule that needs them then returns null, which the caller must turn into
    /// MISSING_ENGINEERING_CLASSIFICATION, never into a default.
    /// </summary>
    public double? Resolve(double? postedSpeedKph, double? operationalProfileKph = null)
    {
        switch (Kind)
        {
            case "const":
                return Kph ?? throw new FormatException("const rule requires 'kph'");
            case "posted":
                return postedSpeedKph;
            case "halfPosted":
                return postedSpeedKph / 2.0;
            case "postedMinus":
                return postedSpeedKph - (Kph ?? throw new FormatException("postedMinus requires 'kph'"));
            case "operationalProfile":
                return operationalProfileKph;
            case "max":
            case "min":
            {
                if (Of is null || Of.Count == 0)
                    throw new FormatException($"{Kind} rule requires 'of'");
                var values = new List<double>();
                foreach (var r in Of)
                {
                    var v = r.Resolve(postedSpeedKph, operationalProfileKph);
                    if (v is null) return null; // propagate missing input
                    values.Add(v.Value);
                }
                return Kind == "max" ? values.Max() : values.Min();
            }
            default:
                throw new FormatException($"unknown speed-rule kind '{Kind}' — kinds are whitelisted");
        }
    }
}
