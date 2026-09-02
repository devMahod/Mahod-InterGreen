# -*- coding: utf-8 -*-
"""ED-016 explicit half — "הארך בשרטוט" written into a real DWG, in real Autodesk hosts, on a COPY.

Example 2, tolerance pre-set to 10 cm, no hand confirmations. IG_SMOKE_EXTEND_IN_DWG must:
  * extend S-L.b2 virtually at Validate (4.37 cm) and leave E-L.b1 (16.45 cm) pending;
  * write S-L.b2's extension into the drawing (LWPolyline vertex / Line endpoint);
  * on re-extraction find S-L.b2 meeting its stop line exactly — nothing virtual any more — while
    E-L.b1 is still pending (the tolerance did not cover it, and nothing was written for it).
The drawing copy is then QSAVEd and re-read by a second process to prove the change persisted.

Usage:  python scripts/realhost_extend_in_dwg.py [--host 2026|2027] [--keep]
"""
import argparse, json, os, shutil, subprocess, sys, tempfile
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle/Contents"
BIN = REPO / "src/Mahod.Intergreen.AutoCAD2026/bin"
TFM = {"2026": "net8.0-windows", "2027": "net10.0-windows"}
WB2 = REPO.parent / "materials" / "Inter-green Automation" / "04 Example 2" / "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx"
DWG2 = REPO / "test-results/dwg-work/ex2.dwg"
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"), ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]
FRESH = ["Mahod.Intergreen.AutoCAD.dll", "Mahod.Intergreen.Host.dll", "Mahod.Intergreen.Geometry.dll"]


def run_console(acad, dwg, script_text, workdir, name):
    scr = workdir / name
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
        bundle_root = Path(os.getenv("APPDATA")) / "Autodesk/ApplicationPlugins/MahodExtendSmoke.bundle"
        if bundle_root.exists():
            shutil.rmtree(bundle_root)
        contents = bundle_root / "Contents"
        shutil.copytree(DIST / year, contents)
        for name in FRESH:
            shutil.copy2(BIN / f"host{year}" / "Release" / TFM[year] / name, contents / name)
        netload = 'SECURELOAD 0\nNETLOAD "' + str(contents / "Mahod.Intergreen.AutoCAD.dll") + '"\n'

        work = Path(tempfile.mkdtemp(prefix=f"ig-extdwg-{year}-"))
        dwg = work / "ex2.dwg"
        shutil.copy2(DWG2, dwg)
        (work / "ex2.intergreen-project.json").write_text(json.dumps({
            "schemaVersion": 1, "unitsConfirmed": "meters",
            "unitsConfirmedBy": "realhost_extend_in_dwg preplaced",
            "autoConfirmEndpointToleranceMeters": 0.10,
        }, indent=2), encoding="utf-8")
        # pass 1: extend virtually, write into the DWG, re-validate, save the copy
        run_console(acad, dwg, netload + f"IG_SMOKE_EXTEND_IN_DWG\n{WB2}\nQSAVE\nQUIT\nY\n", work, "pass1.scr")
        out = work / "ig_extend_in_dwg.json"
        d = json.load(open(out, encoding="utf-8")) if out.exists() else {}
        failures = []
        if not d:
            failures.append("no smoke output")
        if d.get("error"):
            failures.append("error: " + str(d.get("error"))[:300] + " | " + str(d.get("exception_full_detail", ""))[:300])
        vb = [v["curveId"] for v in (d.get("virtual_before") or [])]
        if vb != ["S-L.b2"]:
            failures.append(f"virtual_before={vb} (expected ['S-L.b2'])")
        if d.get("written") != ["S-L.b2"]:
            failures.append(f"written={d.get('written')} skipped={d.get('skipped')} (expected ['S-L.b2'])")
        if d.get("virtual_after") != []:
            failures.append(f"virtual_after={d.get('virtual_after')} (expected [] — the DWG now meets its stop line)")
        if d.get("pending_after") != ["E-L.b1"]:
            failures.append(f"pending_after={d.get('pending_after')} (expected ['E-L.b1'])")

        # pass 2: a fresh process re-reads the saved copy — S-L.b2 must still meet its stop line (persisted)
        out.unlink(missing_ok=True)
        (work / "ex2.intergreen-project.json").write_text(json.dumps({
            "schemaVersion": 1, "unitsConfirmed": "meters",
            "unitsConfirmedBy": "realhost_extend_in_dwg preplaced",
            "autoConfirmEndpointToleranceMeters": 0.0,          # tolerance OFF: only real intersections count now
        }, indent=2), encoding="utf-8")
        run_console(acad, dwg, netload + f"IG_SMOKE_REFERENCE_REVIEW\n{WB2}\nQUIT\nY\n", work, "pass2.scr")
        rr = work / "ig_reference_review.json"
        d2 = json.load(open(rr, encoding="utf-8")) if rr.exists() else {}
        pending2 = [p["curveId"] for p in (d2.get("pending") or [])]
        if pending2 != ["E-L.b1"]:
            failures.append(f"after QSAVE + reopen, pending={pending2} (expected ['E-L.b1'] — S-L.b2 persisted as written)")
        an = d2.get("analyze_after") or {}
        if an.get("conflicts") != 168 or an.get("matrix_valid") != 37 or an.get("matrix_review") != 7 or an.get("matrix_blocked") != 0:
            failures.append(f"golden after confirming E-L.b1: conf={an.get('conflicts')} matrix={an.get('matrix_valid')}/{an.get('matrix_review')}/{an.get('matrix_blocked')}")

        status = "PASS" if not failures else "FAIL"
        ext = (d.get("virtual_before") or [{}])[0]
        print(f"Extend-in-DWG [{year}] {status} | virtual S-L.b2 +{ext.get('extension_cm')} cm → written={d.get('written')} skipped={d.get('skipped')} "
              f"| after write: virtual={d.get('virtual_after')} pending={d.get('pending_after')} | reopened (tol 0): pending={pending2} "
              f"| golden after E-L.b1 confirmed: {an.get('conflicts')} / {an.get('matrix_valid')}-{an.get('matrix_review')}-{an.get('matrix_blocked')} | release={d.get('release_id')}")
        for f in failures:
            print(f"   FAIL[{year}]: {f}")
        ok_all &= not failures
        if not args.keep:
            shutil.rmtree(work, ignore_errors=True)
        else:
            print(f"   kept: {work}")
        shutil.rmtree(bundle_root, ignore_errors=True)
    sys.exit(0 if ok_all else 1)


if __name__ == "__main__":
    main()
