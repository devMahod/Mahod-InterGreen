# CAD_SMOKE_TEST — interactive 5-minute procedure (Directive §56)

**STATUS: AWAITING_ARTHUR_MANUAL_SMOKE** — this normal-GUI test needs a human AutoCAD session.
Claude Code ran the headless accoreconsole path (NETLOAD + IG_SCAN + IG_EXPORT_GEOMETRY on both
real DWGs) but did NOT fabricate a GUI PASS.

## Procedure

1. `powershell -ExecutionPolicy Bypass -File dist\INSTALL_MAHOD_INTERGREEN.ps1`
2. Launch AutoCAD 2026 normally; open `test-results\dwg-work\ex1.dwg`.
3. Command: `INTERGREEN` — the "MAHOD INTERGREEN" palette opens.
4. **Setup** — paste the Example-1 workbook path
   (`..\materials\Inter-green Automation\03 Example 1\05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx`),
   confirm units = Meters when prompted. Expect: "Setup saved -> ex1.intergreen-project.json".
5. **Validate** — expect: 11 movements (4 crossings), 0 errors.
6. **Analyze** — expect: 50 conflicts, matrix 24 VALID / 0 BLOCKED; `ex1.analysis.json`
   written next to the DWG.
7. Select conflict `E-R→a` → **Show in drawing** — expect zoom + amber candidate circles,
   red governing circle at the boundary end (termination candidate), Final IG = 6 in status.
8. **Export Excel** — expect `05_PINES-..._MAHOD_INTERGREEN.xlsx` with 40 rows populated,
   sheets `MAHOD Engine Results` + `MAHOD Matrix Status`, original sheets intact; open in
   Excel — no repair prompt.
9. Close + reopen the drawing, `INTERGREEN` → **Validate** directly: the saved sidecar must be
   reused (no Setup repetition).
10. Open `ex2.dwg`, repeat 3–6 with the Example-2 workbook. Expect: crossing `b` computed
    (6 edges), debris warnings listed, matrix 38 VALID / 6 REVIEW / 0 BLOCKED.

Record command-line/palette screenshots as evidence. `IG_CLEAR_QA` removes highlights.
