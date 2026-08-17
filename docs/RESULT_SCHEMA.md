# RESULT_SCHEMA

## Status semantics (Final Hotfix §18)

| Status | Meaning |
|---|---|
| WARNING | result remains valid; informational concern |
| REVIEW_REQUIRED | result exists but requires engineering review before approval |
| ERROR | the affected engineering calculation is invalid; no number is issued for it |
| BLOCKED | downstream output (movement pair / matrix cell) cannot be completed because an ERROR or unresolved mandatory dependency exists |

A blank output cell never ambiguously means both "no intergreen required" and
"calculation failed" — matrix cells carry an explicit status
(VALID / NOT_APPLICABLE / BLOCKED / REVIEW_REQUIRED) plus findings.

## Serialization invariant (Final Hotfix §11)

No serialized engineering output may contain NaN, Infinity, negative invalid time, or an
unresolved required value. Reaching serialization with such a value is an internal defect:
export fails hard with INTERNAL_NUMERIC_INVARIANT_FAILURE. (Enforced at gate S.)

*(Full analysis.json schema lands with gate F/S of §59 — writer not yet built.)*
