# -*- coding: utf-8 -*-
"""PERMANENT RELEASE GATE — real-Autodesk production workflow (r8 scope).

Runs INSIDE real Autodesk processes (accoreconsole 2026 + 2027), launched with
CWD = the Autodesk install directory (no rules/ exists there or above — the exact
environment that broke r6 Analyze in Arthur's Civil 3D 2027 GUI):

  EX1  IG_SMOKE_SETUP_ACCEPT   Browse+manual Setup parity, full production
                               Validate+Analyze (golden numbers), production
                               Export Excel (planner+writer), source untouched.
       <openpyxl>              exported workbook opens cleanly, original sheets
                               preserved in order, MAHOD sheets appended.
       IG_SMOKE_ADOPT_ANALYZE  a FRESH process adopts the committed sidecar
                               (= DWG close/reopen) and reproduces the analysis.
  EX2  IG_SMOKE_SETUP_ACCEPT   with the confirmed-endpoints sidecar preplaced:
                               168 conflicts, matrix 37 VALID / 7 REVIEW / 0 BLOCKED.

History: r4/r5 passed every clean-process test yet failed in Lin's real AutoCAD
(dependency binding); r6 passed everything except the real-GUI Analyze (rules CWD).
No headless/proxy result may ever substitute for this gate.

Usage:  python scripts/realhost_setup_accept.py [path-to-bundle-root]
Exit 0 = every check on every installed host PASSes.
"""
import json, shutil, subprocess, sys, os, tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
BUNDLE = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "dist/Mahod.Intergreen.bundle"
WB1 = (REPO.parent / "materials" / "Inter-green Automation" / "03 Example 1" /
       "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx")
WB2 = (REPO.parent / "materials" / "Inter-green Automation" / "04 Example 2" /
       "Abarbanel-HaMaccabim_IG_matrix_2026-07-05.xlsx")
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"),
         ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]

EX1_GOLD = dict(movements=11, crossings=4, conflicts=50,
                matrix_valid=24, matrix_review=0, matrix_blocked=0, wl_st_final_ig=5)
EX2_GOLD = dict(movements=16, conflicts=168,
                matrix_valid=37, matrix_review=7, matrix_blocked=0)


def run_console(acad, dwg, script_text, workdir):
    scr = workdir / "run.scr"
    scr.write_text(script_text, encoding="utf-8")
    # CWD = the Autodesk install dir, exactly like the real GUI (broke r6).
    subprocess.run([acad + r"\accoreconsole.exe", "/i", str(dwg),
                    "/s", str(scr), "/l", "en-US"],
                   capture_output=True, timeout=300, cwd=acad)


def load_json(path):
    if not path.exists():
        return None
    return json.load(open(path, encoding="utf-8"))


def check_analyze(an, gold):
    errs = []
    an = an or {}
    for k, v in gold.items():
        if an.get(k) != v:
            errs.append(f"{k}={an.get(k)} (expected {v})")
    if "Autodesk" not in (an.get("process_cwd") or ""):
        errs.append(f"process_cwd={an.get('process_cwd')} (expected Autodesk install dir)")
    return errs


def verify_export_xlsx(path):
    import openpyxl
    errs = []
    wb = openpyxl.load_workbook(path, read_only=True)
    names = wb.sheetnames
    expected_prefix = ['Parameters', 'Pedestrian Xing', 'Signal group key',
                       'Input Distances', 'AutoAdjusted Distances', 'Matrix', 'גיליון1']
    if names[:len(expected_prefix)] != expected_prefix:
        errs.append(f"original sheets/order changed: {names}")
    for required in ('MAHOD Engine Results', 'MAHOD Matrix Status'):
        if required not in names:
            errs.append(f"missing sheet {required}")
    wb.close()
    return errs


ap_root = Path(os.getenv("APPDATA")) / "Autodesk/ApplicationPlugins"
ok_all = True
for year, acad in HOSTS:
    if not Path(acad, "accoreconsole.exe").exists():
        print(f"{year}: host not installed — SKIPPED (must run on a machine that has it)")
        continue
    tb = ap_root / "MahodGate.bundle" / "Contents"
    if tb.parent.exists():
        shutil.rmtree(tb.parent)
    shutil.copytree(BUNDLE / "Contents" / year, tb)
    netload = 'NETLOAD "' + str(tb / "Mahod.Intergreen.AutoCAD.dll") + '"\n'
    failures = []

    # ---- EX1: setup + analyze + export ----
    W1 = Path(tempfile.mkdtemp(prefix=f"ig-gate-{year}-ex1-"))
    shutil.copy2(REPO / "test-results/dwg-work/ex1.dwg", W1 / "ex1.dwg")
    run_console(acad, W1 / "ex1.dwg", netload + f"IG_SMOKE_SETUP_ACCEPT\n{WB1}\n", W1)
    d = load_json(W1 / "ig_setup_accept.json")
    if d is None:
        failures.append("EX1: no smoke output")
    else:
        parity = d.get("input_method_parity") or {}
        if not (d.get("acceptance_status") == "Ok" and d.get("acceptance_status_manual") == "Ok"
                and d.get("sidecar_commit") == "Ok" and d.get("sidecar_reload") == "Ok"
                and parity and all(parity.values())):
            failures.append("EX1 setup/parity: " + json.dumps(parity) + " | " +
                            (d.get("acceptance_detail") or d.get("exception_full_detail") or "")[:300])
        failures += ["EX1 analyze: " + e for e in check_analyze(d.get("analyze"), EX1_GOLD)]
        exp = d.get("export") or {}
        if exp.get("structural_issues") != 0 or not exp.get("rows"):
            failures.append("EX1 export: " + json.dumps(exp, ensure_ascii=False)[:300] + " | " +
                            (d.get("exception_full_detail") or "")[:300])
        elif exp.get("source_sha_before") != exp.get("source_sha_after"):
            failures.append("EX1 export: SOURCE WORKBOOK CHANGED")
        else:
            failures += ["EX1 xlsx: " + e for e in verify_export_xlsx(exp["path"])]

    # ---- EX1 persistence: fresh process adopts the committed sidecar ----
    run_console(acad, W1 / "ex1.dwg", netload + "IG_SMOKE_ADOPT_ANALYZE\n", W1)
    a = load_json(W1 / "ig_adopt_analyze.json")
    if a is None:
        failures.append("EX1 adopt: no output")
    else:
        if a.get("workbook_ref") != "Ok":
            failures.append("EX1 adopt: workbook_ref=" + str(a.get("workbook_ref")) + " " +
                            (a.get("exception_full_detail") or "")[:300])
        failures += ["EX1 adopt analyze: " + e for e in check_analyze(a.get("analyze"), EX1_GOLD)]
        if not str(a.get("rule_pack_id", "")).startswith("legacy-mahod-v1"):
            failures.append("EX1 adopt: rule_pack_id=" + str(a.get("rule_pack_id")))

    # ---- EX2: confirmed-endpoints sidecar preplaced, then setup + analyze ----
    W2 = Path(tempfile.mkdtemp(prefix=f"ig-gate-{year}-ex2-"))
    shutil.copy2(REPO / "test-results/dwg-work/ex2.dwg", W2 / "ex2.dwg")
    shutil.copy2(REPO / "test-results/dwg-work/ex2.intergreen-project.json",
                 W2 / "ex2.intergreen-project.json")
    run_console(acad, W2 / "ex2.dwg", netload + f"IG_SMOKE_SETUP_ACCEPT\n{WB2}\n", W2)
    d2 = load_json(W2 / "ig_setup_accept.json")
    if d2 is None:
        failures.append("EX2: no smoke output")
    else:
        if d2.get("acceptance_status") != "Ok":
            failures.append("EX2 setup: " +
                            (d2.get("acceptance_detail") or d2.get("exception_full_detail") or "")[:300])
        failures += ["EX2 analyze: " + e for e in check_analyze(d2.get("analyze"), EX2_GOLD)]

    status = "PASS" if not failures else "FAIL"
    an1 = (d or {}).get("analyze") or {}
    an2 = (d2 or {}).get("analyze") or {}
    exp1 = (d or {}).get("export") or {}
    print(f"RealHost gate [{year}]: {status} | "
          f"ex1 mov={an1.get('movements')}/{an1.get('crossings')} conf={an1.get('conflicts')} "
          f"matrix={an1.get('matrix_valid')}/{an1.get('matrix_review')}/{an1.get('matrix_blocked')} "
          f"WL-ST={an1.get('wl_st_final_ig')} export_rows={exp1.get('rows')} | "
          f"ex2 conf={an2.get('conflicts')} "
          f"matrix={an2.get('matrix_valid')}/{an2.get('matrix_review')}/{an2.get('matrix_blocked')}")
    for msg in failures:
        print(f"   FAIL[{year}]: {msg}")
    ok_all &= not failures
    shutil.rmtree(tb.parent)
sys.exit(0 if ok_all else 1)
