# Storm, Magma and Swamp Elementals — library batch 1

[Standalone animation gallery](index.html) · [Before/after](before-after.png) · [Three-creature motion reel](wild-elements-motion.mp4) · [GIF](wild-elements-motion.gif) · [Actual Godot replay, silent](godot-specials.mp4)

Refreshed `bosses.air_elemental.storm`, `bosses.earth_elemental.magma` and `bosses.water_elemental.swamp`. The 29-design combat pass stays complete and unchanged. This additional-library batch brings the branch to32 refreshed designs across 16 batches. [Library tracker](../production/library-refresh-status.json): three of31 older two-direction library looks refreshed;28 remain. Another 26 library appearances already have eight-direction art and keep it. No PR opened.

## Art and animation

[Concepts/receipts](../../../../tools/tactics-assets/concepts/wild-elements-2026-11-07/BRIEF.md) · [Pixel provenance/pipeline](../../../../tools/tactics-assets/imagegen/wild-elements-2026-11-07/README.md). Scenario supplied Storm's concept; new jobs were rate-limited, so OpenAI supplied Magma/Swamp concepts using the approved primaries as references. PixelLab was not used. Whole approved primary figures guide the material edits. Sixteen preserved source sheets supply 528 frame keys selecting 384 distinct figures; one corrected whole rear Storm hurt figure replaces a draft that looked sideways.

Storm has charcoal/indigo coils and steel/silver edges; Magma uses rough basalt with restrained orange fissures; Swamp has olive/peat water volumes and ochre foam rims. Anatomy, density and party scale match their approved primary families. Eight authored facings have idle, movement, natural attacks, hurt/down and two distinct special gestures. Hurt/down hold a single pose rather than an animated fall.

Storm spreads its connected coils for Whirlwind and thrusts both palms for Gale Blast. Magma plants alternating heavy feet, punches, raises/slams both fists for Tremor and crosses its forearms for Aegis. Swamp glides without legs, slams a palm, heaves both arms for Tidal Crush and pulls inward for Undertow. Faces stay off full backs, and all limbs/bases remain joined.

Runtime scale 0.32: neutral heights about 46/56/49 world pixels beside a1.75 m fighter. [Battle](battle.png), [overworld lineup](overworld-lineup.png), [town lighting/occlusion](overworld-lighting-occlusion.png), [six beat-timed specials](specials-in-game.png). Portraits crop the front head; raised-pose clearance keeps bars above the silhouette. The combined 24 s reel shows all three together at common zoom and four representative facings; select any of eight directions in the gallery.

The three exact variant keys inherit their refreshed primary family's visual gesture/timing. Most contact at0.4 s/finish 0.8 s; Tremor contacts 0.5 s/finishes 1 s. Undertow targets its selected foe; self moves face the nearest opponent. Existing effects/audio remain unchanged. No stats, damage, resistances, abilities, rules, encounter placements or dialogue portraits change. These five-unit lineups are development art fixtures, not new encounters.

## Verification

- Host and test builds: zero warnings/errors.
- Focused art, attachment, scale, HUD, atlas, board and beat checks:542 passed.
- Full presentation suite: 3,071 passed / 3,074 total; only the same three pre-existing story-count failures remain (`checks/full.log`). These have [baseline evidence](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md). Full solution not rerun for this art/presentation batch.
- Strict source-edge/neighbour checks and projected bounds pass for all 528 packed keys. Every idle/movement/attack/special/cast has four distinct pixel keys in all eight facings. Corrected Storm rear hurt is selected; rejected sourcePose 8 is excluded.
- Browser validation decodes all five embedded scene images, exercises seven actions and movement frames, and fits a390 px phone viewport without horizontal overflow.
- Native Godot battle/world/replay reviewed for contrast, party scale, connected limbs, forward strikes, rear views, HUD portraits, bar clearance and recovery. Software rendering is a visual check, not a performance benchmark. Dummy audio; video is silent.
- Scope receipts preserve prior creature entries, runtime art, all rule/ability/FX JSON, portraits, the completed combat tracker and original 98-look inventory. Only three new creature entries/atlases are added; baseline/production HTML only append a navigation link.

## Reproduce captures

Godot before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Host arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:wild-elements --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=wild-elements-moves --shots=/absolute/move-shots --quit-after=36
--preset=low --world-demo=ashen_ford:wild-elements '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/wild-elements-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`. Then run special-scenes.cjs, review.cjs and verify-review.cjs. Natural hit and six special-contact screenshots follow beat-clock moments; manual anticipation shots remain separate. Video shows three walks, three natural strikes, six specials and return to ready.

Next: [Basalt, Crystal and Temple Golems](../stone-sentinels/README.md), library batch 2.
