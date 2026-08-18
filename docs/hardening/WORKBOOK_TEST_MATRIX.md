# WORKBOOK_TEST_MATRIX (WorkbookInputTests, 7 — all PASS; + journey suite)
| State | Expected | Result |
|---|---|---|
| original Example 1 / Example 2 | accepted, model built | PASS |
| copied + renamed (Hebrew, quoted input) | accepted | PASS |
| read-only source file | accepted (source never written) | PASS |
| required sheet deleted | UNSUPPORTED_TEMPLATE_VERSION | PASS |
| unrelated ordinary .xlsx | UnsupportedTemplate, no stack trace | PASS |
| corrupt OOXML | rejected safely | PASS |
| open in Excel (exclusive lock sim) | Locked with guidance | PASS |
| missing after sidecar saved / moved / renamed | see SIDECAR_RECOVERY_TESTS | PASS |
| source accidentally as export destination | refused | PASS |
| stale formula cache | by design untouched — engine-authoritative sheets + fullCalcOnLoad | n/a |
Setup commits ONLY after acceptance passes — failures cannot corrupt project state.
