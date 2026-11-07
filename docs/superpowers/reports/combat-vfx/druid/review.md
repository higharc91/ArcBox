> Status: Druid native review and full local checks complete; awaiting owner approval before its PR or Paladin.

# Druid — #247

The gallery covers 53 real-engine samples: all 34 abilities, the Grovekeeper sapling proc, five embedded zone hooks, ordinary Attack, and each of four beast forms moving, attacking and reverting after real damage consumes its form HP. The 43-row inventory also lists the three static features.

All 40 ability/proc identities have separate native effects. Four form hit effects, a real-victim Chain Tempest impact and Produce Flame flight bring the export to 46 sheets. Five Druid gestures append 25 frames on all eleven race atlases (214 per facing, nine rows), preserving original indices. Existing sounds and cinematic tiers are retained. Inherited creature coats retain their species voice/weapon even after canonical forms receive separate sprite keys; existing voice/weapon assertions remain and all four form sounds are checked. Fire, weather, moonlight and restoration emit matching light; plants, bark and fog remain unlit. No paid generation or new sounds were used.

## Shapeshifts and sprite consistency

The owner specifically requested time on shapeshifts and consistency with the other sprites. Four Druid-only atlases use the approved eight-direction creature anatomy and motion. The wolf keeps its natural ash coat and living leaf ties; the larger dire wolf has a dark coat and pale ruff. Bear and elder bear have distinct sizes and materials, with grizzled fur and living-bark shoulders on the elder form. Each form retains the original clustered pixels, actual rear/side views and scene shading. Bodies do not emit light.

Imported idle/walk frames sometimes drift several pixels above their foot anchor. The new form variants normalize those grounded frames, while preserving authored airborne attack motion. All five attack frames play at 100/7 fps, with frame two aligned to the 0.14 s bite/swipe impact. Multiple actual attack rolls, knockdown saves/procs and damage remain intact and fit the weapon budget. Multiple numbers stay separate. Every direction, action, ground contact and a second native export is checked.

Six-column packing removes unused atlas cells without changing the actual frame pixels or indices. Only four monster sprite strings select these Druid-only atlases, registered as variants of the existing beast species. No creature statistic, attack, HP, movement, tag, AI or transformation/reversion rule changes. All approved source creature atlases and manifest entries remain untouched.

`forms/index.html` is a separate source-atlas inspector, with the player at the same game magnification, a style grid and eight-direction walk/attack GIFs. The main gallery is captured from the actual game. `form-close-ups.png` crops the settled before/after forms from those native captures at 2× nearest-neighbour magnification, retaining the ally for scale. It includes each transformation, form movement, all real attacks, and actual damage-triggered reversion.

## Native review

Open `index.html` locally. Before is on the left, after on the right. The full cast, travel, impacts, numbers, conditions, lingering effects and camera settle are preserved. Level labels are character unlock levels. Dummy audio is required for capture; gameplay retains the existing sounds.

Both versions use the ordinary TV scene and BeatPlayer under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps. Frozen before is `a4208a682d52cb7956dfd35d391cff49bbe13c38`: latest main `c4e121eb` plus approved shared, Wizard, Sorcerer, Warlock and Cleric dependencies. Only the six neutral entry/fixture/capture files are backported. Before art, renderer, timing and lighting stay frozen. Approved PRs #251–#255 await owner merges.

The workspace interrupted a revised full-reel capture. The final revised gallery therefore captures each of the same 53 abilities independently through the ordinary engine path, saving each completed scene. The baseline uses the complete reel. Both use the same deterministic fixtures, positions, simulation rate and render settings; ordinary Attack equivalence is tested. All three benchmark runs per version use the complete reel, with screenshots disabled.

Healing fixtures have wounded allies; restoration has real removable conditions. Dispel Magic ends an actual Entangle zone. Conjure Animals produces both actual wolves, and Grovekeeper's sapling comes from its battle-start hook. Spike Growth and Moonbeam use actual zone-entry movement; Moonbeam, Sleet Storm and Sanctuary Grove use real turn-start hooks. The real engine supplies every event. Form follow-through fixtures use legal movement, the beast's actual one/two attacks and a real enemy hit that consumes temporary form HP. No event or cast is fabricated.

## Verification and budgets

`bash tools/check-all.sh --full` passes: 7,265 .NET tests (zero skipped), 549 phone tests and production build, 24 Node tool tests, 46 asset tests, 9 audio tests, 23 lore tests, and lore/documentation checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened.

Three independent native runs per version give a class median of **19.67 → 19.95 ms (+1.43%)**, within the 10% class median budget. Each of the 53 sample values is the median of three runs; the class value is the median of those 53 values. Screenshots, tests, builds and encoding run separately from benchmarks. Every individual value is published in `performance-summary.json`. The largest individual increase is **druid.fog_cloud: 20.00 → 21.36 ms (+6.81%)**. Every individual sample also stays within 10% in this matrix.

Whole-reel texture peak is **193.42 → 250.44 MiB (+57.02 MiB)**. The full reel retains all loaded sheets; ordinary encounters load only effects they use. Each human-form Druid race atlas used adds 4.6875 MiB. Lossless effect packing saves 22.29 MiB decoded storage; six-column form packing saves 11.80 MiB against the same four form atlases in their original eight-column layout. Actual pixels, anchors, frame indices, beam reach and projectile centre are retained. The shipped pack grows by **2.18 MiB** (`pack-growth.json`).

Thirty completed six-hero Chapter I rounds containing Druid add at most **3.300 s Full / 3.000 s Auto Short / 0 Off**, below the approved 3.6 s cap and within the original stricter assertions. The other three party variants also retain their exact stricter assertions. Full-reel shown time is **58.200 → 56.000 s (-2.200 s)**. `timings.json` contains actual/base per-sample timings; `native-budget-audit.json` checks all 53 base timings. Eight-frame effect strips last 0.4 s and twelve-frame strips 0.6 s.

Native captures and all six benchmarks complete without renderer errors. The Xvfb VSync and compatibility AA startup warnings occur in both versions. The existing eight-instance ObjectDB shutdown warning appears in 2/3 baseline and 2/3 revised runs; raw-capture presence is before=True, after=True. It is reported separately from build warnings.

The expanded 248-test focused presentation, art, sound and round suite passes. All 53 real scripts, effects, original cinematic tiers and presentation keys are checked. Cantrips and form weapon attacks stay within 0.8 s, paid features within 1.5 s and signatures within 2.5 s, excluding cinematic extra time. Four-tile form movement also fits 0.8 s. Original stricter round assertions remain, extended with a Druid party.

Effects and the four form atlases reproduce byte-for-byte. Packed effects preserve RGBA pixels, anchors, beam reach and projectile centres. A second human Druid atlas bake, front/back gestures, every race's grips and atlas bounds are checked. `verification.json` proves all 34 ability definitions and all passive data are unchanged after removing presentation objects, and all four monster definitions are unchanged after removing their sprite strings. All original creature metadata/art, phone files/fixtures, replays/goldens, audio clips, other class atlases, rules/AI implementation and workflow files are unchanged. Seven golden tests pass without update mode or any refresh.

Container medians are relative software-render measurements. Real-hardware 16 ms frames and physical readability from a TV at 3 m remain hardware validation. CI stays manually disabled.

## Approval boundary

Per [#247](https://github.com/higharc91/ArcBox/issues/247), the Druid PR and Paladin wait for explicit Druid approval. No later class has started. The owner merges approved PRs.
