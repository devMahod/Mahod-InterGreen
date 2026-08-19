# OPEN_QUESTIONS

Questions that change engineering numbers. Nothing here may be resolved silently in code.

## OQ-001 — Does the 3-second minimum apply to the Legacy pack?

§5.6 (June 2025) mandates `T = max(3, …)`. The previous guidelines edition is not in the
supplied material, and no golden row falls below 3 s, so the data cannot answer it.
**Current behaviour**: minimum applied only in the 2025 policy; Legacy policy has none.
(Addendum §B.5.) → **David**

## OQ-002 — 2025 geometry: centreline per lane

§5.4: "ציר מסלול הנסיעה מסומן באמצע מסלול הנסיעה... נקודת ניגוד היא הנקודה בה שתי תנועות
נוגדות נפגשות (מפגש צירי מסלולי הנסיעה)". The worked examples generate 4/2/3 points from
lane counts, not from envelope boundaries. Existing Mahod DWGs contain envelopes only.
Is Mahod prepared to register/draw a centreline per lane for 2025 work? → **David**

## OQ-003 — Reachable destination lanes (2025 pairing)

Example ג' explicitly assumes the conservative case: "מתוך הנחה מחמירה שהרכב הפונה ימינה יכול
לפנות לכל אחד משני הנתיבים". Default = all destination lanes reachable, overridable per
movement in the project sidecar (Addendum §E.1). Confirm the default and when to override. → **David**

## OQ-004 — Same-signal-group rule refinement

Implemented per Addendum §E.3: same SG → no matrix entry; same SG + same approach → no finding;
same SG + different approaches + geometric conflict → REVIEW REQUIRED. → **David to confirm**

## OQ-005 — Dead `interurban` column and the Sheikh Danon example

In both golden workbooks the `interurban (1|0)` column is referenced by the speed formulas,
but both intersections are computed with urban 50/25 values. The meeting demo intersection
(Route 70 — Sheikh Danon) is interurban, yet the sheet used urban speeds. Confirm intended
classification practice and the posted-speed input that interurban requires. → **David** (carefully)

## OQ-006 — 8 approaches vs 4

The written spec/flow diagram says the command should offer 8 approaches; the workbook
supports 4 (12 movements). Internal model uses A01…Ann and is not limited; confirm the
required user-facing set. → **David**

## OQ-007 — Example 1 AutoAdjusted sheet is largely #REF!

Is the AutoAdjusted (INBAR) sheet still in use? 68 of its 109 rows are broken in the Example 1
workbook (`SOURCE_WORKBOOK_EXISTING_ERROR`; v3 §33; F-006). Affects the INBAR export path /
legacy Matrix only — `MAHOD Matrix Status` is authoritative. → **David**

## OQ-008 — V2 template AutoAdjusted slots 2–4 formula (F-008)

`AutoAdjusted Distances!F/H/J` in the Abarbanel (V2) workbook add the slow-speed column (25)
instead of the Inbar addition. Should the template be corrected (as the current V1 template
already is)? The engine export no longer depends on it (governing point only, ED-014). → **David**

## OQ-009 — Legacy `Matrix` is the Inbar-rounded view, `MAHOD Matrix Status` the exact result

The legacy pivot is built on `AutoAdjusted Distances` (CD ceiling 0.5 m, ED floor 0.5 m) and can
read +1 s above the engine's exact value in a few cells (ED-014). Which value is typed into Inbar
— the adjusted distances (as today) or the exact MAHOD times? → **David**
