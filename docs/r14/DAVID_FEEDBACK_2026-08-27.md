# David's feedback of 2026-08-27 — analysis before any change

## Item 1 — where a boundary that stops inside a crossing should be measured to

David: the conflict ends after the crossing; measure to where the boundary line **exits** the crossing.

That is a third answer to a question we posed as two: today's rule measures to the **drawn end**
(r13, Directive §21A); the engineer's 14 for Example 2 `b→W-R` corresponds to the **stop line**.

What the data says before we touch the rule:

| evidence | finding |
|---|---|
| Example 1, all 10 vehicle→pedestrian rows | engine CD == engineer's CD within 0.5 m — **0 rows off**. Two of them (`E-R→a`, `S-T→a`) are boundaries that stop inside crossing `a`, and the engineer measured `E-R→a` to the **drawn end** (18.95 vs boundary length 18.93) — the very case cited in the §21A code comment |
| Example 2, 10 vehicle→pedestrian rows | 4 rows engine shorter by 6–25 m, 5 rows engine **longer** by 9–17 m — noise of ±25 m in both directions, consistent with the documented REVISION_MISMATCH. Not usable as an oracle for this rule |
| Example 2 `W-R→b` specifically | engineer CD 19.78; drawn end 13.49; extending the boundary straight through crossing `b` exits at **15.80** (depth 2.31 m) — the "exit" reading gives 15.80, not 19.78. The engineer's number is 4 m beyond the crossing |
| Lin 05293 | after r13, 55 of 56 rows equal her manual FINAL IG, so vehicle-clearing CD cannot be systematically short there |
| how often it matters | boundaries stopping inside a crossing: Example 1 **2**, Example 2 **6**, Lin **6** |

Conclusion: David's stated principle and his own team's measurements disagree on his clean reference
project. Implementing "exit edge" as stated would move Example 1 `E-R→a` off the engineer's value.
This goes back to him as a precise question with his own numbers, not into the engine.

Safety direction: a longer CD for the **clearing** vehicle lengthens the intergreen (conservative);
for the **entering** vehicle a longer ED shortens it. Any rule change here must be stated per role.

## Item 2 — colours

Fully specified now: see `docs/COLOUR_MODEL.md`, recovered from his files and identical across template
and both examples. No further input needed except diagonal approaches.

## Item 3 — drawing methodology approved

Green light for the assisted movement builder as specified in `הגדרת פעולה.docx` Appendix A.

## Item 4 — data-collection format for non-drawing inputs

He asks us to propose. The template already fixes the fields: `Parameters` (interurban flag, fast /
slow clearing speed, vehicle length, per movement), `Signal group key` (movement → SG), `Pedestrian
Xing` (crossing → SG letter, length). The proposal is a one-screen Hebrew form in the palette that
pre-fills every movement and crossing detected in the drawing and asks only for those values, then
writes the template — not a new format.

## Item 5 — curbs and lane markings drawn by others, inconsistent layers

He proposes: let the user pick the relevant lines/layers once at the start and persist that choice.
That is the right design and matches how the project sidecar already works (units, endpoint
confirmations, rule pack). Persist per project; offer "same as last project" as a default.

## Item 6 — boundaries not reaching the stop line

(a) A clear warning when no end touches the stop line — **exists since r12** (Validate names the
movement, handle and gap in cm; the "נקודות ייחוס…" dialog). Tell him.
(b) Auto-extend to the stop line under a user-set threshold (e.g. 10 cm) — this reverses ED-012's
"no hidden 0.5 m guess", but as a **user-configured project tolerance** it is no longer hidden. Record
as a new engineering decision; default off; log every auto-confirmation in the support log and the QA
column; the 4.7 cm and 6.3 cm cases in Lin's drawing would be covered.
