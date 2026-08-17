# ENGINEERING_DECISIONS

Every non-trivial interpretation, with source, alternatives, impact and the test proving it.

## ED-001 — Two named rounding strategies; neither chosen for David

- **Decision**: implement `mahod-legacy-0.1` (floor when fraction < 0.1, else floor+1; undefined below 1 s) and `guidelines-ceil` (§5.7 plain ceiling). Legacy policy carries the workbook rule; 2025 policy carries the ceiling + 3 s minimum.
- **Why**: David's workbook deviates from §5.7 in 13 of 148 rows (8.8%), always yielding a *shorter* intergreen. His `Consider for manual rounding?` column flags them, so the practice is deliberate.
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
- **Impact**: Example 2 error conflicts dropped 78 → 32 (all remaining involve crossing 'b',
  which is genuinely ambiguous). No silent guessing — the convention itself is the rule.
- **Test**: real-DWG run evidence (EXAMPLE2 artifacts); crossing 'b' remains blocked.
