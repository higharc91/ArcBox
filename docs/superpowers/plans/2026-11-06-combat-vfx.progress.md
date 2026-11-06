> Status: owner approved step 0 in chat; preparing its PR and the Wizard pilot. CI remains manually disabled. No later class is approved.

# Combat VFX #247 progress

The owner authorized work on #247 and optional Scenario concepts. Prefer procedural art for this shared camera/tooling stage. No generation or new sounds have been purchased. The supplied branch started at `6a250288`; it was fast-forwarded to current main `9a7ffb54` before implementation (camp talks and the asset-build fixes). Use `9a7ffb54` as the frozen before baseline for step 0.

## First increment: pure timeline and content contract

- Optional `presentation.cutIn` accepts `none`, `short`, `full`; schema and C# loader validate it. Omitted fields stay omitted from serialized presentation data.
- `BattleCutIn` resolves content tiers and viewer Full/Short/Off preferences, with automatic Short at five/six players unless explicitly overridden.
- The pure linked-chain timeline keeps integer 2×/3× held zoom, adds one push and settle to the whole chain, maps cinematic time to original script time, and exposes completion after settle. Short adds 0.6 s; Full adds 0.9 s; Off adds zero. These are timeline budgets, not yet a measured real-round result.
- All shipped abilities resolve a tier. Existing `signature.*` keys include Fireball and Burning Hands; under the issue's defaults these resolve Full. Magic Missile supplies the ordinary levelled-spell test.
- The foundation is now connected to real BeatPlayer playback, standalone boards and battles in the world. Rules, AI and balance data remain unchanged. Runtime integration is described below.

## Runtime integration and spell lighting

- Real ability markers drive one cut-in per linked chain. Cast frames advance during the close-up while projectiles, effects and results wait for release. Final shown waits for settle. A pending manual reaction acknowledges the partial parent beat while retaining its frame; the final reaction settles once. A real-engine Counterspell test covers this boundary without changing the game or AI.
- Camera framing composes over BoardFit, including world terrain and props together with figures; held positions snap to screen pixels. The close-up holds at 2× for wind-up and pulls back to the target/area for release. Exact area tiles come from Areas.Tiles.
- TV Battle settings (F6) persists Auto/Full/Short/Off and Full/Reduced effects. Auto uses Short for five/six heroes. Reduced omits shake, hit-stop and flashes, shortens effects, and retains softer spell light.
- Heavy-hit feedback samples a 2–4 frame hold inside the existing impact window, adds a local silhouette flash and scaled shake, and adds no ordinary attack time. Existing crit flourish and miss sound/word remain shared.
- Luminous projectiles, bursts, beams/cones, electrified surfaces, persistent spell fire, holy zones and moonbeams illuminate surfaces and figures. Light follows the actual effect and clears with it. Physical hit sparks stay faint so they do not overwhelm fire. No glow textures or generated art were added.
- Dimming uses the existing board draw pass and keeps focused figures/marks readable. Diagnostic software-render measurements exposed an expensive full-screen dim overlay; it was removed. Final paired measurements are still being captured.
- The real-engine, server-free step0 reel exercises Attack, Fire Bolt, Magic Missile and Fireball through the normal TV scene. Class and class:abilityId modes inventory every feature choice/subclass and refuse incomplete trigger fixtures. The Wizard pilot has 49 real samples covering every ability and visible proc plus its weapon attack; 50 inventory rows include two static modifiers. The capture tool renders a frozen main worktree with only the neutral showcase harness backported, then Full/Short/Off/Reduced branch variants, WebM/posters, contact sheet and HTML. Performance runs disable every screenshot readback.
- A deterministic six-hero level-1 river_crossing fight, default loadouts, companion AI and seeds 1–10 gives 30 completed rounds: maximum actual added time at 60 fps is 3.02 s with automatic Short, 3.32 s with explicitly Full, zero with Off (ideal clocks 3.0/3.3). These are regression bounds for that fixture; the owner has not selected a general round cap.

## Review package and next boundary

The gallery is `docs/superpowers/reports/combat-vfx/step0/index.html`, with four abilities × Full/Short/Off/Reduced comparisons, full-resolution posters, native close-up frames, a lighting detail, timings, three-run frame measurements, texture snapshots and actual round timings. The before baseline has only six neutral harness paths changed and no renderer/effect/camera implementation. Both baseline and branch native builds have zero warnings/errors.

The native TV renderer completed all 49 Wizard fixture beats without rendering errors, including its three subclasses and real Shield/Counterspell responses. This is existing-content coverage for the reusable tool; no Wizard art pass has started. Capture tails now include the actual last lingering cue. Future classes must add their specialized proc/reaction fixtures before their class capture can succeed; the tool reports every gap instead of silently skipping it.

A final area-focus check caught a zero-sized opening extent that dimmed the caster; the focus now retains its minimum figure-sized window. Its regression test passes and refreshed camera-on previews show the caster correctly lit. Reduced effects also applies immediately when changed on the TV.

Recorded texture snapshots add about 0.30 MiB at the last sample (including UI/font caches). No game-art texture or audio file was added. Shipped content-tree growth is 86 bytes in the optional cut-in schema. No paid generation was used.

Remaining boundary:

1. Owner reviews the verified gallery/issue preview. No PR until the owner sees it.
2. Owner reviews step 0 and selects the round budget (3.6 s proposed; no universal cap selected/enforced), plus real-TV/hardware/world-encounter visual checks. Container performance is a relative proxy, not a hardware 16 ms claim.
3. Wait for explicit step 0 OK before any Wizard art. Open the step 0 PR after review; do not mark the issue checkbox before approval. Class branches start from latest main after the shared work is merged.

No step or class checkbox is complete yet. CI remains manually disabled. No class art work or paid generation has started.

## Verification log

- Focused: 13 cut-in timeline/serialization tests and 45 content-validation tests pass.
- Initial full gate: zero build warnings/errors; 549 phone tests and production build plus Node tools pass. Presentation tests exposed inherited camp-content count drift (921→945 total lines, 8→32 camp lines); strict snapshots were updated while preserving every portrait, authored-mood, clip and caption assertion. The camp voice-track snapshot likewise expands from 8 to 32 lines. All tool suites passed, with docs-drift additionally requiring this new ledger’s first-line status. The content-count corrections and ledger formatting are included in the final full-gate rerun.

- Final foundation gate: `bash tools/check-all.sh --full` passes all five groups. Build has 0 warnings and 0 errors; 7,204 .NET tests pass with 0 failures/skips, 549 phone tests and production build pass, 24 Node tests pass, and Python asset/audio/lore suites plus lore/docs checks pass. `git diff 9a7ffb54 -- web/play/test/fixtures` is empty. This verifies the foundation increment, not completion of step 0; real camera integration, accessibility settings, showcase/capture tooling and owner preview are still pending.

- Final runtime/tooling gate: `bash tools/check-all.sh --full` passes with 7,224 .NET tests (zero failures/skips), zero build warnings/errors, 549 phone tests and production build, 24 Node tests, and all Python/lore/docs checks. The native pilot completed 49 beats without rendering errors; both before/after native builds have zero warnings/errors. Phone fixtures remain byte-identical. Final three-run comparisons stay within 10% (largest 8.5%); texture snapshot growth is 0.30 MiB, and shipped-content growth is 86 schema bytes. Review assets and detailed measurements are in the step0 report directory.

## Owner approval and Wizard handoff

The owner replied “Yeah, those look good. Finish the rest” after the shared preview and approval question. Step 0 is approved and its issue checkbox is ticked; use the proposed 3.6 s Chapter I round budget. Existing measured assertions remain tighter (3.1 s automatic Short / 3.4 s Full) and are not loosened. Latest main `c4e121eb` was integrated cleanly before the step 0 PR; rerun the full gate on the integration. The frozen step 0 image baseline remains `9a7ffb54`.

The next class is Wizard. Its branch is cut from latest main with the approved shared work included as a dependency while the step 0 PR awaits its owner merge. Keep the Wizard diff and preview separate, and do not start Sorcerer until the Wizard preview receives its required explicit OK.
