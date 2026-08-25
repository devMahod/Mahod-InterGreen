# Defender false positives on our installers

## What happened

2026-08-25, `Mahod_Parking_Setup_0.4.2.exe` was quarantined as **`Trojan:Win32/Wacatac.B!ml`**,
severity Severe, and Windows refused to run it on a client machine ("the file contains a virus or
potentially unwanted software"). The same detection had already fired on `0.4.0` and on a second
copy of `0.4.2` the same morning.

The `!ml` suffix means the verdict came from a cloud machine-learning model, not from a signature
match on known malware. `Wacatac` is the generic bucket those models use for "unsigned executable,
first seen minutes ago, unpacks something and writes it into a user profile" — which is a literal
description of a freshly built installer. It is a statement about reputation, not about the code.

## Does Intergreen have the same exposure? Yes

| | our installer |
|---|---|
| Authenticode signature | **`NotSigned`** |
| Shape | self-contained single-file .NET WinForms exe, ~73 MB, with an embedded `bundle.zip` |
| At install time | extracts into `%APPDATA%\Autodesk\ApplicationPlugins`, copies itself to `Uninstall_Mahod_Intergreen.exe`, writes an HKCU Add/Remove-Programs key |
| Filename | changes every release (`…_r11`, `…_r12`, `…_r13`) — reputation never accumulates on one name |

That is the same profile as the Parking installer. `Mahod_Intergreen_Setup_r13.exe` scans clean on
this machine today (`MpCmdRun -Scan -ScanType 3` → "found no threats", file intact), and Lin
installed r12 and r13 without being blocked — but a local clean scan is not protection. The verdict
is made in Microsoft's cloud, per machine and per definition update, so the next recipient can be
blocked by the same build that worked here an hour earlier.

## The installer is optional

It does three things, and only the first is required for the product to work:

1. copy `Mahod.Intergreen.bundle` into `%APPDATA%\Autodesk\ApplicationPlugins` — Autodesk loads
   plugins from that folder, nothing else is consulted;
2. copy itself as an uninstaller;
3. write an Add/Remove-Programs entry.

So a package with no executable in it installs exactly the same product. `scripts` note: a `.bat`
or `.ps1` "convenience installer" would re-introduce the problem — a script that copies into a
profile location is itself a heuristic trigger. Plain instructions are safer.

## The installer-less package

`Mahod_Intergreen_r13_ללא_מתקין.zip` — sha256
`e129a540884d53c21b4f28185515920e232d4be9c969355576fdb96d13b5d9df`, 5,851,149 bytes
(the installer is 73 MB because it carries the .NET runtime; this carries only the product).

- 49 entries: the 47 bundle files, the Hebrew guide, and `קרא_אותי_התקנה.txt`;
- **no `.exe`, `.msi`, `.bat`, `.cmd`, `.ps1`, `.scr`, `.com`, `.vbs`, `.js`, `.lnk`** — the build
  script (`scratchpad/build_nozip_installerless.py`) refuses to write the file if any appear;
- every bundle file is hash-checked against the dist that passed acceptance, so this is the tested
  build and not a lookalike;
- `MpCmdRun -Scan -ScanType 3` → no threats, file intact afterwards.

Install is: close Civil 3D → paste `%APPDATA%\Autodesk\ApplicationPlugins` into Explorer's address
bar → drag the folder in → reopen. Uninstall is deleting the folder.

## What still needs deciding

1. **Code signing** ends this class of problem and is a purchase: an OV certificate (~$200–400/yr)
   removes `NotSigned`, an EV certificate additionally carries SmartScreen reputation from the first
   download. Sign the DLLs and the installer, and always timestamp (`signtool … /tr`) so signatures
   outlive the certificate.
2. **Report the Parking detection to Microsoft** at https://www.microsoft.com/en-us/wdsi/filesubmission
   → "Software developer" → *"I believe this file is incorrectly detected"*. Free, typically 24–72 h,
   and the correction reaches every client through Defender's cloud without them doing anything.
3. **Keep the installer filename stable** across releases so reputation can accumulate on one name
   instead of resetting every revision.

## What we never do

We do not ask a client to disable Defender, restore a file from quarantine, add an exclusion or
click "Run anyway". It teaches them to wave away security warnings, it does not survive the next
Windows update, and it makes our tool the prime suspect the next time anything on that machine
misbehaves. We ship something that does not trip the alarm instead.
