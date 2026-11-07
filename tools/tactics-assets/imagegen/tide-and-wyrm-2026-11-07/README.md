# Water Elemental and Red Dragon production sources

Scenario concepts and receipts: `../../concepts/tide-and-wyrm-2026-11-07/`. Thirteen original generated transparent pixel sheets, exact prompts and generation IDs are preserved here. PixelLab was not used. Scripts select complete figures, crop and uniformly scale with nearest-neighbour sampling. No mirrored anatomy, independently shifted parts or pixel repainting.

384 packed frame keys select 272 distinct authored figures. Water: 32 idle,32 glide,16 physical windup/contact,32 paired-special windup/contact,16 hurt/down. Dragon:32 idle,32 four-foot walk,16 bite windup/contact,16 each breath/roar/wingbeat,16 hurt/down. Ready/recovery reuse matching idle figures. Hurt/down are single held poses. The authored damage sheets group all eight hurt poses before all eight collapsed poses; measure.py explicitly interleaves them into the runtime order.

Four corrected whole back-left Water special figures replace the draft row, which showed too much of the front. `corrections.json` selects those full figures into both gesture clips. Rejected draft sourcePose12–15 never reach the atlas. Both originals and correction are preserved; 272 counts selected distinct figures, excluding the four rejected figures. No patching individual arms or faces.

Neutral authoring heights: Water 152 px, Dragon 200 px, both at pixelScale 0.32 (about 49/64 world pixels beside a 1.75 m fighter). The crouched six-limbed dragon is about 3.2–3.3m at the horns, with a long compact tail. Water uses320 px cells anchored[160,256], Dragon512 px cells anchored[260,400]. The slight horizontal packing offset leaves space for all eight wing spreads without changing creature size. Dragon has four rows per facing for its third special; Water has three. Source-edge, neighbour and projected atlas bounds checks pass on all 384 selected frame keys. Atlas padding does not enlarge either body.

Water idle 4 fps/glide 5 fps; Dragon idle 3 fps/four-foot walk 4 fps. Natural Water attacks contact at 0.4 s and finish 0.8 s. Dragon's jaws contact at 0.5 s and finish 1 s, including a visible wing unfurl. Tidal Crush and Undertow contact 0.4 s/finish 0.8 s. Dragon Breath and Frightful Presence contact 0.5 s/finish 1.2 s. Cataclysmic Wingbeat contacts 0.7 s/finishes 1.4 s. All keep anticipation/contact/recovery distinct. Action-specific clearance lifts bars above raised poses; front-head crops supply HUD portraits.

BeatScript selects these gestures only for these exact two sprite keys with their existing ability presentation bindings. Undertow aims at the selected foe and puts its existing ice effect on that foe at the pull. Self specials face the nearest foe; aimed dragon breath shares that heading and attaches 42 world pixels above the floor at the mouth. Existing FX/audio are reused. Common Dragon Jaws at one/two-tile reach stays physical; explicit projectile/FX overrides remain respected. Existing colour variants retain their old bindings/timing. No damage, range, HP, resistance, rules, encounter placement, sound/FX artwork or dialogue portraits change.

Five `special-contact` moments are emitted on the beat clock so review captures select the contact key even when first-use FX loading delays wall-clock screenshots. These do not change gameplay or player UI. Development fixture places its fighter in a separate ready snapshot before each attack, preserving previous/current positions.

Reproduce from repository root:

```sh
python tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07
python tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/verify-scope.py
```

Set NODE_PATH/PLAYWRIGHT_BROWSERS_PATH for installed runtimes as needed. Screenshot collection arguments are in the review README. Scope compares against d07880ed and preserves prior art/rules/portraits and the original 98-look baseline.
