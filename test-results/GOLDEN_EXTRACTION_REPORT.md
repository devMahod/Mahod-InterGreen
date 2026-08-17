# GOLDEN_EXTRACTION_REPORT

Independent Python oracle vs workbook cached values.

## example1 — 05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx

- variant: **V1**, constants: `{'pedSpeed': 1.2, 'reaction': 1, 'decel': 3.5, 'vehLenGlobal': None, 'inbarFlag': 'n', 'inbarDefaultVehicleLength': 12}`
- rows: **40**
- FINAL IG match: **40 / 40**
- intermediate (S/W/AA/AE) rows fully matching: **40 / 40**
- max abs delta on matched intermediates: **1.776e-15**
- rows where FINAL IG < 3: **NONE**
- multi-point rows: **NONE**
- missing-measurement rows: **NONE**
- workbook-vs-ceiling delta rows (8):

  - conflict 22 E-R→b: raw=4.029 workbook=4 ceil=5
  - conflict 26 E-T→b: raw=4.02 workbook=4 ceil=5
  - conflict 82 W-L→S-T: raw=4.051 workbook=4 ceil=5
  - conflict 92 b→E-R: raw=6.042 workbook=6 ceil=7
  - conflict 93 b→E-T: raw=6.042 workbook=6 ceil=7
  - conflict 97 d→S-L: raw=6.042 workbook=6 ceil=7
  - conflict 98 d→S-R: raw=6.042 workbook=6 ceil=7
  - conflict 99 d→S-T: raw=6.042 workbook=6 ceil=7

## example2 — Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx

- variant: **V2**, constants: `{'pedSpeed': 1.2, 'reaction': 1, 'decel': 3.5, 'vehLenGlobal': 12, 'inbarFlag': None, 'inbarDefaultVehicleLength': None}`
- rows: **108**
- FINAL IG match: **108 / 108**
- intermediate (S/W/AA/AE) rows fully matching: **108 / 108**
- max abs delta on matched intermediates: **2.665e-15**
- rows where FINAL IG < 3: **NONE**
- multi-point rows: **[(8, 'N-T→W-R', 2)]**
- missing-measurement rows: **[(18, 'N-L→S-L', 'NO_MEASUREMENT_AT_ALL', 4), (26, 'E-T→a', 'MISSING_CLEARING_MEASUREMENT', 4), (34, 'E-L→a', 'MISSING_CLEARING_MEASUREMENT', 4), (60, 'S-L→N-L', 'NO_MEASUREMENT_AT_ALL', 4)]**
- workbook-vs-ceiling delta rows (5):

  - conflict 25 E-R→d: raw=5.044 workbook=5 ceil=6
  - conflict 43 S-R→b: raw=4.023 workbook=4 ceil=5
  - conflict 62 S-L→E-L: raw=3.076 workbook=3 ceil=4
  - conflict 84 W-L→d: raw=6.006 workbook=6 ceil=7
  - conflict 107 c→S-L: raw=6.096 workbook=6 ceil=7

## Totals

- Legacy golden FINAL IG: **148 / 148**
- intermediate rows: **148 / 148**
- rounding-strategy delta rows: **13**
- worst float delta: **2.665e-15**