> Status: step 0 in progress on `combat-vfx`. No class work, preview approval or PR yet. CI remains manually disabled.

# Combat VFX #247 progress

The owner authorized work on #247 and optional Scenario concepts. Prefer procedural art for this shared camera/tooling stage. No generation or new sounds have been purchased. The supplied branch started at `6a250288`; it was fast-forwarded to current main `9a7ffb54` before implementation (camp talks and the asset-build fixes). Use `9a7ffb54` as the frozen before baseline for step 0.

## First increment: pure timeline and content contract

- Optional `presentation.cutIn` accepts `none`, `short`, `full`; schema and C# loader validate it. Omitted fields stay omitted from serialized presentation data.
- `BattleCutIn` resolves content tiers and viewer Full/Short/Off preferences, with automatic Short at five/six players unless explicitly overridden.
- The pure linked-chain timeline keeps integer 2×/3× held zoom, adds one push and settle to the whole chain, maps cinematic time to original script time, and exposes completion after settle. Short adds 0.6 s; Full adds 0.9 s; Off adds zero. These are timeline budgets, not yet a measured real-round result.
- All shipped abilities resolve a tier. Existing `signature.*` keys include Fireball and Burning Hands; under the issue's defaults these resolve Full. Magic Missile supplies the ordinary levelled-spell test.
- This increment is not connected to BeatPlayer or BoardCanvas yet. Existing runtime visuals and gameplay remain unchanged. Tests exercise the pure clock, metadata compatibility, tier defaults, linked reactions and content rejection.

## Remaining step 0 work, before approval

1. Connect the timeline to real BeatPlayer playback and BoardCanvas framing, preserving the existing shake and delaying shown until settle. Snap held positions to screen pixels and fit affected areas.
2. Add TV Effects Full/Reduced and Battle cinematics Full/Short/Off settings; reuse a single ability banner with #164.
3. Implement impact feedback and exact rules-derived area telegraphs using the shared element motifs. The palette/style contract is now documented in `tools/tactics-assets/README.md`; existing assets have not been converted.
4. Build `--fx-demo=<class>[:<abilityId>]` with deterministic, real engine events and the ordinary TV playback path; include subclasses, appropriate targets and visible features without silently skipping unsupported cases.
5. Add the Xvfb/Screenshotter 1080p fixed-fps before/after capture tool, contact sheet and HTML gallery using the frozen main worktree. Show 3–4 abilities across tiers plus Off/Short and close-up art checks.
6. Measure frame performance, texture/pack growth and actual added seconds for a six-player Chapter I round. Full local check-all must remain green with zero warnings; phone fixtures must be byte-identical.
7. Push preview artifacts, post one comment on #247 and wait for explicit step-0 approval. Do not open the step-0 PR before the owner has seen the preview. Wizard starts only after step-0 approval.

No step or class checkbox is complete yet. No owner decision is needed until a concrete step-0 preview is ready.

## Verification log

- Focused: 13 cut-in timeline/serialization tests and 45 content-validation tests pass.
- Initial full gate: zero build warnings/errors; 549 phone tests and production build plus Node tools pass. Presentation tests exposed inherited camp-content count drift (921→945 total lines, 8→32 camp lines); strict snapshots were updated while preserving every portrait, authored-mood, clip and caption assertion. The camp voice-track snapshot likewise expands from 8 to 32 lines. All tool suites passed, with docs-drift additionally requiring this new ledger’s first-line status. The content-count corrections and ledger formatting are included in the final full-gate rerun.

- Final foundation gate: `bash tools/check-all.sh --full` passes all five groups. Build has 0 warnings and 0 errors; 7,204 .NET tests pass with 0 failures/skips, 549 phone tests and production build pass, 24 Node tests pass, and Python asset/audio/lore suites plus lore/docs checks pass. `git diff 9a7ffb54 -- web/play/test/fixtures` is empty. This verifies the foundation increment, not completion of step 0; real camera integration, accessibility settings, showcase/capture tooling and owner preview are still pending.
