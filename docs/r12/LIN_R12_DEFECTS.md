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
