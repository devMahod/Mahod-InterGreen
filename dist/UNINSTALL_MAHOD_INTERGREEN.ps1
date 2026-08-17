$dst = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle"
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force; Write-Host "Removed $dst" } else { Write-Host "Not installed." }
