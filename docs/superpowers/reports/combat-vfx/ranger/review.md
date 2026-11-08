> Status: Approved by the owner (“Hunter looks good, next class”). PR #268 is open; owner merge pending.

# Ranger — #247

The 29 native comparisons cover all 20 abilities, Hunter's Mark's bonus, High Ground Mastery, the Beastmaster companion and Ferocious Bond, Spike Growth's entry hook, Ensnared's real turn-start damage, ordinary Attack, and the actual summoned hound's movement and attack. The 29-row inventory also records three static modifiers. Thirty-one distinct native sheets include five flight strips.

Precision sights and arrows, storm splinters, living thorns, fern healing, pack markings and a hollow Warden canopy use the shared pixel-art palette. Five complete gestures plus a held rapid-release frame append 26 frames to all eleven Ranger race atlases: 215 frames per facing, nine rows. All 4,158 original frame/facing crops retain their exact pixels. Existing sounds and cinematic tiers stay intact. Steel, roots, bark and stealth remain unlit; storm, force, healing and magical bonds use appropriate shared light. No paid generation or new audio is used.

## Native review

Open `index.html` locally: before left, after right. Real engine fixtures use the ordinary TV scene and BeatPlayer under Xvfb/software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps, Dummy audio. Frozen before `e89d584caa84918e2de93684d298cc4a072da04a` includes latest main and approved dependencies. Only six neutral showcase/capture paths are backported. Baseline art, renderer, timing and lighting stay frozen.

Marks are prepared with actual Hunter's Mark and Sic 'Em casts. High Ground Mastery uses a real height delta. Spike Growth triggers through an actual legal move into its zone. Ensnared's thorn damage is captured after an actual Ensnaring Strike and failed save, when the victim's real turn starts. Healing and restoration use wounded/afflicted allies. The companion is summoned by its real battle-start hook and then moves/attacks on its own actual turn. Ferocious Bond preserves both real attacks; native bow playback uses a full initial draw and a short held release for the follow-up. On-hit effects and separate damage numbers land on the victim. A real marked-Volley regression reproduces and fixes the tile-targeted case: the resolved hit supplies the victim even when the ability itself names only an area tile. Videos preserve the complete beat, tails and camera settle; levels are unlock levels.

## Verification and budgets

`bash tools/check-all.sh --full` passes: **7,317 .NET tests (zero skipped), 565 phone tests and production build, 24 Node, 56 asset, 9 audio and 23 lore tests**, plus lore/docs checks. Release and both native Debug builds have zero warnings/errors. No test is skipped or weakened. Focused checks pass: 71 class/round/atlas .NET checks, 61 audio .NET checks, 24 effect-catalog .NET checks and 16 asset checks.

Three independent screenshot-free native runs per version give a class median **20.78 → 21.98 ms (+5.75%)**, within the 10% class budget. Each sample is the median of three runs; the class result is the median of those 29 sample values. Tests, builds, screenshot capture and encoding complete separately from benchmarks. Largest individual increase: **ranger.companion.move: 19.54 → 21.94 ms (+12.30%)**. All individual values are published in `performance-summary.json`.

Whole-reel texture peak **127.36 → 149.49 MiB (+22.13 MiB)**. The reel retains loaded sheets; normal encounters load only those they use. Each used Ranger race atlas adds 4.6875 MiB. Lossless packing preserves exact effect pixels. Shipped pack growth is **0.58 MiB** against the frozen baseline.

Six six-hero Chapter I party variants include the new Ranger party. Their **180 completed rounds** add at most **3.317 s Full / 3.017 s Auto Short / 0 Off**, below the approved 3.6 s cap and within the unchanged stricter assertions. Full-reel shown time **34.150 → 32.200 s (-1.950 s)**. `timings.json` and `native-budget-audit.json` publish actual/base times and all 29 strict base budgets: ordinary attacks/cantrips ≤0.8 s, paid spells ≤1.5 s, signatures ≤2.5 s. Five small impact strips last 0.4 s; other impact sheets last 0.6 s. The five looping flight strips use eight frames at 24 fps.

Native captures and all six benchmarks complete without renderer errors. Existing Xvfb VSync and compatibility AA startup warnings occur in both versions. The existing eight-instance ObjectDB shutdown warning appears in 2/3 baseline and 2/3 revised benchmark runs, separately from zero-warning builds. All existing sounds resolve identically for the 20 new ability keys. Five native arrow strips retain bow launch/impact sound selection. Rapid follow-up bow release preserves both real attacks and separate damage rows, with rider effects on the actual resolved victim, including tile-targeted Volley. Source/art/runtime commit: `8d193e1ed381618e28d811271ab12aea9d05f8b5`.

Presentation-only preservation is published in `verification.json`. All 20 gameplay definitions/passives, original hound/creature art, other classes, phone/fixtures, audio clips and workflow files remain unchanged relative to the frozen baseline. Existing stricter tests are retained. The existing six-case arrow sound test caught a missing Piercing Shot projectile; the exporter now retains that original arrow alongside the new trail, without changing the test. Warlord Bridge changes only 4 Hunter's Mark AbilityUsed presentation objects; replay commands and every non-presentation event remain identical.

Container performance is a relative proxy. Hardware 16 ms frames and physical TV readability remain hardware checks. CI is confirmed manually disabled before publication. It was found active at the start of this class and restored to the owner’s requested disabled state; this work did not enable or dispatch it.

## Approval boundary

Ranger stays unchecked until owner approval under [#247](https://github.com/higharc91/ArcBox/issues/247). Its PR and Fighter wait for that explicit OK. The owner merges approved dependency PRs.
