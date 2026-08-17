# EXAMPLE2 — real-DWG Legacy regression (Directive §10)

Geometry: `ex2.iggeometry.json` (INSUNITS=Unitless, toMeters=1)
Workbook: `Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx`  ·  Rule pack: legacy-mahod-v1  ·  tolerance 0.25 m

## Summary

| metric | value |
|---|---|
| manual rows | 108 |
| comparable (valid manual baseline) | 104 |
| MATCH | 28 |
| ENGINE_RESULT_HIGHER_REVIEW | 0 |
| ENGINE_RESULT_LOWER_ERROR | 0 |
| MANUAL_POINT_NOT_FOUND | 76 |
| INVALID_MANUAL_BASELINE | 4 |
| rows where automation found additional points | 64 |
| engine conflicts total | 186 |
| engine candidate points total | 434 |
| validation errors | 32 |
| validation warnings | 62 |
| blocked matrix cells | 8 |

## Rows

| conflict | pair | classification | ΔCD [m] | ΔED [m] | detail |
|---|---|---|---|---|---|
| 1 | N-R→d | MANUAL_POINT_NOT_FOUND | ΔCD=+7.027 | ΔED=+0.000 | manual IG=4, engine IG=6, pts=4 |
| 2 | N-R→E-T | MANUAL_POINT_NOT_FOUND | ΔCD=-2.007 | ΔED=-2.051 | manual IG=4, engine IG=4, pts=3 |
| 3 | N-R→S-L | MANUAL_POINT_NOT_FOUND | ΔCD=-1.902 | ΔED=-2.003 | manual IG=5, engine IG=5, pts=3 |
| 4 | N-R→c | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 5 | N-T→d | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 6 | N-T→E-T | MATCH | ΔCD=+0.186 | ΔED=+0.235 | manual IG=4, engine IG=4, pts=4 |
| 7 | N-T→E-L | MATCH | ΔCD=+0.207 | ΔED=+0.012 | manual IG=4, engine IG=4, pts=3 |
| 8 | N-T→S-L | MATCH | ΔCD=+0.182 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=4 |
| 8 | N-T→W-R | MANUAL_POINT_NOT_FOUND | ΔCD=-8.458 | ΔED=-2.732 | manual IG=5, engine IG=5, pts=1 |
| 10 | N-T→W-T | MATCH | ΔCD=+0.173 | ΔED=+0.102 | manual IG=5, engine IG=5, pts=4 |
| 11 | N-T→W-L | MATCH | ΔCD=+0.188 | ΔED=+0.022 | manual IG=5, engine IG=5, pts=5 |
| 12 | N-T→b | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 13 | N-L→d | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 14 | N-L→E-T | MANUAL_POINT_NOT_FOUND | ΔCD=+0.375 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=5 |
| 15 | N-L→E-L | MANUAL_POINT_NOT_FOUND | ΔCD=-4.017 | ΔED=+4.228 | manual IG=6, engine IG=5, pts=4 |
| 16 | N-L→S-R | MATCH | ΔCD=+0.163 | ΔED=-0.023 | manual IG=5, engine IG=5, pts=5 |
| 17 | N-L→S-T | MATCH | ΔCD=+0.183 | ΔED=-0.043 | manual IG=5, engine IG=5, pts=4 |
| 18 | N-L→S-L | INVALID_MANUAL_BASELINE | — | — | incomplete manual measurement |
| 19 | N-L→W-T | MANUAL_POINT_NOT_FOUND | ΔCD=-7.227 | ΔED=-7.335 | manual IG=4, engine IG=4, pts=2 |
| 20 | N-L→W-L | MATCH | ΔCD=+0.194 | ΔED=+0.104 | manual IG=4, engine IG=4, pts=4 |
| 21 | N-L→a | MANUAL_POINT_NOT_FOUND | ΔCD=-23.647 | ΔED=+0.000 | manual IG=6, engine IG=4, pts=4 |
| 22 | E-R→a | MANUAL_POINT_NOT_FOUND | ΔCD=+0.902 | ΔED=+0.000 | manual IG=4, engine IG=5, pts=4 |
| 23 | E-R→S-T | MANUAL_POINT_NOT_FOUND | ΔCD=-6.241 | ΔED=-5.217 | manual IG=4, engine IG=4, pts=1 |
| 24 | E-R→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=+0.333 | ΔED=+0.286 | manual IG=4, engine IG=4, pts=2 |
| 25 | E-R→d | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 26 | E-T→a | INVALID_MANUAL_BASELINE | — | — | incomplete manual measurement |
| 27 | E-T→S-T | MATCH | ΔCD=+0.170 | ΔED=+0.057 | manual IG=5, engine IG=5, pts=4 |
| 28 | E-T→S-L | MATCH | ΔCD=+0.146 | ΔED=-0.012 | manual IG=5, engine IG=5, pts=3 |
| 29 | E-T→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=-2.755 | ΔED=+3.000 | manual IG=6, engine IG=6, pts=5 |
| 30 | E-T→N-R | MANUAL_POINT_NOT_FOUND | ΔCD=-6.675 | ΔED=+0.206 | manual IG=5, engine IG=5, pts=3 |
| 31 | E-T→N-T | MATCH | ΔCD=+0.199 | ΔED=+0.203 | manual IG=5, engine IG=5, pts=4 |
| 32 | E-T→N-L | MATCH | ΔCD=+0.201 | ΔED=+0.192 | manual IG=5, engine IG=5, pts=5 |
| 33 | E-T→c | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 34 | E-L→a | INVALID_MANUAL_BASELINE | — | — | incomplete manual measurement |
| 35 | E-L→S-T | MATCH | ΔCD=-0.003 | ΔED=+0.045 | manual IG=6, engine IG=6, pts=4 |
| 36 | E-L→S-L | MANUAL_POINT_NOT_FOUND | ΔCD=+1.514 | ΔED=+5.849 | manual IG=6, engine IG=5, pts=3 |
| 37 | E-L→W-R | MANUAL_POINT_NOT_FOUND | ΔCD=-7.839 | ΔED=-1.981 | manual IG=6, engine IG=5, pts=1 |
| 38 | E-L→W-T | MATCH | ΔCD=+0.201 | ΔED=+0.161 | manual IG=5, engine IG=5, pts=4 |
| 39 | E-L→W-L | MATCH | ΔCD=+0.227 | ΔED=+0.103 | manual IG=5, engine IG=5, pts=2 |
| 40 | E-L→N-T | MATCH | ΔCD=+0.198 | ΔED=+0.193 | manual IG=5, engine IG=5, pts=3 |
| 41 | E-L→N-L | MATCH | ΔCD=+0.206 | ΔED=+0.208 | manual IG=5, engine IG=5, pts=4 |
| 42 | E-L→b | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 43 | S-R→b | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 44 | S-R→W-T | MANUAL_POINT_NOT_FOUND | ΔCD=-0.875 | ΔED=-1.132 | manual IG=4, engine IG=4, pts=3 |
| 45 | S-R→N-L | MANUAL_POINT_NOT_FOUND | ΔCD=-1.570 | ΔED=-1.405 | manual IG=4, engine IG=4, pts=5 |
| 46 | S-R→a | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 47 | S-T→b | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 48 | S-T→W-T | MATCH | ΔCD=+0.123 | ΔED=+0.125 | manual IG=4, engine IG=4, pts=4 |
| 49 | S-T→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=-2.400 | ΔED=-2.340 | manual IG=4, engine IG=4, pts=5 |
| 50 | S-T→N-L | MANUAL_POINT_NOT_FOUND | ΔCD=-2.091 | ΔED=+2.347 | manual IG=6, engine IG=5, pts=4 |
| 51 | S-T→E-R | MANUAL_POINT_NOT_FOUND | ΔCD=-5.307 | ΔED=+2.359 | manual IG=5, engine IG=4, pts=1 |
| 52 | S-T→E-T | MATCH | ΔCD=-0.007 | ΔED=+0.134 | manual IG=4, engine IG=4, pts=4 |
| 53 | S-T→E-L | MATCH | ΔCD=-0.006 | ΔED=+0.174 | manual IG=4, engine IG=4, pts=4 |
| 54 | S-T→d | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 55 | S-L→b | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 56 | S-L→W-T | MANUAL_POINT_NOT_FOUND | ΔCD=-0.617 | ΔED=+0.713 | manual IG=5, engine IG=5, pts=5 |
| 57 | S-L→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=-3.717 | ΔED=+10.254 | manual IG=5, engine IG=4, pts=3 |
| 58 | S-L→N-R | MANUAL_POINT_NOT_FOUND | ΔCD=-9.948 | ΔED=-2.881 | manual IG=5, engine IG=4, pts=3 |
| 59 | S-L→N-T | MATCH | ΔCD=-0.005 | ΔED=+0.196 | manual IG=4, engine IG=4, pts=4 |
| 60 | S-L→N-L | INVALID_MANUAL_BASELINE | — | — | incomplete manual measurement |
| 61 | S-L→E-T | MANUAL_POINT_NOT_FOUND | ΔCD=-6.051 | ΔED=+0.406 | manual IG=4, engine IG=4, pts=3 |
| 62 | S-L→E-L | MATCH | ΔCD=+0.003 | ΔED=+0.196 | manual IG=3, engine IG=3, pts=3 |
| 63 | S-L→c | MANUAL_POINT_NOT_FOUND | ΔCD=-17.336 | ΔED=+0.000 | manual IG=6, engine IG=4, pts=4 |
| 64 | W-R→c | MANUAL_POINT_NOT_FOUND | ΔCD=-2.673 | ΔED=+0.000 | manual IG=4, engine IG=5, pts=3 |
| 65 | W-R→N-T | MANUAL_POINT_NOT_FOUND | ΔCD=-4.662 | ΔED=-4.090 | manual IG=4, engine IG=4, pts=1 |
| 66 | W-R→E-L | MANUAL_POINT_NOT_FOUND | ΔCD=-6.851 | ΔED=+1.781 | manual IG=4, engine IG=3, pts=1 |
| 67 | W-R→b | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 68 | W-T→c | MANUAL_POINT_NOT_FOUND | — | — | engine status REVIEW_REQUIRED |
| 69 | W-T→N-T | MATCH | ΔCD=+0.048 | ΔED=+0.194 | manual IG=4, engine IG=4, pts=4 |
| 70 | W-T→N-L | MANUAL_POINT_NOT_FOUND | ΔCD=-6.935 | ΔED=+0.173 | manual IG=5, engine IG=4, pts=2 |
| 71 | W-T→E-L | MANUAL_POINT_NOT_FOUND | ΔCD=-4.154 | ΔED=+4.245 | manual IG=6, engine IG=6, pts=4 |
| 72 | W-T→S-L | MATCH | ΔCD=-0.018 | ΔED=+0.019 | manual IG=5, engine IG=5, pts=5 |
| 73 | W-T→S-T | MATCH | ΔCD=+0.105 | ΔED=+0.014 | manual IG=5, engine IG=5, pts=4 |
| 74 | W-T→S-R | MANUAL_POINT_NOT_FOUND | ΔCD=-2.321 | ΔED=+4.955 | manual IG=5, engine IG=5, pts=3 |
| 75 | W-T→a | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 76 | W-L→c | MANUAL_POINT_NOT_FOUND | — | — | engine status REVIEW_REQUIRED |
| 77 | W-L→N-T | MANUAL_POINT_NOT_FOUND | ΔCD=-0.667 | ΔED=+0.925 | manual IG=6, engine IG=6, pts=5 |
| 78 | W-L→N-L | MANUAL_POINT_NOT_FOUND | ΔCD=+0.259 | ΔED=+0.000 | manual IG=6, engine IG=6, pts=4 |
| 79 | W-L→E-R | MANUAL_POINT_NOT_FOUND | ΔCD=-5.439 | ΔED=+1.139 | manual IG=6, engine IG=5, pts=2 |
| 80 | W-L→E-T | MATCH | ΔCD=+0.099 | ΔED=+0.200 | manual IG=5, engine IG=5, pts=5 |
| 81 | W-L→E-L | MATCH | ΔCD=+0.097 | ΔED=+0.195 | manual IG=5, engine IG=5, pts=2 |
| 82 | W-L→S-T | MANUAL_POINT_NOT_FOUND | ΔCD=-1.865 | ΔED=-1.973 | manual IG=5, engine IG=5, pts=5 |
| 83 | W-L→S-L | MATCH | ΔCD=+0.105 | ΔED=+0.071 | manual IG=5, engine IG=5, pts=3 |
| 84 | W-L→d | MANUAL_POINT_NOT_FOUND | ΔCD=-25.045 | ΔED=+0.000 | manual IG=6, engine IG=4, pts=4 |
| 85 | d→N-L | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 86 | d→N-R | MANUAL_POINT_NOT_FOUND | ΔCD=-3.434 | ΔED=+11.027 | manual IG=10, engine IG=7, pts=4 |
| 87 | d→N-T | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 88 | d→E-R | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 89 | d→S-T | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 90 | d→W-L | MANUAL_POINT_NOT_FOUND | ΔCD=-3.434 | ΔED=-14.145 | manual IG=9, engine IG=7, pts=4 |
| 91 | a→E-L | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 92 | a→E-R | MANUAL_POINT_NOT_FOUND | ΔCD=+1.117 | ΔED=+4.652 | manual IG=9, engine IG=10, pts=4 |
| 93 | a→E-T | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 94 | a→N-L | MANUAL_POINT_NOT_FOUND | ΔCD=+1.117 | ΔED=-12.647 | manual IG=8, engine IG=10, pts=4 |
| 95 | a→S-R | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 96 | a→W-T | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 97 | b→S-L | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 98 | b→S-R | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 99 | b→S-T | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 100 | b→E-L | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 101 | b→N-T | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 102 | b→W-R | MANUAL_POINT_NOT_FOUND | — | — | engine status ERROR |
| 103 | c→W-L | MANUAL_POINT_NOT_FOUND | — | — | engine status REVIEW_REQUIRED |
| 104 | c→W-R | MANUAL_POINT_NOT_FOUND | ΔCD=+7.580 | ΔED=+1.127 | manual IG=7, engine IG=14, pts=3 |
| 105 | c→W-T | MANUAL_POINT_NOT_FOUND | — | — | engine status REVIEW_REQUIRED |
| 106 | c→E-T | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
| 107 | c→S-L | MANUAL_POINT_NOT_FOUND | ΔCD=+7.580 | ΔED=-7.597 | manual IG=6, engine IG=14, pts=4 |
| 108 | c→N-R | MANUAL_POINT_NOT_FOUND | — | — | no engine conflict |
