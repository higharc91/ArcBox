# Hunting Hound sources and integration

Scenario concept/receipt: `../../concepts/trail-companion-2026-11-07/`. Four original generated transparent pixel sheets, exact prompts and generation IDs are preserved here. PixelLab was not used. Scripts select complete authored figures, crop, uniformly scale with nearest-neighbour sampling and pack them; no mirroring, independently moved parts or pixel repainting.

112 distinct authored poses across eight directions: four idle, four walk, four natural bite, one hurt and one down per direction. Hurt/down are single poses, not an animated fall. Down is a grounded resting/slumped hound with head against its forepaws; collapse can turn the resting head sideways. No howl, special, buff or cast is added: the existing companion has a natural bite and no active abilities.

Facings are authored in runtime order; facings.json, source-sets.json and corrections.json intentionally select no alternate sources. Idle/walk/attack target 56px head-to-floor height from the neutral reference; hurt targets 52px with shoulders lowered. Fixed scale within each source sheet preserves real pose changes. The shared exporter registers walking and idle to the neutral head position, with only a one-pixel walk bob; ears/collar remain connected to the head/neck and the tail remains connected to the rump. 256px cells, [128,224] anchors, two rows per facing and 0.32 runtime scale keep the dog below human waist height.

The actual front head, floppy ears, muzzle and collar fit a [109,165,38,38] HUD crop. Idle 3fps; walk 6fps; bite [8,9,9,9,10,10,11,0] at 10fps. The natural melee beat lasts 0.8 seconds, contacts at 0.4 seconds and finishes its recovery. Existing combat definitions, abilities, passive, damage, FX, audio, spawns and hound library variants remain unchanged.

From repository root with Pillow/NumPy/SciPy and Node Canvas/Playwright available:

```sh
python tools/tactics-assets/imagegen/trail-companion-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/trail-companion-2026-11-07
python tools/tactics-assets/imagegen/trail-companion-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/trail-companion-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/trail-companion-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/trail-companion-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/trail-companion-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/trail-companion-2026-11-07/verify-scope.py
```

Set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH to installed runtime locations as needed. Compare uses the unchanged historical hound and a matching refreshed right profile beside the fighter at the same world zoom. Capture collection arguments are in the review README. Scope compares against 05c4fb65; historical inventory and embedded baseline art remain unchanged.
