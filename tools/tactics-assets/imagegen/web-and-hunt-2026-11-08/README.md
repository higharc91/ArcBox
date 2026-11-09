# Phase Spider, Alpha Wolf and Deep Crab pixel production

[Concepts/receipt](../../concepts/web-and-hunt-2026-11-08/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/web-and-hunt/README.md).

Scenario supplied the shared concept sheet; OpenAI authored complete pixel figures. PixelLab was not used.

13 source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. 336 packed keys select 336 distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

Each creature has eight facings and five actions. Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. Natural strikes contact at .4 s and recover through .8 s, without changing rules, stats, effects or placements.

Smoky lavender spider with eight rooted legs and a front cyan eye cluster. Full rear views show the abdomen and no front face.

Charcoal/silver alpha wolf with a fuller shoulder ruff, grounded diagonal gait, upright ears and a rooted bushy tail.

Navy/cobalt deep crab with weathered teal edges, eight walking legs and two joined pincers; the larger crusher retains its anatomical side.

Three fresh complete rear-down wolf figures replace sideways prone fallbacks; full backs have no face.

Front portraits frame the spider eye cluster, wolf face and crab eyestalks. Live battle/town scenes verify broad grounded arthropod footprints beside the party.

The replay was recorded after the parallel CPU-heavy checks finished so all three engine-timed hits are captured.

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
python tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/references.py
python tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/measure.py
node tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/web-and-hunt-2026-11-08/verify-scope.py
```

The scope verifier compares against `a4d9981e` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
