# Layer / Excel colour model ("Appendix B", recovered)

`הגדרת פעולה.docx` refers to an Appendix B colour model that never reached us. David's e-mail of
2026-08-27 describes it in words: one colour family per approach, progressing clockwise around the
junction in rainbow order; within an approach right = dark shade, straight = normal, left = light.

The exact values were recovered from his own files. The layer colours are identical in Example 1 and
Example 2 (read headlessly with `IG_SCAN` from the reference DWGs), and the `Signal group key` cell
fills are identical in Example 1, Example 2 **and the blank template**. This is therefore the
standard, not a per-project choice.

| approach (clockwise) | family | Left (light) | Through (normal) | Right (dark) |
|---|---|---|---|---|
| **N** | yellow | ACI 2 · `FFFF00` | ACI 52 · `CCCC00` | ACI 42 · `808000` |
| **E** | red | ACI 220 · `FF33CC` | ACI 11 · `FF9999` | ACI 10 · `FF0000` |
| **S** | blue | ACI 4 · `00FFFF` | ACI 150 · `0000FF` | ACI 176 · `002060` |
| **W** | green | ACI 3 · `00FF00` | ACI 102 · `33CC33` | ACI 88 · `007635` |

ACI = AutoCAD Color Index of the `intergreen_<movement>` layer; hex = the `Signal group key` cell fill.

| other | layer ACI | Excel fill |
|---|---|---|
| pedestrian crossings `a`–`h` | 7 | — |
| `intergreen_stopline` | 7 | — |
| `E-L-sherut` | — | `FFC000` |
| `N-T-sherut` | — | `9999FF` |
| `N-T-bus` | — | `FFFFCC` |
| `S-T-bus` | — | `FF3300` |

Entities are always `ByLayer` (colour index 256) — the colour lives on the layer, never on the polyline.

Open point for the client: diagonal approaches (NE/SE/SW/NW, permitted by the naming rule) have no
colours in any reference file.
