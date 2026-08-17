# EXAMPLE 1 — geometric residual classification (0.1.1 aligned)

## Residual criterion — explicit and unambiguous

A historical row belongs to the geometric residual review set when the nearest matched
engine candidate has:

`|ΔCD| > 0.05 m`  **OR**  `|ΔED| > 0.05 m`

Row 82 (`W-L → S-T`) **also meets the geometric residual criterion**: its *nearest* matched
candidate is within 4 mm (ΔCD −0.004 / ΔED −0.001), but the engine's *governing* candidate is a
different construction (`W-L.b2@end`, a boundary-termination candidate, CD 28.52 m) whose CD
differs from the manually governing measurement (CD 24.75 m) by far more than 0.05 m. It is
therefore counted in the residual review set.

## Counts under this criterion (verified against EXAMPLE1_REPORT.md / EXAMPLE1.analysis.json)

| metric | value |
|---|---|
| rows meeting the geometric residual criterion | **12** |
| of which: engine Final IG == manual Final IG | **10** |
| of which: engine Final IG > manual Final IG (ENGINE_RESULT_HIGHER) | **2** (rows 82, 89) |
| of which: engine Final IG < manual Final IG | **0** |

For the full 40 comparable David rows: **38 equal · 2 engine-higher · 0 engine-lower**.

## ENGINE_RESULT_HIGHER — ENGINEERING FINDINGS FOR DAVID

Both findings are in the **conservative / safe direction** (the engine issues a *longer*
intergreen than the historical manual value). They are not defects and are not hidden —
they are agenda items for the engineering review with David
(see `DAVID_PILOT_REVIEW_AGENDA_HE.md`).

### Row 82 — `W-L → S-T`: manual **4 s** → engine **5 s**

- Governing engine candidate: `P7 = W-L.b2@end × S-T.b1~proj` — a boundary-**termination**
  candidate (the W-L boundary ends inside the conflict region), CD ≈ **28.52 m**.
- Engine raw IG at the governing candidate: **4.14 s** → fraction 0.14 ≥ 0.1 → legacy
  rounding **up** → **5 s**.
- The manually measured point corresponds to engine candidate `P6` (CD 24.75 m,
  raw ≈ **4.05 s** → fraction 0.05 < 0.1 → legacy rounding **down** → **4 s**).
- Interpretation: the engine evaluates *all* candidate constructions including the
  termination candidate the manual workflow did not measure; that extra candidate crosses
  the 0.1 rounding threshold. Same rounding-sensitivity family as the 13 known
  historical/current rounding-delta rows.

### Row 89 — `a → S-T`: manual **6 s** → engine **7 s**

- Governing engine candidate: `P1 = a.band × S-T.b1@end` — a crossing-**a** edge candidate
  at the S-T stop-line relationship, with **ED = 0** (entering distance zero at the stop
  line; no entering credit).
- Engine raw IG: **6.96 s** → **7 s**.
- The manual row used a shorter effective relationship (manual Final IG 6 s); the nearest
  matched candidate differs in ED by +0.668 m (part of the systematic +0.6–0.7 m ED offset
  family on pedestrian-clearing rows, see rows 92–99 below).

## Per-row classification (12 rows)

| conflict | pair | ΔCD (nearest) | ΔED (nearest) | Final IG (manual → engine) | classification | evidence |
|---|---|---|---|---|---|---|
| 82 | W-L→S-T | -0.004 | -0.001 | **4 → 5 (ENGINE_RESULT_HIGHER)** | GOVERNING_CANDIDATE_NOT_MEASURED_MANUALLY | governing `W-L.b2@end` termination candidate CD 28.52 vs manual governing CD 24.75; raw 4.14 vs 4.05 across the 0.1 rounding threshold |
| 89 | a→S-T | 0.000 | +0.668 | **6 → 7 (ENGINE_RESULT_HIGHER)** | MEASUREMENT_SEMANTICS_DIFFERENCE | governing `a.band × S-T.b1@end`, ED = 0 at the stop line, raw 6.96 → 7; ED-offset family as rows 92–99 |
| 92 | b→E-R | 0.000 | +0.708 | 6 → 6 | MEASUREMENT_SEMANTICS_DIFFERENCE | W exact; systematic +0.6–0.7 m ED offset on ped-clearing rows: engine measures the entering station along the envelope boundary to the crossing edge; manual trim measured a slightly shorter path |
| 93 | b→E-T | 0.000 | +0.700 | 6 → 6 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above |
| 97 | d→S-L | 0.000 | +0.599 | 6 → 6 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above |
| 98 | d→S-R | 0.000 | +0.602 | 6 → 6 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above |
| 99 | d→S-T | 0.000 | +0.602 | 6 → 6 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above |
| 24 | E-R→W-L | -0.020 | -1.150 | 4 → 4 | MEASUREMENT_SEMANTICS_DIFFERENCE | CD matches to 2 cm; manual ED larger than every candidate on that geometry — manual trim point sits mid-zone; engine ED smaller = safe direction |
| 26 | E-T→b | -0.188 | 0.000 | 4 → 4 | DRAWING_AMBIGUITY | crossing-b far edge vs boundary end within 19 cm |
| 28 | E-T→S-L | -0.002 | -0.222 | 5 → 5 | MEASUREMENT_SEMANTICS_DIFFERENCE | CD exact; 22 cm ED offset, nearest candidate not governing |
| 49 | S-T→W-L | +1.975 | +1.995 | 5 → 5 | DRAWING_AMBIGUITY | manual point ~2 m from every candidate (incl. termination candidates); governing candidate yields identical IG; historical point measured inside the zone, not at a boundary construction — review item for David |
| 74 | W-T→S-R | -2.304 | +4.107 | 5 → 5 | DRAWING_AMBIGUITY | manual CD 22.90 within boundary length but no boundary construction reproduces the pair; engine governing IG equal; kept visible for the engineering review session — review item for David |

## Safety summary

- Engine-lower (unsafe-direction) cases: **0** — across all 40 comparable rows, not only
  the residual set.
- The two IG differences that exist (rows 82, 89) are both **engine-higher**, i.e.
  conservative/safe-direction engineering findings that require engineering review with
  David before the pilot conclusion.
