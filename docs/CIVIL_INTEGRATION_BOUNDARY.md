# CIVIL_INTEGRATION_BOUNDARY — what lives where

> `CIVIL 3D HOST: NOT IMPLEMENTED IN DAVID PILOT`
> `SHARED ENGINE/API: READY FOR FUTURE INTEGRATION`

## Already available in the shared Core (host-independent, regression-locked)

- Exact geometry model: `PolyCurve2D` (lines/arcs, bulge round-trip), stations,
  projections, `EnvelopeRegion`, conflict strategies (Legacy envelopes + 2025 centrelines),
  boundary-termination candidates.
- Both calculation engines: Legacy (148/148 golden) and Israel-2025 (official conformance
  27/27), rounding/minimum policies, `ModePairPolicy`.
- Validation model, findings, statuses (VALID/WARNING/REVIEW_REQUIRED/ERROR/BLOCKED),
  matrix aggregation with BLOCKED propagation.
- Rule packs (versioned, hash-verified), classification/parameter resolution.
- Project sidecar semantics, overrides with SAFETY_REDUCING_OVERRIDE gate.
- Orchestration: `ProjectAssembly`, `AnalysisPipeline`, `AnalysisWriters`
  (analysis/validation/run-manifest), shared Excel `WorkbookReader`/`WorkbookWriter`.

## AutoCAD-host-specific (exists today, not shared)

- `INTERGREEN` command + WPF palette (Setup/Validate/Analyze/Show/Export).
- DWG entity extraction (`IG_SCAN` / `IG_EXPORT_GEOMETRY`), BlockReference recursion,
  layer-convention parsing, DEGENERATE/UNSUPPORTED geometry reporting.
- Transient visualization (candidate circles, zoom, `IG_CLEAR_QA`).
- Autodesk bundle packaging + install/uninstall scripts.

## Future Civil-host responsibility (Vadim — after pilot approval)

- Civil entity extraction (corridors/feature lines/alignments) → `PolyCurve2D`.
- Lane-centreline extraction for the 2025 method (the Civil host's natural advantage).
- Civil document locking/transactions, UI thread discipline.
- Civil-side visualization and conflict navigation.
- Civil GUI smoke test (mirror of CAD_SMOKE_TEST).

## Future MahodAI orchestration responsibility

- Conversation/explanation layer over engine outputs (never computes values).
- Workflow orchestration (which drawing, which rule pack, which outputs).
- Surfacing findings/statuses in natural language with links back to evidence.

## Out of scope for the David pilot

- Any Civil 3D implementation work.
- MahodAI integration coding.
- Assisted Drawing (automatic movement-path drawing) — future feedback only.
- Inbar API integration (no such API exists; manual keying remains).
