> Status: Owner-approved; PR #264 is open against combat-vfx-paladin for the owner merge.

# Bard — #247

All 32 abilities, Font of Inspiration, Silence's real turn-start hook and ordinary Attack receive native comparisons. The 37-row inventory also records three static features. Thirty-four distinct native effects use musical notes and staves, psychic masks, crystalline thunder, healing refrains and college motifs. Five five-frame gestures append 25 frames per facing on eleven race atlases: playing, verse, crescendo, lore and Valor. Original 189 poses retain their indices and pixels. Luminous spells use matching shared emission; masks, Silence and invisibility stay unlit. Existing sounds and cinematic tiers are preserved. No paid generation or new audio is used.

## Native review

Open `index.html` locally: before left, after right. Real engine fixtures run through the ordinary TV scene and BeatPlayer under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps, Dummy audio. Frozen before: `895e6d5ca8ca45c39a4e0a3a018a4dfc7aaac553`, including latest main and approved dependencies. Only six neutral showcase/capture files are backported; baseline art, renderer, timing and lighting stay frozen.

Healing targets start wounded; restorations remove actual conditions. Dispel Magic ends an actual Silence zone. Counterspell answers a real incoming Fireball; Cutting Words answers a real melee attack. Font starts one inspiration short and advances to its real even-round hook. Silence deals authored turn-start damage without rule changes. Battle Magic keeps every weapon and thunder event with separate number rows. Videos preserve cast, impact, results, tails and settle; levels are unlock levels.

## Verification and budgets

`bash tools/check-all.sh --full` passes: **7,279 .NET tests (zero skipped), 562 phone tests and production build, 24 Node, 53 asset, 9 audio and 23 lore tests**, plus lore/docs checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened. Focused checks pass: 69 .NET and 16 asset tests.

Three independent screenshot-free native runs per version give a class median **21.15 → 21.42 ms (+1.28%)**, within the 10% class budget. Each sample is the median of three runs; the class result is the median of those 35 sample values. Tests, builds, screenshot capture and encoding complete separately from benchmarks. Largest individual increase: **valor.combat_inspiration: 20.52 → 22.32 ms (+8.78%)**. All individual values are published in `performance-summary.json`.

Whole-reel texture peak **352.49 → 372.64 MiB (+20.15 MiB)**. The reel retains loaded sheets; normal encounters load only those they use. Each used Bard race atlas adds 4.6875 MiB. Lossless packing preserves exact effect pixels. Shipped pack growth is **0.48 MiB** against the frozen baseline.

All five six-hero Chapter I parties already include a Bard. Their **150 completed rounds** add at most **3.317 s Full / 3.017 s Auto Short / 0 Off**, below the approved 3.6 s cap and within the unchanged stricter assertions. Full-reel shown time **41.617 → 41.550 s (-0.067 s)**. `timings.json` and `native-budget-audit.json` publish actual/base times and all 35 strict base budgets: ordinary attacks/cantrips ≤0.8 s, paid spells ≤1.5 s, signatures ≤2.5 s. Five small strips last 0.4 s; others last 0.6 s.

Native captures and all six benchmarks complete without renderer errors. Existing Xvfb VSync and compatibility AA startup warnings occur in both versions. The existing eight-instance ObjectDB shutdown warning appears in 3/3 baseline and 3/3 revised benchmark runs, separately from zero-warning builds. All existing sounds resolve identically for the 32 new ability keys. Thunderclap uses a shorter wind-up to retain its cantrip budget; Battle Magic preserves weapon sound and separates stacked damage numbers. Source/art/runtime commit: `9b1e30d680dd1ead9615851089258300496c2264`.

Presentation-only preservation is published in `verification.json`. All 4,158 original Bard pose/facing frames retain their exact RGBA pixels. All 32 gameplay definitions and passive data, other classes, creatures, phone/fixtures, goldens, audio and workflow files are unchanged. New effects reproduce deterministically and preserve losslessly packed pixels. Gestures pass front/back, all-race grip, atlas bounds and deterministic rebake checks.

Container performance is a relative proxy; hardware 16 ms frames and physical TV readability remain hardware checks. CI stays manually disabled. Check all was found active during final verification and restored to `disabled_manually` before pushing; this work did not enable or dispatch it. Two active ford-water-261 runs (42387742010 and 42387738864) were canceled to honor the owner’s instruction that CI must not run.

## Approval boundary

The owner approved Bard with “looks great, on to the next class”. Its checkbox is ticked and PR #264 is open against combat-vfx-paladin. Ranger is next. The owner merges approved PRs #251–#256, #259 and #264.
