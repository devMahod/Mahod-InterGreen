# -*- coding: utf-8 -*-
"""Install the built release on THIS machine with the real installer (silent), prove the installed bundle,
and prove autoload — the two things a headless smoke with NETLOAD never proves.

    python scripts/realhost_install_check.py r14

  1. installer/out/Mahod_Intergreen_Setup_<ver>.exe /silent   (refuses if AutoCAD/accoreconsole runs)
  2. %APPDATA%\\Autodesk\\ApplicationPlugins\\Mahod.Intergreen.bundle == dist/ file-for-file (SHA-256, same set)
  3. HKCU Add/Remove Programs entry present, DisplayVersion carries the revision; uninstaller exe in place
  4. autoload: accoreconsole 2026 / 2027 open a COPY of Example 1 and run IG_SMOKE_REFERENCE_REVIEW with
     NO NETLOAD in the script — the command exists only if Autodesk's autoloader loaded the installed bundle;
     the analysis must be the golden (50 conflicts, 24/0/0, W-L→S-T 5) and carry the release id.
  5. record → docs/releases/INSTALL_CHECK_<rev>.txt

Nothing is uninstalled afterwards: the machine keeps the release installed, as an engineer's would.
"""
import hashlib, json, os, re, shutil, subprocess, sys, tempfile
from datetime import datetime
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle"
WB1 = REPO.parent / "materials" / "Inter-green Automation" / "03 Example 1" / "05_PINES-HABAL- SHEM-TOM_IG_matrix_2026-07-28-maya.xlsx"
DWG1 = REPO / "test-results/dwg-work/ex1.dwg"
HOSTS = [("2026", r"C:\Program Files\Autodesk\AutoCAD 2026"), ("2027", r"C:\Program Files\Autodesk\AutoCAD 2027")]
GOLD = dict(conflicts=50, matrix_valid=24, matrix_review=0, matrix_blocked=0, wl_st_final_ig=5)
lines = []


def say(s): print(s); lines.append(s)
def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""): h.update(chunk)
    return h.hexdigest()
def tree(root):
    return {p.relative_to(root).as_posix(): sha(p) for p in Path(root).rglob("*") if p.is_file()}


def main():
    rev = sys.argv[1] if len(sys.argv) > 1 else "r14"
    props = (REPO / "build/MahodRelease.props").read_text(encoding="utf-8")
    eng = re.search(r"<MahodEngineVersion>([\d.]+)</MahodEngineVersion>", props).group(1)
    ver = f"{eng}-{rev}"
    exe = REPO / "installer/out" / f"Mahod_Intergreen_Setup_{ver}.exe"
    say(f"{ver} install check — {datetime.now():%Y-%m-%d %H:%M}")
    failures = []
    if not exe.exists():
        print("installer missing:", exe); sys.exit(1)
    say(f"  installer {exe.name} sha256 {sha(exe)}")

    running = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
    if "acad.exe" in running or "accoreconsole.exe" in running:
        say("[FAIL] an Autodesk process is running — the installer would refuse"); sys.exit(1)

    # 1. silent install
    r = subprocess.run([str(exe), "/silent"], capture_output=True, text=True, timeout=600)
    say(f"  installer /silent exit={r.returncode}")
    if r.returncode != 0:
        log = Path(tempfile.gettempdir()) / "MahodIntergreenSetup.error.log"
        failures.append("installer exit " + str(r.returncode) + (": " + log.read_text(encoding="utf-8", errors="replace")[:400] if log.exists() else ""))

    # 2. installed bundle == dist
    bundle = Path(os.path.expandvars(r"%APPDATA%\Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle"))
    if not bundle.exists():
        failures.append("installed bundle missing: " + str(bundle))
        installed = {}
    else:
        installed = tree(bundle)
    expected = tree(DIST)
    if installed and installed != expected:
        missing = sorted(set(expected) - set(installed)); extra = sorted(set(installed) - set(expected))
        changed = sorted(k for k in expected if k in installed and expected[k] != installed[k])
        failures.append(f"installed bundle differs from dist: missing={missing} extra={extra} changed={changed}")
    elif installed:
        say(f"[PASS] installed bundle == dist ({len(installed)} files, every SHA-256 equal)")
    stamp = re.search(r"Release-stamped: ([^ ]+), git (\w+)", (bundle / "PackageContents.xml").read_text(encoding="utf-8")) if bundle.exists() else None
    if stamp: say(f"  installed PackageContents: {stamp.group(1)} git {stamp.group(2)[:7]}")

    # 3. Add/Remove Programs + uninstaller
    rq = subprocess.run(["reg", "query", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MahodIntergreen"],
                        capture_output=True, text=True)
    if rq.returncode != 0 or rev not in rq.stdout:
        failures.append("Add/Remove Programs entry missing or without the revision: " + rq.stdout[-300:])
    else:
        dv = re.search(r"DisplayVersion\s+REG_SZ\s+(.+)", rq.stdout)
        say(f"[PASS] Add/Remove Programs entry present, DisplayVersion {dv.group(1).strip() if dv else '?'}")
    un = Path(os.path.expandvars(r"%LOCALAPPDATA%\Mahod\MahodIntergreen\Uninstall_Mahod_Intergreen.exe"))
    if un.exists(): say(f"[PASS] uninstaller in place: {un}")
    else: failures.append("uninstaller exe missing: " + str(un))

    # 4. autoload — no NETLOAD anywhere in the script
    for year, acad in HOSTS:
        if not Path(acad, "accoreconsole.exe").exists():
            say(f"  {year}: host not installed — SKIPPED"); continue
        work = Path(tempfile.mkdtemp(prefix=f"ig-install-{year}-"))
        shutil.copy2(DWG1, work / "ex1.dwg")
        scr = work / "run.scr"
        scr.write_text(f"IG_SMOKE_REFERENCE_REVIEW\n{WB1}\nQUIT\nY\n", encoding="utf-8")
        subprocess.run([acad + r"\accoreconsole.exe", "/i", str(work / "ex1.dwg"), "/s", str(scr), "/l", "en-US"],
                       capture_output=True, timeout=300, cwd=acad)
        out = work / "ig_reference_review.json"
        d = json.load(open(out, encoding="utf-8")) if out.exists() else {}
        an = d.get("analyze_before") or {}
        ok = bool(d) and all(an.get(k) == v for k, v in GOLD.items()) and rev in str(d.get("release_id"))
        say(f"[{'PASS' if ok else 'FAIL'}] autoload {year}: " + (f"command available without NETLOAD; release={d.get('release_id')}; "
            f"conf={an.get('conflicts')} matrix={an.get('matrix_valid')}/{an.get('matrix_review')}/{an.get('matrix_blocked')} WL-ST={an.get('wl_st_final_ig')}"
            if d else "no output — the installed bundle was NOT autoloaded (or the command failed)"))
        if not ok: failures.append(f"autoload {year}")
        shutil.rmtree(work, ignore_errors=True)

    say("")
    say("RESULT " + ("PASS" if not failures else "FAIL"))
    for f in failures: say("  FAIL: " + f)
    out = REPO / "docs/releases" / f"INSTALL_CHECK_{rev}.txt"
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("record →", out)
    sys.exit(0 if not failures else 1)


if __name__ == "__main__":
    main()
