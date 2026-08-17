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

## Pilot hardening round (branch pilot-hardening)
- §21B CHECKPOINT: CORE HARDENING PASS — 167/167; IG-GEO-005 32→0; ped W exact (ΔCD=0.000);
  N-edge crossings; BlockRef recursion (DBText label found); 0 unsafe; Ex1 matrix 24V/0B;
  Ex2 REVISION_MISMATCH detected, 0 blocked. Residuals 11/11 classified.
- NEXT: Excel writer (§22-29) → 2025 pipeline+conformance (§33-39) → overrides (§40-41) →
  Legacy <3s floor (§42) → INTERGREEN workflow (§43-46) → bundle (§47) → docs (§48-50) →
  final acceptance (§51-60) → Pilot Candidate ZIP (READY FOR INDEPENDENT VERIFICATION).

## Pilot Candidate (branch pilot-hardening)
- Directive completed through §60: core hardening PASS, Excel writer PASS (both examples,
  0 structural issues), 2025 conformance 27/27 + e2e, overrides+floor, INTERGREEN palette,
  bundle+install scripts, Vadim doc, 5 Hebrew docs, rule packs VALIDATED with structured
  source hashes. Suite 210/210. FINAL_PILOT_VERIFICATION_REPORT.md issued.
- STATUS: READY FOR INDEPENDENT VERIFICATION (never auto-approved; §56 GUI smoke =
  AWAITING_ARTHUR_MANUAL_SMOKE).

## 0.1.1 — documentation/packaging alignment round (LOCKED directive)
- Scope lock respected: ZERO changes to *.cs / *.csproj / *.sln / rules / tests / DLLs.
- Locked facts verified against artifacts BEFORE editing (never rubber-stamped):
  Ex1 all-40 comparison 38 equal / 2 engine-higher (row 82: 4->5 via W-L.b2@end CD 28.52
  raw 4.14; row 89: 6->7 via a.band ED=0 raw 6.958) / 0 engine-lower; residual criterion
  (nearest |dCD|>0.05 OR |dED|>0.05; row 82 included via governing candidate) = 12 rows,
  10 IG-equal; Ex2 matrix artifact = 37 VALID / 7 REVIEW / 0 BLOCKED (my earlier prose
  said 38/6 — corrected tree-wide).
- Corrected: RESIDUAL_CLASSIFICATION, FINAL_PILOT_VERIFICATION_REPORT, CAD_SMOKE_TEST
  (31-step §14 sequence), KNOWN_LIMITATIONS_HE, RESULT_SCHEMA (actual contract),
  README_INSTALL_HE (packaged 07_AUTOCAD_BUNDLE path + troubleshooting),
  QUICK_START_DAVID_HE (no-auto-drawing statement), USER_GUIDE_HE (32 topics).
- New: START_HERE_HE, USER_GUIDE_DAVID_ILLUSTRATED_HE (20 real-screenshot placeholders,
  none fabricated), SCREENSHOT_CAPTURE_CHECKLIST_HE, ARTHUR_GUI_SMOKE_RECORD_HE,
  LIN_PREPILOT_CHECKLIST_HE, PILOT_FEEDBACK_HE, DAVID_PILOT_REVIEW_AGENDA_HE,
  API_EXAMPLES_VADIM, VADIM_IMPLEMENTATION_CHECKLIST_HE, CIVIL_INTEGRATION_BOUNDARY;
  INTEGRATION_VADIM updated (required diagram + host responsibilities).
- STATUS: READY FOR ARTHUR GUI SMOKE (never APPROVED FOR DAVID PILOT; screenshots + GUI
  record = AWAITING_ARTHUR_GUI_SMOKE).

## Productization round (Stage 1) — installer + role packages (2026-08-17)
- Cowork GUI smoke on this machine: ENVIRONMENT-BLOCKED by Autodesk licensing (upi.exe
  crashes before INTERGREEN) — evidence preserved; GUI gate moved to Lin.
- ED-010 stale 'crossing b remains blocked' corrected (final: 37/7/0); sweep clean;
  sealed 0.1.1 ZIP left byte-stable with correction record in master.
- Built Mahod_Intergreen_Setup_0.1.0.exe (self-contained WinForms; per-user; Apps-list
  uninstall; AutoCAD-running guard; unsigned+documented). File-level validation 16/16
  PASS incl. silent install/uninstall/reinstall with full hash identity to dist.
- Built role packages: LIN_PREPILOT (installer+TestKit+7 HE PDFs+return structure),
  DAVID_PILOT_DRAFT (NOT FOR RELEASE; screenshots pending Lin), VADIM_INTEGRATION
  (docs+real contract instances), ENGINEERING_MASTER_0.1.1-r1 (sealed 0.1.1 + Cowork
  evidence + audits + manifests). 18 Hebrew/EN PDFs generated (Edge headless, RTL QA'd).
- STATUS: READY FOR LIN GUI SMOKE. Machine residue: renamed locked bundle directory
  awaiting cleanup after the machine's pending maintenance (documented).

## Vadim r3 + CRITICAL discovery (2026-08-17 late)
- Built VADIM_INTEGRATION_TEST_0.1.0-r3: RU role docs, real TEST_KIT (Ex1 golden +
  Ex2 robustness), SDK (validated DLLs byte-identical + rules + contracts), and a
  compile+RUN-verified adapter example that reproduces the golden numbers end-to-end
  (11 movements / 50 conflicts / matrix 24-0-0 / W-L->S-T IG 5, governing X/Y exact).
- CRITICAL: the run-verified example exposed that dist/Mahod.Intergreen.bundle (and the
  installer payload) is MISSING ClosedXML runtime dependencies SixLabors.Fonts.dll and
  RBush.dll (present in CLI/AutoCAD build outputs, dropped during bundle curation).
  The palette Setup (WorkbookReader.Read) would crash with FileNotFoundException inside
  AutoCAD -> Lin's GUI smoke would FAIL at step 6. Never caught because: CLI ran from
  its own bin (deps present) and the GUI path was never executed (Cowork env-blocked).
- Per directive stop conditions: NO fix applied (installer/payload locked) — reported
  to Arthur for a respin decision (third-party DLLs only; zero Mahod DLL changes;
  full installer revalidation required after).
- LIN r3 email is ON HOLD until the respin decision.

## EMERGENCY Lin replacement (authorized respin, 2026-08-17 night)
- Bundle runtime closure FIXED: added SixLabors.Fonts.dll (e1e2f107...c45d9f) +
  RBush.dll (047e48ab...ab6a1) to dist bundle Contents (exact validated bytes from the
  0.1.0 build output). ZERO Mahod DLL changes (7/7 byte-identical incl. AutoCAD host).
- Runtime closure test (new, mandatory): runner executed FROM a clean copy of the exact
  shipping bundle (WindowsDesktop framework = AutoCAD-faithful; Private=false refs so
  nothing resolves from bin/NuGet/SDK) AND from the installed ApplicationPlugins path.
  Both PASS with golden numbers (11 movements / 50 conflicts / 24-0-0 / W-L->S-T IG 5);
  ClosedXML+SixLabors.Fonts+RBush all loaded from the shipping folder.
- Installer r2 (Mahod_Intergreen_Setup_0.1.0-r2.exe, 5862975d...) validated 13/13;
  old installer + LIN r3 SUPERSEDED - DO NOT DISTRIBUTE (retained in superseded/).
- LIN r4 built (73c6d2b7...): identical docs/kit to r3 except installer refs + Hebrew
  superseded notice; urgent correction email produced (exact directive text).
- LESSON (added to validation doctrine): file-hash identity of a package is NOT runtime
  closure - always execute the real workload from the exact shipping folder.
