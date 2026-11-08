# Gold Dragon, Frost Troll and Ogre — library batch 9

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Animation MP4](gilded-and-rime-motion.mp4) · [GIF](gilded-and-rime-motion.gif) · [Native Godot replay](godot-specials.mp4)

Continues [issue #272](https://github.com/higharc91/ArcBox/issues/272) on `mob-library-refresh-272`. Main stays untouched for the separate playtest. 56 designs are now refreshed across 24 batches: 29 combat designs and 27 library appearances. 4 older library appearances remain; 26 established eight-direction looks retain their art. [Tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/gilded-and-rime-2026-11-08/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/README.md).

Gold-scaled mature dragon retains four grounded legs, two dorsal wings and a rooted tail; wings fold for walking and unfold on jaw strikes.

Pale slate-blue Frost Troll and broad taupe Ogre share the approved giant scale, joined empty-hand strikes, fixed short hair and rope-bound hipwraps.

Boulder windups grip the stone; release hands are empty as the existing projectile leaves. Distinct chest/throat roar gestures remain grounded.

Fresh Gold rear-left damage, east walking and northwest Wingbeat figures correct orientation, unwanted walking attacks and overlapping source gutters.

The rejected Ogre throw sheet retained a carried stone at release; a fresh complete sheet shows empty release palms in every facing.

Frenzy Roar FX now waits for the authored .4 s chest gesture across the Troll family; its full .8 s recovery plays without changing gameplay.

The gallery uses lossless compass strips and one-facing phone decoding; original runtime/source pixels are preserved.

Fresh northeast and southeast jaw sequences visibly unfurl both wings; all eight contact silhouettes are checked against the folded idle.

560 packed keys select 560 complete figures from 26 preserved source sheets. Eight facings per creature. Gold Dragon has eight unique actions; each giant has seven, with existing generic casting aliases sharing its roar gesture. Hurt/down hold single poses. Before/after and inspection animations retain a common scale for all creatures. [Battle](battle.png), [world lineup](overworld-lineup.png), [lighting/occlusion](overworld-lighting-occlusion.png), [ten natural strike, boulder release and special reviews](actions-in-game.png).

## Verification

- Host/test builds: zero warnings/errors. [All 695 focused checks passed](checks/focused.txt), with none skipped. These cover silhouette connectivity, attachment, scale, HUD, atlas bounds, walkable review fixtures and natural contact/recovery.
- [Scope/source receipts](checks/scope.json) preserve prior creature entries/images, rules/abilities, portraits, completed combat tracker and historical baseline. No new stats or encounters. Dragon Jaws contacts at .5 s through 1 s; Breath and Presence at .5 s through 1.2 s, and Wingbeat at .7 s through 1.4 s. Giant strikes/roars contact at .4 s through .8 s; boulders release at .4 s and hit after their flight. Rules, effects, stats and placements remain unchanged.
- [Browser review](browser-validation.json) decodes all five embedded scenes and 8 actions, checks four distinct walk keys, and fits a 390 px phone viewport. [Complete silhouettes fit their gallery/reel panels](checks/artifact-bounds.json).
- Native Godot battle, world and replay reviewed beside the party for scale, lighting, rear views, front portraits, contact and bar clearance. The silent native video lasts 42 seconds; the combined inspection reel lasts 56 seconds with four representative facings. The gallery selects any of eight facings. Software rendering checks appearance, not target-device performance. [Media metadata](checks/media.json).
- Full presentation suite will run after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 presentation tests.

## Reproduce captures

Use Godot Mono 4.7.2 and the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-gilded-and-rime --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-gilded-and-rime-moves --shots=/absolute/move-shots --quit-after=44
--preset=low --world-demo=ashen_ford:library-gilded-and-rime '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. The replay contains three movement round trips, three natural strikes, all seven existing specials and return to ready.
