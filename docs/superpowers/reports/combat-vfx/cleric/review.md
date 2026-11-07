> Status: Cleric preview complete and full local checks pass; awaiting owner approval before its PR or Druid.

# Cleric — #247

The native gallery covers 44 real-engine samples: all 39 abilities across the base class and Life/War/Light domains, Divine Strike, Burning Luminescence, the embedded Death Ward rescue, ordinary Attack and wounded-target Toll the Dead. The inventory includes all 43 feature rows, including the static Crusader's Zeal modifier.

Every ability and visible proc has its own native identity: 42 effect identities plus Guiding Bolt flight. Four five-frame casting gestures append 20 frames for all eleven Cleric races. Original indices remain stable. Other class atlases remain unchanged, and spell learners retain the fallback poses. Restoration uses nature green with holy cores and gold accents; radiant judgement, necrotic wounds, spectral weapons and the three domain motifs follow the shared material language. Matching light follows luminous spells and clears with them, including Flame Strike's fire and radiant streaks. Existing sounds are reused; no paid generation or new audio was used.

## Native review

Open `index.html` locally. Before is on the left, after on the right. Each card contains the complete native cast/reaction/proc, travel, impact, numbers, conditions, lingering effects and camera settle. Level labels give the character's unlock level. Movies use the required Dummy audio device; gameplay retains the existing sounds.

Both versions use the ordinary TV scene and BeatPlayer under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps. Frozen before is `0e96d34360e71f22497286098ca1fe77cad3af8e`: latest main `c4e121eb` plus the approved shared, Wizard, Sorcerer and Warlock dependencies. Only the six neutral entry/fixture/capture files are backported. Before art, renderer, timing and lighting stay frozen. PRs #251–#254 await the owner merge; Cleric is stacked on those approved dependencies.

Healing fixtures use injured allies; Preserve Life starts below half health. Word of Renewal and Mass Revival have actually downed allies. Restoration starts with removable conditions; Dispel Magic targets an ally maintaining an actual Bless cast and carrying a removable magical charm. Turning and Searing use skeletons. Death Ward protects a one-HP ally from an actual lethal enemy attack; its proc returns that ally to one HP and consumes the ward. Warding Flare reacts to an actual level-3 fighter's single attack. Toll the Dead also has a wounded enemy variant. No engine event is fabricated.

Spiritual Weapon remains the rules' one spectral weapon attack. Spirit Guardians remains its immediate burst, with no persistent zone. Banishment depicts the actual condition without inventing a despawn. Presentation does not change these rules.

## Verification and budgets

`bash tools/check-all.sh --full` passes: 7,259 .NET tests (zero skipped), 549 phone tests and production build, 24 Node tool tests, 40 asset tests, 9 audio tests, 23 lore tests, and lore/documentation checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened.

Three independent native runs per version give a class median of **20.54 → 20.45 ms (-0.39%)**, within the 10% class median budget. Each of the 44 sample values is the median of three runs; the class value is the median of those 44 values. Screenshots, tests, builds and encoding run separately from benchmarks. Every individual value is published in `performance-summary.json`. The largest individual increase is **cleric.flame_strike: 20.68 → 22.70 ms (+9.75%)**. The class median does not claim every scene stays within 10%.

Whole-reel texture peak is **231.32 → 252.03 MiB (+20.71 MiB)**. The full reel retains all loaded sheets; ordinary encounters load only effects they use. Each Cleric race atlas used adds 4.6875 MiB. Lossless effect packing saves 15.58 MiB decoded storage while retaining pixels, anchors and the projectile centre. The shipped pack grows by **0.58 MiB** (`pack-growth.json`).

The existing six-hero Chapter I parties all contain Cleric and retain their original formation and strict assertions. Across 90 completed rounds with Wizard, Sorcerer and Warlock party variants, the maximum actual added cinematic time is **3.317 s Full / 3.017 s Auto Short / 0 Off**, below the approved 3.6 s cap. Full-reel shown time is **52.267 → 52.200 s (-0.067 s)**. `timings.json` contains actual/base per-sample timings. Eight-frame strips last 0.4 s and twelve-frame strips 0.6 s; real scripts also pass the tier limits, excluding cinematic extra time.

Native captures and all six benchmarks complete without renderer errors. The Xvfb VSync and compatibility AA startup warnings occur in both versions. The existing eight-instance ObjectDB shutdown warning appears in 3/3 baseline and 3/3 revised benchmark runs; raw-capture presence is before=True, after=True. It is reported separately from build warnings.

The focused 76-test presentation/atlas/round suite passes. `native-budget-audit.json` also checks all 44 actual base timings, including Divine Strike against the stricter 0.8 s weapon/proc limit. Every ability and visible proc resolves its keys and original cinematic tier. Actual scripts and effect durations fit their 0.8/1.5/2.5 s budgets, excluding cinematic extra time. Word of Radiance uses a brief 0.24 s prayer so its actual area saves stay within 0.8 s. Split fire/radiant hits and visible domain procs use distinct number rows while reserving the critical label’s row. A regression reproduces the overlap and verifies Flame Strike, War Priest, Divine Strike and Burning Luminescence. The existing six-hero Chapter I parties all contain Cleric and retain their original formation, seeds and tighter timing assertions.

Every effect reproduces byte-for-byte from the native generator. Lossless packing preserves RGBA pixels, anchors and the projectile centre. A second atlas bake, front/back poses, every race's casting grips and atlas bounds are checked. Original sound IDs are retained. `verification.json` confirms that all 39 gameplay definitions and all passive definitions are unchanged after removing presentation objects. Phone files/fixtures, audio clips, other tactical atlases, rules/AI implementation and workflow files are untouched.

Only presentation objects change in `river-crossing.events.json` (2) and `warlord-bridge.events.json` (14). Event counts/order and every gameplay value remain identical; replay commands and phone fixtures are unchanged. These are refreshed through the normal golden tool and verified without update mode.

Container medians are relative software-render measurements. Real-hardware 16 ms frames and physical readability from a TV at 3 m remain hardware validation. CI stays manually disabled.

## Approval boundary

Per [#247](https://github.com/higharc91/ArcBox/issues/247), the Cleric PR and Druid wait for explicit Cleric approval. No later class has started. The owner merges the PRs.
