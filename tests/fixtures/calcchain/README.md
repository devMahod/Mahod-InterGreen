# calcChain regression fixture (r10)

`calcchain-regression.xlsx` is a minimal, hand-written OOXML workbook that carries an
`xl/calcChain.xml` part — exactly like David's real `IG_matrix` workbooks saved by Excel.

* `Input Distances!D3` — formula cell the exporter REPLACES with an engine value
  (the Lin case: Excel's cached chain still lists it → "Removed Records: Formula from
  /xl/calcChain.xml part" → file opens as `[Repaired]`).
* `Input Distances!F3`, `Other!A1` — formulas that must survive untouched.
* `xl/calcChain.xml` lists all three (`i` = sheetId).

Regenerate with `python make_calcchain_fixture.py` (deterministic).
Consumed by `tests/Mahod.Intergreen.Excel.Tests/WorkbookWriterTests.cs`
(`Export_of_calcchain_fixture_*`).
