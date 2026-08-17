# התקנת Mahod Intergreen — פיילוט

## דרישות
- Windows 11, AutoCAD 2026 (מלא — לא LT).
- ‎.NET 8 Desktop Runtime (מגיע עם AutoCAD 2026).
- **אין צורך בהרשאות מנהל** — ההתקנה ברמת המשתמש בלבד.

## איפה המתקין
בתוך חבילת הפיילוט: תיקיית **`07_AUTOCAD_BUNDLE`**
(בעותק הפיתוח: `dist/` בתוך ריפו המקור — אותם קבצים בדיוק).

## התקנה
1. פתחו PowerShell בתיקיית `07_AUTOCAD_BUNDLE`.
2. הריצו:
   `powershell -ExecutionPolicy Bypass -File .\INSTALL_MAHOD_INTERGREEN.ps1`
3. הפעילו את AutoCAD 2026 (הפעלה ראשונה: הפלאגין נטען אוטומטית).
4. בשורת הפקודה הקלידו `INTERGREEN` — נפתחת פאלטת "MAHOD INTERGREEN".

## מה מותקן
Bundle סטנדרטי של Autodesk תחת `%APPDATA%\Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle`
— קבצי Mahod בלבד (אין קבצי Autodesk בחבילה). קבצי החוקים (Rule Packs) כלולים תחת
`Contents\rules`.

## הסרה / ניקוי
מאותה תיקייה:
`powershell -ExecutionPolicy Bypass -File .\UNINSTALL_MAHOD_INTERGREEN.ps1`
(מוחק את תיקיית ה-bundle מ-ApplicationPlugins; לא נוגע בשרטוטים, בקבצי sidecar או באקסלים.)

## פתרון תקלות
- **הפקודה `INTERGREEN` לא מוכרת**: ודאו ש-AutoCAD הופעל מחדש אחרי ההתקנה; ודאו שהתיקייה
  `%APPDATA%\Autodesk\ApplicationPlugins\Mahod.Intergreen.bundle` קיימת ומכילה את
  `PackageContents.xml`. אם הקבצים הגיעו מהאינטרנט — ייתכן חסימת Windows: קליק ימני על
  ה-ZIP המקורי → Properties → Unblock, וחילוץ מחדש.
- **הפלאגין לא נטען (אין הודעת טעינה)**: בדקו ש-SECURELOAD מאפשר טעינה מנתיב
  ApplicationPlugins (ברירת המחדל מאפשרת), וש-AutoCAD הוא 2026 מלא (לא LT).
- **הפאלטה נפתחת ריקה/שגיאה**: ודאו ‎.NET 8 Desktop Runtime מותקן (`dotnet --list-runtimes`
  אמור לכלול `Microsoft.WindowsDesktop.App 8.x`).
