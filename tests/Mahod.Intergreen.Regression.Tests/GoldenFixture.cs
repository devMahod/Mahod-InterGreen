using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Mahod.Intergreen.Core.Legacy;

namespace Mahod.Intergreen.Regression.Tests;

/// <summary>
/// Loads the golden fixtures extracted from the two completed workbooks
/// (tests/fixtures/golden-example*.json, produced by scripts/extract_golden.py
/// straight from the source XLSX files — see docs/SOURCE_INVENTORY.md).
/// </summary>
public sealed class GoldenFixture
{
    public required string Example { get; init; }
    public required LegacyTemplateVariant Variant { get; init; }
    public required LegacyConstants Constants { get; init; }
    public required Dictionary<string, LegacyMovementParameters> Parameters { get; init; }
    public required List<GoldenRow> Rows { get; init; }

    public LegacyWorkbookCompatibilityCalculator CreateCalculator(FinalIgPolicy? policy = null)
        => new(Constants, Variant, Parameters, policy);

    public static string FixturesDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "tests", "fixtures");
            if (File.Exists(Path.Combine(candidate, "golden-example1.json")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("tests/fixtures not found above " + AppContext.BaseDirectory);
    }

    public static GoldenFixture Load(string example)
    {
        var path = Path.Combine(FixturesDirectory(), $"golden-{example}.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var variant = root.GetProperty("variant").GetString() == "V1"
            ? LegacyTemplateVariant.V1PerMovementVehicleLength
            : LegacyTemplateVariant.V2GlobalVehicleLength;

        var c = root.GetProperty("constants");
        var constants = new LegacyConstants(
            PedestrianSpeedMps: Num(c, "pedSpeed") ?? 1.2,
            ReactionTimeSec: Num(c, "reaction") ?? 1.0,
            DecelerationMps2: Num(c, "decel") ?? 3.5,
            GlobalVehicleLengthMeters: Num(c, "vehLenGlobal"),
            InbarMode: c.TryGetProperty("inbarFlag", out var fl) && fl.ValueKind == JsonValueKind.String && fl.GetString() == "y",
            InbarDefaultVehicleLengthMeters: Num(c, "inbarDefaultVehicleLength") ?? 12.0);

        var parameters = new Dictionary<string, LegacyMovementParameters>(StringComparer.Ordinal);
        foreach (var prop in root.GetProperty("parameters").EnumerateObject())
        {
            var v = prop.Value;
            parameters[prop.Name] = new LegacyMovementParameters(
                FastClearingKph: Num(v, "fastKph"),
                SlowClearingKph: Num(v, "slowKph"),
                VehicleLengthMeters: Num(v, "colE"),
                AdditionToInbarMeters: Num(v, "colF"));
        }

        var rows = new List<GoldenRow>();
        foreach (var r in root.GetProperty("rows").EnumerateArray())
        {
            var pts = new List<MeasuredPoint>();
            foreach (var p in r.GetProperty("points").EnumerateArray())
                pts.Add(new MeasuredPoint(Num(p, "cd"), Num(p, "ed")));

            var cached = r.GetProperty("cached");
            var pointIgs = new List<double?>();
            foreach (var t in cached.GetProperty("pointTimes").EnumerateArray())
                pointIgs.Add(Num(t, "ig"));

            rows.Add(new GoldenRow
            {
                Input = new ConflictRowInput(
                    (int)(Num(r, "conflictNo") ?? 0),
                    r.GetProperty("clearing").GetString()!,
                    r.GetProperty("entering").GetString()!,
                    pts[0], pts[1], pts[2], pts[3]),
                ExpectedPointIgs = pointIgs,
                ExpectedFinalIg = Num(cached, "finalIg"),
                ExpectedDefiningPoint = cached.TryGetProperty("definingPoint", out var dp) && dp.ValueKind == JsonValueKind.String
                    ? dp.GetString() : null,
            });
        }

        return new GoldenFixture
        {
            Example = example,
            Variant = variant,
            Constants = constants,
            Parameters = parameters,
            Rows = rows,
        };
    }

    private static double? Num(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

public sealed class GoldenRow
{
    public required ConflictRowInput Input { get; init; }
    public required List<double?> ExpectedPointIgs { get; init; }
    public required double? ExpectedFinalIg { get; init; }
    public string? ExpectedDefiningPoint { get; init; }
}
