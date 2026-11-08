# Black Scorpion, Warg Rider and Ironbound Mimic — library batch 7

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Animation MP4](iron-and-fangs-motion.mp4) · [GIF](iron-and-fangs-motion.gif) · [Native Godot replay](godot-specials.mp4)

Continues [issue #272](https://github.com/higharc91/ArcBox/issues/272) on `mob-library-refresh-272`. Main stays untouched for the separate playtest. 50 designs are now refreshed across 22 batches: 29 combat designs and 21 library appearances. 10 older library appearances remain; 26 established eight-direction looks retain their art. [Tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/iron-and-fangs-2026-11-08/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/README.md).

Slate-edged black scorpion with eight walking legs, two joined pincers and one rooted segmented stinger.

A goblin spearman seated on a grey warg; spear grip, reins, knees and saddle remain connected through movement and damage.

Dark oak and heavy iron chest mimic with front-only latch and rear-only hinges; closed idle/walk and an existing Adhesive Bite attack.

An OpenAI background edit removed the opaque scorpion idle background without repainting anatomy. A fresh fully closed mimic idle sheet replaced an overactive first draft.

Complete authored rear damage corrections preserve the creatures’ rear orientation.

Runtime review uses real Adhesive Bite binding for Ironbound Mimic and joined rider/mount movement.

336 packed keys select 336 complete figures from 16 preserved source sheets. Five actions and eight facings per creature; hurt/down hold single poses. Before/after and inspection animations retain a common scale for all creatures. [Battle](battle.png), [world lineup](overworld-lineup.png), [lighting/occlusion](overworld-lighting-occlusion.png), [three engine-timed contacts](actions-in-game.png).

## Verification

- Host/test builds: zero warnings/errors. [All 629 focused checks passed](checks/focused.txt), with none skipped. These cover silhouette connectivity, attachment, scale, HUD, atlas bounds, walkable review fixtures and natural contact/recovery.
- [Scope/source receipts](checks/scope.json) preserve prior creature entries/images, rules/abilities, portraits, completed combat tracker and historical baseline. No new stats or encounters. Exact refreshed keys use the authored .4 s contact and .8 s recovery.
- [Browser review](browser-validation.json) decodes all five embedded scenes and five actions, checks four distinct walk keys, and fits a 390 px phone viewport. [Complete silhouettes fit their gallery/reel panels](checks/artifact-bounds.json).
- Native Godot battle, world and replay reviewed beside the party for scale, lighting, rear views, front portraits, contact and bar clearance. The silent native video lasts 20 seconds; the combined inspection reel lasts 24 seconds with four representative facings. The gallery selects any of eight facings. Software rendering checks appearance, not target-device performance. [Media metadata](checks/media.json).
- Full presentation suite will run after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 presentation tests.

## Reproduce captures

Use Godot Mono 4.7.2 and the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-iron-and-fangs --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-iron-and-fangs-moves --shots=/absolute/move-shots --quit-after=22
--preset=low --world-demo=ashen_ford:library-iron-and-fangs '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. The replay contains three movement round trips, three natural attacks and return to ready.
