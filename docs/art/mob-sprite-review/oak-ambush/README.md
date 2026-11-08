# Oak Mimic refresh

Baseline #8 (`bosses.mimic.oak`) now uses dedicated eight-direction art. Its closed disguise has detailed oak planks, dark iron hoops and a brass latch; skittering reveals four short feet, and its hinged bite reveals connected gums, teeth and a rooted tongue. Plain rear wood and hinges stay on the back. The ironbound library variant retains its own art.

- [Before / after](before-after.png), with the fighter at the same world zoom.
- [Standalone interactive gallery](index.html): five actions, eight directions, single-facing phone view and five embedded game/comparison views.
- [Animation MP4](oak-ambush-motion.mp4) / [GIF](oak-ambush-motion.gif): 16 seconds of walking, biting and idle motion in four representative directions using runtime cadence.
- [Actual Godot replay](godot-mimic.mp4): 12 seconds, silent, showing disguise, skitter, natural bite, adhesive bite and restored disguise through the normal TV beat/effect path.
- All-facing [idle](idle-all-facings.png), [walk](walk-all-facings.png), [bite anticipation](attack-all-facings.png), [bite impact](attack-impact-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Game captures: [battle](battle.png), [adhesive bite](bite-in-game.png), [overworld lineup](overworld-lineup.png), [town lighting/foreground occlusion](overworld-lighting-occlusion.png). These development lineups do not add enemy spawns. Adjacent units naturally overlap during contact; the gallery shows unobstructed poses.

112 packed frame keys select 96 distinct authored poses. Ready/recovery reuse closed idle figures; hurt/down are single poses, and down is a low slumped chest with a weakly open lid, not an animated fall. The mouth is occluded on the far side in the full-back attack. Source row ordering is corrected by selecting whole authored figures, without mirroring or moving individual parts.

The small chest targets 64px closed height at 0.32 world scale, below the fighter's waist. Walk/bite sheets allow short bracing feet without enlarging it into a giant. The HUD deliberately shows the complete disguised chest rather than an invented head. Attack headroom lifts the health/status stack above the open lid. Natural and adhesive bite share the existing `bosses.attack` binding; impact stays at 0.4s and recovery completes through 0.8s. No mechanics, balance, ability bindings, FX, audio, lore, outfits or encounter placement change.

## Verification

- Focused affected classes: **589 passed, zero failures** ([log](checks/focused.txt)). Covers atlas bounds, connected silhouettes, all facings, motion, scale, complete-chest HUD crop, overhead clearance, review placement and bite timing.
- Full presentation suite: **2,993 passed / 3 failed, no skips** ([log](checks/presentation.txt)). The same three story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections. The full .NET solution was not rerun.
- [Test](checks/test-build.txt) and [host](checks/host-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt) and [motion replay](checks/moves.txt) captured. Existing static-fixture shutdown ObjectDB warnings remain. This cloud software-renderer review is not a hardware performance benchmark.
- [Browser validation](browser-validation.json): all five embedded views decode; five action hashes differ; all four walk phases differ; no page errors or horizontal overflow at 390px; single-facing close view works.
- [Scope protection](checks/scope.json) and [runtime hashes](checks/runtime-assets.json): only the new Mimic atlas/entry; rules, ability bindings, prior art and historical 98-look baseline remain unchanged.

Scenario concept/receipts: `tools/tactics-assets/concepts/oak-ambush-2026-11-07/`. Original pixel sources, prompts, IDs, hashes, packing and reproducible scripts: `tools/tactics-assets/imagegen/oak-ambush-2026-11-07/`.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:oak-ambush --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=oak-ambush-moves --shots=/absolute/move-shots --quit-after=12
--world-demo=ashen_ford:oak-ambush '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Collect with `python tools/tactics-assets/imagegen/oak-ambush-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --moves /absolute/move-shots`, then rebuild review.cjs and run verify-review.cjs.

Twenty-one looks refreshed across ten batches; [eight older combat looks remain](../production/refresh-status.json). No PR opened.

Subsequent batch: [Hunting Hound](../trail-companion/README.md).
