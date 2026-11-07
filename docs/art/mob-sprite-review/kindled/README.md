# Kindled cultist and Maren / Ember Hierophant: integrated

Baseline #16 (`characters.cultist`) and #15 (`characters.cult_leader`) now use dedicated eight-direction pixel art matching the refreshed roster. The cultist reads as a desperate Vale traveller with a ritual dagger. Maren reads as the middle-aged human preacher described by the lore: grey temples, a close low bun, practical leather armor, maroon mantle and a caged-ember ashwood staff. Their shared faction palette uses rust, umber and charcoal.

- [Standalone interactive review](index.html): all eight facings, six action controls and embedded before/after and game captures.
- [Before / after](before-after.png), at the same world zoom.
- [Combined animation MP4](kindled-motion.mp4) / [GIF](kindled-motion.gif): 18 seconds of walking, physical attacks and spellcasting in four representative directions, using the actual runtime frame sequences and cadence.
- All-facing [idle](idle-all-facings.png), [melee wind-up](attack-all-facings.png), [melee impact](attack-impact-all-facings.png) and [casting](cast-all-facings.png).
- Actual Godot renderers: [battle lineup](battle.png), [Hall of the Flame](hall-encounter.png), [Forest Ruins](forest-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting and occlusion](overworld-lighting-occlusion.png). The lineups are development fixtures, not additional town enemy spawns.

272 selected complete poses: four idle keys, four walk keys, four melee source poses, one hurt pose, one down pose and three cast keys for each role in each of eight directions. Hurt/down are single poses, not animated falls. The cultist keeps the dagger in his right hand and casts with the left. Maren keeps her staff in the left hand and casts with the right; her difficult rear rows and right-profile gesture have dedicated corrections. Both weapons remain baked into the figures, without runtime mirroring or independent equipment movement. Maren's fallen staff rests on the ground in her hand.

The existing shared cult-leader appearance still serves generic Ember Hierophants and Maren's named stat block, including the additional hierophants in the Hall encounter. No new named override or combat/spawn rule was introduced. Twelve new 40×40 dialogue portraits preserve six expression roles for each preset; battle HUD heads crop the actual new atlas art. Physical dagger/staff attacks and point/summon spell gestures use separate frame sequences. Existing projectile and spell FX remain owned by the game renderer.

Visible upright bodies target 120 source pixels for the cultist and 116 for Maren, both at 0.32 runtime scale. Frame padding accommodates weapons and gestures rather than changing world size. All previously approved creature entries and unrelated portraits are unchanged. No combat balance, abilities, encounter definitions, lore text, audio or player outfits changed.

## Verification

- Final focused run: **316 passed, zero failures** ([log](checks/focused.txt)). Covers all ten refreshed looks, atlas bounds and addressing, connected figures/weapons, directional idle/walk motion, party scale, HUD crops, expression roles, spell bindings and walkable fixture placement. A new bright-ember landmark check verifies Maren's staff side across front/rear idle, walking and casting.
- Full presentation suite: **2,933 passed / 3 failed**, no skips ([log](checks/presentation.txt)). The three known story-count assertions remain: 590 vs 614 speakers, 921 vs 945 lines and 8 vs 32 camp reflections. They were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md).
- [Host](checks/host-build.txt) and [test](checks/test-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [Hall](checks/hall.txt) and [Forest](checks/forest.txt) paths load the art. Scene lighting/occlusion apply normally. The final profile-gesture correction leaves all 64 idle frame PNGs used by these captures byte-identical. Existing fixture shutdown ObjectDB warnings remain; captures exit successfully.
- [Browser check](browser-validation.json): all six embedded scenes decode, all six actions render different output, four walk phases produce distinct frames, no page errors and no horizontal overflow at 390px.
- [Scope verification](checks/scope.json) and [runtime SHA-256 receipts](checks/runtime-assets.json): exactly two new creature entries and two changed portrait presets; prior entries are protected. Source prompts, generation IDs, corrections, rejected proposals, hashes and reproducible technical export scripts are in `tools/tactics-assets/imagegen/kindled-2026-11-07/`. Scenario receipts are in `tools/tactics-assets/concepts/kindled-2026-11-07/`.

This is visual and functional review in a software-rendered cloud environment, not a hardware performance benchmark. The full .NET solution was not rerun; this batch changes presentation art, development fixtures and their tests.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:kindled --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:hall_of_the_flame --shots=/absolute/hall-shots --quit-after=12
--tactics-demo=encounter:forest_ruins --shots=/absolute/forest-shots --quit-after=12
--world-demo=ashen_ford:kindled '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Ten looks are refreshed across four batches; [19 older combat looks remain](../production/refresh-status.json). The historical 98-look baseline stays unchanged. No PR has been opened.

Subsequent batch: [Bone Warden warrior and archer](../bone-wardens/README.md). Twelve looks are now refreshed; 17 older combat looks remain.
