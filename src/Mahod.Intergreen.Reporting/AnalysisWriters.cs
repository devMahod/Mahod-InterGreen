using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mahod.Intergreen.Contracts;

namespace Mahod.Intergreen.Reporting;

/// <summary>INTERNAL_NUMERIC_INVARIANT_FAILURE — an impossible value reached serialization (Directive §6).</summary>
public sealed class InternalNumericInvariantException : Exception
{
    public InternalNumericInvariantException(string detail)
        : base("INTERNAL_NUMERIC_INVARIANT_FAILURE: " + detail) { }
}

public static class AnalysisWriters
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes analysis.json after the hard numeric-invariant check.</summary>
    public static string WriteAnalysisJson(AnalysisDocument doc)
    {
        EnforceInvariants(doc);
        return JsonSerializer.Serialize(doc, Options);
    }

    /// <summary>validation.json — full structured findings, deterministically ordered.</summary>
    public static string WriteValidationJson(IEnumerable<ValidationFinding> findings)
    {
        var ordered = findings
            .Select(f => new
            {
                code = f.Code,
                severity = f.Severity.ToString().ToUpperInvariant(),
                conflictRef = f.ConflictRef,
                message = f.Message,
                technicalDetails = f.TechnicalDetails,
                recommendedAction = f.RecommendedAction,
                sourceReference = f.SourceReference,
            })
            .OrderBy(f => f.code, StringComparer.Ordinal)
            .ThenBy(f => f.conflictRef, StringComparer.Ordinal)
            .ThenBy(f => f.message, StringComparer.Ordinal)
            .ToList();
        return JsonSerializer.Serialize(new { schemaVersion = AnalysisPipeline.SchemaVersion, findings = ordered }, Options);
    }

    /// <summary>run_manifest.json — the ONLY place for environment/run data (v3 §41).</summary>
    public static string WriteRunManifestJson(
        string runId, DateTimeOffset startedAt, TimeSpan duration,
        string sourceFileName, string sourceSha256, string? application = null)
    {
        var manifest = new
        {
            runId,
            startedAt = startedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            durationSeconds = Math.Round(duration.TotalSeconds, 3),
            engineVersion = AnalysisPipeline.EngineVersion,
            application,
            machine = Environment.MachineName,
            sourceFile = sourceFileName,
            sourceSha256,
        };
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static string Sha256OfFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    /// <summary>
    /// Hard defence (Final Hotfix §11): no NaN / Infinity / invalid negative engineering
    /// value may be serialized. Reaching here with one is an internal defect — fail loudly.
    /// </summary>
    private static void EnforceInvariants(AnalysisDocument doc)
    {
        void Check(double? v, string what)
        {
            if (v is not double d) return;
            if (double.IsNaN(d) || double.IsInfinity(d))
                throw new InternalNumericInvariantException($"{what} = {d}");
        }
        void CheckNonNegative(double? v, string what)
        {
            Check(v, what);
            if (v is double d && d < 0)
                throw new InternalNumericInvariantException($"{what} = {d} (negative engineering value)");
        }

        foreach (var c in doc.Conflicts)
        {
            CheckNonNegative(c.RawIntergreenSec is double r && c.Status == "VALID" ? r : null,
                $"conflict {c.Id} rawIntergreenSec");
            Check(c.RawIntergreenSec, $"conflict {c.Id} rawIntergreenSec");
            if (c.FinalIg is int f && f < 0)
                throw new InternalNumericInvariantException($"conflict {c.Id} finalIg = {f}");
            foreach (var p in c.Points)
            {
                CheckNonNegative(p.Cd, $"conflict {c.Id} {p.Id} CD");
                CheckNonNegative(p.Ed, $"conflict {c.Id} {p.Id} ED");
                Check(p.RawIg, $"conflict {c.Id} {p.Id} rawIg");
                Check(p.X, $"conflict {c.Id} {p.Id} X");
                Check(p.Y, $"conflict {c.Id} {p.Id} Y");
            }
            if (c.Status == "VALID" && c.FinalIg is null)
                throw new InternalNumericInvariantException(
                    $"conflict {c.Id} is VALID but has no final intergreen (unresolved mandatory value)");
        }
        foreach (var cell in doc.Matrix)
        {
            if (cell.Status == "VALID" && cell.Value is null)
                throw new InternalNumericInvariantException(
                    $"matrix cell {cell.ClearingSignalGroup}→{cell.EnteringSignalGroup} VALID without value");
            if (cell.Value is int v && v < 0)
                throw new InternalNumericInvariantException(
                    $"matrix cell {cell.ClearingSignalGroup}→{cell.EnteringSignalGroup} value {v}");
        }
    }

    /// <summary>Writes the three artefacts for one run. analysis/validation are deterministic.</summary>
    public static (string AnalysisPath, string ValidationPath, string ManifestPath) WriteAll(
        string outputDirectory, string baseName, AnalysisDocument doc,
        IEnumerable<ValidationFinding> findings,
        string runId, DateTimeOffset startedAt, TimeSpan duration, string? application = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var analysisPath = Path.Combine(outputDirectory, baseName + ".analysis.json");
        var validationPath = Path.Combine(outputDirectory, baseName + ".validation.json");
        var manifestPath = Path.Combine(outputDirectory, baseName + ".run_manifest.json");
        File.WriteAllText(analysisPath, WriteAnalysisJson(doc), new UTF8Encoding(false));
        File.WriteAllText(validationPath, WriteValidationJson(findings), new UTF8Encoding(false));
        File.WriteAllText(manifestPath, WriteRunManifestJson(runId, startedAt, duration,
            doc.SourceGeometry.FileName, doc.SourceGeometry.Sha256, application), new UTF8Encoding(false));
        return (analysisPath, validationPath, manifestPath);
    }
}
