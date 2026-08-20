# Lin's r12 report — diagnosis

Source: `_תקלות-R12.pdf` (2026-08-20). Reproduced on her real files with the shipping r12 bundle.
Her three items are **one defect of ours, one that is not ours, and one consequence of the first**.

## 1. "0.0000001 instead of 0" — not ours

The value comes from Mahod's own legacy template, not from the engine:

```
Input Distances!AI3 =IF(...,IF(D3=0,0.0000001,D3),...)
Input Distances!AJ3 =IF(...,IF(E3=0,0.0000001,E3),...)
```

**330 such formulas** exist in her workbook. They deliberately turn a real zero into a tiny non-zero
so the downstream MAX/IF chain can tell "zero distance" apart from "blank". The engine writes
`Math.Round(governing.Ed, 3)` — a plain `0`. Nothing to fix in the product; showing `0` instead
would mean editing her template's formulas, which we deliberately never touch.

## 2. "the conflict point is in the wrong place" — REAL, and ours

`b→S-L`, the case she marked in the red square. Engine result: CD 6.73, **ED 0.00**, Final IG 6.
Her manual value for that row is CD 6.73, **ED 38.47**. The CD matches exactly; the ED does not.

The four candidate points the engine produced:

| point | CD | ED | clearing curve | entering curve |
|---|---|---|---|---|
| **P1 (governing)** | 6.73 | **0** | `b.band` | `S-L.b1@end` |
| P2 | 6.73 | **38.471** | `b.e1` | `S-L.b1` |
| P3 | 6.73 | 49.504 | `b.e1` | `S-L.b2` |
| P4 | 6.73 | 61.373 | `b.band` | `S-L.b2@end` |

P2 is a real intersection and equals her manual 38.47. P1 wins because the governing point is the one
that maximises the intergreen, and ED = 0 maximises it.

**Where P1 comes from.** Measured from the geometry: P1 sits exactly on the end of `S-L.b1`
(handle `4F73`, distance 0.000), exactly on S-L's own stop line `4E8A` (distance 0.000), and
**34–37 m away from all three edges of crossing b**. She is right that the crossing is nowhere near it.

**Root cause.** `ConflictStrategies.VehiclePedestrian`, the Directive §21A block:

```csharp
var endStation = Math.Abs(boundary.TotalLength - vehRefs[i]);
var endPoint   = boundary.PointAtStation(boundary.TotalLength);
```

The rule is sound and grounded in David's manual practice — a boundary that enters a crossing but
stops inside it contributes its drawn far end as a measurement extremum. But it assumes the drawn
far end is at station `TotalLength`, which is only true when the polyline is drawn **from** the stop
line, per David's own convention ("check that each polyline starts at the stopline … PEDIT → reverse").

`S-L.b1` is drawn reversed: its stop line sits at its *end*. So `PointAtStation(TotalLength)` is the
point on the stop line, and `endStation = |TotalLength − ref| = 0`. The candidate is not a
termination inside the crossing at all — it is the vehicle's own starting line, and because its
distance is 0 it always governs.

The same wrong assumption sits in `AddTerminationCandidates` (vehicle × vehicle), which offers both
`Start` and `End` without excluding the one that coincides with the reference station; that produces
the `CD 0 / ED 0` pairs (`S-L→S-R`, `W-R→W-T`, …).

**Fix (not applied — see below).** In both places, take the endpoint *away from* the reference
station rather than assuming `TotalLength`, i.e. skip any endpoint whose `|station − ref|` is zero
within tolerance.

## 3. "the row says 4 and the matrix says 6" — a consequence of #2

Two conflicts map to signal-group pair `5 → e`:

| conflict | Final IG | governing point | true best point |
|---|---|---|---|
| `N-T→e` | 4 | real intersection, CD 3.685 | — |
| `N-R→e` | **6** | **phantom** `@end`, CD 28.992 | raw IG 3.940 → **4** |

The matrix cell is `Max of FINAL IG` over the pair, so it reports 6 and names `N-R→e` as governing.
Remove the phantom and both rows are 4 and the cell reads 4 — which is what she expected.

## How wide this is

Conflicts whose governing point is a termination candidate with a ~0 distance:

| drawing | conflicts | phantom-governed |
|---|---|---|
| Lin 05293_B | 74 | **11** |
| Golden Example 1 | 50 | **10** |
| Golden Example 2 | 168 | **51** |

It is in every release we have shipped, and it is in both accepted goldens.

**Corroboration from our own evidence.** The r11 like-for-like comparison of Example 1 against the
engineers' completed matrix found exactly one disagreement: `a → SG 2`, manual **6**, engine **7**.
`a→S-T` is on the phantom list above with Final IG 7 and a best real-intersection raw IG of 5.287,
which rounds to **6**. The single known mismatch against a human-checked result is this defect, and
the proposed fix moves the engine onto the manual value.

## Why it is not fixed in this commit

The change is in `Mahod.Intergreen.Geometry` — one of the six locked engineering assemblies — and it
**will move both accepted goldens**. Release discipline says a golden may not change until the old
number is proven wrong. For `a→S-T` that proof exists; for the rest it does not yet. This needs
Arthur's decision and a re-validation pass against the engineers' manual matrices before any build.

---

# r13 — fixed, measured, packaged

Source commit `5e84331` · installer `Mahod_Intergreen_Setup_r13.exe`
sha256 `3462dd1f13368e62cd6e0c4a3066df38ae7468239e450468684138b96c20992e`.

## The rule, corrected

A termination candidate is offered for **either** drawn end, and only when that end actually lies in
the crossing — inside one of the strips spanned by a pair of the crossing's edges, or on a drawn edge.
The old code took the end at station `TotalLength` and assumed it was the far one. Crossings are
regularly drawn in more than two pieces (five in Example 2, three in Lin's `b`), so the strip test
spans every pair rather than assuming edges 1 and 2 are the two sides.

Two earlier attempts were measured and discarded, which is why the released rule is the third:

| attempt | agreement with the engineers' values (174 rows) |
|---|---|
| baseline (r12) | 150 |
| skip the reference end everywhere | 148 — broke 4 rows where distance 0 is genuinely right |
| far end only | 153 — still broke `b→S-R`, whose stop line really is in the crossing |
| **either end, must lie in the crossing** | **154** |

## Measured against the engineers' own numbers

Every conflict row compared with the FINAL IG the engineer wrote in that project's own workbook:

| project | before | after |
|---|---|---|
| Example 1 | 38/40 | **39/40** |
| Example 2 (documented REVISION_MISMATCH) | 61/78 | 60/78 |
| Lin 05293_B | 51/56 | **55/56** |
| **total** | **150/174** | **154/174** |

Moved onto the manual value: `ex1 a→S-T` 7→6 (the one cell Example 1 ever disagreed on),
`Lin b→S-L` 6→3, `E-T→b` 8→7, `N-R→e` 6→4, `S-L→b` 9→8.
Moved off: `ex2 b→W-R` 14→13, in the workbook that is documented as out of sync with its own drawing.

Every changed value went **down**: the defect lengthened intergreens, so previous outputs were
conservative rather than unsafe.

## Golden headline numbers — unchanged

| | conflicts | matrix | W-L→S-T |
|---|---|---|---|
| Example 1 | 50, all VALID | 24 VALID | 5 |
| Example 2 | 168 (157 VALID / 11 REVIEW) | 37 VALID / 7 REVIEW | 5 |

So the release-acceptance numbers stand; only individual cells moved, and almost all onto a human value.

## Verification

Suite **343/343** (5 new tests). Headless acceptance through the shipping r13 bytes, both host
flavours, no Civil 3D session: Lin's four flagged rows equal her manual values, matrix cell `5→e`
reads 4, and both goldens reproduce the numbers above. The new invariant test asserts that all 59
(Example 1) and 213 (Example 2) termination candidates lie on the crossing they claim to stop in.

## Packages (prepared, not sent)

| package | sha256 |
|---|---|
| `Mahod_Intergreen_LIN_r13.zip` | `a6cb71857e4a01bea225cf28e4e8791f4eea3e7ad4b64302996236d42fcb62c6` |
| `Mahod_Intergreen_DAVID_r13.zip` | `df0964728095289eae9b3011b1ac574460d83497736ba7e1ceb787f19d13da14` |
