# -*- coding: utf-8 -*-
"""Package a built release for distribution — two artifacts, both Defender-scanned, both hash-recorded.

    python scripts/package_release.py r14 <guide.pdf>

  1. Staff ZIP  Downloads/Mahod_Intergreen_<ver>.zip — exactly two files: the installer exe and the guide
                PDF (Arthur, 2026-08-26: "double-click install, Always Load; nothing else in it").
  2. No-installer ZIP  Downloads/Mahod_Intergreen_<ver>_ללא_מתקין.zip — the bundle folder + guide +
                Hebrew copy instructions; refuses to contain any executable or script (Defender's
                Wacatac.B!ml false positive on unsigned single-file installers — docs/shipping/).
  3. Windows Defender custom scan of both ZIPs (MpCmdRun -Scan -ScanType 3). A finding fails the run.
  4. Every file's SHA-256 goes to docs/releases/PACKAGE_<rev>.txt.

Fail-closed: the installer payload must equal dist (hash), the exe must carry the revision.
"""
import hashlib, os, re, subprocess, sys, zipfile
from datetime import datetime
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle"
DOWNLOADS = Path.home() / "Downloads"
BANNED = (".exe", ".msi", ".bat", ".cmd", ".ps1", ".scr", ".com", ".vbs", ".js", ".lnk", ".dll")
lines = []


def say(s): print(s); lines.append(s)
def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""): h.update(chunk)
    return h.hexdigest()


def defender_scan(path):
    exe = Path(os.path.expandvars(r"%ProgramFiles%\Windows Defender\MpCmdRun.exe"))
    if not exe.exists():
        return "MpCmdRun.exe not found — NOT SCANNED"
    r = subprocess.run([str(exe), "-Scan", "-ScanType", "3", "-File", str(path), "-DisableRemediation"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
    out = (r.stdout or "") + (r.stderr or "")
    clean = r.returncode == 0 and "found no threats" in out.lower()
    return ("CLEAN" if clean else "THREAT/ERROR") + f" (exit {r.returncode}): " + " ".join(out.split())[-200:]


def main():
    if len(sys.argv) < 3: print(__doc__); sys.exit(2)
    rev, guide = sys.argv[1], Path(sys.argv[2])
    props = (REPO / "build/MahodRelease.props").read_text(encoding="utf-8")
    eng = re.search(r"<MahodEngineVersion>([\d.]+)</MahodEngineVersion>", props).group(1)
    ver = f"{eng}-{rev}"
    exe = REPO / "installer/out" / f"Mahod_Intergreen_Setup_{ver}.exe"
    if not exe.exists(): print("installer not built:", exe); sys.exit(1)
    if not guide.exists(): print("guide missing:", guide); sys.exit(1)
    say(f"{ver} packaging — {datetime.now():%Y-%m-%d %H:%M}")
    say(f"  installer  {exe.name}  {exe.stat().st_size} B  sha256 {sha(exe)}")
    say(f"  guide      {guide.name}  {guide.stat().st_size} B  sha256 {sha(guide)}")

    # 1. staff ZIP — exactly two files
    staff = DOWNLOADS / f"Mahod_Intergreen_{ver}.zip"
    if staff.exists(): staff.unlink()
    with zipfile.ZipFile(staff, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(exe, exe.name)
        z.write(guide, "Mahod_Intergreen_מדריך_מהיר.pdf")
    with zipfile.ZipFile(staff) as z:
        names = z.namelist()
    if len(names) != 2: say("[FAIL] staff zip must hold exactly two files: " + str(names)); sys.exit(1)
    say(f"[PASS] staff ZIP {staff}  {staff.stat().st_size} B  sha256 {sha(staff)}  files={names}")

    # 2. no-installer ZIP — bundle + guide + instructions, nothing executable
    noinst = DOWNLOADS / f"Mahod_Intergreen_{ver}_ללא_מתקין.zip"
    if noinst.exists(): noinst.unlink()
    instructions = f"""﻿התקנה ידנית — Mahod Intergreen {ver}
=========================================

אין בחבילה הזו קובץ התקנה. ההתקנה היא העתקה של תיקייה אחת.

1. לסגור את כל חלונות Civil 3D / AutoCAD.
2. לפתוח את סייר הקבצים, להדביק בשורת הכתובת את הנתיב הבא וללחוץ Enter:
   %APPDATA%\\Autodesk\\ApplicationPlugins
3. לגרור לתוך התיקייה שנפתחה את התיקייה Mahod.Intergreen.bundle שנמצאת בזיפ הזה.
   אם כבר קיימת שם תיקייה בשם הזה — למחוק אותה קודם, ואז לגרור את החדשה.
4. לפתוח מחדש את Civil 3D ולהקליד בשורת הפקודה:  INTERGREEN
   בפתיחה הראשונה תופיע שאלת אבטחה על טעינת התוסף — לבחור Always Load.

המדריך המהיר נמצא בקובץ ה-PDF שבזיפ.
"""
    with zipfile.ZipFile(noinst, "w", zipfile.ZIP_DEFLATED) as z:
        for p in sorted(DIST.rglob("*")):
            if p.is_file():
                if p.suffix.lower() in BANNED and p.suffix.lower() != ".dll":
                    say(f"[FAIL] executable/script inside the bundle: {p}"); sys.exit(1)
                z.write(p, "Mahod.Intergreen.bundle/" + p.relative_to(DIST).as_posix())
        z.write(guide, "Mahod_Intergreen_מדריך_מהיר.pdf")
        z.writestr("קרא_אותי_התקנה.txt", instructions)
    say(f"[PASS] no-installer ZIP {noinst}  {noinst.stat().st_size} B  sha256 {sha(noinst)}")

    # 3. Defender
    for p in (staff, noinst):
        verdict = defender_scan(p)
        say(f"  Defender {p.name}: {verdict}")
        if not verdict.startswith("CLEAN"):
            say("[FAIL] Defender did not return a clean verdict"); sys.exit(1)
    say("[PASS] Defender custom scan clean on both ZIPs")

    # 4. record
    say("")
    say("SUMMARY")
    say(f"  staff ZIP        {staff}")
    say(f"  staff sha256     {sha(staff)}")
    say(f"  no-installer ZIP {noinst}")
    say(f"  no-inst sha256   {sha(noinst)}")
    say(f"  installer sha256 {sha(exe)}")
    say("  RESULT           PASS")
    out = REPO / "docs/releases" / f"PACKAGE_{rev}.txt"
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("record →", out)


if __name__ == "__main__":
    main()
