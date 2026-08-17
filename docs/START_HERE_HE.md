# התחילו כאן — Mahod Intergreen, חבילת פיילוט 0.1.1

## סטטוס נוכחי

> **READY FOR ARTHUR GUI SMOKE**

- גרסת חבילה: **0.1.1** (יישור תיעוד/אריזה בלבד).
- גרסת המנוע/פלאגין הבינארית: **ללא שינוי מ-0.1.0** (עברה שתי ביקורות בלתי-תלויות:
  ‏210/210 שוחזר ע"י Claude Web; המנוע והארטיפקטים נסקרו ע"י GPT).
- החבילה **אינה** מאושרת לדוד. הסטטוס `APPROVED FOR DAVID PILOT` יינתן רק אחרי שערי
  האדם שבהמשך.

## 1. מה החבילה הזאת?

חבילת מועמד-פיילוט של **Mahod Intergreen** — מנוע דטרמיניסטי לחישוב זמנים בין-ירוקים
בצמתים מרומזרים, עם מארח AutoCAD 2026 (פקודת `INTERGREEN` + פאלטה) ויצוא לאקסל של דוד.

## 2. מה הכלי עושה?

מ-DWG עם גיאומטריית תנועות קיימת: איתור כל הקונפליקטים, מדידת CD/ED מדויקת (קווים/קשתות,
כולל מועמדי-סיום), חישוב לפי Legacy (מאומת 148/148) או לפי הנחיות 2025, בקרה מלאה
(סטטוס מפורש לכל תוצאה), מטריצת קבוצות רמזור, ויצוא Excel ששומר את קובץ המקור.

## 3. מה הכלי לא עושה?

- **לא מצייר אוטומטית את מסלולי התנועה** — הגיאומטריה היא קלט הנדסי שהמהנדס מכין
  (Assisted Drawing = עתידי בלבד).
- לא מנחש יחידות/סיווג/קווי עצירה/רוחב מעבר — מפורש או חסום.
- לא מזין לענבר (אין API).
- מלוא הפירוט: `09_DAVID_DOCUMENTATION_HE/KNOWN_LIMITATIONS_HE.md`.

## 4. איפה המתקין?

`07_AUTOCAD_BUNDLE/` — הוראות מלאות: `09_DAVID_DOCUMENTATION_HE/README_INSTALL_HE.md`.

## 5. איפה ההתחלה המהירה?

`09_DAVID_DOCUMENTATION_HE/QUICK_START_DAVID_HE.md`.

## 6. איפה המדריך המלא?

`09_DAVID_DOCUMENTATION_HE/USER_GUIDE_HE.md` (טקסט סמכותי) +
`09_DAVID_DOCUMENTATION_HE/USER_GUIDE_DAVID_ILLUSTRATED_HE.md` (מאויר — צילומים ייקלטו
במבחן של ארתור).

## 7. איפה מבחן ה-Smoke של ארתור?

`09_DAVID_DOCUMENTATION_HE/CAD_SMOKE_TEST.md` (הנוהל, 31 צעדים) +
`09_DAVID_DOCUMENTATION_HE/ARTHUR_GUI_SMOKE_RECORD_HE.md` (טופס הרישום) +
`09_DAVID_DOCUMENTATION_HE/SCREENSHOT_CAPTURE_CHECKLIST_HE.md` (צילומי המסך).

## 8. איפה הצ'קליסט של לין?

`09_DAVID_DOCUMENTATION_HE/LIN_PREPILOT_CHECKLIST_HE.md` +
`09_DAVID_DOCUMENTATION_HE/PILOT_FEEDBACK_HE.md`.

## 9. איפה ערכת הבדיקה?

`08_PILOT_TEST_KIT/` — קלטי המקור המקוריים (DWG + Excel) לשתי הדוגמאות, עם hash לכל
קובץ, תוצרי ייחוס, ותיקיית עבודה. הוראות: `08_PILOT_TEST_KIT/README_TEST_KIT_HE.md`.

## 10. איפה הראיות וההדוחות ההנדסיים?

- `00_START_HERE/FINAL_PILOT_VERIFICATION_REPORT.md` — כל המספרים.
- `04_EXAMPLE1_FINAL/` — דוגמה 1 (כולל `RESIDUAL_CLASSIFICATION.md`: ‏12 שאריות, 10 שוות,
  2 ‏engine-higher — שורות 82 ו-89).
- `05_EXAMPLE2_FINAL/` — דוגמה 2 ‏(REVISION_MISMATCH, מטריצה 37/7/0).
- `02_TEST_RESULTS/`, ‏`03_RULES_AND_OFFICIAL_SOURCES/`, ‏`06_EXCEL_OUTPUTS/`.
- ביקורת סבב 0.1.1: `00_START_HERE/BINARY_IDENTITY_0.1.0_vs_0.1.1.md`,
  ‏`00_START_HERE/SOURCE_CHANGE_AUDIT_0.1.0_vs_0.1.1.md`,
  ‏`00_START_HERE/0.1.1_COMPLIANCE_MATRIX.md`.

## 11. איפה חבילת האינטגרציה לוודים (Civil/MahodAI)?

`10_VADIM_INTEGRATION/` — ‏`INTEGRATION_VADIM.md`, ‏`API_EXAMPLES_VADIM.md`,
‏`VADIM_IMPLEMENTATION_CHECKLIST_HE.md`, ‏`CIVIL_INTEGRATION_BOUNDARY.md`,
‏`RESULT_SCHEMA.md`. ‏(Civil לא ממומש בפיילוט; אותו מנוע ישרת אותו בעתיד.)

## 12. מה הסטטוס ומה הסדר מכאן?

```text
0.1.1 independent documentation/package verification
        ↓
Arthur real AutoCAD GUI smoke + real screenshots
        ↓
replace screenshot placeholders with real captures
        ↓
Lin internal pre-pilot
        ↓
fix only genuine pilot findings if any
        ↓
David pilot / engineering review
        ↓
only after pilot approval:
prepare clean David end-user package
and separately proceed with Vadim/Civil host implementation
```

אין להפוך את הסדר. אין למסור לדוד ריפו הנדסי (חבילת דוד הנקייה תיגזר אחרי שלין עוברת).
אין לממש Civil לפני שהפיילוט ב-AutoCAD מאומת.
