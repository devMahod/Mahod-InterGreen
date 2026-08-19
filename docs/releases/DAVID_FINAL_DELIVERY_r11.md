# DAVID FINAL DELIVERY PACKAGE PREPARED — 0.1.0-r11 (2026-08-19)

Internal record only (NOT part of David's zip).

## What David receives
`Mahod_Intergreen_DAVID_r11.zip` — exactly two root files, no folders:

| File | Size | SHA-256 |
|---|---|---|
| `Mahod_Intergreen_Setup_r11.exe` | 73,831,836 | `3598b6e7d4717cc07240a4d86654439c3d4703f13b621d1615224910be0121b0` |
| `Mahod_Intergreen_מדריך_מהיר.pdf` (2 pages, Hebrew RTL) | 85,653 | `00de0b4f1f037450adff6e5f0b6555732340974d3d05c924891688da87c20c56` |

ZIP SHA-256: `fe2fdf6a08b246aa258d80ff38cd5edb92620c97675aa9f0d676294e1ac836c2` (68,382,048 bytes).

## Branding-only rebuild (binary difference vs. the accepted r11 installer `ae653fa9…`)
The accepted r11 installer `Mahod_Intergreen_Setup_0.1.0-r11.exe` (Lin's recheck package) is untouched.
The David installer was rebuilt from source commit `32434c5` (r11 code `df96c36` + branding-only commit):

- `src/Mahod.Intergreen.AutoCAD2026/IgWorkflowCommands.cs` — 3 lines: palette header text
  `"MAHOD INTERGREEN — Pilot"` → `"Mahod Intergreen"`, `AddVisual("Pilot")` → `"Intergreen"`, doc comment.
- `dist/.../PackageContents.xml` — AppDescription `"INTERGREEN pilot workflow"` → `"Intergreen workflow"`, git stamp.
- Installer published as `Mahod_Intergreen_Setup_r11.exe` (AssemblyName only; self-named uninstaller copy,
  payload resource looked up by suffix — unaffected).

Bytes that changed inside the bundle: `Mahod.Intergreen.AutoCAD.dll` (2026/2027: strings + git stamp) and
`Mahod.Intergreen.Host.dll` (git stamp only). **Unchanged bytes**: `Mahod.Intergreen.Excel.dll` (`d88cc968…`,
accepted r11), the 5 locked engineering DLLs (r4-era approved bytes), rule packs (locked, sha unchanged),
third-party DLLs. Engineering logic, Excel writer and rules were not modified.
Static identity gate: `RELEASE_BUILD_r11_branding.txt` = PASS; automated suite after the branding commit: 319/319.

## "Pilot" wording audit (David-facing)
- ZIP filename, installer filename, PDF filename: 0 hits.
- PDF text + raw bytes: 0 hits (Pilot/pilot/פיילוט).
- Installer exe bytes (ASCII + UTF-16): 0 hits.
- Inside the compressed payload, the only occurrence is the rule-pack manifest
  `rules/legacy-mahod-v1/manifest.json` → `"notes": "… pending David pilot sign-off (Directive §32)"`.
  Rule packs are locked (sha-pinned) and the `notes` field is never read or displayed by any code
  (grep: no consumer in src/). Left as-is by design; not visible to David.

## Not done (by directive)
- Autodesk (Civil 3D / AutoCAD) was NOT opened or touched for this package. The branding rebuild is
  covered by the automated suite + static identity gate only; the r11 GUI smoke (07_GUI_SMOKE_r11) was
  executed on the accepted r11 build `df96c36`/`ae653fa9…` whose engineering/Excel bytes are identical.
- No email sent. Lin's `Mahod_Intergreen_LIN_RECHECK_0.1.0-r11.zip` (`79888327…ddc17`) untouched.
- `Mahod_Intergreen_DAVID_PILOT_0.1.0-r11.zip` (held) is superseded by this package.
