# -*- coding: utf-8 -*-
"""ED-016 (amended 2026-09-02) — the tolerance pass in real Autodesk hosts, without touching Arthur's Civil.

For every installed host (accoreconsole 2026 / 2027), with a TEMP bundle = dist/Contents/<year> plus the
freshly built AutoCAD/Host/Geometry DLLs:

  EX2  sidecar pre-placed with autoConfirmEndpointToleranceMeters = 0.10 and NO confirmed endpoints
       (the accepted golden needs E-L.b1 and S-L.b2 confirmed by hand). The pass must extend S-L.b2
       (4.4 cm) and leave E-L.b1 (16.45 cm) to the engineer; once the smoke confirms that one the way
       she would, the golden must hold: 168 conflicts, matrix 37 VALID / 7 REVIEW / 0 BLOCKED.
  EX1  same tolerance; Example 1 has no near-miss, so nothing is resolved and the golden stands:
       50 conflicts, 24/0/0, W-L→S-T 5.

Usage:  python scripts/realhost_tolerance_pass.py [--host 2026|2027] [--keep]
Exit 0 = every check on every installed host PASSes.
"""
import argparse, json, os, shutil, subprocess, sys, tempfile
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle/Contents"
BIN = REPO / "src/Mahod.Intergreen.AutoCAD2026/bin"
TFM = {"2026": "net8.0-windows", "2027": "net10.0-windows"}
MAT = REPO.parent / "materials" / "Inter-green Automation"
WB = {"ex1": MAT / "03 Example 1" / "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx",
      "ex2": MAT / "04 Example 2" / "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx"}
DWG = {"ex1": REPO / "test-results/dwg-work/ex1.dwg", "ex2": REPO / "test-results/dwg-work/ex2.dwg"}
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"), ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]
FRESH = ["Mahod.Intergreen.AutoCAD.dll", "Mahod.Intergreen.Host.dll", "Mahod.Intergreen.Geometry.dll"]
GOLD = {"ex1": dict(conflicts=50, matrix_valid=24, matrix_review=0, matrix_blocked=0, wl_st_final_ig=5),
        "ex2": dict(conflicts=168, matrix_valid=37, matrix_review=7, matrix_blocked=0)}
# Example 2's two hand-confirmed boundaries: S-L.b2 misses its stop line by ~4.4 cm (under David's 10 cm →
# extended), E-L.b1 by 16.45 cm (wider than the tolerance → stays with the engineer, exactly as it should).
EXPECT_RESOLVED = {"ex1": set(), "ex2": {"S-L.b2"}}
EXPECT_PENDING = {"ex1": set(), "ex2": {"E-L.b1"}}
TOLERANCE = 0.10


def run_console(acad, dwg, script_text, workdir):
    scr = workdir / "run.scr"
    scr.write_text(script_text, encoding="utf-8")
    subprocess.run([acad + r"\accoreconsole.exe", "/i", str(dwg), "/s", str(scr), "/l", "en-US"],
                   capture_output=True, timeout=300, cwd=acad)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", choices=["2026", "2027"])
    ap.add_argument("--keep", action="store_true")
    args = ap.parse_args()
    ok_all = True
    for year, acad in HOSTS:
        if args.host and year != args.host:
            continue
        if not Path(acad, "accoreconsole.exe").exists():
            print(f"{year}: host not installed — SKIPPED")
            continue
        bundle_root = Path(os.getenv("APPDATA")) / "Autodesk/ApplicationPlugins/MahodToleranceSmoke.bundle"
        if bundle_root.exists():
            shutil.rmtree(bundle_root)
        contents = bundle_root / "Contents"
        shutil.copytree(DIST / year, contents)
        for name in FRESH:
            shutil.copy2(BIN / f"host{year}" / "Release" / TFM[year] / name, contents / name)
        netload = 'SECURELOAD 0\nNETLOAD "' + str(contents / "Mahod.Intergreen.AutoCAD.dll") + '"\n'
        for ex in ("ex2", "ex1"):
            work = Path(tempfile.mkdtemp(prefix=f"ig-tol-{year}-{ex}-"))
            shutil.copy2(DWG[ex], work / f"{ex}.dwg")
            (work / f"{ex}.intergreen-project.json").write_text(json.dumps({
                "schemaVersion": 1,
                "unitsConfirmed": "meters",
                "unitsConfirmedBy": "realhost_tolerance_pass preplaced",
                "autoConfirmEndpointToleranceMeters": TOLERANCE,
            }, indent=2), encoding="utf-8")
            run_console(acad, work / f"{ex}.dwg", netload + f"IG_SMOKE_REFERENCE_REVIEW\n{WB[ex]}\nQUIT\nY\n", work)
            out = work / "ig_reference_review.json"
            failures = []
            d = json.load(open(out, encoding="utf-8")) if out.exists() else {}
            if not d:
                failures.append("no smoke output")
            if d.get("error"):
                failures.append("error: " + str(d.get("error"))[:300])
            resolved = d.get("auto_resolved") or []
            done = {r["curveId"] for r in resolved if r.get("kind") in ("Extended", "Confirmed")}
            if done != EXPECT_RESOLVED[ex]:
                failures.append(f"resolved {sorted(done)} (expected {sorted(EXPECT_RESOLVED[ex])})")
            pending = {p["curveId"] for p in (d.get("pending") or [])}
            if pending != EXPECT_PENDING[ex]:
                failures.append(f"pending {sorted(pending)} (expected {sorted(EXPECT_PENDING[ex])})")
            if abs((d.get("auto_extend_tolerance_m") or 0) - TOLERANCE) > 1e-9:
                failures.append("tolerance read back as " + str(d.get("auto_extend_tolerance_m")))
            # the golden holds once the engineer has confirmed what the tolerance rightly left to her
            an = (d.get("analyze_after") if pending else d.get("analyze_before")) or {}
            for k, v in GOLD[ex].items():
                if an.get(k) != v:
                    failures.append(f"analyze {k}={an.get(k)} (expected {v})")
            status = "PASS" if not failures else "FAIL"
            kinds = ", ".join(f"{r['curveId']}:{r['kind']}" + (f"+{r['extension_cm']}cm" if r.get("extension_cm") is not None else f"({r['gap_cm']}cm)") for r in resolved)
            print(f"Tolerance pass [{year}/{ex}] {status} | resolved: {kinds or '—'} | pending={len(d.get('pending') or [])} | "
                  f"conf={an.get('conflicts')} matrix={an.get('matrix_valid')}/{an.get('matrix_review')}/{an.get('matrix_blocked')} "
                  f"WL-ST={an.get('wl_st_final_ig')} | release={d.get('release_id')}")
            for f in failures:
                print(f"   FAIL[{year}/{ex}]: {f}")
            ok_all &= not failures
            if not args.keep:
                shutil.rmtree(work, ignore_errors=True)
            else:
                print(f"   kept: {work}")
        shutil.rmtree(bundle_root, ignore_errors=True)      # never leave a smoke bundle where AutoCAD autoloads
    sys.exit(0 if ok_all else 1)


if __name__ == "__main__":
    main()
