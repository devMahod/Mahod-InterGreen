# מדריך משתמש מאויר — Mahod Intergreen (דוד)

> **סטטוס צילומי המסך: `AWAITING_ARTHUR_GUI_SMOKE`** — כל המקומות המסומנים
> `[SCREENSHOT NN — …]` הם צילומי מסך **אמיתיים** שייקלטו במהלך מבחן ה-GUI של ארתור
> (`TO BE CAPTURED DURING ARTHUR GUI SMOKE`). אין במסמך זה אף צילום מפוברק/מדומה.
> רשימת הקליטה המדויקת: `SCREENSHOT_CAPTURE_CHECKLIST_HE.md`.

> **הגרסה הנוכחית אינה מציירת אוטומטית את מסלולי התנועה. הגיאומטריה הקיימת ב-DWG היא קלט
> הנדסי שהמהנדס מכין. מרגע שהמסלולים קיימים, הכלי מבצע אוטומטית את איתור הקונפליקטים,
> המדידות, החישובים, הבקרה והמטריצה/Excel.**

---

## שלב 1 — פתיחת הפרויקט

פתחו ב-AutoCAD 2026 את שרטוט הבין-ירוקים (בערכת הבדיקה: דוגמה 1, פינס–בעל שם טוב).

`[SCREENSHOT 01 — AutoCAD with Example 1 open and INTERGREEN command visible]`

## שלב 2 — הפעלת הפקודה

בשורת הפקודה הקלידו `INTERGREEN` ואשרו.

`[SCREENSHOT 02 — INTERGREEN command launch at the AutoCAD command line]`

## שלב 3 — הפאלטה

נפתחת פאלטת "MAHOD INTERGREEN" ובה הכפתורים Setup / Validate / Analyze / Show / Export.

`[SCREENSHOT 03 — Mahod Intergreen palette, all actions visible]`

## שלב 4 — Setup

בחרו את קובץ האקסל של הפרויקט. ההגדרות נשמרות ליד השרטוט (sidecar) — פעם אחת בלבד.

`[SCREENSHOT 04 — Setup screen with the Example 1 source workbook selected]`

## שלב 5 — אישור יחידות

אם השרטוט ללא יחידות (INSUNITS=Unitless) — אישור חד-פעמי שהשרטוט במטרים.

`[SCREENSHOT 05 — units confirmation]`

## שלב 6 — מיפוי וסיווג

השלמת מיפוי תנועות/קבוצות רמזור/קווי עצירה במקומות שבהם הכלי מבקש זאת.

`[SCREENSHOT 06 — mapping/classification/stop-line setup where visible]`

## שלב 7 — Validate

לחצו Validate. צפוי בדוגמה 1: 11 תנועות (מהן 4 מעברי חצייה), 0 שגיאות.

`[SCREENSHOT 07 — Validate summary]`

## שלב 8 — Analyze

לחצו Analyze. צפוי בדוגמה 1: 50 קונפליקטים; קובץ `analysis.json` נכתב ליד השרטוט.

`[SCREENSHOT 08 — Analyze result]`

## שלב 9 — המטריצה

בדוגמה 1 המטריצה צפויה להיות **24 VALID / 0 REVIEW_REQUIRED / 0 BLOCKED**.

`[SCREENSHOT 09 — Example 1 matrix showing 24/0/0]`

## שלב 10 — רשימת הקונפליקטים

כל זוג תנועות: סטטוס, IG סופי, מספר נקודות מועמד.

`[SCREENSHOT 10 — conflict list]`

## שלב 11 — שורה 82 (ממצא הנדסי: 4 → 5)

בחרו את הקונפליקט `W-L → S-T`. המנוע מנפיק 5 שניות (ידני היסטורי: 4) — המועמד הקובע הוא
מועמד-סיום `W-L.b2@end` ‏(CD ≈ 28.52 מ') שלא נמדד ידנית. ממצא שמרני/בטוח לדיון עם דוד.

`[SCREENSHOT 11 — Row 82 (W-L→S-T) selected, Final IG 5 visible]`

## שלב 12 — Show לשורה 82

לחצו Show: זום + עיגולים ענבריים (מועמדים) ועיגול אדום (הנקודה הקובעת).

`[SCREENSHOT 12 — SHOW for Row 82 in the drawing]`

## שלב 13 — שורה 89 (ממצא הנדסי: 6 → 7)

בחרו את `a → S-T` ולחצו Show. המועמד הקובע: קצה מעבר החצייה a בקו העצירה של S-T‏
(ED = 0). המנוע: 7 שניות (ידני: 6).

`[SCREENSHOT 13 — Row 89 (a→S-T) selected / shown]`

## שלב 14 — יצוא Excel

לחצו Export Excel עם קובץ המקור של דוגמה 1.

`[SCREENSHOT 14 — Export Excel action]`

## שלב 15 — הקובץ שנוצר

פתחו את `*_MAHOD_INTERGREEN.xlsx`: כל הגיליונות המקוריים קיימים, באותו סדר, ללא הודעת
תיקון של Excel. קובץ המקור לא השתנה.

`[SCREENSHOT 15 — generated workbook, original sheets preserved]`

## שלב 16 — גיליון MAHOD Engine Results

כל הנקודות + עקיבות מלאה (המקור האמין).

`[SCREENSHOT 16 — MAHOD Engine Results sheet]`

## שלב 17 — גיליון MAHOD Matrix Status

סטטוס מפורש לכל תא במטריצה.

`[SCREENSHOT 17 — MAHOD Matrix Status sheet]`

## שלב 18 — שמירת פרויקט

סגרו ופתחו מחדש את השרטוט → `INTERGREEN` → ‏Validate ישירות, בלי Setup חוזר.

`[SCREENSHOT 18 — close/reopen showing sidecar persistence]`

## שלב 19 — דוגמה 2: REVISION_MISMATCH

פתחו את דוגמה 2 (אברבנאל–המכבים). הכלי מזהה אוטומטית שה-DWG אינו גרסת האקסל ומסמן
REVISION_MISMATCH (בדיקת עמידות, לא קבלה גיאומטרית).

`[SCREENSHOT 19 — Example 2 REVISION_MISMATCH indication]`

## שלב 20 — מטריצת דוגמה 2

צפוי: **37 VALID / 7 REVIEW_REQUIRED / 0 BLOCKED**; מעבר `b` (6 קטעים) מחושב ואינו חסום.

`[SCREENSHOT 20 — Example 2 matrix 37/7/0]`
