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
| G | Rule Pack framework | PASS | rules/ packs load+validate; RulePackTests 24 tests | — | management commands at gate T |
| H | Classification + parameter resolution | PASS | ParameterResolver + IG-CLS-001/002/003 tests | — | — |
| I | Neutral line/arc/polycurve geometry | PASS | exact line/arc/polycurve, bulge conversion verified vs ezdxf + first-principles, JSON round-trip | — | — |
| J | Synthetic geometry tests | PASS | 20 fixtures: line/arc/arc-arc, tangent, dedup, reversal invariance, transform invariance, mm/m, disconnection | — | — |
| K | Legacy envelope strategy | PASS | LegacyEnvelopeConflictStrategy: all points kept, POSSIBLE_UNRESOLVED_CONFLICT, reversal invariant | — | — |
| L | 2025 lane-centreline strategy | PASS | LaneCentrelineConflictStrategy: m×n lanes → 4/2/3 per §5.4 examples; 2025_GEOMETRY_MISSING blocks envelope input | — | — |
| M | 2025 formula engine (official §5.5–5.6) | PASS | Israel2025CalculationService, tables transcribed first-hand from §5.3 (pdf 148-153), 45 Core tests | — | — |
| N | Excel readers / template adapters V1+V2 | PASS (reader) | WorkbookReader: content-based V1/V2 detection, C# reads ORIGINAL xlsx → engine reproduces 148/148 FINAL+intermediates locally (Hotfix §13 closed in C#); SOURCE_WORKBOOK_EXISTING_ERROR surfaces AutoAdjusted #REF! | — | writer at gate S/G |
| O | AutoCAD 2026 adapter | PASS | project builds; NETLOAD in real accoreconsole PASS; IG_SCAN PASS; IG_EXPORT_GEOMETRY PASS (exact bulge geometry + handles) | — | palette/UI = P1 |
| P | Real example DWG scan/export | PASS | both DWGs ran end-to-end: scan+export in AutoCAD → ig-analyze → analysis/validation/run_manifest + §10 regression reports | — | STOP per Directive §16 |
| Q | Real geometry regression | PASS | Ex1: 28/40 MATCH, 0 LOWER_ERROR; Ex2: 28/104 MATCH, 0 LOWER_ERROR, 4 INVALID_BASELINE; per-row deltas in EXAMPLE*_REPORT.md | — | review of unmatched rows = engineering session |
| R | Signal-group matrix | PASS | SignalGroupMatrixService + Tests A/B/C + §17A acceptance: 4/4 incomplete detected, exactly 3 blocked cells (2→4, 4→2, 1→a), 0 unexpected, 0 missing; report test-results/EXAMPLE2_MATRIX_ACCEPTANCE.md | — | — |
| S | Deterministic outputs (analysis/validation/run_manifest) | PASS | AnalysisPipeline + AnalysisWriters; determinism byte-identical test; INTERNAL_NUMERIC_INVARIANT_FAILURE on NaN/negative; env noise excluded; BLOCKED propagation in document | — | Excel writer deferred post-P per Directive §13 |
| T | INTERGREEN workflow / basic UI | NOT STARTED | | | |
| U | Bundle packaging | NOT STARTED | | | |
| V | Vadim integration docs | NOT STARTED | | | |
| W | Full suite re-run | NOT STARTED | | | |

## Checkpoint log

- T+0: pre-flight PASS (SDK 8.0.415, ACAD_OK, CORECONSOLE_OK, git 2.52.0). Working root confirmed: `C:\Users\arthurf\Downloads\InterGreens\Mahod.Intergreen`.

- T+60 checkpoint written: test-results/T60_CHECKPOINT.md — 39/39 tests, 148/148 golden, 4/4 findings, 13-row delta. Next: Gate G.

- FINAL HOTFIX BLOCK applied (before gate I): stable production numerics + max(0,a) clamp,
  compat 80km/h singularity reproduction, Movement.Mode branching, structured outcomes,
  metamorphic ped tests, continuity tests, full intermediate-column comparison (P/Q/R + AL)
  over all 148 rows. Suite: 98/98 (61 Core + 24 Rules + 13 Regression).
  NOTE: doc #4 "HOTFIX NOTES" was never delivered — only #5 FINAL HOTFIX BLOCK (which supersedes it).

- FINAL CONTINUATION DIRECTIVE received: scope frozen to S→O→P, stop after P. _scratch was never tracked (gitignored from commit 1). Baseline 143/143 re-verified before Gate S.

- GATE P complete. STOP point reached per FINAL CONTINUATION DIRECTIVE §16. Suite 149/149.
