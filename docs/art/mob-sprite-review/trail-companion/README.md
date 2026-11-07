# Hunting Hound refresh

Baseline #6 (`bosses.hound.tan`) now uses dedicated eight-direction art. The ranger companion has a lean canine silhouette, tan fur, a dark saddle, cream muzzle/chest/toes, floppy brown ears and a simple leather collar. Walking articulates its connected legs/paws, with restrained ear/tail motion; the natural bite lowers and extends the neck toward its target. The collar follows the neck and its buckle stays on the front/side. Grey and black hound library variants keep their own art.

- [Before / after](before-after.png): matching profile views beside the fighter at the same world zoom.
- [Interactive gallery](index.html): five actions, eight facings, single-facing close view for phones, and five embedded game/comparison views.
- [Animation MP4](trail-companion-motion.mp4) / [GIF](trail-companion-motion.gif): 16 seconds of walking, bite and idle motion in four representative facings using runtime cadence.
- [Actual Godot replay](godot-hound.mp4): nine seconds, silent, showing movement, bite and recovery through the normal TV renderer.
- All-facing [idle](idle-all-facings.png), [walk](walk-all-facings.png), [bite anticipation](attack-all-facings.png), [bite contact](attack-impact-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Actual game views: [battle](battle.png), [bite](bite-in-game.png), [overworld lineup](overworld-lineup.png), [town lighting/foreground occlusion](overworld-lighting-occlusion.png). Development lineups do not add encounter spawns; adjacent combatants naturally overlap during contact. The gallery shows unobstructed poses.

112 distinct selected complete poses: four idle, four walk, four bite, one hurt and one down per facing. Hurt/down are single poses, not an animated fall. Down is a grounded resting/slumped hound with its head against its forepaws; the collapsed head can turn sideways. Rear attacks face away, without painting the face on its back. No mirrored or independently moved limbs/accessories are used.

The neutral hound targets 56px head-to-floor height at 0.32 world scale; it stays below the adult party's waist. The HUD crop includes the actual head, both ears, muzzle and collar. A 0.8-second natural bite contacts at 0.4 seconds, then finishes its recovery. No special clip or active ability is invented. Existing rules, balance, passive, ability bindings, FX, audio, lore, player outfits and encounter placement remain unchanged.

## Verification

- Focused affected classes: **403 passed, zero failures** ([log](checks/focused.txt)). Covers complete silhouettes, all facings, atlas bounds, movement, scale, HUD rendering, friendly review placement, walkable round trip and full bite/contact/recovery timing.
- Full presentation suite: **2,997 passed / 3 failed, no skips** ([log](checks/presentation.txt)). The three known story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections. The full .NET solution was not rerun.
- [Test](checks/test-build.txt) and [host](checks/host-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [motion replay](checks/moves.txt) and [recording](checks/recording.txt) captured. The cloud host has no ALSA output device and falls back to dummy audio (ERR_CANT_OPEN); clips are silent and audio was not reviewed. Existing software-renderer and static-fixture shutdown warnings remain. This is a visual/functional cloud review, not a hardware performance benchmark.
- [Browser validation](browser-validation.json): five embedded views decode; all five actions differ; [all four walking phases differ in every facing](checks/directional-motion.json); no page errors or horizontal overflow at 390px; single-facing close view works.
- [Scope check](checks/scope.json) and [runtime hashes](checks/runtime-assets.json): only one new creature entry/atlas; rules, ability bindings, prior art, portraits and the historical 98-look baseline remain unchanged.

Original sources, prompts, IDs, hashes and reproducible scripts: `tools/tactics-assets/imagegen/trail-companion-2026-11-07/`. Scenario concept/receipt: `tools/tactics-assets/concepts/trail-companion-2026-11-07/`.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:trail-companion --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=trail-companion-moves --shots=/absolute/move-shots --quit-after=10
--world-demo=ashen_ford:trail-companion '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Collect with `python tools/tactics-assets/imagegen/trail-companion-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --moves /absolute/move-shots`, then rebuild review.cjs and run verify-review.cjs.

Twenty-two looks refreshed across eleven batches; [seven older combat looks remain](../production/refresh-status.json). No PR opened.
