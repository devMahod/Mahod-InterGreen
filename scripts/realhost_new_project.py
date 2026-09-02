# -*- coding: utf-8 -*-
"""r14 "new project" — real-Autodesk proof without touching Arthur's Civil session.

For every installed host (accoreconsole 2026 / 2027):
  * a TEMP bundle = dist/Contents/<year> with the freshly built AutoCAD/Host/Geometry DLLs laid over
    it (dist itself is never modified here — that is the release build's job);
  * a copy of the Example 1 drawing WITHOUT a sidecar, i.e. a brand-new project;
  * IG_SMOKE_NEW_PROJECT <reference workbook>: the palette's own new-project path, headless — the
    form's answers are lifted from the engineers' hand-filled workbook so the generated one can be
    held against the golden numbers;
  * checks: crossing slots a..d → c2..c5 (the engineers' own placement), workbook accepted and
    committed as the project's workbook, Validate clean, Analyze == golden
    (50 conflicts, matrix 24/0/0, W-L→S-T 5).

Usage:  python scripts/realhost_new_project.py [--host 2026|2027] [--keep]
Exit 0 = every check on every installed host PASSes.
"""
import argparse, json, os, shutil, subprocess, sys, tempfile
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle/Contents"
BIN = REPO / "src/Mahod.Intergreen.AutoCAD2026/bin"
TFM = {"2026": "net8.0-windows", "2027": "net10.0-windows"}
WB1 = (REPO.parent / "materials" / "Inter-green Automation" / "03 Example 1" /
       "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx")
DWG1 = REPO / "test-results/dwg-work/ex1.dwg"
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"),
         ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]
FRESH = ["Mahod.Intergreen.AutoCAD.dll", "Mahod.Intergreen.Host.dll", "Mahod.Intergreen.Geometry.dll"]
EX1_GOLD = dict(movements=11, crossings=4, conflicts=50, matrix_valid=24, matrix_review=0, matrix_blocked=0, wl_st_final_ig=5)
EX1_SLOTS = {"a": [2], "b": [3], "c": [4], "d": [5]}


def run_console(acad, dwg, script_text, workdir):
    scr = workdir / "run.scr"
    scr.write_text(script_text, encoding="utf-8")
    subprocess.run([acad + r"\accoreconsole.exe", "/i", str(dwg), "/s", str(scr), "/l", "en-US"],
                   capture_output=True, timeout=300, cwd=acad)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", choices=["2026", "2027"])
    ap.add_argument("--keep", action="store_true", help="keep the temp folders for inspection")
    args = ap.parse_args()
    ok_all = True
    for year, acad in HOSTS:
        if args.host and year != args.host:
            continue
        if not Path(acad, "accoreconsole.exe").exists():
            print(f"{year}: host not installed — SKIPPED")
            continue
        bundle_root = Path(os.getenv("APPDATA")) / "Autodesk/ApplicationPlugins/MahodNewProjectSmoke.bundle"
        if bundle_root.exists():
            shutil.rmtree(bundle_root)
        contents = bundle_root / "Contents"
        shutil.copytree(DIST / year, contents)
        fresh_dir = BIN / f"host{year}" / "Release" / TFM[year]
        fresh = []
        for name in FRESH:
            src = fresh_dir / name
            if not src.exists():
                print(f"   FAIL[{year}]: fresh build missing {src}")
                ok_all = False
                continue
            shutil.copy2(src, contents / name)
            fresh.append(name)
        work = Path(tempfile.mkdtemp(prefix=f"ig-newproject-{year}-"))
        shutil.copy2(DWG1, work / "ex1.dwg")
        netload = 'SECURELOAD 0\nNETLOAD "' + str(contents / "Mahod.Intergreen.AutoCAD.dll") + '"\n'
        run_console(acad, work / "ex1.dwg", netload + f"IG_SMOKE_NEW_PROJECT\n{WB1}\nQUIT\nY\n", work)
        out = work / "ig_new_project.json"
        failures = []
        if not out.exists():
            failures.append("no smoke output (NETLOAD or command failed)")
            d = {}
        else:
            d = json.load(open(out, encoding="utf-8"))
            if d.get("error"):
                failures.append("error: " + str(d.get("error")) + " | " + str(d.get("exception_full_detail", ""))[:400])
            pre = d.get("prefill") or {}
            slots = {c["letter"]: c["slots"] for c in pre.get("crossings", [])}
            if slots != EX1_SLOTS:
                failures.append(f"slots {slots} (expected {EX1_SLOTS})")
            if pre.get("warnings"):
                failures.append("prefill warnings: " + json.dumps(pre.get("warnings"), ensure_ascii=False))
            if d.get("setup_state") != "configured":
                failures.append("setup_state=" + str(d.get("setup_state")))
            if d.get("validate_pending_references") not in (0, None):
                failures.append("pending references=" + str(d.get("validate_pending_references")))
            an = d.get("analyze") or {}
            for k, v in EX1_GOLD.items():
                if an.get(k) != v:
                    failures.append(f"analyze {k}={an.get(k)} (expected {v})")
            wb = d.get("workbook")
            if not (wb and Path(wb).exists()):
                failures.append("generated workbook missing: " + str(wb))
        status = "PASS" if not failures else "FAIL"
        an = d.get("analyze") or {}
        print(f"NewProject smoke [{year}] {status} | fresh={','.join(n.split('.')[2] for n in fresh)} | "
              f"slots={ {c['letter']: c['slots'] for c in (d.get('prefill') or {}).get('crossings', [])} } | "
              f"conf={an.get('conflicts')} matrix={an.get('matrix_valid')}/{an.get('matrix_review')}/{an.get('matrix_blocked')} "
              f"WL-ST={an.get('wl_st_final_ig')} | workbook={Path(d.get('workbook') or '?').name} | release={d.get('release_id')}")
        for f in failures:
            print(f"   FAIL[{year}]: {f}")
        ok_all &= not failures
        if not args.keep:
            shutil.rmtree(bundle_root, ignore_errors=True)
            shutil.rmtree(work, ignore_errors=True)
        else:
            print(f"   kept: {work}")
    sys.exit(0 if ok_all else 1)


if __name__ == "__main__":
    main()
