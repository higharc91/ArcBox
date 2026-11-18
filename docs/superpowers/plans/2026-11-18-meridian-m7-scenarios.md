> Status: approved 2026-11-18; in progress on branch `ccr-de425e76-4vxzw2` (tasks #408–#419). Live task state belongs on GitHub issues (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M7 Scenarios 6–12 Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** Ship the seven scenarios that complete v1 (spec §4.5). They are Company Asset, Captain on Deck, Cold Sleep, Scuttle Charge, Hull Rot, Vent Cycle and The Last Clean Sample. Tune all twelve against the simulator's targets, and finish the playtest checklist. **M7 is the last build stage. It is done when every success criterion in spec §1 passes.**

**Architecture:**
- **Nothing new in the contract.** M3's `IScenario`, primitives and audiences were checked on paper against all twelve scenarios (M3, Task 3). The seven are classes over the same contract, each with its own content file and bots, exactly as the S1 five are.
- **A scenario's look moves into its content first** (Task 1). Today four Presentation files name the S1 scenarios' token kinds and counters: the counter labels, token glyphs, monster kinds and the art contract's token list. So a new scenario would touch TV code, which breaks success criterion 8 ("a thirteenth scenario touches only its class, its content file, its tests and one registration line"). Each scenario's JSON will say how its tokens and counters look, and the TV reads that.
- **Primitives grow by extraction** (Task 2). Five things the seven need are already written inside S1 scenarios: venting (Tiller), monster spawning (The Passenger), a carried token taken by combat (Salvage Rights), "alone in a compartment" (One of Us Isn't) and a delayed reveal. They move into `Primitives` with the S1 scenarios using them, behaviour unchanged. The genuinely new ones are a scenario sealing and unsealing a hatch, spread along open hatches, and a hidden token revealed to everyone.
- **Every scenario list comes from the registry.** The secrecy sweeps, beat timing, Tiller's lines, fixtures, demo recordings, the hub tests and the simulator iterate `ScenarioRegistry.Default`, so each new scenario is covered by every existing sweep the moment it's registered.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md), §1 (success criteria), §2.9, §4, §6, §7, §8. **Earlier stages:** [M3](2026-11-13-meridian-m3-breach-engine.md) (the contract and the S1 five), the [endgame balance report](../reports/2026-11-meridian-endgame-balance.md), and [M4](2026-11-17-meridian-m4-tv-scene.md), [M5](2026-11-17-meridian-m5-art-and-audio.md) and [M6](2026-11-17-meridian-m6-restart.md), including their *Implementation notes*.

**Not in this plan:**
- **The art for the new tokens.** One `art` issue extends #336 with the new kinds. Until it lands they draw as labelled glyphs from their content (Task 1).
- **The TV lobby's title card** (#405), a platform change.
- **New phone screens.** Scenario actions, targets, votes, briefings and objectives already render generically. A scenario that needs a new kind of screen would be a design change, not M7.

## Global constraints

- **Everything in M1–M6's Global constraints still holds:**
  - pure rules, with the `Rng` in the state;
  - views only from `Visibility.For`;
  - every scenario has a round limit and a side that wins at it;
  - bots are part of the scenario;
  - numbers live in content;
  - briefings are at most 120 words, and announcements are public;
  - effects and sounds come only from public beats;
  - the state is serializable, so snapshots cover new scenarios for free.
- **Criterion 8 is enforced by a test** (Task 1). Outside `Meridian.Logic/Scenarios/`, the content and the tests, no code may name a scenario id, role, token kind or counter id. The one exception is the registration line.
- **Each new scenario is checked by every sweep:**
  - the secrecy test (spec criterion 2) at 3–6 players;
  - Tiller's lines and the sound and effect sweep;
  - beat timing;
  - snapshot round-trip and determinism after a restore (M6);
  - the 8 KB TV view budget and the one-packet explore patch.
- **Hidden roles stay hidden by timing too.** As in One of Us Isn't, a hidden traitor's act has no immediate public trace that turn order could pin on them. Anything public is revealed at the round's end.

## The seven scenarios, as designed

These pin down spec §4.5's one-line descriptions. Every number is a starting point for Task 10's simulator to tune. **Bold** marks a rule this plan adds to the spec's line; each is listed under *Decisions for the owner*.

| Scenario | Roles | Setup | Actions and rules | The other side wins | The crew wins |
|---|---|---|---|---|---|
| **Company Asset** (revealed traitor) | The highest Tech is the **synthetic** (ties: the drawer, then turn order); everyone else is crew | Security **drones** (Brawn 3, Agility 4, Nerve 3; faster and weaker than brood), players − 2 of them (at least 1), in the Drone Bay if it's placed, else with the synthetic. The synthetic commands them. A new drone each round's end while there are fewer than players − 1 | *Transmit* the logs in the Bridge or the Comms Array: Tech 5+. **Two transmissions are needed (three at 5–6 players).** **A failed transmission alerts the drones: each moves one step toward the transmitter** | Every crew member dead, or the round limit (12) | The logs transmitted |
| **Captain on Deck** (revealed traitor) | The lowest Nerve speaks for the dead **captain** (ties: the drawer); crew | Three **command cores** on random placed compartments, none of them start tiles, at least two steps apart. **Authority** starts at 2, is public, and gains 1 at each round's end, up to 4 | The captain may spend Authority on their turn. *Seal* a hatch of their compartment costs 1: one action per open hatch, and the sealed hatch blocks movement both ways. *Vent* their compartment or an adjacent one costs 2: each crew member there makes an Agility check (3+) or loses a physical step (Tiller's vent). Crew: *Pull a core* (Brawn 4+). ***Cut through* a sealed hatch from either side (Brawn 5+) unseals it** | The round limit (10), or every crew member dead | Three cores pulled |
| **Cold Sleep** (revealed traitor) | The drawer is the **waker**; crew | **CanRun:** the Cryo Bay is placed or still in the pile. **Sleepers** (Brawn 3, Agility 2, Nerve 1) wake in the Cryo Bay, or with the waker until it's found. Three power **relays** on placed Engineering compartments (Power Relay, Reactor Core and Engine Room first) | At each round's end, one sleeper wakes per relay still powered. The waker commands them. Crew *Reroute* a relay (Tech 4+). When every relay is rerouted, no more sleepers wake | **Sleepers in the Airlock at a round's end: players − 1 (at least 2)**, or every crew member dead, **or the round limit (12)** | Every relay rerouted **and every awake sleeper destroyed or stunned at a round's end** |
| **Scuttle Charge** (hidden traitor) | One random seat is secretly the **saboteur**, possibly the drawer; crew | The saboteur carries **charges** (a counter only they see): players + 1 | The saboteur may *Plant* a charge while alone in a compartment with none. It's a hidden token only they see. A public **armed** count, which names no places, updates at each round's end. Crew *Search* a compartment (Nerve 4+): any charge there becomes public. *Disarm* a found charge (Tech 4+). Crew may attack each other as always | **Armed charges at a round's end: 3 (4 at 5–6 players)** | **The round limit (10)**, or the saboteur dead |
| **Hull Rot** (co-op against the ship) | Nobody: every seat is crew | The rot's **source** on a random placed compartment, at least three steps from the Airlock; its compartment starts rotted | At each round's end the rot spreads to **one adjacent compartment per rotted compartment, along open hatches (rng)**, and to the source's neighbours first. **Ending a turn in rot costs a Nerve check (3+) or a mental step.** *Burn* the rot from your compartment (Tech 4+), but not the source's while the source is there. *Cut the source free* when its compartment is the only rotted one within two steps (Brawn 5+): you carry it, slowed by 1. *Jettison* it in the Airlock | The rot reaches the Airlock, or the round limit (12) | The source jettisoned |
| **Vent Cycle** (timed escape) | Nobody at first; every seat wins or loses for itself (as Decaying Orbit) | **Suits**, one fewer than the players, in the Suit Lockers if placed, else the Docking Ring. A **cycle** counter: the decks vent one at a time, Engineering, then Habitat, then Command, **every 3 rounds** | *Suit up* where the suits are. *Cycle out* in the Airlock while suited: you escape and win. **On a vented deck, ending your turn unsuited costs a physical step.** When Command vents, everyone left aboard is lost. **A hit in combat takes the loser's suit** (Salvage Rights' rule) | — | Each seat that cycles out |
| **The Last Clean Sample** (free-for-all) | Everyone is **infected** and plays for themselves | The **sample** in a lab (Med Bay, Hydroponics, Containment Lab or Isolation Ward), placed or the first one found. **Infection**: a public per-seat counter from 0 | At each round's end every seat's infection rises by 1; **at 6 that seat dies.** *Synthesize* at the sample (Tech 5+): you carry the single **dose**, slowed by 1. *Inject* yourself while carrying it: **you win at the start of your next turn if you still carry it.** A hit in combat takes the dose, which also cancels an injection | — | The seat whose injection takes. If everyone dies, nobody wins |

**Decaying Orbit stays the fallback.** Every new scenario's `CanRun` is tested at every player count on a start-layout-only ship and on a full ship.

### Decisions for the owner

These go past spec §4.5's one-liners. Each is a starting point, and the simulator may move the numbers. The rules themselves are the owner's call before Task 3.

1. **Company Asset:** transmissions needed (2, or 3 at 5–6 players), and drones alerted by a failed transmission.
2. **Captain on Deck:** crew can cut through a sealed hatch (Brawn 5+). Without it, a captain could wall a core off for good.
3. **Cold Sleep:**
   - the waker wins by numbers in the Airlock;
   - the waker also wins at the limit, so the crew can't turtle in the Airlock;
   - the crew must also clear the awake sleepers, so rerouting isn't an instant win.
4. **Scuttle Charge:**
   - the armed count is public, but not where the charges are;
   - the saboteur wins by count at a round's end;
   - the crew win at the limit, like the captain's side in reverse. This makes it the one hidden-traitor scenario where the traitor must act, so the round limit decides only when the saboteur fails.
5. **Hull Rot:** the spread rate, the spore cost of ending a turn in rot, and "cut free" needing the source's surroundings clean.
6. **Vent Cycle:** three rounds per deck, the unsuited cost, and suits taken in combat.
7. **The Last Clean Sample:**
   - the infection death at 6;
   - the injection pays off at your next turn, so the table gets a round to take the dose back. An instant injection would make the highest Tech win.

## Review focus

Situations a main-path test would not reach. Each gets a pinned test in the task in brackets.

1. **Criterion 8, for real.** A thirteenth scenario is added in a test from only its class, its content file and one registration line. It runs, briefs, shows its tokens and counters on the TV with their content labels, and is bot-played to an ending. [Task 1]
2. **A hidden act traced by timing.** In Scuttle Charge, nothing public changes on the turn a charge is planted: not a counter, a notice, a sound, an effect or the TV view's bytes. [Task 6]
3. **A sealed hatch that cuts the ship in two.** A core, a relay, the Airlock or the suits become unreachable by sealing. The crew can always cut through, and the bots do. [Tasks 2, 4]
4. **Rot or venting on the compartment a seat is waiting in.** A crew member dies of rot or vacuum while owing a roll, a trade or a vote. The wait is cancelled cleanly, as in M3's review focus 2. [Tasks 7, 8]
5. **A carried token whose carrier dies or escapes.** The source, a suit or the dose drops in the compartment, or leaves with an escaping seat in a suit's case. It is never lost. [Tasks 2, 8, 9]
6. **Two wins at once.** The last relay is rerouted on the round the Airlock is overrun, or an injection takes on the turn the infection kills its seat. The order is fixed and pinned: a completed objective beats a loss at the same moment, as in M3. [Tasks 5, 9]
7. **Restart mid-hazard.** A snapshot taken in each new scenario, mid-endgame, restores and plays on identically (M6's determinism test, swept). [Task 1, then each scenario]

---

## Tasks

### Task 1: Scenarios own their look; every list comes from the registry

**Files:**
- Modify `ScenarioDef` and the scenario schema, and the five S1 content files.
- Modify `HudModel.CounterLabel`, `BoardStyle.Token`, `CrewMotion.MonsterKinds` and `ArtContract.TokenKinds`.
- Modify the tests that list the five scenarios: `BeatTimingTests`, `TillerLinesTests`, `ResumeTests` and `PhoneFixtureTests`, plus the Sim.
- Create `Meridian.Logic.Tests/Scenarios/ThirteenthScenarioTests.cs` and a boundary test.

- **Content:**
  - `"tokens": { "<kind>": { "label": "B", "colour": "#ff3b3b", "monster": true, "states": { "fixed": { "label": "✓", "colour": "#5fe08a" } } } }`;
  - `"counters": { "<id>": "Memory banks pulled" | null }`, where null means not shown on the TV;
  - `"counterPrefixes"` for per-seat counters (`suspect-`, `infection-`), labelled with the seat's name.
- The TV and the art check read them from the pack. A token kind no scenario declares is a content error. The art contract's token ids become the union of the scenarios' kinds.
- **The boundary test** scans `Meridian.Game`, `Meridian.Presentation`, `src/ArcBox.Host/games/meridian` and the phone module for any registered scenario's id, role ids, token kinds and counter ids, and fails on a hit. The registration line is allowed.
- **The thirteenth scenario** is a test-only class with its own JSON in a temp pack. It goes through the Breach, the briefings and the TV's HUD model, and is bot-played to an ending (review focus 1).

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** fail, implement, pass. The S1 five look exactly as before; the fixtures and recordings don't change.
- [ ] **Step 5: Commit** `refactor(meridian): scenarios own their look; every list from the registry`.

### Task 2: Primitives the seven need

**Files:** modify `Scenarios/Primitives.cs`, `Rules/Rules.Move.cs` (sealing), `ThePassenger.cs`, `TillerSaysNo.cs`, `SalvageRights.cs` and `OneOfUsIsnt.cs`; test `Scenarios/PrimitivesTests.cs`.

- **Extracted, with the S1 scenarios moved onto them and their tests unchanged:**
  - `Vent(state, compartment, save)`;
  - `Spawn(state, kind, stats, at, controller)`;
  - `Carried` tokens: drop on death or escape, slow the carrier, and pass on a combat hit (from Salvage Rights' prize);
  - `Alone(state, seat)`.
- **New:**
  - `Seal(state, compartment, side)` and `Unseal`, which both emit `HatchSealed` / a new `HatchUnsealed`. The TV already presents `sealed`, and the unseal gets a beat.
  - `Spread(state, from, count)` along open, unsealed hatches;
  - `Reveal(state, tokenId)`, which widens a hidden token's audience to everyone, with an event;
  - `Distance(state, a, b)` over open hatches.
- **`ScenarioNotice` gains an optional effect kind** from the art contract's `fx` list (`vent`, `rot`, `pressure`, `sparks`). The TV's `EffectPlan` and `SoundPlan` play it on the notice's compartment. This is how `vent` and `rot`, unused since M5, get cued.

- [ ] **Step 1: Write the failing tests.** One for each primitive. Sealing blocks movement and monsters both ways, and unsealing restores it. Spread never crosses a sealed hatch, and dropped carried tokens are never lost (review focus 5).
- [ ] **Steps 2–4:** fail, implement, pass. The S1 balance numbers are unchanged: Sim output is identical for a fixed seed.
- [ ] **Step 5: Commit** `refactor(meridian): shared primitives for the remaining scenarios`.

### Tasks 3–9: one scenario each

**Files, per scenario:** create `Scenarios/<Name>.cs` and `content/meridian/base/rules/scenarios/<id>.json`, with its title, announcement, roles, briefings, objectives, numbers, tokens and counters. Test `Scenarios/<Name>Tests.cs`. Register it with one line.

The rules are as in the table above. **Bots** follow the S1 pattern: score the open actions against the role's objective, with a little noise, and use only what the seat may see.

- **Task 3: Company Asset.** The synthetic sends drones at whoever is nearest a transmitter. Crew converge on the nearest transmitter and transmit.
- **Task 4: Captain on Deck.** The captain seals the hatch on the crew's shortest path to the nearest unpulled core, and vents when two or more crew share a compartment. Crew head for cores and cut through seals on their path (review focus 3).
- **Task 5: Cold Sleep.** The waker sends sleepers to the Airlock by the shortest path and attacks crew who block it. Crew split between rerouting relays and holding the Airlock.
- **Task 6: Scuttle Charge.** The saboteur plays the crew policy and plants only when the rules say nobody saw it (spec §6). Crew search compartments they haven't searched yet, nearest first, and disarm what they find.
- **Task 7: Hull Rot.** Crew burn the rot nearest the Airlock first, then head for the source.
- **Task 8: Vent Cycle.** A bot suits up while suits remain and cycles out. A bot with no suit hunts a suited seat while the decks allow.
- **Task 9: The Last Clean Sample.** Bots race to the sample. The dose carrier injects at once if nobody shares its compartment, else runs. Everyone else hunts the carrier.

- [ ] **Step 1, per scenario: write the failing tests.**
  - setup and `CanRun`, at every player count, on a start-only ship and a full one;
  - each action, rule, win and loss, and the round limit;
  - the review-focus items in brackets above;
  - bots play it to an outcome for seeds 1–200 at 3–6 players, with invariants after every step.
- **Steps 2–4:** fail, implement, pass. The registry sweeps (secrecy, lines, sound and effects, timing, snapshots, view budgets) cover it as soon as it's registered.
- **Step 5:** commit `feat(meridian): <Scenario>`.

### Task 10: The Breach table for twelve, and the balance report

**Files:** modify `content/meridian/base/rules/breach.json` and the Sim; rewrite `docs/superpowers/reports/2026-11-meridian-endgame-balance.md`.

- **The table:** each of the twelve Anomalies leads to three scenarios, and each scenario appears three times, across at least two archetypes. A content test checks the even spread, and that every row has a scenario that can always run.
- **The simulator:** run `endgame --scenario all --games 2000 --players 3-6` for all twelve. Tune each new scenario's numbers until spec §7.3's targets hold, or say plainly which can't and why, as M3 did.
  - The M3 report's open findings get a second look, with the owner's decision on round-limit scenarios kept: crew win rates outside 40–60%, and most games estimating 24–44 minutes against a 45–60-minute target.
  - The length target applies to the whole game, so if every scenario is short, the fix may be the Explore phase's length (the Breach threshold, or tiles per deck). That would be proposed in the report, not changed silently.

- [ ] **Step 1: The table and its content test.**
- [ ] **Step 2: Run, tune, and write the report from the real output.**
- [ ] **Step 3: Commit** `feat(meridian): the Breach table for twelve scenarios, and the balance report`.

### Task 11: End to end, on the TV and the phones

**Files:**
- Modify `tools/tv-e2e.sh` (a loop over every scenario).
- Add the seven demo recordings (`src/ArcBox.Host/games/meridian/Demo/`).
- Add to `docs/playtest-checklist.md` and the TV review.

- `tools/tv-e2e.sh` with `ARCBOX_GAME=meridian` and `ARCBOX_SCENARIO=all` runs every scenario through the real TV and bot phones, then repeats with a restart mid-endgame. Every event must be signalled by the TV itself.
- **Seven new demo recordings,** made by the existing recorder. TV screenshots at 1080p and 720p go into the TV review for each new scenario's tokens, notices and Debrief, and the readability rules are checked again.
- **One `art` issue** for the new token kinds (drones, sleepers, cores, relays, charges, rot, the source, suits, the sample and the dose), extending #336 to the contract.
- **The playtest checklist** gains a line per new scenario, the things only people can judge: whether the saboteur can be read from the table's talk, and whether the captain's seals feel fair.

- [ ] **Step 1: Run each end to end; fix what breaks.**
- [ ] **Step 2: Recordings, screenshots, the art issue and the checklist.**
- [ ] **Step 3: Commit** `test(meridian): every scenario end to end`.

### Task 12: The success criteria, audited

**Files:** create `docs/superpowers/reports/2026-11-meridian-v1-criteria.md`.

Spec §1's eight criteria, one row each. For each: the test or run that shows it, its result on the final commit, and anything a person must still check (the game nights). Criterion 4's row cites Task 10's report. The full gate runs last: `bash tools/check-all.sh --full` passes, apart from any Tactics failures already on `main`.

- [ ] **Step 1: The audit and the full gate.**
- [ ] **Step 2: Commit** `docs(meridian): v1 success criteria audited`.

## Addendum: the owner's decisions on the balance report (2026-11-18)

After Task 10's report, the owner decided three things. They become Task 10b, which comes before Tasks 11 and 12:

1. **A second loss path, not an exemption.** Where a side wins only by holding out to the limit, it gets an active way
   to win too:
   - **One of Us Isn't:** the mimic strikes. Its bot attacks a crew member it's alone with, and the mimic wins when at
     most one other crew member is left aboard.
   - **Tiller Says No:** lethal vents. A failed save by someone already hurt (a physical trait below its starting step)
     kills.
   - **Captain on Deck:** lethal vents, as for Tiller, and the captain wins when at most one crew member is left aboard.
   - **Scuttle Charge:** the crew win as soon as the saboteur can no longer reach the target, counting both the armed
     charges and those still in the kit.
   - **Cold Sleep and Hull Rot** already have one: the Airlock overrun, and the rot reaching the Airlock. Their
     limit-decided share is tuned, not given a new rule.
2. **Vent Cycle's suits are scattered:** each lies on a random placed compartment at least three steps from the Airlock
   (else the farthest there is), so fetching one costs turns while the decks vent.
3. **The Breach threshold is 7** (was 6), so the Explore phase runs about a round longer in every scenario, within the
   Breach-timing target. The balance report is rerun afterwards.

## Order and parallelism

- Tasks 1 and 2 come first, in that order, because every scenario relies on both.
- Tasks 3–9 are independent of each other and can run in any order. They're listed archetype by archetype, so each pattern is reused while it's fresh.
- Task 10 needs all seven, and Tasks 11 and 12 come last.

## Done when

- Twelve scenarios are registered, each bot-played to an outcome at 3–6 players. They pass the secrecy test, Tiller's lines, the sound and effect sweep, beat timing and the restart determinism test.
- Each simulator target from spec §7.3 holds for each scenario, or the balance report says plainly why not. Owner decisions are recorded.
- Every scenario plays end to end on the real TV with bot phones, including a restart mid-endgame.
- A thirteenth scenario can be added through its own files and one registration line, as shown by the test.
- The criteria audit shows all eight success criteria passing, and the full gate passes, apart from any Tactics failures already on `main`.
