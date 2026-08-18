using System.Reflection;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>
/// Shipped build identity (traceability audit 2026-08-18). Values are stamped by
/// build/MahodRelease.props at compile time — never hand-typed, so a shipped binary can
/// never report a stale revision or Git SHA.
/// </summary>
internal static class HostBuild
{
#if ACADHOST2027
    public const string Year = "2027";
#else
    public const string Year = "2026";
#endif

    private static string Meta(string key)
        => typeof(HostBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
               .FirstOrDefault(a => a.Key == key)?.Value ?? "?";

    public static string EngineVersion => Meta("MahodEngineVersion");
    public static string ReleaseRevision => Meta("MahodReleaseRevision");
    public static string GitSha => Meta("MahodGitSha");

    /// <summary>e.g. "0.1.0-r6 host2026 git a164e88..." — used in the support log,
    /// run manifests and the in-process smoke output.</summary>
    public static string ReleaseId =>
        $"{EngineVersion}-{ReleaseRevision} host{Year} git {GitSha}";
}
