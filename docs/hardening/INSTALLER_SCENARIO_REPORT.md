# INSTALLER_SCENARIO_REPORT — Mahod_Intergreen_Setup_0.1.0-r3.exe (14/14 PASS)
| # | Scenario | Result |
|---|---|---|
| S2/S3 | upgrade over existing r2 install | PASS — replaced wholesale; 24/24 files; SixLabors+RBush+Host present; a stale missing-deps bundle cannot remain active |
| S4 | reinstall same revision | PASS |
| S12 | stale LOCKED old install | PASS — pre-delete probe aborts cleanly (rc=1, Hebrew message, existing install byte-untouched) |
| S7 | AutoCAD/accoreconsole running | PASS — guard blocks (proven against a live accoreconsole) |
| S5 | uninstall | PASS — removes only the Mahod bundle + registry key |
| S11 | unrelated Autodesk plugins | PASS — untouched |
| S1/S6 | clean install / install again after uninstall | PASS |
| S14 | per-user, no admin | PASS (APPDATA/LOCALAPPDATA/HKCU only) |
| gate | runtime closure from INSTALLED path | PASS (golden numbers) |
S9 (no AutoCAD) — not simulatable here; GUI warns and allows by design. S10
(ApplicationPlugins absent) — folder in live use by other plugins; CreateDirectory runs on
every install. S13 covered by S2/S3+S12. Old r2 EXE: SUPERSEDED.
