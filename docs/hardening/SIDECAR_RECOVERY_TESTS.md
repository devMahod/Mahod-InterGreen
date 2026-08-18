# SIDECAR_RECOVERY_TESTS (SidecarStoreTests, 10 — all PASS)
| Case | Behavior proven |
|---|---|
| first run, no sidecar | clean NoSidecar |
| commit-load roundtrip | schemaVersion=1 stamped |
| corrupt JSON | Recovered + corrupt-backup preserved + Hebrew repair message, no crash |
| legacy quoted stored path | normalized through the central resolver |
| relative stored path | resolved against the drawing folder |
| workbook missing | Missing + message — never silently replaced |
| moved workbook, same name next to DWG | CANDIDATE offered; Setup confirmation required |
| failed new Setup after valid old config | old sidecar byte-identical (transactional) |
| unversioned old sidecar | version stamped on next commit; no invented values |
| atomic commit | .bak of previous version kept |
Reopen-and-continue (sidecar adopted when the palette opens again) is covered by the
journey reopen simulation.
