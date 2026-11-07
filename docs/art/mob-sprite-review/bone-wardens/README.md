# Bone Warden warrior and archer: integrated

Baseline #26 (`characters.skeleton`) and #27 (`characters.skeleton_archer`) now use dedicated eight-direction art matching the refreshed roster. Both follow the Bone Wardens bestiary: the oldest crypt guards, empty rather than cruel, wearing weathered uniform remnants. Ivory bones, empty sockets, charcoal leather and slate-blue tabards connect the two roles. The warrior retains a shortsword; the archer retains a shortbow and back quiver.

- [Standalone interactive gallery](index.html): all eight facings, five actions and embedded comparisons/game scenes.
- [Before / after](before-after.png), at the same world zoom.
- [Combined animation MP4](bone-wardens-motion.mp4) / [GIF](bone-wardens-motion.gif): 12 seconds of walking and attacks in four representative directions, using runtime frame sequences and cadence.
- All-facing [idle](idle-all-facings.png), [attack wind-up](attack-all-facings.png) and [attack impact/release](attack-impact-all-facings.png).
- Actual Godot renderers: [battle lineup](battle.png), [Crypt Hall](crypt-encounter.png), [The Broken Gate](gate-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting and occlusion](overworld-lighting-occlusion.png). Lineups are development fixtures, not new town enemy spawns.

224 selected complete poses: four idle, four walk and four attack keys, one hurt pose and one down pose per role/facing. Hurt/down are single poses, not animated falls. Quiet watching and weight/cloth settling avoid biological breathing. Weapons stay baked into complete figures. The warrior holds his sword in the right hand, bracing that wrist during the corrected back-left thrust. The archer grips the bow with the left hand through release; five attack rows received dedicated corrections. The game still owns flying arrows. No runtime mirroring or independent weapon movement is introduced.

Both visible upright bodies target 120 source pixels at 0.32 runtime scale. Extra frame padding accommodates weapons rather than changing world size. Twelve new 40px dialogue portraits retain six expression roles; HUD portraits crop the actual new atlas heads. Existing sprite keys update all encounters already using these appearances. No balance, abilities, spawn definitions, lore, audio or player outfits changed.

## Verification

- Final focused run: **324 passed, zero failures** ([log](checks/focused.txt)). Covers all twelve refreshed looks, atlas bounds/addressing, connected figures/weapons, directional idle/walk motion, party scale, HUD crops, portrait roles, archer shot bindings and fixture placement.
- Full presentation suite: **2,941 passed / 3 failed**, no skips ([complete log](checks/presentation.txt)). The three existing story-count failures were reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections.
- [Host](checks/host-build.txt) and [test](checks/test-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [crypt](checks/crypt.txt) and [gate](checks/gate.txt) captures exit successfully. Lighting and occlusion apply normally. Existing fixture shutdown ObjectDB warnings remain.
- [Browser validation](browser-validation.json): six embedded scenes decode, five actions render distinct output, four walk phases differ, no page errors and no horizontal overflow at 390px.
- [Scope protection](checks/scope.json) and [runtime hashes](checks/runtime-assets.json): exactly two new creature entries and two changed portrait presets; previous entries and unrelated portraits remain unchanged. Thirteen source images, prompts, generation IDs, hashes, rejected cells and reproducible export scripts are in `tools/tactics-assets/imagegen/bone-wardens-2026-11-07/`; Scenario receipts are in `tools/tactics-assets/concepts/bone-wardens-2026-11-07/`.

Review used a software-rendered cloud environment, not a hardware performance benchmark. The full .NET solution was not rerun; changes concern presentation art, development fixtures and their tests.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:bone-wardens --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:crypt_hall --shots=/absolute/crypt-shots --quit-after=12
--tactics-demo=encounter:keep_gate --shots=/absolute/gate-shots --quit-after=12
--world-demo=ashen_ford:bone-wardens '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Twelve looks are refreshed across five batches; [17 older combat looks remain](../production/refresh-status.json). The historical 98-look baseline stays unchanged. No PR has been opened.
