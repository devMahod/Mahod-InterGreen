# EXAMPLE 1 — residual >0.05 m point classification (Directive §20)

11 residual points, all with **engine IG == manual IG** (0 unsafe; the fixed E-R→a case now
matches at ΔCD −0.020 via its boundary-termination candidate).

| conflict | pair | ΔCD | ΔED | classification | evidence |
|---|---|---|---|---|---|
| 89 | a→S-T | 0.000 | +0.668 | MEASUREMENT_SEMANTICS_DIFFERENCE | W exact; systematic +0.6–0.7 m ED offset on all ped-clearing rows: engine measures the entering station along the envelope boundary to the crossing edge, the manual trim measured a slightly shorter path. IG 6=6. |
| 92 | b→E-R | 0.000 | +0.708 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above, IG 6=6 |
| 93 | b→E-T | 0.000 | +0.700 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above, IG 6=6 |
| 97 | d→S-L | 0.000 | +0.599 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above, IG 6=6 |
| 98 | d→S-R | 0.000 | +0.602 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above, IG 6=6 |
| 99 | d→S-T | 0.000 | +0.602 | MEASUREMENT_SEMANTICS_DIFFERENCE | as above, IG 6=6 |
| 24 | E-R→W-L | -0.020 | -1.150 | MEASUREMENT_SEMANTICS_DIFFERENCE | CD matches to 2 cm; manual ED larger than every candidate on that geometry — manual trim point sits mid-zone; engine ED smaller = safe direction; IG 4=4 |
| 26 | E-T→b | -0.188 | 0.000 | DRAWING_AMBIGUITY | crossing-b far edge vs boundary end within 19 cm; IG 4=4 |
| 28 | E-T→S-L | -0.002 | -0.222 | MEASUREMENT_SEMANTICS_DIFFERENCE | CD exact; 22 cm ED offset, nearest candidate not governing; IG 5=5 |
| 49 | S-T→W-L | +1.975 | +1.995 | DRAWING_AMBIGUITY | manual point ~2 m from every candidate (incl. termination candidates); governing candidate yields identical IG 5=5; historical point measured inside the zone, not at a boundary construction |
| 74 | W-T→S-R | -2.304 | +4.107 | DRAWING_AMBIGUITY | manual CD 22.90 within boundary length but no boundary construction reproduces the pair; engine governing IG 5=5 equal; kept visible for the engineering review session |

**Unsafe unexplained discrepancies: 0** — in every residual the engine's final IG equals the
manual final IG; no engine-lower case exists after the §21A termination-candidate rule.
