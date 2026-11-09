# Crypt kin pixel production — issue #272

[Scenario concept and receipt](../../concepts/crypt-kin-2026-11-08/BRIEF.md) · [Review and captures](../../../../docs/art/mob-sprite-review/crypt-kin/README.md).

Vampire Bat, Undead Bear and Crystal Slime are complete authored eight-facing library variants. Existing primary runtime figures supply twelve reference sheets. Fourteen source PNGs are preserved: twelve action sheets, one rejected bat flight draft and one three-pose bear rear-down correction. The 336 packed keys select 336 distinct whole figures. Rejected draft figures and the three superseded bear down poses are not selected. Hurt/down hold single authored poses.

All creative figures come from OpenAI image generation after Scenario concepts. No PixelLab, procedural recolouring, mirrored parts, repainted anatomy or detached effects. Technical operations only copy, crop, uniformly scale, register and pack complete figures. Source IDs, exact prompts, reference inputs, corrections, landmark coordinates and PNG hashes are preserved.

The bat uses 320 px padded cells at [160,240] so raised and lowered wings fit. Other cells are 256 px at [128,224]. Runtime scale is .32 for this batch. Bear neutral authoring height is 116 px; slime 64 px. Bat flight is uniformly scaled using charcoal torso measurements (42 px core) and registered to the torso rather than moving wing tips. The colour/morphology masks locate landmarks only; source RGBA is never repainted or masked. Grounded bear/gel poses register to the base. HeadTop uses the highest neutral silhouette, action clearance includes raised wings/paws, and front portraits frame faces rather than the bear's shoulder hump.

Movement rates: bat 8 fps, bear 5 fps, slime 4 fps; idle 4/3/2 fps respectively. Four natural-action keys run through ready, held windup, contact at .4 s and recovery through .8 s. Existing common attacks use the normal beat path. No active special, rule/stat change or encounter is invented.

Run from repository root with the installed Python imaging libraries and Node/Playwright:

```sh
python tools/tactics-assets/imagegen/crypt-kin-2026-11-08/references.py
python tools/tactics-assets/imagegen/crypt-kin-2026-11-08/measure.py
node tools/tactics-assets/imagegen/crypt-kin-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/crypt-kin-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/crypt-kin-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/crypt-kin-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/crypt-kin-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/crypt-kin-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/crypt-kin-2026-11-08/verify-scope.py
```

Set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the installed browser. The owned exporter disables synthetic head pinning/walk bob for these complete figures; established exporters and runtime images stay untouched.
