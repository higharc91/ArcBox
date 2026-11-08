# Troll and granite Golem production sources

Scenario references and receipts: `../../concepts/stone-and-mire-2026-11-07/`. The existing troll uses natural claws, boulder toss and frenzy roar; it keeps a rangy hooked profile, long forearms, olive-grey skin, fixed short crest and rope-bound hipwrap. The granite Golem uses carved masonry, rigid articulated joints, dark eye recesses and restrained fixed moss, distinguishing a built construct from an Earth Elemental. No PixelLab dependency or new canonical lore is introduced.

Image generation authors complete figures. `prompts-final.json`, `source-images.json` and `source-sha256.json` retain exact prompts, generation IDs and source hashes. `packing.json` selects 352 complete poses: 176 per role, with four idle, four walk, four melee, four primary-special and four buff keys plus one hurt and one down pose for each of eight authored facings. Hurt/down are single poses, not animated falls.

The initial troll heave sheet crowded its raised boulders and shortened its recovery stance; `troll-special-crowded-rejected.png` is retained and excluded. Two roomier four-facing sheets replace it through `source-sets.json`. A dedicated full-back strip replaces that row in `corrections.json`, keeping both arms raised in a symmetrical release away from the camera. Held rocks touch both hands in anticipation, and are absent after release so the existing game projectile can take over.

Only technical measurement, whole-figure positioning, uniform scaling and nearest-neighbor packing follow generation. No tool paints, erases, mirrors, deforms or independently moves anatomy or equipment. Source isolation, complete alpha bounds and destination padding remain strict. Fallen giants centre the whole collapsed body horizontally on the tile rather than using the lowest hand as the centre; the entire figure moves together and retains its ground height.

Upright source targets are 188px for the troll and 172px for the Golem at a common 0.32 runtime scale. The troll uses padded 320px cells anchored at [160,288] for its raised rock; the Golem fits 256px cells at [128,224]. Both use eight columns and three rows per facing. Cell padding does not enlarge world bodies. HUD heads crop 64px from the current front-facing head position; no dialogue presets use these creatures, so existing dialogue portraits are untouched.

The troll walks at 5fps and the Golem at 4fps. Physical strikes use anticipation/impact holds and 0.8s presentation clips. Boulder release occurs at 0.4s, matching the transition from held rock to empty hands. Seismic fists land at 0.5s, when the existing dust and damage begin, with recovery continuing through the 1s clip. Roar and aegis use separate 0.8s buff clips. Two ability presentation strings change from `bosses.special` to `bosses.buff`; the boss manifest registers buff as a legacy special alias so old library variants retain their animation. Optional per-animation `overheadClearance` metadata raises the health/status stack during these overhead specials so it does not cover the held boulder or raised fists. Clearance is measured from the exported alpha bounds, leaves body/head scale intact, and defaults to zero for every old sprite. Combat mechanics, projectiles, FX, balance, spawns, music, lore and player outfits remain unchanged.

Reproduce from repository root using Python (Pillow/NumPy/SciPy), Node (Playwright/Chromium) and ffmpeg:

```sh
python tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/stone-and-mire-2026-11-07
python tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/integrate.py
python tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/verify-scope.py
mkdir -p docs/art/mob-sprite-review/stone-and-mire
node tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/special-scenes.cjs
node tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/verify-review.cjs
```

Copy the actual Godot captures listed in the review README before the final special-scene/gallery/browser commands. `--tactics-demo=stone-and-mire-moves` feeds the four real ability presentations through the normal TV beat path, with no server or new encounters.
