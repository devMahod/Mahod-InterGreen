namespace Mahod.Intergreen.Host.Tests;

/// <summary>Repo layout discovery + temp fixture helpers (real filesystem fixtures per §16).</summary>
public static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string MaterialsRoot { get; } =
        Path.GetFullPath(Path.Combine(RepoRoot, "..", "materials", "Inter-green Automation"));
    public static string Example1Workbook { get; } =
        Path.Combine(MaterialsRoot, "03 Example 1", "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx");
    public static string Example2Workbook { get; } =
        Path.Combine(MaterialsRoot, "04 Example 2", "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx");
    public static string Example1Geometry { get; } =
        Path.Combine(RepoRoot, "test-results", "dwg-work", "ex1.iggeometry.json");
    public static string RulesLegacy { get; } =
        Path.Combine(RepoRoot, "rules", "legacy-mahod-v1");

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) &&
                Directory.Exists(Path.Combine(d.FullName, "rules")))
                return d.FullName;
            d = d.Parent!;
        }
        throw new InvalidOperationException("repo root not found");
    }

    public static string NewTempDir(string hint = "ig-host-test")
    {
        string p = Path.Combine(Path.GetTempPath(), $"{hint}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }
}
