# RUNTIME_DEPENDENCY_CLOSURE — permanent release gate (r3 bundle)
Method: closure runner (net8.0-windows + WindowsDesktop framework = AutoCAD-faithful;
Private=false references so NOTHING resolves from bin/NuGet/SDK), executed from
(a) a clean temp copy of the exact shipping Contents and (b) the installed
ApplicationPlugins path. Workload: WorkbookReader on the ORIGINAL Example-1 workbook +
the full Example-1 pipeline + forced load of lazy dependencies.

Result (both locations): movements=11, conflicts=50, VALID=24, REVIEW_REQUIRED=0,
BLOCKED=0, W-L to S-T = 5. ClosedXML / SixLabors.Fonts / RBush all loaded FROM the
shipping folder (paths recorded in run output). New Mahod.Intergreen.Host.dll references
only the framework + Mahod.Intergreen.Excel — no new third-party dependencies.
System.IO.Packaging comes from the WindowsDesktop runtime (present inside AutoCAD 2026).
Mutation proof: removing SixLabors.Fonts.dll from the shipping copy fails the gate (M2).
