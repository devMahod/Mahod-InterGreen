# -*- coding: utf-8 -*-
"""Release build with gates — the procedure that produced r10..r13, written down and committed.

    python scripts/release_build.py r14

What it does, in order, failing closed at every step:

  1. Revision gate    build/MahodRelease.props must say the requested revision; the tree must be clean.
  2. Source gate      the five locked engineering assemblies (Contracts, Core, Reporting, Rules, Excel) and
                      rules/ are byte-for-byte the accepted r11 source (commit 02c7a47). Geometry is the one
                      engineering assembly that moved (r13 §21A, r14 ED-015) — by recorded decision.
  3. Build            both host flavours, Release, stamped with the real git SHA (never UNSTAMPED-DEV).
  4. Dist update      AutoCAD / Host / Geometry DLLs copied into dist/Contents/{2026,2027}; every other
                      file in dist is hash-checked unchanged (the locked DLLs stay the accepted bytes).
  5. Manifest         PackageContents.xml stamped with the revision, version and SHA.
  6. Installer        payload/bundle.zip == dist; dotnet publish → installer/out/Mahod_Intergreen_Setup_<ver>.exe
  7. Record           docs/releases/RELEASE_BUILD_<rev>.txt with every hash.

Real-host smokes (scripts/realhost_*.py) are run by the caller AFTER this, against the updated dist.
"""
import hashlib, json, os, re, shutil, subprocess, sys, zipfile
from datetime import datetime
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = Path(__file__).resolve().parents[1]
DIST = REPO / "dist/Mahod.Intergreen.bundle"
PROPS = REPO / "build/MahodRelease.props"
ACCEPTED_R11_SOURCE = "02c7a47"
LOCKED = ["src/Mahod.Intergreen.Contracts", "src/Mahod.Intergreen.Core", "src/Mahod.Intergreen.Reporting",
          "src/Mahod.Intergreen.Rules", "src/Mahod.Intergreen.Excel", "rules"]
FRESH = ["Mahod.Intergreen.AutoCAD.dll", "Mahod.Intergreen.Host.dll", "Mahod.Intergreen.Geometry.dll"]
TFM = {"2026": "net8.0-windows", "2027": "net10.0-windows"}
lines = []


def say(s):
    print(s); lines.append(s)


def fail(s):
    say("[FAIL] " + s); write_record(); sys.exit(1)


def ok(s):
    say("[PASS] " + s)


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def run(cmd, cwd=REPO):
    r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def write_record():
    out = REPO / "docs" / "releases" / f"RELEASE_BUILD_{REV}.txt"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"record → {out}")


REV = sys.argv[1] if len(sys.argv) > 1 else None
if not REV or not re.fullmatch(r"r\d+", REV):
    print("usage: release_build.py r<N>"); sys.exit(2)

say(f"{REV} release build — {datetime.now():%Y-%m-%d %H:%M}")

# 1. revision gate
props = PROPS.read_text(encoding="utf-8")
m = re.search(r"<MahodReleaseRevision>(r\d+)</MahodReleaseRevision>", props)
n = re.search(r"<MahodReleaseRevisionNumber>(\d+)</MahodReleaseRevisionNumber>", props)
eng = re.search(r"<MahodEngineVersion>([\d.]+)</MahodEngineVersion>", props)
if not (m and n and eng) or m.group(1) != REV or n.group(1) != REV[1:]:
    fail(f"MahodRelease.props says {m and m.group(1)}/{n and n.group(1)}, expected {REV}/{REV[1:]}")
VER = f"{eng.group(1)}-{REV}"
ok(f"MahodRelease.props = {VER}")
code, out = run(["git", "status", "--porcelain"])
if out.strip():
    fail("working tree not clean:\n" + out)
code, sha_full = run(["git", "rev-parse", "HEAD"]); SHA = sha_full.strip()
ok(f"source commit {SHA}")

# 2. source gate
code, diff = run(["git", "diff", "--stat", ACCEPTED_R11_SOURCE, "HEAD", "--"] + LOCKED)
if diff.strip():
    fail(f"locked engineering source changed since accepted r11 ({ACCEPTED_R11_SOURCE}):\n{diff}")
ok(f"Contracts/Core/Reporting/Rules/Excel + rules/ SOURCE unchanged since accepted r11 ({ACCEPTED_R11_SOURCE})")
code, gdiff = run(["git", "diff", "--stat", ACCEPTED_R11_SOURCE, "HEAD", "--", "src/Mahod.Intergreen.Geometry"])
say("      Geometry source delta since r11 (re-shipped by decision, r13 §21A + r14 ED-015):\n" + gdiff.rstrip())

# 3. build both hosts with the real SHA
for year in ("2026", "2027"):
    code, out = run(["dotnet", "build", "src/Mahod.Intergreen.AutoCAD2026/Mahod.Intergreen.AutoCAD2026.csproj",
                     "-c", "Release", f"-p:AutoCADVersion={year}", f"-p:MahodGitSha={SHA}", "--nologo"])
    if code != 0 or "Build succeeded" not in out:
        fail(f"host {year} build failed:\n" + out[-2000:])
    ok(f"host {year} build (git-stamped)")

# 4. dist update with a hash-diff gate
before = {p.relative_to(DIST).as_posix(): sha(p) for p in DIST.rglob("*") if p.is_file()}
for year in ("2026", "2027"):
    src_dir = REPO / "src/Mahod.Intergreen.AutoCAD2026/bin" / f"host{year}" / "Release" / TFM[year]
    for name in FRESH:
        s = src_dir / name
        if not s.exists():
            fail(f"fresh build missing {s}")
        shutil.copy2(s, DIST / "Contents" / year / name)
        say(f"      copied {name} -> Contents/{year}  {sha(s)[:16]}")
after = {p.relative_to(DIST).as_posix(): sha(p) for p in DIST.rglob("*") if p.is_file()}
allowed = {f"Contents/{y}/{n}" for y in ("2026", "2027") for n in FRESH} | {"PackageContents.xml"}
changed = sorted(k for k in before if k in after and before[k] != after[k])
added = sorted(set(after) - set(before)); removed = sorted(set(before) - set(after))
bad = [k for k in changed if k not in allowed] + added + removed
if bad:
    fail("dist changed outside the shipped host/Host/Geometry set: " + ", ".join(bad))
for k in sorted(before):
    if k not in allowed:
        pass
ok(f"identity gate: {len([k for k in before if k not in allowed])} locked files in dist byte-identical; changed only {changed}")

# 5. manifest
pc = DIST / "PackageContents.xml"
x = pc.read_text(encoding="utf-8")
x = re.sub(r"<!-- Release-stamped: .*? -->", f"<!-- Release-stamped: {VER}, git {SHA} (regenerate via scripts/release_build.py) -->", x)
x = re.sub(r'AppVersion="[\d.]+"', f'AppVersion="{eng.group(1)}.{REV[1:]}"', x)
x = re.sub(r'Version="[\d.]+"', f'Version="{eng.group(1)}.{REV[1:]}"', x)
x = re.sub(r"release 0\.1\.0-r\d+", f"release {VER}", x)
pc.write_text(x, encoding="utf-8")
ok(f"PackageContents.xml stamped {VER}, git {SHA[:7]}")

# host DLL version metadata
for year in ("2026", "2027"):
    dll = DIST / "Contents" / year / "Mahod.Intergreen.AutoCAD.dll"
    code, info = run(["powershell", "-NoProfile", "-Command",
                      f"$v=(Get-Item '{dll}').VersionInfo; \"$($v.FileVersion)|$($v.ProductVersion)\""])
    info = info.strip()
    if REV not in info or SHA[:7] not in info:
        fail(f"{year} AutoCAD.dll version metadata does not carry {REV} + SHA: {info}")
    ok(f"{year} AutoCAD.dll {info}")

# 6. installer
payload_dir = REPO / "installer/MahodIntergreenSetup/payload"; payload_dir.mkdir(parents=True, exist_ok=True)
payload = payload_dir / "bundle.zip"
with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED) as z:
    for p in sorted(DIST.rglob("*")):
        if p.is_file():
            z.write(p, "Mahod.Intergreen.bundle/" + p.relative_to(DIST).as_posix())
with zipfile.ZipFile(payload) as z:
    names = z.namelist()
    if len(names) != len(after):
        fail(f"payload has {len(names)} entries, dist has {len(after)}")
ok(f"payload bundle.zip == dist ({len(names)} entries) sha256 {sha(payload)}")
out_dir = REPO / "installer/out"
code, pub = run(["dotnet", "publish", "installer/MahodIntergreenSetup", "-c", "Release", "-o", str(out_dir),
                 "-p:IncludeAllContentForSelfExtract=true", f"-p:MahodGitSha={SHA}", "--nologo"])
if code != 0:
    fail("installer publish failed:\n" + pub[-2000:])
exe = out_dir / f"Mahod_Intergreen_Setup_{VER}.exe"
if not exe.exists():
    fail(f"published exe not found: {exe}")
code, info = run(["powershell", "-NoProfile", "-Command",
                  f"$v=(Get-Item '{exe}').VersionInfo; \"$($v.FileVersion)|$($v.ProductVersion)\""])
ok(f"installer {exe.name}  {exe.stat().st_size} bytes  sha256 {sha(exe)}  version {info.strip()}")

say("")
say("SUMMARY")
say(f"  source commit      {SHA}")
say(f"  release            {VER}")
say(f"  installer          {exe}")
say(f"  installer sha256   {sha(exe)}")
say(f"  payload sha256     {sha(payload)} ({len(names)} entries)")
for year in ("2026", "2027"):
    for name in FRESH:
        say(f"  {year} {name:<32} {sha(DIST / 'Contents' / year / name)}")
say("  RESULT             PASS")
write_record()
