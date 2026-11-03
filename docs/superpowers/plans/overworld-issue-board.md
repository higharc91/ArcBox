# Overworld issue board

The running list of what's left on Wardenflame's overworld. **Keep this file current:** when you start an item, set it to `doing`. When you finish, set it to `done`, say what proved it, and add anything new you found. Don't delete done items until the user has seen them.

- **Reviewed:** 2026-11-02, on a real Windows PC with an RTX 5080.
- **Status values:** `todo`, `doing`, `blocked`, `done`, `needs user`.
- **Priority:** P0 blocks others. P1 hurts the game. P2 matters but can wait. P3 is polish.

## Where the code is

`main` is old: it stops at PR #67. **Don't start from `main`.**

| Branch | What it holds | State |
|---|---|---|
| `ccr-18ad4510-q68nad` | The latest overworld: town, story, fights, hidden things, animals, default-off environment rules | Latest. Contains all of `world-dressing-design`. A cloud session pushes here, so pull with rebase first |
| `environmental-tiles` | The same commit as above | Issue #69 |
| `feat/creature-overworld-integration` | The **new characters**, eight-direction creatures, art audit | 10 commits ahead, not merged. See OW-02 |
| `shops-and-gold` | Gold and shops | 4 commits ahead, not merged. Issue #68. See OW-01 |
| `overworld-issue-board` | This file | From `ccr-18ad4510-q68nad` at `ea90c2e2` |

## This review's results

Tip `ea90c2e2`, 1920×1080, `ultra`, Debug.

| Check | Latest branch | With new characters merged locally |
|---|---|---|
| Build | 0 warnings | 0 warnings |
| .NET tests | 5,805 of 5,806 (OW-16) | 3 fail in a trial merge (OW-02, OW-16, OW-17) |
| Web tests and build | 478 pass, build OK | No web change |
| `ARCBOX_STAGE=1` | OK | OK |
| `ARCBOX_RESTART=1` | OK | Not run |
| `ARCBOX_WALK=1` | **Fails**: stale route (OW-04) | Not valid: a short test route |
| `ARCBOX_CHAPTER=1` | **Stalled** after Muster (OW-03) | Not run |
| Frames walking, no shots | 0 of 2,663 over 16 ms, p99 12.04, worst 14.22 | 2 of 2,037 over 16 ms, worst 273 ms (OW-06). This run took shots, so re-measure |
| Press to the TV's `WALK` line | median 6 ms | Not measured |
| Texture memory, world open | 158 MB | **350 MB** (OW-05) |
| Errors in TV logs | 0 | 0 |

Shots are in `docs/superpowers/reports/overworld-review-2026-11-02/`. The new characters look much better than the old ones, and the world looks good in town. The ford is the weak place.

## P0: do these first

### OW-01 · Merge shops and gold · `needs user`
Five files conflict (6 hunks): `manifest.json:15`, `encounter.schema.json:183`, `Defs.cs:457`, `PackLoader.cs:44,188`, `RulesPack.cs:194`. All are the tip's environment beside the branch's shops on the same line: keep both. A trial merge built clean, and only the symlink test failed. **It changes the phone contract and adds rules, with no explicit approval recorded,** so ask the user first. Do this before OW-02, because it's small.
**Done when:** merged, all tests pass, old fixtures byte-identical, the user has OKed it.

### OW-02 · Merge the new characters · `todo`
Don't merge as is. It hand-edits `rules/world/ashen_ford.json` without the scene, so `A_region_file_is_what_its_scene_exports` fails, and a re-export would drop the dog, sheep and moved goat. It also warns about `pine` at (104,66) and (109,67). Probable cause, not proven: `PaletteBuilder.PropSwatches` follows manifest order, the branch reordered tent variants and added dog and sheep, and `palette/props.*` and the scene weren't rebuilt.
**Done when:** the palette and scene are regenerated without losing paint, the region file equals its export, and tests pass. OW-05 and OW-06 must be looked at before this ships.

### OW-03 · `ARCBOX_CHAPTER=1` stalls on Windows · `todo`
After class picks the bots never take a step, and the run hit a 25-minute limit. The TV showed `STORY marker ch1_mayors_request` and the party at (26.5, 58.5). `route.cs` works by hand in 3 s and gives `e e e s s`. Suspect the bots' wait in `tools/tactics-playtest/bots.cjs:1037`: the party's tile only comes from `WALK` lines at `:722`, and a fresh party hasn't walked, so nothing ever sets it. Check the first tile comes from `TACTICS walk snapped to`. Also check `\r` on Windows at `bots.cjs:717`: lines split on `\n`, and exact comparisons follow.
**Done when:** Chapter 1 plays to its card on both branches on this PC. **Nobody has watched all of Chapter 1 run on a real GPU yet.**

### OW-04 · `ARCBOX_WALK=1` has a stale route · `todo`
`tools/tv-e2e.sh:211`'s default script walks into a building since Emberwick was rebuilt: the party bumps at (36,56), and the check wants over 12 steps. It's a test bug, not a game bug. Make the route come from `route.cs` so a repaint can't break it.
**Done when:** the mode passes, and `ARCBOX_SHOTS=0` gives clean frame numbers.

## P1: bugs and problems

### OW-05 · Character memory · `todo`
Texture memory goes from 158 MB to 350 MB with the new characters, measured on the TV. A hero's sheets are about 37.5 MB each by estimate, and the pack grows from 118 MB to 179 MB, a cold download 51% larger. Fine on an RTX 5080, unknown on a TV box. Options: load only the sheets a party uses, drop frames, or smaller atlases.
**Done when:** a target is agreed with the user, and memory is under it.

### OW-06 · A 273 ms frame walking with the new characters · `todo`
One walking frame of 273 ms and two over 16 ms, in a run with `--perf` and shots. It may be the capture or a lazy texture load. Re-measure with `ARCBOX_SHOTS=0`. Then look at `FinishBuild` too (OW-12).
**Done when:** the cause is known, and no frame is regularly over 16 ms.

### OW-07 · Beaten goblins never leave · `todo`
`RegionPerson` has no flags (`RegionDefs.cs:32`), and `WorldPeople.Units` (`WorldPeople.cs:89`) reads none. Win at the ford and goblins still stand at (99,63), (99,64), (95,58), (117,55), (117,61), and camp goblins stay after any ending. Two of those are also a tile from the fight. It needs a `needs`/`gone` flag field on people, which is a region format change only.
**Done when:** people appear and leave by story flag, with a test for each branch.

### OW-08 · The ford's bridge planks end in a straight cut · `todo`
Shot `fight-bridge-cut-and-soft-ground.png` and the ford shots: the planks stop in a vertical line, and a unit behind them is cut off with it. It's on the latest branch, not caused by the merge. Probably a sorting or block-edge clip, or `bridge`'s sprite against the stamp edge. Add the case to `OverlayPictureTests`.
**Done when:** the crossing reads as a whole structure from every side, with no clipped units.

### OW-09 · The ford's ground looks soft and blotchy · `needs user`
Against the crisp town kit, the ash reads as an airbrushed smear, and the fight's ground is orange and olive camouflage. It's the worst-looking part of the region, and it's on the latest branch. Check whether `*_ford` materials are too soft or low-frequency, then repaint with crisper tops. Show the user a before and after.
**Done when:** the user is happy with the ford. It needn't match the town, but it should be the same pixel density.

### OW-10 · Heroes and townsfolk don't match · `needs user`
With the new characters, heroes are detailed and about 36 px tall, while townsfolk still use chunky 32×40 sheets at about 29 px. The new work only covers race × class. Options: regenerate NPCs the same way, or scale them.
**Done when:** the user picks, and a square of people reads as one art style.

### OW-11 · Only battles survive an exception · `todo`
`TryApply` and `CallOffBattle` guard battles, but walk, story, interact and resume exceptions reach `GameSession.Guard` (`GameSession.cs:267`) and end the room. A throw on resume would repeat on each open (found by reading the code). Invariants run only in Debug.
**Done when:** a failure in those paths keeps the room and the save, with a test per path.

### OW-12 · World open stalls the main thread · `todo`
`FinishBuild` (`WorldCanvas.cs:~527`) runs `WorldFauna`'s constructor: about 440 ms in Debug by reading and timing. The world's load is meant to be off the main thread. The title card hides it. `FaunaRange.Pen` is up to 18^4 rectangles. The new characters add 3 animals.
**Done when:** the main-thread part of opening is under one frame's budget.

### OW-13 · Explore view is 2.9 KB · `todo`
Its budget is 1,420 bytes, and it goes out on every step. Send things only when they change, or only those nearby.

### OW-14 · Fauna draws over props · `todo`
Animals avoid props only through `FaunaRange.Behind`. Sort them properly, or keep them clear.

## P2: tests, tools and tidying

- **OW-15 · Stray folder · `todo`:** `src/ArcBox.Host/src/ArcBox.Host/docs/...` holds untracked shots. A relative `--shots` path is resolving from the Host folder. Move anything wanted, delete the rest and fix the command. It's untracked, so check before deleting.
- **OW-16 · Symlink test · `todo`:** `AddTacticsTests.Lore_that_can_never_show_an_option_is_logged_at_start` needs symlink rights, so it always fails on a normal Windows account. Use a plain copy.
- **OW-17 · Flaky test · `todo`:** `ThingShowTests.Fifty_things` (`ThingShowTests.cs:512`) is a wall-clock limit. It failed once under load, measured 1.445 ms.
- **OW-18 · REDLINE e2e · `blocked`:** needs Playwright. The user said not now.
- **OW-19 · Second hardware check · `todo`:** `local-hardware-check-2.md` hasn't run. `ARCBOX_RESTART=kill` and forced `ARCBOX_DROP_SIGNALS` have no confirmed pass.
- **OW-20 · Stale docs · `todo`:** cloud handoff §3, §5, §8 items 6, 9, 10. `2026-11-01-ashen-ford-handoff.md`. Report `2026-11-02-tactics-overworld.md` lines 28, 42, 107, 223, 233, 292. `environmental-tiles/README.md`. `tools/tactics-world/README.md:176`. `docs/tv-host.md:353`, now 197 buildings. The world dressing plan's `TallProps.cs`, which was never kept. `local-hardware-check-2.md:33`.
- **OW-21 · Ambient · `todo`:** birds, leaves, embers and motes aren't built. The chimney path in `AmbientPlan.cs` matches no prop, so it's dead code. `river_steam` art on the creature branch is unused.
- **OW-22 · Place shots · `todo`:** replace `place-*.png` from a real walk after OW-09.

## P2: features under way

### OW-30 · Environmental tiles · `todo`, issue #69
Engine and AI are built and off by default. **Left, in order:** D oil flask (no `oil_flask` in rules), E TV art (`TileReacted` isn't drawn, no surface `ZoneArt`), F phone (Risk, Warn, Spread), G content (shaman lightning, opt-ins, sims in band), a new golden, then Chapter 1 end to end.
**New finding:** River Crossing has no native fuel. Its materials are `grass_ford/*`, and `environment.json` lists `grass/autumn`, so nothing burns there without oil. The plan's smallest version, decks, doesn't exist on that map. `needs user`.

### OW-31 · Shops · after OW-01
Decisions B to L in issue #68. **There's no gold source in the base content,** so the only income is selling kit, and sell-then-remake may be an exploit (unverified). Then give `oil_flask` a price of 10.

### OW-32 · Hidden things · `todo`
Scuffs and a cache exist in Ashen Ford, but no hidden path or door. Open items are in the interactables design §7.8.

### OW-33 · Places still short of the lore · `todo`
No granary building, because the kit has none. (42,70) is sacks and crates. Maud's hut is a plain cottage with no charms. Both are art requests: ask before generating.

## P3: later, needs design with the user

Don't build these without the user: a true Warden bridge (changes the battle map), mobs in the overworld, fog of war, talking to NPCs and side quests, hazards that hurt on the overworld (`TileFlags.Hazard` is read by no rule), inventory beyond the shops branch, Chapter II, and regenerating remaining weak art.

## Decisions waiting for the user

1. OK the shops contract change (OW-01), and shops decisions B to L.
2. Accept the new characters' memory and download size (OW-05).
3. Hero and townsfolk art (OW-10).
4. The ford's look (OW-09), and whether a true bridge is worth changing the map.
5. Fuel for River Crossing (OW-30).
6. Hand-listed: handoff §8 items 4, 5, 10, 11, 19, 31. Interactables decisions 1 to 17. Speech fallback for lines with no clip.

## Only a person can check

Painting in the real editor, the pad on real phones, sound, sofa distance, a modest TV box, story pacing, animal and fence look, and every **(person)** box in `docs/playtest-checklist.md`.

## Rules for whoever works from this

- Don't change game rules, balance numbers or the server-to-phone contract without asking the user.
- Don't merge to `main`, open PRs, or spend Scenario credits without asking.
- Pull with rebase before pushing to `ccr-18ad4510-q68nad`. A cloud session pushes there. If in doubt, use your own branch.
- **The user games on their right monitor.** Godot windows open on the left and never take focus: `--position 160,100` before the `--`, `--audio-driver Dummy`, and an uncommitted `src/ArcBox.Host/override.cfg` with `[display]` then `window/size/no_focus=true`, deleted afterwards. Always `--quit-after`, and stop what you start.
- Windows tools: `export PATH="/c/Users/fatma/AppData/Local/Microsoft/dotnet:$PATH"`. Godot is at `/c/Users/fatma/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`. Python isn't installed.
- Measure frames with `ARCBOX_SHOTS=0`: screenshots stall frames.
- Tests pass before every commit. Don't weaken a check to pass it. Look at the real TV for anything visual, and compare with `docs/superpowers/mockups/tactics/02-tv-explore-vote.png`.
- Regenerating the region deletes the scene: never do it over hand painting. Check `git log` on the scene first.
