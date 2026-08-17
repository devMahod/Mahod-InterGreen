# installer/ — Mahod_Intergreen_Setup_0.1.0.exe (packaging layer)

Self-contained .NET 8 WinForms setup app wrapping the VALIDATED `dist/` bundle.
It never builds or references engineering assemblies (productization directive §6).

Build (payload + publish, from the repo root):

```bash
python - <<'PY'
import zipfile, pathlib
d = pathlib.Path('dist/Mahod.Intergreen.bundle')
o = pathlib.Path('installer/MahodIntergreenSetup/payload'); o.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(o / 'bundle.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(d.rglob('*')):
        if p.is_file():
            z.write(p, 'Mahod.Intergreen.bundle/' + p.relative_to(d).as_posix())
PY
dotnet publish installer/MahodIntergreenSetup -c Release -o installer/out -p:IncludeAllContentForSelfExtract=true
```

Modes: GUI (default) · `/silent` · `/uninstall` · `/skipacadcheck` (automated file-level
validation only — never documented to end users).

Validation record: `docs/INSTALLER_BUILD_AND_FILE_VALIDATION.md` (16/16 PASS, 2026-08-17).
`payload/` and `out/` are generated — not committed (see `.gitignore`).
