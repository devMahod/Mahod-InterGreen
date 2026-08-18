# MULTIHOST_BUILD_REPORT

ONE codebase (`src/Mahod.Intergreen.AutoCAD2026`, output `Mahod.Intergreen.AutoCAD.dll`),
built twice via `-p:AutoCADVersion=`:

| Target | TFM | Autodesk refs (Private=false, never shipped) | Output SHA-256 |
|---|---|---|---|
| 2026 | net8.0-windows | C:\Program Files\Autodesk\AutoCAD 2026 (acdbmgd/acmgd/accoremgd) | 207c647036d303d8c8f85c8357454755658cef04f6eb16bf5d6f5fb2a533a80b |
| 2027 | net10.0-windows | C:\Program Files\Autodesk\AutoCAD 2027 (acdbmgd/acmgd/accoremgd) | f4414d1ec31be40c0561745558b2ed56948973aaf6d52df2684a2c05e3231acf |

Wrong-year resolution is impossible: the reference dir is pinned per AutoCADVersion and a
build-time guard errors if the pinned acdbmgd.dll is absent. Separate obj/bin per year.
The only year-specific code artifact is the `HostBuild.Year` constant (§10 diagnostics).

Shared layers (single net8.0 build consumed by BOTH hosts — byte-identical everywhere):
6 engineering DLLs (validated 0.1.0 bytes, hash-gated) + Mahod.Intergreen.Host.dll +
third-party closure (ClosedXML set + SixLabors.Fonts + RBush).
Engineering source changes for host compatibility: NONE (identity gate §11-A holds — no
recompilation of engineering assemblies was needed).
.NET 10 SDK 10.0.400 installed from the official Microsoft winget source (authorized).
