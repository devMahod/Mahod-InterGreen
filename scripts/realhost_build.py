# -*- coding: utf-8 -*-
"""r14 WP6 — the Appendix A movement builder, headless, in real Autodesk hosts, on a COPY of Example 1.

Example 1 has no north approach. The smoke builds "N-T" out of the two S-T boundary entities and the
S stop line (as if the engineer had pointed at planner lines): the builder must COPY them onto
intergreen_N-T (colour ACI 52, north straight per Appendix B / ColourModel) and intergreen_stopline, leave the
sources untouched, and the engine must then see N-T with two boundaries and a stop line.

Usage:  python scripts/realhost_build.py [--host 2026|2027] [--keep]
"""
import argparse, json, os, shutil, subprocess, sys, tempfile
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle/Contents"
BIN = REPO / "src/Mahod.Intergreen.AutoCAD2026/bin"
TFM = {"2026": "net8.0-windows", "2027": "net10.0-windows"}
DWG1 = REPO / "test-results/dwg-work/ex1.dwg"
FIX1 = REPO / "tests/fixtures/geometry/example1.iggeometry.json"
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"), ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]
FRESH = ["Mahod.Intergreen.AutoCAD.dll", "Mahod.Intergreen.Host.dll", "Mahod.Intergreen.Geometry.dll"]
NORTH_ACI = 52  # ColourModel: north straight (N-T) = yellow 52 (N-L 2, N-R 42)


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

    fx = json.load(open(FIX1, encoding="utf-8"))
    by_layer = {}
    for c in fx["curves"]:
        by_layer.setdefault(c["layer"].lower(), []).append(c["handle"])
    st = by_layer["intergreen_s-t"]
    stop = by_layer["intergreen_stopline"][0]
    before_counts = {k: len(v) for k, v in by_layer.items()}

    ok_all = True
    for year, acad in HOSTS:
        if args.host and year != args.host:
            continue
        if not Path(acad, "accoreconsole.exe").exists():
            print(f"{year}: host not installed — SKIPPED")
            continue
        bundle_root = Path(os.getenv("APPDATA")) / "Autodesk/ApplicationPlugins/MahodBuildSmoke.bundle"
        if bundle_root.exists():
            shutil.rmtree(bundle_root)
        contents = bundle_root / "Contents"
        shutil.copytree(DIST / year, contents)
        for name in FRESH:
            shutil.copy2(BIN / f"host{year}" / "Release" / TFM[year] / name, contents / name)
        netload = 'SECURELOAD 0\nNETLOAD "' + str(contents / "Mahod.Intergreen.AutoCAD.dll") + '"\n'

        work = Path(tempfile.mkdtemp(prefix=f"ig-build-{year}-"))
        shutil.copy2(DWG1, work / "ex1.dwg")
        run_console(acad, work / "ex1.dwg", netload + f"IG_SMOKE_BUILD\nN-T {st[0]} {st[1]} {stop}\n\nQUIT\nY\n", work)
        out = work / "ig_build.json"
        d = json.load(open(out, encoding="utf-8")) if out.exists() else {}
        failures = []
        if not d:
            failures.append("no smoke output")
        if d.get("error"):
            failures.append("error: " + str(d.get("error"))[:300] + " | " + str(d.get("exception_full_detail", ""))[:300])
        built = (d.get("built") or [{}])[0]
        if built.get("error"):
            failures.append("build error: " + str(built.get("error")))
        if built.get("aci") != NORTH_ACI:
            failures.append(f"layer aci={built.get('aci')} (expected {NORTH_ACI})")
        if len(built.get("copied") or []) != 3:
            failures.append(f"copied={built.get('copied')} (expected 3: two boundaries + stop line)")
        layers = {k.lower(): v for k, v in (d.get("layers_after") or {}).items()}
        if layers.get("intergreen_n-t") != 2:
            failures.append(f"intergreen_N-T holds {layers.get('intergreen_n-t')} curves (expected 2)")
        if layers.get("intergreen_s-t") != before_counts["intergreen_s-t"]:
            failures.append(f"source layer intergreen_S-T changed: {layers.get('intergreen_s-t')} vs {before_counts['intergreen_s-t']}")
        if layers.get("intergreen_stopline") != before_counts["intergreen_stopline"] + 1:
            failures.append(f"intergreen_stopline holds {layers.get('intergreen_stopline')} (expected {before_counts['intergreen_stopline'] + 1})")
        nt = next((m for m in (d.get("movements_after") or []) if m.get("id") == "N-T"), None)
        if not nt or nt.get("boundaries") != 2 or not nt.get("stopLine") or nt.get("mode") != "Vehicle":
            failures.append(f"engine sees N-T as {nt}")
        colours = {k.lower(): v for k, v in (d.get("layer_colours") or {}).items()}
        if colours.get("intergreen_n-t") != NORTH_ACI:
            failures.append(f"layer colour intergreen_N-T={colours.get('intergreen_n-t')} (expected {NORTH_ACI})")
        status = "PASS" if not failures else "FAIL"
        print(f"Builder [{year}] {status} | N-T from S-T {st} + stop {stop} → copied={built.get('copied')} aci={built.get('aci')} "
              f"| layers: N-T={layers.get('intergreen_n-t')} S-T={layers.get('intergreen_s-t')} stopline={layers.get('intergreen_stopline')} "
              f"| engine: {nt} | release={d.get('release_id')}")
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
