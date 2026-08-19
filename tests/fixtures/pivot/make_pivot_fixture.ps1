# Builds tests/fixtures/pivot/pivot-regression.xlsx with REAL Microsoft Excel (COM, acceptance
# tooling only — Excel is never a runtime dependency of the plugin).
#
# The fixture mirrors the shape of David's IG_matrix workbooks that matters for Lin's r10
# refresh finding:
#   * 'Input Distances' — header row 2, conflict rows from row 3 (B=clearing, C=entering, D/E=CD/ED);
#   * 'Calc'            — formulas feeding a pivot source (Clearing SG / Entering SG / FINAL IG);
#   * 'Matrix'          — a PivotTable (Max of FINAL IG by Clearing SG × Entering SG) whose cache
#                         was refreshed by Excel with the ORIGINAL values (stale after an export).
# Excel also writes xl/calcChain.xml, so the calcChain regression is covered by the same file.
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "pivot-regression.xlsx"
if (Test-Path $out) { Remove-Item $out -Force }
$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false; $xl.DisplayAlerts = $false
$wb = $xl.Workbooks.Add()
while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }
$inp = $wb.Worksheets.Item(1); $inp.Name = "Input Distances"
$inp.Range("A2").Value2 = "Conflict No."; $inp.Range("B2").Value2 = "Clearing Movement"; $inp.Range("C2").Value2 = "Entering Movement"
$inp.Range("D2").Value2 = "CD"; $inp.Range("E2").Value2 = "ED"; $inp.Range("F2").Value2 = "CD"; $inp.Range("G2").Value2 = "ED"
$inp.Range("A3").Value2 = 1; $inp.Range("B3").Value2 = "X"; $inp.Range("C3").Value2 = "Y"; $inp.Range("D3").Value2 = 10; $inp.Range("E3").Value2 = 2
$inp.Range("A4").Value2 = 2; $inp.Range("B4").Value2 = "X"; $inp.Range("C4").Value2 = "Z"; $inp.Range("D4").Value2 = 20; $inp.Range("E4").Value2 = 4
$inp.Range("A5").Value2 = 3; $inp.Range("B5").Value2 = "W"; $inp.Range("C5").Value2 = "Y"; $inp.Range("D5").Value2 = 30; $inp.Range("E5").Value2 = 6
# a formula OUTSIDE the CD/ED slots that must survive any export untouched
$inp.Range("M2").Value2 = "check"; $inp.Range("M3").Formula = "=D3*2"

$calc = $wb.Worksheets.Add([Type]::Missing, $inp); $calc.Name = "Calc"
$calc.Range("A1").Value2 = "Clearing SG"; $calc.Range("B1").Value2 = "Entering SG"; $calc.Range("C1").Value2 = "FINAL IG"
for ($r = 2; $r -le 4; $r++) {
  $i = $r + 1
  $calc.Range("A$r").Formula = "='Input Distances'!B$i"
  $calc.Range("B$r").Formula = "='Input Distances'!C$i"
  # a legacy-like rule: (CD - ED) / 2 rounded up to whole seconds
  $calc.Range("C$r").Formula = "=ROUNDUP(('Input Distances'!D$i-'Input Distances'!E$i)/2,0)"
}

$mx = $wb.Worksheets.Add([Type]::Missing, $calc); $mx.Name = "Matrix"
$cache = $wb.PivotCaches().Create(1, "Calc!R1C1:R4C3")         # xlDatabase = 1
$pt = $cache.CreatePivotTable("Matrix!R3C3", "PivotTable1")
$pt.PivotFields("Clearing SG").Orientation = 1                  # xlRowField
$pt.PivotFields("Entering SG").Orientation = 2                  # xlColumnField
$df = $pt.AddDataField($pt.PivotFields("FINAL IG"), "Max of FINAL IG", -4136)   # xlMax
$pt.RowGrand = $false; $pt.ColumnGrand = $false
$pt.RefreshTable() | Out-Null
$xl.CalculateFull()
$wb.SaveAs($out, 51)                                            # xlOpenXMLWorkbook
$wb.Close($false); $xl.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
Write-Host "wrote $out"
