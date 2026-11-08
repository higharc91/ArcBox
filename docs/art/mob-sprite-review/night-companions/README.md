# Black Hound, Grey Hound and Violet Imp — library batch 4

[Standalone animation gallery](index.html) · [Before/after](before-after.png) · [Three-creature motion reel](night-companions-motion.mp4) · [GIF](night-companions-motion.gif) · [Actual Godot replay, silent](godot-specials.mp4)

Refreshes `bosses.hound.black`, `bosses.hound.grey` and `bosses.imp.violet`. The 29-design combat pass remains unchanged. This fourth library batch brings the branch to 41 refreshed designs across 19 batches. [Library tracker](../production/library-refresh-status.json): 12 of 31 older two-direction appearances refreshed; 19 remain. Another 26 already have eight-direction art and retain it. No PR opened or merge performed.

## Art and animation

[Concepts/receipt](../../../../tools/tactics-assets/concepts/night-companions-2026-11-07/BRIEF.md) · [Pixel pipeline/provenance](../../../../tools/tactics-assets/imagegen/night-companions-2026-11-07/README.md). Scenario supplied a shared concept sheet with three material columns; OpenAI authored all complete pixel figures. PixelLab was not used.

Black Hound has charcoal fur and umber muzzle/lower legs; Grey Hound uses slate/silver fur and a dark saddle. Both retain Tan's lean scent-hound anatomy, floppy ears, four legs, rooted curved tail and leather collar. Full backs show plain leather rather than a copied front buckle. Violet Imp uses lavender skin, plum wing membranes, ivory horns, small amber eyes, a simple waistwrap/rope and rooted spade tail. Both wing roots remain attached, and rear views turn the face and front waist detail away.

Thirteen preserved source sheets supply 336 packed frame keys selecting 336 distinct complete figures. The first imp idle draft included casting gestures; it is preserved but unselected. A fresh complete sheet keeps both hands relaxed down. No mirrored parts, procedural recolouring or head/hand/wing patches. Hurt/down hold single poses rather than animated falls.

Neutral height 56 authoring pixels at runtime scale 0.32 matches the Tan Hound/Crimson Imp (about 18 world pixels), with no animal enlargement. Before/after uses the same 6× world zoom for all old/new figures. [Battle](battle.png), [overworld lineup](overworld-lineup.png), [town lighting/occlusion](overworld-lighting-occlusion.png), [two jaw contacts and imp hand release](actions-in-game.png). The 24-second combined reel enlarges all three together for inspection at four representative facings; the gallery selects any of eight directions. HUD portraits crop the front head/horns, and active-pose clearance keeps bars above the silhouette.

Idle runs at 3 fps and movement at 6 fps. Hounds alternate diagonal footfalls with restrained ear/tail lag and bite close to the ground. The imp steps with attached wings rather than rising into high flight. Four natural-action keys cover ready, windup, contact/release and recovery. Hound bites contact at 0.4 s and recover through 0.8 s. The imp launches existing hellfire at 0.4 s and finishes its full clip; projectile travel can extend the pose. Explicit projectile presentations keep priority. No active special abilities are added.

No stats, rules, ability definitions, damage, resistances, ranges, sound assets, encounter placements, prior runtime images or dialogue portraits change. Exact variant keys inherit their approved primary family's visual timing/inference. Review lineups/replay are development art fixtures, not new encounters.

## Verification

- Host and test builds: zero warnings/errors.
- Focused attachment, motion, scale, HUD, atlas, board and beat checks: **596 passed**.
- Full presentation suite: **3,125 passed / 3,128 total**; only the same three pre-existing story-count failures remain ([log](checks/full.log)). Expected/actual counts remain 590/614 speakers, 921/945 lines and 8/32 camp reflections, with [prior baseline evidence](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). Full solution not rerun for this art/presentation batch.
- All 336 packed keys pass source-edge, neighbour and projected-bound checks. Idle/walk/attack each have four distinct pixel keys in all eight facings. All 13 preserved source PNGs match their generation-ID originals exactly; the rejected idle draft is unselected.
- Browser validation decodes all five embedded scenes, exercises five actions and four distinct movement keys, and fits a 390 px phone viewport without horizontal overflow.
- Native Godot battle, world and replay reviewed for contrast, party scale, joined anatomy, rear views, front portraits, bar clearance and contact/recovery. Three movement round trips, two natural bites and one ranged hellfire use the ordinary beat/FX path. Shared fire effects remain unchanged. Software rendering is a visual check, not a performance benchmark. Dummy audio means the video is silent.
- Scope receipts preserve all prior creature entries/runtime art, rules/abilities/FX, portraits, completed combat tracker and historical 98-look baseline. Three new entries/atlases only; baseline and production HTML append a navigation link.

## Reproduce captures

Use Godot Mono 4.7.2 with the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Host arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:night-companions --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=night-companions-moves --shots=/absolute/move-shots --quit-after=22
--preset=low --world-demo=ashen_ford:night-companions '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/night-companions-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`. Then run action-scenes.cjs, review.cjs and verify-review.cjs. Hound jaw contacts and imp target impact follow engine beat-clock captures; imp hand release/windup captures and video allow the launch to be reviewed separately. The silent replay includes three movement round trips, two bites, a ranged hellfire bolt and return to ready.

[Next: Vampire Bat, Undead Bear and Crystal Slime](../crypt-kin/index.html), library batch 5 for issue #272 on the separate follow-up branch.
