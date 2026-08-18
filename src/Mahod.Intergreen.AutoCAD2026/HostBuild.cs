namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>Compile-time host build identity (Multi-Host directive §10). One codebase,
/// year-correct builds — this constant is the only year-specific artifact.</summary>
internal static class HostBuild
{
#if ACADHOST2027
    public const string Year = "2027";
#else
    public const string Year = "2026";
#endif
}
