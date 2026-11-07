> Status: Warlock preview complete and full local checks pass; awaiting owner approval before its PR or Cleric.

# Warlock — #247

The gallery covers 44 real-engine samples: all 33 class/subclass abilities, seven visible class procs, Hunger of Hadar's real turn-start pulse, ordinary Attack, and Eldritch Blast at levels 5 and 7 with its two and three real beams. The inventory includes the embedded zone proc and the static class features.

Warlock uses barbed force, binding curses, spectral blades, infernal fire, tentacles and broken void geometry. Every ability and visible proc has a separate native identity: 41 effect identities plus one flight sheet. Four five-frame casting gestures and one held release frame append 21 frames for all eleven Warlock races. Original animation indices remain stable. Other class atlases stay unchanged, and spell learners retain fallback poses.

Luminous force, curses, frost, lightning and infernal fire use their shared material light. Darkness, Hunger, shadow veils and Shadow of Moil do not emit light. Hunger's cold/acid turn pulse receives its own brief local feedback. Existing audio is reused; no paid generation or new sounds were used.

## Native review

Open `index.html` locally. Before is on the left, after on the right. Each card contains the complete native cast/reaction/proc, travel, impact, numbers, conditions, lingering effects and camera settle. Level labels give the character's unlock level. Movies use the required Dummy audio device; normal gameplay still plays the existing sounds.

Both versions use the ordinary TV scene and BeatPlayer under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps. The frozen before is `4eb38ab6a3d287849553c8c0012cb5e39483d76e`: main `c4e121eb` plus the approved shared, Wizard and Sorcerer dependencies. Only the six neutral entry/fixture/capture files are backported. The before art, renderer, timing and lighting stay frozen. PRs #251, #252 and #253 await the owner merge; Warlock is stacked on those approved dependencies.

Real fixtures establish Hex before Hex-dependent effects, advance the round to trigger pact recovery, summon the imp at battle start, and trigger Dark One's Blessing on a wounded enemy while another enemy remains alive. Arms of Hadar receives nearby enemies within its actual self-centred burst, and Poison Spray receives a legal short-range target. Defensive reactions respond to a real single-attack level-3 fighter; Counterspell responds to the existing level-5 Wizard. No engine event is invented.

Existing rule behaviour is visible in both versions: Vampiric Touch and Cursed Slash emit their healing on the targeted enemy. These previews preserve those exact engine events. Any change to their healing target belongs to a rules follow-up.

## Verification and budgets

`bash tools/check-all.sh --full` passes: 7,255 .NET tests (zero skipped), 549 phone tests and production build, 24 Node tool tests, 37 asset tests, 9 audio tests, 23 lore tests, and lore/documentation checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened.

Three independent native runs per version give a class median of **19.79 → 20.11 ms (+1.63%)**, within the 10% class median budget. Each of the 44 sample values is the median of three runs; the class value is the median of those 44 values. Screenshots, tests, builds and encoding run separately from benchmarks. Every individual value is published in `performance-summary.json`. The largest individual increase is **warlock.eldritch_blast.level7: 19.71 → 21.48 ms (+8.96%)**. The class median does not claim every scene stays within 10%.

Whole-reel texture peak is **355.39 → 375.39 MiB (+20.00 MiB)**. The full reel retains all loaded sheets; ordinary encounters load only effects they use. Each Warlock race atlas used adds 4.6875 MiB. Lossless effect packing saves 14.57 MiB decoded storage, retaining pixels, anchors, projectile centres and beam reach. The shipped pack grows by **0.56 MiB** (`pack-growth.json`).

Thirty real six-hero Chapter I rounds containing Warlock remain below the approved 3.6 s cap: max **3.317 s Full / 3.017 s Auto Short / 0 Off**. Existing tighter assertions stay unchanged. Full-reel shown time is **49.400 → 44.217 s (-5.183 s)**. `timings.json` contains actual/base per-sample timings. Eight-frame strips last 0.4 s and twelve-frame strips 0.6 s; real scripts also pass the tier limits, excluding cinematic extra time.

Native capture and all six benchmarks complete without rendering errors. The Xvfb VSync and compatibility AA startup warnings occur in both versions. An intermittent eight-instance ObjectDB shutdown warning also occurs in two of three baseline runs and two of three revised runs; it is not new to the revised content.

The focused 99-test presentation/content/round suite passes. All 44 beats resolve; the actual script and effect lengths fit their 0.8/1.5/2.5 s budgets. Eldritch Blast charges once and then releases the engine's subsequent beams rapidly with a held-palm pose, separately stacked damage numbers and actual repelling motion. Its full level-7 volley stays below 0.8 s without changing any attack, damage or push. Battle-start/round and zone procs use the class-feature 1.5 s budget.

Every effect sheet reproduces byte-for-byte from its native generator. Lossless packing retains the exact RGBA pixels, anchors, projectile centre and beam-light reach. A second atlas bake, front/back poses, all races' grip attachments and atlas bounds are checked. Existing spells' original sound IDs are retained. `verification.json` confirms all 33 gameplay definitions and all passive definitions are unchanged after removing presentation objects. Phone files, audio clips, other tactical atlases, rules/AI implementation and workflow files remain unchanged.

Container medians are relative software-render measurements. Real-hardware 16 ms frames and physical readability from a TV at 3 m remain hardware validation. CI stays manually disabled.

## Approval boundary

Per [#247](https://github.com/higharc91/ArcBox/issues/247), the Warlock PR and Cleric wait for explicit Warlock approval. No later class has started. The owner merges the PRs.
