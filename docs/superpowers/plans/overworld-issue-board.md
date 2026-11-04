# Overworld issue board

The running list of what's left on Wardenflame's overworld. **Keep this file current:** when you start an item, set it to `doing`. When you finish, set it to `done`, say what proved it, and add anything new you found. Don't delete done items until the user has seen them.

- **Reviewed:** 2026-11-02, on a real Windows PC with an RTX 5080, on this branch.
- **Status values:** `todo`, `doing`, `blocked`, `done`, `needs user`.
- **Priority:** P0 blocks others. P1 hurts the game. P2 matters but can wait. P3 is polish.

## Where the code is

All integrated overworld, character, environment, shop, rest and NPC/quest work is on `main` as of `9c41a7bd` (PR #75).
Issue #76 is being implemented on `stability-and-tooling`, cut from that main commit.

| Branch | What it holds | State |
|---|---|---|
| `main` | Overworld, characters, environmental tiles, shops, recovery, NPCs and quests | Integrated through PR #75 |
| `stability-and-tooling` | Issue #76 stability and tooling fixes | In progress |
| `environmental-tiles`, `shops-and-gold`, `rest-and-recovery`, `feat/creature-overworld-integration`, `npcs-and-quests` | Earlier feature work | Integrated into main |
| `overworld-issue-board`, `ccr-18ad4510-q68nad` | Historical review/session branches | Use current main and the issue branch for new work |

## Historical review results (2026-11-02)

| Check | Result |
|---|---|
| Build | 0 warnings |
| .NET tests | 5,901, all pass except OW-16 (always fails on this PC) and OW-17 (flaky, passes alone) |
| Web tests and build | 483 pass, build OK |
| `ARCBOX_STAGE=1`, with the new characters, after the `main` merge | OK, no errors |
| `ARCBOX_RESTART=1` (before the characters) | OK |
| `ARCBOX_WALK=1` | Fails on the default route (OW-04). Passes with a route that avoids buildings |
| `ARCBOX_CHAPTER=1` | **Stalls** after Muster (OW-03) |
| Walking frames, new characters, no screenshots | 0 of 3,784 over 16 ms, median 7.46, p99 9.42, worst 11.85 |
| Press to the TV's `WALK` line | median 2 ms, worst 15 |
| Texture memory, world open | 350 MB with the new characters, up from 158 MB (OW-05) |
| Errors in TV logs | 0 |

Shots are in `docs/superpowers/reports/overworld-review-2026-11-02/`: the new characters in town and at the ford, the same ford view with the old characters, and a fight. The new characters look much better. The town looks good. **The ford is the weakest place** (OW-08, OW-09).

## P0: do these first

### OW-01 · Merge shops and gold · `done`
Merged into `main` by PR #70, and here, with the five environment-and-shops conflicts resolved by keeping both. The branch's last commit says it fixed the remake gold exploit and funded Chapter 1's shops. Not re-verified.

### OW-02 · Merge the new characters · `done`
Merged here (`00b9ba4d`), fixed in `7802cc3c`. The art branch had changed the props palette's order (the tent's `hide` variant moved, and `farm_dog` and `farm_sheep` were added) and hand-edited the region file. Fixed by rebuilding the palette, `--remap-props` against the old manifest (986 cells), placing the dog, two sheep and moved goat with a new `--edit-props` command, and adding them to `AshenFordDraft`. The region file now equals its scene's export.
**Don't regenerate the scene from the draft:** that was tried, and it doesn't reproduce the region. The scene holds changes the draft doesn't make, such as a material and some trees.

### OW-03 · `ARCBOX_CHAPTER=1` stalls on Windows · `done`
The initial tile is taken from `TACTICS walk snapped to` (already present on the baseline). CRLF handling now strips line endings before exact screen/marker comparisons. LF/CRLF regression tests pass. Linux TV/phone runs completed both branches to the card and walking afterward: force 98 lines (including camp/inn), parley 130. Exact Windows commands are in `2026-11-02-local-hardware-check-2.md`; the owner’s real-GPU check remains OW-19.

### OW-04 · `ARCBOX_WALK=1`'s default route is stale · `done`
The default route uses `route.cs` navigation between authored targets, with enough waypoints to retain >12 steps before restart. Displacement starts at the initial snap rather than a delayed first motion sample, and still requires rendered movement. The final Linux Vulkan run passed unchanged timing/coverage/drop checks: 19 steps before restart, correct saved positions, cold TV reconnect, 20/20 presses and median latency 72 ms (budget 100). Parser/metric/navigation regressions pass.

## P1: bugs and problems

### OW-05 · Character memory · `seven approved NPCs pass; recheck later cast`
Texture memory goes from 158 MB to 350 MB with the new characters, measured on the TV. A hero is about 37.5 MB of sheets by estimate, and the pack grows from 118 MB to 179 MB, so a cold TV download is 51% larger. Fine on an RTX 5080, unknown on a TV box. Options: load only the sheets a party uses, drop frames, or smaller atlases.
**Owner decision (issue #80):** maximum **512 MiB (536,870,912 bytes)** of TV texture memory with four members.
On `townsfolk-art`, world sheets now resolve the actual sprite path, queue uploads within the existing texture budget, and release unused party/battle sheets. Only the current region's placed people and actual drawn heroes/creatures are requested. Fauna share one texture per source; their navigation is prepared off-thread, and their uploads are budgeted. Approved Bree is now installed; legacy sheets remain available.
The new `--world-demo=<region> --perf` gate draws four distinct heroes, walks the region, replaces all four with another race, restores them, and fails above the binary limit. Its log verifier also requires actual uploads/releases and rejects incomplete runs.
Loading-only checkpoint, before Bree installation: Linux software-Vulkan baseline → after region-open measurements: Ashen Ford **269.6 → 264.9 MiB**; Cinder Hollow **154.9 → 154.9 MiB**. Measured roster-swap peaks are **302.6 / 192.4 MiB**, both below 512. These are memory results, not GPU timing claims. At that checkpoint the unchanged production cold pack was **200,832,616 bytes (191.53 MiB)**, with identical before/after inventory hashes.
After approved Bree integration: production Ashen Ford opens at **267.56 MiB**, roster peak **305.24 MiB**; Cinder Hollow remains **154.87 / 192.37 MiB**. The production cold pack is now **201,146,592 bytes** (+313,976). Halden’s separate review passed at **307.44 MiB** peak; he was then approved and installed. Current combined production peaks at **307.61 MiB**. Current evidence: `reports/2026-11-03-townsfolk-art/approved-integration/`.
Evidence: `docs/superpowers/reports/2026-11-03-townsfolk-art/loading/`. Debug/Release builds have zero warnings/errors; 508 phone and 16 bot-tool tests pass. The full solution covered 6,253 tests with one known AI timing fluctuation; the entire isolated logic rerun then passed all 1,258 cases with unchanged limits. All other modules passed. The live walk/reconnect and Cinder Hollow creature/battle tour pass, with four real-party sheets retained across formation. Re-run the gate for the final approved cast; the art approval gate remains separate.

### OW-06 · A 273 ms frame with the new characters · `done`
It was the screenshots. With `ARCBOX_SHOTS=0` no frame was over 16 ms. See the results table.

### OW-07 · Beaten goblins never leave · `done`
People have stable IDs, optional lore identities and flag gates, round-tripped through the painting workspace. Goblin warriors leave after a won force or parley ending; Mother Skarrow leaves only after force. A failed attempt keeps them present. Departures fade over 0.35 seconds; arrivals wait off-screen or behind the party. Existing story flags remain unchanged.
**Validation:** 6,048 .NET tests and 497 web tests pass; two actual Godot exports are byte-identical. Integrated on `npcs-and-quests` for issue #73.

### OW-08 · The ford's bridge planks end in a straight cut · `todo`
In `fight-bridge-cut-and-soft-ground.png` and both ford shots, the planks stop in a vertical line, and a unit behind them is cut off with it. It's there without the new characters too. Probably a sorting or block-edge clip, or the bridge sprite against the stamp's edge. Add the case to `OverlayPictureTests`.
**Done when:** the crossing reads as a whole structure from every side, with no clipped units.

### OW-09 · The ford's ground looks soft and blotchy · `needs user`
Against the crisp town kit, the ash reads as an airbrushed smear, and the fight's ground is orange and olive camouflage. It's the worst-looking part of the region. Check whether the `*_ford` materials are too soft or too low in contrast, then repaint with crisper tops. Show the user a before and after.
**Done when:** the user is happy with the ford, at the same pixel density as the town.

### OW-10 · Heroes and townsfolk don't match · `seven named NPCs approved and integrated`
The new heroes are detailed and about 36 px tall, while townsfolk still use chunky 32×40 sheets at about 29 px. The new work only covers race × class.
**Owner decision (issue #80):** regenerate through Scenario concepts then PixelLab,
with owner approval between stages. On `townsfolk-art`, updated to main `2e64b9df`,
the queue covers 12 placed named NPCs (including Rook) and seven generic presets
with two variants each. Bree's first 2K concept anchor cost 12 Scenario CU with
explicit owner approval to spend; the owner approved its design on 2026-11-03
(“yes, her design looks correct”). The remaining 11 named sheets and seven paired
townsfolk sheets were individually priced at 13 CU each: 234 additional CU.
The owner approved the 234 CU batch and Mother Skarrow's design. All 19 primary
concept sheets are saved: 12 named characters and seven paired townsfolk presets.
Halden and Maud were recovered from existing Scenario assets with no new generation.
A child-B supplement completes body views missing from the original paired sheet.
Farmhand and guard are simpler 1K studies after provider output-token failures;
all four failed attempts were refunded in Scenario's usage ledger. Net issue spend
including Bree is **221 CU**, leaving **25 CU unused**. The inventory verifier
passes and the guarded resume reports zero missing primary sheets. The remaining
17 character/preset designs and child-B supplement were approved on 2026-11-03
(“yes, tthose look good. Proceed”). Paired-sheet view gaps still need directional
pixel review. PixelLab is now authenticated via local owner-supplied configuration.
Bree has four static candidates (15 generations); the owner switched back to
candidate 1 after trying candidate 2. Both idle studies are retained (2 generations
each, 19 total); the existing candidate-1 idle is reused without new spending.
The owner approved candidate 1's original idle. Its full eight-direction idle, walk
and talk review set is now complete, with real Windows TV shots at the inn, square
and market. The compact atlas is 4.5 MiB; that one-actor demo measured 190.5 →
195.0 MiB textures, not the required four-party OW-05 gate. PixelLab spend is
49.25 generations for Bree. The owner then approved the complete set and committed
TV appearance (“I approve or bree.”). Her exact 4.5 MiB atlas is now installed and
ordinary barks use talk with idle fallback. Optional placed-person variants survive
native Godot save/load/export; drawing and preload use one selector.
Halden’s complete eight-direction idle/walk/talk set cost 30 more generations;
the owner approved his 4.21875 MiB atlas and TV shots (“OK, he looks good”), and
the exact atlas is now installed.
The current live balance is **0.75 generations**; no purchase/upgrade. Other 24
sprites and portraits remain. The approved Bree production gate peaks at
**305.24 MiB** in Ashen Ford and **192.37 MiB** in Cinder Hollow. Cold production
pack is **201,146,592 bytes** (+313,976 bytes).
The earlier loading-only OW-05 checkpoint measured 264.91 MiB at production
Ashen Ford open, 267.56 MiB with the separate Bree review pack; all final module checks pass
with an isolated rerun of the known AI timing fluctuation. Full evidence and current checks are in `reports/2026-11-03-townsfolk-art/approved-integration/`.
Current continuation checks: 2,460 presentation, 676 game, 508 phone and 16 Node cases pass, plus native variant round-trip and writer cases. Live four-phone restart/reconnect/drop passes at 95 ms median with Mesa limited to two raster threads; the initial default-thread 136 ms miss remains documented. Budgets and contracts are unchanged.
Handoff: `docs/superpowers/plans/2026-11-03-townsfolk-art-cloud-handoff.md`. See
`tools/tactics-assets/concepts/townsfolk/README.md` for prompts, receipts and comparison.
**Done when:** both art stages are approved, the town reads as one style, optional
variants round-trip through the editor, and OW-05's memory/download checks pass.

### OW-11 · Only battles survive an exception · `done`
World inputs, timers and reconnect/resume operations now roll back failed actions and recover to safe walking, preserving purses, bags, stock and quest state. Broken active cursors, transient notifications and their timers are cleared; failed recovery saves bypass quest processing. The phones and TV show a recoverable notice. Invalid saved battles are checked before replay and called off without rewards/injuries, with structured engine-fault logs. The 30 focused checks passed (world paths, actual quest payout rollback, existing battle recovery and chapter settings), including a real subsequent walking step, reopening recovered saves and strictly advancing epochs for repeated recoveries.

Review follow-up: the recovery block itself is protected. If it fails, a direct minimal save clears all active cursors and moves the preserved party to the region start before callbacks or timer work. Two `EnterWorld` failure cases reproduce the original escape, then verify reopening/walking and phone/TV notices even with a throwing log sink. All 31 focused world/battle-fault checks pass, followed by all 638 gameplay and 411 server tests; the solution rebuild has zero warnings/errors.

### OW-12 · World open stalls the main thread · `todo`
`FinishBuild` (`WorldCanvas.cs:~527`) runs `WorldFauna`'s constructor: about 440 ms in Debug by reading and timing. `FaunaRange.Pen` can be up to 18^4 rectangles. The title card hides it.
**Done when:** the main-thread part of opening is under a frame's budget.

### OW-13 · Explore view is too big for one packet · `done`, issue #77
The whole TV view in Ashen Ford is about 4.3 KB, against one packet's 1,420 bytes, and it went out on every step. The TV now asks for **host view patches** (`HostViewPatch` in `ArcBox.Sdk.Protocol`). After a whole view on bind, a new game or a resend, it gets only what changed; arrays of things patch by id.

**Measured** (Tactics tests on Ashen Ford's real views):
- a party of 4 walking: median 449 bytes and at most 457 a step (whole view 4,271);
- a party of 6: median 316 and at most 450;
- a chest opening: 234.

**Proved by:**
- `WalkingTests` (every step of a walk under a packet, rebuilt exactly);
- `HostViewPatchTests` (format cases, plus 300 random views × 20 changes each, more with `HOSTPATCH_SEEDS`);
- `HostPatchTests` (the room's whole-or-patch rules);
- `HostClientTests` (a real TV client applying patches, an older TV that doesn't ask, and a TV taking a room over).

No game code changed: the TV's scenes still get whole views.

**On the real TV** (Linux, Xvfb and lavapipe, `ARCBOX_SHOTS=0`):
- `ARCBOX_WALK=1`: OK (restart, reconnect and dropped phone).
- `ARCBOX_RESTART=1`: OK; 5 patches applied.
- `ARCBOX_RECONNECT=1`: OK.
- `ARCBOX_CHAPTER=1`: OK; 580 patches applied.

None of the runs asked for a whole view again. Battles rarely patch: their views have no id-keyed arrays, so the whole view is usually as small, and it's sent whole.

### OW-14 · Fauna draws over props · `todo`
Animals avoid props only through `FaunaRange.Behind`. Sort them properly, or keep them clear.

## P2: tests, tools and tidying

- **OW-15 · Stray folder · `done`:** the reported `src/ArcBox.Host/src/ArcBox.Host/docs/...` folder was absent in this checkout; no files were deleted. Relative screenshot paths now produce an absolute-path instruction. Both command-line regressions pass.
- **OW-16 · Symlink test · `done`:** the lore-warning fixture now copies ordinary directories/files, removing its Windows symlink privilege requirement. The rebuilt targeted test passes.
- **OW-17 · Flaky test · `done`:** `Fifty_things_have_bounded_projection_work_and_allocate_little` replaces the former elapsed-time assertion with a projection-work bound, retaining the 50-object and 64 KB/frame checks. The focused test passes.
- **OW-18 · REDLINE e2e · `blocked`:** needs Playwright. The user said not now.
- **OW-19 · Second hardware check · `todo`:** `local-hardware-check-2.md` hasn't run. `ARCBOX_RESTART=kill` and forced `ARCBOX_DROP_SIGNALS` have no confirmed pass.
- **OW-20 · Stale docs · `done`:** cloud handoff §3, §5, §8 items 6, 9, 10. `2026-11-01-ashen-ford-handoff.md`. Report `2026-11-02-tactics-overworld.md` lines 28, 42, 107, 223, 233, 292. `environmental-tiles/README.md` and milestones B and C. `tools/tactics-world/README.md:176`. `docs/tv-host.md:353` (197 buildings now). The world dressing plan's `TallProps.cs`, which wasn't kept. `local-hardware-check-2.md:33`. The creature branch's `overworld-art-review` README "Current integration boundary". The named entries are updated, `PropFade` replaces obsolete `TallProps` references, and the painting guide documents `--edit-props`. See the stability report for verification.
- **OW-21 · Ambient · `todo`:** birds, leaves, embers and motes aren't built. The chimney path in `AmbientPlan.cs` matches no prop, so it's dead code. The new `river_steam` art is unused.
- **OW-22 · Place shots · `todo`:** replace `place-*.png` from a real walk after OW-09.

## P2: features under way

### OW-30 · Environmental tiles · `done`, issue #69
Merged in #72: engine, AI, oil, TV effects, immersive phone effects, content, simulations and Chapter 1 validation. Steam dissipates slowly and the web effect uses actual web shapes. Available on this branch through main.

### OW-31 · Shops · `done`, issue #68
The live TV/phone run earned the shipped council advance (200 gold, four 50-gold purses), verified own-purse/own-bag purchases and sales plus duplicate trade protection, sold the starting kit, remade three times through the real authenticated portal, reopened the shop and confirmed the kit stayed empty and gold unchanged. `oil_flask` is priced at 10 in shipped content. Remaining owner-overridable defaults are explicitly listed below; per-character purses, inventory and even gold rewards are settled.

### OW-32 · Rest and recovery · `done`, issue #71
Camp, inn and temple services are integrated into main. The issue #76 force run completed camp and inn through dawn on TV and phones; NPC conversations reuse these services.

### OW-33 · Hidden things · `todo`
Ashen Ford has scuffs and a cache, but no hidden path or door. The open items are in the interactables design §7.8.

### OW-34 · Places still short of the lore · `todo`
No granary building, because the kit has none: (42,70) is sacks and crates. Maud's hut is a plain cottage with no charms. Both are art requests, so ask before generating.

### OW-35 · NPCs and side quests · `done`, issue #73
Eight named conversations, voiced townsfolk barks, three lore-checked Ashen Ford quests, atomic rewards and the shared/private pinned quest journal. Completed on `npcs-and-quests`; main integration is tracked in [PR #75](https://github.com/higharc91/ArcBox/pull/75). Rest/recovery and the character/palette board are integrated as prerequisites; OW-07 is complete. Full suite: 6,081 .NET and 504 phone tests. Mobile layout audit: 264 cases, including movement-wheel clearance. Actual force/parley chapter runs and the grain quest passed. [Report](../reports/2026-11-03-npcs-quests/README.md).

### OW-36 · Ashmaw bounty · `doing`, issue #74
Work is on `ashmaw-bounty`, based on merged #73. Both character concepts and the pixel direction are approved. All eight directions and action sets are generated and curated; actual TV world/battle motion is owner-approved, and dedicated atlases/portraits are installed. The quest, cave, travel, class trinkets, dormant remake behavior, private phone rewards and voiced dialogue are implemented. A six-player live run completed all four chambers and the return/reward flow. Final cave dressing adds candles, animal remains and dried blood. The owner retained level 2, with an explicit journal recommendation, and requested tougher fights only for 4+ players. Paired trinket comparisons are complete. Final tuning gives four-player wins of 59.4% at level 2, 82.7% at level 3 and 97.5% at level 4; smaller-party results are unchanged. Verified: 6,191 .NET and 508 web tests, six phone viewports, zero-warning solution/editor builds. The owner’s follow-up requests matching concept art and a sprite overhaul for the remaining kobolds; that art follow-up is next. [Concepts and receipts](../../../tools/tactics-assets/concepts/ashmaw/README.md).

## P3: later, needs design with the user

Don't build these without the user: a true Warden bridge (changes the battle map), mobs in the overworld, fog of war, hazards that hurt on the overworld (`TileFlags.Hazard` is read by no rule), inventory beyond shops, Chapter II, and regenerating the remaining weak art.

## Decisions waiting for the user

1. OW-05's target is resolved: 512 MiB with four members. Used-only loading and fresh production/Bree-review memory gates pass. Recheck after final approved art integration.
2. All concepts and Bree’s complete set/TV appearance are approved; Bree is installed. Halden’s complete directional/motion set and TV preview are approved and installed. Other 24 sprites and portraits remain. Live PixelLab balance is 0.75 generations after 30 for Halden; no purchase or upgrade. Scenario remains 221 CU. See the approved-integration report.
3. The ford's look (OW-09), and whether a true bridge is worth changing the map.
4. Shops #68 defaults still available for owner override: SRD-style prices (C), 50% sell value (D), haggling off (E), no bag limit (I), and restocking policy (J: current stock is per-entry—potions restock each chapter, smoke bombs never restock; the original question proposed chapter restocking). Starting gold remains 0 (B2). Per-character purses/inventories, even splitting of all gold rewards, phone trading after keeper interaction and purchases into the buyer’s own bag were explicitly answered. Inn/temple spending was subsequently authorized in #71; the initial “no other uses” default is superseded.
5. Handoff §8 items 4 and 5 and remaining interactable owner choices stay in their source designs. Issue #76 settled 10 (called off, nothing won/lost), 11 (Release invariants), 19/31 (seeded Story chapter bots), and missing-clip speech (captions only).

## Only a person can check

Painting in the real editor, the pad on real phones, sound, sofa distance, a modest TV box, story pacing, the animals and fences, and every **(person)** box in `docs/playtest-checklist.md`.

## Rules for whoever works from this

- Don't change game rules, balance numbers or the server-to-phone contract without asking the user.
- Don't merge to `main`, open PRs, or spend Scenario credits without asking.
- Pull with rebase before pushing to the issue's designated branch: **`townsfolk-art` for #80**. Do not merge or open a PR for that issue. The earlier `ccr-18ad4510-q68nad` cloud branch is historical.
- **The user games on their right monitor.** Godot windows open on the left and never take focus: `--position 160,100` before the `--`, `--audio-driver Dummy`, and an uncommitted `src/ArcBox.Host/override.cfg` with `[display]` then `window/size/no_focus=true`, deleted afterwards. Always `--quit-after`, and stop what you start.
- Windows tools: `export PATH="/c/Users/fatma/AppData/Local/Microsoft/dotnet:$PATH"`. Godot is at `/c/Users/fatma/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`. Python isn't installed.
- Measure frames with `ARCBOX_SHOTS=0`: screenshots stall frames.
- Tests pass before every commit. Don't weaken a check to pass it. Look at the real TV for anything visual, and compare with `docs/superpowers/mockups/tactics/02-tv-explore-vote.png`.
- **Never regenerate the region from the draft.** Change single props with `--edit-props`, or paint in the editor, then export.

## Issue #76 owner follow-ups

- **Release invariants · `done`:** enabled by default in every build. Three Release corruption/check tests pass without helper overrides. A 14-unit, 10,000-check benchmark measured 0.068336 ms/check on the cloud runner; see the stability report for limits.
- **Called-off battles · `done`:** owner confirmed nothing won or lost: no XP or injuries, return to the world. `CallOffBattle` remains unchanged.
- **Missing recordings · `done`:** Tactics defaults to captions and the narrator blocks live TTS even for an unreadable recording. Registered themes remain. Story/policy tests pass; missing clips are logged.
- **Chapter harness · `done`:** chapter battles default to Story and seed 76; `ARCBOX_DIFFICULTY` overrides difficulty. The live parley save confirmed both settings, both branches reached the card, and the engine-setting regression test passes. Non-chapter/default gameplay retains Normal and fresh seeds.

## Additional validation observations

- Linux OpenGL compatibility cold TV reopening intermittently exceeded the loading watchdog on this software-rendered worker. Managed snapshots did not identify a managed deadlock; loader errors now include stage/progress. Default Vulkan/Mesa validation passed the cold reconnect. Further OpenGL/drawing investigation belongs with OW-12/the drawing issue; no opening timeout was increased.
- The production phone build succeeds but reports the existing REDLINE flyover chunk at 537.65 kB. This is recorded for the REDLINE follow-up; its warning limit was not raised.
- All five existing AI performance cases passed in an isolated Debug process with their original 250 ms p95 budgets. The final full-suite result is recorded in the issue #76 report.

### Issue #80 — Halden approval continuation

The owner approved Halden’s complete set and TV appearance in chat: “OK, he looks good”.
His exact reviewed 4.21875 MiB atlas is installed with existing talk/stop/resume
wiring; legacy assets remain. No new generation, portrait, rules, balance or phone
change. Production four-member texture gate peaks: Ashen Ford **307.61 MiB**,
Cinder Hollow **192.37 MiB**, below **512 MiB**. Cold pack **201,517,448 bytes**
(+370,856 from Bree-only). Other 24 sprites and portraits remain; 0.75 generations
was the last observed balance, with no spend for this integration. Latest report:
`docs/superpowers/reports/2026-11-03-townsfolk-art/halden-installed/report.md`.

Issue #80 alternative-art trial: per owner request, Mother Aubrine now has one
assistant-image-generation appearance candidate, reusing her approved concept.
No PixelLab credits spent; not installed or approved. Facing duplication, alpha
and native-grid/scale validation remain. See `reports/2026-11-03-townsfolk-art/imagegen-trial.md`.

Issue #80 Aubrine completion: appearance approved; full eight-direction idle/walk/
talk review (144 native frames) plus six portraits is finished with the image tool.
Final full-set/TV approval remains separate. Typed import/bark/contact, exact
rebuild, alpha/frame/height and four-party memory checks pass:310.65MiB Ashen,
192.37MiB Cinder below512. No production/rules/phone changes or PixelLab spend.
A guarded installer is prepared. Other23 sprites remain. Current review:
`docs/superpowers/reports/2026-11-03-townsfolk-art/aubrine-imagegen/report.md`.

### Issue #80 — Aubrine approved; Tobin appearance pass

Aubrine's finished set/native TV review was approved: “She looks great! Who is next?”.
Her exact144-frame atlas and six40×40 portraits are installed, with both priest
and named aliases. No completed art was regenerated. Fresh four-member production
memory peaks **310.83MiB Ashen /192.37MiB Cinder**, below512. Cold pack
**202,137,516bytes**, +620,068 from Bree/Halden. **111 focused tests pass**, and
Debug/Release test-project builds have zero warnings/errors. Rules, balance and
phone contracts unchanged. Report: `reports/2026-11-03-townsfolk-art/aubrine-installed/report.md`.

The owner chose Tobin Fell next. His first image-tool eight-view appearance
candidate is in `tools/tactics-assets/imagegen/townsfolk/tobin-fell/`, reusing the
approved concept with exact request/hashes. Await appearance approval before
motion generation; retain final TV/native validation gates. No Tobin production
installation. Zero PixelLab calls/credits; other23 sprites and remaining portraits
ahead. Keep branch only; rebase before pushing, no PR/merge/issue closure.

### Issue #80 — Tobin complete-set review

Tobin's appearance is approved: “yeah that looks great. Proceed with finishing him”.
His image-tool set now includes eight real idle/walk/talk facings (144 frames),
six 40×40 portraits, a 16.6-second review reel and native forge TV screenshot.
Full-set/portrait/TV approval remains pending; no production swap yet. The regular
source grid is registered once per direction, with shared transforms/palette,
binary alpha and horizontal/vertical foot checks. The corrected front walk keeps
all 138 unaffected native frames exact. Typed import/bark/portrait and offline
frame/rebuild checks pass. Four-party review memory peaks **313.87 MiB Ashen /
192.37 MiB Cinder**, below512. Production cold pack remains202,137,516 bytes.
Zero PixelLab calls/spend; rules/balance/phone unchanged. Other22 sprite sets
and remaining portraits are ahead. See `reports/2026-11-03-townsfolk-art/tobin-imagegen/report.md`.

Tobin's finished-set/TV approval has now arrived: “Looks good. Who is next”.
Approval is recorded against unchanged review hashes; production installation
and its follow-up checks are pending. Lark is next in the named concept queue.

### Issue #80 — Lark appearance trial

The owner authorized starting Lark. One image-tool eight-view pixel appearance
candidate reuses the approved concept, under `tools/tactics-assets/imagegen/townsfolk/lark/`.
Appearance approval is pending; no motions, portraits or production installation.
The rear concept defines one consistent instrument carry where front/rear studies
had disagreed. Exact request/input/output hashes are retained. Zero PixelLab calls
or credits; rules, balance and phone contracts unchanged. Tobin's full-set approval
is recorded, with production integration and its checks still pending.

### Issue #80 — Tobin production integration

Tobin's owner-approved body and six portraits are installed unchanged; no art
regeneration or PixelLab spend.118 focused tests pass; Debug/Release test-project
builds have zero warnings/errors. Fresh four-party production memory peaks:
**314.04 MiB Ashen /192.37 MiB Cinder**, below512. Cold production pack:
**202,843,253 bytes /4,828 files**, +705,737 bytes. Rules, balance and phone contracts
unchanged. Evidence: `reports/2026-11-03-townsfolk-art/tobin-installed/report.md`.
Lark's appearance approval has arrived; the completed-set/TV gate remains separate.

### Issue #80 — Lark finished-set review ready

Lark's pixel appearance was approved: “Yes, looks great. Continue”. The complete
image-tool review now includes eight real idle/walk/talk facings,144 native slots
from143 selected source cells (one valid talk peak is held), six40×40 portraits,
a16.6-second reel and a native TV square screenshot. Full-set/portrait/TV approval
remains pending; no Lark production installation. Northwest satchel/south walk
corrections retain119 unrelated native frames exactly; frame/alpha/palette/height/
foot/gait, typed importer/bark/portrait and exact-rebuild checks pass. Review peaks
**317.09 MiB Ashen /192.37 MiB Cinder**, four heroes, cap512. Production remains
202,843,253 bytes after approved Tobin installation.11 completion image-tool calls,
zero PixelLab calls/spend; rules/balance/phone unchanged. Other21 sprite sets and
remaining portraits are ahead. Report: `reports/2026-11-03-townsfolk-art/lark-imagegen/report.md`.

### Issue #80 — Lark installed; Corvin appearance review

The owner approved Lark's finished set: “Looks good, next”. The exact approved body
and six portraits are installed without regeneration.125 focused tests pass;
Debug/Release test-project builds have zero warnings/errors. Fresh production
four-party peaks **317.26 MiB Ashen /192.37 MiB Cinder**, below512. Cold pack:
**203,477,572 bytes /4,835 files**, +634,319. Rules/balance/phone unchanged.
Evidence: `reports/2026-11-03-townsfolk-art/lark-installed/report.md`.

Corvin Ashby is next in the approved named queue. His first image-tool eight-view
pixel appearance candidate reuses the approved concept, with exact request/hashes.
Appearance approval is pending before motions/portraits; finished-set/TV approval
remains a separate gate. No Corvin production change. One image-tool appearance
call; zero PixelLab calls/credits for this continuation. Other21 sprite sets and
remaining portraits are ahead. See `reports/2026-11-03-townsfolk-art/corvin-imagegen-trial.md`.

### Issue #80 — Corvin finished review

Corvin appearance approved: “OK, he looks good. Proceed”. The full directional motion and six portraits are ready for separate finished-set/TV approval. Native frame and typed importer/bark checks pass; four-party review peaks 320.30 MiB Ashen / 192.37 MiB Cinder, below 512 MiB. No Corvin production installation, rules/balance/phone change or PixelLab spend. See `reports/2026-11-03-townsfolk-art/corvin-imagegen/report.md`.

Owner preference: include a playable existing character voice sample with future character drafts and completed reviews; reuse the matching recorded bark/story audio.

### Issue #80 — Corvin production integration

The owner approved installation: “OK, finish him”. Corvin’s exact approved body and
six portraits are installed. Fresh four-party production peaks: **320.48 MiB Ashen /
192.37 MiB Cinder**, below512. Cold pack **204,126,234 bytes /4,842 files**,
+648,662 bytes. Rules, balance and phone contracts unchanged. Existing voice clips
should be included in future character reviews as recorded in the handoff.
Evidence: `reports/2026-11-03-townsfolk-art/corvin-installed/report.md`.

### Issue #80 — Pip appearance and voice draft

Pip’s first image-tool appearance candidate reuses the approved concept. An existing
recorded town bark is included as a phone-friendly MP3 with source/output hashes.
Appearance approval is pending. Child target52 native /26 world pixels; do not use
the adult importer unchanged. Correct the draft’s duplicated right-facing second
cell before deriving southwest motion guides. Pip needs a named child variant so
other children keep their own art. No production/rules/balance/phone changes or
PixelLab spend. Assets: `tools/tactics-assets/imagegen/townsfolk/pip-ashdown/`.

## Current Pip completion checkpoint

Appearance approved: “Yep, she looks good”. The completed eight-direction motion
and six portraits, recorded voice MP3 and voiced review reel are in
`tools/tactics-assets/imagegen/townsfolk/pip-ashdown/`. Full-set/TV approval is pending;
do not request appearance approval again or regenerate completed assets. The compact
child atlas is1152×512 /2.25 MiB, frames64×64, anchor[32,56], body52 native/26 world,
scale0.5. Southwest is corrected; southeast talk holds a prior pose instead of the
drifting last raw cell. Use final rotations/, not the old wrong southwest guide.
Review uses Pip’s named child_pip variant; generic children remain unchanged.
Frame/import/portrait/bark/variant isolation and exact-rebuild checks pass. Review
peaks322.55 /192.37 MiB with four heroes, below512. Pending installer guard preserves
production. All15 review hashes/TV screenshot are bound in the approval record.
Nine completion image-tool calls, zero PixelLab spend/new voice generation. Continue
with finished-set approval then exact installation and production checks; other19
sprite sets and remaining portraits are ahead. Include existing recorded voice
samples in future reviews. Rebase before push, no PR/merge/issue closure.
Evidence: `docs/superpowers/reports/2026-11-03-townsfolk-art/pip-imagegen/report.md`.

## Current checkpoint — Pip installed; Dagna appearance/voice draft

Pip’s approved exact atlas/six portraits are installed as a named child_pip variant,
preserving generic children.140 focused tests pass, Debug/Release test-project
builds zero warnings/errors; typed import/bark/portrait/child-isolation checks pass.
Region data differs only in Pip’s cosmetic variant. Four-party production peaks
**322.73 MiB Ashen /192.37 MiB Cinder**, below512. Cold204,485,341 bytes /4,849 files.
Evidence: `reports/2026-11-03-townsfolk-art/pip-installed/report.md`.

Dagna’s first appearance draft and existing recorded voice sample are ready for
appearance approval. Approved concept reused; initial southwest angle corrected.
Two image-tool calls, zero PixelLab spend/new speech. No Dagna production change;
retain appearance and full-set/TV gates. `tools/tactics-assets/imagegen/townsfolk/dagna-holt/`.

## Dagna replacement voice audition — 2026-11-04

Scenario OAuth reconnected in the current environment. A Gacrux audition combines
the existing town line and battle command, directed as a mature, low, flat Northern
guard captain. Source WAV, playable normalized MP3, exact request, live schema,
cost estimate, cap check, paid job receipt and hashes are retained in
`tools/tactics-assets/imagegen/townsfolk/dagna-holt/voice-recast/`.
One paid job: **4 CU quoted /4 CU billed**. No credits purchased. Member/project
caps are unlimited; this does not establish the remaining team plan balance.

**Owner voice approval pending.** Production cast, all66 Dagna clips and captions
remain unchanged. Gacrux also exists in the player grim.high cast; assess identity
distinction before finalizing. The MP3 has normalization only, no pitch shift;
this is a fresh synthesis, not processing of the rejected recording. Source is
mono24 kHz /7.48 seconds; mechanical decode/hash checks pass. Human listening
review must assess maturity, accent, exact words and command delivery.

Retain appearance and full-set/TV gates. Keep authored-recordings-only playback;
the handoff's old live-fallback instructions are historical. No live TTS is to be
enabled. No rules, balance, phone contracts or production textures changed.

## Current Dagna checkpoint — approved veteran voice installed

The owner approved the Gacrux audition and authorized the remaining voice lines.
All66 Dagna lines are now installed in that cast, with the approved veteran
direction, no pitch/filter and unchanged captions.63 distinct renders cost232 CU
quoted/billed (236 CU including the prior audition). No credits purchased.
Original clips and paid provenance remain intact; the normal draft voice sample
now plays the new production town bark. Future lines inherit the cast direction.
Full voice inventory1443/1443; checker0 flags;20 Python,71 voice/scene and52
story/bark playback tests pass. Exact re-import hashes match. Rules, balance,
phone contracts, authored-recordings-only playback and textures unchanged.
Appearance/full-set/TV artwork gates remain pending. Report:
`docs/superpowers/reports/2026-11-03-townsfolk-art/dagna-voice-recast/report.md`.
Reuse these recordings; do not regenerate the approved voice. No PR or merge;
rebase before pushing townsfolk-art. Earlier blocked voice notes are historical.

## Current checkpoint — Dagna completed review with approved voice

The owner authorized finishing her existing appearance (“ok, finish those please”).
Eight real idle/walk/talk directions,144 native cells, six40×40 portraits and a
19.6-second voiced review are ready for **finished-set/TV approval before art
installation**. Her66-line veteran voice recast remains approved and installed;
the reel reuses its town bark, with no new speech generation. No appearance
reapproval or concept regeneration is needed.

Atlas1728×768 /5.0625 MiB,96×96 frames, anchor[48,84], scale0.5, adult72/36.
Binary alpha/shared96-colour palette, fixed transform per direction, no mirroring
or per-frame recentering. Straight-back pauldron corrected to anatomical right;
use final rotations and north-corrected-source, not the superseded north guide.
All144 source cells selected; native bounds/motion/foot checks and six portraits
pass. Ten completion image calls plus two prior appearance calls; zero PixelLab
calls/spend/purchases. Requests/raw outputs/input hashes/normalized frames retained.

Typed import/portrait/bark stop-resume and140 focused presentation tests pass;
TV host Debug build0 warnings/errors. Final review four-party texture peaks
**325.77 MiB Ashen /192.37 MiB Cinder**, below512; native uploads/releases and
all-four race replacement/restoration pass. Production unchanged:207,000,224 bytes /
4,915 files; inventory hash544cebc50f4f92407ee35be5e2e7fb12fec42f45dbde3addfb7202fafd5addb8.
No rules, balance or phone changes. Guarded installer refuses pending full-set
approval before writing production.19 review hashes and native TV shot are bound
in sprite-approval.json; offline exact rebuild verified.

Review/video/voice: tools/tactics-assets/imagegen/townsfolk/dagna-holt/.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/dagna-imagegen/report.md.
Next: owner finished-set/TV approval, exact installation, production checks.
Reuse all completed assets/voice. Rebase before pushing townsfolk-art; no PR, merge
or issue closure. Earlier pending-appearance and blocked-voice notes are historical.
