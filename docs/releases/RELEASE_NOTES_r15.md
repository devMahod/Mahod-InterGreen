# Mahod Intergreen 0.1.0-r15 — usage counts for Mahod Impact

Built 2026-10-06 from source `07ed08778cb12b99db7a5482db4c56effc3f4e76` (branch `feat/impact-usage`, on r14 `d321312`).

## The only change

Mahod Intergreen now reports its usage to Mahod AI (`POST https://dev.mahodeng.co.il/api/v1/usage`, the route
Mahod Impact reads), exactly like the Intergreen that ships inside MahodAI (plugin commit `02d1601`):

| What | Record |
|---|---|
| A palette button that ran (through `IgWorkflowCommands.Guard`) | action `intergreen_<button>` (labels mapped as in-tree, else the workflow step: `intergreen_setup`, `_validate`, `_analyze`, `_show`, `_export`, `_clearqa`, `_palette`), door `palette`, `completed` / `failed`, run time |
| Typed `INTERGREEN`, `IG_CLEAR_QA`, `IG_SCAN`, `IG_EXPORT_GEOMETRY` | action `intergreen` / `ig_clear_qa` / `ig_scan` / `ig_export_geometry`, door `command` |
| A junction whose matrix export passed its structural check ("5. Export Excel") | one priced unit, feature `junction_intergreen`, key = drawing `FingerprintGuid` + `"|junction:"` + workbook file name (no extension, lower-case), hashed on the PC (first 128 bits of SHA-256) |

Not counted: a button refused by its workflow gate (it ran nothing), document activation, the `IG_SMOKE_*`
harness commands. Status is only `completed` or `failed` (no `cancelled`). Never sent: the drawing, its name or
path, layers, values, the Windows user, a PC id. The request authenticates with the PC's MahodAI installation
key when MahodAI's updater is installed, else with the products' key baked in at build time. Records queue in
`%LOCALAPPDATA%\Mahod\cad-usage` and upload on a pool thread at most every 15 minutes; `MAHOD_USAGE_OFF=1` turns it
off. The recorder (`src/Mahod.Intergreen.AutoCAD2026/Usage/MahodUsage.cs`) is a byte-for-byte copy of the MahodAI
plugin's `Utilities/MahodUsage.cs` (git blob `85a350d`, plugin `a259bed`), pinned by a test. Engineering, palette,
files and drawing behaviour are those of r14: the five locked engineering assemblies ship as the accepted r11
bytes, and Geometry / Host are rebuilt from their unchanged r14 source (only their version stamp moves).

Known limitation: the record's version field `v` reads `1.0.0` — the recorder reports the assembly version, and
Intergreen pins `AssemblyVersion` at 1.0.0.0 by decision (build/MahodRelease.props). The product revision is in
FileVersion `0.1.0.15` / ProductVersion `0.1.0-r15`.

## Public repo: the key never enters git

The key is baked only from `MAHOD_CAD_KEY` in the build process's environment (`build/MahodUsageKey.targets`,
imported by the host project only). From r15 `scripts/release_build.py` stages the shipped bundle in the ignored
`build/out/r15/` and leaves the tracked `dist/` as the keyless r14 bundle; it checks (booleans only) that the key is
in both host DLLs, in no other staged file and in no tracked file. Never commit `build/out/`, `installer/out/` or
`installer/MahodIntergreenSetup/payload/`.

## Artifacts (not in git)

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `Mahod_Intergreen_Setup_0.1.0-r15.exe` | 78485551 | `55a3f6e433a2a8f0ff4db824ca40a7a20da3c41cf71d05284324d809a149c7dc` |
| `guide_staff_r15.pdf` (in the ZIP as `Mahod_Intergreen_מדריך_מהיר.pdf`, 5 pages) | 556342 | `1d6cf3c6ce6fac5432a5a012f769af51ffa85bf0354ffd7900fccc1e0054e8bc` |
| `Mahod_Intergreen_0.1.0-r15.zip` (the two files above; Defender clean) | 73214668 | `0173da21a843b2405d9b73090f9036b68915d471fb0a50fe31d13de0dab85358` |

Hosts: AutoCAD / Civil 3D 2026 (.NET 8, R25.1) and 2027 (.NET 10, R26.0) — the same two as r14. The installer is the
same per-user setup as r14 (HKCU `Uninstall\MahodIntergreen`, `%APPDATA%\Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle`,
PackageContents ProductCode `{7A1C4E2B-9D3F-4B4E-A2F3-6C1D2E3F4A5C}`), so it replaces r14 in place. Close AutoCAD first.

## Evidence

- `RELEASE_BUILD_r15.txt`: all gates PASS — 42 locked dist files byte-identical, both hosts built and stamped
  `0.1.0.15|0.1.0-r15 … git 07ed087`, usage key baked in both host DLLs and nowhere else, payload == stage (49 entries).
- `PACKAGE_r15.txt`: staff ZIP, two files, Defender clean.
- Tests (reconstructed client materials for the Excel/Host fixtures): 472 / 473 pass — the new
  `Mahod.Intergreen.Usage.Tests` 23/23 (contract, unit key, button names, hook points, recorder identical to the
  plugin's, loopback end-to-end POST). The one failure is the pre-existing `Garbage_workbook_is_rejected_…`, whose
  client file `01 Guidelines\Relevant Pages.xlsx` is not on the build PC (fails identically on r14).
- The shipped 2026 and 2027 host DLLs were driven outside AutoCAD by a loopback harness: baked key present and
  selected (`X-Mahod-Product-Key`) on a PC without MahodAI; the POST carries only `tool=intergreen` events, the
  `junction_intergreen` unit, no identity field or value; the queue empties on 200.
- NOT done: installing the EXE, running inside AutoCAD / Civil 3D, the real-host smokes (`scripts/realhost_*.py`),
  an upload to the real Mahod AI route.
