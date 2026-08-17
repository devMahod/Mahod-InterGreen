# INTEGRATION_VADIM — Civil 3D / MahodAI host contract

**Rule zero (Directive §2/§48): the deterministic engine is the product. No second Civil
calculation engine is ever created; MahodAI is an orchestration/explanation layer and never
computes CD/ED/intergreen values itself.**

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

## What Vadim must NOT reimplement

Geometry rules, clearing/entering formulas, rounding/minimum policies, signal-group
aggregation and BLOCKED propagation, 2025 lane-centreline logic — all regression-locked.
