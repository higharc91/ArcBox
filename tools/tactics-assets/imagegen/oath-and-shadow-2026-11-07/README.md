# Halvard and Drow / Ysolde production sources

Scenario concepts and receipts are in `../../concepts/oath-and-shadow-2026-11-07/`. These follow the existing lore, equipment and shared character presets. Halvard is an oathbound former human in infernal-fire-blackened full plate, a closed great helm and a worn burgundy/ivory split tabard. The Drow preset also supplies Ysolde, so the shared female elf design retains grey-lilac skin, silver-white hair and practical light leather, with a slate-teal/plum palette. No PixelLab assets are used.

Image generation authors the complete pixel figures and six expression busts per role. `prompts-final.json`, `source-images.json` and `source-sha256.json` retain exact prompts, generation IDs and original source hashes. `packing.json` is the authoritative selected-pose list. Initial Halvard walk/attack sheets crowded neighboring rows and clipped a blade tip; `*-crowded-rejected.png` drafts are retained for provenance and excluded from packing. Regenerated tall sheets keep complete sword arcs isolated. Technical tools never erase, repaint, mirror, deform or independently move a limb or weapon.

Each role has 136 selected complete poses: four idle, four walk, four attack keys, one hurt, one down and three selected cast keys per eight authored directions. Hurt/down are single poses, not animated falls. Halvard settles his joints/tabard without biological breathing. Both plated gauntlets remain on his greatsword hilt, right by the crossguard and left by the pommel. His physical cleave has deliberate anticipation/impact holds; the existing `slam` binding selects this swing, while `buff` uses a separate bowed sword-guard ward. The Drow holds the brass-cupguard rapier in her right hand through walking and thrusting; the free left arm invokes shadow step. Hair stays fixed at the nape. The game retains ownership of spell/projectile FX.

The technical pipeline measures connected whole figures, applies one fixed uniform scale per source sheet and crops/packs with nearest-neighbor sampling. It rejects source-edge intersections and neighboring figures in a pose's bounding box. Halvard targets 132 upright source pixels at 0.32 runtime scale, in padded 288px cells anchored at [144,256]. The Drow targets 120px at 0.32 scale, in 256px cells anchored at [128,224]. Padding accommodates weapons/gestures rather than increasing world size. Twelve dialogue portraits export at 40px; HUD heads crop 64px from the actual sprite atlases.

Reproduce from repository root with Python (Pillow/NumPy/SciPy), Node (Playwright/Chromium), and ffmpeg:

```sh
python tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07
python tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/portraits.cjs
mkdir -p docs/art/mob-sprite-review/oath-and-shadow
node tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/verify-scope.py
```

Copy the four actual Godot captures listed in the review README before the final gallery/browser commands. Review and validation receipts are in `docs/art/mob-sprite-review/oath-and-shadow/`. No balance, abilities, equipment rules, spawn definitions, lore text, music or player outfits change.
