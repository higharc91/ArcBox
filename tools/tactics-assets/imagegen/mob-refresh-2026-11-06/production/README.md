# First approved mob batch

Integrated keys: `characters.goblin_cutthroat`, `characters.orc_brute`, `beasts.drake.red` and its existing `beasts.drake` alias. All other mob roles, named designs and drake colors retain their own art. This is the first batch of the wider refresh, not a claim that the entire catalog has been replaced.

Concepts were authored in Scenario and the owner approved the goblin/orc style and revised winged drake. OpenAI image generation authored the pixel poses locally; PixelLab was not used. See `generation-record.json` for source generation image IDs, hashes and corrections. Original generated files remain under `/workspace/generated_images` in the authoring workspace; portable copies are in `source/`.

Each mob has eight genuine compass facings, four authored quiet-idle poses, four authored walk keys, four attack source poses, one hurt pose and one collapsed pose per facing: 112 selected poses each, 336 total. Attack playback uses short anticipation/peak holds and an idle recovery; the drake's recovery reuses the half-open wing pose instead of a source pose that turns its head back towards the camera. Hurt/down are single authored poses, not multi-frame transitions. Weapons and wings are baked into the complete figure; runtime does not move a separate weapon or wing layer. No mirroring is used.

The drake has four legs plus two wings. Folded idle/walk preserve its grounded silhouette, and the attack/special visibly spread both wings. Its combat stats, flight flag, abilities, AI and encounter placement are unchanged. Goblin and orc dialogue portraits have all six original expression roles, exported at 40px. Battle HUD portraits crop the actual new atlas head.

`measure.py` identifies complete source actors without cutting approximate generated grid boundaries. Each action uses a fixed source-to-native scale; corrected source sheets have explicit separate calibration because the image generator outputs a different resolution. `export.cjs` copies whole poses with Canvas nearest-neighbor sampling, aligns quiet-idle ground contact and walk heads, and rejects clipped silhouettes. No script repaints anatomy, recolors art, deforms limbs or manufactures motion by warping sprites. `packing.json` and `export/*/checks.json` record every chosen source pose and exported bounds. The raw `drake-idle.png` turnaround is a design reference; `drake-idle-breath.png` supplies production idle frames.

Rebuild from the repository root (Python with Pillow/NumPy/SciPy; Node with Playwright/Chromium; FFmpeg for the reel):

```sh
python tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs
python tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/integrate.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/portraits.cjs
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/review.cjs
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/reel.cjs
```

The standalone review in `docs/art/mob-sprite-review/production/index.html` exposes every facing and action. The motion GIF/MP4 shows four representative facings at a common world zoom; the full HTML includes all eight. Godot evidence and verification are recorded alongside it. Development-only `--tactics-demo=river_crossing:mob-refresh` places the batch beside the fighter and newer kobold through the real battle renderer. `--world-demo=ashen_ford:mob-refresh` adds a spaced review formation to the real region renderer; it does not spawn enemies in the town during play.

The drake renders on its canonical 256px authoring grid before a lossless anchor-relative crop to a 192px runtime frame. All 112 RGBA poses were checked against that crop. This avoids fractional nearest-neighbor rounding changing when the canvas origin moves, and removes 14 MiB of empty texture allocation from the first authoring layout (32 → 18 MiB). The atlas is 1536×3072.
