# INSTALLER_BUILD_AND_FILE_VALIDATION — Mahod_Intergreen_Setup_0.1.0.exe

Date: 2026-08-17 · Machine: PC-ARTHURF (the environment-blocked workstation — file-level
validation only; **this is NOT a replacement for the real AutoCAD launch test**, which
moves to Lin per the productization directive §8).

## What the installer is

- Technology: self-contained .NET 8 WinForms setup application (single EXE; no downloads,
  no PowerShell, no ExecutionPolicy, no NETLOAD, no manual copying, no Registry knowledge
  required from the recipient). Source: `installer/MahodIntergreenSetup/` — a packaging
  support project; it never references or rebuilds engineering assemblies.
- Payload: the **validated bundle exactly as shipped in 0.1.1/0.1.0** — embedded as a zip
  created byte-for-byte from `dist/Mahod.Intergreen.bundle` (12 engine/support DLLs +
  rule packs + PackageContents.xml; zero Autodesk proprietary DLLs).
- UX: double-click → Hebrew RTL welcome (product, publisher, engine version 0.1.0) →
  AutoCAD 2026 detection (registry `R25.1` / Program Files) → Install → success screen
  ("פתחו את AutoCAD 2026 והקלידו INTERGREEN").
- Per-user install to `%APPDATA%\Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle` —
  **no admin rights at any point**.
- Uninstall: Windows Settings → Installed Apps → "Mahod Intergreen (AutoCAD 2026)" →
  Uninstall (HKCU uninstall registration + copied uninstaller EXE). Removes ONLY the
  Mahod bundle + its registry key; never touches drawings, sidecars, Excel files, or
  other Autodesk plugins.
- Safety guard: refuses to install/uninstall while `acad.exe`/`accoreconsole.exe` is
  running (a loaded plugin DLL cannot be replaced) with a clear Hebrew message.
- Unsigned (no Mahod code-signing certificate available) — documentation explains the
  possible Windows SmartScreen warning; signed status is NOT claimed.

## Artifact identity

| item | value |
|---|---|
| File | `Mahod_Intergreen_Setup_0.1.0.exe` |
| Size | 74,326,479 bytes |
| SHA-256 | `90bb2b139ea7fcbaf272a04f464a50189eae7715476c6b218d04a1e9e99ae44a` |
| Embedded payload | 21 files, byte-identical to `dist/Mahod.Intergreen.bundle` (= validated 0.1.0 binaries, per BINARY_IDENTITY_0.1.0_vs_0.1.1.md) |

## File-level validation executed on this machine (all silent-mode)

| # | Check | Result |
|---|---|---|
| 0 | AutoCAD-running guard: silent install refused (exit 1, clear error) while a hung `acad.exe` was live | PASS |
| 1 | Silent install exit code 0 | PASS |
| 2 | Installed file set == dist payload (21/21 files, no extras) | PASS |
| 3 | SHA-256 of every installed file == dist (== validated 0.1.0 DLLs) | PASS |
| 4 | `PackageContents.xml` parses as valid XML | PASS |
| 5 | HKCU uninstall key created (`…\Uninstall\MahodIntergreen`) | PASS |
| 6 | Uninstall values (DisplayName / 0.1.0 / Mahod Engineering / UninstallString) | PASS |
| 7 | Uninstaller copy at `%LOCALAPPDATA%\Mahod\MahodIntergreen` | PASS |
| 8 | Silent uninstall exit code 0 | PASS |
| 9 | Bundle directory fully removed | PASS |
| 10 | Registry key removed | PASS |
| 11 | Uninstaller directory self-removed | PASS |
| 12 | Other Autodesk plugins untouched (`Autodesk Save to Web and Mobile.bundle` intact) | PASS |
| 13 | Reinstall OK; all hashes == dist again | PASS |
| 14 | Registry key present after reinstall | PASS |
| 15 | GUI wizard launched visually: RTL layout correct, AutoCAD 2026 detected (green), install/cancel buttons — closed gracefully | PASS |

**Machine end state**: plugin installed (as the Cowork session left it), uninstall entry
registered.

## Known residue on this workstation (not an installer issue)

The crashed-AutoCAD licensing session (see COWORK_AUTODESK_ENVIRONMENT_BLOCKER.md) holds
the *previous* bundle's DLL loaded. That directory was renamed aside as
`%APPDATA%\Autodesk\ApplicationPlugins\Mahod.Intergreen_locked_by_crashed_acad_cleanup_after_reboot`
(a loaded DLL can be renamed but not deleted). It is inert (Autodesk only loads `*.bundle`
directories) — delete it after the machine reboot that the licensing repair needs anyway.

## Not validated here (moves to Lin's healthy workstation)

- Real AutoCAD 2026 launch with the plugin loaded; `INTERGREEN` palette; the full 31-step
  GUI smoke; the 20 real screenshots.
