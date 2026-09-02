

## r14 (2026-09-02) — David's six items, delivered as a tool; Codex review absorbed

- Source commit 3600f49 (pilot-hardening, local only; supersedes the cbd8f94 build after the UI polish found by rendering the shipped DLL). Release id 0.1.0-r14. Installer `installer/out/Mahod_Intergreen_Setup_0.1.0-r14.exe` sha256 2ad861bb80d8c4c70c79454244f77c3d879664d2eadc0e98190ccd3b7acd3622.
- Staff ZIP `Downloads/Mahod_Intergreen_0.1.0-r14.zip` (exe + illustrated guide, Defender clean) sha256 11c587b90b3667ed047578cc95f31cfd8a171bd5ed7648d3432e4df910fbe54c; no-installer ZIP sha256 d0e04e127da8e2ab501c18b0b5df53e54ae5aadea47712a977579418b7e2f815. NOT SENT — Arthur decides. Guide: `docs/guides/STAFF_GUIDE_HE_r14.html` + `img/` (screenshots rendered from the plugin by scratchpad/dlgcheck --r14), e-mail text `docs/r14/EMAIL_TO_DAVID_HE.md`.
- What changed: ED-015 crossing exit (Geometry re-shipped), ED-016 amended (tolerance OFF by default; virtual extension / confirm-beside-end / pending; explicit extend-in-DWG), ED-017 workbook from the client's template with crossing slots from junction topology, ED-018 movement builder (copies, never edits). Palette: "בניית תנועות…", "Excel חדש מהשרטוט…", "סף הארכה אוטומטית…", "הארך בשרטוט…", "הצג קו קצר…".
- Gates: `scripts/release_build.py r14` PASS (42 locked dist files byte-identical; only AutoCAD/Host/Geometry moved), `scripts/realhost_setup_accept.py` PASS x2, `scripts/realhost_{new_project,tolerance_pass,extend_in_dwg,build}.py` PASS x2 each, 450 tests.
- Records: `docs/releases/RELEASE_BUILD_r14.txt`, `docs/releases/PACKAGE_r14.txt`, `docs/r14/CODEX_REVIEW_RESPONSE_2026-09-02.md`, `docs/r14/DAVID_REPORT_r14_HE.md` (the David-facing report, Hebrew).
- Lin's geometry is now a fixture: `tests/fixtures/geometry/lin05293.iggeometry.json`.
