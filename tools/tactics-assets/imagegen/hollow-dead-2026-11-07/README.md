# Risen and wraith production sources

Scenario concepts and job/asset receipts are in `../../concepts/hollow-dead-2026-11-07/`. The Risen shared appearance is one human former Frostpeak pilgrim, with ashen skin, sparse grey hair, moss-olive clothes and a faded cream scarf. The wraith follows the bestiary's cold, memoryless spirit: charcoal-indigo hood, grey spectral hands and one connected floating tail. No PixelLab assets were used.

Image generation authored all pixel poses, corrections and the Risen's portrait sheet. `prompts-final.json`, `source-images.json` and `source-sha256.json` retain prompts, generation IDs and original source hashes. `packing.json` is the authoritative selection. Each role has 112 base poses: four idle, four locomotion and four physical attack keys, one hurt pose and one down pose in each of eight directions. Wraith adds three selected drain keys (source 0,2,3) per direction: 136 total, 248 across the pair. Hurt/down are single poses, not animated fall transitions.

The Risen shuffles at four keys/second; the wraith glides at five, with no feet or stepping. Both idle at three keys/second with quiet settling rather than breathing. Risen slam uses both empty hands. Wraith physical touch uses a visible reaching arm; life drain uses separate two-handed gathering/pulling poses bound to `bosses.special`. Unarmed touch does not require a particular anatomical hand. Dedicated corrections replace its back-right, right-profile and front-right physical attack rows so the reach goes toward the target. Rejected original rows remain as provenance. Flying FX remain owned by the game renderer.

The technical pipeline measures complete connected figures, isolates the head from nearby hands, crops whole poses, applies fixed uniform scale per source sheet and packs with nearest-neighbor sampling. It does not paint, deform, mirror or independently move body parts. Selected source-edge intersections fail export. Upright visual targets are 112px for the stooped Risen and 126px for wraith including its tail, at 0.32 runtime scale, inside padded 224px cells anchored at [112,198]. Six Risen dialogue portraits export at 40px; both HUD heads crop 64px from the actual atlas. Wraith has no existing dialogue preset, so no unused portrait preset is added.

Reproduce from repository root with Python (Pillow/NumPy/SciPy), Node (Playwright/Chromium) and ffmpeg:

```sh
python tools/tactics-assets/imagegen/hollow-dead-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/hollow-dead-2026-11-07
python tools/tactics-assets/imagegen/hollow-dead-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/hollow-dead-2026-11-07/portraits.cjs
mkdir -p docs/art/mob-sprite-review/hollow-dead
node tools/tactics-assets/imagegen/hollow-dead-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/hollow-dead-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/hollow-dead-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/hollow-dead-2026-11-07/verify-review.cjs
```

Copy the five actual game captures described in the review README before the final gallery/browser commands. Review and validation receipts: `docs/art/mob-sprite-review/hollow-dead/`.
