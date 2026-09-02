# Mahod Intergreen — Codex onboarding

You are joining an existing, shipped product with a demanding release discipline. Your first job is
to understand it exactly as it is, not to improve it. Read this file fully, then read the documents
it points to, then write me a one-page summary of what you found **before proposing any change**.

Working language: code, commits and technical docs are English; anything a user sees (palette,
messages, PDFs, emails) is Hebrew, right-to-left, professional register. The users are traffic
engineers, not developers.

---

## 1. What this is

**Mahod Intergreen** is a Civil 3D / AutoCAD 2026 + 2027 plugin that computes intergreen times
(זמני בין־ירוק) for signalised intersections. The engineer draws each traffic movement as a pair of
boundary polylines on a layer named `intergreen_<movement>` (vehicles `N-T`, `S-L`, `E-R`… with
optional `-bus`/`-sherut`; pedestrian crossings a single lowercase letter), stop lines on
`intergreen_stopline`, and supplies the project's Excel workbook (the legacy `IG_matrix` template:
`Parameters`, `Pedestrian Xing`, `Signal group key`, `Input Distances`, `AutoAdjusted Distances`,
`Matrix` pivot). The plugin:

1. extracts the geometry from the open drawing;
2. finds every conflict between movement pairs, computes candidate points, clearing distance (CD)
   and entering distance (ED) from each movement's stop line, and the governing point;
3. runs the legacy Mahod intergreen calculation (a versioned **rule pack**);
4. writes a **new** workbook next to the source (`*_MAHOD_INTERGREEN.xlsx`) with the engine's
   values in `Input Distances`, plus two appended sheets (`MAHOD Engine Results`,
   `MAHOD Matrix Status`), never touching the source file or the drawing.

The command is `INTERGREEN` (a WPF palette): שרטוט DWG → Setup (pick Excel, confirm metres) →
Validate → Analyze → select a conflict → Show in drawing → Export Excel, plus support buttons
(נקודות ייחוס…, בחירת חוקים…, Clear QA, Export Support Log).

## 2. Where everything is

| what | path |
|---|---|
| repository (branch `pilot-hardening`) | `C:\Users\arthurf\Downloads\InterGreens\Mahod.Intergreen` |
| client materials — spec, templates, the two reference projects | `C:\Users\arthurf\Downloads\InterGreens\materials\Inter-green Automation` |
| release staging + manifest of every package ever built | `C:\Users\arthurf\Downloads\Mahod_Intergreen_DISTRIBUTION_2026-08-17` |
| the original build plan (web-Claude, 148/148 verified legacy engine spec) | `C:\Users\arthurf\Downloads\InterGreens\mahod-intergreen-build-plan-v1.md` |

Inside the repo, read in this order:

1. `docs/START_HERE_HE.md`, `docs/WHAT_WE_BUILT_HE.md` — orientation.
2. `docs/ENGINEERING_DECISIONS.md` — ED-001…ED-014. **Every numbered decision is binding.**
3. `docs/OPEN_QUESTIONS.md`, `docs/KNOWN_GAPS.md`, `docs/FINDINGS.md`.
4. `docs/r12/LIN_CONFLICT_DIFF.md` and `docs/r12/LIN_R12_DEFECTS.md` — the two most recent
   real-world defects, their root causes, and how each fix was proven. These show the standard of
   evidence expected here.
5. `docs/shipping/ANTIVIRUS_FALSE_POSITIVE.md` — how we deliver Windows binaries and why.
6. `docs/CIVIL_INTEGRATION_BOUNDARY.md`, `docs/INSTALLER_BUILD_AND_FILE_VALIDATION.md`.
7. `TASK_LEDGER.md`, `FINAL_PILOT_VERIFICATION_REPORT.md`.

In `materials/…/05 Current Code/הגדרת פעולה.docx` is the client's own original specification of the
interactive tool, including **Appendix A**: the exact deterministic drawing method for movement
envelopes (curb line, tan-tan-tan circle, 4.5 m offset, fillet). `סכמה.pptx` is its flowchart.

## 3. Architecture and what may be touched

```
src/
  Mahod.Intergreen.Contracts     ┐
  Mahod.Intergreen.Core          │  ENGINEERING assemblies. Approved bytes. Byte-identity gated at
  Mahod.Intergreen.Geometry      │  release. Changing one is an engineering decision that needs
  Mahod.Intergreen.Reporting     │  proof against human-computed values (see §6) and Arthur's
  Mahod.Intergreen.Rules         │  explicit approval. Geometry was re-shipped once, in r13, with
  Mahod.Intergreen.Excel         ┘  exactly that proof.
  Mahod.Intergreen.Host          — user-journey layer: paths, sidecar, workflow gating, dialogs
                                   abstraction, support log. NO Autodesk dependency, NO engineering
                                   logic. Fully unit-testable. Safe to change.
  Mahod.Intergreen.AutoCAD2026   — the plugin: WPF palette, AutoCAD commands, geometry extraction.
                                   Builds twice: AutoCADVersion=2026 (net8) and 2027 (net10).
  Mahod.Intergreen.Cli           — `ig-analyze`: runs the exact engine headlessly over an
                                   `*.iggeometry.json` + workbook. Same DLLs as the plugin.
rules/legacy-mahod-v1/           — the rule pack (manifest, parameters, source references). sha-locked.
dist/Mahod.Intergreen.bundle/    — the exact bytes that ship; PackageContents.xml stamped with git sha.
installer/MahodIntergreenSetup/  — self-contained single-file WinForms installer; copies the bundle to
                                   %APPDATA%\Autodesk\ApplicationPlugins, writes an HKCU uninstall key.
tests/                           — 343 tests: Rules 24, Geometry 49, Core 93, Regression 42, Excel 21,
                                   Host 114. tests/fixtures/geometry/example{1,2}.iggeometry.json are
                                   the real drawings' extracted geometry, byte-identical to the
                                   accepted release evidence.
scripts/diagnose_drawing.py      — one command: DWG + XLSX → layer inventory, near-miss stop-line
                                   boundaries, matrix before/after confirming them. No Civil session.
```

Release identity lives in `build/MahodRelease.props` (`0.1.0-r13`, revision 13) and is stamped into
host DLLs and the installer with `-p:MahodGitSha=$(git rev-parse HEAD)`. A build without it is
`UNSTAMPED-DEV` and must never ship. Engineering assemblies deliberately do **not** import that file.

## 4. Release history — what each round fixed and why

| rev | source | what changed | trigger |
|---|---|---|---|
| r9 | `4bc1d3a` | real host identity, drawing-bound palette; full GUI acceptance on this machine | baseline |
| r10 | `c403af9` | stale `calcChain` dropped (export opened as `[Repaired]`); DWG file picker | Lin |
| r11 | `df96c36` | **ED-014**: legacy Matrix pivot never ships stale (refreshOnLoad, purged cache, cleared cells; real Excel open == Refresh == Refresh All == reopen); rows with no engine result are cleared and flagged, never mixed with manual values | Lin |
| r12 | `7cd934d` | Validate names boundaries that stop short of their stop line (gap in cm, DWG handle); new palette button **נקודות ייחוס…** confirms them into the project sidecar. The engine always honoured `confirmedEndpointReferences`; the UI never exposed it — Example 2's golden only ever passed because a gate sidecar had been hand-edited | Lin: "conflicts not detected" |
| **r13** | **`5e84331`** | **Directive §21A fix** in `Geometry/ConflictStrategies.cs`: a boundary-termination candidate must actually lie in the crossing it claims to stop in. The old rule took the end at `TotalLength` as the far end, true only for polylines drawn from the stop line outwards; reversed polylines put a phantom point on the stop line at distance 0, which maximises the intergreen and therefore always governed | Lin: b→S-L came out 6, her manual 3 |

Current: **0.1.0-r13**, source `5e84331`, artifacts `a2c405f`, latest commit `dec1776`. Lin ran r13
on both reference projects and one of her own: correct. David has not yet received r13.

## 5. The goldens — these numbers do not move without proof

| | Example 1 (PINES–BAAL SHEM TOV) | Example 2 (Abarbanel–HaMaccabim) |
|---|---|---|
| movements | 11 (7 vehicle, 4 crossings) | 16 |
| conflicts | **50**, all VALID | **168** (157 VALID / 11 REVIEW) |
| matrix | **24 VALID** | **37 VALID / 7 REVIEW / 0 BLOCKED** |
| W-L→S-T | **5** | 5 |
| sidecar needed | none | `confirmedEndpointReferences: ["E-L.b1","S-L.b2"]` |
| status | clean | **REVISION_MISMATCH** — its workbook is documented as out of sync with its own drawing; a weak oracle for individual cells |

Locked by `tests/Mahod.Intergreen.Regression.Tests/{EndpointReferenceRegressionTests,TerminationCandidateTests}.cs`.

## 6. How things are proven here

You will be held to the same standard the last two rounds were.

- **Against humans, not against ourselves.** The decisive test for any engine change is
  `scratchpad/prove_fix.py`-style: compare every conflict's FINAL IG with the value the engineer
  wrote by hand in that project's own workbook (`Input Distances` column AK). r13 moved agreement
  from 150 to 154 of 174 rows across three real projects; two earlier candidate fixes were measured
  at 148 and 153 and discarded. A change that moves the engine away from the human values needs an
  engineering argument, not a code argument.
- **Headless, never the GUI.** Civil 3D on this machine belongs to Arthur; only he launches it and
  it is often busy. Every acceptance run uses `accoreconsole.exe` (AutoCAD 2026 and 2027 both
  installed): a `.scr` with `SECURELOAD 0` / `NETLOAD <bundle>\Contents\<year>\Mahod.Intergreen.AutoCAD.dll`
  / a command / `QUIT Y`, `IG_OUTPUT_DIR` set. `IG_SMOKE_REFERENCE_REVIEW` drives Setup → Validate →
  confirm → Analyze and writes JSON. Run it on the **shipping dist bytes**, on both host flavours.
- **Palette dialogs without Autodesk.** `scratchpad/dlgcheck` loads the shipping AutoCAD DLL by
  reflection, instantiates the private dialog, renders it with `RenderTargetBitmap`, ticks boxes and
  asserts the return value. The dialog code references no Autodesk types, so this works.
- **Identity gates.** `release_build_r13.py` (scratchpad) checks the five untouched engineering DLLs
  byte-for-byte against the accepted dist, stamps versions, builds the payload, publishes the
  installer, and verifies the embedded payload equals dist. Rebuilding an engineering DLL produces
  different bytes purely from the SDK SourceLink stamp (`1.0.0+<sha>`) — that is why accepted bytes
  are kept and not re-shipped.
- **Every number in a report has a source you can re-run.** Hashes, counts, before/after tables.

Two traceability quirks to know: `SourceGeometry.Sha256` in every `*.analysis.json` is the hash of
the **geometry JSON**, not the DWG, while `SourceGeometry.FileName` is the DWG name; and the
`0.0000001` values engineers see in `AutoAdjusted Distances` come from the template's own
`IF(x=0,0.0000001,x)` formulas (330 of them), not from us.

## 7. Delivery

Windows Defender's cloud ML flags fresh, unsigned, single-file installers as
`Trojan:Win32/Wacatac.B!ml` (it did, to a sibling Mahod tool, on a client machine). Our installer is
unsigned and has that shape. Read `docs/shipping/ANTIVIRUS_FALSE_POSITIVE.md`. Two package formats
exist for r13 in `C:\Users\arthurf\Downloads`:

- `Mahod_Intergreen_0.1.0-r13.zip` — bundle folder + guide + Hebrew read-me, **no executable**;
  gated fail-closed (no exe/script, every file hash-checked against dist **and** against the payload
  inside the installer Lin validated, Defender scan survived).
- `Mahod_Intergreen_0.1.0-r13_STAFF.zip` — exactly two files, the installer + guide, for internal staff.

Never tell a recipient to disable Defender, restore from quarantine, add an exclusion or click
"Run anyway". Scan any artefact with `MpCmdRun -Scan -ScanType 3 -File <path>` before it leaves this
machine and confirm the file survived.

## 8. What is open

**Engineering questions for the client (David Suchinsky, traffic signals dept):**
- Example 2 `b→W-R`: his engineer wrote 14, r13 computes 13. David answered on 2026-08-27:
  measure to where the boundary **exits** the crossing — a third reading that matches neither today's
  rule nor his engineer's 14, and his own Example 1 team measured `E-R→a` to the drawn end. Analysis
  and the question to send back: `docs/r14/DAVID_FEEDBACK_2026-08-27.md`. **Not implemented.**
- ~~Appendix B (the layer colour model)~~ — **recovered 2026-08-27** from his own drawings and template: `docs/COLOUR_MODEL.md`. Only diagonal approaches remain unspecified.
- Which layers in the base/xref drawings carry curb lines and lane edges — David (2026-08-27): they
  are drawn by the traffic planner and are **not consistent across projects**; he proposes the user
  selects them once per project. Design accordingly (persist in the project sidecar).
- The 2025 guideline method (conflict points on lane **centrelines**, not envelopes) is stubbed as
  a second strategy and has no validated example.

**Product work David asked for (P1/P2), blocked on the answers above:**
- **P1 — create the project workbook from the drawing.** Their blank template already holds the
  full 108-row conflict grid with all formulas ("YOU CAN DELETE UNUSED"); the work is: start from the
  template, keep only rows whose movement pairs exist in the drawing, fill CD/ED — i.e. the export we
  already do, with a different starting file. Engineering inputs not in the drawing (speeds, vehicle
  length, signal-group numbers, crossing lengths) still come from the engineer, via a short form.
- **P2 — assisted movement builder.** The client's own spec: approach → movement → layer created
  with the right name/colour → pick two boundary polylines → mark each start vertex → repeat, with
  undo at every step. Appendix A is the deterministic construction if we also generate geometry.
  Do not build an opaque "AI guesses the movements" feature.

**P3 — remote update.** `latest.json` over HTTPS → compare → notify → download → verify SHA-256 →
run the normal installer. The plugin currently has **zero** network code. Blocked on Arthur choosing
the URL; leaving it empty would make already-installed builds unable to ever check.

**Infrastructure:** a code-signing certificate (ends the Defender problem; purchase decision).
Report the Parking false positive to Microsoft.

**Small known items:** `ReferenceReview.Line` uses `F1` banker's rounding (6.25 cm prints 6.2);
the reference dialog's list box is fixed-height; the dialog does not sort its own input (its caller
does); the rule-pack manifest `notes` still contains the word "pilot" (never displayed).

**Assets we want:** Lin's third project (the one she validated r13 on) as a third geometry fixture.

## 9. Rules of engagement

1. Read before you write. Your first output is the summary described at the top of this file.
2. One bounded task at a time, each with its own verification and evidence. No sweeping refactors.
3. Do not open, launch, close or automate any Autodesk GUI. Headless only.
4. Do not rebuild or re-ship an engineering assembly, change a rule pack, or move a golden number
   without Arthur's explicit approval **and** the human-value proof of §6.
5. Do not commit, push, tag or deploy unless asked. When asked to commit, the commit message
   explains why, in the register of the existing history (`git log`).
6. Never print secrets, tokens or credentials. There are none in this repo; keep it that way.
7. Report what was done and what was verified, with the command output. Do not describe work as
   complete that you have not verified. If something cannot be verified from here, say so plainly.
8. Client-facing text: Hebrew, RTL, concise, no internal jargon, no version-control or hash talk,
   never the word "Pilot".

When you have finished reading, tell me: what the product does, in your words; the three most
important engineering decisions and why they exist; the current release and what is proven about
it; the open items in priority order; and anything in the code or docs that contradicts this file.


## r14 (2026-09-02) — David's six items, delivered as a tool; Codex review absorbed

- Source commit 3600f49 (pilot-hardening, local only; supersedes the cbd8f94 build after the UI polish found by rendering the shipped DLL). Release id 0.1.0-r14. Installer `installer/out/Mahod_Intergreen_Setup_0.1.0-r14.exe` sha256 2ad861bb80d8c4c70c79454244f77c3d879664d2eadc0e98190ccd3b7acd3622.
- Staff ZIP `Downloads/Mahod_Intergreen_0.1.0-r14.zip` (exe + illustrated guide, Defender clean) sha256 11c587b90b3667ed047578cc95f31cfd8a171bd5ed7648d3432e4df910fbe54c; no-installer ZIP sha256 d0e04e127da8e2ab501c18b0b5df53e54ae5aadea47712a977579418b7e2f815. NOT SENT — Arthur decides. Guide: `docs/guides/STAFF_GUIDE_HE_r14.html` + `img/` (screenshots rendered from the plugin by scratchpad/dlgcheck --r14), e-mail text `docs/r14/EMAIL_TO_DAVID_HE.md`.
- What changed: ED-015 crossing exit (Geometry re-shipped), ED-016 amended (tolerance OFF by default; virtual extension / confirm-beside-end / pending; explicit extend-in-DWG), ED-017 workbook from the client's template with crossing slots from junction topology, ED-018 movement builder (copies, never edits). Palette: "בניית תנועות…", "Excel חדש מהשרטוט…", "סף הארכה אוטומטית…", "הארך בשרטוט…", "הצג קו קצר…".
- Gates: `scripts/release_build.py r14` PASS (42 locked dist files byte-identical; only AutoCAD/Host/Geometry moved), `scripts/realhost_setup_accept.py` PASS x2, `scripts/realhost_{new_project,tolerance_pass,extend_in_dwg,build}.py` PASS x2 each, 450 tests.
- Records: `docs/releases/RELEASE_BUILD_r14.txt`, `docs/releases/PACKAGE_r14.txt`, `docs/r14/CODEX_REVIEW_RESPONSE_2026-09-02.md`, `docs/r14/DAVID_REPORT_r14_HE.md` (the David-facing report, Hebrew).
- Lin's geometry is now a fixture: `tests/fixtures/geometry/lin05293.iggeometry.json`.
