# Grove Sapling sources and integration

Scenario concept and receipts: `../../concepts/grove-ward-2026-11-07/`. Five original generated transparent pixel sheets, exact prompts and generation IDs are preserved here. PixelLab was not used. Scripts select complete authored figures, crop and uniformly scale with nearest-neighbour sampling, then pack them. No mirrored anatomy, independently moved parts or pixel repainting.

112 packed frame keys select 96 distinct source figures: 32 idle, 32 root-walk, 8 windups from the draft attack sheet, 8 corrected contacts and 16 hurt/down figures. Ready/recovery reuse matching idle figures. Eight draft contacts point backward or switch arms and are rejected; none reach the atlas. Some rejected draft bounding boxes overlap the next row, so only those discarded boxes bypass the source-neighbour assertion. Every selected figure retains strict source-edge/neighbour checks and is exported intact. Corrections select whole contact figures, not limb patches.

The vine stays on the concept's anatomical left arm (front image-right, rear image-left). Corrected contacts look and lash toward the target, keeping the vine rooted through the wrist. Compact crown leaf masses remain connected to their twigs; root feet and branch arms remain connected to the trunk. Hurt/down are single poses, not an animated fall. Down rests the joined plant sideways.

Neutral idle/walk/attack target 64px crown-to-root-floor height; hurt targets 60px. Both crown leaf masses register the head together, avoiding a jump between separate clusters. Contact figures anchor on brown wooden root feet, not a green vine tip reaching below them. Scale stays fixed within each source sheet. Padding is 320px at [160,192], leaving room in front of and behind the root feet for the full physical lash. Runtime scale 0.32; two rows per facing. The visible young tree remains roughly waist-high beside the party. HUD crop [133,125,54,54] includes both crown masses and its bark face. Existing optional overhead clearance keeps bars above raised movement/attack pixels without resizing the plant.

Idle 3fps; root-walk 4fps; attack [8,9,9,9,10,10,11,0] at 10fps. The existing physical lunge now handles this companion's generic common.attack at both adjacent and two-tile reach, contacts at 0.4s and finishes through 0.8s. It no longer falls through to an inferred arrow. Explicit projectile/FX overrides are preserved. No rules, damage, range, resistances, active abilities, encounter placement, audio/FX assets or other plant variants change.

From repository root with Pillow/NumPy/SciPy and Node Canvas/Playwright available:

```sh
python tools/tactics-assets/imagegen/grove-ward-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/grove-ward-2026-11-07
python tools/tactics-assets/imagegen/grove-ward-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/grove-ward-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/grove-ward-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/grove-ward-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/grove-ward-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/grove-ward-2026-11-07/verify-scope.py
```

Set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for installed runtimes as needed. Capture collection arguments are in the review README. Scope compares against 79d4317b; prior art, rule/ability JSON, portraits and the historical baseline stay unchanged.
