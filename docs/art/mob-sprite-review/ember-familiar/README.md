# Bound Imp refresh

Baseline #7 (`bosses.imp.crimson`) now has dedicated eight-direction pixel art: crimson skin, narrow amber eyes, two ivory horns, two connected burgundy bat wings, a rooted spade tail and a plain rope-tied waistwrap. It remains a small summoned companion beside the party. The violet library variant keeps its own art.

- [Before / after](before-after.png), beside the fighter at the same world zoom.
- [Interactive gallery](index.html): five actions, eight facings, a single-facing close view for phones and five embedded comparison/game views.
- [Animation MP4](ember-familiar-motion.mp4) / [GIF](ember-familiar-motion.gif): 16 seconds of wing-assisted stepping, casting and idle motion in four representative directions using runtime cadence.
- [Actual Godot replay](godot-imp.mp4): nine seconds, silent, showing movement, a hand-cast fire projectile, impact and recovery through the normal TV beat path.
- All-facing [idle](idle-all-facings.png), [movement](walk-all-facings.png), [cast gathering](attack-all-facings.png), [cast release](attack-impact-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Game views: [battle](battle.png), [bolt release](bolt-in-game.png), [overworld lineup](overworld-lineup.png), [town lighting/foreground occlusion](overworld-lighting-occlusion.png). These are development lineups, not new encounter spawns.

112 selected complete frame keys: four idle, four movement, four cast, one hurt and one down per facing. Hurt/down are single poses, not an animated fall. The collapsed body rests low with wings, limbs, tail and horns still joined. The original full-back cast pointed sideways using the wrong arm; four replacement whole figures now gather on the far side and cast away beside the right shoulder. Both upper-back wing roots remain present. No mirroring or independently moved body parts are used.

The neutral Imp targets 56px horn-to-floor height at 0.32 world scale, approximately half the fighter's height. Ivory horns register the head, avoiding a moving wing tip as the anchor. The HUD crop contains the actual horns, ears and face. Walking is a grounded or low-hover scamper with shoulder-driven wing movement, not high aerial flight.

The existing generic basic ranged attack previously fell back to an arrow. For the crimson Imp's `common.attack`, the renderer now infers the existing `projectiles.fireball` and `fx.flames.hellfire` visuals, plus its dedicated cast clip; explicit presentation overrides are preserved. Release occurs at the forward hand pose at 0.4s, and recovery completes through 0.8s or the end of a longer flight. The existing fire projectile uses the normal spell-cast sound instead of bow release. No new spell/active ability, damage, status, balance, FX/audio asset, lore, encounter placement or player outfit is introduced; all ability and monster JSON remains unchanged.

## Verification

- Focused affected classes: **469 passed, zero failures** ([log](checks/focused.txt)). Covers all facings, complete silhouettes, atlas bounds, movement, scale, HUD rendering, friendly review placement and walkable route. New regressions cover close/long-range fire inference, exact release-frame timing, complete recovery and explicit projectile overrides. The legacy exact height assertion now uses the unchanged violet Imp; the refreshed crimson variant checks effects against its actual head position.
- Full presentation suite: **3,003 passed / 3 failed, no skips** ([log](checks/presentation.txt)). The three known story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections. The full .NET solution was not rerun.
- [Test](checks/test-build.txt) and [host](checks/host-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [motion replay](checks/moves.txt) and [recording](checks/recording.txt) run with dummy audio; clips are silent and audio was not reviewed. Existing software-renderer and static-fixture shutdown warnings remain. This cloud review is not a hardware performance benchmark.
- [Browser validation](browser-validation.json): five embedded views decode; five action hashes differ; no page errors or horizontal overflow at 390px; single-facing close view works. [All four frames differ in every facing](checks/directional-motion.json) for idle, movement and casting.
- [Scope protection](checks/scope.json), [runtime hashes](checks/runtime-assets.json): one new creature entry/atlas; rules, ability JSON, existing art, portraits and historical 98-look baseline remain unchanged. Five original source sheets retain 116 figures, of which 112 are selected; the rejected four full-back keys are not exported.

Scenario concept/receipt: `tools/tactics-assets/concepts/ember-familiar-2026-11-07/`. Sources, prompts, IDs, hashes, correction selection and reproducible scripts: `tools/tactics-assets/imagegen/ember-familiar-2026-11-07/`.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:ember-familiar --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=ember-familiar-moves --shots=/absolute/move-shots --quit-after=11
--world-demo=ashen_ford:ember-familiar '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Use Godot `--audio-driver Dummy` before `--` on hosts without an audio device. Collect with `python tools/tactics-assets/imagegen/ember-familiar-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --moves /absolute/move-shots`, then rebuild review.cjs and run verify-review.cjs.

Twenty-three looks refreshed across twelve batches; [six older combat looks remain](../production/refresh-status.json). No PR opened.

Subsequent batch: [Grove Sapling](../grove-ward/README.md).
