# Air, Earth and Fire Elemental production sources

Scenario concepts and receipts: `../../concepts/elemental-triad-2026-11-07/`. Fifteen original generated transparent pixel sheets, exact prompts and generation IDs are preserved here. PixelLab was not used. Scripts select complete authored figures and uniformly scale them with nearest-neighbour sampling. No mirrored anatomy, independently moved limbs or pixel repainting.

528 packed frame keys select 384 distinct source figures: per creature, 32 idle, 32 walk, 16 melee windup/contact, 32 paired-special windup/contact and 16 hurt/down figures. Ready and recovery use matching idle figures. Hurt/down are single held poses, not animated falls. Earth settles into a low connected rock crouch; Air and Fire collapse into connected knots. Each action includes all eight facings, with faces confined to forward views.

Neutral visible authoring heights are Air 144 px, Earth 176 px and Fire 152 px; runtime pixelScale 0.32 yields approximately 46/56/49 world pixels beside the 1.75 m fighter. Each source sheet uses one uniform scale, including anticipation and contact. Air/Fire cells are 256 px anchored[128,224]. Earth cells are 320 px anchored[160,256], adding transparent room around broad punches and raised fists without enlarging its body. Strict source-edge/neighbour and projected export bounds checks pass for every selected figure. Head registration stabilizes idle/walk; natural attack roots remain anchored as whole figures.

Air idle 4 fps/walk 6 fps; Earth idle 3 fps/walk 4 fps; Fire idle 6 fps/walk 6 fps. Basic natural attacks run 0.8 s with contact at 0.4 s. Whirlwind/Gale Blast, Aegis and Flame Surge run 0.8 s/contact 0.4 s; Tremor runs 1 s/contact 0.5 s; Flame Breath runs 1 s/release 0.4 s. Ready, anticipation, contact and recovery are separate keys. Existing renderer clearance lifts bars above raised poses. HUD portraits crop the front head, independently of the all-facing highest-pixel clearance.

BeatScript selects these gestures only for the three refreshed sprite keys with their existing presentation bindings. It synchronizes their existing effects and subsequent damage to contact and finishes the full recovery. Aimed breath uses the same nearest-foe heading as its pose and a 30 px mouth lift instead of the shared humanoid 15 px body anchor. Legacy colour variants and the drake keep their current clips and timing. No rule JSON, damage, HP, range, resistances, encounter placement, sound or effect artwork changes.

From repository root:

```sh
python tools/tactics-assets/imagegen/elemental-triad-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/elemental-triad-2026-11-07
python tools/tactics-assets/imagegen/elemental-triad-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/elemental-triad-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/elemental-triad-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/elemental-triad-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/elemental-triad-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/elemental-triad-2026-11-07/verify-scope.py
```

Use installed NODE_PATH/PLAYWRIGHT_BROWSERS_PATH as needed. Screenshot collection and game arguments are in the review README. Scope compares against 3b957490, preserving prior art, rules, portraits and the historical baseline.
