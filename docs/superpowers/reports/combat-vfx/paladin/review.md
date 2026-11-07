> Status: Owner-approved; PR #259 is open against combat-vfx-druid for the owner merge.

# Paladin — #247

The 37 real-engine samples cover all 24 abilities, seven visible class procs, the Consecrated Ground turn-start hook, ordinary Attack, three extra undead smite cases and an actual attack with Elemental Weapon active. The inventory lists the four static features, including all three fighting styles.

Thirty-three separate native sheets give each ability/proc its own geometry, plus the enchanted weapon hit. Gold and ivory smites, azure Guardian shields, Crusader consecration and ember Avenger marks use the shared material palettes and matching light. Five complete five-frame gestures append 25 frames on all eleven Paladin race atlases: 214 frames per facing, nine rows, with original indices preserved. Existing sounds and cinematic tiers are retained. No paid generation or new audio is used.

## Native review

Open `index.html` locally: before on the left, after on the right. The ordinary TV scene and BeatPlayer run under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps, dummy audio. Frozen before is `d6c216bfbe7db4e7f043ac3cebae59c6700ae92c`: latest main `c4e121eb` plus the approved dependency stack. Only six neutral entry/fixture/capture files are backported. Before art, renderer, timing and lighting stay frozen. Approved PRs #251–#256 await owner merges.

The baseline uses the full reel; after scenes are saved independently for durable progress. Both use identical deterministic fixtures. Smites and Avenger strikes prepare real conditions using actual casts. The three undead cases use actual skeleton targets and the engine's extra radiant damage. Healing targets are wounded; restoration removes real conditions; Dispel Magic ends an ally's real Bless concentration. Auras and Consecrated Ground use actual opening/end/turn hooks; the Guardian ward is a real reaction offer and response. Every event comes from the engine.

The native videos include full cast, impact, numbers, conditions, effect tails and camera settle. Level labels are character unlock levels. Dummy audio is required for capture; the game retains its existing sounds.

## Verification and budgets

`bash tools/check-all.sh --full` passes with **7,270 .NET tests (zero skipped), 549 phone tests and production build, 24 Node, 50 asset, 9 audio and 23 lore tests**, plus lore/documentation checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened. The full inventory assertion independently derives embedded zone hooks from rule features and retains exact equality.

Three independent screenshot-free native runs per version give a class median of **20.02 → 20.53 ms (+2.51%)**, within the 10% class budget. Each sample value is the median of three runs; the class result is the median of those 37 sample values. Tests, builds, screenshot capture and encoding run separately from benchmarks. The largest individual increase is **paladin.smite_damage.undead: 19.50 → 21.87 ms (+12.14%)**. Every individual value is published in `performance-summary.json`.

Whole-reel texture peak is **228.87 → 248.24 MiB (+19.37 MiB)**. The reel retains all loaded sheets; ordinary encounters load effects they use. Each human-form Paladin race atlas used adds 4.6875 MiB. Lossless effect packing preserves the exact pixels. The shipped pack grows by **0.66 MiB** against the frozen baseline.

Thirty completed six-hero Paladin Chapter I rounds add at most **3.300 s Full / 3.017 s Auto Short / 0 Off**, below the approved 3.6 s cap and within the original stricter assertions. All four prior party variants retain those exact assertions. Full-reel shown time is **39.967 → 35.083 s (-4.883 s)**. `timings.json` and `native-budget-audit.json` publish actual/base timings and all 37 base budgets. Small proc strips last 0.4 s; the other effects last 0.6 s.

Native captures and all six benchmarks complete without renderer errors. Existing Xvfb VSync and compatibility AA startup warnings occur in both versions. The existing eight-instance ObjectDB shutdown warning appears in 2/3 baseline and 3/3 revised benchmark runs, separately from zero-warning builds.

All 69 focused .NET checks and 16 focused asset checks pass: all 37 scripts fit their base budgets; 32 ability/proc keys are distinct and resolve with matching light; the extra weapon enchantment resolves too. Weapon attacks/riders stay within 0.8 s, paid spells within 1.5 s and signatures within 2.5 s, excluding cinematic extra time. Original stricter round assertions remain unchanged, extended with a Paladin party.

All 33 effects reproduce byte-for-byte. Lossless packing preserves RGBA pixels, anchors and reach. The new gestures pass front/back render, all-race grip, atlas bounds and deterministic rebake checks. Paladin swings play all five frames at 20 fps with frame two aligned to the 0.10 s impact. Split weapon/radiant damage keeps separate rows. The sound art-key parser now accepts digits, preserving rank-two and rank-three smites' original clip; all 24 old/new sound mappings are checked.

Presentation-only preservation is published in `verification.json`. All 4,158 original Paladin pose/facing frames retain their exact RGBA pixels, accounting for the extra atlas row; only the new gestures are appended. All 24 ability gameplay definitions and passive data, original creature art, other class atlases, phone files/fixtures, audio clips, replays/goldens, rules/AI implementation and workflow files remain unchanged.

Container medians measure relative software-render performance. Real-hardware 16 ms frames and physical TV readability remain hardware validation. CI stays manually disabled.

## Approval boundary

The owner approved Paladin with ‘ok look good, on to the next class’. PR #259 is open against combat-vfx-druid. Bard is next; the owner merges approved PRs.
