# RUNTIME_DEPENDENCY_CLOSURE_2027

Runner: net10.0-windows + WindowsDesktop framework reference (AutoCAD-faithful), Private=false refs
(nothing resolves from bin/NuGet/SDK). Executed from (a) a clean temp copy of the exact
`Contents/2027` shipping payload and (b) the installed
ApplicationPlugins `Contents/2027` path after installer r4.

Both: WorkbookReader on the ORIGINAL Example-1 workbook + full pipeline -> golden numbers
(11 / 50 / 24-0-0 / IG 5). ClosedXML, SixLabors.Fonts, RBush loaded FROM the 2027
shipping folder (paths recorded). The pre-r4 ClosedXML dependency fix is preserved in
both payloads. Mutation-proof gate: removing SixLabors.Fonts fails the run.
