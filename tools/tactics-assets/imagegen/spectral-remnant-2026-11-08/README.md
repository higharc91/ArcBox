# Spectre pixel production

[Concepts/receipt](../../concepts/spectral-remnant-2026-11-08/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/spectral-remnant/README.md).

Scenario supplied the single-creature turnarounds and gesture concept. OpenAI authored all complete pixel figures, matching the primary Wraith reference.

6 source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. 144 packed keys select 144 distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

Eight facings and six unique actions; the existing special alias selects the distinct Life Drain gathering clip. Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. Spectral touch and Life Drain contact at .4 s and recover through .8 s. Rules, stats, ranges, encounter placements and FX are unchanged.

Pale mist-grey hood and robe preserve the approved human-sized Wraith anatomy and cool material planes.

Eight genuine facings have plain rear hoods, grey hands joined to sleeves and a continuous drifting tail without legs or loose wisps.

Natural spectral touch and the existing targeted Life Drain have distinct full gestures; hurt and low collapsed down poses read clearly.

The rejected damage draft remained upright in down cells. A fresh complete sheet supplies low collapsed robes in every facing.

Each hand remains connected to its sleeve, with no carried weapons or detached spectral fragments.

The final singleton batch completes all 31 older library appearances; separate TV/phone playtest feedback remains open on issue #272.

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
python tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/references.py
python tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/measure.py
node tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/spectral-remnant-2026-11-08/verify-scope.py
```

The scope verifier compares against `d5ce1853` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
