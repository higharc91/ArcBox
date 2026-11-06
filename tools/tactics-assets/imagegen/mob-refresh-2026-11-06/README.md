# Mob overhaul: first pixel-art candidates

Owner request: overhaul older mobs such as orcs, goblins and drakes through Scenario concepts, then locally made pixel art/animation, matching the newer approved sprites. Branch `mob-sprite-refresh`.

Concepts and Scenario job/asset receipts: `../../concepts/mob-refresh-2026-11-06/` (model Nano Banana 2.1; approved Grisk sheet is the shared style reference). Pixel artwork is generated with OpenAI image generation, using those concepts plus the approved worker-kobold south sprite as the pixel-craft reference. PixelLab is not used.

Candidate files:
- `orc-idle-turnaround-v2.png`: corrected eight-angle design pilot. The earlier `orc-idle-turnaround-pilot.png` is retained as a rejected direction-layout attempt.
- `goblin-idle-turnaround.png` and `drake-idle-turnaround.png`: eight-angle design pilots.
- `goblin-walk-front-clean.png`: eight front-walk frames after generating a clean transparency matte.
- `pilots/`: technical native-size PNG exports, packing metadata/checks, individual frames, and the front-walk GIF review.

`measure-pilots.py` inspects source alpha only. All four source sheets contain exactly eight connected opaque actors, including their weapons. Extracting whole components avoids cutting a long tail at the AI-generated grid boundaries or accidentally importing a fragment of the neighboring sprite. `pack-pilots.cjs` uses Canvas nearest-neighbor copying to produce 128/192-pixel review frames at a proposed .32 pixelScale. Walk frames align to the face instead of a weapon-dependent silhouette centre, with one-pixel vertical bob; other idle candidates use foot anchors. Every exported silhouette fits inside its frame. This technical packing does not repaint anatomy or add animation through warping.

These files preserve the **historical review candidates**. The owner approved the goblin/orc style and winged drake revision, then authorized continuation. The selected eight-direction idle, walk, attack, hurt and down poses are now integrated through `production/`, with matching portraits and real world/battle review. Source pilots remain here for provenance. Other mob roles and drake colors remain separate work.

The comparison and standalone animation review are in `docs/art/mob-sprite-review/pilots/`. They compare current/proposed art at a common world zoom. GIFs use an opaque review background to avoid palette-transparency artifacts; candidate PNGs keep alpha.

## Wing correction

`drake-winged-attack.png` supersedes the wingless drake design. Generated from the revised Scenario concept and the earlier pixel palette using OpenAI image generation (`exec-4a0e8444-b3a6-4ab4-8311-75b2bffa74f8.png`). Eight chronological frames show a grounded bite with both wings opening and recovering. `pack-drake-wings.py` measures whole connected poses; `pack-drake-wings.cjs` copies with nearest-neighbor scaling. A single scale derived from folded idle is used for every frame, anchored to the feet. All eight silhouettes fit with margin in 256px frames; alpha is preserved. The preview GIF uses 8fps and an opaque background. Body scale remains proposed .32; production placement is outstanding. This does not change flying rules or combat stats. The updated comparison uses its folded ready frame.
