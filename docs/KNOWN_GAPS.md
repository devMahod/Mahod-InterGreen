# KNOWN_GAPS

None of these is a hidden blocker; each is visible in the artifacts.

1. **Crossing 'b' in Example 2** — drawn as 6 polylines (three-section crossing over a split
   carriageway) + 1 BlockReference. No deterministic pairing exists → all ×b conflicts ERROR,
   8 matrix cells BLOCKED. Needs guided setup / project-sidecar boundary registration (post-P).
2. **In-CAD one-command INTERGREEN palette not built** — P0 ran via IG_SCAN + IG_EXPORT_GEOMETRY
   in real AutoCAD (accoreconsole) + the CLI `ig-analyze` over the canonical export (the
   Directive §8 reproducibility path). UI/palette is P1 (Directive §13).
3. **Unmatched manual rows pending engineering review** — Ex1: 12, Ex2: 76 (of which 32 are
   crossing-b errors). Deltas per row in EXAMPLE{1,2}_REPORT.md. Dominant causes: crossing-length
   differences (engine measures drawn geometry; sheet records slightly different lengths),
   0.25 m regression tolerance, imprecise drawings (David: "not always perfect"). No case of
   ENGINE_RESULT_LOWER_ERROR with a confirmed point match.
4. **Drawing units** — both DWGs have INSUNITS=Unitless; runs used operator confirmation
   (--units meters, IG-UNIT-001 warning). CD agreement with the workbook confirms the scale.
5. **Matrix status string** — serialized as "REVIEWREQUIRED" (no underscore); cosmetic,
   RESULT_SCHEMA.md documents intended "REVIEW_REQUIRED".
6. **2025 geometry NOT validated on real drawings** — the legacy DWGs contain no lane
   centrelines; 2025_GEOMETRY_MISSING is the correct outcome (Directive §11). Formula engine
   is unit-tested only.
7. **Xrefs** — analysis DWG copies ran with xrefs unresolved/unloaded; intergreen layers are
   host-drawing entities, unaffected. Background xrefs remain untouched (v3 §35).
