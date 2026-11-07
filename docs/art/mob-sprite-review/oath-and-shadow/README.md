# Ser Halvard / Black Knight and Drow Blade / Ysolde

Baseline #14 (`characters.black_knight`) and #17 (`characters.dark_elf`) now use dedicated eight-direction art. Existing shared keys are preserved. Halvard reads as the courteous, honourable oathbound captain from his biography: infernal-fire-blackened full plate, a closed great helm, steel-grey edges and a worn burgundy/ivory tabard. The Drow preset also supplies Ysolde, so the shared female elf retains grey-lilac skin, silver-white hair, pointed ears and practical light armor. Slate teal and muted plum distinguish the light duelist from the heavy knight.

- [Standalone interactive gallery](index.html): eight facings, six action controls and five embedded comparisons/game captures.
- [Before / after](before-after.png), at the same world zoom.
- [Combined motion MP4](oath-and-shadow-motion.mp4) / [GIF](oath-and-shadow-motion.gif): 18 seconds of walking, physical attacks and ward/shadow-step gestures in four representative directions, using runtime sequences and cadence.
- All-facing [idle](idle-all-facings.png), [attack wind-up](attack-all-facings.png), [attack impact](attack-impact-all-facings.png), [ward / invocation](cast-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png). Down previews use a wider zoom so complete grounded blades fit their cards; runtime sprite scale is unchanged.
- Godot captures: [comparison battle](battle.png), [Halvard at the actual Broken Gate](gate-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting and occlusion](overworld-lighting-occlusion.png). Both roles appear beside the fighter, refreshed bandit and Bone Warden in the development lineups. No new Drow encounter or Ysolde world region is introduced.

272 selected complete poses: 136 per role. Each facing has four idle, four walk and four physical attack keys, one hurt, one down and three selected ward/invocation keys. Hurt/down are single poses, not animated falls. Halvard's idle settles joints/tabard without biological breathing. His slower 5fps walk keeps both gauntlets on the same greatsword hilt: right by the crossguard, left by the pommel. The cleave holds anticipation/impact, while the separate ward raises the sword to the breastplate and bows the helmet. The Drow's 6fps walk keeps the rapier in the right hand and her white bob fixed at the nape. She attacks with a forward fencing thrust and invokes shadow step with her free left palm overhead. Rear views use back seams/plain belts.

Halvard's existing `slam` presentation binding explicitly uses the physical greatsword attack; his `buff` uses the separate ward. The Drow's `cast` binding uses the left-hand invocation. The game still owns spell/projectile effects. Twelve new 40px dialogue portraits retain both existing presets and all six mood bindings; HUD heads crop 64px from the actual sprite atlases. Halvard keeps his closed helm in every expression.

Upright body targets are 132 source pixels for Halvard and 120 for the Drow, both at 0.32 runtime scale. The knight's 288px and elf's 256px padded cells contain weapons/gestures rather than enlarging bodies in the world. Initial crowded Halvard walk/attack sheets are retained as rejected provenance; complete replacements pass the same strict source/atlas checks. Previous approved creature entries and unrelated dialogue presets are unchanged. No balance, abilities, equipment rules, spawn definitions, lore text, audio or player outfits changed.

## Verification

Final focused run: **350 passed, zero failures**. Host and test builds have zero warnings/errors. All three Godot capture runs exit successfully; scene lighting, foreground occlusion and battle HUD heads render correctly.

- Focused [log](checks/focused.txt): authored facing motion, connected figures/weapons, source addressing/bounds, human scale, HUD heads, six portrait moods, physical-cleave versus ward/shadow-step bindings, and walkable fixture placement.
- Full presentation suite: **2,967 passed / 3 failed, no skips** ([log](checks/presentation.txt)). The three existing story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections.
- [Host](checks/host-build.txt) and [test](checks/test-build.txt) builds; Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt) and [Broken Gate](checks/gate.txt) logs. Existing fixture shutdown ObjectDB warnings remain.
- [Browser validation](browser-validation.json): five embedded scenes decode, all six actions produce distinct output, four walk phases differ, no page errors or horizontal overflow at 390px.
- [Scope protection](checks/scope.json) and [runtime hashes](checks/runtime-assets.json). Exact prompts, generation IDs, source hashes and reproducible export scripts are under `tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/`; Scenario concept receipts are under `tools/tactics-assets/concepts/oath-and-shadow-2026-11-07/`.

This is visual/functional review in a software-rendered cloud environment, not a hardware performance benchmark. The full .NET solution was not rerun; changes concern presentation art, development fixtures and their tests.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:oath-and-shadow --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:keep_gate --shots=/absolute/gate-shots --quit-after=12
--world-demo=ashen_ford:oath-and-shadow '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Eighteen looks are refreshed across eight batches; [11 older combat looks remain](../production/refresh-status.json). The historical 98-look baseline stays unchanged. No PR has been opened.
