# Ivo and Oswin production sources

Scenario concepts and receipts: `../../concepts/rime-keepers-2026-11-07/`. Ivo's existing shared necromancer key now reads as a living, exhausted Warden-trained physician: short brown/grey hair, charcoal-blue coat, cream scarf and medicine pouch. Oswin's lich preset reads as the century-undead Warden-Keeper: connected ivory skull, empty sockets, slate-navy robes, ivory stole and an unlit ivory-ring staff. No PixelLab assets were used.

Both use right-hand staves and free left-hand casting, following the clearer handedness in the Scenario concepts. Ivo has a small capped blue-grey glass focus; Oswin's ring is unlit, with no ember-fire. Primary grips remain in the right hand through walking and staff melee; the left hand may briefly support the shaft/wrist in a physical strike. Casting keeps the staff planted and uses the free left arm. Existing game projectiles/spell FX remain separate.

Image generation authored pixel poses, corrections and portraits. `prompts-final.json`, `source-images.json` and `source-sha256.json` preserve prompts, generation IDs and original source hashes; `packing.json` is the authoritative production selection. The first Oswin idle proposal had an unwanted pale matte and is retained as `lich-idle-opaque-rejected.png`; the transparent replacement is selected. Dedicated corrections replace Ivo's front-left/back-right casting with a raised invocation, Oswin's front-left casting with a forward palm, and both back-left hurt poses with right-hand staves. The rejected Ivo oblique-cast proposal remains in provenance. The shared hurt correction calibrates each character from its own reference pose, preserving body scale. Technical tools do not erase or repaint backgrounds or equipment.

Each role has 136 selected complete poses: four idle, four walk, four physical attack keys, one hurt pose, one down pose and three cast keys in each of eight directions. Cast selects source keys 0, 2, 3 and binds the existing cast/point/summon/special aliases. Hurt/down are single poses, not animated falls. Fallen staves lie on the ground while remaining held by the connected right hand. Oswin settles quietly without biological breathing.

The technical pipeline measures connected whole figures, isolates the head from nearby staff tips, applies fixed uniform scale per source sheet and crops/packs with nearest-neighbor sampling. It never paints, mirrors, deforms or independently moves limbs/gear. Selected source-edge intersections fail export. Upright body targets are 120px for Ivo and 122px for Oswin at 0.32 runtime scale, in 256px padded cells anchored at [128, 224]. Padding accommodates gestures rather than changing world size. Twelve dialogue expressions export at 40px; HUD heads crop 64px from the actual atlas.

Reproduce from repository root with Python (Pillow/NumPy/SciPy), Node (Playwright/Chromium), and ffmpeg:

```sh
python tools/tactics-assets/imagegen/rime-keepers-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/rime-keepers-2026-11-07
python tools/tactics-assets/imagegen/rime-keepers-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/rime-keepers-2026-11-07/portraits.cjs
mkdir -p docs/art/mob-sprite-review/rime-keepers
node tools/tactics-assets/imagegen/rime-keepers-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/rime-keepers-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/rime-keepers-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/rime-keepers-2026-11-07/verify-review.cjs
```

Copy the five actual game captures listed in the review README before the final gallery/browser commands. Review and validation receipts: `docs/art/mob-sprite-review/rime-keepers/`.
