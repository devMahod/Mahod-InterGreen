# LIN_CONFLICT_DIFF — 05293 "part of the conflicts are not detected at all"

Diagnosis of Lin's r12/P0 report, reproduced end-to-end on her real files. No code was changed.
No Autodesk GUI session was touched: geometry was extracted headlessly with `accoreconsole`
(AutoCAD 2026) from a **copy** of her drawing, and the analysis was run with the shipping r11 DLLs.

## Inputs (as received 2026-08-20)

| File | SHA-256 |
|---|---|
| `04_intergreens_05293_B_2026-08-04.dwg` (431,980 B, AC1032) | `beac4e6653c7d9e34b8102ad5bbc6f9a136497ab847c4e065105fe0a692ba612` |
| `04_IG_matrix_05293_2026-08-04.xlsx` (486,601 B) | `581f3c9d9d53129ad8285c9b613d7989e101d31d22f06d09b94d4793c36d3fe3` |

Workbook is a **source** workbook (no MAHOD sheets), template-compatible: `Input Distances`
header row 2 matches and `FINAL IG` is at column AK(37). 56 conflict pair rows, 14 movements
(9 vehicle: E-T, N-R, N-T, S-L, S-R, S-T, W-L, W-R, W-T; 5 pedestrian: a, b, e, i, j).
Drawing: 460 layers, 23 `intergreen_*` layers, 40 extracted curves, 0 unsupported entities,
INSUNITS = Unitless (meters confirmed via sidecar, as the GUI does).

## Harness control (proves the reproduction pipeline)

Golden Example 1 through the **same** harness: `movements=11 conflicts=50 {VALID:50}`,
`matrix 24 {VALID:24}`, `errors=0` — identical to the accepted baseline. The pipeline is sound.

## Root cause

**Two boundary polylines stop a few centimetres short of their stop line.**

| Movement | Curve id | DWG handle | Start point | Gap to `intergreen_stopline` |
|---|---|---|---|---|
| E-T | `E-T.b1` | `4EBA` | (219506.767, 628687.625) | **0.0474 m** |
| N-R | `N-R.b2` | `4F27` | (219481.570, 628699.630) | **0.0625 m** |

Every other vehicle boundary in the drawing meets its stop line exactly (0.0000 m).

Per Directive §14 / ED-012 the engine refuses to promote a near-miss endpoint to an engineering
reference station on its own — it raises `IG-GEO-004` (ERROR) with
`RecommendedAction: "Confirm the endpoint reference in Project Setup (persisted in the sidecar) or fix the drawing."`
Without a station-0 reference the clearing distance for E-T and N-R cannot be measured, so every
conflict involving either movement yields no candidate point.

Propagation: 2 boundaries → 52 × `IG-GEO-004` (26 per boundary, one per conflict) → 42 × `IG-MTX-001`
(`Matrix cell is BLOCKED: contributing conflict(s) have no valid result`).

## Exact before/after on her workbook

| | Rows filled | Rows cleared (`NO_ENGINE_POINTS`) | Findings | Matrix |
|---|---|---|---|---|
| As she runs it today | 38 / 56 | **18** | 94 errors | 74 cells: 30 VALID / 44 BLOCKED |
| With `E-T.b1` + `N-R.b2` confirmed | **56 / 56** | **0** | **0 errors** | 54 cells: **52 VALID** / 2 BLOCKED |

All 18 cleared rows involve E-T or N-R:
`N-R→e, N-R→E-T, N-R→S-L, N-R→b, N-T→E-T, E-T→S-T, E-T→S-L, E-T→W-L, E-T→N-R, E-T→N-T, E-T→b,`
`S-T→E-T, S-L→N-R, S-L→E-T, W-L→E-T, e→N-R, b→E-T, b→N-R`.

The 2 cells still BLOCKED after the fix are a genuine engineering review item, not this defect:
`IG-GEO-005` × 2 — envelope regions overlap without any boundary crossing
(POSSIBLE_UNRESOLVED_CONFLICT) — the engine correctly refuses to invent a point.

## Why "now"

The detection engine is byte-identical between r9 and r11 (`git diff 4bc1d3a..df96c36 -- src/`
touches only the palette, the CLI, the Excel writer and the DWG picker — nothing in
Core/Geometry/Rules), and both goldens reproduce unchanged. The same drawing therefore produced
the same 18 unmeasurable pairs in r9 and r10.

What changed is **visibility**, by design (ED-014, r11): before r11 a row with no engine result
kept the engineer's manual CD/ED, so the sheet and the legacy Matrix still looked complete.
From r11 such rows are cleared and flagged, so the 18 pairs became visibly empty. Lin is seeing
a pre-existing drawing condition that r11 stopped hiding.

Classification per the r12 template: **not** a detection regression; **not** invalid geometry;
**not** a tolerance/classification/layer/direction defect. It is `reference-station unconfirmed`
(drawing imprecision) + `made visible by the r11 export contract`.

## Product gap this exposes (r12 P0)

`ProjectSidecar.ConfirmedEndpointReferences` exists in the engine and is honoured
(`ConflictStrategies.cs:338`), keyed by curve id (`"E-T.b1"`). **It is not exposed anywhere in the
palette** — `IgWorkflowCommands.cs` contains no endpoint-confirmation UI. So the product tells the
engineer to "confirm in Project Setup" while offering no way to do it, and the only visible symptom
is empty rows plus an English note in a QA column.

Minimum r12 fix:
1. A review step listing every near-miss reference (movement, curve id, measured gap in cm) with
   confirm / reject per item, persisted to the sidecar.
2. A Hebrew, actionable palette message naming the movements and the gap instead of a bare error count.
3. A regression fixture built from this exact case (boundary ending 4–6 cm before the stop line)
   asserting: unconfirmed → `IG-GEO-004` + cleared row; confirmed → full result.

## Immediate unblock for Lin (no new build required)

Extend the two polylines so they reach the stop line — `intergreen_E-T` handle `4EBA`
(4.7 cm short) and `intergreen_N-R` handle `4F27` (6.3 cm short) — then re-run Analyze.
Verified equivalent here: all 56 conflicts compute and the matrix returns 52 VALID.

---

# ADDENDUM — the condition is systemic, and we already worked around it internally

Added after checking David's own example drawings with the same harness.

## Harness validated to the byte on both goldens

The geometry extracted headlessly here is **byte-identical** to the accepted r11 evidence:

| Geometry file | SHA-256 | matches accepted evidence |
|---|---|---|
| `ex1.iggeometry.json` | `9A82EE4A…3C43B609` | yes |
| `ex2.iggeometry.json` | `B57C7D8E…805B3293` | yes |

(The value recorded as `SourceGeometry.Sha256` in every analysis is the hash of the **geometry JSON**,
not of the DWG, while `SourceGeometry.FileName` is the DWG name — worth tightening for traceability.)

Both goldens reproduce exactly through this harness: Example 1 → `50 conflicts {VALID:50}`,
`matrix 24 {VALID:24}`, 0 errors. Example 2 → `168 conflicts {VALID:157, REVIEW:11}`,
`matrix 44 {VALID:37, REVIEW:7}`, 0 errors.

## Boundaries that miss their stop line, per drawing

| Drawing | Near-miss vehicle boundaries (gap ≤ 0.5 m) |
|---|---|
| Example 1 (PINES) | none |
| **Example 2 (Abarbanel)** | `E-L` 5762 (16.44 cm), `S-L` 5357 (0.54 cm), `S-R` 5065 (2.00 cm), `S-T` 5075 (1.04 cm), `W-T` 57B4 (2.76 cm) |
| Lin 05293_B | `E-T` 4EBA (4.74 cm), `N-R` 4F27 (6.25 cm) |

Two of the three real drawings we hold exhibit the condition. It is a normal by-product of how these
drawings are drafted, not a one-off mistake by one engineer.

## Example 2's golden only passes because of a hand-made sidecar

The gate sidecar for Example 2 (`ig-gate-2026-ex2-*/ex2.intergreen-project.json`) contains:

```json
"confirmedEndpointReferences": ["E-L.b1", "S-L.b2"],
"notes": "Endpoint stop-line references confirmed from Gate-P evidence per Directive §14."
```

Re-running Example 2 **without** that sidecar — i.e. exactly as a customer would run it — gives:

| Example 2 | conflicts | errors | workbook rows with no candidate point |
|---|---|---|---|
| with the gate's confirmations (accepted golden) | 168, 0 ERROR | **0** | 26 of 107 (pre-existing REVISION_MISMATCH) |
| as a customer runs it (no sidecar) | 178, 50 ERROR | **86** | **54 of 107** |

So the accepted Example 2 baseline is green only because two endpoint references were confirmed by
hand-editing JSON during the gate. The mechanism was used by us and never surfaced to the user.
Lin did not hit an unknown defect — she hit the case we had already solved for ourselves off-stage.

## Consequences

1. **Any customer running Example 2 as shipped sees 86 errors and 54 empty rows.** The same is true
   for any drawing with this ordinary drafting imprecision. This is the single largest source of
   "the tool doesn't detect my conflicts" that we know of.
2. The r12 P0 item is therefore not "fix Lin's bug" but **expose the reference-confirmation step
   that the engine already implements**, and detect the condition early (at Validate, not deep in Analyze).
3. Golden hygiene: Example 2's baseline should carry its sidecar explicitly as part of the fixture,
   so it is never mistaken for a drawing that passes unaided.

---

# r12 — the fix, and how it was verified without a Civil 3D session

Source commit `7cd934d` · installer `Mahod_Intergreen_Setup_r12.exe`
sha256 `548d55d5148a3098ada352fb6930f8290e56a4334262037d2f0cbc44118edf04`.

## What changed

Only `Mahod.Intergreen.Host` and `Mahod.Intergreen.AutoCAD` — the six engineering assemblies and the
rule packs are byte-identical to the accepted r11 (identity gate PASS, `RELEASE_BUILD_r12.txt`).

- `ReferenceReview` (Host): ordering (widest gap first), the Hebrew wording, the sidecar key.
- `SidecarStore.SetStrings` / `GetStrings`: canonical, de-duplicated string arrays.
- Palette: **Validate** now names the affected movements and the gap in centimetres instead of
  reporting a bare error count, and a new **"נקודות ייחוס…"** button lists every unconfirmed
  boundary with its DWG handle and gap, writes the ones the engineer ticks to the sidecar, and
  re-runs. Nothing is ticked by default and nothing is confirmed implicitly.
- `IG_SMOKE_REFERENCE_REVIEW`: the whole feature minus the WPF dialog, drivable headlessly.

## Verification (Civil 3D was in use by another project throughout; it was never touched)

Automated suite: **331/331** (was 319; 12 new Host tests).

Headless end-to-end through `accoreconsole`, running the **shipping dist bytes**:

| Run | host | result |
|---|---|---|
| Lin 05293_B | 2026 (net8) | finds `N-R.b2`/4F27 6.25 cm + `E-T.b1`/4EBA 4.74 cm → confirm → matrix 30→**52 VALID**, 44→**2 BLOCKED**, pending 0 |
| Lin 05293_B | 2027 (net10) | identical to the 2026 run, field for field |
| Golden ex1 | 2026 | 0 pending; 11 movements, 50 conflicts, matrix **24 VALID**, W-L→S-T **5** — accepted golden, unchanged |
| Golden ex2 | 2026 | finds `E-L.b1` 16.45 cm + `S-L.b2` 4.37 cm — **the same two boundaries the gate had hand-confirmed** — → confirm → 168 conflicts, matrix **37 VALID / 7 REVIEW / 0 BLOCKED**, W-L→S-T **5** — accepted golden, unchanged |

The Example 2 run is the strongest evidence available: the feature independently rediscovered the two
confirmations that previously existed only as hand-edited JSON in a gate folder, and reproduced the
accepted baseline from them.

**Not verified:** the WPF dialog's own rendering (layout, RTL, checkbox behaviour). It cannot be shown
in `accoreconsole`, and every other part of the path it drives is proven above. It needs about two
minutes in a real Civil 3D session before the package is sent.
