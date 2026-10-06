# -*- coding: utf-8 -*-
"""Release build with gates — the procedure that produced r10..r15, written down and committed.

    <usage-key wrapper> python scripts/release_build.py r15          (MAHOD_CAD_KEY set: the shipped build)
    python scripts/release_build.py r15 --keyless                    (a rehearsal: no Mahod Impact key)

What it does, in order, failing closed at every step:

  1. Revision gate    build/MahodRelease.props must say the requested revision; the tree must be clean.
  2. Source gate      the five locked engineering assemblies (Contracts, Core, Reporting, Rules, Excel) and
                      rules/ are byte-for-byte the accepted r11 source (commit 02c7a47). Geometry is the one
                      engineering assembly that moved (r13 §21A, r14 ED-015) — by recorded decision.
  3. Build            both host flavours, Release, --no-incremental, stamped with the real git SHA (never
                      UNSTAMPED-DEV). From r15 the host compiles the Mahod Impact usage recorder and bakes the
                      products' usage key in from MAHOD_CAD_KEY (build/MahodUsageKey.targets).
  4. Stage            r15+: THIS REPO IS PUBLIC and the host DLL now carries the usage key, so the shipped
                      bundle is no longer written into the tracked dist/. dist/ stays the last keyless bundle
                      (r14) and is the identity baseline; the release bundle is staged under the ignored
                      build/out/<rev>/Mahod.Intergreen.bundle: a copy of dist with the AutoCAD / Host /
                      Geometry DLLs replaced; every other file is hash-checked unchanged (the locked DLLs stay
                      the accepted bytes).
  5. Manifest         the staged PackageContents.xml stamped with the revision, version and SHA.
  6. Key gate         the key is present in both staged host DLLs and in no other staged file, and in no
                      tracked file of the repo. Checked as booleans only — the key is never printed.
  7. Installer        payload/bundle.zip == stage; dotnet publish → installer/out/Mahod_Intergreen_Setup_<ver>.exe
  8. Record           docs/releases/RELEASE_BUILD_<rev>.txt with every hash (never the key).

Real-host smokes (scripts/realhost_*.py) are run by the caller AFTER this, against the staged bundle.
NEVER commit build/out/, installer/out/ or installer/MahodIntergreenSetup/payload/ — they carry the key.
"""
import hashlib, os, re, shutil, subprocess, sys, zipfile
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
KEYED = "Mahod.Intergreen.AutoCAD.dll"
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
KEYLESS = "--keyless" in sys.argv[2:]
if not REV or not re.fullmatch(r"r\d+", REV):
    print("usage: release_build.py r<N> [--keyless]"); sys.exit(2)
STAGE_ROOT = REPO / "build/out" / REV
STAGE = STAGE_ROOT / "Mahod.Intergreen.bundle"

# The products' usage key: from the environment of THIS process only (the key wrapper sets it).
KEY = (os.environ.get("MAHOD_CAD_KEY") or "").strip()


def holds_key(data: bytes) -> bool:
    # A C# string literal lives in the assembly's #US heap as UTF-16LE; UTF-8 covers any other file.
    return bool(KEY) and (KEY.encode("utf-16-le") in data or KEY.encode("utf-8") in data)


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
if KEY:
    if not re.fullmatch(r"[A-Za-z0-9_-]{16,128}", KEY):
        fail("MAHOD_CAD_KEY is not a valid products' key (value not shown)")
    ok("MAHOD_CAD_KEY present in the build environment (value not shown) — the usage key will be baked in")
elif KEYLESS:
    say("[WARN] --keyless: NO Mahod Impact usage key — usage from PCs without MahodAI will queue and never be sent. Not for shipping.")
else:
    fail("MAHOD_CAD_KEY is not set: a release build carries the products' usage key. Run it under the key "
         "wrapper (never type the key), or pass --keyless for a rehearsal.")

# 2. source gate
code, diff = run(["git", "diff", "--stat", ACCEPTED_R11_SOURCE, "HEAD", "--"] + LOCKED)
if diff.strip():
    fail(f"locked engineering source changed since accepted r11 ({ACCEPTED_R11_SOURCE}):\n{diff}")
ok(f"Contracts/Core/Reporting/Rules/Excel + rules/ SOURCE unchanged since accepted r11 ({ACCEPTED_R11_SOURCE})")
code, gdiff = run(["git", "diff", "--stat", ACCEPTED_R11_SOURCE, "HEAD", "--", "src/Mahod.Intergreen.Geometry"])
say("      Geometry source delta since r11 (re-shipped by decision, r13 §21A + r14 ED-015):\n" + gdiff.rstrip())

# 3. build both hosts with the real SHA (no incremental build: a keyed and a keyless build must never share obj/)
for year in ("2026", "2027"):
    code, out = run(["dotnet", "build", "src/Mahod.Intergreen.AutoCAD2026/Mahod.Intergreen.AutoCAD2026.csproj",
                     "-c", "Release", "--no-incremental", f"-p:AutoCADVersion={year}", f"-p:MahodGitSha={SHA}", "--nologo"])
    if KEY:
        out = out.replace(KEY, "<MAHOD_CAD_KEY>")
    if code != 0 or "Build succeeded" not in out:
        fail(f"host {year} build failed:\n" + out[-2000:])
    ok(f"host {year} build (git-stamped)")

# 4. stage = dist + fresh host DLLs, with a hash-diff gate against dist
if STAGE_ROOT.exists():
    shutil.rmtree(STAGE_ROOT)
shutil.copytree(DIST, STAGE)
before = {p.relative_to(DIST).as_posix(): sha(p) for p in DIST.rglob("*") if p.is_file()}
for year in ("2026", "2027"):
    src_dir = REPO / "src/Mahod.Intergreen.AutoCAD2026/bin" / f"host{year}" / "Release" / TFM[year]
    for name in FRESH:
        s = src_dir / name
        if not s.exists():
            fail(f"fresh build missing {s}")
        shutil.copy2(s, STAGE / "Contents" / year / name)
        say(f"      staged {name} -> Contents/{year}  {sha(s)[:16]}")
after = {p.relative_to(STAGE).as_posix(): sha(p) for p in STAGE.rglob("*") if p.is_file()}
allowed = {f"Contents/{y}/{n}" for y in ("2026", "2027") for n in FRESH} | {"PackageContents.xml"}
changed = sorted(k for k in before if k in after and before[k] != after[k])
added = sorted(set(after) - set(before)); removed = sorted(set(before) - set(after))
bad = [k for k in changed if k not in allowed] + added + removed
if bad:
    fail("stage differs from dist outside the shipped host/Host/Geometry set: " + ", ".join(bad))
ok(f"identity gate: {len([k for k in before if k not in allowed])} locked files in the stage byte-identical to dist; "
   f"changed only {changed}")
code, dist_status = run(["git", "status", "--porcelain", "--", "dist"])
if dist_status.strip():
    fail("the tracked dist/ was modified by the build:\n" + dist_status)
ok(f"tracked dist/ untouched (stays the keyless r14 bundle); release bundle staged at {STAGE.relative_to(REPO).as_posix()}")

# 5. manifest
pc = STAGE / "PackageContents.xml"
x = pc.read_text(encoding="utf-8")
x = re.sub(r"<!-- Release-stamped: .*? -->", f"<!-- Release-stamped: {VER}, git {SHA} (regenerate via scripts/release_build.py) -->", x)
x = re.sub(r'AppVersion="[\d.]+"', f'AppVersion="{eng.group(1)}.{REV[1:]}"', x)
x = re.sub(r'Version="[\d.]+"', f'Version="{eng.group(1)}.{REV[1:]}"', x)
x = re.sub(r"release 0\.1\.0-r\d+", f"release {VER}", x)
pc.write_text(x, encoding="utf-8")
stale = sorted(set(re.findall(r"0\.1\.0[.-]r?\d+", x)) - {f"{eng.group(1)}.{REV[1:]}", VER})
if stale:
    fail(f"staged PackageContents.xml still carries another revision: {stale}")
ok(f"PackageContents.xml stamped {VER}, git {SHA[:7]}")

# host DLL version metadata
for year in ("2026", "2027"):
    dll = STAGE / "Contents" / year / KEYED
    code, info = run(["powershell", "-NoProfile", "-Command",
                      f"$v=(Get-Item '{dll}').VersionInfo; \"$($v.FileVersion)|$($v.ProductVersion)\""])
    info = info.strip()
    if REV not in info or SHA[:7] not in info:
        fail(f"{year} AutoCAD.dll version metadata does not carry {REV} + SHA: {info}")
    ok(f"{year} AutoCAD.dll {info}")

# 6. key gate (booleans only)
if KEY:
    for year in ("2026", "2027"):
        if not holds_key((STAGE / "Contents" / year / KEYED).read_bytes()):
            fail(f"{year} {KEYED}: usage key NOT baked in")
        ok(f"{year} {KEYED}: usage key baked in (checked as a boolean)")
    others = [p.relative_to(STAGE).as_posix() for p in STAGE.rglob("*")
              if p.is_file() and p.name != KEYED and holds_key(p.read_bytes())]
    if others:
        fail("usage key found outside the host DLLs: " + ", ".join(others))
    ok("usage key in no other staged file")
    code, tracked = run(["git", "ls-files", "-z"])
    leaked = [f for f in tracked.split("\0") if f and (REPO / f).is_file() and holds_key((REPO / f).read_bytes())]
    if leaked:
        fail("usage key found in TRACKED files of this public repo: " + ", ".join(leaked))
    ok("usage key in no tracked file of the repo")
else:
    say("[WARN] keyless build — no key gate")

# 7. installer
payload_dir = REPO / "installer/MahodIntergreenSetup/payload"; payload_dir.mkdir(parents=True, exist_ok=True)
payload = payload_dir / "bundle.zip"
with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED) as z:
    for p in sorted(STAGE.rglob("*")):
        if p.is_file():
            z.write(p, "Mahod.Intergreen.bundle/" + p.relative_to(STAGE).as_posix())
with zipfile.ZipFile(payload) as z:
    names = z.namelist()
    if len(names) != len(after):
        fail(f"payload has {len(names)} entries, stage has {len(after)}")
    for name in names:
        rel = name[len("Mahod.Intergreen.bundle/"):]
        if hashlib.sha256(z.read(name)).hexdigest() != sha(STAGE / rel):
            fail(f"payload entry differs from the stage: {name}")
        if KEY and rel.endswith("/" + KEYED) and not holds_key(z.read(name)):
            fail(f"payload {name}: usage key NOT baked in")
ok(f"payload bundle.zip == stage ({len(names)} entries, hash by hash) sha256 {sha(payload)}")
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
if REV not in info:
    fail(f"installer version metadata does not carry {REV}: {info.strip()}")
ok(f"installer {exe.name}  {exe.stat().st_size} bytes  sha256 {sha(exe)}  version {info.strip()}")

say("")
say("SUMMARY")
say(f"  source commit      {SHA}")
say(f"  release            {VER}")
say(f"  usage key baked    {'yes (2026 + 2027 host DLLs)' if KEY else 'NO (--keyless rehearsal)'}")
say(f"  staged bundle      {STAGE.relative_to(REPO).as_posix()} (ignored; never commit)")
say(f"  installer          {exe.relative_to(REPO).as_posix()}")
say(f"  installer sha256   {sha(exe)}")
say(f"  payload sha256     {sha(payload)} ({len(names)} entries)")
for year in ("2026", "2027"):
    for name in FRESH:
        say(f"  {year} {name:<32} {sha(STAGE / 'Contents' / year / name)}")
say(f"  PackageContents    {sha(STAGE / 'PackageContents.xml')}")
say("  RESULT             PASS")
write_record()
