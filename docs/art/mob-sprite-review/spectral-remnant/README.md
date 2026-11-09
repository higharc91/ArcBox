# Spectre — library batch 11

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Animation MP4](spectral-remnant-motion.mp4) · [GIF](spectral-remnant-motion.gif) · [Native Godot replay](godot-specials.mp4)

Continues [issue #272](https://github.com/higharc91/ArcBox/issues/272) on `mob-library-refresh-272`. Main stays untouched for the separate playtest. 60 designs are now refreshed across 26 batches: 29 combat designs and 31 library appearances. 0 older library appearances remain; 26 established eight-direction looks retain their art. [Tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/spectral-remnant-2026-11-08/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/README.md).

Pale mist-grey hood and robe preserve the approved human-sized Wraith anatomy and cool material planes.

Eight genuine facings have plain rear hoods, grey hands joined to sleeves and a continuous drifting tail without legs or loose wisps.

Natural spectral touch and the existing targeted Life Drain have distinct full gestures; hurt and low collapsed down poses read clearly.

The rejected damage draft remained upright in down cells. A fresh complete sheet supplies low collapsed robes in every facing.

Each hand remains connected to its sleeve, with no carried weapons or detached spectral fragments.

The final singleton batch completes all 31 older library appearances; separate TV/phone playtest feedback remains open on issue #272.

144 packed keys select 144 complete figures from 6 preserved source sheets. Eight facings and six unique actions; the existing special alias selects the distinct Life Drain gathering clip. Hurt/down hold single poses. Before/after and inspection animations retain a common scale for all creatures. [Battle](battle.png), [world lineup](overworld-lineup.png), [lighting/occlusion](overworld-lighting-occlusion.png), [two engine-timed spectral touch and Life Drain contacts](actions-in-game.png).

## Verification

- Host/test builds: zero warnings/errors. [All 723 focused checks passed](checks/focused.txt), with none skipped. These cover silhouette connectivity, attachment, scale, HUD, atlas bounds, walkable review fixtures and natural contact/recovery.
- [Scope/source receipts](checks/scope.json) preserve prior creature entries/images, rules/abilities, portraits, completed combat tracker and historical baseline. No new stats or encounters. Spectral touch and Life Drain contact at .4 s and recover through .8 s. Rules, stats, ranges, encounter placements and FX are unchanged.
- [Browser review](browser-validation.json) decodes all five embedded scenes and 6 actions, checks four distinct walk keys, and fits a 390 px phone viewport. [Complete silhouettes fit their gallery/reel panels](checks/artifact-bounds.json).
- Native Godot battle, world and replay reviewed beside the party for scale, lighting, rear views, front portraits, contact and bar clearance. The silent native video lasts 24 seconds; the combined inspection reel lasts 40 seconds with four representative facings. The gallery selects any of eight facings. Software rendering checks appearance, not target-device performance. [Media metadata](checks/media.json).
- Full presentation suite will run after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 presentation tests.

## Reproduce captures

Use Godot Mono 4.7.2 and the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-spectral-remnant --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-spectral-remnant-moves --shots=/absolute/move-shots --quit-after=26
--preset=low --world-demo=ashen_ford:library-spectral-remnant '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. One movement round trip, one natural touch, one real targeted Life Drain and return to ready.
