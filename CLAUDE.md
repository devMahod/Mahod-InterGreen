# CLAUDE.md — Mahod Intergreen

Guidance for Claude Code and other coding agents in this repo.

## Releasing Mahod Intergreen — through MahodAI (since 2026-10-08)

**Mahod Intergreen has no installer of its own any more.** Since MahodAI 2.0 every Mahod tool ships
INSIDE the MahodAI plugin and reaches engineers through MahodAI's signed auto-update feed; the
MahodAI 2.0 setup removes the old standalone installs. So a release of this tool **is** a MahodAI
release:

1. Finish the work here, merge it to **main** and push. A release ships exactly what is on main.
2. From the MahodAI plugin (`C:\WORK\mahod_ai_workspace\plugin`, MahodAI-Plugin):
   `.\scripts\release\Release-Tool.ps1 -Tool intergreen` — it syncs this repo into the plugin,
   picks the next MahodAI version, runs the gates, packs and signs, and commits plugin and feed in
   throwaway worktrees. **It pushes nothing.**
3. Show the owner its summary. Only on an explicit go: `.\scripts\release\Release-Tool.ps1 -Tool intergreen -Publish`
   pushes, tags this repo `v<version>` and checks the live feed. Engineers have it within the hour
   (or at once with the עדכונים button on the MahodAI ribbon tab).

The full procedure and its traps: `RELEASING.md` in MahodAI-Plugin. Slash command here:
`/release-to-mahodai`.

**Not yet a mirror.** The plugin carries a decompile of this product's shipped build
(`MahodAI.Civil3D.Plugin/Ported/`, `Ported/README.md` has the recipe and the local-edit ledger).
It becomes a byte mirror when the plugin's branch `feat/mirror-culvert-intergreen` (its two
source commits) is merged to main; until then a new version is brought in by a three-way merge
by hand, in the plugin, and released with `-Tool plugin`. After the merge, `-Tool intergreen` works
like Parking's.

In MahodAI the tool is the **Intergreen** button on the MahodAI ribbon tab
(panel כלים הנדסיים) and the commands `INTERGREEN` (the palette), `IG_SCAN`, `IG_EXPORT_GEOMETRY`. Its version is `<MahodEngineVersion>` + `<MahodReleaseRevision>` in `build/MahodRelease.props`.

**Its user guide is MahodAI's**: `guides/content/intergreen.md` in MahodAI-Plugin, built to
`Mahod_*_Guide.pdf` by `guides/build-guides.py` and shipped in every release (the ribbon's
מדריכים button, the Start menu). A user-visible change here needs that guide updated in the same
release — not a PDF in this repo.

**Retired, do not use:** this repo's own installer and portal ZIPs, and the
`ci/release-pipeline` branch (an automated design superseded by the above — do not merge it).
