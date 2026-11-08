> Status: Druid native review and full local checks complete; awaiting owner approval before its PR or Paladin.

# Druid — #247

The gallery covers 53 real-engine samples: all 34 abilities, the Grovekeeper sapling proc, five embedded zone hooks, ordinary Attack, and each of four beast forms moving, attacking and reverting after real damage consumes its form HP. The 43-row inventory also lists the three static features.

All 40 ability/proc identities have separate native effects. Four form hit effects, a real-victim Chain Tempest impact and Produce Flame flight bring the export to 46 sheets. Five Druid gestures append 25 frames on all eleven race atlases (214 per facing, nine rows), preserving original indices. Existing sounds and cinematic tiers are retained. Inherited creature coats retain their species voice/weapon even after canonical forms receive separate sprite keys; existing voice/weapon assertions remain and all four form sounds are checked. Fire, weather, moonlight and restoration emit matching light; plants, bark and fog remain unlit. No paid generation or new sounds were used.

## Shapeshifts and sprite consistency

The owner specifically requested time on shapeshifts and consistency with the other sprites. Four Druid-only atlases use the approved eight-direction creature anatomy and motion. The wolf keeps its natural ash coat and living leaf ties; the larger dire wolf keeps its native dark coat and pale chest/neck shading. Bear and elder bear have distinct sizes, with restrained living-bark shoulder plates on the elder form. Each form retains the original clustered pixels, actual rear/side views and scene shading. Bodies do not emit light.

Imported idle/walk frames sometimes drift several pixels above their foot anchor. The new form variants normalize those grounded frames, while preserving authored airborne attack motion. All five attack frames play at 100/7 fps, with frame two aligned to the 0.14 s bite/swipe impact. Multiple actual attack rolls, knockdown saves/procs and damage remain intact and fit the weapon budget. Multiple numbers stay separate. Every direction, action, ground contact and a second native export is checked.

Six-column packing removes unused atlas cells without changing the actual frame pixels or indices. Only four monster sprite strings select these Druid-only atlases, registered as variants of the existing beast species. No creature statistic, attack, HP, movement, tag, AI or transformation/reversion rule changes. All approved source creature atlases and manifest entries remain untouched.

`forms/index.html` is a separate source-atlas inspector, with the player at the same game magnification, a style grid and eight-direction walk/attack GIFs. The main gallery is captured from the actual game. `form-close-ups.png` crops the settled before/after forms from those native captures at 2× nearest-neighbour magnification, retaining the ally for scale. It includes each transformation, form movement, all real attacks, and actual damage-triggered reversion.

## Motion correction after owner review

The owner reported that the planted animal textures looked strange in motion, especially the dire wolf. The previous decoration used the whole sprite bounds, so moving legs/tails slid its position. Its clipped radial ornament and a luminance-selected fur window also reshaped material between poses. The revised four forms retain original native fur shading, use smaller leaf ties/shoulder plates, and follow explicit neck/shoulder keys for all eighteen authored poses in each of eight facings. Motifs are no longer clipped against a changing silhouette.

`forms/native-motion/index.html` compares all four forms walking and attacking in the actual game, extracting the previous/revised Druid panels from the native galleries. `forms/motion/index.html` plays the previous review (`97328797`) and the correction in synchronization, with controls for all four forms, eight facings, six actions, half speed, pause and frame stepping. This is source-atlas inspection; the main gallery contains the actual game. Browser validation checks 768 synchronized frame steps, live playback and the player ground anchor with no page errors. A regression first fails on the old generator and then passes: changing an extremity outside the neck must not slide or reshape its decoration. Existing deterministic, compass, ground-anchor and distinct-form tests remain unchanged and pass. `motion-correction.json` records provenance and the 18 recaptured scene IDs. No runtime code, rules, layout, timing or original creature art changes in this correction.

## Native review

Open `index.html` locally. Before is on the left, after on the right. The full cast, travel, impacts, numbers, conditions, lingering effects and camera settle are preserved. Level labels are character unlock levels. Dummy audio is required for capture; gameplay retains the existing sounds.

Both versions use the ordinary TV scene and BeatPlayer under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps. Frozen before is `a4208a682d52cb7956dfd35d391cff49bbe13c38`: latest main `c4e121eb` plus approved shared, Wizard, Sorcerer, Warlock and Cleric dependencies. Only the six neutral entry/fixture/capture files are backported. Before art, renderer, timing and lighting stay frozen. Approved PRs #251–#255 await owner merges.

The revised gallery saves scenes independently through the ordinary engine path. For this motion correction, 18 affected scenes were freshly captured; 35 unaffected scenes are retained from the previous review. The baseline uses the full reel. Both use the same deterministic fixtures, positions, simulation rate and render settings. All three fresh performance runs per version use the full reel with screenshots disabled.

Healing fixtures have wounded allies; restoration has real removable conditions. Dispel Magic ends an actual Entangle zone. Conjure Animals produces both actual wolves, and Grovekeeper's sapling comes from its battle-start hook. Spike Growth and Moonbeam use actual zone-entry movement; Moonbeam, Sleet Storm and Sanctuary Grove use real turn-start hooks. The real engine supplies every event. Form follow-through fixtures use legal movement, the beast's actual one/two attacks and a real enemy hit that consumes temporary form HP. No event or cast is fabricated.

## Verification and budgets

`bash tools/check-all.sh --full` passes: 7,265 .NET tests (zero skipped), 549 phone tests and production build, 24 Node tool tests, 47 asset tests, 9 audio tests, 23 lore tests, and lore/documentation checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened.

Three fresh independent native runs per version give a class median of **20.10 → 20.25 ms (+0.76%)**, within the 10% class budget. Each sample is the median of three runs; the class result is the median of those 53 samples. Screenshots, tests, builds, browser checks and encoding are separate from benchmarks. The largest individual increase is **druid.spike_growth: 22.43 → 24.31 ms (+8.40%)**. All individual values are published in `performance-summary.json`.

Whole-reel texture peak is **193.42 → 250.44 MiB (+57.02 MiB)**. The reel retains all loaded sheets; ordinary encounters load effects they use. Texture dimensions, frame count, scale, anchors and playback timing are unchanged by this motion correction. The shipped pack grows by **2.08 MiB** against the frozen pre-Druid baseline. Native effects and six-column form atlases retain their existing lossless packing.

Thirty completed six-hero Druid Chapter I rounds add at most **3.300 s Full / 3.000 s Auto Short / 0 Off**, below the approved 3.6 s cap and within the original stricter assertions. The other three parties retain their exact stricter assertions. Full-reel shown time is **58.200 → 56.000 s (-2.200 s)**. `timings.json` and `native-budget-audit.json` publish the actual/base timing and all 53 base budgets.

Native captures and six fresh benchmarks complete without renderer errors. The existing Xvfb VSync and compatibility AA startup warnings occur in both versions. The existing eight-instance ObjectDB shutdown warning appears in 2/3 baseline and 3/3 revised benchmark runs, separately from zero-warning builds.

The expanded 248-test focused presentation, art, sound and round suite passes. All 53 real scripts, effects, original cinematic tiers and presentation keys are checked. Cantrips and form weapon attacks stay within 0.8 s, paid features within 1.5 s and signatures within 2.5 s, excluding cinematic extra time. Four-tile form movement also fits 0.8 s. Original stricter round assertions remain, extended with a Druid party.

Effects and the four form atlases reproduce byte-for-byte. Packed effects preserve RGBA pixels, anchors, beam reach and projectile centres. A second human Druid atlas bake, front/back gestures, every race's grips and atlas bounds are checked. `verification.json` proves all 34 ability definitions and all passive data are unchanged after removing presentation objects, and all four monster definitions are unchanged after removing their sprite strings. All original creature metadata/art, phone files/fixtures, replays/goldens, audio clips, other class atlases, rules/AI implementation and workflow files are unchanged. Seven golden tests pass without update mode or any refresh.

Container medians are relative software-render measurements. Real-hardware 16 ms frames and physical readability from a TV at 3 m remain hardware validation. CI stays manually disabled.

## Approval boundary

Per [#247](https://github.com/higharc91/ArcBox/issues/247), the Druid PR and Paladin wait for explicit Druid approval. No later class has started. The owner merges approved PRs.
