# Roadside bandit and Vorga's Host hobgoblin

Scenario concepts and their generation receipts are in `../../concepts/road-and-host-2026-11-07/`. Pixel poses and six-expression portrait sheets were authored with image generation, not PixelLab. `source-images.json` records generation IDs; `source-sha256.json` records raw PNG hashes. Prompts include rejected proposals for review history; only `packing.json` determines which poses ship.

The selection packs 224 complete pose slots across two eight-direction atlases. Each facing has four quiet idle keys, four walk keys, four attack playback slots, one hurt pose and one down pose. Hurt/down are single poses, not animated falls. In the bandit's rear diagonals, two corrected attack keys sit between authored ready/recovery idle poses. This deliberately excludes the mirrored or wrong-hand drafts. Other facings use four attack source poses. No views are runtime mirrors, and weapons are baked into the figures.

`bandit-attack-rear.png` contributes only its full-back row. `bandit-rear-keys.png` contributes the rear-diagonal wind-up/impact poses. The filenames containing `rejected` never enter the runtime. The hobgoblin's front two attack rows replace a clipped first proposal. A narrow image-generation cleanup (`hobgoblin-overhead-clean.png`) removes a disconnected mark from the original full-back wind-up while retaining its pose and body proportions. The alternative four-pose full-back sheet was rejected for inconsistent proportions. Corrected sheets can contain fewer poses than the original proposal; measurement calibrates each sheet as a whole, without independently stretching bodies or weapons.

The technical pipeline locates opaque connected figures, crops complete pose cells, uniformly scales and packs them with nearest-neighbor sampling. It does not repaint or deform anatomy. `sourceEdge` rejects selected poses that touch their original canvas boundary, even when unselected rejected frames in the same sheet touch it. The shared first-batch exporter checks projected frame bounds and pixel occupancy. Source PNGs retain original alpha; no algorithmic background painting is used.

Reproduce from repository root (Python/Pillow/NumPy/SciPy, Node/Playwright with Chromium, ffmpeg):

```sh
python tools/tactics-assets/imagegen/road-and-host-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/road-and-host-2026-11-07
python tools/tactics-assets/imagegen/road-and-host-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/road-and-host-2026-11-07/portraits.cjs
node tools/tactics-assets/imagegen/road-and-host-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/road-and-host-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/road-and-host-2026-11-07/reel.cjs
```

Bandit uses 208px frames, hobgoblin 224px, each with eight columns and two rows per facing. Runtime scale is 0.32; visible upright bodies target 120 and 132 source pixels respectively. The frame sizes provide weapon padding rather than changing world size. Battle portraits crop a 64px square from the corresponding atlas; dialogue portraits export six distinct expression roles at 40×40. Runtime review is in `docs/art/mob-sprite-review/road-and-host/`.
