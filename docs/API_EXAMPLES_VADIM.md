# API_EXAMPLES_VADIM — call snippets for the future Civil 3D / MahodAI host

Documentation only — no Civil implementation exists in the David pilot.
These snippets show the **shared** engine API as actually shipped (see
`INTEGRATION_VADIM.md` for responsibilities and `RESULT_SCHEMA.md` for the output
contract). Signatures are illustrative of the shipped 0.1.0 assemblies; the compiler in
`01_SOURCE` is the ground truth.

## 1. Build project input (host-extracted geometry → shared model)

```csharp
// Host extracts curves per intergreen layer into the exact geometry model.
// Civil 3D: from corridor feature lines / polylines inside a locked read transaction.
var curvesByLayer = new Dictionary<string, List<PolyCurve2D>>();
curvesByLayer["intergreen_E-L"] = extractedCurves;      // lines/arcs, no approximation
curvesByLayer["intergreen_a"]   = crossingEdges;        // pedestrian crossing edges
curvesByLayer["intergreen_stopline"] = stopLines;

// Workbook-side inputs (signal groups, pedestrian widths) via the shared reader:
var workbook = WorkbookReader.Read(sourceWorkbookPath);

// Project sidecar (units confirmation, modes, endpoint confirmations, overrides):
var sidecar = ProjectSidecar.LoadOrNew(drawingPath + ".intergreen-project.json");
```

## 2. Choose rule pack

```csharp
var pack = RulePackLoader.Load("rules/legacy-mahod-v1");   // or "rules/israel-2025-06"
RulePackValidator.Validate(pack);                          // fails hard on tampering
```

## 3. Validate

```csharp
var findings = new List<Finding>();
var movements = ProjectAssembly.BuildMovements(
    curvesByLayer, workbook.SignalGroups, workbook.PedestrianWidths, sidecar, findings);
// findings now contains IG-VAL-*/IG-GEO-* items; ERROR-level findings block
// only what depends on them.
```

## 4. Analyze

```csharp
var input = new PipelineInput
{
    Movements = movements,
    RulePack = pack,
    Sidecar = sidecar,
    Workbook = workbook,
};
PipelineOutput output = AnalysisPipeline.Run(input);
// output.Analysis  — deterministic engineering payload (analysis.json shape)
// output.Findings  — validation + analysis findings
```

## 5. Read conflicts

```csharp
foreach (var c in output.Analysis.Conflicts)
{
    // c.Clearing, c.Entering, c.Status, c.FinalIg, c.RawIntergreenSec
}
```

## 6. Read governing candidates

```csharp
var conflict = output.Analysis.Conflicts.First(c => c.Id == 82);
var governing = conflict.Points.First(p => p.Id == conflict.DefiningPointId);
// governing.Cd, governing.Ed, governing.X, governing.Y,
// governing.ClearingCurveId, governing.EnteringCurveId, governing.RawIg
```

## 7. Read matrix / statuses

```csharp
foreach (var cell in output.Analysis.Matrix.Cells)
{
    // cell.ClearingSignalGroup, cell.EnteringSignalGroup,
    // cell.Status (VALID / NOT_APPLICABLE / REVIEW_REQUIRED / BLOCKED), cell.FinalIg
}
```

## 8. Show a result from the Civil host (host-side visualization)

```csharp
// Pure host code — the engine only supplies coordinates + curve ids/handles.
foreach (var p in conflict.Points)
    DrawTransientCircle(p.X, p.Y, isGoverning: p.Id == conflict.DefiningPointId);
ZoomTo(conflict.Points);
```

## 9. Export Excel (shared writer only)

```csharp
WorkbookWriter.Export(sourceWorkbookPath, outputPath, output.Analysis, workbook);
// Surgical OpenXML edits: source file untouched; original sheets preserved;
// adds "MAHOD Engine Results" + "MAHOD Matrix Status".
```

## 10. Surface REVIEW / BLOCKED / ERROR correctly

```csharp
// Never collapse statuses. A host UI must render, at minimum:
//  - per-conflict Status and FindingCodes;
//  - per-matrix-cell Status (blank cell is NEVER ambiguous);
//  - REVIEW_REQUIRED as "number exists, engineer sign-off required";
//  - BLOCKED/ERROR as "no number issued", with the findings that caused it.
if (output.Findings.Any(f => f.Severity == Severity.Error))
    ShowBlockingBanner(output.Findings);
```

**Do not create a second implementation of any calculation** — geometry rules,
clearing/entering formulas, rounding/minimum policies, matrix aggregation and BLOCKED
propagation, 2025 centreline logic are all regression-locked in the shared Core.
