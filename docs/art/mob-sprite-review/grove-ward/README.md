# Grove Sapling refresh

Baseline #9 (`bosses.sapling.spring`) now uses dedicated eight-direction pixel art: warm knotted bark, restrained moss, small recessed amber eyes, compact spring leaf masses, root feet and a coiled living vine arm. It remains a small summoned plant companion, roughly waist-high beside the party. Autumn/blossom library variants retain their own art.

- [Before / after](before-after.png), with the fighter at the same world zoom.
- [Interactive gallery](index.html): five actions, eight facings, a phone close view and five embedded comparison/game images. Wider cells show the complete extended vine.
- [Animation MP4](grove-ward-motion.mp4) / [GIF](grove-ward-motion.gif): 16 seconds of root-footed walking, physical lashes and idle motion in four representative directions using runtime cadence.
- [Actual Godot replay](godot-sapling.mp4): nine seconds, silent, showing movement, a two-tile vine strike, impact and recovery through the normal TV renderer.
- All-facing [idle](idle-all-facings.png), [walk](walk-all-facings.png), [lash anticipation](attack-all-facings.png), [lash contact](attack-impact-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Game captures: [battle](battle.png), [vine contact](lash-in-game.png), [overworld lineup](overworld-lineup.png), [town lighting/foreground occlusion](overworld-lighting-occlusion.png). These are development lineups, not new encounter spawns. Contact uses the beat's own hit clock: cloud renderer stalls can make fixed wall-clock shots early.

112 packed frame keys select 96 distinct authored figures: 32 idle, 32 walk, eight windups, eight corrected contacts and sixteen hurt/down poses. Matching idle figures supply attack ready/recovery. Eight draft contacts that lashed backward or switched arms are excluded. Corrected contacts look and strike toward their target, with the vine continuously joined through its wrist. Both crown masses stay joined to their twigs, and roots/branch arms stay joined to the trunk. Hurt/down are single poses, not an animated fall; down rests the joined plant sideways. No mirroring, independently moved limbs or pixel repainting is used.

The successful concept draws its vine on the anatomical left arm; production follows that orientation. Neutral height is 64px crown-to-floor at 0.32 world scale. Both leaf masses register the head together; attack contact anchors on brown wooden root feet rather than a vine tip reaching below them. 320px cells at [160,192] keep the full lash inside its frame without enlarging the body. A [133,125,54,54] HUD crop contains both crown masses and the bark face. Existing optional animation headroom clears raised pixels without changing body size.

Its existing vine_lash is a physical, non-ranged natural attack with two-tile reach. The generic renderer previously inferred an arrow at that distance. The spring Sapling's basic attack now uses the physical lunge path at one or two tiles, contacting on the extended-vine frame at 0.4s and completing recovery through 0.8s. Explicit visual overrides remain intact. No projectile, spell, active ability, FX/audio asset, damage, range, resistance, balance, lore, outfit or encounter placement is added or changed. Rule/ability JSON remains unchanged.

## Verification

- Focused affected classes: **475 passed, zero failures** ([log](checks/focused.txt)). Covers all facings, complete silhouettes, atlas bounds, root movement, scale, head effects, HUD rendering, friendly review placement and walkable route. Regressions verify physical one-/two-tile attacks, no inferred projectile, exact contact/recovery timing and preservation of explicit projectile overrides.
- Full presentation suite: **3,009 passed / 3 failed, no skips** ([log](checks/presentation.txt)). The three known story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections. The full .NET solution was not rerun.
- [Test](checks/test-build.txt) and [host](checks/host-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt), [motion replay](checks/moves.txt) and [recording](checks/recording.txt) use dummy audio; clips are silent and audio was not reviewed. Existing software-renderer/static-fixture shutdown warnings remain. This cloud review is not a hardware performance benchmark.
- [Browser validation](browser-validation.json): all five embedded views decode, all five actions differ, no page errors or horizontal overflow at 390px, and single-facing close view works. [All four frames differ in each direction](checks/directional-motion.json) for idle, walk and attack.
- [Scope protection](checks/scope.json) and [runtime hashes](checks/runtime-assets.json): one new creature atlas/entry; rules, ability JSON, existing art, portraits and the historical 98-look baseline stay unchanged. Five original source sheets retain 104 figures; 96 are selected and ready/recovery reuse sixteen of those selections.

Scenario concept and both attempt receipts: `tools/tactics-assets/concepts/grove-ward-2026-11-07/`. Sources, exact prompts, IDs, hashes, correction selections and reproducible scripts: `tools/tactics-assets/imagegen/grove-ward-2026-11-07/`.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:grove-ward --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=grove-ward-moves --shots=/absolute/move-shots --quit-after=11
--world-demo=ashen_ford:grove-ward '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Use Godot `--audio-driver Dummy` before `--` on hosts without audio devices. Collect with `python tools/tactics-assets/imagegen/grove-ward-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --moves /absolute/move-shots`, then rebuild review.cjs and run verify-review.cjs.

Twenty-four looks refreshed across thirteen batches; [five older combat looks remain](../production/refresh-status.json). No PR opened.
