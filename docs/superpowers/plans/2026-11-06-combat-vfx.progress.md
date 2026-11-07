> Status: Wizard approved and PR #252 open; Sorcerer preview complete, awaiting owner OK before its PR or Warlock. Shared PR #251 awaits the owner merge. CI remains manually disabled.

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

Step 0 boundary recorded before approval (historical):

1. Owner reviews the verified gallery/issue preview. No PR until the owner sees it.
2. Owner reviews step 0 and selects the round budget (3.6 s proposed; no universal cap selected/enforced), plus real-TV/hardware/world-encounter visual checks. Container performance is a relative proxy, not a hardware 16 ms claim.
3. Wait for explicit step 0 OK before any Wizard art. Open the step 0 PR after review; do not mark the issue checkbox before approval. Class branches start from latest main after the shared work is merged.

At that earlier stage no checkbox was complete and class art had not started. The current status is recorded at the top and in the owner-approval/Wizard sections below. CI remains manually disabled; no paid generation was used.

## Verification log

- Focused: 13 cut-in timeline/serialization tests and 45 content-validation tests pass.
- Initial full gate: zero build warnings/errors; 549 phone tests and production build plus Node tools pass. Presentation tests exposed inherited camp-content count drift (921→945 total lines, 8→32 camp lines); strict snapshots were updated while preserving every portrait, authored-mood, clip and caption assertion. The camp voice-track snapshot likewise expands from 8 to 32 lines. All tool suites passed, with docs-drift additionally requiring this new ledger’s first-line status. The content-count corrections and ledger formatting are included in the final full-gate rerun.

- Final foundation gate: `bash tools/check-all.sh --full` passes all five groups. Build has 0 warnings and 0 errors; 7,204 .NET tests pass with 0 failures/skips, 549 phone tests and production build pass, 24 Node tests pass, and Python asset/audio/lore suites plus lore/docs checks pass. `git diff 9a7ffb54 -- web/play/test/fixtures` is empty. This verifies the foundation increment, not completion of step 0; real camera integration, accessibility settings, showcase/capture tooling and owner preview are still pending.

- Final runtime/tooling gate: `bash tools/check-all.sh --full` passes with 7,224 .NET tests (zero failures/skips), zero build warnings/errors, 549 phone tests and production build, 24 Node tests, and all Python/lore/docs checks. The native pilot completed 49 beats without rendering errors; both before/after native builds have zero warnings/errors. Phone fixtures remain byte-identical. Final three-run comparisons stay within 10% (largest 8.5%); texture snapshot growth is 0.30 MiB, and shipped-content growth is 86 schema bytes. Review assets and detailed measurements are in the step0 report directory.

## Owner approval and Wizard handoff

The owner replied “Yeah, those look good. Finish the rest” after the shared preview and approval question. Step 0 is approved and its issue checkbox is ticked; use the proposed 3.6 s Chapter I round budget. Existing measured assertions remain tighter (3.1 s automatic Short / 3.4 s Full) and are not loosened. Latest main `c4e121eb` was integrated cleanly before the step 0 PR; rerun the full gate on the integration. The frozen step 0 image baseline remains `9a7ffb54`.

The next class is Wizard. Its branch is cut from latest main with the approved shared work included as a dependency while the step 0 PR awaits its owner merge. Keep the Wizard diff and preview separate, and do not start Sorcerer until the Wizard preview receives its required explicit OK.

## Wizard implementation

Wizard starts on main `c4e121eb` with approved shared dependency `15ecd7a1`; the frozen before includes that dependency so the class comparison isolates its art. Step 0 is in PR #251, awaiting the owner merge. Class-specific work is on `combat-vfx-wizard`.

All 47 abilities receive distinct native spell sheets, seven receive dedicated flight strips, and Empowered Evocation receives a presentation-only proc sigil. Five casting animations add 34 frames to each Wizard race sheet. Other classes retain their atlases and use fallback poses if they learn these spells. Existing signature tiers remain Full; ordinary default tiers remain dynamic, retaining downing-critical promotion. Scorching Ray winds up once then rapidly releases its remaining real attacks to meet the 1.5 s budget. Gameplay definitions, AI, damage and resources are unchanged.

The showcase captures all 49 real samples: 47 abilities, the Evoker proc and ordinary Attack. Comparisons, three-run container performance, texture/pack growth, round timing and the final full gate are being recorded. The snapshot update changes only AbilityUsed.presentation in river-crossing and warlord-bridge; non-presentation replay events are asserted identical. No phone fixture or audio clip changes. The gallery will be posted once verification passes; no Wizard PR before approval, and no Sorcerer work before Wizard approval.

Native visual review additionally caught a pre-existing Counterspell detonation: the incoming Fireball was drawn despite being canceled. Playback now holds an offered spell and resumes its presentation only after a declined or failed counter, or cancels it when the engine emits Countered. No engine event is changed; the resumed ability marker exists only in BeatPlayer's presentation script. Real-engine tests cover success, pass and a failed higher-tier counter. Native Counterspell footage is retaken. Cold, lightning and poison aimed effects now have distinct geometry and motion, with a palette-independent regression check.

The native Counterspell review led to explicit nested-chain coverage as well. Suspended incantations are retained as a stack rather than a single slot. Real engine tests cover both answering and declining a counter to the Wizard's own spell, with no premature release, correct result ordering, continuous framing, and exact final HP/life. The existing three outcomes remain covered. Source is frozen while the full gate reruns; baseline performance runs are retained and after measurements use the final Debug assembly.

## Wizard review handoff

The complete Wizard gallery is `../reports/combat-vfx/wizard/index.html`: 49 native before/after movies and full-resolution posters, all 50 inventory rows (including two static modifiers), a contact sheet, four highlights and a native cast close-up. All links and representative VP9/1920×540 media validate. Every final capture and benchmark log completes without rendering errors; capture uses the real engine/BeatPlayer at 1920×1080, 60 simulation / 30 capture fps and Dummy audio. All 49 spell/proc/ordinary attack samples are covered.

The final full gate passes with 7,247 .NET tests, zero build warnings/errors, 549 phone tests/build, 24 Node tests, 31 asset tests, 9 audio tests, 23 lore tests and lore/docs checks. Native builds also have zero warnings/errors. All effect PNGs reproduce byte-for-byte; cropped export restores every original RGBA pixel exactly while retaining anchors, shot centres and beam-light reach, saving 18.10 MiB decoded effect storage. Other class atlases, audio clips, phone fixtures and gameplay fields stay unchanged. Replay goldens change only 18 AbilityUsed.presentation events.

Three independent software-render runs per version give a class median 20.6423 → 21.3141 ms (+3.25%, within the 10% class median limit). This is not a per-scene guarantee: the largest individual regression is Temporal Stasis, 20.0742 → 24.6191 ms (+22.64%), documented for physical hardware validation. Whole-reel texture peak grows by 39.18 MiB; shipped content grows by 1.09 MiB. Thirty real six-player Chapter I rounds remain under the approved 3.6 s cap: Full max 3.317 / Auto Short max 3.017 / Off 0 extra seconds. Exact metrics and limitations are in review.md and the JSON reports.

Wizard remains unchecked until the owner's explicit approval. Do not open its PR or start Sorcerer until that approval. The owner merges PR #251 and the later Wizard PR. No new sounds, paid generation, rule/balance/AI edits or CI runs were introduced.


## Wizard approval and Sorcerer

The owner replied “Looks good, next class.” Wizard is approved, its checkbox on #247 is ticked, and PR #252 is open against `combat-vfx` to keep its review separate from the shared dependency in PR #251. Both await the owner merge. Latest main remains `c4e121eb`; Sorcerer starts from it with approved Wizard dependency `a3c5e0d3`, also the frozen Sorcerer before baseline.

Sorcerer has 45 abilities and one visible proc. The gallery includes these, ordinary Attack, and three additional real random-outcome fixtures, covering all five Wild Surge/Controlled Surge damage branches. Every ability has a distinct native effect. Four Sorcerer cast gestures append 20 frames per race without changing existing indices. Draconic, chaotic and runic motifs distinguish the subclasses. Actual engine damage selects the chaotic effect's material and light; no result is invented by the showcase or renderer. Existing audio, gameplay and phone data remain unchanged. No later class has started.


## Sorcerer review handoff

The 50-sample native before/after gallery is `../reports/combat-vfx/sorcerer/index.html`. It covers all 45 abilities, Draconic Affinity, ordinary Attack, and the remaining random outcome fixtures. Four casting gestures append 20 frames for every race. Mixed Controlled Surge outcomes keep a neutral central release and use each victim's actual damage type for its smaller impact and light. All five surge captures were retaken after that correction. No engine rule, number, AI, balance, phone file, audio clip or non-Sorcerer atlas changed.

The final full gate passes: 7,251 .NET tests (zero skipped), zero build warnings/errors, 549 phone tests/build, 24 Node tests, 34 asset tests, 9 audio tests, 23 lore tests, lore/docs checks. Frozen/final native Debug builds have zero warnings/errors. Native captures and three-run benchmarks complete without rendering errors. Source/art commit: `c7530bf44ca2463210575c56a51acc7e17a2b1aa`.

Class median is 20.3094 → 20.9010 ms (+2.91%, within 10%). Largest individual increase: sorcerer.lightning_bolt (+10.16%), published transparently. Whole-reel texture growth is 22.13 MiB; shipped-pack growth 0.62 MiB. Thirty six-hero Sorcerer Chapter I rounds add at most 3.300 s Full / 3.000 s Auto Short / 0 s Off, below the approved 3.6 s cap without loosening assertions. Real-hardware 16 ms and physical 3 m TV validation remain hardware checks.

Sorcerer remains unchecked on #247 until explicit owner approval. Do not open its PR or start Warlock before that approval. The owner merges #251, #252 and the later class PRs. CI remains manually disabled.
