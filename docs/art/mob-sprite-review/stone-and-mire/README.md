# Troll and granite Stone Golem

Baseline #10 (`bosses.troll.troll`) and #5 (`bosses.golem.granite`) now use dedicated eight-direction art. The troll has a hooked profile, long clawed arms, olive-grey skin, a fixed short crest and rope-bound hipwrap. The Golem has carved granite planes, rigid connected joints, dark eye recesses and restrained fixed moss. Their existing combat definitions use natural claws/fists, so neither receives a carried weapon. Frost/ogre trolls and basalt/temple/crystal golems retain their distinct library art.

- [Standalone interactive gallery](index.html): seven actions, all eight facings, a single-facing close view for phones, and five embedded comparisons/game views.
- [Before / after](before-after.png), at the same world zoom.
- [Combined motion MP4](stone-and-mire-motion.mp4) / [GIF](stone-and-mire-motion.gif): 24 seconds of walking, physical strikes, primary specials and buffs in four representative directions, using runtime sequences/cadence.
- [Actual Godot special-move recording](godot-specials.mp4), silent, and [four-move contact sheet](specials-in-game.png). These use the normal TV beat/projectile/effect path with the four real ability presentations.
- All-facing [idle](idle-all-facings.png), [walk](walk-all-facings.png), [melee anticipation](attack-all-facings.png), [melee impact](attack-impact-all-facings.png), [special anticipation](special-windup-all-facings.png), [special release/impact](special-release-all-facings.png), [buff](buff-all-facings.png), [hurt](hurt-all-facings.png) and [down](down-all-facings.png).
- Actual game views: [comparison battle](battle.png), [overworld lineup](overworld-lineup.png), [town lighting/foreground occlusion](overworld-lighting-occlusion.png). These are development lineups beside the fighter, refreshed bandit and Bone Warden; no encounter or town enemy spawns are added.
- Full-resolution special captures: [held boulder](troll-boulder-held.png), [released boulder](troll-boulder-release.png), [frenzy roar](troll-frenzy-roar.png), [slam anticipation](golem-slam-windup.png), [slam impact](golem-slam-impact.png), [stone aegis](golem-stone-aegis.png). The boulder-release capture uses the beat's event clock; the cloud renderer can hitch on first projectile load, making fixed wall-clock shots early.

352 selected complete poses: 176 per role. Each of eight facings has four idle, four walk, four melee, four primary-special and four buff keys, plus one hurt and one down pose. Hurt/down are single poses, not animated falls. Complete body/limb/joint silhouettes remain connected. The troll's crest stays attached to its scalp, rear rope knots stay on the back, and the Golem has no biological breathing or independently drifting masonry.

The troll walks at 5fps; the Golem moves at 4fps with rigid joint articulation. The troll's two-handed boulder heave releases at 0.4s: its held rock disappears exactly when the existing projectile starts. A corrected full-back strip releases with both arms toward the far side, rather than throwing sideways. Frenzy uses a separate open-jawed, spread-claw roar. The Golem distinguishes its single-fist attack, two-fist seismic slam and crossed-arm aegis. Seismic dust/damage begin at the 0.5s fist-contact frame, while recovery completes through 1s. Heavy melee and self buffs retain their full 0.8s clips.

Only the roar/aegis visual bindings change to `bosses.buff`. The boss manifest registers that key as the original special clip for old library sheets. All old animation entries, balance, ability effects, costs, projectiles, FX, equipment, lore, spawns, audio and player outfits remain unchanged. The base troll/Golem overrides use the new distinct buff clips.

The troll targets 188 upright source pixels and the Golem 172, both at 0.32 runtime scale, deliberately larger than the human fighter. Padding is 320px at [160,288] for the troll's raised boulder and 256px at [128,224] for the Golem. Fallen bodies centre across the tile without stretching or separately moving limbs. HUD crops use the actual front-facing head position, preserving the troll's full muzzle; global head bounds still keep overhead marks above every facing. Optional per-animation headroom lifts the health/status stack during the overhead specials, clearing the raised boulder and fists while leaving body size and idle marks unchanged. The new UI regression checks compare bar placement with actual opaque sprite pixels. No dialogue portrait preset uses these creatures, so existing dialogue art is untouched.

## Verification

- Focused affected-class run: **582 passed, zero failures** ([log](checks/focused.txt)). Covers complete anatomy, atlas bounds, all authored facings, motion, scale, HUD heads, distinct ability bindings, legacy buff fallback, playable review placement and beat timing/projectile/impact synchronization. Legacy down-frame and overhead-effect tests now use the unchanged frost troll, retaining their original multi-frame coverage.
- HUD-only follow-up after the front-head crop correction: **116 passed, zero failures** ([log](checks/hud-final.txt)).
- Full presentation suite: **2,986 passed / 3 failed, no skips** ([log](checks/presentation.txt)). The three existing story-count failures were previously reproduced on unchanged main in [the ambience report](../../../superpowers/reports/2026-11-06-ashen-ford-ambience/README.md): 590 vs 614 speakers, 921 vs 945 lines, 8 vs 32 camp reflections.
- [Test](checks/test-build.txt) and [host](checks/host-build.txt) builds: zero warnings/errors. Godot [import](checks/import.txt), [battle](checks/battle.txt), [overworld](checks/overworld.txt) and [special-move replay](checks/specials.txt) complete without errors. Existing static-fixture shutdown ObjectDB warnings remain.
- [Browser validation](browser-validation.json): all five embedded views decode, seven actions differ, all four walk phases differ for each creature, no page errors or horizontal overflow at 390px, and the single-facing close view renders correctly.
- [Scope protection](checks/scope.json) proves combat mechanics, existing FX/projectiles, prior creature entries, legacy animation entries, dialogue portraits and the historical 98-look baseline are unchanged. [Runtime hashes](checks/runtime-assets.json); original sources/prompts/IDs/hashes and reproducible scripts are under `tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/`. Scenario receipts are under `tools/tactics-assets/concepts/stone-and-mire-2026-11-07/`.

The full .NET solution was not rerun. This is a visual/functional presentation review in a software-rendered cloud environment, not a hardware performance benchmark.

Host arguments after `--`:

```sh
--tactics-demo=river_crossing:stone-and-mire --shots=/absolute/battle-shots --quit-after=12
--tactics-demo=stone-and-mire-moves --shots=/absolute/special-shots --quit-after=18
--world-demo=ashen_ford:stone-and-mire '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=30
```

Use `python tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/special-shots`, then the special-scene/gallery commands in the source README.

Twenty looks are refreshed across nine batches; [nine older combat looks remain](../production/refresh-status.json). The historical baseline remains unchanged. No PR has been opened.
