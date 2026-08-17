# TASK_LEDGER — Mahod Intergreen (v3 §59 gates)

Legend: NOT STARTED / ACTIVE / PASS / FAIL / BLOCKED / NOT RUN

| Gate | Deliverable | Status | Evidence | Blocking issue | Next action |
|---|---|---|---|---|---|
| A | Inventory + hash all source material | PASS | docs/SOURCE_INVENTORY.md (15 files hashed) | — | — |
| B | Source-reference map | PASS | ENGINEERING_DECISIONS.md + OPEN_QUESTIONS.md source refs | — | — |
| C | Solution / contracts skeleton | PASS | build clean, commit 4ef2c64 | — | — |
| D | Legacy compatibility engine | PASS | LegacyWorkbookCompatibilityCalculator + 26 unit tests | — | — |
| E | 148/148 golden (FINAL + intermediates) | PASS | GoldenLegacyTests 148/148+148/148; oracle GOLDEN_EXTRACTION_REPORT.md | — | re-verify via C# XLSX reader at gate N |
| F | Production validation + 4-row test (18/26/34/60) | PASS | ProductionValidationRegressionTests: 18/60=IG-VAL-001, 26/34=IG-VAL-002 | — | — |
| G | Rule Pack framework | NOT STARTED | | | |
| H | Classification + parameter resolution | NOT STARTED | | | |
| I | Neutral line/arc/polycurve geometry | NOT STARTED | | | |
| J | Synthetic geometry tests | NOT STARTED | | | |
| K | Legacy envelope strategy | NOT STARTED | | | |
| L | 2025 lane-centreline strategy | NOT STARTED | | | |
| M | 2025 formula engine (official §5.5–5.6) | NOT STARTED | | | |
| N | Excel readers / template adapters V1+V2 | NOT STARTED | | | |
| O | AutoCAD 2026 adapter | NOT STARTED | | | |
| P | Real example DWG scan/export | NOT STARTED | | | needs O; interactive if coreconsole fails |
| Q | Real geometry regression | NOT STARTED | | | needs P |
| R | Signal-group matrix | NOT STARTED | | | |
| S | Final Excel + JSON export | NOT STARTED | | | |
| T | INTERGREEN workflow / basic UI | NOT STARTED | | | |
| U | Bundle packaging | NOT STARTED | | | |
| V | Vadim integration docs | NOT STARTED | | | |
| W | Full suite re-run | NOT STARTED | | | |

## Checkpoint log

- T+0: pre-flight PASS (SDK 8.0.415, ACAD_OK, CORECONSOLE_OK, git 2.52.0). Working root confirmed: `C:\Users\arthurf\Downloads\InterGreens\Mahod.Intergreen`.

- T+60 checkpoint written: test-results/T60_CHECKPOINT.md — 39/39 tests, 148/148 golden, 4/4 findings, 13-row delta. Next: Gate G.
