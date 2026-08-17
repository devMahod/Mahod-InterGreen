# התקנת Mahod Intergreen — פיילוט

## דרישות
- Windows 11, AutoCAD 2026 (מלא — לא LT).
- ‎.NET 8 Desktop Runtime (מגיע עם AutoCAD 2026).

## התקנה (ללא הרשאות מנהל)
1. פתחו PowerShell בתיקיית `dist`.
2. הריצו: `powershell -ExecutionPolicy Bypass -File .\INSTALL_MAHOD_INTERGREEN.ps1`
3. הפעילו את AutoCAD 2026. הפקודה `INTERGREEN` זמינה.

## הסרה
`powershell -ExecutionPolicy Bypass -File .\UNINSTALL_MAHOD_INTERGREEN.ps1`

## מה מותקן
Bundle סטנדרטי של Autodesk תחת `%APPDATA%\Autodesk\ApplicationPlugins` — קבצי Mahod בלבד
(אין קבצי Autodesk בחבילה). קבצי החוקים (Rule Packs) כלולים תחת `Contents\rules`.
