# EXPORT_TEST_MATRIX (ExportPlannerTests, 6 — all PASS)
| Case | Behavior |
|---|---|
| normal destination | expected *_MAHOD_INTERGREEN.xlsx name |
| destination folder missing | created |
| Hebrew/space destination | Ok |
| destination exists / export twice | NeedsConfirmOverwrite + unique _2 fallback |
| source == destination | REFUSED (source protected) |
| real export (golden pipeline) | source SHA-256 unchanged; output opens; MAHOD sheets present |
Cancel-save and export-after-Setup-change: the state machine invalidates results until
re-Analyze. Over-4-point rows: unchanged 0.1.0 writer behavior (Excel suite 14/14).
Read-only/network destination: DestinationError path implemented; live share unavailable
in env (disclosed).
