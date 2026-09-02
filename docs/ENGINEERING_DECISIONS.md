# ENGINEERING_DECISIONS

Every non-trivial interpretation, with source, alternatives, impact and the test proving it.

## ED-001 — Two named rounding strategies; neither chosen for David

- **Decision**: implement `mahod-legacy-0.1` (floor when fraction < 0.1, else floor+1; undefined below 1 s) and `guidelines-ceil` (§5.7 plain ceiling). Legacy policy carries the workbook rule; 2025 policy carries the ceiling + 3 s minimum.
- **Why**: the workbook's `<0.1 → round down` rule is a **superseded official historical requirement** (Directive §30) — not an ad-hoc engineer deviation. It differs from the current §5.7 ceiling in 13 of 148 rows (8.8%), always yielding a shorter intergreen; the workbook's `Consider for manual rounding?` column flags them.
- **Source**: workbook column AK formula `IF(MOD(MAX,INT(MAX))<0.1,INT(MAX),INT(MAX)+1)`; guidelines §5.7 "יעוגלו כלפי מעלה לפי שניות שלמות".
- **Alternative rejected**: silently picking one rule for both packs.
- **Impact**: 13-row delta (below). The choice per pack is policy, selectable, never silent.
- **Test**: `RoundingDeltaRegressionTests` asserts the exact 13-row set; `RoundingStrategyTests` covers boundaries.

### The full 13-row delta (workbook vs §5.7 ceiling)

| Example | Conflict | Pair | Raw | Workbook | Ceiling |
|---|---|---|---|---|---|
| 1 | 22 | E-R→b | 4.029 | 4 | 5 |
| 1 | 26 | E-T→b | 4.020 | 4 | 5 |
| 1 | 82 | W-L→S-T | 4.051 | 4 | 5 |
| 1 | 92 | b→E-R | 6.042 | 6 | 7 |
| 1 | 93 | b→E-T | 6.042 | 6 | 7 |
| 1 | 97 | d→S-L | 6.042 | 6 | 7 |
| 1 | 98 | d→S-R | 6.042 | 6 | 7 |
| 1 | 99 | d→S-T | 6.042 | 6 | 7 |
| 2 | 25 | E-R→d | 5.044 | 5 | 6 |
| 2 | 43 | S-R→b | 4.023 | 4 | 5 |
| 2 | 62 | S-L→E-L | 3.076 | 3 | 4 |
| 2 | 84 | W-L→d | 6.006 | 6 | 7 |
| 2 | 107 | c→S-L | 6.096 | 6 | 7 |

## ED-002 — Ceiling epsilon 1e-9 s

- **Decision**: `guidelines-ceil` computes `ceil(raw − 1e-9)`.
- **Why**: IEEE doubles make e.g. `4.8/1.2 = 4.000000000000001`; a naive ceiling adds a full second for 1e-15 of float noise. Discovered on Example 2 pedestrian rows (91/92/93…) where the naive ceiling produced spurious delta rows beyond the verified 13.
- **Alternative rejected**: exact `Math.Ceiling` (adds a second for float noise); rounding raw to N decimals first (changes the engineering value).
- **Impact**: values within 1e-9 s of a whole second round to that second. 1e-9 s ≪ any measurement precision (distances are recorded at cm level).
- **Test**: `Workbook_vs_ceiling_delta_rows_are_exactly_the_known_set` fails with a naive ceiling and passes with the epsilon.

## ED-003 — Blank distance = 0 is COMPATIBILITY-ONLY behaviour

- **Decision**: `LegacyWorkbookCompatibilityCalculator` reproduces blank==0 (needed for 148/148 regression, incl. the four phantom rows). `LegacyProductionAnalyzer` blocks those rows with `IG-VAL-001/-002` and emits no number.
- **Source**: v3 §5/§25/§26; Addendum §C.3/§D.
- **Test**: `ProductionValidationRegressionTests` — 18/60 = `IG-VAL-001` (nothing measured), 26/34 = `IG-VAL-002` (ED=0 defined, CD missing).

## ED-004 — Sub-1-second production behaviour

- **Decision**: production emits the conservative `ceil(raw)` plus a `IG-VAL-003` REVIEW REQUIRED finding (never a silent blank, never a smaller value).
- **Why**: Addendum §C.1 — the workbook yields blank and the pair silently vanishes from the matrix; production must always surface a value and a finding.
- **Test**: `MinimumIntergreenTests` + analyzer unit coverage.

## ED-005 — Defining-point tie semantics

- **Decision**: on equal intergreen values the earliest point wins, matching Excel's IF-cascade in column AF.
- **Test**: `Defining_point_matches_workbook` (148 rows) + `Multi_point_fixture_conflict8…`.

## ED-006 — Golden fixtures are generated, never hand-edited

- **Decision**: `tests/fixtures/golden-example*.json` are produced by `scripts/extract_golden.py` directly from the source workbooks (hashes in docs/SOURCE_INVENTORY.md). The independent Python oracle in the same script re-verified every baseline claim before any C# existed: 148/148 FINAL, 148/148 intermediates, worst delta 2.665e-15, 13 delta rows, 4 incomplete rows with the 18/60-vs-26/34 distinction, single multi-point row (Ex2 conflict 8, N-T→W-R).
- **Gate N follow-up**: the C# Excel readers must re-read the original XLSX files and agree with the fixtures.

## ED-007 — Continuous zero-acceleration extension for Legacy Production (Final Hotfix §1–§2)

- **Decision**: Production vehicle clearing uses `a = max(0, 1.5 − 1.5·kph/80)` and the stable
  form `t = reaction + 2L/(sqrt(v² + 2·L·a) + v)`, `L = v²/(2·decel) + d + len`.
- **Reason**: the workbook formula is singular at exactly 80 km/h (division by a=0 → #DIV/0!).
  The stable form is algebraically equivalent away from the singularity and has the exact
  constant-speed limit L/v at a=0.
- **Compatibility behaviour**: preserved separately (ED-008). **Production behaviour**: finite,
  continuous. **Impact**: Legacy Production only; NOT an official guideline formula.
- **Test**: `ProductionNumericsTests` — continuity at 79.9…80.1, equality with the historical
  form at 25/50/70/79/90 kph to 1e-9, constant-speed identity at exactly 80.

## ED-008 — Compatibility calculator reproduces the 80 km/h workbook singularity

- **Decision**: at exactly coeff==0 the compatibility calculator returns the workbook's
  IFERROR result (-1) instead of IEEE NaN (LEGACY_SOURCE_FORMULA_SINGULARITY, Final Hotfix §3).
- **Test**: `Compatibility_reproduces_the_workbook_singularity_at_exactly_80`.

## ED-009 — Production movement branching by Movement.Mode only (Final Hotfix §4)

- **Decision**: `LegacyProductionAnalyzer` selects the pedestrian/vehicle algorithm from
  `MovementMode`, never from `speed == 1.2`. A 1.0 m/s pedestrian stays a pedestrian; an
  unknown vehicle movement blocks with IG-VAL-004 and never falls back to pedestrian speed.
- **Test**: `PedestrianMetamorphicTests` (1.2 / 1.0 / synthetic 0.9 m/s).

## ED-010 — Deterministic debris exclusion + convention-based boundary selection

- **Decision**: (a) curves shorter than 0.5 m on a movement layer are drawing debris and are
  excluded (envelope boundaries must span the conflict zone); (b) when more than 2 candidates
  remain, the boundaries are the curves that resolve a stop-line reference (Appendix A: every
  boundary starts at the stop line) — applied only when exactly 2 qualify. Every exclusion is
  a WARNING with handles (IG-GEO-011 / IG-GEO-012); anything still ambiguous stays an ERROR.
- **Why**: Example 2 reality — intergreen_E-L carried a 0.01 m fragment, intergreen_W-T a
  0.07 m fragment plus a 4.46 m leftover that does not reach the stop line.
- **Impact**: Example 2 error conflicts dropped 78 → 32 at the time this decision landed.
  No silent guessing — the convention itself is the rule. Subsequently, the multipart
  N-edge crossing model + sidecar endpoint confirmations resolved crossing 'b' as well:
  it computes with project W = 16.00 and is NOT generically blocked in the final
  artifacts — final Example 2 matrix: 37 VALID / 7 REVIEW_REQUIRED / 0 BLOCKED.
- **Test**: real-DWG run evidence (EXAMPLE2 final artifacts; 0 blocked cells).

## ED-011 — Boundary-termination candidates (Directive §21A)

- **Decision**: a boundary endpoint lying inside/on the opposing envelope region (veh×veh),
  or a vehicle boundary that enters a crossing band and terminates before its remaining edges
  (veh×ped), generates an explicit termination candidate (owning-boundary station exact;
  opposing stations by exact projection). Origin recorded as `boundary-termination`.
- **Evidence**: Example 1 E-R→a — manual CD 18.95 ≈ boundary full length 18.93; ΔCD after the
  rule −0.020 m and IG 6=6 (was engine 5 < manual 6, the last unsafe case).
- **Tests**: `BoundaryTerminationTests`, `Vehicle_boundary_terminating_inside_crossing_adds_end_candidate`.

## ED-012 — Endpoint stop-line references require explicit confirmation (Directive §14)

- **Decision**: exact intersection → valid; endpoint within 0.5 m → SUGGESTION only, blocking
  IG-GEO-004 until confirmed in the project sidecar (`confirmedEndpointReferences`).
- **Evidence**: Example 2 confirmed refs are exactly `E-L.b1`, `S-L.b2` — the two cases the
  Directive predicted from Gate-P.

## ED-013 — Pedestrian model (Directive §5–§6)

- **Decision**: crossings carry N edges (2/4/6…) and the authoritative project width W from
  Pedestrian Xing / sidecar; clearing CD = W; averaging of edge lengths is forbidden and
  absent from the code; disagreeing W rows → PEDESTRIAN_WIDTH_CONFLICT (ERROR).
- **Evidence**: Example 1 ped-clearing rows now match with ΔCD = 0.000 exactly (previous
  averaging gave +0.17…+0.29 m); Example 2 crossing c uses 8.30, not ~15.88.

## ED-014 — Excel export contract re-confirmed: engine-fed legacy chain, one source of truth (r11)

- **Trigger**: Lin's refresh finding — the r10 export displayed the source's cached (manual)
  `Matrix` PivotTable until Refresh; after Refresh the pivot rebuilt from the engine-populated
  sheets and numbers changed ("legacy visual result from old cache + engine values underneath").
- **Decision (contract)**: the familiar legacy sheets ARE fed by the engine (compatibility
  view, v3 §25) and the whole legacy chain must then be internally consistent:
  1. `Input Distances` receives the engine's **governing point only** (slot 1), exactly like the
     manual one-point practice; every candidate point lives in `MAHOD Engine Results`. Writing
     four slots fed the V2 template's defective AutoAdjusted slot-2..4 formulas (F-008) and is
     not needed — the governing point alone yields the identical FINAL IG.
  2. Rows whose movement pair has **no engine conflict/points are cleared** (all four slots) and
     flagged in the `MAHOD QA` column (`NOT_IN_ENGINE_RESULTS` / `NO_ENGINE_POINTS`); the source
     workbook keeps the manual values untouched. A row without an engine result therefore looks
     exactly like an unmeasured row of the manual template (the legacy pivot renders such pairs
     as 0 — a template artefact of MAX over the text result `""`). Rationale: the export is the
     engine's workbook; manual numbers must never sit next to engine numbers unflagged.
  3. Every worksheet-sourced pivot cache ships `refreshOnLoad=1`, with its cached records purged
     and the pivot's rendered cells cleared — Excel rebuilds the PivotTable from the engine-
     populated workbook on open (after `fullCalcOnLoad`). Refresh stays functional; no formula is
     touched. Proven in real Excel: Matrix after open == after Refresh == after Refresh All ==
     after close/reopen (0 changed cells; Example 1, Example 2, the V1 template, the fixture).
- **Engine authority**: `MAHOD Matrix Status` (exact engine result) is authoritative. The legacy
  `Matrix` pivots the `AutoAdjusted Distances` sheet = the Inbar-entry view (CD rounded UP to
  0.5 m, ED DOWN to 0.5 m, + "Addition to Inbar distances"); it can therefore be +1 s above the
  engine in a few cells (Example 1: b→1, c→2, d→2 from pedestrian CD 7.25→7.5 / ED 0.7→0.5).
  That is the legacy template's own Inbar adaptation, identical to the manual workflow, not an
  engineering delta — every such cell is listed in the r11 matrix delta reports.
- **Alternative rejected**: keeping manual numbers in rows the engine could not compute (r10
  behaviour) — mixes two sources inside one pivot and can make a legacy cell outvote the engine.
- **Tests**: `Multi_point_conflicts_keep_every_candidate_in_engine_sheet_and_only_the_governing_point_in_the_legacy_row`,
  `Rows_without_engine_result_are_cleared_and_flagged_never_left_with_manual_numbers`,
  `Export_never_ships_a_stale_pivot_refreshOnLoad_purged_records_cleared_cells`,
  `Pivot_fixture_as_shipped_by_r10_is_detected_as_a_stale_cache_risk`,
  `Real_example_exports_reset_their_legacy_matrix_pivot_and_report_source_health`
  (fixture: `tests/fixtures/pivot/pivot-regression.xlsx`, built with real Excel).

## ED-015 — A conflict with a pedestrian crossing ends after the crossing (David Suchinsky, 2026-08-27)

**Source.** David's e-mail of 2026-08-27, item 1: "we prefer the conflict to end after the crossing;
measure to the point where the boundary line exits the crossing area."

**Rule, stated per role** (the safety direction differs between the two, and the e-mail did not
distinguish them — this is our reading, shown to David on his own numbers in the r14 delivery):

- **Clearing vehicle.** When a boundary stops inside a crossing (Directive §21A case), the
  measurement point is not the drawn end but where the boundary would leave the crossing. The
  boundary is carried on along its own last segment — a straight segment continues straight, an arc
  keeps its centre, radius and sense (turns are fillets, Appendix A) — and the farthest crossing edge
  it meets is the exit. CD grows; the intergreen grows; conservative.
- **Entering vehicle.** The conflict begins at first contact with the crossing. The intersection
  candidates already hold that point and, being the smallest ED, it governs. Unchanged.
- **Fallbacks, never silent.** If the drawn end already sits on a crossing edge (≤ 0.5 m) there is
  nothing to extend to: the drawn end is the exit (Warning, "ends at an edge of crossing"). If no
  crossing edge lies on the continuation at all, the drawn end is kept and flagged for review ("the
  clearing distance may be short").

**Evidence.** Example 1: all 10 clearing-vehicle × crossing rows already equalled the engineer's CD;
`E-R→a` ends 3 cm from the far edge of crossing `a` and stays at the engineer's 18.93/18.95 — the
rule reproduces, not lengthens, the human measurement there. Example 2: `W-R→b` 13.49 → 15.63
(exit 2.14 m beyond the drawn start), `W-R.b2` +5.00 m, `E-L.b1` +1.76 m; no FINAL IG changed on
either example (39/40 and 60/78 agreement with the workbooks, identical to r13). Locked by
`Regression.Tests/TerminationCandidateTests` (`Example2_W_R_to_b_is_measured_to_the_crossing_exit`,
`Example1_E_R_to_a_ends_at_the_crossing_edge_and_keeps_the_engineers_distance`) and
`Geometry.Tests/HardeningTests` (synthetic: 12.0 → 13.5; entering role unchanged).

**Supersedes** the §21A "drawn end" reading for the clearing role (r13). Engineering assembly changed:
`Mahod.Intergreen.Geometry` (`ConflictStrategies.cs`: `CrossingExit`, `Continuation`).
