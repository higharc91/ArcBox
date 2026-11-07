# Risen of Frostpeak and wraith: integrated

Baseline #28 (`characters.zombie`) and #12 (`bosses.wraith.wraith`) now use dedicated eight-direction art. The Risen shared appearance represents one former human pilgrim from the bestiary's dead of the Frostpeak pass: ashen face, sparse grey hair, moss-olive tunic, patched umber jerkin, faded cream scarf and muddy boots. The wraith follows the memoryless spirit lore with a charcoal-indigo hood, cold grey hands and a connected floating tail. Both remain empty rather than gleefully cruel.

- [Standalone interactive gallery](index.html): eight facings, six action controls and embedded comparison/game captures. The drain/cast control leaves the Risen idle because it has no spell.
- [Before / after](before-after.png), at the same world zoom.
- [Combined motion MP4](hollow-dead-motion.mp4) / [GIF](hollow-dead-motion.gif): 18 seconds of locomotion, physical attacks and wraith life drain in four representative directions, using runtime sequences and cadence.
- All-facing [idle](idle-all-facings.png), [physical wind-up](attack-all-facings.png), [physical impact](attack-impact-all-facings.png) and [life drain](drain-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Actual Godot scenes: [battle lineup](battle.png), [Crypt Hall](crypt-encounter.png), [Rime Tower](rime-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting and occlusion](overworld-lighting-occlusion.png). Lineups are development fixtures, not new overworld enemy spawns.

248 selected complete poses: 112 for the Risen and 136 for wraith. Each role/facing has four idle, four locomotion and four physical attack keys, plus one hurt and one down pose. Wraith adds three drain keys per facing. Hurt/down are single poses, not animated falls: Risen lies prone; wraith collapses into a connected cloth remnant with extinguished eye slits. Nothing is scattered into detached body parts or wisps.

Risen uses a heavy four-key/second shuffle and two-handed slam. Wraith uses a five-key/second spectral glide without legs, a single-arm physical touch and a separate two-handed gather/pull for `bosses.special` life drain. Three right-facing physical rows have authored corrections so the reach goes toward the target. Its unarmed touch can use either visible arm; no equipment hand swaps or runtime mirroring are involved. The game retains ownership of spell FX.

Upright visual targets are 112 source pixels for the stooped Risen and 126 for wraith including the tail, both at 0.32 runtime scale. Ready attack poses calibrate to the same height as idle. Six new 40px Risen dialogue portraits preserve existing expression roles. Both HUD heads crop 64px from the actual atlas; no unused wraith dialogue preset was added. Existing appearance keys update current encounters. Balance, abilities, spawn definitions, lore text, audio and player outfits are unchanged.

## Verification

- Final focused run: **331 passed, zero failures** ([log](checks/focused.txt)). Includes all fourteen refreshed looks, atlas addressing/bounds, connected figures, real directional motion, scale, HUD heads, portrait roles, wraith special binding and walkable fixture placement.
- Full presentation suite on the final state: **2,948 passed / 3 failed**, no skips ([complete log](checks/presentation.txt)). Three known story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections.
- [Host](checks/host-build.txt) and [test](checks/test-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [crypt](checks/crypt.txt) and [Rime Tower](checks/rime.txt) paths load the new art. Existing fixture shutdown ObjectDB warnings remain.
- [Browser validation](browser-validation.json): all six embedded scenes decode, six action controls render distinct output, four locomotion phases differ, no page errors and no horizontal overflow at 390px.
- [Scope protection](checks/scope.json) and [runtime hashes](checks/runtime-assets.json): exactly two new creature entries and one changed portrait preset; all previous entries and unrelated portraits are preserved. Eleven source PNGs, exact prompts, generation IDs, hashes, corrections and reproducible technical export scripts: `tools/tactics-assets/imagegen/hollow-dead-2026-11-07/`. Scenario concept receipts: `tools/tactics-assets/concepts/hollow-dead-2026-11-07/`.

This is visual/functional review in a software-rendered cloud environment, not a hardware performance benchmark. The full .NET solution was not rerun; changes concern presentation art, development fixtures and their tests.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:hollow-dead --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:crypt_hall --shots=/absolute/crypt-shots --quit-after=12
--tactics-demo=encounter:rime_tower --shots=/absolute/rime-shots --quit-after=12
--world-demo=ashen_ford:hollow-dead '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Fourteen looks are refreshed across six batches; [15 older combat looks remain](../production/refresh-status.json). The historical 98-look baseline stays unchanged. No PR has been opened.
