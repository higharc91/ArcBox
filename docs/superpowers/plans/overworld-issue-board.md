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

### OW-05 · Character memory · `needs user`
Texture memory goes from 158 MB to 350 MB with the new characters, measured on the TV. A hero is about 37.5 MB of sheets by estimate, and the pack grows from 118 MB to 179 MB, so a cold TV download is 51% larger. Fine on an RTX 5080, unknown on a TV box. Options: load only the sheets a party uses, drop frames, or smaller atlases.
**Done when:** the user agrees a target, and memory is under it.

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

### OW-10 · Heroes and townsfolk don't match · `needs user`
The new heroes are detailed and about 36 px tall, while townsfolk still use chunky 32×40 sheets at about 29 px. The new work only covers race × class.
**Done when:** the user picks regenerating NPCs the same way or scaling them, and a square of people reads as one style.

### OW-11 · Only battles survive an exception · `done`
World inputs, timers and reconnect/resume operations now roll back failed actions and recover to safe walking, preserving purses, bags, stock and quest state. Broken active cursors, transient notifications and their timers are cleared; failed recovery saves bypass quest processing. The phones and TV show a recoverable notice. Invalid saved battles are checked before replay and called off without rewards/injuries, with structured engine-fault logs. The 30 focused checks passed (world paths, actual quest payout rollback, existing battle recovery and chapter settings), including a real subsequent walking step, reopening recovered saves and strictly advancing epochs for repeated recoveries.

### OW-12 · World open stalls the main thread · `todo`
`FinishBuild` (`WorldCanvas.cs:~527`) runs `WorldFauna`'s constructor: about 440 ms in Debug by reading and timing. `FaunaRange.Pen` can be up to 18^4 rectangles. The title card hides it.
**Done when:** the main-thread part of opening is under a frame's budget.

### OW-13 · Explore view is 2.9 KB · `todo`
Its historical budget is 1,420 bytes, and it goes out on every step. Send things only when they change, or only those nearby.

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

## P3: later, needs design with the user

Don't build these without the user: a true Warden bridge (changes the battle map), mobs in the overworld, fog of war, hazards that hurt on the overworld (`TileFlags.Hazard` is read by no rule), inventory beyond shops, Chapter II, and regenerating the remaining weak art.

## Decisions waiting for the user

1. The new characters' memory and download size (OW-05).
2. Hero and townsfolk art (OW-10).
3. The ford's look (OW-09), and whether a true bridge is worth changing the map.
4. Shops #68 defaults still available for owner override: SRD-style prices (C), 50% sell value (D), haggling off (E), no bag limit (I), and restocking policy (J: current stock is per-entry—potions restock each chapter, smoke bombs never restock; the original question proposed chapter restocking). Starting gold remains 0 (B2). Per-character purses/inventories, even splitting of all gold rewards, phone trading after keeper interaction and purchases into the buyer’s own bag were explicitly answered. Inn/temple spending was subsequently authorized in #71; the initial “no other uses” default is superseded.
5. Handoff §8 items 4 and 5 and remaining interactable owner choices stay in their source designs. Issue #76 settled 10 (called off, nothing won/lost), 11 (Release invariants), 19/31 (seeded Story chapter bots), and missing-clip speech (captions only).

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
