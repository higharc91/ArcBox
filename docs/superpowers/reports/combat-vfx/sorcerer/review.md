> Status: Sorcerer preview complete and full local checks pass; awaiting owner approval before the Sorcerer PR or Warlock.

# Sorcerer — #247

The gallery covers all 45 Sorcerer/class-subclass abilities, Draconic Affinity's visible proc, ordinary Attack, and three additional real random-outcome fixtures: 50 samples in all. The complete inventory also lists static modifiers. All five Wild Surge and Controlled Surge damage branches are exercised through real engine seeds. Surge variant labels identify the first target; Controlled Surge retains the independent outcomes rolled for its other targets.

Sorcerer uses innate releases, finger-flame streams, spiralling force, fractured Chaos motifs, draconic wings/scales and carved Runeborn glyphs. Each ability has a distinct effect identity. Six spells receive flight sheets. Four casting gestures add 20 frames to all eleven Sorcerer race sheets, retaining every original frame index. Other class atlases are unchanged; characters learning Sorcerer spells use their existing fallback poses.

Only the two chaotic surge effects opt into material selection from actual resolved engine damage. Each victim’s real damage result supplies its own impact colour and light. Controlled Surge rolls independently per victim, so a mixed burst retains a neutral central release with smaller typed impacts. Fire and lightning branches emit their matching light. Fire spells, frost/lightning, magical wards and runes reuse the shared spell-light implementation; poison and acid stay unlit. Existing audio clips are reused. No paid generation or new sounds were used.

## Reviewing the gallery

Open `index.html` locally. Every card includes the complete native before/after movie and a full-resolution poster. Before is on the left. Movies retain anticipation, cast, travel, impact, conditions, lingering effects and camera settle. Level labels are character unlock levels. Dummy audio makes exported movies silent; normal gameplay still plays the existing sounds.

Both versions run the ordinary TV scene and BeatPlayer under Xvfb, software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps. The frozen before is `a3c5e0d3ad61e059ff470a9fc0fe7b67c4a45244`: latest main `c4e121eb` plus the approved shared and Wizard dependencies. This isolates Sorcerer changes. Only six neutral showcase/entry/capture files are backported; the before renderer, timing, art and lighting remain frozen. PRs #251 and #252 await the owner merge; Sorcerer is stacked on those dependencies.

## Verification and budgets

`bash tools/check-all.sh --full` passes: 7,251 .NET tests (zero skipped), 549 phone tests and production build, 24 Node tool tests, 34 asset tests, 9 audio tests, 23 lore tests, and lore/documentation checks. The Release build and both frozen/final native Debug builds have zero warnings and errors. No test is skipped or weakened.

Thirty real six-hero Chapter I rounds containing a Sorcerer stay under the approved 3.6 s cap: maximum extra shown time is **3.300 s Full / 3.000 s automatic Short / 0 s Off**. The existing tighter test limits remain unchanged. Scorching Ray releases its real subsequent attacks after one wind-up, bringing its base script within 1.5 s. Cinematic tiers and downing-critical promotion are retained.

Three independent native runs per version give a class median of **20.31 → 20.90 ms (+2.91%)**, within the 10% class median budget. Each of the 50 sample values is the median of three runs; the class value is the median of those 50 values. Screenshots, tests, builds and encoding run separately from the benchmarks. All individual values are published in `performance-summary.json`. The largest individual increase is **sorcerer.lightning_bolt: 20.31 → 22.37 ms (+10.16%)**. The class median does not claim every scene stays within 10%.

Whole-reel texture peak is **357.38 → 379.51 MiB (+22.13 MiB)**. The complete reel retains all loaded spell sheets; normal encounters load only the effects they use. Each Sorcerer race atlas used adds 4.6875 MiB. Lossless effect packing saves 18.63 MiB of decoded art while preserving pixels, anchors, projectile centres and beam-light reach. The shipped pack grows by **0.62 MiB** (`pack-growth.json`).

The complete fifty-sample reel's shown time is **63.133 → 62.567 s (-0.567 s)**. Exact per-sample base/actual timing is in `timings.json`. Cantrip strips last 0.4 s and other strips 0.6 s; real scripts also pass the tier budgets, excluding cinematic extra time.

Both versions report the same Xvfb VSync and compatibility-renderer AA startup warnings. All published native captures, retakes and benchmarks complete without rendering errors. The original five surge captures were replaced with final per-victim-colour footage; unchanged samples were retained.

Every sheet reproduces byte-for-byte from its native generator. Lossless empty-margin packing retains every original RGBA pixel, anchor, projectile centre and aimed light reach. A second Sorcerer atlas bake matches the shipped sheet. Front/back casting frames, hand attachment and bounds are checked. Presentation keys, actual beats, all random damage variants, audio mappings and the 0.8/1.5/2.5 s base-script limits are tested. Paid class features use the levelled-spell budget even when their ability tier is zero; the reaction sample includes its real incoming attack.

`verification.json` confirms that all 45 gameplay definitions are unchanged after removing presentation objects. Phone files, audio clips and non-Sorcerer tactical atlases are unchanged. No rule, number, resource, AI or balance change was made.

Real-hardware 16 ms frames and a physical TV check at 3 m remain hardware validation. Container frame medians are relative software-render measurements. CI remains manually disabled.

## Approval boundary

Per [#247](https://github.com/higharc91/ArcBox/issues/247), the Sorcerer PR and Warlock wait for explicit Sorcerer approval. No later class has started. The owner merges the PRs.
