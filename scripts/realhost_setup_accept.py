# -*- coding: utf-8 -*-
"""PERMANENT RELEASE GATE — RealHost_SetupAccept_OriginalExample1 (failure-closure §7/§10).

Runs the production Setup-acceptance path (WorkbookPathResolver -> WorkbookAcceptance ->
WorkbookReader -> sidecar commit/reload) INSIDE real Autodesk processes (accoreconsole
2026 + 2027) against the exact original Example-1 workbook, via IG_SMOKE_SETUP_ACCEPT.

History: the r5/r4 builds passed every clean-process test yet failed in Lin's real
AutoCAD (NotImplementedException) because ClosedXML bound to Autodesk's own
DocumentFormat.OpenXml. No headless/proxy result may ever substitute for this gate.

Usage:  python scripts/realhost_setup_accept.py [path-to-bundle-root]
Exit 0 = both hosts PASS.
"""
import json, shutil, subprocess, sys, os, tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
BUNDLE = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "dist/Mahod.Intergreen.bundle"
WB = REPO.parent / "materials" / "Inter-green Automation" / "03 Example 1" / \
     "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx"
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"),
         ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]

ap_root = Path(os.environ["APPDATA"]) / "Autodesk/ApplicationPlugins"
ok_all = True
for year, acad in HOSTS:
    if not Path(acad, "accoreconsole.exe").exists():
        print(f"{year}: host not installed — SKIPPED (must run on a machine that has it)")
        continue
    tb = ap_root / "MahodGate.bundle" / "Contents"
    if tb.parent.exists():
        shutil.rmtree(tb.parent)
    shutil.copytree(BUNDLE / "Contents" / year, tb)
    W = Path(tempfile.mkdtemp(prefix=f"ig-gate-{year}-"))
    shutil.copy2(REPO / "test-results/dwg-work/ex1.dwg", W / "ex1.dwg")
    (W / "run.scr").write_text(
        f'NETLOAD "{tb / "Mahod.Intergreen.AutoCAD.dll"}"\nIG_SMOKE_SETUP_ACCEPT\n{WB}\n',
        encoding="utf-8")
    subprocess.run([acad + r"\accoreconsole.exe", "/i", str(W / "ex1.dwg"),
                    "/s", str(W / "run.scr"), "/l", "en-US"],
                   capture_output=True, timeout=300)
    out = W / "ig_setup_accept.json"
    ok = False
    detail = "no output"
    if out.exists():
        d = json.load(open(out, encoding="utf-8"))
        parity = d.get("input_method_parity") or {}
        ok = (d.get("acceptance_status") == "Ok" and d.get("acceptance_status_manual") == "Ok"
              and d.get("sidecar_commit") == "Ok" and d.get("sidecar_reload") == "Ok"
              and d.get("sidecar_commit_manual") == "Ok" and d.get("sidecar_reload_manual") == "Ok"
              and bool(parity) and all(parity.values()))
        detail = (f"browse+manual OK | parity {parity} | variant={d.get('template_variant')} "
                  f"| counts={d.get('model_counts')}"
                  if ok else (d.get("acceptance_detail") or d.get("exception_full_detail") or "")[:400])
    print(f"RealHost_SetupAccept_OriginalExample1 [{year}]:", "PASS" if ok else "FAIL", "|", detail)
    ok_all &= ok
    shutil.rmtree(tb.parent)
sys.exit(0 if ok_all else 1)
