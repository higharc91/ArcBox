# Basalt, Crystal and Temple Golems — library batch 2

[Standalone animation gallery](index.html) · [Before/after](before-after.png) · [Three-creature motion reel](stone-sentinels-motion.mp4) · [GIF](stone-sentinels-motion.gif) · [Actual Godot replay, silent](godot-specials.mp4)

Refreshes `bosses.golem.basalt`, `bosses.golem.crystal` and `bosses.golem.temple`. The 29-design combat pass remains unchanged. This second library batch brings the branch to 35 refreshed designs across 17 batches. [Library tracker](../production/library-refresh-status.json): six of 31 older two-direction library appearances refreshed; 25 remain. Another 26 appearances already have eight-direction art and retain it. No PR opened or merge performed.

## Art and animation

[Concepts/receipts](../../../../tools/tactics-assets/concepts/stone-sentinels-2026-11-07/BRIEF.md) · [Pixel pipeline/provenance](../../../../tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/README.md). Scenario supplied Basalt's concept; after its new-job rate limit, OpenAI supplied Crystal and Temple concepts. OpenAI authored all complete pixel figures. PixelLab was not used.

Basalt has charcoal slabs, cool slate edges and a small amber ring core. Crystal uses lavender stone with physically joined ice-blue/violet facets. Temple uses weathered sandstone, restrained moss and carved forearm bands. Front chest cores and eyes turn away in back views. Eight authored facings have idle, heavy walking, connected right-fist punches, hurt/down, Seismic Slam and Stone Aegis. Hurt/down hold single poses rather than an animated fall.

Sixteen preserved source sheets supply 528 packed frame keys selecting 384 distinct complete figures. The corrected two-figure Crystal rear-left punch replaces draft poses that showed too much face. Both draft and correction are retained; no head or arm patches. Ready and recovery reuse idle figures. Whole-figure scaling and nearest-neighbour packing preserve anatomy.

All three match the approved Granite Golem at 172 authoring pixels and runtime scale 0.32 (about 55 world pixels). [Battle](battle.png), [overworld lineup](overworld-lineup.png), [town lighting and foreground occlusion](overworld-lighting-occlusion.png), [six beat-timed specials](specials-in-game.png). The 24-second combined reel shows all three together at common zoom and four representative facings; the gallery selects any of eight directions. HUD portraits crop the front head and active-pose clearance raises health bars above overhead fists.

Walking is 4 fps with alternating planted feet. Punch contact is 0.4 s with recovery through 0.8 s. Seismic Slam raises both fists before striking the ground at 0.5 s and recovering through 1 s. Stone Aegis gathers then crosses the forearms, contacting at 0.4 s and finishing at 0.8 s. Self abilities face the nearest opponent; existing effects and sound remain. No stats, rules, ability definitions, damage, resistances, encounter placements, old runtime art or dialogue portraits change. Review lineups are development art fixtures, not new encounters.

## Verification

- Host and test builds: zero warnings/errors.
- Focused attachment, motion, scale, HUD, atlas, board and beat checks: **564 passed**.
- Full presentation suite: **3,093 passed / 3,096 total**; only the same three pre-existing story-count failures remain ([log](checks/full.log)). Their expected/actual counts remain 590/614 speakers, 921/945 lines and 8/32 camp reflections, with [prior baseline evidence](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). Full solution not rerun for this art/presentation batch.
- All 528 packed keys pass source-edge, neighbour and projected-bound checks. Every idle/walk/punch/slam/guard has four distinct pixel keys in all eight facings. Corrected Crystal rear-left punch is selected; rejected draft poses 6/7 are excluded. All 16 source PNGs match their generation-ID originals exactly.
- Browser validation decodes all five embedded scenes, exercises seven actions and walking frames, and fits a 390 px phone viewport without horizontal overflow.
- Native Godot battle, world and replay reviewed for contrast, party scale, connected anatomy, rear views, front portraits, bar clearance and contact/recovery. Six specials use the ordinary beat/FX path. Software rendering is a visual check, not a performance benchmark; Dummy audio means the video is silent.
- Scope receipts preserve all prior creature entries/runtime art, rules/abilities/FX, portraits, completed combat tracker and historical 98-look baseline. Three new entries/atlases only; baseline and production HTML append a navigation link.

## Reproduce captures

Use Godot Mono 4.7.2 with the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Host arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:stone-sentinels --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=stone-sentinels-moves --shots=/absolute/move-shots --quit-after=36
--preset=low --world-demo=ashen_ford:stone-sentinels '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`. Then run special-scenes.cjs, review.cjs and verify-review.cjs. Natural hits and six special-contact captures use engine beat-clock moments; manual anticipation captures remain separate. Video includes three walks, three natural strikes, six specials and return to ready.
