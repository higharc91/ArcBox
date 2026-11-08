# Basalt, Crystal and Temple Golem pixel production

[Concepts and receipts](../../concepts/stone-sentinels-2026-11-07/BRIEF.md). Scenario supplied Basalt's concept; OpenAI supplied Crystal and Temple concepts after Scenario rate-limited new jobs. OpenAI authored all complete pixel figures. No PixelLab, procedural recolouring, mirrored parts, or anatomy patches.

`references.cjs` technically copies whole approved Granite Golem runtime poses into 16 transparent input sheets. References preserve the primary's proportions, action geometry and eight facing order. Exact prompts, reference paths, source generation IDs and hashes are retained. These references are not new production sprites.

Sixteen preserved source sheets supply 528 packed keys selecting 384 distinct whole figures: each variant has 32 idle, 32 walking, 16 punch windup/contact, 32 paired special windup/contact and 16 hurt/down poses. Ready and recovery reuse idle. A two-figure correction replaces the draft Crystal rear-left punch; rejected source poses 6/7 remain preserved but are not selected. No limb/head patches. Hurt and down are held poses rather than animated falls. Source silhouettes remain complete; packing uses uniform sheet scaling and nearest-neighbour sampling only.

All three use neutral height 172 authoring pixels and runtime pixelScale 0.32, matching the approved Granite Golem (about 55 world pixels). Cells are 320 px at anchor [160,256]; canvas padding does not enlarge the body. Movement head registration prevents swaying head/core placement. The round chest core remains on the front; backs have carved stone spines. HUD crops use the front head and active-pose clearance lifts health bars above raised fists.

Idle is 3 fps; heavy walking 4 fps. Natural punches contact at 0.4 s and finish at 0.8 s. Seismic Slam contacts at 0.5 s and finishes at 1 s; Stone Aegis contacts at 0.4 s and finishes at 0.8 s. Four pose keys cover ready, windup, contact and recovery. Buff aliases the crossed-arm cast clip. All seven actions retain eight facings. Six beat-clock special-contact moments capture actual engine contact rather than wall-clock estimates.

Only these three exact library keys receive the new visuals and timing. No rule/ability/FX JSON, stats, damage, resistance, sound, encounter placement, prior runtime art or dialogue portraits change. The five-unit development lineups and replay are art fixtures. The original 29-design combat tracker stays unchanged; extra library variants have a separate tracker.

From repository root (Node/Playwright and Python imaging dependencies installed):

```sh
node tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/references.cjs
python tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/stone-sentinels-2026-11-07
python tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/verify-scope.py
```

Set NODE_PATH/PLAYWRIGHT_BROWSERS_PATH for the installed browser. Scope compares against `459b0fd5`. [Review and native Godot capture reproduction](../../../../docs/art/mob-sprite-review/stone-sentinels/README.md).
