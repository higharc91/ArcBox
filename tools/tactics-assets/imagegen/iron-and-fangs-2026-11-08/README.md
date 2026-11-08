# Black Scorpion, Warg Rider and Ironbound Mimic pixel production

[Concepts/receipt](../../concepts/iron-and-fangs-2026-11-08/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/iron-and-fangs/README.md).

Scenario authored the three-creature concept. An OpenAI whole-sheet revision restored the missing goblin on one walking study; both original and selected concept are retained.

16 source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. 336 packed keys select 336 distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

Each creature has eight facings and five actions. Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. Natural strikes contact at .4 s and recover through .8 s, without changing rules, stats, effects or placements.

Slate-edged black scorpion with eight walking legs, two joined pincers and one rooted segmented stinger.

A goblin spearman seated on a grey warg; spear grip, reins, knees and saddle remain connected through movement and damage.

Dark oak and heavy iron chest mimic with front-only latch and rear-only hinges; closed idle/walk and an existing Adhesive Bite attack.

An OpenAI background edit removed the opaque scorpion idle background without repainting anatomy. A fresh fully closed mimic idle sheet replaced an overactive first draft.

Complete authored rear damage corrections preserve the creatures’ rear orientation.

Runtime review uses real Adhesive Bite binding for Ironbound Mimic and joined rider/mount movement.

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
python tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/references.py
python tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/measure.py
node tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/iron-and-fangs-2026-11-08/verify-scope.py
```

The scope verifier compares against `6b767166` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
