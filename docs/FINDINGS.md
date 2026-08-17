# FINDINGS

Where the engine/verification disagrees with the manual work — the QA value of the tool.

## F-001 — Four intergreens entered the signal matrix without any measurement

Example 2, conflicts **18, 26, 34, 60**: the workbook emits **FINAL IG = 4 s** into the
matrix although the required clearing measurement is absent (blank treated as 0).

- 18 (N-L→S-L), 60 (S-L→N-L): **no CD and no ED at all** → `IG-VAL-001`.
- 26 (E-T→a), 34 (E-L→a): **ED = 0 is the defined pedestrian value (§5.4), CD missing** → `IG-VAL-002`.

Production validation blocks all four; proven by `ProductionValidationRegressionTests`.

## F-002 — Workbook rounding is shorter than §5.7 in 13 of 148 rows

Always in the unsafe (shorter) direction. Full table in ENGINEERING_DECISIONS.md ED-001.
The workbook itself flags these rows in `Consider for manual rounding?` — deliberate practice,
but a deviation from the published ceiling rule.

## F-003 — Duplicate conflict number in Example 2

Two rows are numbered **8** (N-T→S-L and N-T→W-R) and number **9 is skipped**.
Any process keyed on the sheet's conflict number is ambiguous. The engine assigns its own
stable conflict IDs. Proven by `Example2_conflict_numbering_has_a_duplicate_8_and_skips_9`.

## F-004 — Only one point measured per conflict (147 of 148 rows)

The single exception is Example 2 conflict 8 (N-T→W-R) with two points. Full §5.4 compliance
at these intersections would require ~4× the manual measurements; the engine computes all
candidate points at zero cost. This is the core value of the tool.

## F-005 — The `interurban` parameter column is effectively dead

Both workbooks compute with urban 50/25 despite the demo intersection (Route 70) being
interurban. See OQ-005 — raised as a question, not silently "fixed".

## F-006 — Example 1 AutoAdjusted (INBAR) sheet is entirely #REF!

Pre-existing source-workbook error (`SOURCE_WORKBOOK_EXISTING_ERROR`, v3 §33). See OQ-007.

## F-007 — Sub-1-second results silently vanish from the workbook matrix

The AK rounding rule is undefined below 1 s (MOD by zero → blank). No golden row hits this,
but the failure mode is silent omission of a conflict pair. Production emits a value + finding
(`IG-VAL-003`, ED-004).
