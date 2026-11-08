# Autumn Sapling, Blossom Sapling and Shade pixel production

[Concepts/receipt](../../concepts/autumn-and-veil-2026-11-08/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/autumn-and-veil/README.md).

Scenario supplied the initial concept; an OpenAI whole-concept revision corrected sapling vine handedness and removed human beard anatomy. OpenAI authored all complete pixel figures.

20 source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. 368 packed keys select 368 distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

Eight facings each. Five unique actions per sapling and six for Shade; the existing special alias selects its Life Drain gathering clip. Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. Sapling lashes and Shade touch/drain contact at .4 s and recover through .8 s. Primary Wraith touch/drain now complete their existing .6/.625 s clips at .3/.375 s contact; its art is unchanged. Rules, stats, ranges and FX remain unchanged.

Small seasonal saplings preserve rooted twin crowns, warm bark, root-foot walking and their attached anatomical left vine.

Charcoal-violet Shade drifts with a continuous robe tail and joined grey hands; no legs, weapons or detached wisps.

Directional physical vine lashes and spectral touches use full contact/recovery; Shade retains its existing targeted Life Drain with a distinct gathering gesture.

Seven mixed-species drafts are preserved but excluded. Fresh single-creature sheets replace them; material identity is checked across every selected facing/action.

Vine attack registration follows brown root feet, never the low lash tip; the full authored figures are copied without deformation.

Plain rear crowns and hoods face away; attached arms, vine and continuous robe remain intact.

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
python tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/references.py
python tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/measure.py
node tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/autumn-and-veil-2026-11-08/verify-scope.py
```

The scope verifier compares against `f01f6875` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
