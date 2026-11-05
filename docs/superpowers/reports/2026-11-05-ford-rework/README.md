# Ford rework: concept checkpoint

Issue [#82](https://github.com/higharc91/ArcBox/issues/82), branch `ford-rework`.
Based on merged main `da796d97f5613732b6446e4a66b7b4d4c719b81b`.

## Current review: native pixel art

Revision 2 concepts are approved. The owner selected native art authoring.
The [pixel-art and TV checkpoint](native-art/README.md) is now ready for approval.
No extra generation spend; Scenario remains 52 CU. Live content is unchanged.

## Approved concept: revision 2

The owner found revision 1's differences too subtle. The new
[comparison](../../../../tools/tactics-assets/concepts/ford-rework/revision-2-review.jpg)
uses large state silhouettes with matching architecture: closed spiked defenses
and tall hides; smashed defenses and localized scorch marks; orderly open gates,
rolled hides and a pale grain shelter. All three read distinctly at approximately
200 pixels per state. Revision 1 remains archived as provenance, not approved art.

One new Scenario comparison image cost **13 CU**; total concept spend is **52 CU**.
Its original 3168×1344 PNG, prompt, references, job/asset IDs and SHA-256 are recorded
in the concept batch. The labels on the JPG are review annotations only.
The revised near-approach gates and scorch dressing are design proposals; final
module placement must preserve the stamp's route and both story outcomes.
Owner concept approval was subsequently given; see the native pixel-art checkpoint above.
No runtime code changed in this revision; the baseline test results below belong
to the preceding checkpoint, and have not been rerun for this art-only revision.

## Scope and approval boundary

The first stage proposes one continuous Warden stone-and-timber bridge in three
states: Skarrow fortified, reopened after force, and reopened after parley.
The briefs are appended to `tools/tactics-assets/concepts/scenario-briefs.json`;
full generation receipts live in that directory's `ford-rework/` batch.
These are painted design references, not production sprites or approved map geometry.

Issue #82 explicitly says to show the contact sheet and wait for owner approval
before pixel art. No production textures, map cells, environmental reactions,
story flags, encounter numbers or objectives have been changed at this checkpoint.
No PR is requested or opened.

## Design and implementation constraints

- Preserve the Chapter 1 bridge choice, Dagna's loss, Mother Skarrow's force
  aftermath and the grain bargain; parley still has a hothead fight before reopening.
- The gatehouse sits beside the clear cart lane. Stone piers and parapets support
  a level timber deck; hide screens, stakes and barricades are separate dressing.
- In force, broken removable defenses are pulled aside; the bridge remains sound.
  In parley, defenses are opened or stacked and a modest grain delivery is visible.
- The old northern stubs at `(105..108,48)` remain unwalkable deep river.
  The only crossing stays inside the `river_crossing` stamp at `(100,52)`.
  Both Chapter 1 encounters continue to share it; nearby shallows remain wadeable.
- Deck art must become walkable planks with fuel, water stays conductive, and
  parapets block. Final tile geometry is a later reviewed implementation step.
- Reuse #78's whole-frame sampling and per-tile split sorting. OW-08's old cut
  was fixed in `799e8f70`; this task does not need to recreate that fix.
- After approved pixel art, verify world and fight overlays, flag-gated states,
  byte-identical exports twice, force/parley Chapter 1 completion and refreshed
  place shots. Report both encounters over 20 seeds, levels 1–3, party sizes 1–4,
  environment enabled against the environmental-tiles baseline. Propose balance
  adjustments for the owner without changing encounter numbers.

## Ground diagnosis and baseline

Fresh native Godot software OpenGL captures at 1280×720 are in `before/`:
`world-ford-west.png`, `world-ford-east.png`, and `world-ash-bank.png`.
The software renderer's timing is not a hardware performance acceptance result.

The orange/olive patches and cloudy ash are visible in the current assets.
The historical `2026-10-31-tactics-overworld/texture-art-pass.md` records deliberate
larger, quieter painterly forms, soft organic masks, reduced micro-contrast and
skipped small decals. The current renderer uses nearest texture filtering;
switching filtering alone will not restore missing sharp material forms.

The Ford top textures are 256×256. `before/material-audit.json` records dimensions,
RGB colour counts and hashes: for example grass has 7,613 colours and ash 1,715.
These counts identify a broad tonal source rather than proving perceived contrast
by themselves. `GroundBlend.BuildSoft` also makes continuous-alpha feathering
with Ford thresholds 0.12/0.94 and wobble 0.34. Battle stamps skip world edge
blending, so changing only that mask cannot fix the battle camouflage.

Part 2 should author clustered, crisp ground forms through the terrain pipeline
(`parts/terrain.py`, `dev_terrain.py`, `build.py`) at the current town's visible
pixel density. Preserve the original Ford PNGs and their hashes as evidence.
The one-off Ford repaint has no reproducible generator found in that pipeline;
do not claim a rerun of the generic catalogue will reproduce it. New ground art
still needs its separate concept/pixel/TV approval stages.

## Validation at this checkpoint

All three Scenario images completed successfully: **39 CU total** (13 each).
The [contact sheet](../../../../tools/tactics-assets/concepts/ford-rework/contact-sheet.jpg)
shows fortified, after force and after parley from left to right. Full-resolution
PNGs and exact prompts/references/job IDs/output hashes are in the same batch.
The proposal has three stone arch openings; both outcome images retain them.
The initial brief requested two, so the three-opening result is explicitly a
design proposal for owner review.

Baseline checks: **3 Ashen Ford world-plan tests and 49 overlay picture tests
passed**. This covers the old northern bridge remaining impassable, the ford
remaining the only crossing, and current world/fight painter ordering. Test logs
are in `before/`. Three fresh native TV views also completed successfully.
Runtime and encounter implementation checks belong after design approval; no
full-suite completion or part 2 ground approval is claimed here.
