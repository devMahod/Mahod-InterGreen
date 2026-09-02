

## r14 (2026-09-02) — David's six items, delivered as a tool; Codex review absorbed

- Source commit cbd8f94 (pilot-hardening, local only). Release id 0.1.0-r14. Installer `installer/out/Mahod_Intergreen_Setup_0.1.0-r14.exe` sha256 00c4f0a59de97240df482ab69fda4b70c173b4cddfd02bc71566821fc5100f01.
- Staff ZIP `Downloads/Mahod_Intergreen_0.1.0-r14.zip` (exe + guide, Defender clean) sha256 494db5f2077b445fd2a7efbbeaa3cb835f6b226253f2753bf249c2486f5ffbf9; no-installer ZIP sha256 5a2d8d85b7dfa030b089b128d1003929984ad65aaba56e02db77977bbb154814. NOT SENT — Arthur decides.
- What changed: ED-015 crossing exit (Geometry re-shipped), ED-016 amended (tolerance OFF by default; virtual extension / confirm-beside-end / pending; explicit extend-in-DWG), ED-017 workbook from the client's template with crossing slots from junction topology, ED-018 movement builder (copies, never edits). Palette: "בניית תנועות…", "Excel חדש מהשרטוט…", "סף הארכה אוטומטית…", "הארך בשרטוט…", "הצג קו קצר…".
- Gates: `scripts/release_build.py r14` PASS (42 locked dist files byte-identical; only AutoCAD/Host/Geometry moved), `scripts/realhost_setup_accept.py` PASS x2, `scripts/realhost_{new_project,tolerance_pass,extend_in_dwg,build}.py` PASS x2 each, 450 tests.
- Records: `docs/releases/RELEASE_BUILD_r14.txt`, `docs/releases/PACKAGE_r14.txt`, `docs/r14/CODEX_REVIEW_RESPONSE_2026-09-02.md`, `docs/r14/DAVID_REPORT_r14_HE.md` (the David-facing report, Hebrew).
- Lin's geometry is now a fixture: `tests/fixtures/geometry/lin05293.iggeometry.json`.
