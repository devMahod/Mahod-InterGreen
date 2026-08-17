# FINAL_PILOT_VERIFICATION_REPORT — Mahod Intergreen 0.1.0

Branch `pilot-hardening`. All numbers below are from actual runs on 2026-08-17.

```
FULL TEST SUITE: 210 / 210
  Core 93 · Geometry 49 · Rules 24 · Regression 30 · Excel 14

LEGACY
Golden FINAL: 148 / 148
Golden intermediates: 148 / 148   (incl. P/Q/R per-point time columns + AL flag)
13-row historical/current rounding delta: reproduced
4 incomplete historical rows: reproduced in compatibility, blocked in production
duplicate conflict 8 / missing 9: detected, not silently corrected
production <3 s safety floor: active (REVIEW_REQUIRED + conservative 3 s; compatibility untouched)

EXAMPLE 1  (same-revision primary geometry Golden)
manual rows:                    40
manual measured points:         40 (P1–P4 all evaluated)
IG-GEO-005:                     0        (was 32 AABB false positives)
VERY_CLOSE <=1cm:               CD 18/40 · ED 18/40   (classification: 7 points fully VERY_CLOSE)
<=2cm:                          CD 21/40 · ED 20/40
<=5cm:                          CD 37/40 · ED 30/40   (29 points MATCH/SMALL_DELTA)
>5cm residual:                  11 — each individually classified (RESIDUAL_CLASSIFICATION.md),
                                all with engine Final IG == manual Final IG
unresolved unsafe discrepancies: 0
matrix VALID:                   24
matrix REVIEW_REQUIRED:         0
matrix BLOCKED:                 0

EXAMPLE 2
status:                         REVISION_MISMATCH (auto-detected: manual CD 24.07 m exceeds
                                longest current boundary 20.02 m) — robustness dataset only
multipart crossing b (6 edges): PASS  (project W = 16.00; 0 blocked cells)
nested BlockReference:          PASS  (recursively resolved; content = DBText label, reported
                                with provenance path 6549/6602)
degenerate geometry handling:   PASS  (5773 → DEGENERATE_GEOMETRY_EXCLUDED)
known validation findings:      PASS  (debris, endpoint-reference confirmations E-L.b1 + S-L.b2
                                via sidecar, width source, 4 incomplete workbook rows)
matrix:                         38 VALID / 6 REVIEW_REQUIRED / 0 BLOCKED

2025
official conformance tests:     27 / 27   (נספח 1 תוספת א' categories א–ז, ≥2 examples per case,
                                expected values hand-derived from ch.5 and script-verified)
mode-pair cases:                15 / 15   (ModePairPolicy; bus/sherut → vehicle class)
synthetic end-to-end:           PASS      (centrelines → params → calc → matrix → analysis.json,
                                deterministic)
near-side 1.5 m separation:     PASS      (2025 only; Legacy guard test)
formula separation diagnostic:  PASS      (+0.242 s-class mean signature on historical vehicle rows)
real-project geometry Golden:   NOT AVAILABLE (legacy DWGs contain no lane centrelines —
                                2025_GEOMETRY_MISSING is the enforced safe outcome)

EXCEL
Example1 export: PASS  (40 rows populated, 10 rows >4 points flagged, 0 structural issues)
Example2 export: PASS  (78 rows populated, 36 rows >4 points flagged, 0 structural issues)
structural preservation: PASS  (sheets/order/formulas/images verified; surgical OpenXML edits;
                        pre-existing #REF! reported, untouched)
formula/engine cross-check: PARTIAL — engine-authoritative sheets written and fullCalcOnLoad
                        forced; a desktop-Excel recalculation integration test was NOT run
                        (Excel COM not exercised in this environment; see CAD_SMOKE step 8)
new spreadsheet errors: 0

AUTOCAD PILOT
bundle install:        NOT RUN  (scripts prepared: dist/INSTALL_MAHOD_INTERGREEN.ps1)
INTERGREEN command:    NOT RUN in GUI — AWAITING_ARTHUR_MANUAL_SMOKE (docs/CAD_SMOKE_TEST.md)
headless plugin load:  PASS     (NETLOAD in accoreconsole on both real DWGs)
headless scan/export:  PASS     (IG_SCAN + IG_EXPORT_GEOMETRY, exact bulge geometry + handles)
project persistence:   implemented (sidecar); GUI verification part of the manual smoke
show conflict:         implemented (transient highlight + zoom); GUI verification in smoke
Excel export via palette: implemented; verified through the identical shared code path (CLI)

RULES
Legacy status: VALIDATED (human approval pending David pilot sign-off)
2025 status:   VALIDATED (conformance suite green; never auto-APPROVED)
source hashes complete: PASS  (structured SourceDocuments with SHA-256 in both manifests)
override audit tests:   PASS  (SAFETY_REDUCING_OVERRIDE gate, base value permanent)

FINAL STATUS:
READY FOR INDEPENDENT VERIFICATION

Blocking issues: NONE
Open before David (not blocking independent review):
  1. AWAITING_ARTHUR_MANUAL_SMOKE — the 5-minute GUI procedure in docs/CAD_SMOKE_TEST.md
  2. OPEN_QUESTIONS.md items for David (OQ-001…OQ-007)
```

Full disclosure of remaining limitations: docs/KNOWN_LIMITATIONS_HE.md (Directive §60).
This build is NOT labelled production-ready, approved, or ready-for-David (Directive §57).
