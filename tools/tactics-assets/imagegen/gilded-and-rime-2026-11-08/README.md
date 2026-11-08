# Gold Dragon, Frost Troll and Ogre pixel production

[Concepts/receipt](../../concepts/gilded-and-rime-2026-11-08/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/gilded-and-rime/README.md).

Scenario supplied the three-creature concept sheet. Its raised Gold Dragon gesture is excluded: production retains four grounded legs from the approved Red Dragon anatomy. OpenAI authored all complete pixel figures.

26 source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. 560 packed keys select 560 distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

Eight facings per creature. Gold Dragon has eight unique actions; each giant has seven, with existing generic casting aliases sharing its roar gesture. Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. Dragon Jaws contacts at .5 s through 1 s; Breath and Presence at .5 s through 1.2 s, and Wingbeat at .7 s through 1.4 s. Giant strikes/roars contact at .4 s through .8 s; boulders release at .4 s and hit after their flight. Rules, effects, stats and placements remain unchanged.

Gold-scaled mature dragon retains four grounded legs, two dorsal wings and a rooted tail; wings fold for walking and unfold on jaw strikes.

Pale slate-blue Frost Troll and broad taupe Ogre share the approved giant scale, joined empty-hand strikes, fixed short hair and rope-bound hipwraps.

Boulder windups grip the stone; release hands are empty as the existing projectile leaves. Distinct chest/throat roar gestures remain grounded.

Fresh Gold rear-left damage, east walking and northwest Wingbeat figures correct orientation, unwanted walking attacks and overlapping source gutters.

The rejected Ogre throw sheet retained a carried stone at release; a fresh complete sheet shows empty release palms in every facing.

Frenzy Roar FX now waits for the authored .4 s chest gesture across the Troll family; its full .8 s recovery plays without changing gameplay.

The gallery uses lossless compass strips and one-facing phone decoding; original runtime/source pixels are preserved.

Fresh northeast and southeast jaw sequences visibly unfurl both wings; all eight contact silhouettes are checked against the folded idle.

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
python tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/references.py
python tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/measure.py
node tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/verify-scope.py
```

The scope verifier compares against `aed32f95` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
