# Mahod Intergreen - user-level Pilot install (no admin required)
$src = Join-Path $PSScriptRoot "Mahod.Intergreen.bundle"
$dst = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle"
if (-not (Test-Path $src)) { Write-Error "bundle not found next to this script"; exit 1 }
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
Copy-Item $src $dst -Recurse
Write-Host "Installed to $dst"
Write-Host "Start AutoCAD 2026 and run the INTERGREEN command."
