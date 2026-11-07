# Azure, Radiant and Shadow Fire Elementals — library batch 3

[Standalone animation gallery](index.html) · [Before/after](before-after.png) · [Three-creature motion reel](prismatic-flames-motion.mp4) · [GIF](prismatic-flames-motion.gif) · [Actual Godot replay, silent](godot-specials.mp4)

Refreshes `bosses.fire_elemental.azure`, `bosses.fire_elemental.radiant` and `bosses.fire_elemental.shadow`. The 29-design combat pass stays unchanged. This third library batch brings the branch to 38 refreshed designs across 18 batches. [Library tracker](../production/library-refresh-status.json): nine of 31 older two-direction appearances refreshed; 22 remain. Another 26 already have eight-direction art and retain it. No PR opened or merge performed.

## Art and animation

[Concepts/receipt](../../../../tools/tactics-assets/concepts/prismatic-flames-2026-11-07/BRIEF.md) · [Pixel pipeline/provenance](../../../../tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/README.md). Scenario supplied a single shared concept sheet with three material columns; OpenAI authored all complete pixel figures. PixelLab was not used.

Azure uses deep blue flame planes and icy cyan edges, Radiant uses amber/gold and cream highlights, and Shadow uses plum/violet with restrained lavender edges. They share the approved Ember elemental's joined arms, continuous flame column and curled base, with no legs, clothes or weapons. Full backs have no front face. Eight facings have idle, gliding, natural palm strikes, hurt/down, Flame Surge and Flame Breath. Hurt/down hold single poses rather than animated falls.

Sixteen preserved source sheets supply 528 packed keys selecting 384 distinct whole figures; ready/recovery reuse idle. Radiant's first strike sheet is preserved as an unselected draft because a background artifact joined two figures; a fresh complete sheet replaces it. Each new whole figure follows an approved primary pose. Packing uses uniform sheet scaling and nearest-neighbour sampling. No recoloured atlas copies, mirrored limbs or head/hand patches.

All three match Ember at 152 authoring pixels and runtime scale 0.32 (about 49 world pixels). [Battle](battle.png), [overworld lineup](overworld-lineup.png), [town lighting/occlusion](overworld-lighting-occlusion.png), [six beat-timed specials](specials-in-game.png). The 24-second combined reel shows all three at common zoom in four representative facings; the gallery selects any of eight directions. HUD portraits crop the front head; active-pose clearance lifts bars above the silhouette.

Gliding and idle run at 6 fps. Natural palm strikes and Flame Surge contact at 0.4 s and recover through 0.8 s. Flame Breath has a separate forward-leaning exhale at 0.4 s and recovery through 1 s. Self abilities face the nearest opponent. Existing shared fire effects/audio retain their assets and color. No stats, mechanics, ability definitions, resistances, damage, encounter placements, older runtime images or dialogue portraits change. Review lineups are development art fixtures, not new encounters.

## Verification

- Host and test builds: zero warnings/errors.
- Focused attachment, motion, scale, HUD, atlas, board and beat checks: **583 passed**.
- Full presentation suite: **3,112 passed / 3,115 total**; only the same three pre-existing story-count failures remain ([log](checks/full.log)). Their expected/actual counts remain 590/614 speakers, 921/945 lines and 8/32 camp reflections, with [prior baseline evidence](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). Full solution not rerun for this art/presentation batch.
- All 528 packed keys pass source-edge, neighbour and projected-bound checks. Every idle/glide/strike/surge/breath has four distinct pixel keys in all eight facings. All 16 preserved source PNGs match their generation-ID originals exactly.
- Browser validation decodes all five embedded scenes, exercises seven actions and movement frames, and fits a 390 px phone viewport without horizontal overflow.
- Native Godot battle, world and replay reviewed for contrast, party scale, joined anatomy, rear views, front portraits, bar clearance and contact/recovery. Six specials use the ordinary beat/FX path; shared fire effect assets and color remain unchanged. Software rendering is a visual check, not a performance benchmark. Dummy audio means the video is silent.
- Scope receipts preserve all prior creature entries/runtime art, rules/abilities/FX, portraits, completed combat tracker and historical 98-look baseline. Three new entries/atlases only; baseline and production HTML append a navigation link.

## Reproduce captures

Use Godot Mono 4.7.2 with the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Host arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:prismatic-flames --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=prismatic-flames-moves --shots=/absolute/move-shots --quit-after=36
--preset=low --world-demo=ashen_ford:prismatic-flames '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`. Then run special-scenes.cjs, review.cjs and verify-review.cjs. Natural hits and six special-contact captures follow engine beat-clock moments; manual anticipation captures remain separate. Video includes three glides, three natural strikes, six specials and return to ready. Dummy audio means it is silent.

Next: [Black Hound, Grey Hound and Violet Imp](../night-companions/README.md), library batch 4.
