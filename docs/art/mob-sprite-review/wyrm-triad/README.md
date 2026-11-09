# Black, Blue and Green Dragon — library batch 8

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Animation MP4](wyrm-triad-motion.mp4) · [GIF](wyrm-triad-motion.gif) · [Native Godot replay](godot-specials.mp4)

Continues [issue #272](https://github.com/higharc91/ArcBox/issues/272) on `mob-library-refresh-272`. Main stays untouched for the separate playtest. 53 designs are now refreshed across 23 batches: 29 combat designs and 24 library appearances. 7 older library appearances remain; 26 established eight-direction looks retain their art. [Tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/wyrm-triad-2026-11-08/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/README.md).

Three mature six-limbed dragons match the approved Red Dragon scale: four grounded legs, two dorsal bat wings and one rooted tail.

Obsidian/wine, cobalt/violet and moss/fern palettes are complete authored figures, with folded wings during walking and visibly unfurled wings during jaw contact.

All three existing ability gestures are authored in eight facings: Fire Breath brace, Frightful Presence mantle and Cataclysmic Wingbeat sweep. Existing shared gameplay and FX remain unchanged.

Texture padding remains 512 pixels: four rows per facing fit the runtime 16384-pixel texture limit.

Live battle/world review checks the same mature family scale beside the party, plus head portraits, wing clearance and whole-silhouette occlusion.

Three complete rear-left hurt/down pairs replace forward-facing damage poses. Black and Blue east Wingbeat rows use fresh whole figures with empty gutters; no selected crop contains a neighboring figure.

Diagnostic beat screenshots allow twelve examples per moment so the nine genuine ability contacts can all be captured; gameplay and world screenshot limits are unchanged.

Lossless WebP compass strips preserve every runtime RGBA pixel while reducing the embedded atlas payload by 41%. Phones decode one facing at a time and release previous views.

624 packed keys select 624 complete figures from 23 preserved source sheets. Each creature has eight facings and 8 actions. Hurt/down hold single poses. Before/after and inspection animations retain a common scale for all creatures. [Battle](battle.png), [world lineup](overworld-lineup.png), [lighting/occlusion](overworld-lighting-occlusion.png), [twelve engine-timed attacks and specials](actions-in-game.png).

## Verification

- Host/test builds: zero warnings/errors. [All 664 focused checks passed](checks/focused.txt), with none skipped. These cover silhouette connectivity, attachment, scale, HUD, atlas bounds, walkable review fixtures and natural contact/recovery.
- [Scope/source receipts](checks/scope.json) preserve prior creature entries/images, rules/abilities, portraits, completed combat tracker and historical baseline. No new stats or encounters. Dragon Jaws contacts at .5 s through a full 1 s clip; Breath and Presence contact at .5 s through 1.2 s, and Wingbeat at .7 s through 1.4 s. Shared rules, effects and explicit projectile overrides are preserved.
- [Browser review](browser-validation.json) decodes all five embedded scenes and 8 actions, checks four distinct walk keys, and fits a 390 px phone viewport. [Complete silhouettes fit their gallery/reel panels](checks/artifact-bounds.json).
- Native Godot battle, world and replay reviewed beside the party for scale, lighting, rear views, front portraits, contact and bar clearance. The silent native video lasts 48 seconds; the combined inspection reel lasts 56 seconds with four representative facings. The gallery selects any of eight facings. Software rendering checks appearance, not target-device performance. [Media metadata](checks/media.json).
- Full presentation suite will run after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 presentation tests.

## Reproduce captures

Use Godot Mono 4.7.2 and the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-wyrm-triad --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-wyrm-triad-moves --shots=/absolute/move-shots --quit-after=50
--preset=low --world-demo=ashen_ford:library-wyrm-triad '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. The replay contains three movement round trips, three natural jaw attacks, all nine existing special gestures and return to ready.

[Next: Gold Dragon, Frost Troll and Ogre](../gilded-and-rime/index.html), continuing issue #272 on the separate follow-up branch.
