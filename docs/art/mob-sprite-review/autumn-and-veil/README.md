# Autumn Sapling, Blossom Sapling and Shade — library batch 10

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Animation MP4](autumn-and-veil-motion.mp4) · [GIF](autumn-and-veil-motion.gif) · [Native Godot replay](godot-specials.mp4)

Continues [issue #272](https://github.com/higharc91/ArcBox/issues/272) on `mob-library-refresh-272`. Main stays untouched for the separate playtest. 59 designs are now refreshed across 25 batches: 29 combat designs and 30 library appearances. 1 older library appearances remain; 26 established eight-direction looks retain their art. [Tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/autumn-and-veil-2026-11-08/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/README.md).

Small seasonal saplings preserve rooted twin crowns, warm bark, root-foot walking and their attached anatomical left vine.

Charcoal-violet Shade drifts with a continuous robe tail and joined grey hands; no legs, weapons or detached wisps.

Directional physical vine lashes and spectral touches use full contact/recovery; Shade retains its existing targeted Life Drain with a distinct gathering gesture.

Seven mixed-species drafts are preserved but excluded. Fresh single-creature sheets replace them; material identity is checked across every selected facing/action.

Vine attack registration follows brown root feet, never the low lash tip; the full authored figures are copied without deformation.

Plain rear crowns and hoods face away; attached arms, vine and continuous robe remain intact.

368 packed keys select 368 complete figures from 20 preserved source sheets. Eight facings each. Five unique actions per sapling and six for Shade; the existing special alias selects its Life Drain gathering clip. Hurt/down hold single poses. Before/after and inspection animations retain a common scale for all creatures. [Battle](battle.png), [world lineup](overworld-lineup.png), [lighting/occlusion](overworld-lighting-occlusion.png), [four engine-timed lash, touch and Life Drain contacts](actions-in-game.png).

## Verification

- Host/test builds: zero warnings/errors. [All 715 focused checks passed](checks/focused.txt), with none skipped. These cover silhouette connectivity, attachment, scale, HUD, atlas bounds, walkable review fixtures and natural contact/recovery.
- [Scope/source receipts](checks/scope.json) preserve prior creature entries/images, rules/abilities, portraits, completed combat tracker and historical baseline. No new stats or encounters. Sapling lashes and Shade touch/drain contact at .4 s and recover through .8 s. Primary Wraith touch/drain now complete their existing .6/.625 s clips at .3/.375 s contact; its art is unchanged. Rules, stats, ranges and FX remain unchanged.
- [Browser review](browser-validation.json) decodes all five embedded scenes and 6 actions, checks four distinct walk keys, and fits a 390 px phone viewport. [Complete silhouettes fit their gallery/reel panels](checks/artifact-bounds.json).
- Native Godot battle, world and replay reviewed beside the party for scale, lighting, rear views, front portraits, contact and bar clearance. The silent native video lasts 24 seconds; the combined inspection reel lasts 40 seconds with four representative facings. The gallery selects any of eight facings. Software rendering checks appearance, not target-device performance. [Media metadata](checks/media.json).
- Full presentation suite will run after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 presentation tests.

## Reproduce captures

Use Godot Mono 4.7.2 and the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-autumn-and-veil --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-autumn-and-veil-moves --shots=/absolute/move-shots --quit-after=26
--preset=low --world-demo=ashen_ford:library-autumn-and-veil '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. Three movement round trips, three natural strikes, one real targeted Life Drain and return to ready.

[Next: Spectre](../spectral-remnant/index.html), continuing issue #272 on the separate follow-up branch.
