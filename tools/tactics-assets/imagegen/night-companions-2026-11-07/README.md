# Black Hound, Grey Hound and Violet Imp pixel production

[Scenario concepts and receipt](../../concepts/night-companions-2026-11-07/BRIEF.md). Scenario supplied a shared three-column concept sheet; OpenAI authored the complete pixel figures. No PixelLab, procedural recolouring, mirrored parts or anatomy patches.

`references.cjs` technically copies complete approved Tan Hound/Crimson Imp runtime poses into 12 transparent reference sheets. Corrected primary poses supply eight-facing anatomy and natural-action geometry; the shared concept supplies each selected material. Exact prompts, inputs, generation IDs and PNG hashes remain preserved.

Thirteen preserved source sheets supply 336 packed keys selecting 336 distinct whole figures: per variant 32 idle, 32 walking, 32 complete natural-action keys and 16 hurt/down. The first imp idle draft included casting gestures; it remains preserved but unselected. A fresh whole sheet keeps both hands relaxed down. No head/wing/hand patches. Hurt/down hold single poses rather than animated falls.

Neutral authoring height 56 px and pixelScale 0.32 match the existing Tan Hound/Crimson Imp (about 18 world pixels), with 256 px cells at [128,224]. Empty cell padding does not enlarge the animals. Whole-figure uniform scaling and nearest-neighbour packing preserve anatomy. Movement head registration stabilizes the head; front portraits crop the head/horns and active-pose clearance keeps bars above the silhouette.

Idle is 3 fps; movement 6 fps. Four action keys cover ready, windup, contact/release and recovery. Hound bites contact at 0.4 s and recover through 0.8 s. Violet Imp's natural hellfire launches at 0.4 s and completes its clip through at least 0.8 s; longer projectile travel remains supported. Explicit projectile presentations retain priority. No active special abilities are invented for these companions. All five actions retain eight facings and joined paws/jaws/tails/wing roots/hands.

No stats, rules/ability/FX JSON, damage, range, sound assets, encounter placement, prior art or dialogue portraits change. The exact two hound keys inherit Tan's visual bite timing; Violet inherits Crimson's natural hellfire presentation. Review lineups/replay are development art fixtures. The completed 29-design combat tracker stays unchanged; extra library appearances are tracked separately.

From repository root, with Node/Playwright and Python imaging dependencies installed:

```sh
node tools/tactics-assets/imagegen/night-companions-2026-11-07/references.cjs
python tools/tactics-assets/imagegen/night-companions-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/night-companions-2026-11-07
python tools/tactics-assets/imagegen/night-companions-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/night-companions-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/night-companions-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/night-companions-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/night-companions-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/night-companions-2026-11-07/verify-scope.py
```

Set NODE_PATH/PLAYWRIGHT_BROWSERS_PATH for the installed browser. Scope compares against `d437ecb7`. [Review and native Godot capture reproduction](../../../../docs/art/mob-sprite-review/night-companions/README.md).
