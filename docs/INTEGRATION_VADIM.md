# INTEGRATION_VADIM — Civil 3D / MahodAI host contract

> `CIVIL 3D HOST: NOT IMPLEMENTED IN DAVID PILOT`
> `SHARED ENGINE/API: READY FOR FUTURE INTEGRATION`

```text
Civil 3D / MahodAI Host
        ↓
Project Assembly / Adapter
        ↓
SAME Mahod.Intergreen Core
        ↓
Validation / Analysis / Matrix / Excel
```

**Rule zero: NO SECOND CIVIL CALCULATION ENGINE.** The deterministic engine is the
product; MahodAI is an orchestration/explanation layer and never computes CD/ED/intergreen
values itself.

Companion documents: `API_EXAMPLES_VADIM.md` (call snippets),
`VADIM_IMPLEMENTATION_CHECKLIST_HE.md` (future implementation sequence),
`CIVIL_INTEGRATION_BOUNDARY.md` (what is shared / host-specific / future / out of scope),
`RESULT_SCHEMA.md` (the actual result contract).

## The shared stack (normal .NET 8 assemblies; Core has no AutoCAD dependency)

| Layer | Assembly | Entry points |
|---|---|---|
| Canonical model | Mahod.Intergreen.Geometry | `PolyCurve2D` (exact line/arc, JSON round-trip), `MovementGeometry`, `EnvelopeRegion` |
| Rules | Mahod.Intergreen.Rules | `RulePackLoader.Load(dir)`, `RulePackValidator.Validate`, versioned packs under `rules/` |
| Engine | Mahod.Intergreen.Core | `LegacyProductionAnalyzer`, `Israel2025ProductionAnalyzer`, `ModePairPolicy`, `SignalGroupMatrixService`, `ProjectOverrideService` |
| Orchestration | Mahod.Intergreen.Reporting | `ProjectAssembly.BuildMovements`, `AnalysisPipeline.Run(PipelineInput)`, `AnalysisWriters.WriteAll` |
| Excel | Mahod.Intergreen.Excel | `WorkbookReader.Read`, `WorkbookWriter.Export` |

## Workflow equivalents (v3 §7 facade)

- **Scan** — the host extracts curves (Civil: from its own entities) into `PolyCurve2D` per layer.
- **Validate** — `ProjectAssembly.BuildMovements(curvesByLayer, signalGroups, pedWidths, sidecar, findings)`.
- **Analyze** — `AnalysisPipeline.Run(input)` → `PipelineOutput { Analysis, Findings }`.
- **GetConflict / GetMatrix** — read `output.Analysis.Conflicts` / `.Matrix` (all candidate points, statuses, governing ids).
- **ExportExcel** — `WorkbookWriter.Export(sourceWb, outPath, output.Analysis, workbookModel)`.
- **ShowConflict** — host-side: highlight `ConflictRecord.Points` (X/Y + curve ids + handles).

## Contracts

- `analysis.json` — deterministic engineering payload (docs/RESULT_SCHEMA.md); environment
  lives only in `run_manifest.json`.
- Project sidecar `<drawing>.intergreen-project.json` (Directive §12): units confirmation,
  movement modes, confirmed endpoint references, pedestrian widths, classification, overrides.
- Statuses: VALID / WARNING / REVIEW_REQUIRED / ERROR / BLOCKED.

## Threading / document locks

The engine is pure (no mutable statics). In CAD hosts, extract geometry inside a locked
transaction, then run the pipeline off-document. Never mutate source entities.

## Host responsibilities checklist (Civil 3D)

- **Geometry extraction**: convert Civil entities (corridor feature lines, polylines,
  alignments as needed) to `PolyCurve2D` exactly — lines/arcs, no polygonal approximation;
  keep entity handles for traceability and Show.
- **2025 lane centrelines**: the Civil host is the natural source of one centreline per
  lane (alignments/offset targets) — feed them as the 2025 geometry input.
- **Document locking / transactions**: extract inside a locked read transaction; run the
  pipeline off-document; never mutate source entities.
- **UI / main thread**: pipeline calls are pure CPU — keep them off the UI thread; only
  Show/visualization touches the document again (own transaction).
- **Sidecar**: reuse `<drawing>.intergreen-project.json` semantics unchanged (units
  confirmation, modes, endpoint confirmations, widths, classification, overrides).
- **Units / classification / stop-lines / pedestrian W**: same explicit-or-blocked policy
  as the AutoCAD host — the engine enforces it; the host only collects confirmations.
- **Rule-pack selection**: pass the pack directory (`legacy-mahod-v1` / `israel-2025-06`);
  never hardcode parameters in the host.
- **Overrides**: only through `ProjectOverrideService` (sidecar-backed, audited,
  SAFETY_REDUCING_OVERRIDE gate). No host-side value substitution.
- **Error/review propagation**: surface VALID / WARNING / REVIEW_REQUIRED / ERROR /
  BLOCKED exactly as returned — never collapse REVIEW/BLOCKED into silence.
- **Excel export**: through the shared `WorkbookWriter` only.

## What Vadim must NOT reimplement

Geometry rules, clearing/entering formulas, rounding/minimum policies, signal-group
aggregation and BLOCKED propagation, 2025 lane-centreline logic — all regression-locked.
