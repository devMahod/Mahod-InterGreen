# EXAMPLE1 — real-DWG Legacy regression (Directive §17–§20)

| provenance | value |
|---|---|
| geometry export | `ex1.iggeometry.json` sha256 9A82EE4A60EAF009… |
| workbook | `05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx` sha256 29428DF77484CAFE… |
| rule pack | legacy-mahod-v1 1.0.0 sha256 786F4A998217E814… |
| sidecar | none |

## Revision provenance: **BASELINE_REVISION_UNVERIFIED**

## Summary

| metric | value |
|---|---|
| historical rows | 40 |
| historical measured points (P1–P4) | 40 |
| VERY_CLOSE (≤0.01 m) | 7 |
| SMALL_DELTA_REVIEW (≤0.05 m) | 22 |
| POINT_NOT_REPRODUCED (>0.05 m) | 11 |
| NO_ENGINE_CANDIDATES | 0 |
| INVALID_MANUAL_BASELINE | 0 |
| unsafe same-revision cases (engine < manual, matched) | 0 |
| engine conflicts | 50 |
| engine candidate points | 224 |
| validation errors | 0 |
| validation warnings | 1 |
| review-required findings | 8 |
| IG-GEO-005 findings | 0 |
| matrix VALID / REVIEW / BLOCKED | 24 / 0 / 0 |

## Delta distributions (|Δ| of nearest candidates)

- CD: min 0.000, max 2.304, mean 0.125, median 0.019, std 0.465 · ≤0.01: 18, ≤0.02: 21, ≤0.05: 37, ≤0.10: 37, ≤0.20: 38, ≤0.50: 38, ≤1.00: 38 of 40
- ED: min 0.000, max 4.107, mean 0.293, median 0.020, std 0.730 · ≤0.01: 18, ≤0.02: 20, ≤0.05: 30, ≤0.10: 30, ≤0.20: 30, ≤0.50: 31, ≤1.00: 37 of 40

## Unsafe cases

NONE

## Points

| conflict | pair | point | classification | ΔCD [m] | ΔED [m] | detail |
|---|---|---|---|---|---|---|
| 22 | E-R→b | P1 | VERY_CLOSE | ΔCD=-0.002 | ΔED=+0.000 | nearest=P4(E-R.b1×b.e1,governs); manual IG=4, engine IG=4 |
| 23 | E-R→S-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.020 | ΔED=-0.020 | nearest=P5(E-R.b2@end×S-T.b1~proj,governs); manual IG=4, engine IG=4 |
| 24 | E-R→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.020 | ΔED=-1.150 | nearest=P5(E-R.b2×W-L.b1,governs); manual IG=4, engine IG=4 |
| 25 | E-R→a | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.020 | ΔED=+0.000 | nearest=P4(E-R.b2@end×a.band,governs); manual IG=6, engine IG=6 |
| 26 | E-T→b | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.188 | ΔED=+0.000 | nearest=P4(E-T.b2×b.e1,governs); manual IG=4, engine IG=4 |
| 27 | E-T→S-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.019 | ΔED=-0.034 | nearest=P4(E-T.b2×S-T.b1,governs); manual IG=4, engine IG=4 |
| 28 | E-T→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.002 | ΔED=-0.222 | nearest=P8(E-T.b2×S-L.b1,not-governing); manual IG=5, engine IG=5 |
| 29 | E-T→W-L | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.016 | ΔED=-0.026 | nearest=P4(E-T.b2×W-L.b1,governs); manual IG=5, engine IG=5 |
| 43 | S-R→d | P1 | VERY_CLOSE | ΔCD=-0.001 | ΔED=+0.000 | nearest=P4(S-R.b1×d.e1,governs); manual IG=4, engine IG=4 |
| 44 | S-R→W-T | P1 | VERY_CLOSE | ΔCD=+0.007 | ΔED=-0.004 | nearest=P3(S-R.b2×W-T.b2,not-governing); manual IG=4, engine IG=4 |
| 46 | S-R→c | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.023 | ΔED=+0.000 | nearest=P4(S-R.b2×c.e1,governs); manual IG=6, engine IG=6 |
| 47 | S-T→d | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.021 | ΔED=+0.000 | nearest=P3(S-T.b1×d.e1,governs); manual IG=4, engine IG=4 |
| 48 | S-T→W-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.033 | ΔED=-0.004 | nearest=P3(S-T.b1×W-T.b2,governs); manual IG=4, engine IG=4 |
| 49 | S-T→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+1.975 | ΔED=+1.995 | nearest=P2(S-T.b1×W-L.b1,not-governing); manual IG=5, engine IG=5 |
| 51 | S-T→E-R | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.028 | ΔED=-0.032 | nearest=P4(S-T.b2×E-R.b1,governs); manual IG=6, engine IG=6 |
| 52 | S-T→E-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.030 | ΔED=+0.004 | nearest=P4(S-T.b2×E-T.b1,governs); manual IG=5, engine IG=5 |
| 54 | S-T→a | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.028 | ΔED=+0.000 | nearest=P4(S-T.b2×a.e2,governs); manual IG=6, engine IG=6 |
| 55 | S-L→d | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.022 | ΔED=+0.000 | nearest=P4(S-L.b2×d.e1,governs); manual IG=4, engine IG=4 |
| 56 | S-L→W-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.040 | ΔED=-0.026 | nearest=P3(S-L.b1×W-T.b2,governs); manual IG=4, engine IG=4 |
| 57 | S-L→W-L | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.029 | ΔED=-0.012 | nearest=P3(S-L.b1×W-L.b1,governs); manual IG=5, engine IG=5 |
| 61 | S-L→E-T | P1 | VERY_CLOSE | ΔCD=+0.008 | ΔED=+0.004 | nearest=P8(S-L.b2×E-T.b1,governs); manual IG=4, engine IG=4 |
| 72 | W-T→S-L | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.030 | ΔED=-0.024 | nearest=P3(W-T.b1×S-L.b2,governs); manual IG=4, engine IG=4 |
| 73 | W-T→S-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.019 | ΔED=-0.010 | nearest=P3(W-T.b1×S-T.b2,governs); manual IG=5, engine IG=5 |
| 74 | W-T→S-R | P1 | POINT_NOT_REPRODUCED | ΔCD=-2.304 | ΔED=+4.107 | nearest=P2(W-T.b2×S-R.b2,not-governing); manual IG=5, engine IG=5 |
| 75 | W-T→c | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.035 | ΔED=+0.000 | nearest=P4(W-T.b2×c.e1,governs); manual IG=6, engine IG=6 |
| 79 | W-L→E-R | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.028 | ΔED=-0.032 | nearest=P4(W-L.b2×E-R.b1,governs); manual IG=6, engine IG=6 |
| 80 | W-L→E-T | P1 | VERY_CLOSE | ΔCD=+0.006 | ΔED=+0.004 | nearest=P4(W-L.b2×E-T.b1,governs); manual IG=5, engine IG=5 |
| 82 | W-L→S-T | P1 | VERY_CLOSE | ΔCD=-0.004 | ΔED=-0.001 | nearest=P6(W-L.b2×S-T.b2,not-governing); manual IG=4, engine IG=5 |
| 83 | W-L→S-L | P1 | VERY_CLOSE | ΔCD=-0.003 | ΔED=-0.001 | nearest=P4(W-L.b2×S-L.b2,governs); manual IG=4, engine IG=4 |
| 84 | W-L→a | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.028 | ΔED=+0.000 | nearest=P4(W-L.b2×a.e2,governs); manual IG=6, engine IG=6 |
| 88 | a→E-R | P1 | SMALL_DELTA_REVIEW | ΔCD=+0.000 | ΔED=-0.038 | nearest=P1(a.e1×E-R.b1,governs); manual IG=7, engine IG=7 |
| 89 | a→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+0.668 | nearest=P2(a.e1×S-T.b2,not-governing); manual IG=6, engine IG=7 |
| 90 | a→W-L | P1 | SMALL_DELTA_REVIEW | ΔCD=+0.000 | ΔED=-0.025 | nearest=P1(a.e1×W-L.b1,governs); manual IG=6, engine IG=6 |
| 92 | b→E-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+0.708 | nearest=P1(b.e2×E-R.b1,governs); manual IG=6, engine IG=6 |
| 93 | b→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+0.700 | nearest=P1(b.e2×E-T.b1,governs); manual IG=6, engine IG=6 |
| 95 | c→S-R | P1 | SMALL_DELTA_REVIEW | ΔCD=+0.000 | ΔED=-0.016 | nearest=P1(c.e2×S-R.b1,governs); manual IG=3, engine IG=3 |
| 96 | c→W-T | P1 | SMALL_DELTA_REVIEW | ΔCD=+0.000 | ΔED=-0.036 | nearest=P1(c.e2×W-T.b1,governs); manual IG=3, engine IG=3 |
| 97 | d→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+0.599 | nearest=P1(d.e2×S-L.b1,governs); manual IG=6, engine IG=6 |
| 98 | d→S-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+0.602 | nearest=P1(d.e2×S-R.b2,governs); manual IG=6, engine IG=6 |
| 99 | d→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+0.602 | nearest=P1(d.e2×S-T.b1,governs); manual IG=6, engine IG=6 |
