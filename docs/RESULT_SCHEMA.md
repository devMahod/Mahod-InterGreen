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

## Actual shipped result contract (`analysis.json`, written by `AnalysisWriters`)

Deterministic engineering payload — environment data (machine, timestamps, input paths)
lives only in the sibling `run_manifest.json`.

| Field | Content |
|---|---|
| `SchemaVersion`, `EngineVersion` | contract + engine identifiers |
| `IntersectionName`, `SourceGeometry` | project identity + geometry provenance (file, SHA-256) |
| `RulePack` | pack id, version, SHA-256 (legacy-mahod-v1 / israel-2025-06) |
| `Classification` | resolved classification axes with source references |
| `Movements` | movements with mode, signal group, curves, stop-line references |
| `Conflicts[]` | per pair: `Id`, `Clearing`, `Entering`, `Status`, `Points[]`, `DefiningPointId`, `RawIntergreenSec`, `FinalIg`, `CalculationTrace`, `FindingCodes` |
| `Conflicts[].Points[]` | `Id`, `Cd`, `Ed`, `X`, `Y`, `ClearingCurveId`, `EnteringCurveId`, `RawIg` — every candidate point (never truncated to 4) |
| `Matrix` | signal-group matrix; every cell carries an explicit status + findings |

Companion outputs: `validation.json` (findings before analysis), `run_manifest.json`
(environment + input hashes), Excel export (`MAHOD Engine Results` + `MAHOD Matrix Status`
sheets — written by the shared `WorkbookWriter`, source workbook never modified).
