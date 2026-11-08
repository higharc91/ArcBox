# Azure, Radiant and Shadow Fire Elemental pixel production

[Scenario concept and receipt](../../concepts/prismatic-flames-2026-11-07/BRIEF.md). Scenario supplied the shared three-column concept sheet; OpenAI authored all complete pixel figures. No PixelLab, procedural recolouring, mirrored parts or anatomy patches.

`references.cjs` technically copies whole approved Ember Fire Elemental runtime figures into 15 transparent pose sheets. Inputs preserve the primary anatomy, eight-facing order and action geometry. Exact prompts, input paths, generation IDs and PNG hashes are retained. The shared concept is used only for each named variant's material palette. Source frames are authored whole, then packed with uniform sheet scaling and nearest-neighbour sampling.

Sixteen preserved source sheets supply 528 packed frame keys selecting 384 distinct figures: per variant 32 idle, 32 gliding, 16 natural windup/contact, 32 paired-special windup/contact and 16 hurt/down poses. Ready/recovery reuse idle. The first Radiant strike sheet had a background bridge between two figures; it is preserved as an unselected draft. A fresh complete sheet replaces it. Hurt/down are held poses rather than animated falls. All arms and curled bases remain joined; no detached sparks or drawn external ability projectiles.

Neutral height 152 authoring pixels and runtime pixelScale 0.32 match Ember (about 49 world pixels). Cells are 320 px at anchor [160,256], allowing full extended-arm poses without enlarging the body. Movement head registration stabilizes the crest; HUD portraits crop the front head and active-pose clearance keeps bars above flame tips.

Idle/gliding are 6 fps. Natural strikes and Flame Surge contact at 0.4 s, finish at 0.8 s. Flame Breath contacts at 0.4 s and finishes at 1 s. Four pose keys cover ready, anticipation, contact and recovery. All seven actions retain eight facings. Exact variant keys select Ember's dedicated visual gestures/timing; other appearances remain unchanged. Existing shared fire effects/audio retain their assets and color. Six beat-clock special-contact moments capture real engine contact.

No rule/ability/FX JSON, stats, resistances, damage, range, sound, encounter placement, prior runtime art or dialogue portraits change. Five-unit lineups/replay are development art fixtures, not new encounters. The completed 29-design combat tracker remains untouched; additional-library progress is tracked separately.

From repository root, with Node/Playwright and Python imaging dependencies installed:

```sh
node tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/references.cjs
python tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/prismatic-flames-2026-11-07
python tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/prismatic-flames-2026-11-07/verify-scope.py
```

Set NODE_PATH/PLAYWRIGHT_BROWSERS_PATH for the installed browser. Scope compares against `619dba6a`. [Review and Godot capture reproduction](../../../../docs/art/mob-sprite-review/prismatic-flames/README.md).
