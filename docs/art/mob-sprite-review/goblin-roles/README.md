# Goblin role refresh: integrated

Goblin archer, generic shaman / Skarrow Stormcaller and Vorga / goblin boss now use dedicated eight-direction art, matching the approved cutthroat family. Mother Skarrow retains her named design. The legacy `characters.goblin_spear` key is retained for compatibility; Vorga correctly carries the scimitar used by his rules.

- [Standalone interactive review](index.html): embedded art, before/after comparison, six action controls and game captures.
- [Before / after](before-after.png), at the same world zoom.
- [Combined animation GIF](goblin-roles-motion.gif) / [MP4](goblin-roles-motion.mp4): walking, attacks and cast/rally in four representative directions, using the shipped frame sequences and cadence.
- [Idle](idle-all-facings.png), [attack](attack-all-facings.png) and [cast/rally](cast-all-facings.png) across all eight directions. Archer uses idle during cast review because it has no casting role.
- Actual Godot captures: [battle role lineup](battle.png), [Vorga's authored encounter](vorga-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting and occlusion](overworld-lighting-occlusion.png). The two lineups are development fixtures; they do not add enemy spawns to the town.

384 selected complete poses: 112 archer, 136 shaman and 136 boss. Every facing includes four idle keys, four walk keys, four attack source poses, one hurt pose and one down pose. Shaman and boss each add three selected cast/rally keys per facing, bound to the existing point/summon and related abilities. Hurt/down are single poses, not animated collapse transitions. Weapons are baked into the figures; archer release leaves the flying arrow to the game's projectile rendering. All views are authored, without runtime mirroring.

Final corrections keep the bow in the archer's left hand, the quarterstaff in the shaman's right hand and Vorga's scimitar in his right hand across poses. Vorga wears brown leather at both shoulders and small iron chest plates, avoiding the earlier inconsistent single pauldron. Eighteen new dialogue portraits preserve six expression roles per preset; battle HUD heads crop the actual new atlas art. No combat balance, abilities, lore, encounter spawns, audio or player outfit art changed.

## Verification

- Final affected-class run: **300 passed, zero failures** ([log](checks/focused.txt)). Covers atlas geometry and connectivity, all facings, idle/walk motion, ability bindings, HUD crops, expression roles, named overrides and walkable demo placement.
- Full presentation suite: **2,917 passed / 3 failed**, no skips ([log](checks/presentation.txt)). All three failures are existing story-count assertions: 590 vs 614 speakers, 921 vs 945 lines and 8 vs 32 camp reflections. These were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). No new presentation failures remain.
- [Host build](checks/host-build.txt): zero warnings/errors. Godot imported and loaded the art in the actual [battle](checks/battle.txt), [overworld](checks/overworld.txt) and [Vorga encounter](checks/vorga.txt) renderers. Scene lighting and foreground occlusion apply to the new art. Shutdown retains the fixture's existing ObjectDB leak warning; captures exit successfully.
- [Browser validation](browser-validation.json): embedded images decode, all six controls render distinct output, no page errors and no horizontal overflow at 390px.
- [Runtime SHA-256 receipts](checks/runtime-assets.json). Previous creature entries and all unrelated portrait presets remain unchanged.

This is a functional and visual review in a software-rendered cloud environment, not a hardware performance benchmark. Sources, prompts, rejected drafts, corrections and technical export scripts are retained in `tools/tactics-assets/imagegen/goblin-roles-2026-11-07/`; Scenario concept receipts are in `tools/tactics-assets/concepts/goblin-roles-2026-11-07/`.

Host review arguments after `--`:

```sh
--tactics-demo=river_crossing:goblin-roles --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:warlord_bridge --shots=/absolute/vorga-shots --quit-after=12
--world-demo=ashen_ford:goblin-roles '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Six refreshed looks are integrated across both batches; [the tracker](../production/refresh-status.json) retains 23 older combat looks for later work. No PR has been opened.

Subsequent batch: [hobgoblin and roadside bandit](../road-and-host/README.md) brings current progress to eight refreshed looks and 21 older combat looks remaining.
