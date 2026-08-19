# Pivot-refresh regression fixture (r11)

`pivot-regression.xlsx` is a workbook written by REAL Microsoft Excel (`make_pivot_fixture.ps1`,
COM — acceptance tooling only; Excel is never a runtime dependency of the plugin):

* `Input Distances` — header row 2, conflict rows from row 3 (`X→Y` CD 10/ED 2, `X→Z` 20/4,
  `W→Y` 30/6); `M3` = a formula outside the CD/ED slots that must survive any export.
* `Calc` — formulas feeding a pivot source (Clearing SG / Entering SG / FINAL IG =
  `ROUNDUP((CD-ED)/2,0)`).
* `Matrix` — a PivotTable "Max of FINAL IG" by Clearing SG × Entering SG whose cache was
  refreshed by Excel with the ORIGINAL values (records 4 / 8 / 12) — exactly the stale state the
  r10 exporter shipped (it copied the pivot parts untouched).
* Excel also wrote `xl/calcChain.xml`, so the calcChain regression is covered by the same file.

Consumed by `tests/Mahod.Intergreen.Excel.Tests/WorkbookWriterTests.cs`
(`Pivot_fixture_*`, `Export_never_ships_a_stale_pivot_*`). Lin's finding: the export changed an
underlying value (X→Y CD 10 → 30) while the cached pivot still said 4 — visible as a different
matrix before and after Refresh. r11: refreshOnLoad + purged records + cleared rendered cells.
