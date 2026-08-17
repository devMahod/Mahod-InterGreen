# EXAMPLE1 — real-DWG Legacy regression (Directive §10)

Geometry: `ex1.iggeometry.json` (INSUNITS=Unitless, toMeters=1)
Workbook: `05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx`  ·  Rule pack: legacy-mahod-v1  ·  tolerance 0.25 m

## Summary

| metric | value |
|---|---|
| manual rows | 40 |
| comparable (valid manual baseline) | 40 |
| MATCH | 28 |
| ENGINE_RESULT_HIGHER_REVIEW | 0 |
| ENGINE_RESULT_LOWER_ERROR | 0 |
| MANUAL_POINT_NOT_FOUND | 12 |
| INVALID_MANUAL_BASELINE | 0 |
| rows where automation found additional points | 40 |
| engine conflicts total | 82 |
| engine candidate points total | 168 |
| validation errors | 0 |
| validation warnings | 1 |
| blocked matrix cells | 18 |

## Rows

| conflict | pair | classification | ΔCD [m] | ΔED [m] | detail |
|---|---|---|---|---|---|
| 22 | E-R→b | MATCH | ΔCD=-0.002 | ΔED=+0.000 | manual IG=4, engine IG=4, pts=4 |
| 23 | E-R→S-T | MATCH | ΔCD=-0.020 | ΔED=-0.020 | manual IG=4, engine IG=4, pts=3 |
| 24 | E-R→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=-0.020 | ΔED=-1.150 | manual IG=4, engine IG=4, pts=3 |
| 25 | E-R→a | MANUAL_POINT_NOT_FOUND | ΔCD=-3.133 | ΔED=+0.000 | manual IG=6, engine IG=5, pts=3 |
| 26 | E-T→b | MATCH | ΔCD=-0.188 | ΔED=+0.000 | manual IG=4, engine IG=4, pts=4 |
| 27 | E-T→S-T | MATCH | ΔCD=-0.019 | ΔED=-0.034 | manual IG=4, engine IG=4, pts=4 |
| 28 | E-T→S-L | MATCH | ΔCD=-0.002 | ΔED=-0.222 | manual IG=5, engine IG=5, pts=6 |
| 29 | E-T→W-L | MATCH | ΔCD=-0.016 | ΔED=-0.026 | manual IG=5, engine IG=5, pts=4 |
| 43 | S-R→d | MATCH | ΔCD=-0.001 | ΔED=+0.000 | manual IG=4, engine IG=4, pts=4 |
| 44 | S-R→W-T | MATCH | ΔCD=+0.007 | ΔED=-0.004 | manual IG=4, engine IG=4, pts=2 |
| 46 | S-R→c | MATCH | ΔCD=-0.023 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=4 |
| 47 | S-T→d | MATCH | ΔCD=-0.021 | ΔED=+0.000 | manual IG=4, engine IG=4, pts=4 |
| 48 | S-T→W-T | MATCH | ΔCD=-0.033 | ΔED=-0.004 | manual IG=4, engine IG=4, pts=4 |
| 49 | S-T→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=+1.975 | ΔED=+1.995 | manual IG=5, engine IG=5, pts=5 |
| 51 | S-T→E-R | MATCH | ΔCD=-0.028 | ΔED=-0.032 | manual IG=6, engine IG=6, pts=3 |
| 52 | S-T→E-T | MATCH | ΔCD=-0.030 | ΔED=+0.004 | manual IG=5, engine IG=5, pts=4 |
| 54 | S-T→a | MATCH | ΔCD=-0.028 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=3 |
| 55 | S-L→d | MATCH | ΔCD=-0.022 | ΔED=+0.000 | manual IG=4, engine IG=4, pts=4 |
| 56 | S-L→W-T | MATCH | ΔCD=-0.040 | ΔED=-0.026 | manual IG=4, engine IG=4, pts=4 |
| 57 | S-L→W-L | MATCH | ΔCD=-0.029 | ΔED=-0.012 | manual IG=5, engine IG=5, pts=4 |
| 61 | S-L→E-T | MATCH | ΔCD=+0.008 | ΔED=+0.004 | manual IG=4, engine IG=4, pts=6 |
| 72 | W-T→S-L | MATCH | ΔCD=-0.030 | ΔED=-0.024 | manual IG=4, engine IG=4, pts=4 |
| 73 | W-T→S-T | MATCH | ΔCD=-0.019 | ΔED=-0.010 | manual IG=5, engine IG=5, pts=4 |
| 74 | W-T→S-R | MANUAL_POINT_NOT_FOUND | ΔCD=-2.304 | ΔED=+4.107 | manual IG=5, engine IG=4, pts=2 |
| 75 | W-T→c | MATCH | ΔCD=-0.035 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=4 |
| 79 | W-L→E-R | MATCH | ΔCD=-0.028 | ΔED=-0.032 | manual IG=6, engine IG=6, pts=3 |
| 80 | W-L→E-T | MATCH | ΔCD=+0.006 | ΔED=+0.004 | manual IG=5, engine IG=5, pts=4 |
| 82 | W-L→S-T | MATCH | ΔCD=-0.004 | ΔED=-0.001 | manual IG=4, engine IG=4, pts=5 |
| 83 | W-L→S-L | MATCH | ΔCD=-0.003 | ΔED=-0.001 | manual IG=4, engine IG=4, pts=4 |
| 84 | W-L→a | MATCH | ΔCD=-0.028 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=4 |
| 88 | a→E-R | MANUAL_POINT_NOT_FOUND | ΔCD=+0.291 | ΔED=-0.038 | manual IG=7, engine IG=7, pts=3 |
| 89 | a→S-T | MANUAL_POINT_NOT_FOUND | ΔCD=+0.291 | ΔED=+0.668 | manual IG=6, engine IG=6, pts=3 |
| 90 | a→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=+0.291 | ΔED=-0.025 | manual IG=6, engine IG=6, pts=4 |
| 92 | b→E-R | MANUAL_POINT_NOT_FOUND | ΔCD=+0.273 | ΔED=+0.708 | manual IG=6, engine IG=7, pts=4 |
| 93 | b→E-T | MANUAL_POINT_NOT_FOUND | ΔCD=+0.273 | ΔED=+0.700 | manual IG=6, engine IG=7, pts=4 |
| 95 | c→S-R | MATCH | ΔCD=-0.019 | ΔED=-0.016 | manual IG=3, engine IG=3, pts=4 |
| 96 | c→W-T | MATCH | ΔCD=-0.019 | ΔED=-0.036 | manual IG=3, engine IG=3, pts=4 |
| 97 | d→S-L | MANUAL_POINT_NOT_FOUND | ΔCD=+0.168 | ΔED=+0.599 | manual IG=6, engine IG=7, pts=4 |
| 98 | d→S-R | MANUAL_POINT_NOT_FOUND | ΔCD=+0.168 | ΔED=+0.602 | manual IG=6, engine IG=7, pts=4 |
| 99 | d→S-T | MANUAL_POINT_NOT_FOUND | ΔCD=+0.168 | ΔED=+0.602 | manual IG=6, engine IG=7, pts=4 |
