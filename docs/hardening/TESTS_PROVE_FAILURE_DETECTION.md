# TESTS_PROVE_FAILURE_DETECTION (5/5 mutations detected, code restored)
| # | Injected defect (in PRODUCT code) | Detecting test | Observed |
|---|---|---|---|
| M1 | quote-trimming removed from resolver | Lin quoted-path regression | Failed: 1 |
| M2 | SixLabors.Fonts.dll removed from exact shipping copy | runtime closure gate | rc nonzero, FileNotFound |
| M3 | moved-workbook CANDIDATE silently returned as Ok | not_auto_used test | Failed: 1 |
| M4 | source==destination export allowed | Source_equals_destination_refused | Failed: 1 |
| M5 | Analyze allowed before Setup | workflow gate test | Failed: 1 |
After each mutation the original code was restored; the full host suite was re-verified
green (56/56) and the closure gate PASS. Tests use literal independent expectations and
temporary filesystem fixtures per the test-the-tests policy.
