# MULTIHOST_COMPATIBILITY_MATRIX (0.1.0-r4, 2026-08-18)

| Host | Runtime | Host build | Status |
|---|---|---|---|
| AutoCAD 2026 | .NET 8 | 2026 (207c647036d303d8) | **GUI_SMOKE baseline = Lin r5 (in flight)** + AUTOMATED_VERIFIED + real-runtime headless NETLOAD/SCAN/EXPORT PASS |
| Civil 3D 2026 | .NET 8 | 2026 (same DLL) | COMPILED + AUTOMATED_VERIFIED (shared-year host; no Civil 3D 2026 install available anywhere yet — GUI smoke pending if an environment appears) |
| AutoCAD 2027 | .NET 10 | 2027 (f4414d1ec31be40c) | AUTOMATED_VERIFIED + real-runtime headless PASS (the 2027 console on this machine is the Civil-flavored one) |
| Civil 3D 2027 | .NET 10 | 2027 (same DLL) | AUTOMATED_VERIFIED + real-runtime headless NETLOAD/SCAN/EXPORT PASS in the actual Civil 3D 2027 console — **real GUI smoke = Arthur package, PENDING** |

Statuses per directive §16: COMPILED < AUTOMATED_VERIFIED < GUI_SMOKE_VERIFIED — never collapsed.
Host detection on this machine: AutoCAD 2026 · AutoCAD 2027 · Civil 3D 2027 (correctly no Civil 3D 2026).
2027 note: NETLOAD from an untrusted path is blocked by 2027 SECURELOAD hardening; the product
installs to ApplicationPlugins (trusted) — proven loading from there.
