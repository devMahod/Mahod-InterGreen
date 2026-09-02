# Codex review of the r14 branch (2026-09-02) — point-by-point, with what was done

Codex reviewed the r14 work at `aa9aa14` without running Autodesk or changing code. Every claim was
re-checked against the sources it cites; this records the verdict on each and the change it produced.
Verdicts: **accepted** (changed), **accepted in part**, **not accepted** (with the evidence).

## 1. Engineering rule for vehicle × crossing — *agreed, and it was already the implemented reading*

הנחיות יוני 2025, p. 157 (ch. 5.4): vehicle clearing / pedestrian entering → L2 to the **far edge**,
T3 = 0; pedestrian clearing / vehicle entering → L3 to the **near side**, L3 < 1.5 m → 0. ED-015 is
stated per role and applies the far-edge reading to the clearing vehicle only; the entering role is
unchanged. Codex's caution "do not present envelope extension as an implementation of the 2025
guidelines" is **accepted**: ED-015 is a legacy-envelope adaptation consistent with the 2025 far-edge
principle, and will be worded so in the David report.

## 2. WP1 did not change David's literal pair `b→W-R` — *true, and correct*

| pair | engineer | engine | why |
|---|---|---|---|
| `b→W-R` (b clears, W-R enters) | CD 16, **ED 0**, IG 14 | CD 16, ED 6.67, IG 13 | near side of b is 6.67 m from W-R's stop line (both W-R boundaries end on stop line 6565; drawn reversed); ≥ 1.5 m, so 0 does not apply |
| `W-R→b` (W-R clears, b enters) | CD 19.78, IG 6 | CD 13.49 → 15.63 (r13 → WP1), IG 5 | boundary ends at the near edge of b's right-turn piece; the crossing is 2.1–3.0 m deep there; 19.78 is not reproducible from this drawing (Example 2 is a documented REVISION_MISMATCH) |

Across all 34 pedestrian-clearing rows of both examples the engineers write ED = 0 only for movements
that leave the crossing's own arm; W-R reaches b from another arm. Its 0 is the single outlier in their
own data. **Not accepted** as a defect; **accepted** that it must go to David with the citation and his
own numbers, not as a "fixed" case. `prove_against_manual.py` on the current build: 39/40 + 60/78 =
99/118, unchanged from r13 — WP1 moved distances, not FINAL IGs, exactly as ED-015 states.

Algorithm cautions (continuation along the last segment, 60 m search, 0.5 m "at the edge") are
**accepted as our assumptions**, not guideline text; they are now labelled so. The termination-candidate
invariant (`Every_termination_candidate_lies_on_the_crossing_it_claims_to_stop_in`) already covers the
`+exit` candidates (≤ 1 m from a crossing edge). The "0.30 m" comment was wrong (2.6 cm) — **fixed**.

## 3. WP2 — *accepted*

- Default **OFF**; 10 cm is the dialog's suggestion (`SuggestedAutoConfirmToleranceMeters`) — done.
- Clamp the sidecar value on read — done (`ReferenceReview.Clamp`).
- "10 cm cannot change an intergreen" withdrawn in ED-016 — done.
- Confirming an endpoint is not extending a line — **accepted and done** (commit 9fd00ac): the pass now
  extends along the end tangent to the stop line (length along the tangent, unique intersection); a
  boundary that runs past the stop line's end vertex — Lin's E-T, 4.7 cm beside it — cannot be extended
  and is confirmed the way she did by hand, recorded as automatic; ambiguous or wider gaps go to the
  engineer. Proven on Lin's geometry and in accoreconsole 2026/2027 on Example 2 (S-L.b2 +4.37 cm;
  E-L.b1 at 16.45 cm rightly left pending). The explicit action exists too (commit aa79a4b): "הארך בשרטוט…" writes the extension into the DWG with one Undo, proven on a copy in both hosts (written, re-validated, saved, reopened).

## 4. WP3 colours — *accepted in part*

Appendix B exists (`אפיון.xlsx` Sheet2!A60:A85) and differs from the template/examples for E-R/E-T/E-L.
**Accepted**: recorded as a precedence decision (David's e-mail + the files in use over the older
appendix), not as a recovery — `docs/COLOUR_MODEL.md`. Wiring to CAD layers happens where layers are
created (WP6 builder); Excel fills already live in the template. Bus/sherut/diagonal/U-turn colours are
open questions for David, listed in the report.

## 5. WP4/WP5 — *accepted in part; several items were already fixed between Codex's snapshot and now*

- Half-length role heuristic flips on reversed polylines — **accepted and replaced twice**: first by
  stop-line anchoring (still wrong for Example 2's short right turns E-R × a, W-R × c), then by the
  junction's topology (`CrossingSlots.RolesFromGeometry`: ≥2 movements of one approach meeting a
  crossing ⇒ its approach crossing; a lone one with siblings ⇒ exit arm). Proven on both real drawings,
  including the reversed boundaries (Example 1 S-T, Example 2 W-R).
- Automatic c9..c12 inference — **removed**; the arm slot is proposed and the channelised alternative
  is named for the engineer.
- Slot collision must stop, not warn — **accepted**: the form refuses to close on a crossing without a
  slot or two crossings on one slot; `TemplateWorkbook` keeps its warnings as a backstop for the
  headless path.
- `File.Copy(overwrite:true)` — **fixed**: an existing workbook is never replaced.
- Hebrew form — **now exists** (`WpfNewProjectForm`), with explicit slot text per crossing, arm named.
- Proof on Example 2 — **added** (generated-workbook regression, full-width crossings in both halves).
- Real-host proof — `scripts/realhost_new_project.py`: accoreconsole 2026 and 2027, sidecar-less
  Example 1, slots c2..c5 from geometry, workbook committed, Analyze 50 / 24-0-0 / W-L→S-T 5. PASS.

## 6. WP6 — *accepted as design constraints*

No silent "same as last project": a saved profile may be offered, every project previews and
confirms. Curb vs lane-boundary layers separated; entity-level selection; xref/block/ByLayer
handling; explicit direction and start point; never edit source or xref; full Cancel/Undo; method
(legacy envelopes vs 2025 axes) chosen up front.

## 7. Commits — *flagged to Arthur*

Four local commits exist on `pilot-hardening` (WP1, WP3, WP5, WP4+review); nothing is pushed, nothing
is tagged, nothing is packaged. Arthur decides whether local per-WP commits continue.

## 8. The r13 ZIP — *not a mismatch*

`Downloads\Mahod_Intergreen_0.1.0-r13.zip` (68 MB, `7AD2B695…`) is the two-file staff ZIP Arthur asked
for on 2026-08-26, Defender-scanned, installer payload hash-checked. The 5.85 MB installer-less package
(`83E58434…`) is the alternate. Both are now recorded in `docs/shipping/ANTIVIRUS_FALSE_POSITIVE.md`.
`MahodRelease.props` still says r13 by design — the bump is the packaging step (WP7), not before.
