# Meridian v1: the success criteria, audited (M7, Task 12)

- **Date:** 2026-11-20
- **Build:** branch `ccr-de425e76-4vxzw2`, the commit that adds this report.
- **Scope:** spec §1's eight success criteria, for all twelve scenarios. Each has a row naming the tests or runs that show it, its result on this build, and what only people can still check.

## Summary

| # | Criterion | Result |
|---|---|---|
| 1 | 3–6 players play Boarding to Debrief; the room returns to the lobby with results | **Passes** for every scenario, with bot phones on the real TV. A game night with people is still to come |
| 2 | Secrecy enforced by tests | **Passes** |
| 3 | Every rule under test, deterministic for a fixed seed | **Passes** |
| 4 | Every scenario bot-played to an end, and the simulator's targets hold | **Partly.** Every scenario plays to an end. The targets hold for most rows, not all: see the row |
| 5 | A dropped phone never stalls the game | **Passes** |
| 6 | An Explore step changes the TV view by under one packet | **Passes** |
| 7 | A server restart resumes the same game at the turn's start | **Passes** for every scenario, on the real TV too |
| 8 | A thirteenth scenario touches only its own files and one registration line | **Passes**, shown by a test that adds one |

**Full gate** (`bash tools/check-all.sh --full`, 2026-11-20): .NET build, phone tests and build, Node tool tests and Python checks pass. .NET tests: **8,608 of 8,620 passed**; the 12 failures are the Tactics tests that also fail on a clean `main` at `cf156c46`, and nothing in M7 touches Tactics.

## The criteria

### 1. A whole game, and back to the lobby

> A group of 3–6 plays from Boarding to Debrief on the platform, and the room returns to the lobby with the results.

- **Server hub tests** (`MeridianHubTests`, one per scenario): three phones play through the Breach to the Debrief, and the room returns to the lobby with results. The scenarios come from the registry, so all twelve run.
- **On the real TV** (`tools/tv-e2e.sh`, `ARCBOX_GAME=meridian`, Godot 4.7.2 on Xvfb with bot phones): every scenario played from crew pick to the Debrief.
  - The TV signalled every presented event itself (70–126 a game), with no gaps and none abandoned.
  - Tiller spoke the Breach, the scenario and the outcome in order.
- **Still to check, with people:** the game night in `docs/playtest-checklist.md`.

### 2. Secrecy

> No `HostView`, and no `PlayerView` for the wrong player, ever contains a hidden role, a secret objective, a hidden token or a card still in a deck.

- **`SecrecyTests`:** every scenario at 3–6 players, over whole bot-played games. No TV view and no wrong-seat phone view holds a role, objective, token or counter outside its audience. Dropping the role filter in `Visibility` fails it.
- **Spoken lines** (`TillerLinesTests`): no line Tiller says names a secret, for every scenario at every player count.
- **Sounds and effects** (`SoundPlanTests`): each comes from the TV's beats, lands on something the TV shows, and only public decisions count down.
- **Timing** (`ScuttleChargeTimingTests`): planting a charge changes not one byte of the TV's view or any other seat's phone.
- **Restores** (M6 `ResumeTests`): the first views after a restore hold nothing outside their audience.
- **No public trace:** every hidden act is designed to leave none. One of Us Isn't's sabotage and Scuttle Charge's count show only at a round's end, and the TV never receives private objectives or bookkeeping counters.

### 3. Rules under test, and deterministic

> All rules are covered by `GameHarness` tests and are deterministic for a fixed seed.

- `Meridian.Logic.Tests` (342 tests): every rule, every card, the Breach, combat, death and echoes, each scenario's actions, wins and losses, round limits and `CanRun`, and the shared primitives.
- `Meridian.Game.Tests` (235 tests, on `GameHarness`): phases, deadlines, presentation and stale signals, disconnects, bots taking over and handing back, snapshots and restores.
- **Determinism:**
  - In every scenario, a game restored from a mid-endgame snapshot plays on to exactly the original's final state (`SnapshotTests`).
  - The simulator's output is byte-identical for a fixed seed. Moving the first five scenarios onto shared primitives, and the engine fix for hazard deaths, were each checked this way.

### 4. Bot-played, and the simulator's targets

> Every scenario can be played start to finish by bots alone, and the simulator's balance targets hold (§7.3).

- **Bot-played:** each scenario's 200-seed sweep at 3–6 players plays to an outcome with every invariant held after every step. All twelve pass, and every simulated game ended (1,000 per scenario per player count).
- **The targets** ([balance report](2026-11-meridian-endgame-balance.md), 48 rows):

| Target | Holds |
|---|---|
| The crew win 40–60% | 20 of 32 traitor and co-op rows. Misses cluster at 3 players (five scenarios) and in several 5–6-player rows |
| Free-for-all turn position ≤ 1.3× fair | Every row (Salvage Rights, The Last Clean Sample) |
| Round limit decides < 10% | Six scenarios. Four still let most games run to the limit after the owner's second-way-to-win decision (One of Us Isn't, Tiller, Captain on Deck, Cold Sleep). Company Asset and Hull Rot run 10–32% |
| Breach at 35–55% of turns | 38 of 48 rows. The threshold of 7 moved some short-endgame rows late |
| Median game 45–60 minutes | 13 of 48 rows. Most estimate 30–45 minutes |

- **Why this is "partly":** spec §7.3 says the targets "catch a broken scenario; they don't replace a playtest". No scenario is broken: every one plays to an end with both sides able to win. The misses are recorded with their causes and the owner's decisions, and each has a named lever for after the game night. Turning every row green before people have played would tune the game to bots.

### 5. A dropped phone never stalls

> A phone that disconnects never stalls the game: every wait has a deadline, and a bot can play any seat in any role.

- `DisconnectTests`:
  - a dropped active seat is played after the grace;
  - a seat dropped mid-wait is answered by a bot;
  - reconnecting takes the next decision back;
  - everyone dropped still plays to the end.
- `TvFeedTests`: the endgame pause looks the same whoever drops, in every scenario, so a pause can't tell anyone who the traitor is.
- Every role in every scenario has a bot (`ScenarioRegistry.Check` fails a pack otherwise). The bot sweeps play every seat in every role.

### 6. One packet per Explore step

> A step in the Explore phase changes the TV's view by less than one network packet (about 1,420 bytes) as a host patch.

- `HostViewTests.An_explore_step_patches_in_under_one_packet`. Also `HostView_is_under_8_kb_with_every_compartment_placed`.
- Beyond the spec: the endgame keeps the same budgets in every scenario (`The_endgame_view_keeps_its_budgets`). That test found M7's game-over reveal sending private objectives, now kept off the TV.

### 7. Surviving a restart

> A server restart mid-game brings the room back in the same game at the start of the interrupted turn (§5.6).

- **Server restart tests** (`MeridianRestartTests`, one per scenario): the server restarts in the endgame, and the same game plays on to the Debrief.
- **Restores** (`SnapshotTests`, `ResumeTests`): mid-vote, mid-briefing and mid-presentation restores, bots handing seats back, and determinism after a restore, in every scenario.
- **On the real TV:** every scenario with the server restarted early in the endgame. The TV came back to its room, and the same game played on to its end. Every event from the TV's return was signalled, none abandoned.
- **Still to check, with people:** a restart during a game night (playtest checklist).

### 8. A thirteenth scenario

> Adding a thirteenth scenario touches only the scenario's own class, its content file, its tests and one registration line.

- `ThirteenthScenarioTests`: a scenario defined only in the test, with its class, its JSON and one registration line, plays from crew pick to the Debrief. Its tokens and counters show on the TV with their content's looks.
- **The boundary test:** shared code (the adapter, the TV's presentation, the Godot scene, the server and the phone module) may not quote any scenario's id, role, token kind, counter id or vote purpose. It found a leak (One of Us Isn't's vote question in the adapter), since fixed.
- **The simulator** reports by archetype, not scenario id.
- **In practice:** each of M7's seven scenarios touched exactly those files, plus its line in the Breach table's list and a generated demo recording.

## What only people can check

From `docs/playtest-checklist.md`:
- a game night at 3–6 players on the TV;
- a restart during one;
- the M7 lines for each new scenario;
- the balance report's open questions: does the round limit still dominate with people, how long is a real game, and how do three players fare?
