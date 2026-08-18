# PACKAGECONTENTS_VALIDATION

- XML well-formed (parsed); one ApplicationPackage, ProductCode unchanged.
- Two Components, each with `RuntimeRequirements OS="Win64" Platform="AutoCAD*"`:
  - SeriesMin/SeriesMax R25.1 -> ModuleName ./Contents/2026/Mahod.Intergreen.AutoCAD.dll
  - SeriesMin/SeriesMax R26.0 -> ModuleName ./Contents/2027/Mahod.Intergreen.AutoCAD.dll
- `Platform="AutoCAD*"` covers AutoCAD-based verticals incl. Civil 3D (the previous
  exact `Platform="AutoCAD"` would have excluded Civil — fixed in this round).
- Both ModuleName targets exist in the shipped bundle (checked at packaging).
- Wrong-year loading prevented by exact SeriesMin=SeriesMax pinning per component.
- Real-runtime load evidence: explicit NETLOAD of each year's module in that year's own
  console (2026 console; 2027 Civil-flavored console via trusted ApplicationPlugins path).
  Autoload-by-bundle is exercised by the GUI smokes (Lin 2026 — in flight; Arthur Civil
  2027 — pending).
