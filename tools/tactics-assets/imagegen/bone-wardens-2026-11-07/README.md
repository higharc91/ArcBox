# Bone Warden production sources

Scenario lore concepts and receipts are in `../../concepts/bone-wardens-2026-11-07/`. The skeleton warrior retains a right-hand shortsword and empty left hand; the archer uses a left-hand shortbow, right draw hand and back quiver. Both wear weathered remnants of the same crypt uniform. Empty sockets, slate-blue cloth, charcoal leather and ivory bones follow the Bone Wardens bestiary. No PixelLab assets were used.

Image generation authored all pixel poses and portraits. `prompts-final.json`, `source-images.json` and `source-sha256.json` retain prompts, generation IDs and all thirteen source hashes. `packing.json` is the authoritative selection: 112 poses per role, 224 total. Each facing has four idle, four walk and four attack keys, plus one hurt and one down pose. Hurt/down are single poses, not animated falls. Undead idle uses quiet watching and weight/cloth movement rather than breathing.

The first archer attack proposal has four disconnected release bows. Its rejected cells are explicitly flagged and forbidden in final selections. Dedicated left and rear corrections replace five complete attack rows; the other three original rows remain. The warrior's back-left wind-up and thrust use the corrected right-wrist brace; original right-hand ready/recovery keys remain. Unselected correction keys remain as provenance. Weapons are baked into complete figures, never mirrored or moved independently. Existing game projectiles own flying arrows.

The technical pipeline measures connected whole figures, isolates the head from nearby weapon tips, applies a fixed uniform scale per source sheet, then crops/packs with nearest-neighbor sampling. It does not repaint or deform art. Selected source-edge intersections and disconnected rejected cells fail export. Both upright bodies target 120 source pixels at 0.32 runtime scale, in 224px padded frames anchored at [112,198]. Twelve dialogue portraits export at 40px; HUD heads crop 64px from the actual atlas. Portrait moods use skull tilt, jaw and shadow.

Reproduce from repository root with Python (Pillow/NumPy/SciPy), Node (Playwright/Chromium) and ffmpeg:

```sh
python tools/tactics-assets/imagegen/bone-wardens-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/bone-wardens-2026-11-07
python tools/tactics-assets/imagegen/bone-wardens-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/bone-wardens-2026-11-07/portraits.cjs
mkdir -p docs/art/mob-sprite-review/bone-wardens
node tools/tactics-assets/imagegen/bone-wardens-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/bone-wardens-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/bone-wardens-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/bone-wardens-2026-11-07/verify-review.cjs
```

Capture the five game scenes described in the review README before the last review/browser commands. Review artifacts and validation receipts: `docs/art/mob-sprite-review/bone-wardens/`.
