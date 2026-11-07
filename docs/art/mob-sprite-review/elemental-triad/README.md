# Air, Earth and Fire Elementals — batch fourteen

[Standalone animation gallery](index.html) · [Before/after](before-after.png) · [Three-creature motion reel](elemental-triad-motion.mp4) · [GIF](elemental-triad-motion.gif) · [Actual Godot replay, silent](godot-specials.mp4)

Three refreshed looks: `bosses.air_elemental.gale`, `bosses.earth_elemental.stone`, `bosses.fire_elemental.ember`. The existing colour variants keep their art. Twenty-seven looks refreshed across fourteen batches; Water Elemental and Red Dragon remain. No PR opened.

## Art and animation

Scenario supplied three concept sheets; OpenAI authored fifteen transparent pixel sheets. [Concepts and receipts](../../../../tools/tactics-assets/concepts/elemental-triad-2026-11-07/BRIEF.md), [pixel provenance and reproducible pipeline](../../../../tools/tactics-assets/imagegen/elemental-triad-2026-11-07/README.md). 384 distinct authored figures supply 528 packed frame keys, including ready/recovery reuse. All eight facings have idle, movement, natural strike, hurt, down and two distinct specials. Hurt/down hold one pose each.

Air's connected wind ribbons glide on a single vortex base, spread for Whirlwind and thrust both palms for Gale Blast. Earth alternates heavy rock feet, punches, raises and slams both fists for Tremor, then crosses its forearms for Aegis. Fire flows on its continuous flame column, thrusts a connected hand, spreads its flame fans for Surge and opens its mouth toward the enemy for Breath. Front faces are absent from rear views.

At runtime scale 0.32, neutral heights are approximately 46 px/56 px/49 px beside the 1.75 m fighter. Earth receives extra transparent padding for wide punches and raised fists, without increasing its body scale. HUD portraits show the front head; action-specific overhead clearance keeps bars above raised limbs. [Battle](battle.png), [overworld party lineup](overworld-lineup.png), [world lighting/occlusion](overworld-lighting-occlusion.png), [six specials](specials-in-game.png).

Visual contact and existing FX align at 0.4 s except Tremor at 0.5 s. Full recovery completes at 0.8 s, or 1 s for Tremor/Breath. Breath's aimed effect shares its pose heading and attaches 30 world pixels above the floor at the taller body's mouth. Existing lightning, smoke, dust, shield and fire effect artwork/bindings are retained; no new mechanics, stats, encounter placement, audio or dialogue portraits. The development replay uses real ability presentations through the normal Godot beat path; its five-unit lineup is a review fixture rather than a new encounter.

## Verification

- Host and presentation-test builds: zero warnings/errors.
- Focused art/board/HUD/beat regression checks: 498 passed.
- Full presentation suite: 3,032 passed / 3,035 total; the same three pre-existing story-count failures remain (`checks/full.log`). These have baseline evidence in [ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). No broader full-solution run for this batch.
- Every selected source figure passes source-edge/neighbour checks; all 528 packed keys pass projected bounds checks. Each of the eight directions has four distinct keys for idle, movement, natural strike and each special.
- Browser validation decodes all five embedded review scenes, checks seven actions and moving frames, and fits a 390 px phone viewport without horizontal overflow. All gallery sprite/scenery images are embedded.
- Scope receipts preserve prior creature entries, art, rule/ability JSON, portraits and the original 98-look catalog. Only three creature entries/PNGs are added.
- Native Godot battle/world/move captures reviewed for scale, attached arms, forward contacts, raised-fist clearance, portraits and recovery. Software rendering is a visual check, not a performance benchmark. Dummy audio prevents host-device errors; replay is silent.

## Reproduce game captures

Godot flags before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920 x 1080`. Host arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:elemental-triad --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=elemental-triad-moves --shots=/absolute/move-shots --quit-after=36
--preset=low --world-demo=ashen_ford:elemental-triad '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Collect with `python tools/tactics-assets/imagegen/elemental-triad-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`. Run special-scenes.cjs then review.cjs and verify-review.cjs. Physical contact screenshots use the beat's hit clock. Six special captures show release; windup captures are also retained. Video shows complete walks, three natural attacks, all six specials and return to ready.
