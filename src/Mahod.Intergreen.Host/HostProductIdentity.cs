namespace Mahod.Intergreen.Host;

/// <summary>
/// Real host-product identification (r9). AutoCAD's PRODUCT system variable reports
/// "AutoCAD" even inside Civil 3D, so the support log lied about the host. Civil 3D is
/// identified by evidence that cannot be faked by branding: the AECC managed modules
/// (AeccDbMgd and friends) are loaded ONLY when the Civil vertical is actually running.
/// Pure function over the loaded-assembly list so it is unit-testable without Autodesk.
/// </summary>
public static class HostProductIdentity
{
    /// <summary>"Civil 3D" when any AECC (Civil) managed module is loaded; otherwise the
    /// host-reported product name (defaulting to "AutoCAD").</summary>
    public static string Detect(IEnumerable<string?> loadedAssemblyNames, string? reportedProduct)
    {
        foreach (var name in loadedAssemblyNames)
        {
            if (name is not null && name.StartsWith("Aecc", StringComparison.OrdinalIgnoreCase))
                return "Civil 3D";
        }
        return string.IsNullOrWhiteSpace(reportedProduct) ? "AutoCAD" : reportedProduct!;
    }
}
