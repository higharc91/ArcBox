# Storm, Magma and Swamp pixel production

[Concepts/receipts](../../concepts/wild-elements-2026-11-07/BRIEF.md). Storm concept used Scenario; a new-job HTTP 429 cooldown required OpenAI for Magma/Swamp concepts. OpenAI authored the complete material-variant pixel figures. PixelLab was not used.

references.cjs technically copies whole approved primary runtime figures into 15 transparent reference sheets. This preserves calibrated anatomy, eight facings, corrected Water rear gestures and correct hurt/down ordering. No painting, mirrored parts or procedural recolouring. Image generation rematerializes each complete figure; exact prompts, inputs, generation IDs and PNG hashes are preserved. references/ contains the actual input sheets, not new production art.

Sixteen source sheets supply 528 packed keys selecting 384 distinct authored figures: per variant 32 idle,32 movement,16 physical windup/contact,32 paired-special windup/contact and 16 hurt/down. Ready/recovery reuse idle. One additional whole rear Storm hurt figure replaces a draft pose that turned its head sideways; the rejected sourcePose 8 is never selected. Both draft and replacement remain preserved. No head/limb patches. Hurt/down are single held poses, not animated falls; all damage sheets use alternating hurt/down pairs in reading order.

Runtime pixelScale 0.32 and neutral heights 144/176/152 px match Gale/Stone/Sea (about 46/56/49 world pixels). Storm cells 256 px at[128,224]; Magma/Swamp 320 px at[160,256]. Whole-figure uniform sheet scaling and nearest-neighbour packing retain complete limbs/bases. Extra cell padding does not enlarge bodies. Head registration stabilizes movement; HUD crops use the front head and action clearance keeps bars above raised fists/coils.

Idle 4/3/4 fps; movement 6/4/5 fps. Natural strikes and most gestures contact 0.4 s and finish 0.8 s. Magma Tremor contacts 0.5 s/finishes 1 s. Four keys cover ready, anticipation, contact and recovery. Aegis aliases the crossed-arm cast clip. All actions retain eight facings and connected anatomy. The three exact library keys now share their refreshed primary family's visual timing/gesture selection; other variants stay unchanged. Undertow remains directed at the selected foe. Six beat-clock special-contact moments avoid wall-clock capture drift.

No rule/ability/FX JSON, damage, HP, resistances, ranges, sound, encounter placement, prior runtime images or dialogue portraits change. These are library appearances; the five-unit development roster is an art fixture, not a new encounter. The original 29-design combat tracker remains complete and unchanged; additional-library progress has its own tracker.

From repository root:

```sh
node tools/tactics-assets/imagegen/wild-elements-2026-11-07/references.cjs
python tools/tactics-assets/imagegen/wild-elements-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/wild-elements-2026-11-07
python tools/tactics-assets/imagegen/wild-elements-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/wild-elements-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/wild-elements-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/wild-elements-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/wild-elements-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/wild-elements-2026-11-07/verify-scope.py
```

Set NODE_PATH/PLAYWRIGHT_BROWSERS_PATH for installed tools as needed. Scope compares againstd 4fbd 7e5. Reproduce Godot captures using the review README.
