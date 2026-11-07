> Status: Druid native review complete; awaiting owner OK before its PR or Paladin. Approved dependency PRs #251–#255 await owner merges. CI remains manually disabled.

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


## Sorcerer approval and Warlock

The owner replied “those look good, continue.” Sorcerer is approved, its #247 checkbox is ticked, and PR #253 is open against `combat-vfx-wizard`. Shared PR #251 and Wizard PR #252 remain open for the owner merge. Warlock starts from latest main `c4e121eb` with approved Sorcerer dependency `4eb38ab6`; that dependency is the frozen Warlock before baseline.

Warlock has 33 abilities, seven class/subclass visible procs, and Hunger of Hadar's embedded turn-start proc. Its 44 real samples also include ordinary Attack and the level-5/two-beam and level-7/three-beam Eldritch Blast variants. Real fixtures apply Hex before Hex effects, advance the actual pact-recovery round, summon the imp through battle-start hooks, and trigger the kill blessing on a wounded enemy with another enemy still alive. Self-centred and short-range spells receive legal nearby targets. No rules are changed.

The art adds 41 distinct effect identities and one flight sheet, four five-frame gestures and one held release pose. Original frame indices remain stable; Warlock race atlases use nine rows per facing. Eldritch Blast charges once and releases its real subsequent beams rapidly, with separate damage-number rows and actual repelling motion inside the cantrip budget. Darkness, Hunger, shadow veils and Moil remain unlit; infernal fire, force, cold, curses and lightning use appropriate shared light. The focused 99-test presentation/content/round suite passes; the full gate and native review are next. No later class has started.


## Warlock review handoff

The 44-sample native gallery is `../reports/combat-vfx/warlock/index.html`: 33 abilities, seven visible class procs, Hunger's embedded turn proc, an ordinary attack and two higher-level Eldritch Blast variants. All real trigger fixtures, every animation/effect/sound key, all script/effect budgets, and new atlas bounds/attachments pass. The full local gate passes with zero build warnings/errors: 7,255 .NET (zero skipped), 549 phone tests/build, 24 Node, 37 asset, 9 audio and 23 lore tests plus lore/docs checks. Native captures and benchmarks finish without rendering errors. Source/art commit `a11e227daeed40394b1f4d6f74db64eec3d8f852`.

Three-run class median 19.7884 → 20.1112 ms (+1.63%, within 10%). Largest individual increase warlock.eldritch_blast.level7 (+8.96%), reported transparently. Peak texture growth +20.00 MiB; shipped-pack growth 0.56 MiB. Thirty six-player Warlock Chapter I rounds add at most 3.317 s Full / 3.017 s Auto Short / 0 Off, within the approved cap without loosening existing limits. Real-hardware 16 ms and physical TV readability remain hardware checks.

All 33 gameplay definitions and passive definitions are unchanged. Existing Vampiric Touch/Cursed Slash enemy-targeted healing remains visible exactly as emitted by the engine; rules follow-up is separate. Phone, audio clips, other class atlases and workflow files are untouched. Warlock remains unchecked until explicit owner approval; its PR and Cleric wait for that OK. Owner merges the approved dependency PRs. CI remains disabled.


## Warlock approval and Cleric

The owner replied “ok, that looks good. Proceed.” Warlock is approved, its #247 checkbox is ticked, and PR #254 is open against `combat-vfx-sorcerer`. The owner merges the approved dependency PRs #251–#254. Cleric starts from latest main `c4e121eb` with approved Warlock dependency `0e96d343`; that dependency is the frozen Cleric before baseline.

Cleric covers all 39 abilities, Divine Strike, Burning Luminescence, the embedded Death Ward rescue, ordinary Attack and wounded-target Toll the Dead: 44 real-engine samples. Fixtures use wounded/downed allies for healing and revival, skeletons for turning/searing, real conditions for restoration, an ally's actual Bless concentration for Dispel Magic, and an actual lethal attack for Death Ward. No rules or events are invented.

Native art adds 42 identities plus Guiding Bolt flight and four five-frame casting gestures on all eleven Cleric race atlases. Green/gold healing, radiant judgement, necrotic wounds, spectral guardians and the three domain motifs retain shared palettes and matching light. Existing sounds and cinematic tiers are preserved. The 76-test focused presentation/atlas/round suite passes. The existing three six-hero Chapter I fixture parties all include Cleric, retain their original formation and strict timing assertions, and supply the round measurements. Full local checks and native review follow. Cleric's PR and Druid wait for Cleric approval; CI remains disabled.

Native Flame Strike review exposed overlapping split-damage numbers. A regression reproduces the collision; Cleric effects/procs now stack each victim’s damage numbers independently and reserve the critical label row. Flame Strike, War Priest, Divine Strike and Burning Luminescence are recaptured after the fix. Only 16 presentation objects changed across the two golden event files; replay commands and every gameplay event value are unchanged.


## Cleric review handoff

The 44-sample native gallery is `../reports/combat-vfx/cleric/index.html`: 39 abilities, three visible procs including Death Ward rescue, an ordinary attack and wounded-target Toll the Dead. Green/gold restoration, radiant judgement and distinct domain shapes use matching light. Four casting gestures append 20 frames on each of eleven Cleric race atlases. Split damage and domain proc numbers remain separate; all four affected native scenes were recaptured. Source/art commit `2927f1257f251291bb8885d2131dc0076ad7fde9`.

Full local gate passes: 7,259 .NET tests (zero skipped), 549 phone tests/build, 24 Node, 40 asset, 9 audio, 23 lore tests and lore/docs checks. Release and native Debug builds have zero warnings/errors. Three-run class median 20.5354 → 20.4545 ms (-0.39%, within 10%). Largest individual increase cleric.flame_strike (+9.75%), reported transparently. Peak texture growth +20.71 MiB; shipped-pack growth 0.58 MiB. All 90 six-hero Chapter I rounds containing Cleric remain within the approved cap: max 3.317 s Full / 3.017 s Auto Short / 0 Off. Real-hardware 16 ms and physical TV checks remain hardware validation.

All 39 gameplay definitions and passive data remain unchanged. Only 16 presentation objects changed in two golden event files; event order/counts, gameplay values, replay commands and phone fixtures are unchanged. Existing sounds are reused; other class atlases and workflow files are untouched. Cleric remains unchecked until explicit owner approval. Its PR and Druid wait for that OK; the owner merges the approved dependencies. CI remains disabled.


## Cleric approval and Druid

The owner replied “looks good, next class.” Cleric is approved and checked on #247; PR #255 is open against `combat-vfx-warlock`. The approved dependencies #251–#255 await owner merges. Druid starts from latest main `c4e121eb` with approved Cleric dependency `a4208a682d52cb7956dfd35d391cff49bbe13c38`, also its frozen before baseline.

The owner added: “spend time on the shapeshifts, make them match our other sprites and look cool.” Four Druid-only form atlases reuse approved anatomy/motion with native coat, ruff, leaf and bark work. Wolf and dire wolf now have distinct bodies/materials and sizes, as do bear and elder bear. All eight directions and actual actions are retained. Imported idle/walk foot drift is corrected on these variants, and six-column packing removes unused cells. Four monster sprite strings and their art registration change; all statistics and transformation rules remain identical.

Druid has 34 abilities, one visible class proc, five embedded zone hooks and three static features. The native reel has 53 samples: all visible features, ordinary Attack and each form's real movement, attacks and damage-triggered reversion. Healing/restoration/dispel/summoning/zone fixtures establish real prerequisites. Five gestures append 25 frames; every ability/proc has its own effect. Fire, weather, moonlight and healing emit matching light; physical plants/bark/fog stay unlit. Real beast attacks play all five frames with their impact aligned at 0.14 s, preserve every actual attack/save/proc, and keep multiple damage numbers separate.

The 87-test focused presentation/content/round suite passes. The added Druid party has 30 Chapter I rounds at max 3.300 s Full / 3.000 s Auto Short / 0 Off, passing the original stricter assertions. Seven golden tests pass normally, with no replay/golden refresh. Asset export/ground/compass checks pass. The full local gate and native review follow. No Paladin work has started; its work and the Druid PR wait for Druid approval. No paid generation or new sounds were used. CI remains disabled.


## Druid review handoff

The 53-sample native gallery is `../reports/combat-vfx/druid/index.html`: 34 abilities, sapling, five zone procs, ordinary Attack and each of four forms moving, attacking and reverting through real engine commands/events. Four Druid-only sprite variants preserve the approved creature anatomy/motion, with distinct sizes, coats, ruffs, leaf/bark work and corrected idle/walk ground contact. The player-scale inspector includes eight-direction walk/attack GIFs. All original creature art remains unchanged. Source/art commit `f6bd5a2334f9bc81d056d2fca7837f5c14fef745`.

The full local gate passes: 7,265 .NET tests (zero skipped), 549 phone tests/build, 24 Node, 46 asset, 9 audio, 23 lore tests and lore/docs checks. Release and native Debug builds have zero warnings/errors. Three-run class median 19.6715 → 19.9522 ms (+1.43%, within 10%). Largest individual increase druid.fog_cloud (+6.81%), reported transparently. Peak texture growth +57.02 MiB; pack growth 2.18 MiB. Thirty six-hero Druid Chapter I rounds add at most 3.300 s Full / 3.000 s Auto Short / 0 Off and pass the unchanged stricter limits. Real-hardware 16 ms and physical TV checks remain hardware validation.

All 34 ability definitions/passives and all four monster gameplay definitions are unchanged after removing only their presentation/sprite fields. Only four monster sprite strings select distinct Druid variants. Original creature metadata/art, phone/fixtures, replays/goldens, audio clips, other classes and workflow files stay unchanged. Legacy species voices/weapons remain available to inherited coats through the presentation sound fallback. All 248 focused checks and original sound assertions pass. No paid generation or new sounds were used. Druid remains unchecked pending explicit owner approval; its PR and Paladin wait for that OK. CI remains disabled.
