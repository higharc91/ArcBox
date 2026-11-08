# Kindled production sources

Scenario concepts and job/asset receipts are in `../../concepts/kindled-2026-11-07/`. The cultist and the existing shared Maren / Ember Hierophant appearance use eight authored facings. Maren's concept adopts a left-hand staff and free right casting hand; the cultist holds a right-hand dagger and casts with the left. No PixelLab assets were generated for this batch.

Image generation authored the pixel poses, corrections and portraits. `prompts-final.json`, `source-images.json` and `source-sha256.json` retain exact prompts, generation IDs and source hashes. Original wrong-hand proposals and rejected portraits remain as provenance, not production selections. `packing.json` is the authoritative selection.

Each role has 136 packed poses: four idle, four walk and four physical-attack keys, one hurt pose, one down pose and three selected cast keys in every direction. Cast selects source keys 0, 2 and 3; aliases bind the existing point/summon and related spell animations to these gestures. The original game's projectiles and spell FX stay separate from the figures. Hurt/down are single authored poses, not animated fall transitions.

Maren's corrected rear idle, walk, cast and hurt rows keep the staff on her left side. Her right-profile cast uses an upward gesture rather than the original backward extension. The damage correction puts the staff on the ground in fallen poses. The cultist portrait correction restores the same stubbled male face as his sprite rather than the first proposal's borrowed facial features. Dialogue sheets preserve six expression roles; native battle HUD portraits crop the actual new atlas heads.

The technical pipeline locates complete connected figures, chooses the head rather than an adjacent staff tip for idle/walk alignment, crops whole pose cells, applies a fixed uniform scale per source sheet and packs with nearest-neighbor sampling. It does not paint, deform or independently move weapons. Selected poses touching the original source edge are rejected. Source alpha is preserved; creative cleanup uses image generation.

Reproduce from repository root (Python with Pillow/NumPy/SciPy; Node with Playwright/Chromium; ffmpeg):

```sh
python tools/tactics-assets/imagegen/kindled-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/kindled-2026-11-07
python tools/tactics-assets/imagegen/kindled-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/kindled-2026-11-07/portraits.cjs
node tools/tactics-assets/imagegen/kindled-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/kindled-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/kindled-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/kindled-2026-11-07/verify-review.cjs
```

All variants use 0.32 runtime scale. Visible upright bodies target 120 source pixels for the cultist and 116 for Maren; extra atlas area accommodates weapon/gesture padding. Portrait exports are 40×40 and HUD head crops are 64×64. The shared first-batch exporter accepts this batch's three rows per facing and explicit action offsets. Runtime review and validation logs are in `docs/art/mob-sprite-review/kindled/`.
