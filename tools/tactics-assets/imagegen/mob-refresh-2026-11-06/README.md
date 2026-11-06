# Mob overhaul: first pixel-art candidates

Owner request: overhaul older mobs such as orcs, goblins and drakes through Scenario concepts, then locally made pixel art/animation, matching the newer approved sprites. Branch `mob-sprite-refresh`.

Concepts and Scenario job/asset receipts: `../../concepts/mob-refresh-2026-11-06/` (model Nano Banana 2.1; approved Grisk sheet is the shared style reference). Pixel artwork is generated with OpenAI image generation, using those concepts plus the approved worker-kobold south sprite as the pixel-craft reference. PixelLab is not used.

Candidate files:
- `orc-idle-turnaround-v2.png`: corrected eight-angle design pilot. The earlier `orc-idle-turnaround-pilot.png` is retained as a rejected direction-layout attempt.
- `goblin-idle-turnaround.png` and `drake-idle-turnaround.png`: eight-angle design pilots.
- `goblin-walk-front-clean.png`: eight front-walk frames after generating a clean transparency matte.
- `pilots/`: technical native-size PNG exports, packing metadata/checks, individual frames, and the front-walk GIF review.

`measure-pilots.py` inspects source alpha only. All four source sheets contain exactly eight connected opaque actors, including their weapons. Extracting whole components avoids cutting a long tail at the AI-generated grid boundaries or accidentally importing a fragment of the neighboring sprite. `pack-pilots.cjs` uses Canvas nearest-neighbor copying to produce 128/192-pixel review frames at a proposed .32 pixelScale. Walk frames align to the face instead of a weapon-dependent silhouette centre, with one-pixel vertical bob; other idle candidates use foot anchors. Every exported silhouette fits inside its frame. This technical packing does not repaint anatomy or add animation through warping.

These are **review candidates**, not runtime atlases. No production sprite registrations have changed. Only the goblin's south/front walk has been piloted. Full directional idle/walk/attack/special/hurt/down, grip/gear continuity, palette/edge polish and world/battle placement are still outstanding. Owner scope and style questions are pending before a large batch is generated.

The comparison and standalone animation review are in `docs/art/mob-sprite-review/pilots/`. They compare current/proposed art at a common world zoom. GIFs use an opaque review background to avoid palette-transparency artifacts; candidate PNGs keep alpha.
