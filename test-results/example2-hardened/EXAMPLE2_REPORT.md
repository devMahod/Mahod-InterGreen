# EXAMPLE2 — real-DWG Legacy regression (Directive §17–§20)

| provenance | value |
|---|---|
| geometry export | `ex2.iggeometry.json` sha256 B57C7D8EE9DF5EA1… |
| workbook | `Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx` sha256 44969DBC9EEE6035… |
| rule pack | legacy-mahod-v1 1.0.0 sha256 786F4A998217E814… |
| sidecar | ex2.intergreen-project.json |

## Revision provenance: **REVISION_MISMATCH**
- conflict 8 (N-T→W-R): manual CD 24.07 m exceeds longest current boundary 20.02 m
- conflict 8 (N-T→W-R): manual CD 22.58 m exceeds longest current boundary 20.02 m
- conflict 37 (E-L→W-R): manual CD 32.20 m exceeds longest current boundary 27.85 m
- conflict 66 (W-R→E-L): manual CD 16.40 m exceeds longest current boundary 13.49 m
- conflict 67 (W-R→b): manual CD 19.78 m exceeds longest current boundary 13.49 m

## Summary

| metric | value |
|---|---|
| historical rows | 108 |
| historical measured points (P1–P4) | 107 |
| VERY_CLOSE (≤0.01 m) | 4 |
| SMALL_DELTA_REVIEW (≤0.05 m) | 2 |
| POINT_NOT_REPRODUCED (>0.05 m) | 73 |
| NO_ENGINE_CANDIDATES | 26 |
| INVALID_MANUAL_BASELINE | 2 |
| unsafe same-revision cases (engine < manual, matched) | 0 |
| engine conflicts | 168 |
| engine candidate points | 716 |
| validation errors | 0 |
| validation warnings | 7 |
| review-required findings | 70 |
| IG-GEO-005 findings | 0 |
| matrix VALID / REVIEW / BLOCKED | 38 / 6 / 0 |

## Delta distributions (|Δ| of nearest candidates)

- CD: min 0.000, max 25.045, mean 1.785, median 0.186, std 4.480 · ≤0.01: 17, ≤0.02: 18, ≤0.05: 19, ≤0.10: 26, ≤0.20: 45, ≤0.50: 57, ≤1.00: 60 of 79
- ED: min 0.000, max 14.145, mean 1.209, median 0.103, std 2.875 · ≤0.01: 21, ≤0.02: 25, ≤0.05: 33, ≤0.10: 38, ≤0.20: 53, ≤0.50: 60, ≤1.00: 63 of 79

## Unsafe cases

NONE

## Points

| conflict | pair | point | classification | ΔCD [m] | ΔED [m] | detail |
|---|---|---|---|---|---|---|
| 1 | N-R→d | P1 | POINT_NOT_REPRODUCED | ΔCD=+7.027 | ΔED=+0.000 | nearest=P1(N-R.b2×d.e1,not-governing); manual IG=4, engine IG=6 |
| 2 | N-R→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.287 | ΔED=-0.062 | nearest=P6(N-R.b1~proj×E-T.b2@start,governs); manual IG=4, engine IG=4 |
| 3 | N-R→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-1.902 | ΔED=-2.003 | nearest=P3(N-R.b1×S-L.b2,governs); manual IG=5, engine IG=5 |
| 4 | N-R→c | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 5 | N-T→d | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 6 | N-T→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.186 | ΔED=+0.235 | nearest=P3(N-T.b2×E-T.b1,governs); manual IG=4, engine IG=4 |
| 7 | N-T→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.119 | ΔED=+0.042 | nearest=P2(N-T.b1@end×E-L.b1~proj,not-governing); manual IG=4, engine IG=4 |
| 8 | N-T→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.182 | ΔED=+0.000 | nearest=P5(N-T.b2~proj×S-L.b2@end,governs); manual IG=6, engine IG=6 |
| 8 | N-T→W-R | P1 | POINT_NOT_REPRODUCED | ΔCD=-4.407 | ΔED=+0.914 | nearest=P4(N-T.b1@end×W-R.b1~proj,not-governing); manual IG=5, engine IG=5 |
| 8 | N-T→W-R | P2 | POINT_NOT_REPRODUCED | ΔCD=-2.564 | ΔED=-2.962 | nearest=P5(N-T.b2~proj×W-R.b1@start,not-governing); manual IG=5, engine IG=5 |
| 10 | N-T→W-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.173 | ΔED=+0.102 | nearest=P3(N-T.b1×W-T.b2,governs); manual IG=5, engine IG=5 |
| 11 | N-T→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.188 | ΔED=+0.022 | nearest=P6(N-T.b1×W-L.b2,governs); manual IG=5, engine IG=5 |
| 12 | N-T→b | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.561 | ΔED=+0.000 | nearest=P1(N-T.b1×b.e4,governs); manual IG=6, engine IG=6 |
| 13 | N-L→d | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 14 | N-L→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.375 | ΔED=+0.000 | nearest=P5(N-L.b1×E-T.b1,not-governing); manual IG=6, engine IG=6 |
| 15 | N-L→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-4.017 | ΔED=+4.228 | nearest=P4(N-L.b1×E-L.b1,governs); manual IG=6, engine IG=5 |
| 16 | N-L→S-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.163 | ΔED=-0.023 | nearest=P6(N-L.b2×S-R.b1,governs); manual IG=5, engine IG=5 |
| 17 | N-L→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.183 | ΔED=-0.043 | nearest=P4(N-L.b2×S-T.b2,governs); manual IG=5, engine IG=5 |
| 19 | N-L→W-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.163 | ΔED=+0.037 | nearest=P4(N-L.b2~proj×W-T.b2@end,governs); manual IG=4, engine IG=4 |
| 20 | N-L→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.194 | ΔED=+0.104 | nearest=P5(N-L.b2×W-L.b2,governs); manual IG=4, engine IG=4 |
| 21 | N-L→a | P1 | POINT_NOT_REPRODUCED | ΔCD=-23.647 | ΔED=+0.000 | nearest=P4(N-L.b2×a.e1,governs); manual IG=6, engine IG=4 |
| 22 | E-R→a | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.902 | ΔED=+0.000 | nearest=P1(E-R.b1×a.e1,not-governing); manual IG=4, engine IG=5 |
| 23 | E-R→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=-1.499 | ΔED=+0.085 | nearest=P3(E-R.b2~proj×S-T.b2@end,governs); manual IG=4, engine IG=4 |
| 24 | E-R→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.333 | ΔED=+0.286 | nearest=P5(E-R.b2×W-L.b1,governs); manual IG=4, engine IG=4 |
| 25 | E-R→d | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 26 | E-T→a | P1 | INVALID_MANUAL_BASELINE | — | — | CD missing in workbook |
| 27 | E-T→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.170 | ΔED=+0.057 | nearest=P4(E-T.b1×S-T.b1,governs); manual IG=5, engine IG=5 |
| 28 | E-T→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.146 | ΔED=-0.012 | nearest=P3(E-T.b1×S-L.b2,governs); manual IG=5, engine IG=5 |
| 29 | E-T→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.244 | ΔED=+0.003 | nearest=P6(E-T.b1@start×W-L.b1~proj,governs); manual IG=6, engine IG=6 |
| 30 | E-T→N-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.938 | ΔED=+0.833 | nearest=P4(E-T.b2@start×N-R.b2~proj,not-governing); manual IG=5, engine IG=5 |
| 31 | E-T→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.199 | ΔED=+0.203 | nearest=P3(E-T.b2×N-T.b1,governs); manual IG=5, engine IG=5 |
| 32 | E-T→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.201 | ΔED=+0.192 | nearest=P7(E-T.b2×N-L.b2,governs); manual IG=5, engine IG=5 |
| 33 | E-T→c | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 34 | E-L→a | P1 | INVALID_MANUAL_BASELINE | — | — | CD missing in workbook |
| 35 | E-L→S-T | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.003 | ΔED=+0.045 | nearest=P4(E-L.b1×S-T.b1,governs); manual IG=6, engine IG=6 |
| 36 | E-L→S-L | P1 | VERY_CLOSE | ΔCD=-0.002 | ΔED=+0.000 | nearest=P3(E-L.b1~proj×S-L.b2@end,not-governing); manual IG=6, engine IG=6 |
| 37 | E-L→W-R | P1 | POINT_NOT_REPRODUCED | ΔCD=-4.352 | ΔED=+0.854 | nearest=P5(E-L.b2@start×W-R.b1~proj,not-governing); manual IG=6, engine IG=6 |
| 38 | E-L→W-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.201 | ΔED=+0.161 | nearest=P4(E-L.b2×W-T.b2,governs); manual IG=5, engine IG=5 |
| 39 | E-L→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.227 | ΔED=+0.103 | nearest=P2(E-L.b2×W-L.b2,governs); manual IG=5, engine IG=5 |
| 40 | E-L→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.198 | ΔED=+0.193 | nearest=P4(E-L.b2×N-T.b1,governs); manual IG=5, engine IG=5 |
| 41 | E-L→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.206 | ΔED=+0.208 | nearest=P4(E-L.b2×N-L.b2,governs); manual IG=5, engine IG=5 |
| 42 | E-L→b | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.198 | ΔED=+0.000 | nearest=P7(E-L.b2×b.e4,governs); manual IG=6, engine IG=6 |
| 43 | S-R→b | P1 | POINT_NOT_REPRODUCED | ΔCD=+3.924 | ΔED=+0.000 | nearest=P3(S-R.b1×b.e1,not-governing); manual IG=4, engine IG=6 |
| 44 | S-R→W-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.377 | ΔED=+0.437 | nearest=P5(S-R.b2~proj×W-T.b2@end,not-governing); manual IG=4, engine IG=4 |
| 45 | S-R→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-1.570 | ΔED=-1.405 | nearest=P6(S-R.b2×N-L.b1,governs); manual IG=4, engine IG=4 |
| 46 | S-R→a | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 47 | S-T→b | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 48 | S-T→W-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.123 | ΔED=+0.125 | nearest=P3(S-T.b1×W-T.b1,governs); manual IG=4, engine IG=4 |
| 49 | S-T→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.085 | ΔED=+0.029 | nearest=P10(S-T.b2@end×W-L.b1~proj,governs); manual IG=4, engine IG=4 |
| 50 | S-T→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-2.091 | ΔED=+2.347 | nearest=P4(S-T.b1×N-L.b1,governs); manual IG=6, engine IG=5 |
| 51 | S-T→E-R | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.005 | ΔED=+1.052 | nearest=P2(S-T.b2@end×E-R.b1~proj,governs); manual IG=5, engine IG=5 |
| 52 | S-T→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.007 | ΔED=+0.134 | nearest=P4(S-T.b2×E-T.b2,governs); manual IG=4, engine IG=4 |
| 53 | S-T→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.006 | ΔED=+0.174 | nearest=P4(S-T.b2×E-L.b2,governs); manual IG=4, engine IG=4 |
| 54 | S-T→d | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 55 | S-L→b | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 56 | S-L→W-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.096 | ΔED=+0.001 | nearest=P6(S-L.b2@start×W-T.b1~proj,governs); manual IG=5, engine IG=5 |
| 57 | S-L→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.096 | ΔED=+0.003 | nearest=P4(S-L.b2@start×W-L.b1~proj,governs); manual IG=5, engine IG=5 |
| 58 | S-L→N-R | P1 | POINT_NOT_REPRODUCED | ΔCD=-9.948 | ΔED=-2.881 | nearest=P2(S-L.b1×N-R.b1,governs); manual IG=5, engine IG=4 |
| 59 | S-L→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=-0.005 | ΔED=+0.196 | nearest=P5(S-L.b1×N-T.b1,governs); manual IG=4, engine IG=4 |
| 61 | S-L→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=-6.051 | ΔED=+0.406 | nearest=P3(S-L.b2×E-T.b1,not-governing); manual IG=4, engine IG=4 |
| 62 | S-L→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.003 | ΔED=+0.196 | nearest=P5(S-L.b1×E-L.b2,governs); manual IG=3, engine IG=3 |
| 63 | S-L→c | P1 | POINT_NOT_REPRODUCED | ΔCD=-17.336 | ΔED=+0.000 | nearest=P4(S-L.b2×c.e2,governs); manual IG=6, engine IG=4 |
| 64 | W-R→c | P1 | POINT_NOT_REPRODUCED | ΔCD=-2.673 | ΔED=+0.000 | nearest=P2(W-R.b2×c.e2,not-governing); manual IG=4, engine IG=5 |
| 65 | W-R→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.088 | ΔED=-0.095 | nearest=P4(W-R.b1@start×N-T.b1~proj,governs); manual IG=4, engine IG=4 |
| 66 | W-R→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-2.912 | ΔED=-1.688 | nearest=P4(W-R.b1@start×E-L.b1~proj,governs); manual IG=4, engine IG=4 |
| 67 | W-R→b | P1 | POINT_NOT_REPRODUCED | ΔCD=-6.292 | ΔED=+0.000 | nearest=P4(W-R.b1×b.e5,governs); manual IG=6, engine IG=5 |
| 68 | W-T→c | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 69 | W-T→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.048 | ΔED=+0.194 | nearest=P4(W-T.b1×N-T.b2,governs); manual IG=4, engine IG=4 |
| 70 | W-T→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.437 | ΔED=+0.199 | nearest=P3(W-T.b2@end×N-L.b1~proj,governs); manual IG=5, engine IG=5 |
| 71 | W-T→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=-4.154 | ΔED=+4.245 | nearest=P4(W-T.b1×E-L.b1,governs); manual IG=6, engine IG=6 |
| 72 | W-T→S-L | P1 | SMALL_DELTA_REVIEW | ΔCD=-0.018 | ΔED=+0.019 | nearest=P7(W-T.b2×S-L.b1,governs); manual IG=5, engine IG=5 |
| 73 | W-T→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.105 | ΔED=+0.014 | nearest=P3(W-T.b2×S-T.b2,governs); manual IG=5, engine IG=5 |
| 74 | W-T→S-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.137 | ΔED=-0.023 | nearest=P4(W-T.b2@end×S-R.b1~proj,governs); manual IG=5, engine IG=5 |
| 75 | W-T→a | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 76 | W-L→c | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 77 | W-L→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.259 | ΔED=+0.000 | nearest=P6(W-L.b1@start×N-T.b1~proj,governs); manual IG=6, engine IG=6 |
| 78 | W-L→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.259 | ΔED=+0.000 | nearest=P4(W-L.b1×N-L.b1,not-governing); manual IG=6, engine IG=6 |
| 79 | W-L→E-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.121 | ΔED=+0.202 | nearest=P4(W-L.b2@start×E-R.b1~proj,governs); manual IG=6, engine IG=6 |
| 80 | W-L→E-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.099 | ΔED=+0.200 | nearest=P7(W-L.b2×E-T.b2,governs); manual IG=5, engine IG=5 |
| 81 | W-L→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.097 | ΔED=+0.195 | nearest=P2(W-L.b2×E-L.b2,governs); manual IG=5, engine IG=5 |
| 82 | W-L→S-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.097 | ΔED=-0.010 | nearest=P10(W-L.b2@start×S-T.b2~proj,not-governing); manual IG=5, engine IG=5 |
| 83 | W-L→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.105 | ΔED=+0.071 | nearest=P5(W-L.b2×S-L.b1,governs); manual IG=5, engine IG=5 |
| 84 | W-L→d | P1 | POINT_NOT_REPRODUCED | ΔCD=-25.045 | ΔED=+0.000 | nearest=P4(W-L.b2×d.e1,governs); manual IG=6, engine IG=4 |
| 85 | d→N-L | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 86 | d→N-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+11.027 | nearest=P1(d.e1×N-R.b2,governs); manual IG=10, engine IG=9 |
| 87 | d→N-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 88 | d→E-R | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 89 | d→S-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 90 | d→W-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=-14.145 | nearest=P4(d.e1×W-L.b2,not-governing); manual IG=9, engine IG=10 |
| 91 | a→E-L | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 92 | a→E-R | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+4.652 | nearest=P1(a.e1×E-R.b1,governs); manual IG=9, engine IG=9 |
| 93 | a→E-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 94 | a→N-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=-12.647 | nearest=P4(a.e1×N-L.b2,not-governing); manual IG=8, engine IG=9 |
| 95 | a→S-R | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 96 | a→W-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 97 | b→S-L | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 98 | b→S-R | P1 | VERY_CLOSE | ΔCD=+0.000 | ΔED=+0.000 | nearest=P1(b.band×S-R.b2@end,governs); manual IG=14, engine IG=14 |
| 99 | b→S-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 100 | b→E-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+10.998 | nearest=P7(b.e4×E-L.b2,not-governing); manual IG=13, engine IG=14 |
| 101 | b→N-T | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=+3.861 | nearest=P1(b.e4×N-T.b1,governs); manual IG=13, engine IG=12 |
| 102 | b→W-R | P1 | VERY_CLOSE | ΔCD=+0.000 | ΔED=+0.000 | nearest=P1(b.band×W-R.b2@end,governs); manual IG=14, engine IG=14 |
| 103 | c→W-L | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 104 | c→W-R | P1 | VERY_CLOSE | ΔCD=+0.000 | ΔED=+0.000 | nearest=P1(c.band×W-R.b2@end,governs); manual IG=7, engine IG=7 |
| 105 | c→W-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status REVIEW_REQUIRED |
| 106 | c→E-T | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
| 107 | c→S-L | P1 | POINT_NOT_REPRODUCED | ΔCD=+0.000 | ΔED=-7.597 | nearest=P4(c.e2×S-L.b2,not-governing); manual IG=6, engine IG=7 |
| 108 | c→N-R | P1 | NO_ENGINE_CANDIDATES | — | — | engine status absent |
