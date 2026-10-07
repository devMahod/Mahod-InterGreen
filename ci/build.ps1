<#
.SYNOPSIS
    CI entry point for Mahod Intergreen: release gate -> tests -> scripts/release_build.py r<N>
    (keyed host for 2026 + 2027, staged bundle, .NET setup) -> the staff guide PDF ->
    scripts/package_release.py (the portal ZIP, Defender-scanned) + <OutDir>\release.json.
    Run by MahodAI-Plugin's scripts/release/Invoke-ToolRelease.ps1 on the release PC; runs by
    hand the same way.

.DESCRIPTION
    The release identity is build/MahodRelease.props (MahodEngineVersion + MahodReleaseRevision),
    so the version is "<engine>-<rev>", e.g. 0.1.0-r15. A release needs, prepared by a human:
    the props bump, docs/guides/STAFF_GUIDE_HE_<rev>.html and docs/releases/RELEASE_NOTES_<rev>.md.

    PUBLIC repository: the keyed bundle and setup stay in ignored folders (build/out/,
    installer/out/); the ZIP is written to -OutDir, outside the tree.

    Tests: the five suites that need no client data. Excel.Tests and Host.Tests read the client's
    workbooks from ..\materials\ (not in any repository) and are left to the release engineer.
    release_build.py also needs the full history (its engine-freeze gate diffs against 02c7a47):
    the workflow checks out with fetch-depth 0.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Python = 'py'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

# ---- 1. release identity -------------------------------------------------------------------
$props = ([xml](Get-Content -LiteralPath (Join-Path $repo 'build\MahodRelease.props') -Raw)).Project.PropertyGroup
$engine = $props | ForEach-Object { $_.MahodEngineVersion } | Where-Object { $_ } | Select-Object -First 1
$rev = $props | ForEach-Object { $_.MahodReleaseRevision } | Where-Object { $_ } | Select-Object -First 1
$version = "$engine-$rev"
$problems = @()
$guideHtml = Join-Path $repo "docs\guides\STAFF_GUIDE_HE_$rev.html"
if (-not (Test-Path $guideHtml)) { $problems += "docs\guides\STAFF_GUIDE_HE_$rev.html is missing" }
elseif ((Get-Content -LiteralPath $guideHtml -Raw -Encoding UTF8) -notmatch [regex]::Escape("Mahod_Intergreen_Setup_$version.exe")) {
    $problems += "STAFF_GUIDE_HE_$rev.html does not name Mahod_Intergreen_Setup_$version.exe"
}
if (-not (Test-Path (Join-Path $repo "docs\releases\RELEASE_NOTES_$rev.md"))) { $problems += "docs\releases\RELEASE_NOTES_$rev.md is missing" }
if ($problems) { $problems | ForEach-Object { Write-Host "  $_" }; throw "Mahod Intergreen $version is not ready - nothing was built" }
Write-Host "Mahod Intergreen $version"

# ---- 2. tests (no client data, no CAD host) ------------------------------------------------
foreach ($t in 'Core', 'Geometry', 'Regression', 'Rules', 'Usage') {
    $proj = Join-Path $repo "tests\Mahod.Intergreen.$t.Tests\Mahod.Intergreen.$t.Tests.csproj"
    & $dotnet test $proj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Mahod.Intergreen.$t.Tests failed" }
}
# The test run leaves only ignored bin/obj; release_build.py insists on a clean tree.
& git -C $repo status --porcelain | ForEach-Object { throw "the tree is not clean after the tests: $_" }

# ---- 3. the keyed build and the setup ------------------------------------------------------
$env:PYTHONIOENCODING = 'utf-8'
Push-Location $repo
try {
    $keyArgs = if ($env:MAHOD_CAD_KEY) { @() } else { Write-Host 'MAHOD_CAD_KEY not set: KEYLESS rehearsal build'; @('--keyless') }
    & $Python scripts/release_build.py $rev @keyArgs
    if ($LASTEXITCODE -ne 0) { throw "release_build.py failed with exit code $LASTEXITCODE" }

    # ---- 4. the guide (Edge headless; render_guide.ps1 waits a fixed 4 s, so wait for the file)
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\guides\render_guide.ps1') -Rev $rev
    $guide = Join-Path $repo "docs\guides\out\guide_staff_$rev.pdf"
    for ($i = 0; $i -lt 30 -and -not ((Test-Path $guide) -and (Get-Item $guide).Length -gt 10000); $i++) { Start-Sleep -Seconds 1 }
    if (-not (Test-Path $guide)) { throw "render_guide.ps1 did not produce $guide" }

    # ---- 5. the portal ZIP (exe + guide, Defender-scanned) --------------------------------
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    & $Python scripts/package_release.py $rev $guide --out $OutDir --staff-only
    if ($LASTEXITCODE -ne 0) { throw "package_release.py failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }
$zipName = "Mahod_Intergreen_$version.zip"
if (-not (Test-Path (Join-Path $OutDir $zipName))) { throw "package_release.py did not produce $zipName" }

$release = [ordered]@{ version = $version; zip = $zipName; setup = "Mahod_Intergreen_Setup_$version.exe" }
[IO.File]::WriteAllText((Join-Path $OutDir 'release.json'), ($release | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("ready: {0} ({1:N0} bytes)" -f (Join-Path $OutDir $zipName), (Get-Item (Join-Path $OutDir $zipName)).Length)
