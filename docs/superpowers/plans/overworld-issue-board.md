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
| `tv-drawing-polish` | Issue #78: OW-08, OW-12, OW-14, OW-21 and the wall-plug seam (OW-23) | Pushed for review, no PR yet |
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

### OW-05 · Character memory · `needs user`
Texture memory goes from 158 MB to 350 MB with the new characters, measured on the TV. A hero is about 37.5 MB of sheets by estimate, and the pack grows from 118 MB to 179 MB, so a cold TV download is 51% larger. Fine on an RTX 5080, unknown on a TV box. Options: load only the sheets a party uses, drop frames, or smaller atlases.
**Done when:** the user agrees a target, and memory is under it.

### OW-06 · A 273 ms frame with the new characters · `done`
It was the screenshots. With `ARCBOX_SHOTS=0` no frame was over 16 ms. See the results table.

### OW-07 · Beaten goblins never leave · `done`
People have stable IDs, optional lore identities and flag gates, round-tripped through the painting workspace. Goblin warriors leave after a won force or parley ending; Mother Skarrow leaves only after force. A failed attempt keeps them present. Departures fade over 0.35 seconds; arrivals wait off-screen or behind the party. Existing story flags remain unchanged.
**Validation:** 6,048 .NET tests and 497 web tests pass; two actual Godot exports are byte-identical. Integrated on `npcs-and-quests` for issue #73.

### OW-08 · The ford's bridge planks end in a straight cut · `done`, issue #78
In `fight-bridge-cut-and-soft-ground.png` and both ford shots, the planks stop in a vertical line, and a unit behind them is cut off with it. It's there without the new characters too. Probably a sorting or block-edge clip, or the bridge sprite against the stamp's edge. Add the case to `OverlayPictureTests`.
**Done when:** the crossing reads as a whole structure from every side, with no clipped units.
**What it was:** neither a block nor a stamp edge. `bridge_timber.png` is 192×128 for a 96×64 prop, and the painter took a sprite's size in texels as its source, so it drew the top-left quarter at twice the size, cut off straight down and across, and that quarter went over the unit beside it. Drawn whole, the deck then sorted on its own tile and went over anyone on the tiles it spans.
**Fixed (`799e8f70`):** the painter samples one whole frame of a texture, whatever its size. A low prop wider than a tile (the three bridge spans) is cut into quads half a tile wide, split along its run's tile edges, each sorted on the tile it lies over: in the world, a fight in place and a plain board. A quad clamped to a triangle no longer crosses nothing in `WorldPlan`'s test.
**Proved by:** `OverlayPictureTests`: the bridge is drawn whole, a unit on each of its tiles stands over it and one behind it is under it, on the region and on the map, and the ford's walks are still true painter's order. Shots: [ford](../reports/tv-drawing-polish/ow08-ford-bridge-before-after.png) and [fight](../reports/tv-drawing-polish/ow08-fight-bridge-before-after.png), before and after, and the [fight in place](../reports/tv-drawing-polish/ow08-fight-in-place-after.png) from an `ARCBOX_STAGE=1` run. Issue #82 replaces the bridge, and nothing here repaints it. Its banks are at different heights, so a unit wading the ford beside the high bank's abutment is partly behind it, which is right for that art.

### OW-09 · The ford's ground looks soft and blotchy · `needs user`
Against the crisp town kit, the ash reads as an airbrushed smear, and the fight's ground is orange and olive camouflage. It's the worst-looking part of the region. Check whether the `*_ford` materials are too soft or too low in contrast, then repaint with crisper tops. Show the user a before and after.
**Done when:** the user is happy with the ford, at the same pixel density as the town.

### OW-10 · Heroes and townsfolk don't match · `needs user`
The new heroes are detailed and about 36 px tall, while townsfolk still use chunky 32×40 sheets at about 29 px. The new work only covers race × class.
**Done when:** the user picks regenerating NPCs the same way or scaling them, and a square of people reads as one style.

### OW-11 · Only battles survive an exception · `done`
World inputs, timers and reconnect/resume operations now roll back failed actions and recover to safe walking, preserving purses, bags, stock and quest state. Broken active cursors, transient notifications and their timers are cleared; failed recovery saves bypass quest processing. The phones and TV show a recoverable notice. Invalid saved battles are checked before replay and called off without rewards/injuries, with structured engine-fault logs. The 30 focused checks passed (world paths, actual quest payout rollback, existing battle recovery and chapter settings), including a real subsequent walking step, reopening recovered saves and strictly advancing epochs for repeated recoveries.

Review follow-up: the recovery block itself is protected. If it fails, a direct minimal save clears all active cursors and moves the preserved party to the region start before callbacks or timer work. Two `EnterWorld` failure cases reproduce the original escape, then verify reopening/walking and phone/TV notices even with a throwing log sink. All 31 focused world/battle-fault checks pass, followed by all 638 gameplay and 411 server tests; the solution rebuild has zero warnings/errors.

### OW-12 · World open stalls the main thread · `done`, issue #78
`FinishBuild` (`WorldCanvas.cs:~527`) runs `WorldFauna`'s constructor: about 440 ms in Debug by reading and timing. `FaunaRange.Pen` can be up to 18^4 rectangles. The title card hides it.
**Done when:** the main-thread part of opening is under a frame's budget.
**Fixed (`48589e34`):** `FaunaRange.Pen` keeps its rule but reads each side of a rectangle from running counts, four lookups each. The herd (`WorldHerd`), the ambient plan, the prop fade and the world's nodes (blocks, the overlay canvas, the ambient layer, made out of the tree) are built with the plan on the build thread, and the main thread only adds them. The animals' sheets, the ambient images and the animal calls' sounds load with the textures, within their 8 ms budget.
**Measured** with `--world-demo --perf` on the cloud worker (Debug, lavapipe, one run each), as the new `WORLD open on the main thread` line: Ashen Ford **1,112.7 ms → 9.0 ms** (988 ms of it was the fauna); Cinder Hollow's demo, which opens the ford and then the cave, **1,094.9 and 15.9 ms → 11.2 and 1.1 ms**. The worst frame in the `PERF` window that holds the open went from 1,210 ms to 56 ms, about a lavapipe frame. **Proved by** `FaunaTests` (the pens are the same as counting tile by tile, on random fences and the ford's own; a field of fences is quick). Not measured on the RTX 5080.

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

### OW-14 · Fauna draws over props · `done`, issue #78
Animals avoid props only through `FaunaRange.Behind`. Sort them properly, or keep them clear.
**Fixed (`48589e34`):** `WorldHerd` gives the animals to the frame as units, sorted in with the people and walkers, so the overlay paints what stands in front of one over it. `Behind` is gone, so an animal may now go behind a prop. A figure walking along one axis could round into the next tile's slot (`UnitPlace.Over`), and that's fixed too. **Proved by** `OverlayPictureTests` (a goat behind a haystack is hidden by it from three sides, one in front is over it, both true painter's order), `FaunaTests` (the ford's herd wanders the same way twice and keeps to its ground) and the [farm](../reports/tv-drawing-polish/ow14-farm-animals-after.png).

## P2: tests, tools and tidying

- **OW-15 · Stray folder · `done`:** the reported `src/ArcBox.Host/src/ArcBox.Host/docs/...` folder was absent in this checkout; no files were deleted. Relative screenshot paths now produce an absolute-path instruction. Both command-line regressions pass.
- **OW-16 · Symlink test · `done`:** the lore-warning fixture now copies ordinary directories/files, removing its Windows symlink privilege requirement. The rebuilt targeted test passes.
- **OW-17 · Flaky test · `done`:** `Fifty_things_have_bounded_projection_work_and_allocate_little` replaces the former elapsed-time assertion with a projection-work bound, retaining the 50-object and 64 KB/frame checks. The focused test passes.
- **OW-18 · REDLINE e2e · `blocked`:** needs Playwright. The user said not now.
- **OW-19 · Second hardware check · `todo`:** `local-hardware-check-2.md` hasn't run. `ARCBOX_RESTART=kill` and forced `ARCBOX_DROP_SIGNALS` have no confirmed pass.
- **OW-20 · Stale docs · `done`:** cloud handoff §3, §5, §8 items 6, 9, 10. `2026-11-01-ashen-ford-handoff.md`. Report `2026-11-02-tactics-overworld.md` lines 28, 42, 107, 223, 233, 292. `environmental-tiles/README.md` and milestones B and C. `tools/tactics-world/README.md:176`. `docs/tv-host.md:353` (197 buildings now). The world dressing plan's `TallProps.cs`, which wasn't kept. `local-hardware-check-2.md:33`. The creature branch's `overworld-art-review` README "Current integration boundary". The named entries are updated, `PropFade` replaces obsolete `TallProps` references, and the painting guide documents `--edit-props`. See the stability report for verification.
- **OW-21 · Ambient · `done`, issue #78:** birds, leaves, embers and motes weren't built, the chimney path in `AmbientPlan.cs` matched no prop, and the new `river_steam` art was unused. Now (`5eaad666`, wired in `48589e34`) leaves fall from leafy trees in their own colours, embers rise from campfires and torches, motes hang in lamplight at dusk, a bird or a flock crosses the sky now and then (crows at dusk), and the ford's river steams with `river_steam`. All of it is ambient-manifest art, chosen once by hash. The chimney path is gone, since the town kit's buildings smoke for themselves. **Proved by** `AmbientPlanTests` and shots of the [camp](../reports/tv-drawing-polish/ow21-ambient-camp-before-after.png), [river](../reports/tv-drawing-polish/ow21-ambient-river-before-after.png) and [trees](../reports/tv-drawing-polish/ow21-ambient-trees-before-after.png). **Frame time** on lavapipe (Debug, no shots, same machine, before and after): the ford's crossing had a median of 55.6 and 53.3 ms, with render CPU 1.29 both times; a town walk had a median of 96.5 and 97.8 ms, with render CPU 1.74 and 1.82. Lavapipe can't show 16 ms, so the RTX 5080 is still the check (OW-19). The `river_steam` mist is faint by design (alpha at most 60 of 255) and was lost on the water in the dusk's light, so it's drawn whole and additively, catching the light as a glow does.
- **OW-22 · Place shots · `todo`:** replace `place-*.png` from a real walk after OW-09.
- **OW-23 · Wall-plug seam · `done`, issue #78:** an unfound hidden path left a notch in its hedge, and a secret door left a faint seam (interactables design §7.8). There were two causes. The overlay painted the open tile's own top back over the plug's sides, and the plug wasn't graded as the world is. The earlier `world: true` changed nothing visible because the notch hid it. Fixed in `59586ff5`. **Proved by** `WallPlugsTests` (a plugged tile against its neighbour, for both) and the [path](../reports/tv-drawing-polish/plug-hedge-path-before-after.png) and [door](../reports/tv-drawing-polish/plug-secret-door-before-after.png) shots.

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

### OW-37 · The owner's rule decisions · `done`, issue #83
Merged in PR #89. The owner's answers of 2026-11-03, each recorded as decided in its design:
found items go to the party's pick by a 15 s vote on the 5d vote screen, one per loot line, silence to the opener, a party of one at once
([interactables §3.1](../specs/2026-11-02-tactics-interactables-design.md)); a sprung trap gives the leader a minor rest-kind injury
(three new: scraped hands, singed, twisted knee; off the battle table) that any camp or inn mends (§3.3); a failed pick, force, seal or
search reopens after the next long rest (`InteractableSave.Rest`, §3.2); the key ring in the quest journal and the portal (§3.8); and
haggling, once per shop visit, Persuasion vs the shop's DC (default 15), −10% buys and +10% sells on a pass
([shops §3.3](../specs/2026-11-02-tactics-shops-and-gold-design.md)). New phone fixture `vote-loot`; the five shop fixtures gained
`haggle`; every other fixture is unchanged. e2e: `ARCBOX_DECISIONS=1 tools/tv-e2e.sh` (bots on the real TV, a copied pack with one
strongbox).


**Follow-up answers (the owner, 2026-11-04):**
- Keep the trap injuries as built, with black eye included.
- A failed Arcana seal reopens after a rest, like pick and force.
- Search may be retried once per rest.
- **Rooms and cures haggle too:** a tenth off, rounded up. The inn and temple offer Haggle (`haggle-services`).

## P3: later, needs design with the user

Don't build these without the user: a true Warden bridge (changes the battle map), mobs in the overworld, fog of war, hazards that hurt on the overworld (`TileFlags.Hazard` is read by no rule), inventory beyond shops, Chapter II, and regenerating the remaining weak art.

## Decisions waiting for the user

1. The new characters' memory and download size (OW-05).
2. Hero and townsfolk art (OW-10).
3. The ford's look (OW-09), and whether a true bridge is worth changing the map.
4. Shops #68 defaults still available for owner override: SRD-style prices (C), 50% sell value (D), no bag limit (I), and restocking policy (J: current stock is per-entry—potions restock each chapter, smoke bombs never restock; the original question proposed chapter restocking). Starting gold remains 0 (B2). Per-character purses/inventories, even splitting of all gold rewards, phone trading after keeper interaction and purchases into the buyer’s own bag were explicitly answered. Inn/temple spending was subsequently authorized in #71; the initial “no other uses” default is superseded. Haggling (E) was answered on 2026-11-03 (OW-37).
5. Handoff §8 items 4 and 5 and the interactable owner choices still open (4–7, 9 and 10–17 but 15) stay in their source designs. Issue #76 settled 10 (called off, nothing won/lost), 11 (Release invariants), 19/31 (seeded Story chapter bots), and missing-clip speech (captions only).
6. New from OW-37, for the owner: the three trap injuries' names, effects and weights (3, 3, 2) and whether a trap may also draw the old black eye; art for their icons (stand-ins now); whether a seal reopens after a rest like a pick or force (it does now); whether rooms and cures should haggle (they don't).

**Answered 2026-11-03 (issue #83, OW-37), no longer waiting:** interactables decisions 1 (loot: the party's pick), 2 (failed ways reopen after a long rest), 3 (traps: a minor injury), 8 (keys: a key ring) and 15 (a missed search retries after a rest), and shops decision E (haggling on, once per visit).

## Only a person can check

Painting in the real editor, the pad on real phones, sound, sofa distance, a modest TV box, story pacing, the animals and fences, and every **(person)** box in `docs/playtest-checklist.md`.

## Rules for whoever works from this

- Don't change game rules, balance numbers or the server-to-phone contract without asking the user.
- Don't merge to `main`, open PRs, or spend Scenario credits without asking.
- Pull with rebase before pushing to `ccr-18ad4510-q68nad`. A cloud session pushes there. If in doubt, use your own branch.
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
