# T60_CHECKPOINT

```
Unit tests:              39 / 39   (26 Core + 13 Regression)
Legacy golden FINAL IG:  148 / 148
Legacy golden interm.:   148 / 148
Known QA findings:       4 / 4 detected  (18, 26, 34, 60)
                         26/34 classified MISSING_CLEARING_MEASUREMENT (IG-VAL-002),
                         18/60 classified MISSING_MEASUREMENT (IG-VAL-001)
Minimum-IG tests:        10 / 10  (synthetic sub-3s + boundaries; golden data cannot cover these)
Rounding delta:          13 rows reported (8 in Example 1, 5 in Example 2) — ENGINEERING_DECISIONS.md ED-001
Next gate:               G (Rule Pack framework)
Blocked by:              NONE
```

Independent verification performed before the C# engine existed
(`scripts/extract_golden.py` — Python oracle straight from the source workbooks):
148/148 FINAL IG, 148/148 intermediates, worst float delta 2.665e-15,
exactly 13 rounding-delta rows, single multi-point row = Example 2 conflict 8 (N-T→W-R),
variant V1/V2 wiring confirmed from the constants-block labels.

Additional finding beyond the spec baseline: Example 2 duplicates conflict number 8 and
skips 9 (FINDINGS.md F-003).

Checkpoint artifacts: tests/fixtures/golden-example*.json, test-results/GOLDEN_EXTRACTION_REPORT.md.
