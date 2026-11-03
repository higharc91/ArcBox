# Overworld issue board

The running list of what's left on Wardenflame's overworld. **Keep this file current:** when you start an item, set it to `doing`. When you finish, set it to `done`, say what proved it, and add anything new you found. Don't delete done items until the user has seen them.

- **Reviewed:** 2026-11-02, on a real Windows PC with an RTX 5080, on this branch.
- **Status values:** `todo`, `doing`, `blocked`, `done`, `needs user`.
- **Priority:** P0 blocks others. P1 hurts the game. P2 matters but can wait. P3 is polish.

## Where the code is

**Start from `overworld-issue-board`.** It's the only branch with everything together.

| Branch | What it holds | State |
|---|---|---|
| `overworld-issue-board` | `main` (overworld, shops, gold) plus the latest overworld, plus the **new characters and textures**, plus this board | Builds clean, 5,901 tests. Not merged anywhere |
| `main` | The overworld and shops, PR #70 | Doesn't have the new characters or the latest environment work |
| `ccr-18ad4510-q68nad` | The overworld before shops | A cloud session pushes here. Fully contained here |
| `environmental-tiles` | The environment work, issue #69 | **7 commits newer than here** (the oil flask). Merge it in before working on OW-30 |
| `feat/creature-overworld-integration` | The new characters | Fully merged here, with the fix from OW-02 |
| `shops-and-gold`, `rest-and-recovery` | Shops (merged in #70); rest is issue #71 | `rest-and-recovery` is the same commit as `main` so far |

## This review's results

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

### OW-03 · `ARCBOX_CHAPTER=1` stalls on Windows · `todo`
After class picks the bots never take a step, and the run hit a 25-minute limit. The TV showed `STORY marker ch1_mayors_request` and the party at (26.5, 58.5). `route.cs` works by hand in 3 s. Suspect `tools/tactics-playtest/bots.cjs:1037`, which waits for the party's tile, while the tile only comes from `WALK` lines (`:722`). A fresh party hasn't walked, so nothing ever sets it. Take the first tile from `TACTICS walk snapped to`. Also check `\r` on Windows at `:717`.
**Done when:** Chapter 1 plays to its card on both branches on this PC. **Nobody has watched all of Chapter 1 on a real GPU yet.**

### OW-04 · `ARCBOX_WALK=1`'s default route is stale · `todo`
`tools/tv-e2e.sh:211`'s route walks into a building since Emberwick was rebuilt: the party bumps at (36,56). It's a test bug. Make the route come from `route.cs`, so a repaint can't break it.
**Done when:** the default mode passes.

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

### OW-11 · Only battles survive an exception · `todo`
`TryApply` and `CallOffBattle` guard battles, but walk, story, interact and resume exceptions reach `GameSession.Guard` (`GameSession.cs:267`) and end the room. A throw on resume would repeat each time the room opens (found by reading). Invariants run only in Debug.
**Done when:** a failure in those paths keeps the room and the save, with a test per path.

### OW-12 · World open stalls the main thread · `todo`
`FinishBuild` (`WorldCanvas.cs:~527`) runs `WorldFauna`'s constructor: about 440 ms in Debug by reading and timing. `FaunaRange.Pen` can be up to 18^4 rectangles. The title card hides it.
**Done when:** the main-thread part of opening is under a frame's budget.

### OW-13 · Explore view is 2.9 KB · `todo`
Its budget is 1,420 bytes, and it goes out on every step. Send things only when they change, or only those nearby.

### OW-14 · Fauna draws over props · `todo`
Animals avoid props only through `FaunaRange.Behind`. Sort them properly, or keep them clear.

## P2: tests, tools and tidying

- **OW-15 · Stray folder · `todo`:** `src/ArcBox.Host/src/ArcBox.Host/docs/...` holds untracked shots. A relative `--shots` path is resolving from the Host folder. Move anything wanted, delete the rest and fix the command. Check before deleting: it's untracked.
- **OW-16 · Symlink test · `todo`:** `AddTacticsTests.Lore_that_can_never_show_an_option_is_logged_at_start` needs symlink rights, so it always fails on a normal Windows account. Use a plain copy.
- **OW-17 · Flaky test · `todo`:** `ThingShowTests.Fifty_things_cost_a_frame_little_and_allocate_little` (`ThingShowTests.cs:512`) has a wall-clock limit. It fails under a full parallel run and passes alone.
- **OW-18 · REDLINE e2e · `blocked`:** needs Playwright. The user said not now.
- **OW-19 · Second hardware check · `todo`:** `local-hardware-check-2.md` hasn't run. `ARCBOX_RESTART=kill` and forced `ARCBOX_DROP_SIGNALS` have no confirmed pass.
- **OW-20 · Stale docs · `todo`:** cloud handoff §3, §5, §8 items 6, 9, 10. `2026-11-01-ashen-ford-handoff.md`. Report `2026-11-02-tactics-overworld.md` lines 28, 42, 107, 223, 233, 292. `environmental-tiles/README.md` and milestones B and C. `tools/tactics-world/README.md:176`. `docs/tv-host.md:353` (197 buildings now). The world dressing plan's `TallProps.cs`, which wasn't kept. `local-hardware-check-2.md:33`. The creature branch's `overworld-art-review` README "Current integration boundary". Add `--edit-props` to the painting guide.
- **OW-21 · Ambient · `todo`:** birds, leaves, embers and motes aren't built. The chimney path in `AmbientPlan.cs` matches no prop, so it's dead code. The new `river_steam` art is unused.
- **OW-22 · Place shots · `todo`:** replace `place-*.png` from a real walk after OW-09.

## P2: features under way

### OW-30 · Environmental tiles · `done`, issue #69
Merged in #72: engine, AI, oil, TV effects, immersive phone effects, content, simulations and Chapter 1 validation. Steam dissipates slowly and the web effect uses actual web shapes. Available on this branch through main.

### OW-31 · Shops · `todo`, issue #68
Merged in #70. Check the remake fix and the Chapter 1 gold source on the TV and phones, and give `oil_flask` a price of 10 once OW-30's branch is merged. The questions in #68 still stand.

### OW-32 · Rest and recovery · `integrated`, issue #71
Camp, inn and temple implementations from `rest-and-recovery` are merged into `npcs-and-quests` as an issue #73 prerequisite. Main has not been changed by this integration. NPC conversations will reuse these services.

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

1. The new characters' memory and download size (OW-05).
2. Hero and townsfolk art (OW-10).
3. The ford's look (OW-09), and whether a true bridge is worth changing the map.
4. Fuel for River Crossing (OW-30).
5. Shops questions in #68.
6. Hand-listed: handoff §8 items 4, 5, 10, 11, 19, 31. Interactables decisions 1 to 17. The speech fallback for lines with no clip.

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
