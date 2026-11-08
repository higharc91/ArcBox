# Goblin archer, shaman and Vorga production sources

Scenario concepts and their generation receipts are retained in `../../concepts/goblin-roles-2026-11-07/`. `boss-final-concept.png` supersedes the single-pauldron draft. Pixel sprites and portraits were authored with image generation; PixelLab was not used. `source-images.json` records generation IDs, `source-sha256.json` records raw PNG hashes, and `prompts-final.json` records the final prompts and corrections. Earlier prompts and rejected images remain as provenance, not production selections.

`packing.json` selects 384 poses from the authored sheets. The scripts measure, crop, uniformly scale and pack existing image content without repainting or deforming figures. Corrected archer/shaman rows replace wrong-hand or wrong-direction proposals. Shaman cast corrections remove a duplicate staff; background cleanup is an image-generation edit. Vorga's final body/attack sheets replace the rejected shoulder-armor drafts. Shaman and Vorga cast playback deliberately selects source keys 0, 2 and 3, omitting inconsistent intermediate gestures. Portraits include six expression roles each.

Reproduce from the repository root (requires Python with Pillow/NumPy/SciPy, Node with Playwright/Chromium, and ffmpeg):

```sh
python tools/tactics-assets/imagegen/goblin-roles-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/goblin-roles-2026-11-07
python tools/tactics-assets/imagegen/goblin-roles-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/goblin-roles-2026-11-07/portraits.cjs
node tools/tactics-assets/imagegen/goblin-roles-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/goblin-roles-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/goblin-roles-2026-11-07/reel.cjs
```

The shared exporter accepts the source root and per-mob row counts; its first-batch defaults are retained. Atlas dimensions are 1024×2048 for archer and 1280×3840 for shaman/boss. Runtime scale is 0.32, with a common grounded anchor per figure. Archer/shaman bodies target 80 source pixels; boss targets 88. All eight authored facings retain complete weapons within their frames. Hurt/down each use one selected pose.

See `docs/art/mob-sprite-review/goblin-roles/README.md` for runtime review and verification limits.
