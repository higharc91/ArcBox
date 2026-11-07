# Water Elemental and Red Dragon — batch fifteen

[Standalone eight-direction gallery](index.html) · [Before/after](before-after.png) · [Combined motion reel](tide-and-wyrm-motion.mp4) · [GIF](tide-and-wyrm-motion.gif) · [Actual Godot replay, silent](godot-specials.mp4)

Refreshed `bosses.water_elemental.sea` and `bosses.dragon.red`. Combat-roster pass complete: 29 designs across 15 batches; no older combat looks remain in this pass. Additional creature-library colour variants remain separate. No PR opened.

## Art and motion

[Scenario concepts/receipts](../../../../tools/tactics-assets/concepts/tide-and-wyrm-2026-11-07/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/README.md). Thirteen preserved source sheets supply 272 distinct selected figures and 384 packed frame keys. Four Water rear special figures are corrected wholesale. All eight facings have idle, movement, natural attacks, hurt/down and complete special gestures; down is a held collapsed pose rather than an animated fall.

Water uses dark teal liquid volumes, turquoise wave planes and pale foam rims, continuously joined arms and a single column/pool base. It glides, slams a palm, heaves both arms forward for Tidal Crush and pulls inward for Undertow. The adult red dragon has four grounded legs plus two distinct dorsal wings; wings settle folded when walking, unfurl to brace a bite, half-open for breath/roar and sweep down for Cataclysmic Wingbeat. Horns, jaws, neck, legs, tail and wing membranes stay attached. Faces appear on forward/side views, never on full backs.

Both retain pixelScale 0.32. Water stands approximately 49 world pixels high; Dragon 64 px at the horns in neutral front posture, with a long folded-wing quadruped silhouette. [Battle with fighter reference](battle.png), [overworld lineup](overworld-lineup.png), [town lighting/occlusion](overworld-lighting-occlusion.png), [five specials](specials-in-game.png). The eight-action selector's Wingbeat entry applies only to Dragon; Water rests idle there. The 40 s reel shows both together at common zoom, four representative facings, complete movement/attack/primary/secondary/wingbeat clips.

Natural jaws use their real two-tile physical reach. Existing FX/audio synchronize to Water contact 0.4 s, Dragon bite/breath/roar0.5s and wingbeat0.7s. Breath starts at the mouth and shares the pose heading; Undertow is a directed pull with its target effect. Attack/recovery completes before return to idle. Stats, abilities, rules, encounter placements, other variants and dialogue portraits remain unchanged. These four-unit development lineups are review fixtures, not new encounters.

## Verification

- Host/test builds: zero warnings/errors. Focused art, animation, HUD, board and beat checks: 520 passed; the additional five complete-atlas regressions also pass.
- Full presentation suite: 3,054 passed / 3,057 total; only the same three pre-existing story-count failures remain (`checks/full.log`). These have [baseline evidence](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). Full solution not rerun for this art/presentation batch.
- All384 selected frame keys pass source-neighbour/edge and export bounds checks. Each action has four distinct authored keys in all eight facings, except the one-pose hurt/down states. Exact source/runtime hashes, correction selection and damage ordering are verified in checks.
- Browser gallery decodes five embedded scene images, exercises eight actions and four movement frames, and fits a 390 px phone viewport without horizontal overflow. Select one facing for a close view.
- Actual Godot 4.7.2 compatibility-renderer battle/world/move captures reviewed for scale, connected limbs/wings, grounded movement, jaws at reach, mouth-aligned breath, HUD portraits, raised-pose bar clearance and full recovery. Dummy audio; video is silent. Software rendering is a visual check, not a performance benchmark.
- Scope preserves previous runtime art/creature entries, all rules/ability/FX JSON, portraits and the original 98-look inventory. Baseline/production HTML changes only append this gallery link.

## Reproduce game review

Godot flags before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Host flags after `--`:

```sh
--preset=low --tactics-demo=river_crossing:tide-and-wyrm --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=tide-and-wyrm-moves --shots=/absolute/move-shots --quit-after=30
--preset=low --world-demo=ashen_ford:tide-and-wyrm '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

The world demo exits when its walk/two camera spots finish; 90 s allows a cold software-rendered host to finish. Collect with `python tools/tactics-assets/imagegen/tide-and-wyrm-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`. Run special-scenes.cjs, review.cjs and verify-review.cjs. Special contact and natural-hit screenshots follow beat-clock moments, avoiding first-use FX loading drift. Separate manual windup captures are retained. Replay shows two walks, two natural strikes, all five specials and return to ready.
