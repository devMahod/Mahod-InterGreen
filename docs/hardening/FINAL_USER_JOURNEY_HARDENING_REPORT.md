# FINAL_USER_JOURNEY_HARDENING_REPORT (r5 / installer r3, 2026-08-18)

**Trigger:** Lin's first real GUI run failed in Setup on a quoted pasted path
("Copy as path") passed raw to File.Exists. Full record + root cause:
Engineering-Master `LIN_R4_SETUP_PATH_FAILURE/ROOT_CAUSE.md`.

## What changed (host/UX layer only — engineering Core untouched)
1. **Setup = real Windows file picker** (WPF OpenFileDialog, .xlsx filter; initial folder:
   last valid project folder, then drawing folder, then Documents). No typing/pasting paths.
2. **One central path resolver** — `Mahod.Intergreen.Host.WorkbookPathResolver`: trim, one
   legitimate surrounding quote pair, safe normalization, Unicode/Hebrew preserved,
   extension/existence/read-probe, distinguishes NotFound / FolderNotFound / AccessDenied /
   UnsupportedExtension / PathIsDirectory / EmptyFile / MalformedWorkbook / Locked /
   CloudUnavailable — structured Hebrew guidance, never a stack trace.
3. **Workbook acceptance before commit** — structure validated via the shared reader;
   unsupported template gets an explicit UNSUPPORTED_TEMPLATE_VERSION message.
4. **Transactional Setup + sidecar hardening** — `SidecarStore`: schemaVersion=1, atomic
   commit (tmp + File.Replace + .bak), corrupt-JSON recovery with preserved backup,
   legacy quoted/relative path migration, moved-workbook offered as CANDIDATE (never
   silently used). Cancel/invalid pick leaves the previous valid config byte-identical.
5. **Workflow state machine** — out-of-order actions (Validate/Analyze before Setup, Show
   without selection, Export before Analyze, Setup-after-Analyze staleness) get clear
   Hebrew gate messages; stale results are invalidated, never shown as current.
6. **Error quality** — every material failure states what failed, which file, and what to
   do next; technical detail goes to the log only.
7. **Support logging** — logs under LOCALAPPDATA Mahod/MahodIntergreen/logs, session id,
   state transitions, structured codes + **Export Support Log** palette button (to Desktop).
8. **Export policy** — never source==dest, no silent overwrite (confirm or auto _2 name),
   destination folder auto-created, clear errors.
9. **Installer r3** — locked-old-install probe aborts cleanly with a Hebrew message and
   the existing install untouched (no partial deletes; no rename hacks in product flow).

## Test counts
| Suite | Result |
|---|---|
| 1. Engineering regression (unchanged) | 210/210 |
| 2-6. Host/UI unit (path 17 / workbook 7 / sidecar 10 / export 6 / workflow 6) + journey 5 + misc 5 | 56/56 |
| 7. Installer/package scenarios | 14/14 |
| 8. Runtime dependency closure | PASS (clean shipping copy + installed path) |
| 9. Journey workspace simulations (simple / spaces / Hebrew / quoted / reopen) | 5/5 golden |
| 10. Failure-injection mutations | 5/5 detected |

## Skipped (full disclosure)
| Case | Reason | Residual risk |
|---|---|---|
| Live UNC share / mapped drive | no share in env; syntactic UNC/mapped inputs covered (NotFound path) | low |
| Real OneDrive placeholder | no synced folder; attribute-based detection implemented | low |
| ACL access-denied file | needs admin ACLs; Locked-file covers the probe path | low |
| Windows long path over 260 | machine policy dependent; ~220-char nested path tested | low |
| Installer S9 (no AutoCAD present) | AutoCAD installed here; GUI warns + allows by design | low |
| Installer S10 (ApplicationPlugins folder absent) | folder in live use; CreateDirectory runs every install | very low |

## Versions
- Engineering Core: 0.1.0 (6/6 DLLs byte-identical).
- AutoCAD host: faade08a877bd88a to 56d819d662d8c6b4 (Setup/file-picker + gating +
  logging) + NEW Mahod.Intergreen.Host.dll 0c001582 (host-support layer, no Autodesk deps).
- Installer r3 1974c879 / LIN r5 2d42f145 — r2/r4 SUPERSEDED.
