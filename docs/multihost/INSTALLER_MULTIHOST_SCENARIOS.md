# INSTALLER_MULTIHOST_SCENARIOS — Mahod_Intergreen_Setup_0.1.0-r4.exe (15/15 PASS)

| Scenario | Result |
|---|---|
| upgrade from installed r3 (old single-year layout) | PASS — replaced wholesale (47/47 files, old layout gone; a stale missing-dep/old bundle cannot stay active) |
| host detection | PASS — AutoCAD 2026 + AutoCAD 2027 + Civil 3D 2027; correctly NOT Civil 3D 2026 (registry flavor names + C3D filesystem probe) |
| one uninstall entry, display name 2026-2027 | PASS |
| reinstall same | PASS (byte identity) |
| locked old bundle | PASS — pre-delete probe, clean abort, untouched |
| Autodesk console running during install (no override) | PASS — guard blocks |
| uninstall removes ONLY the one multihost bundle + key | PASS |
| unrelated Autodesk plugins | PASS — untouched |
| clean install after uninstall | PASS |
| runtime closure from INSTALLED 2026 path | PASS |
| runtime closure from INSTALLED 2027 path | PASS |
| bundle pristine after closure runners | PASS |

Host-presence permutations that cannot be physically simulated here (only-2026 machine,
only-2027 machine, no-host machine): detection is registry/filesystem-per-year and each
component's loading is decided by Autodesk per PackageContents series pinning — the
per-year behavior was verified individually; the no-host GUI path shows an explicit
warning (documented policy: install allowed, plugin loads when a supported host appears).
