# Handoff: finish the Wardenflame overworld overnight

You're a cloud Claude Code session taking over from a local session on the user's Windows PC. The user is asleep or away, and won't answer. Work without them: decide, record the decision, and carry on. Anything only they can decide goes on the list in §8.

The game's display name is Wardenflame. Its ids stay `tactics`.

## 1. Get the code

```sh
git fetch origin ccr-18ad4510-q68nad
git checkout ccr-18ad4510-q68nad
git pull --rebase origin ccr-18ad4510-q68nad
```

Work and push on `ccr-18ad4510-q68nad`. Run `git pull --rebase` before every push, because other sessions push here too.

PR #67 was merged by the user, so everything up to `3894cad` is on `main`. Later commits on this branch aren't, and **no PR is open. Don't open or merge one unless the user asks.**

Before anything else, confirm the build is green:

```sh
dotnet build ArcBox.slnx            # expect 0 warnings
dotnet test --solution ArcBox.slnx  # see §3 for the count
npm --prefix web/play ci
npm --prefix web/play test
npm --prefix web/play run build
```

On some runners `dotnet test ArcBox.slnx` needs the `--solution` form, and a filter is `--filter-class "*PhoneFixtureTests"`.

## 2. Binding rules from the user

- Don't change game rules, balance numbers or the server-to-phone contract without asking. The overworld's new phone screens and TV view data are approved in spec §4.5. Existing battle screens don't change.
- Don't merge or open PRs unless asked.
- If the host view or a phone view changes, regenerate the fixtures and check the diff: `UPDATE_FIXTURES=1 dotnet test --project games/Tactics/Tactics.Game.Tests --filter-class "*PhoneFixtureTests"`.
- TV: animate from each beat's own events, not from state. Send `Signal("shown", {seq})` after each beat. Keep each attack under about 1 s. A slow or missing TV must never stall the game.
- Read frame indices from manifests, never hard-code them. `down` holds its last frame, and `death` ends flat.
- Anything worded, ordered or computed is pure C# with tests. Godot classes only draw.
- Don't generate art through Scenario without asking.
- Don't make the balance decisions listed under "Decisions for the user" in the rules-engine tracker.
- Commit trailer, last line: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Stage files by name. Keep `.superpowers/` and `.claude/` out of commits.

The user's standard for this work, in their words: "Spend a ton of time on making sure the quality and interactions are perfect", and "After the overworld is finished, continue to work."

## 3. Where things stand

Expect about 3,600 .NET tests and 333 web tests, all passing, with 0 warnings. If they don't, stop and find out why before building on it.

**What works now:** after Muster the party stands in Ashen Ford. The leader steers from an 8-way pad on their phone, and the party walks the real TV world with the camera following. Leader rotation, handover, saved position, a stop that survives a lost release, and TV drops are all done. The battle list is still reached with `battles`, and a list fight returns to the world. **The world has no story and no fights on the spot yet.**

| Milestone | State | Where |
|---|---|---|
| 5a: world format, painting workspace, export, checks, Ashen Ford draft | Done | [plan](2026-10-31-tactics-overworld.md), `tools/tactics-world/README.md` |
| 5b: walking | Tasks 1 to 10 done; see below for Task 11 | [plan](2026-10-31-tactics-overworld-5b.md) |
| 5c: fights on the spot | Planned, not started | [plan](2026-10-31-tactics-overworld-5c.md) |
| 5d: the story runtime | Planned, not started | [plan](2026-10-31-tactics-overworld-5d.md) |
| 5e: tuning and a real session | Needs the user | [spec](../specs/2026-10-31-tactics-overworld-design.md) §6 |

**Also done on this branch:** board lighting (baked vertex colours, see `docs/tv-host.md`), and the fresh-install crash fix: `audio/general/text_to_speech` is off in `src/ArcBox.Host/project.godot`, because loading speech at startup collided with the cold shader compile. Crashes went from 12 of 43 to 0 of 30 launches. `ProjectSettingsTests` fails if it comes back.

Reports and proof shots are in `docs/superpowers/reports/2026-10-31-tactics-overworld/`.

## 4. What to do, in order

Each milestone's plan is complete: read everything above its "### Task 1" first, because the flags and defaults there bind you.

1. **Finish Task 11 of 5b** if `git log` has no `feat(tactics-playtest): the bots walk`: see §5.
2. **Catch engine failures,** the first item in §5.
3. **Do the interaction fixes.** [The interaction review](../reports/2026-10-31-tactics-overworld/interaction-review-5b.md) has a ruling on each item. Do every **Do** item before 5c, with tests.
4. Build 5c from its plan.
5. Build 5d from its plan.
6. Review the whole branch deeply, with one independent reviewer per area (rules engine, adapter, TV, phone, content, tools), then one fix wave.
7. Update `docs/superpowers/reports/2026-10-31-tactics-tv-scene.md` and add an overworld report, with open items ranked.

**How the work was run, and what paid off:** a fresh implementer per task or pair of tasks, then an independent reviewer told not to trust the implementer's report, then a fix round, then a scoped re-review. Reviewers probing real behaviour found far more than reading did. Keep these:

- **Model tests:** `WalkModelTests` runs random events against invariants. Raise it with `WALK_MODEL_EVENTS`. It found rotation electing an absent member and the crash in §5. Do the same for 5d's scene states.
- **Pictures:** compose or render and compare; frozen shots missed the overlay bug, which only showed in motion.
- **Mutations:** break a rule and check a test fails.
- **An interaction reviewer per milestone,** thinking as friends on a sofa, on top of code review.

## 5. Known bugs and open threads

- **Do this early: an engine failure can end a room and poison a save.** Nothing catches a `RulesInvariantException`, so `GameSession.Guard` reports "Game crashed" and the room returns to the lobby. Rules-engine spec §8 says the adapter should catch it, keep the prior state and show "Something went wrong with that move", and that was never built. The fatal command is lost, `EndBattle` never runs, and a save can stay poisoned: on seed 108, without the fix below, reopening with one player crashed 3 times out of 3, because the AI replayed the same move. Build what §8 says, with a test, and make sure an AI that hits it can't loop forever: skip or end its turn.
- **Fixed, the "share the tile" crash** (`Objectives.End` dropped queued `MoveStep`s without running `Halt`, leaving a walker on a Downed ally's tile). An engine fix with regression tests in `ReactionTests.cs`. Invariants run only in Debug, and the TV and `dotnet run` are Debug. The final beat gains one existing `Moved` event.
- **The sweep isn't finished.** Across 16,200 battles each, 5 crashes without the fix and 0 with it, but those runs used different battles, so that's suggestive and not proof. All 5 were this bug, on river_crossing (2), crypt_hall, ford_assault and forest_ruins. The deterministic harness is `docs/superpowers/reports/2026-10-31-tactics-overworld/battle-sweep-harness.cs.txt`. It needs two temporary patches, not committed: a `public static bool ZzNoFix` in Objectives with `while (!ZzNoFix && ...)` in `End`, and an `e.Data["outcome"]` catch around `Invariants.Check` in `Battle.Settled`. Run with `SWEEP_BOTH=1 SWEEP_SEEDS=100 SWEEP_OUT=<path>`: about 85 minutes on 14 threads. Run it after your own engine changes if you have the cores.
- **A second overlap remains:** `LastFree` can go stale if something spawns onto the tile a walker just left. Only `dragon_wakes` in `hall_of_the_flame`, with its `hpBelow` branch, might do this in the base pack. Choosing a fallback tile is a rules decision, so it's on the user's list.
- **Battles that don't end:** about 1.4% of sweep battles hit the 1,500-iteration cap, mostly a passive hero against archers that Dodge forever at range. Not a hang, because a player who moves ends it. Worth a line in the AI's notes.

- **Task 11, if unfinished:** Muster now lands on `walk`, so `tools/tactics-playtest/bots.cjs` must send `battles` or every `tools/tv-e2e.sh` mode fails. Add the walking modes, then measure thumb-to-TV latency (target ≤ 100 ms) and how long a killed or silent phone takes to raise the disconnect event. 5d and interaction review item 10 depend on that second number.
- **A one-off followers lag:** once, with a rate limiter flooded and stops being lost, followers sat about 8 tiles behind the leader. Not reproduced since. Task 11 was asked to add a flooding bot and check.
- **A flaky test, maybe:** one full run failed one unidentified test after Task 9. Three later runs and 3 after Task 10 were clean. If you see it, name it and fix it.
- **One late crash:** in a baseline run the TV died about 7.3 s in, around quit, with speech on. Not seen since it went off, but the count is small.
- **`Unable to create shader cache directory ShadowFrustumShaderRD/...`** appears in some filled-cache runs. Harmless so far, uninvestigated.

## 6. Notes that bind 5c and 5d

These came from reviews after the plans were written. The plans don't all say them.

**5c:**
- A saved battle no longer pins a region file, because `rules/world/` left the battle-save hash. A repaint that moves a stamp mid-battle won't restart the fight, so place that battle sensibly.
- A teleport is `_epoch++` with the legs cleared, never the clock. `Epoch` starts from `ctx.Now`.
- `Held` is false when the party is blocked, so it stands, and `Facing` shows the way.
- "Affected blocks rebuilt" has no API yet: `WorldBlock`'s items and boxes are readonly, probes are dropped after baking (`LitBoard.Build` can re-probe from `Lighting`), a sink also changes the next block's sides, and the world's `Lighting` is fixed, so a sunk prop's light stays.
- The world is hidden through the RenderingServer, not `Visible`, because showing it again re-recorded all 80 blocks. Keep that when `BoardCanvas` joins the layer.
- `WorldCamera` has `PartyReach` = 12, which outranks `EdgeMargin`, because a tile is 128 layout px wide. Use `Hold` in a fight.
- `party_spawn` is on the west bank, so a Parley party coming from the hills fades in rather than running through the goblins. That's in the 5c plan's default 3.
- `StoryOrder` in `Tactics.WorldTools` models 5d's beat order and resumes at `party_spawn[0]` after a win. 5c uses the leader's battle tile. Keep them consistent.

**5d:**
- `ReconsiderLeader`'s mid-scene guard goes in `WantedLeader`'s first arm, not at the top of `ReconsiderLeader`, or a leader who leaves mid-scene won't be replaced. A comment marks the spot.
- `StoryOrder` follows the 5d plan. If the order changes, change both. It reads only choice and beat `sets`, not node `sets`.
- After a Parley loss the far-bank "!" sits beyond the armed stamp. A step in restarts the fight, so nothing soft-locks, but hide or move the marker on a retry if it reads badly.
- The story check warns at export, and `BasePackRegionTests` fails on any warning.

## 7. Your environment

The local session was on Windows with an RTX 5080. Check what you have.

- **Godot 4.7.2 .NET** may be missing. If so, you can't run the TV. Do all pure work with tests, keep Godot-only changes small, and say plainly what wasn't seen. Don't claim a picture works from tests alone. List it for the user to check.
- **`tools/tv-e2e.sh`** has run its Tactics modes only on Windows. Its Linux path uses Xvfb and software Vulkan, and needs checking.
- **Don't install new packages.** `npm ci` from the lockfile is fine.
- **If the user's PC is on:** Godot windows go on their left monitor, `--position 160,100`, and never take focus: an uncommitted `src/ArcBox.Host/override.cfg` with `[display]` then `window/size/no_focus=true`, deleted afterwards. This doesn't apply on a cloud machine.

## 8. The user's list

Keep this list, add to it, and put it at the top of your final report. Don't stop work for these.

**Decisions:**
1. Speech fallback for lines with no clip: a Google TTS key, or captions only. The local session leans towards captions only. 5d has one switch, `StoryVoice.Fallback`.
2. Unanswered votes take the first option, so nobody touching a phone kills Mother Skarrow. The rule stands with a 45 s timer.
3. Losing pays half XP every time, so retrying can be farmed. Left alone as a rules change.
4. `hall_of_the_flame` is 16 px short of drawing at 2×, on the darkest map.
5. The balance decisions in the rules-engine tracker.
6. **Environmental tiles:** the user wants tiles with effects that interact with spells, such as water and lightning. Hazards stay harmless until this is designed with them. Bring it up when 5d finishes.
7. A deployed region export makes each TV re-download all 125 MB. Per-file caching would fix it.
8. If a spawn lands on the tile a walker just left, where should the walker go: the nearest free tile, or stay and allow the overlap?
9. "Back to walking" is a dead button on a pack with no region, and Results' "Next battle" now returns to the world. Both need a phone contract change.

**Defaults the user can overrule:** the 8-way pad; the session leader taking the lead back once stopped; hazards harmless; the loader refusing a recovery spawn below a one-way drop; lines advancing by themselves; one beat at a time; skill rolls with no natural 20 or 1; a skill-to-stat table of 18 SRD lines; `ch1_kindler` having no `place`.

**Only the user can check:**
- Painting in the real Godot editor against `tools/tactics-world/README.md`, whose editor steps are from memory, plus a workspace screenshot and time taken (spec §7).
- The pad on a real phone: vibration, iOS Safari, address bar, locking mid-walk, the back gesture.
- How it sounds: footsteps at -18 dB, `town_emberwick`, the board's audio levels.
- Sofa distance on a real TV, and a modest TV box. Performance is only measured on an RTX 5080, Debug build.
- The Ash pattern repeats every 4 tiles. A low-frequency vertex tint is the cheap fix to try in polish.
