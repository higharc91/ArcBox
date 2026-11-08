# Black, Blue and Green Dragon pixel production

[Concepts/receipt](../../concepts/wyrm-triad-2026-11-08/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/wyrm-triad/README.md).

Scenario supplied the shared concept sheet; OpenAI authored complete pixel figures. PixelLab was not used.

23 source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. 624 packed keys select 624 distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

Each creature has eight facings and 8 actions. Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. Dragon Jaws contacts at .5 s through a full 1 s clip; Breath and Presence contact at .5 s through 1.2 s, and Wingbeat at .7 s through 1.4 s. Shared rules, effects and explicit projectile overrides are preserved.

Three mature six-limbed dragons match the approved Red Dragon scale: four grounded legs, two dorsal bat wings and one rooted tail.

Obsidian/wine, cobalt/violet and moss/fern palettes are complete authored figures, with folded wings during walking and visibly unfurled wings during jaw contact.

All three existing ability gestures are authored in eight facings: Fire Breath brace, Frightful Presence mantle and Cataclysmic Wingbeat sweep. Existing shared gameplay and FX remain unchanged.

Texture padding remains 512 pixels: four rows per facing fit the runtime 16384-pixel texture limit.

Live battle/world review checks the same mature family scale beside the party, plus head portraits, wing clearance and whole-silhouette occlusion.

Three complete rear-left hurt/down pairs replace forward-facing damage poses. Black and Blue east Wingbeat rows use fresh whole figures with empty gutters; no selected crop contains a neighboring figure.

Diagnostic beat screenshots allow twelve examples per moment so the nine genuine ability contacts can all be captured; gameplay and world screenshot limits are unchanged.

Lossless WebP compass strips preserve every runtime RGBA pixel while reducing the embedded atlas payload by 41%. Phones decode one facing at a time and release previous views.

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
python tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/references.py
python tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/measure.py
node tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/export.cjs
python tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/integrate.py
node tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/compare.cjs
node tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/review.cjs
node tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/reel.cjs
node tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/verify-review.cjs
python tools/tactics-assets/imagegen/wyrm-triad-2026-11-08/verify-scope.py
```

The scope verifier compares against `82f13e16` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
