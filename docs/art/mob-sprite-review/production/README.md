# Approved first batch: integrated

Goblin cutthroat, orc brute and red drake are registered in the game on `mob-sprite-refresh`. Existing combat keys are retained. The red drake keeps its original `beasts.drake` alias and remains grounded by the rules. Other mob roles, named designs and drake color variants retain their own art; [refresh-status.json](refresh-status.json) tracks the remaining 13 older combat looks after the seventh batch.

- [Standalone review](index.html): all eight directions and all five actions, with embedded battle/overworld evidence. Animation playback uses the shipped manifest's frame sequence and cadence.
- [Combined animation GIF](first-batch-motion.gif) / [MP4](first-batch-motion.mp4): walking and attacks in four representative directions, at the shipped action cadence with brief pauses between attacks.
- [All-facing idle](idle-all-facings.png) and [attack](attack-all-facings.png) contact sheets.
- [Battle](battle.png), [clear overworld lineup](overworld-lineup.png), and [town lighting/occlusion](overworld-lighting-occlusion.png). These use the actual Godot TV renderers, with party/newer-kobold references. They are development stand-ins for visual review, not new town enemy spawns.

The three sets contain 336 selected authored poses: four quiet-idle frames, four walk keys, four attack source poses, one hurt pose and one collapsed pose for each of eight facings. Attack playback includes recovery and deliberate anticipation/peak holds. The drake visibly unfolds both wings during attack/special and folds them during idle/walk. Hurt/down are single poses, not animated transitions. Goblin/orc dialogue portraits include all six expression roles; battle HUD heads crop the new atlas art. Sources, generation receipts, corrections and reproducible technical export scripts are in `tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/`.

## Verification

- Final affected-class run: **116 passed, zero failures** ([log](checks/final-focused.txt)). Covers atlas addressing/clipping, real facing selection, ground contact, world scale, visible drake wing spread, source silhouette connectivity, non-static motion, new HUD crops/expression roles and the corrected battle review placement.
- Full solution run: **7,207 passed / 5 failed**, no skips ([log](checks/full-solution.txt)). Two branch-related failures were the old goblin portrait expectation and the initial orc review position on a blocked tile. Both are corrected and included in the green final 116-test run. The other three are existing story-count failures (590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections), previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). The full solution was not repeated after those focused corrections.
- [Host build](checks/host-build.txt): zero warnings/errors. Godot imported and loaded the new assets through the actual [battle](checks/battle.txt) and [overworld](checks/overworld.txt) paths. In the town, both foreground occlusion and warm/cool scene lighting apply to the new art.
- [Browser check](browser-validation.json): all embedded scenes decode, every action renders, no page errors, and no horizontal overflow at 390px.
- [Padding check](checks/drake-padding.json): all 112 drake poses remain pixel-identical after trimming the initial 256px authoring frame to 192px; decoded atlas allocation falls from 32 to 18 MiB. [Runtime asset hashes](checks/runtime-assets.json).

These are visual and functional checks in a software-rendered cloud environment, not a hardware performance benchmark. No combat balance, abilities, encounter spawns, music or player outfit art changed.

Reproduce the scene checks with the host:

```sh
# Godot arguments after --
--tactics-demo=river_crossing:mob-refresh --shots=/absolute/battle-shots --quit-after=12
--world-demo=ashen_ford:mob-refresh '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

No PR has been opened for this batch.

Latest: [goblin archer, shaman and Vorga refresh](../goblin-roles/README.md), including separate cast/rally animations.

Latest third batch: [hobgoblin and roadside bandit](../road-and-host/README.md).

Latest fourth batch: [Kindled cultist and Maren / Ember Hierophant](../kindled/README.md).

Latest fifth batch: [Bone Warden warrior and archer](../bone-wardens/README.md).

Latest sixth batch: [Risen of Frostpeak and wraith](../hollow-dead/README.md).

Latest seventh batch: [Ivo Rime / necromancer and Oswin Hale / lich](../rime-keepers/README.md).

Latest eighth batch: [Ser Halvard / Black Knight and Drow Blade / Ysolde](../oath-and-shadow/README.md). Eighteen looks refreshed; 11 older combat looks remain.

Latest ninth batch: [Troll and granite Stone Golem](../stone-and-mire/README.md). Twenty looks refreshed; nine older combat looks remain.

Latest tenth batch: [Oak Mimic](../oak-ambush/README.md). Twenty-one looks refreshed; eight older combat looks remain.

Latest eleventh batch: [Hunting Hound](../trail-companion/README.md). Twenty-two looks refreshed; seven older combat looks remain.

Latest twelfth batch: [Bound Imp](../ember-familiar/README.md). Twenty-three looks refreshed; six older combat looks remain.

Latest thirteenth batch: [Grove Sapling](../grove-ward/README.md). Twenty-four looks refreshed; five older combat looks remain.

Latest fourteenth batch: [Air, Earth and Fire Elementals](../elemental-triad/README.md). Twenty-seven looks refreshed; Water Elemental and Red Dragon remain.

Latest fifteenth batch: [Water Elemental and Red Dragon](../tide-and-wyrm/README.md). Combat-roster pass complete: twenty-nine refreshed looks, no older combat looks remaining. Additional library colour variants remain separate.

Sixteenth overall / first library batch: [Storm, Magma and Swamp Elementals](../wild-elements/README.md). Combat tracker stays complete at29; library tracker records three refreshed appearances,28 older two-direction appearances remaining,26 existing eight-direction appearances preserved.

Seventeenth overall / second library batch: [Basalt, Crystal and Temple Golems](../stone-sentinels/README.md). Combat tracker stays complete at 29; six additional library appearances refreshed, 25 older two-direction looks remaining, 26 existing eight-direction looks preserved.

Eighteenth overall / third library batch: [Azure, Radiant and Shadow Fire Elementals](../prismatic-flames/README.md). Combat tracker stays complete at 29; nine additional library appearances refreshed, 22 older two-direction looks remaining, 26 existing eight-direction looks preserved.

Nineteenth overall / fourth library batch: [Black Hound, Grey Hound and Violet Imp](../night-companions/README.md). Combat tracker stays complete at 29; 12 additional library appearances refreshed, 19 older two-direction looks remaining, 26 existing eight-direction looks preserved.

[Batch 20: Vampire Bat, Undead Bear and Crystal Slime](../crypt-kin/index.html) adds library batch 5. 44 designs refreshed; 16 older library appearances remain. Main playtest checkpoint is PR #273; follow-up work stays on `mob-library-refresh-272` for issue #272.

[Batch 21: Phase Spider, Alpha Wolf and Deep Crab](../web-and-hunt/index.html) adds library batch 6; 13 older appearances remain.

[Batch 22: Black Scorpion, Warg Rider and Ironbound Mimic](../iron-and-fangs/index.html) adds library batch 7; 10 older appearances remain.

[Batch 23: Black, Blue and Green Dragon](../wyrm-triad/index.html) adds library batch 8; 7 older appearances remain.

[Batch 24: Gold Dragon, Frost Troll and Ogre](../gilded-and-rime/index.html) adds library batch 9; 4 older appearances remain.

[Batch 25: Autumn Sapling, Blossom Sapling and Shade](../autumn-and-veil/index.html) adds library batch 10; 1 older appearance remains.

[Batch 26: Spectre](../spectral-remnant/index.html) adds library batch 11; 0 older appearances remain.

[Complete issue #272 follow-up review](../library-completion/README.md): all nineteen additions together, shared party scale, eight-direction walking, and final verification against main `0d808be2`. The 31 older library appearances are complete; TV/phone playtest feedback remains on the issue.
