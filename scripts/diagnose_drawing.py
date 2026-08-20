# -*- coding: utf-8 -*-
"""One-command diagnosis of a customer drawing + workbook, without opening Civil 3D.

    python diagnose_drawing.py <drawing.dwg> <workbook.xlsx> [--host 2026|2027] [--out <dir>]

Copies both inputs (the originals are never touched), drives the SHIPPING bundle inside a headless
accoreconsole session, and prints:

  * the intergreen_* layer inventory and how many curves each layer holds;
  * every boundary that only reaches its stop line through a drawn endpoint, with the gap in cm
    and the DWG handle — as reported by the engine itself, not by a lookalike heuristic;
  * the matrix before confirming those references and after, so the cost of the condition is
    visible in one table;
  * anything the run could not do, verbatim.

Written for the r12 support loop: when Lin or David sends a file, this answers "what is wrong with
this drawing" in about a minute.
"""
import argparse, json, os, shutil, subprocess, sys, tempfile

# The palette speaks Hebrew and the Windows console defaults to cp1255; force UTF-8 so the
# messages this tool quotes are the messages the engineer actually sees.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DIST = r"C:\Users\arthurf\Downloads\InterGreens\Mahod.Intergreen\dist\Mahod.Intergreen.bundle\Contents"


def accoreconsole(year):
    p = os.path.join(r"C:\Program Files\Autodesk", "AutoCAD " + year, "accoreconsole.exe")
    if not os.path.exists(p):
        sys.exit("accoreconsole not found for AutoCAD " + year)
    return p


def run(work, dwg, xlsx, year, commands):
    """Drive one accoreconsole session; returns its console output."""
    scr = os.path.join(work, "run.scr")
    lines = ["SECURELOAD", "0", "NETLOAD", os.path.join(DIST, year, "Mahod.Intergreen.AutoCAD.dll")]
    for cmd, arg in commands:
        lines.append(cmd)
        if arg is not None:
            lines.append(arg)
    lines += ["QUIT", "Y", ""]
    open(scr, "w", encoding="utf-8").write("\n".join(lines))
    env = dict(os.environ, IG_OUTPUT_DIR=work)
    # accoreconsole emits UTF-16-ish console text; decode defensively rather than crash on it.
    return subprocess.run([accoreconsole(year), "/i", dwg, "/s", scr],
                          capture_output=True, env=env, timeout=900).stdout.decode("utf-8", "replace")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dwg")
    ap.add_argument("xlsx")
    ap.add_argument("--host", default="2026", choices=("2026", "2027"))
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    # accoreconsole resolves its own working directory, so every path handed to it is absolute.
    work = os.path.abspath(a.out) if a.out else tempfile.mkdtemp(prefix="ig-diag-")
    os.makedirs(work, exist_ok=True)
    dwg = os.path.join(work, "subject.dwg")
    xlsx = os.path.join(work, "subject.xlsx")
    shutil.copy(a.dwg, dwg)
    shutil.copy(a.xlsx, xlsx)
    print("workspace:", work)

    run(work, dwg, xlsx, a.host, [("IG_SCAN", None)])
    run(work, dwg, xlsx, a.host, [("IG_SMOKE_REFERENCE_REVIEW", xlsx)])

    scan_path = os.path.join(work, "subject.igscan.json")
    if os.path.exists(scan_path):
        scan = json.load(open(scan_path, encoding="utf-8"))
        print("\n== drawing ==")
        print("   units      :", scan["units"]["insunits"])
        print("   layers     :", scan["layerCount"], "of which intergreen_*:", scan["intergreenLayerCount"])
        print("   intergreen layers with content:")
        for l in scan["layers"]:
            if l.get("isIntergreen") and l.get("entities"):
                print(f"      {l['name']:<28} {l['entities']}")
        empty = [l["name"] for l in scan["layers"] if l.get("isIntergreen") and not l.get("entities")]
        if empty:
            print("   empty intergreen layers   :", ", ".join(empty))

    rp = os.path.join(work, "ig_reference_review.json")
    if not os.path.exists(rp):
        sys.exit("\nNo review output — the drawing or workbook was rejected before analysis.")
    d = json.load(open(rp, encoding="utf-8"))
    print("\n== plugin ==")
    print("   release    :", d.get("release_id"))
    print("   workbook   :", d.get("acceptance_status"))
    if d.get("error"):
        print("   ERROR      :", d["error"])

    pending = d.get("pending") or []
    print("\n== boundaries not reaching their stop line ==")
    if not pending:
        print("   none — every boundary crosses its stop line")
    for p in pending:
        print(f"   {p['movement']:<8} {p['curveId']:<10} handle {p['handle']:<8} gap {p['gap_cm']:>6.2f} cm")
    if d.get("summary_he"):
        print("\n   palette says:", d["summary_he"])

    before, after = d.get("analyze_before"), d.get("analyze_after")
    if before:
        print("\n== result ==")
        head = f"   {'':<22}{'as it runs now':>16}"
        if after:
            head += f"{'after confirming':>18}"
        print(head)
        for label, key in (("conflicts", "conflicts"), ("matrix VALID", "matrix_valid"),
                           ("matrix REVIEW", "matrix_review"), ("matrix BLOCKED", "matrix_blocked")):
            row = f"   {label:<22}{str(before.get(key)):>16}"
            if after:
                row += f"{str(after.get(key)):>18}"
            print(row)
        if after:
            print(f"\n   confirming {', '.join(d.get('confirmed', []))} recovers "
                  f"{after.get('matrix_valid', 0) - before.get('matrix_valid', 0)} matrix cells")


if __name__ == "__main__":
    main()
