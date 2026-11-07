# Ivo Rime / necromancer and Oswin Hale / lich

Baseline #24 (`characters.necromancer`) and #23 (`characters.lich`) use dedicated eight-direction art. Their existing shared keys are preserved. Ivo reads as the living, exhausted Warden-trained physician from his biography, with short brown/grey hair, a charcoal-blue coat, cream scarf and medicine pouch. Oswin reads as the century-undead Last Warden-Keeper, with a connected ivory skull, empty sockets, slate-navy robes and worn ivory stole. His staff is unlit, respecting his refusal to use ember-fire.

- [Standalone interactive gallery](index.html): eight facings, six action controls and embedded comparisons/game captures.
- [Before / after](before-after.png), at the same world zoom.
- [Combined motion MP4](rime-keepers-motion.mp4) / [GIF](rime-keepers-motion.gif): 18 seconds of walking, staff melee and spellcasting in four representative directions, using runtime sequences and cadence.
- All-facing [idle](idle-all-facings.png), [staff wind-up](attack-all-facings.png), [staff impact](attack-impact-all-facings.png), [casting](cast-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Godot captures: [comparison battle](battle.png), [generic necromancer in actual Crypt Hall](crypt-encounter.png), [named Ivo in actual Rime Tower](rime-encounter.png), [overworld lineup](overworld-lineup.png), [town lighting and occlusion](overworld-lighting-occlusion.png). Oswin appears in the development lineups; no new lich encounter or overworld enemy spawn is introduced.

272 selected complete poses: 136 per role. Each facing has four idle, four walk and four physical staff-attack keys, one hurt pose, one down pose and three selected spell keys. Hurt/down are single poses, not animated falls. Oswin's idle settles quietly without biological breathing. Both staves stay in the right hand; the left may briefly support a physical strike, then casts separately while the staff remains planted. Dedicated corrections handle Ivo's raised invocation at two difficult angles, Oswin's forward palm at front-left, and both back-left hurt grips. Fallen staves lie on the ground in the connected right hand. No runtime mirroring or independent equipment movement is used.

Existing cast, point, summon and special presentation bindings use the authored spell gestures. Projectiles and spell FX remain owned by the game renderer. Ivo's generic and named combat appearances still share the necromancer key. Oswin's lich key is ready for existing uses without changing encounter rules. Twelve new 40px dialogue portraits preserve the existing preset/mood bindings; both battle HUD heads crop 64px from their actual atlases.

Upright body targets are 120 source pixels for Ivo and 122 for Oswin, at 0.32 runtime scale. Their 256px padded cells accommodate staff gestures rather than enlarging the characters in the world. Previous approved creature entries and unrelated dialogue presets remain unchanged. No balance, abilities, spawn definitions, lore text, audio or player outfits changed.

## Verification

Final focused run: **341 passed, zero failures**. Both builds have zero warnings/errors. All five Godot capture runs exit successfully; lighting, occlusion and HUD heads render correctly.

- Focused [log](checks/focused.txt): directional motion, connected figures/weapons, atlas bounds/addressing, human scale, HUD heads, six dialogue moods, spell bindings and fixture placement. New focus-landmark checks cover the right-hand staff side through idle/walking and front/rear turns for both characters.
- Full presentation suite: **2,958 passed / 3 failed**, no skips ([log](checks/presentation.txt)). The three known story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections.
- [Host](checks/host-build.txt) and [test](checks/test-build.txt) builds; Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [crypt](checks/crypt.txt) and [Rime Tower](checks/rime.txt) capture logs. Existing fixture shutdown ObjectDB warnings remain.
- [Browser validation](browser-validation.json): six embedded scenes decode, six actions render distinct output, four locomotion phases differ, no page errors or horizontal overflow at 390px.
- [Scope protection](checks/scope.json), [runtime hashes](checks/runtime-assets.json), source prompts/IDs/hashes and reproducible export scripts under `tools/tactics-assets/imagegen/rime-keepers-2026-11-07/`. Scenario receipts under `tools/tactics-assets/concepts/rime-keepers-2026-11-07/`.

This is visual/functional review in a software-rendered cloud environment, not a hardware performance benchmark. The full .NET solution was not rerun; changes concern presentation art, development fixtures and their tests.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:rime-keepers --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=encounter:crypt_hall --shots=/absolute/crypt-shots --quit-after=12
--tactics-demo=encounter:rime_tower --shots=/absolute/rime-shots --quit-after=12
--world-demo=ashen_ford:rime-keepers '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Sixteen looks are refreshed across seven batches; [13 older combat looks remain](../production/refresh-status.json). The historical 98-look baseline stays unchanged. No PR has been opened.
