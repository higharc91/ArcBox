# Vampire Bat, Undead Bear and Crystal Slime — library batch 5

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Combined animation MP 4](crypt-kin-motion.mp4) · [GIF](crypt-kin-motion.gif) · [Native Godot replay](godot-specials.mp4)

First batch for [issue #272](https://github.com/higharc91/ArcBox/issues/272), on `mob-library-refresh-272` after PR #273. Main stays unchanged for the user's playtest. Refreshes `beasts.bat.vampire`, `beasts.bear.undead` and `beasts.slime.crystal`. This brings the project to 44 refreshed designs across 20 batches: 29 combat designs plus 15 additional library appearances.16 older library looks remain;26 established eight-direction looks are preserved. [Library tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/crypt-kin-2026-11-08/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/crypt-kin-2026-11-08/README.md).

The vampire bat has charcoal fur, burgundy rooted wings and a real upstroke/downstroke cycle. The first flight draft only tilted outspread wings and was rejected. Torso registration keeps the whole body steady while the complete wings move. The undead bear retains a heavy quadruped silhouette, grounded paw gait, ash-grey fur and restrained bone/moss details. Three fresh whole rear-down poses correct the original sideways fallbacks. The crystal slime squashes at its attached base and strikes with a joined pseudopod; the crystal ridge turns with the body and full backs have no face. No anatomy patches or mirrored parts.

336 packed keys select 336 distinct figures from 14 preserved source PNGs. Five actions and eight facings per creature; hurt/down are held poses. Common-zoom review preserves relative body scale. [Battle](battle.png), [world lineup](overworld-lineup.png), [town lighting/occlusion](overworld-lighting-occlusion.png), [three engine-timed contacts](actions-in-game.png). The bat has 320 px padding for wing reach, without changing body scale. Portraits show front faces; bars clear the highest neutral and active silhouettes.

No rules, stats, abilities, ranges, sound assets, encounter placement, dialogue portraits or earlier runtime art change. Exact refreshed variant keys receive their authored .4 s natural contact and .8 s clip recovery. A reusable development-only library lineup/replay keeps future batches on the normal TV renderer path.

## Verification

- Host/test builds: zero warnings/errors. [607 focused checks passed](checks/focused.txt), including silhouettes, attachments, scale, HUD, atlas bounds, walkable review fixtures and natural-contact timing. After correcting development-fixture physical damage types, [179 beat cases passed again](checks/beats-final.txt).
- All 336 packed keys fit; 14 source PNGs match their generation originals. The rejected bat flight draft is unselected; whole rear-down corrections replace three sideways bear poses. [Scope](checks/scope.json).
- Standalone browser review decodes five embedded scenes, exercises all five actions and four distinct walk keys, and fits a 390 px phone viewport. All gallery/reel silhouettes fit their panels, including fully raised/lowered wings. [Browser](browser-validation.json) · [Artifact bounds](checks/artifact-bounds.json).
- Native battle, world and replay reviewed for relative scale, lighting/occlusion, front portraits, back views, contact and bar clearance. The silent 20-second replay uses three round trips and three natural attacks. The 24-second combined reel uses four representative facings; all eight are selectable in the gallery.
- Rules, abilities, previous creature entries/images, portraits, completed combat tracker and historical baseline are unchanged. Exact refreshed keys use authored .4 s contact and .8 s recovery. Development-only fixtures do not add encounters.
- Full presentation suite is planned after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 tests.

## Reproduce native captures

Use Godot Mono 4.7.2 with the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Host arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-crypt-kin --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-crypt-kin-moves --shots=/absolute/move-shots --quit-after=22
--preset=low --world-demo=ashen_ford:library-crypt-kin '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/crypt-kin-2026-11-08/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. Native replay includes three movement round trips, three adjacent natural attacks and recovery. It is silent with Dummy audio. Software-rendered captures verify appearance, not hardware performance.
