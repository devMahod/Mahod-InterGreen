# CAD_SMOKE_TEST — Arthur GUI smoke (self-contained)

**STATUS: AWAITING_ARTHUR_MANUAL_SMOKE** — this normal-GUI test needs a human AutoCAD
session. Claude Code ran the headless accoreconsole path (NETLOAD + IG_SCAN +
IG_EXPORT_GEOMETRY on both real DWGs) but did NOT and must NOT mark this test PASS.

Record every step's PASS/FAIL in `ARTHUR_GUI_SMOKE_RECORD_HE.md` and capture the real
screenshots listed in `SCREENSHOT_CAPTURE_CHECKLIST_HE.md`.

All paths below are inside the pilot package. Work only on **writable copies** from
`08_PILOT_TEST_KIT` — never on the original source workbook (the export never overwrites
the source; verify that explicitly in step 15).

## Example 1

1. Install the plugin from the packaged path:
   `07_AUTOCAD_BUNDLE` → `powershell -ExecutionPolicy Bypass -File .\INSTALL_MAHOD_INTERGREEN.ps1`
2. Copy `08_PILOT_TEST_KIT\EXAMPLE_1\INPUTS\` to a writable work folder
   (e.g. `08_PILOT_TEST_KIT\OUTPUT\EXAMPLE_1\`); open the copied
   `PINNES-BAAL SHEM-TOV-04_intergreens_2026-07-28-maya.dwg` in AutoCAD 2026.
3. Run `INTERGREEN` — the "MAHOD INTERGREEN" palette opens.
4. **Setup** — select the copied Example-1 source workbook
   (`05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx`).
5. Confirm units (if INSUNITS is Unitless — one-time "drawing is in meters" confirmation).
6. Complete any required project mapping/configuration the palette requests.
   Expect: "Setup saved" → `<drawing>.intergreen-project.json` next to the DWG.
7. **Validate** — expect 11 movements (4 crossings), 0 errors.
8. **Analyze** — expect 50 conflicts; `<drawing>.analysis.json` written next to the DWG.
9. Verify the matrix: **24 VALID / 0 REVIEW_REQUIRED / 0 BLOCKED**.
10. Select one ordinary conflict (e.g. `E-R→a`) → **Show in drawing** — expect zoom +
    amber candidate circles, red governing circle (termination candidate at the boundary
    end), Final IG = 6 in status. `IG_CLEAR_QA` removes highlights.
11. Select **Row 82** (`W-L→S-T`) → inspect/Show. Expect Final IG = 5 (manual was 4);
    governing candidate `W-L.b2@end`, CD ≈ 28.52 m — a conservative engine finding,
    see RESIDUAL_CLASSIFICATION.md.
12. Select **Row 89** (`a→S-T`) → inspect/Show. Expect Final IG = 7 (manual was 6);
    governing candidate on crossing-a edge with ED = 0 at the S-T stop line.
13. **Export Excel** using the copied **ORIGINAL Example 1 source workbook** as source.
14. Verify the generated `*_MAHOD_INTERGREEN.xlsx` opens in Excel with **no repair prompt**.
15. Verify the original source workbook is byte-unchanged (timestamp/size, or compare
    hash against `08_PILOT_TEST_KIT\EXAMPLE_1\INPUTS\SHA256SUMS_INPUTS.txt`).
16. Verify all original sheets exist in the export, in the original order, formulas and
    images intact.
17. Verify sheet `MAHOD Engine Results` (all candidate points + full traceability).
18. Verify sheet `MAHOD Matrix Status` (explicit status per matrix cell).
19. Close the drawing / AutoCAD.
20. Reopen the same DWG.
21. Run `INTERGREEN` → **Validate** directly — the saved sidecar must be reused
    (no Setup repetition). This verifies project persistence.

## Example 2

22. Copy `08_PILOT_TEST_KIT\EXAMPLE_2\INPUTS\` to a writable work folder; open the copied
    `04_Aba-HaM_intergreens_2026-007-07-maya.dwg`. For reproducing the reference result
    exactly, apply the sidecar from `EXAMPLE_2\REFERENCE_CONFIG\` (see its README) —
    a clean first run will instead ask to confirm two endpoint references (IG-GEO-004).
23. **Validate** — expect debris warnings listed, crossing `b` with 6 edges accepted.
24. **Analyze**.
25. Verify the project is flagged **REVISION_MISMATCH** (manual CD 24.07 m exceeds the
    longest current boundary 20.02 m — the DWG is not the workbook's revision).
26. Verify the matrix: **37 VALID / 7 REVIEW_REQUIRED / 0 BLOCKED**.
27. Confirm crossing `b` is **not** generically blocked (it computes with project W = 16.00).
28. Export Excel for Example 2 as well; same preservation checks as steps 14–18.

## End

29. Exercise the uninstall/cleanup procedure:
    `07_AUTOCAD_BUNDLE` → `powershell -ExecutionPolicy Bypass -File .\UNINSTALL_MAHOD_INTERGREEN.ps1`
30. Record PASS/FAIL for **every** step in `ARTHUR_GUI_SMOKE_RECORD_HE.md`.
31. Capture the required real screenshots per `SCREENSHOT_CAPTURE_CHECKLIST_HE.md`.

For every FAIL record: step number, exact message/error, screenshot, file used, short note.
