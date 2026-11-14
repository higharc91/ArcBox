> Status: approved 2026-11-14; in progress on branch `ccr-de425e76-4vxzw2`. Live task state belongs on GitHub issues (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M3 Breach Engine Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** Make the game whole. When the Breach fires, the game picks a scenario, deals roles (some secret), briefs each phone, and plays an asymmetric endgame with combat, monsters, death and echoes until a side wins. Then a Debrief reveals everything. Five scenarios ship in M3, one per archetype (spec §4.5, the **S1** rows).

**Architecture:**
- The rules stay pure and stay in `Meridian.Logic`. The endgame adds a scenario board to `GameState`, an `IScenario` contract (spec §4.2) and the shared primitives (spec §4.3). Each scenario is a C# class over those primitives, with its text and numbers in a content file.
- **Secrecy is by construction** (spec §4.4). Every role, token, counter and objective carries an `Audience`. One function, `Visibility.For(state, viewer)`, returns the state as a viewer may see it, and every view (TV, phone, bot) is built only from that.
- `Meridian.Game` grows the Breach sequence, the Endgame flow, the endgame disconnect pause and the Debrief. M2's single clock and session record carry over unchanged in shape.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md), §2.6–§2.9, §3, §4, §6, §7. **Earlier stages:** [M1](2026-11-13-meridian-m1-rules-core.md) and [M2](2026-11-13-meridian-m2-playable-explore.md), including their *Implementation notes*.

**Not in this plan:**
- Scenarios 6–12 (stage M7). The contract and primitives must be able to express them; Task 3 checks that on paper against all twelve.
- The Godot TV scene, art and narration (M4, M5). The dev board stays the board.
- Snapshots and resume (M6). Everything added stays inside `GameState` and `MeridianSession`, which already round-trip through JSON.

## Global constraints

- Everything in M1's and M2's Global constraints still holds: pure rules, the `Rng` in the state, `EqArray` collections, absolute intents, one clock, budgets.
- **No view is built from the raw state.** The TV, every phone and every bot read `Visibility.For(state, viewer)`. A test enforces this structurally (Task 5), not only by sampling.
- **Every scenario has a round limit and a side that wins at it** (spec §2.9), so no endgame can run forever.
- **A scenario's bots are part of the scenario** (spec §4.2). A scenario without a bot for every role it deals can't be registered: the registry test fails.
- **Scenario numbers** (round limits, check targets, counts) live in `content/meridian/base/rules/scenarios/<id>.json` beside the scenario's text, validated by the loader like M1's content. A briefing is at most 120 words (spec §11's risk on briefing length).
- **Hidden information never reaches the TV.** The TV's audience is always "everyone" (spec §4.4).
- **The endgame disconnect pause applies to every living seat**, never only the traitor, so the pause can't reveal who the traitor is (spec §3.3).
- **Development only:** a `Meridian:Scenario` setting forces the scenario the Breach picks, for playtests and the end-to-end tests. Ignored outside Development, as Tactics' seeds are.

## The five S1 scenarios, as built

These pin down spec §4.5's one-line descriptions. Every number is a starting point for Task 10's simulator to tune.

| Scenario | Roles | Setup | Actions and rules | The other side wins | The crew wins |
|---|---|---|---|---|---|
| **The Passenger** (revealed traitor) | The drawer is the **host**; everyone else is crew | Two **brood** (Brawn 4, Agility 3, Nerve 2) in the host's compartment, commanded by the host | *Synthesize* (Tech check, 5+) in the Med Bay, Hydroponics or Containment Lab adds a **dose**. *Inject* the host while sharing a compartment and carrying a dose: Brawn against Brawn, and a win ends it. At each round's end a new brood spawns with the host if there are fewer brood than players | Every crew member is dead, or the round limit (12) | The host is injected |
| **One of Us Isn't** (hidden traitor) | One random seat is secretly the **mimic**, possibly the drawer | Four **repair points** on random placed compartments | *Repair* a point (Tech 4+). The mimic may *Sabotage* a repaired point while alone in its compartment, un-repairing it publicly but anonymously. Once per round anyone in the Sensor Suite (or the Command Landing) may *Call a vote*: every living seat votes in secret. Two votes per game | The round limit (10), or every other crew member dead | All four points repaired at once, or a vote names the mimic |
| **Tiller Says No** (co-op against the ship) | Nobody | Tiller's **core** in the Bridge (or the Command Landing); a **lockdown** counter | *Pull a memory bank* at the core (Tech 4+). At each round's end Tiller vents the compartment holding the most crew: each there makes an Agility check (3+) or loses 1 physical | The round limit (8) | Three banks pulled |
| **Decaying Orbit** (timed escape; also the fallback) | Nobody; every seat wins or loses for itself | An **orbit** counter at 8; **escape pods** (one fewer than the players) in the Escape Pod Bay (or the Airlock) | *Board a pod* in the pod compartment: you escape and win, and leave the board. *Restart the engines* in the Engine Room (or the Engineering Landing): Tech 5+, three successes needed. The orbit counter drops each round | — | Engines restarted: everyone aboard wins. At orbit 0, whoever is aboard dies; those who escaped win |
| **Salvage Rights** (free-for-all) | Everyone is solo | The Breach's Anomaly becomes **the Prize**, carried by the drawer; the carrier's movement is cut by 2 (minimum 1) | Attack the carrier and win to take the Prize. Standing in the Airlock with the Prize at the end of your turn wins | The round limit (10): the ship keeps it, and nobody wins | Whoever carries the Prize out |

**Decaying Orbit is the fallback** (spec §9): it runs at every player count and needs only compartments that the start layout always has, via its "(or …)" alternatives. A test checks that it can always run.

## Review focus

Situations a main-path test would not reach. Each gets a pinned test in the task in brackets.

1. **A hidden role in any view it doesn't belong in.** For every S1 scenario at 3–6 players, over whole bot-played games, no TV view and no wrong-seat phone view contains the role, a secret objective, a hidden counter or a hidden token, and neither does a bot's input. [Tasks 5, 13]
2. **A death mid-wait.** A crew member dies while owing a roll or a trade answer. The wait is cancelled cleanly and play moves on. [Task 2]
3. **The traitor dies.** Their monsters keep acting on the traitor's turns, as an echo commands them, unless the scenario says otherwise. [Task 2, Task 6]
4. **The last crew member and the round limit on the same turn.** The win is decided once, by the scenario's order: an objective completed on the final turn beats the limit. [Task 3]
5. **A vote with a disconnected voter.** The ballot is cast by the bot after the pause, and nobody learns from the timing who has voted. [Tasks 7, 11]
6. **No scenario can run.** The game falls back to Decaying Orbit. [Task 3]
7. **A snapshot taken mid-endgame.** A state with secret roles round-trips through JSON and plays on identically. [Task 1]
8. **The endgame pause for an innocent.** Any living seat that drops gets the same 60 s pause as the traitor would. [Task 11]

---

## File map

```
games/Meridian/Meridian.Logic/
  State/  Endgame.cs  Audience.cs  Actors.cs           scenario board, tokens, counters, objectives, roles, audiences
  Rules/  Rules.Combat.cs  Rules.Death.cs  Rules.Endgame.cs  Visibility.cs
  Scenarios/  IScenario.cs  ScenarioRegistry.cs  BreachTable.cs  Primitives.cs
              ThePassenger.cs  OneOfUsIsnt.cs  TillerSaysNo.cs  DecayingOrbit.cs  SalvageRights.cs
  Bots/   EndgameBot.cs  (shared crew policy; each scenario adds its role policies)
games/Meridian/Meridian.Logic.Tests/
  Endgame/  CombatTests.cs  DeathTests.cs  BreachSelectionTests.cs  ScenarioContractTests.cs  VisibilityTests.cs
  Scenarios/  one file per scenario
games/Meridian/Meridian.Sim/  EndgameReport.cs           (modify Program.cs: an `endgame` command)
games/Meridian/Meridian.Game/
  Briefing.cs  Endgame.cs  Debrief.cs  Views/…           (modify) new screens and host-view fields
games/Meridian/Meridian.Game.Tests/  BriefingTests.cs  EndgameTests.cs  DebriefTests.cs  SecrecyTests.cs
content/meridian/base/rules/
  breach.json                                           Anomaly + compartment → candidate scenarios
  scenarios/{the_passenger,one_of_us_isnt,tiller_says_no,decaying_orbit,salvage_rights}.json
  schema/{breach,scenario}.schema.json
web/play/src/games/meridian/                            (modify) briefing, allocate, monsters, vote, debrief screens
web/play/src/dev/MeridianBoard.tsx                      (modify) tokens, counters, round clock, Debrief
src/ArcBox.Server/Games/Meridian/                       (modify) the Development-only Meridian:Scenario setting
src/ArcBox.Server.Tests/Meridian/MeridianHubTests.cs    (modify) one end-to-end run per S1 scenario
docs/superpowers/reports/2026-11-meridian-endgame-balance.md   written by Task 10
```

## Order and parallelism

Tasks 1 → 2 → 3 → 4 → 5 are a chain: state, combat and death, the scenario contract, actions and hazards, visibility. Tasks 6–9 (the five scenarios) need 1–5 and can run in parallel, each on its own branch, merged in order. Task 10 (balance) needs 6–9. Tasks 11–13 (adapter, phone, TV) need 5 and can start alongside 6–9 against The Passenger alone. Task 14 needs everything.

---

### Task 1: Endgame state

**Files:** create `State/Endgame.cs`, `State/Audience.cs`, `State/Actors.cs`; modify `GameState.cs`, `CrewState.cs` (via `GameState.cs`), `Invariants.cs`; test `Endgame/EndgameStateTests.cs`.

**Interfaces:**

```csharp
public enum Phase { Explore, Breach, Endgame, Over }

/// <summary>Who may see something (spec §4.4).</summary>
public sealed record Audience(AudienceKind Kind, string? Value = null)   // Everyone | Side | Role | Seat
{
    public static Audience Everyone { get; }
    public bool Includes(GameState state, SeatId? viewer);               // null viewer = the TV
}

public sealed record RoleCard(SeatId Seat, string Role, string Side, Audience Audience);
public sealed record Token(string Id, string Kind, CompartmentId? At, SeatId? Carrier, SeatId? Controller,
                           MonsterStats? Stats, bool Stunned, string? State, Audience Audience);
public sealed record MonsterStats(int Brawn, int Agility, int Nerve);
public sealed record Counter(string Id, int Value, Audience Audience);
public sealed record Objective(string Id, string Text, ObjectiveState State, Audience Audience);

public sealed record ScenarioBoard(string Scenario, EqArray<RoleCard> Roles, EqArray<Token> Tokens, EqArray<Counter> Counters,
                                   EqArray<Objective> Objectives, int RoundLimit, int StartRound, Outcome? Outcome);
public sealed record Outcome(EqArray<SeatId> Winners, string Message);

// CrewState gains: bool Alive = true; bool Escaped = false; Stats Stats (revealed, cards, dice, damage dealt), for awards.
// GameState gains: ScenarioBoard? Board.
```

- [ ] **Step 1: Write the failing tests.** Every new record round-trips through JSON with value equality; `Audience.Includes` for each kind, including the TV; the invariants accept a valid endgame state and catch a token in a compartment that doesn't exist, a role for an unknown seat, and a living crew member with a marker at step 0 (Review focus 7: a state with secret roles round-trips and plays on).
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): endgame state and audiences`.

### Task 2: Combat, damage, death and echoes

**Files:** create `Rules/Rules.Combat.cs`, `Rules/Rules.Death.cs`; test `Endgame/CombatTests.cs`, `Endgame/DeathTests.cs`.

**Rules implemented** (spec §2.7, §2.8):
- `Attack(from, target)` against a crew member or a monster in the same compartment, once per turn: Brawn against Brawn. The higher total wins by the difference; a defender's win **staggers** the attacker (the rest of their movement is lost); a tie does nothing.
- **Damage is the defender's choice**, as a new wait, `AllocatePending(seat, group, steps)`: split lost steps between Brawn and Agility (Tech and Nerve for mind attacks). A timeout splits them evenly, higher trait first.
- **Monsters** are tokens with `MonsterStats`. On their controller's turn, each monster may move up to its Agility and attack once (`MonsterMove`, `MonsterAttack` commands from the controller). A monster that would take damage is **stunned** for its next activation instead.
- **After the Breach the floor is 0**: a marker pushed below step 1 kills. A dead crew member drops their items in their compartment as `item` tokens, and stays as an **echo**: on their turn they may *Lend* one living crew member an extra die on that member's next check, and nothing else.
- **A death mid-wait** cancels the wait the dead seat owed and resolves it as a timeout would have (Review focus 2).
- **A dead controller's monsters** still activate on that seat's turns (Review focus 3).

- [ ] **Step 1: Write the failing tests.** Every combat outcome; allocation and its timeout; a stagger; stunned monsters skip one activation; death drops items; echo lending; Review focus 2 and 3.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): combat, damage, death and echoes`.

### Task 3: The scenario contract, the Breach table and selection

**Files:** create `Scenarios/IScenario.cs`, `ScenarioRegistry.cs`, `BreachTable.cs`, `Rules/Rules.Endgame.cs`; content `rules/breach.json` and its schema; test `Endgame/BreachSelectionTests.cs`, `Endgame/ScenarioContractTests.cs`.

**Interfaces** (spec §4.2, with M1's state-held `Rng`):

```csharp
public interface IScenario
{
    ScenarioInfo Info { get; }                                 // id, archetype, player range, round limit, roles dealt
    bool CanRun(RulesPack pack, GameState state, BreachInfo breach);
    GameState Setup(RulesPack pack, GameState state, BreachInfo breach);     // deals roles, places tokens, counters, objectives
    GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events);
    IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat);
    GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events);
    Outcome? Evaluate(RulesPack pack, GameState state);
    IBotPolicy Bot(string role);
}
```

- **Selection** (spec §2.6): `breach.json` maps an Anomaly and the drawn compartment's tile to candidate scenario ids; the game drops those that can't run and picks one with the state's `Rng`. No candidates: any runnable scenario. None at all: Decaying Orbit, which always can (Review focus 6). With no Anomaly (the ship-full Breach), any runnable scenario.
- **Win order** (Review focus 4): `Evaluate` is checked after every command and at each round's end, before the round-limit check. An objective completed on the last turn beats the limit.
- **The registry** refuses a scenario that deals a role with no bot, or whose content file is missing or fails validation.
- **On paper:** a test-only table lists all twelve scenarios of spec §4.5 against the primitives each needs. It fails if any needs a primitive that doesn't exist, so M7 can't discover a missing one late.

- [ ] **Step 1: Write the failing tests.** Selection by table, filtering, fallback and the ship-full case; win order; registry refusals; the twelve-scenario coverage table.
- [ ] **Step 2–4:** fail, implement, pass. Until Task 6, a stub scenario in the tests stands in.
- [ ] **Step 5: Commit** `feat(meridian): scenario contract, Breach table and selection`.

### Task 4: Scenario actions, hazards and votes

**Files:** create `Scenarios/Primitives.cs`; extend `Rules.Endgame.cs`, `Pending.cs`, `Commands.cs`; test `Endgame/PrimitiveTests.cs`.

- **Scenario actions** (spec §4.3): a `ScenarioAction` names an id, a label, where it's allowed, who may take it, and usually a check with outcomes. `Rules.Legal` lists them as `ScenarioAct` commands; `Step` routes them to the scenario's `Act`.
- **Hazards** run at each round's end in a fixed order, through the scenario's `On(RoundEnded)`.
- **Votes** are the first wait owed by more than one seat. `VotePending(Owed, Ballots, Audience)` accepts a ballot from any seat still owed, stores it with that seat's own audience, and resolves when the last is in. `Rules.Legal` lists `CastVote` for every owed seat; `BotRunner.Actor` returns the first owed seat, for bots.
- The phone's `turn` screen gains these actions from `Rules.Legal`, as M2 did for everything else.

- [ ] **Step 1: Write the failing tests.** Actions appear only where and for whom they're allowed; a check-based action resolves through M1's check path; hazards run once per round in order; a vote takes ballots in any order, hides each until resolved, and resolves once.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): scenario actions, hazards and votes`.

### Task 5: Visibility

**Files:** create `Rules/Visibility.cs`; test `Endgame/VisibilityTests.cs`.

- `Visibility.For(state, viewer)` returns a copy of the state with everything outside the viewer's audience removed: other seats' secret roles (shown as an unknown role on the public side), hidden tokens, hidden counters, others' secret objectives, others' ballots, and the draw piles' order. A null viewer is the TV.
- **Bots** get `Visibility.For(state, seat)`, not the state; `IBotPolicy.Choose` gains no other way in. `BotRunner` is changed to pass the visible state.
- **The structural test** (Review focus 1): reflection walks `GameState` and fails if a field carrying secret information has no audience. That catches a new secret field added later without a filter.

- [ ] **Step 1: Write the failing tests.** Each removed thing for each viewer kind; the TV sees nothing secret; a seat sees its own secrets; the structural test.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): visibility by audience`.

### Task 6: The Passenger

**Files:** create `Scenarios/ThePassenger.cs`, content `scenarios/the_passenger.json`; test `Scenarios/ThePassengerTests.cs`.

As in the table above. **Bots:** the host attacks the nearest crew member with brood and stays away from crew carrying a dose; crew synthesize, then converge on the host together.

- [ ] **Step 1: Write the failing tests.** Setup; synthesize, inject and the win; brood spawning; the round limit; every crew member dead; Review focus 3 (a dead host's brood still act); bots play it to an outcome for seeds 1–200 at 3–6 players with invariants after every step.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): The Passenger`.

### Task 7: One of Us Isn't

**Files:** create `Scenarios/OneOfUsIsnt.cs`, content `scenarios/one_of_us_isnt.json`; test `Scenarios/OneOfUsIsntTests.cs`.

As in the table above. **Bots:** crew repair the nearest point and call a vote when a repaired point has been sabotaged, naming the seat most often alone near it. The mimic plays the crew policy, sabotaging only when the rules say nobody saw it (spec §6), and votes like crew.

- [ ] **Step 1: Write the failing tests.** Setup deals one mimic; repair and sabotage; sabotage only when alone; the two-vote limit; a correct and a wrong vote; Review focus 5 at the rules level (ballots are hidden until the vote resolves); bots play it to an outcome for seeds 1–200 at 3–6 players.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): One of Us Isn't`.

### Task 8: Tiller Says No and Decaying Orbit

**Files:** create `Scenarios/TillerSaysNo.cs`, `Scenarios/DecayingOrbit.cs` and their content; test both.

As in the table above. **Bots:** Tiller's crew head for the core and pull banks, leaving a vented room; in Decaying Orbit a bot restarts the engines while the orbit counter allows, and boards a pod when it no longer does.

- [ ] **Step 1: Write the failing tests.** Each rule in the table; Decaying Orbit can run from the start layout alone at every player count (Review focus 6); escaped seats leave the turn order but count as winners; bots play both to an outcome for seeds 1–200 at 3–6 players.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): Tiller Says No and Decaying Orbit`.

### Task 9: Salvage Rights

**Files:** create `Scenarios/SalvageRights.cs` and its content; test it.

As in the table above. **Bots:** the carrier heads for the Airlock by the cheapest path; everyone else heads for the carrier and attacks.

- [ ] **Step 1: Write the failing tests.** Taking the Prize by combat; the carrier's slowed movement; the Airlock win; the round limit; bots to an outcome for seeds 1–200 at 3–6 players.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): Salvage Rights`.

### Task 10: Endgame simulation and the balance report

**Files:** create `Meridian.Sim/EndgameReport.cs`; modify `Program.cs`; write `docs/superpowers/reports/2026-11-meridian-endgame-balance.md`.

- `dotnet run --project games/Meridian/Meridian.Sim -- endgame --scenario <id|all> --games 2000 --players 3-6` plays whole games with the scenario forced, and reports per scenario and player count: the crew's win rate (or each seat position's, in a free-for-all), how often the round limit decided it, rounds after the Breach, deaths, and an estimated length at fixed seconds per turn type.
- The report compares these with spec §7.3's targets: crew wins 40–60%, a free-for-all seat at most 1.3× its fair share, round-limit endings under 10%, and a median game of 45–60 minutes. It also re-measures the Breach timing against the real target (35–55% of a whole game's turns), which M1 could only proxy.
- Tune each scenario's numbers in its content file until its targets hold, or say plainly in the report which can't and why. Numbers from bots, not people.

- [ ] **Step 1: A Sim-category smoke test.**
- [ ] **Step 2: Implement; run; tune; write the report from the real output.**
- [ ] **Step 3: Commit** `feat(meridian): endgame simulator and balance report`.

### Task 11: The adapter: Breach sequence, Endgame and Debrief

**Files:** create `Meridian.Game/Briefing.cs`, `Endgame.cs`, `Debrief.cs`; modify the clock (`Timing.cs`); test `BriefingTests.cs`, `EndgameTests.cs`, `DebriefTests.cs`.

- **The Breach sequence** (spec §3.1): after the Breach is presented, every phone gets a `briefing` of the same shape and length, with **Ready**. It ends when all living seats are ready, or after 90 s. M2's early ending is removed.
- **Endgame:** M2's turn flow, with the endgame commands. The disconnect rule changes for the endgame (spec §3.3): a living seat that drops when it owes a decision gets **one 60 s pause per disconnection**, the same for every seat (Review focus 8), then the role's bot plays it. Echo turns get a short 15 s deadline.
- **Debrief:** the outcome, every role revealed, the full map and three awards from the crew stats (most compartments revealed, most damage dealt, last one standing). It ends after 20 s or when the VIP taps Continue; then `EndGame` with every seat scored 1 for a win and 0 otherwise, winners first (spec §2.9).
- **The Development setting** `Meridian:Scenario` forces the scenario, through `AddMeridian()`.

- [ ] **Step 1: Write the failing tests.** Briefing lengths are equal across roles in a hidden-traitor game; Ready and the 90 s fallback; the pause is the same for an innocent and for the traitor (Review focus 8), and a vote's disconnected voter is botted after it (Review focus 5); Debrief, Continue and the results.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): Breach sequence, Endgame and Debrief in the adapter`.

### Task 12: Phone screens for the endgame

**Files:** modify `Views/Screens.cs`, `Views/Projection.cs`, `web/play/src/games/meridian/*`; fixtures; tests.

- New screens: `briefing` (role, objectives, side rules, Ready), `allocate` (split lost steps), `monsters` (pick a monster, move it, attack), `vote` (a secret choice), `echo` (lend a die), `debrief` (your result and role).
- `turn` gains attack targets and scenario actions; `sheet` gains a Briefing tab that keeps your objectives in reach for the rest of the game (spec §11).
- Every phone screen is built from `Visibility.For(state, seat)`.

- [ ] **Step 1: Generate fixtures** for every new screen and state with `UPDATE_FIXTURES=1`, and read each.
- [ ] **Step 2: Write the Vitest tests**, as M2's: every fixture renders; every button sends exactly its absolute intent; the vote's choice stays on the phone until submitted.
- [ ] **Step 3: Implement; `npm test` and `npm run build`.**
- [ ] **Step 4: Commit** `feat(meridian): endgame phone screens`.

### Task 13: The TV view, the dev board and the secrecy test

**Files:** modify `Views/HostView.cs`, `Projection.cs`, `web/play/src/dev/MeridianBoard.tsx`; test `SecrecyTests.cs`.

- The host view gains the scenario's title, public tokens and counters, the round clock (rounds left), and at Debrief, every role revealed. It is built from `Visibility.For(state, null)`.
- **The secrecy test** (Review focus 1; spec success criterion 2): for each S1 scenario at 3–6 players, play whole games with bots. After every step, serialize the TV view and every phone view, and fail if any contains a role, objective id, token id, counter id or ballot belonging to an audience that excludes that viewer. Bots are checked the same way through what `BotRunner` hands them.
- The view budgets still hold: the full host view under 8 KB with every compartment and the largest scenario's tokens; every step under one packet as a patch; every phone screen under 16 KB.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): endgame TV view and the secrecy test`.

### Task 14: End to end

**Files:** modify `MeridianHubTests.cs`, `tools/meridian-playtest/bots.cjs`, `docs/playtest-checklist.md`.

- **Hub test:** for each S1 scenario, forced through `Meridian:Scenario`, three phones and a TV play from room creation through Explore, the Breach, briefings and the endgame to Debrief and back to the lobby. Integration category.
- **Bot phones** learn the new screens; `--scenario=<id>` documents how to force one on a Development server.
- **Checklist:** read a briefing; play a hidden-traitor game with people; call a vote; drop a phone in the endgame and see the same pause whoever it is; read the Debrief.
- Run `bash tools/check-all.sh --full`; expect only the Tactics failures already on `main`, unless they've been fixed by then.

- [ ] **Step 1: Write the hub tests; make them pass.**
- [ ] **Step 2: Update the bot script; run each scenario against a local server and the dev page.**
- [ ] **Step 3: Run the full gate.**
- [ ] **Step 4: Commit** `test(meridian): end-to-end games through the Breach`.

---

## Done when

- A group of 3–6 plays any S1 scenario from crew pick to Debrief on `/dev.html`, and the room returns to the lobby with the results (spec success criterion 1, for the first five scenarios).
- The secrecy test passes for all five scenarios at every player count (spec success criterion 2).
- Every S1 scenario is played to an outcome by bots alone, and the balance report states where its targets hold (spec success criterion 4).
- A phone that drops never stalls the endgame, and its pause is the same whoever it is (spec success criterion 5).
- `bash tools/check-all.sh --full` passes, apart from any Tactics failures already on `main`.
