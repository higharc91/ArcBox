# Hobgoblin and roadside bandit: integrated

Baseline #22 (`characters.hobgoblin`) and #13 (`characters.bandit`) now use dedicated eight-direction pixel art matching the refreshed roster. The hobgoblin wears heavy iron lamellar, carries a longsword and retains the sun-marked sash described in Chapter 3. The bandit is an ordinary hooded Vale human with the handaxe specified by the rules. Existing roadside-tough and roaming aliases resolve through the same bandit key. Captain/Rook art stays unchanged.

- [Standalone interactive review](index.html): all eight facings, five action controls and embedded game captures.
- [Before / after](before-after.png): the original and refreshed sprites at the same world zoom.
- [Combined motion reel: MP4](road-and-host-motion.mp4) / [GIF](road-and-host-motion.gif): 12 seconds of walking and attacks in four representative directions, with the runtime manifest's actual cadence and frame sequence.
- All-facing [idle](idle-all-facings.png), [attack wind-up](attack-all-facings.png) and [attack impact](attack-impact-all-facings.png) sheets.
- Real Godot renderers: [battle role lineup](battle.png), [Warlord's Bridge encounter](vorga-encounter.png), [Roadside Toughs encounter](bandit-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting/occlusion](overworld-lighting-occlusion.png). The two lineups are development fixtures, not added town enemy spawns.

The two sets contain 224 packed pose slots from 220 distinct authored source poses. Each facing includes four idle keys, four walk keys, four attack playback slots, one hurt pose and one down pose. The bandit's rear diagonals use two corrected attack keys between ready/recovery idle poses; mirrored or wrong-hand proposals were rejected. Hurt/down are single poses, not animated fall transitions. No runtime mirroring or independent weapon movement is used. Weapons are part of each complete figure.

Twelve new 40×40 dialogue portraits preserve six distinct expressions for each preset. Battle HUD portraits crop the new atlas art. Visible upright bodies target 120 pixels for the bandit and 132 for the hobgoblin, both at 0.32 runtime scale; atlas padding accommodates weapon reach rather than enlarging the characters in the world. Existing goblins, orc, drake and named characters retain their approved art. No combat rules, abilities, spawn definitions, lore text, audio or player outfits changed.

## Verification

- Final focused run: **307 passed, zero failures** ([log](checks/focused.txt)). Covers all eight refreshed looks, atlas addressing/clipping, whole-figure connectivity, idle/walk motion, scale limits, HUD crops, expression roles, named overrides and walkable fixture placement.
- Full presentation suite: **2,924 passed / 3 failed**, no skips ([log](checks/presentation.txt)). The same three known story-count assertions remain: 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections. They were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md).
- [Host build](checks/host-build.txt) and [test build](checks/test-build.txt): zero warnings/errors. Godot [imported](checks/import.txt) the final atlases and loaded them in the [battle](checks/battle.txt), [overworld](checks/overworld.txt), [bridge encounter](checks/vorga.txt) and [bandit encounter](checks/bandits.txt). Scene lighting and foreground occlusion apply normally. Existing fixture shutdown ObjectDB warnings remain; scene runs exit successfully.
- [Browser check](browser-validation.json): six embedded scenes decode, all five actions produce different output, four walk phases produce distinct frames, no page errors and no horizontal overflow at 390px.
- [Scope verification](checks/scope.json): only two added creature entries and two changed portrait presets; every previous creature entry remains unchanged. [Runtime asset hashes](checks/runtime-assets.json).

This is a visual and functional check using software rendering in the cloud, not a hardware performance benchmark. Sources, exact prompts, rejected drafts, image-generation cleanup and reproducible packing scripts are in `tools/tactics-assets/imagegen/road-and-host-2026-11-07/`. Scenario concepts and job/asset receipts are in `tools/tactics-assets/concepts/road-and-host-2026-11-07/`.

Host review arguments after `--`:

```sh
--tactics-demo=river_crossing:road-and-host --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:warlord_bridge --shots=/absolute/bridge-shots --quit-after=12
--tactics-demo=encounter:mob_road_bandits --shots=/absolute/bandit-shots --quit-after=12
--world-demo=ashen_ford:road-and-host '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Eight refreshed looks are now integrated across three batches; [21 older combat looks remain](../production/refresh-status.json). The historical 98-look baseline remains unchanged. No PR has been opened.
