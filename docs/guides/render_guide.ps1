# Renders docs/guides/STAFF_GUIDE_HE_<rev>.html to docs/guides/out/guide_staff_<rev>.pdf with Edge headless.
#   powershell -NoProfile -ExecutionPolicy Bypass -File docs/guides/render_guide.ps1 -Rev r14
param([string]$Rev = "r14")
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "C:\Program Files\Microsoft\Edge\Application\msedge.exe" }
$outDir = Join-Path $dir "out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir "guide_staff_$Rev.pdf"
if (Test-Path $out) { Remove-Item $out -Force }
$html = (Join-Path $dir "STAFF_GUIDE_HE_$Rev.html") -replace '\\', '/'
& $edge --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$out" "file:///$html" | Out-Null
Start-Sleep 4
Get-Item $out | Select-Object Name, Length
