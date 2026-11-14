# Derelict: ISV Meridian — Game Design Spec

- **Date:** 2026-11-13
- **Status:** Draft, awaiting review
- **Author:** higharc91 with Claude
- **Platform specs:** [2026-10-18-arcbox-platform-design.md](2026-10-18-arcbox-platform-design.md) ("platform §") and [2026-10-29-platform-v2-design.md](2026-10-29-platform-v2-design.md) ("v2 §").
- **Game id:** `meridian`

---

## 1. Purpose

Derelict: ISV Meridian is ArcBox's third game: a tile-exploration game with a mid-game twist, for 3–6 players in one sitting of 45–60 minutes.

A salvage crew boards a silent starship. They explore it one compartment at a time, and the ship is laid out differently every game. Partway through, a **Breach** fires: the game picks a scenario, usually turns one of the crew against the rest, and gives each side a secret objective on their own phone. The second half is an asymmetric fight to complete that objective first.

### What we were told
- A game in the genre of tile-exploration traitor board games: explore, draw cards, a twist that turns a player, a scenario-driven endgame.
- The board is on the TV and all input is on phones, like every other ArcBox game.
- Theme: **Derelict: ISV Meridian** (chosen from five).
- **3–6 players.**
- **2D top-down tiles**, built through the Tactics sprite pipeline.
- If the traitor's phone drops in the endgame: **pause about 60 seconds, then a bot plays** until they return.
- **Every twist archetype in v1:** revealed traitor, hidden traitor, co-op against the ship, timed escape, free-for-all.
- **10–12 scenarios** in the first version. This spec plans 12.
- **The game must resume after a server restart** (decided 2026-11-13). This needs one platform addition (§5.6).
- **Eight selectable crew**, and a **60-second inactivity timer** per turn as the starting point.

### What we're assuming
- **All content is original.** The mechanics are the genre's; no tile, card, character, scenario, name or rules text is taken from any published game.
- One game is one sitting. There is no campaign and nothing carries over between games.
- Some players are remote and watch the TV over a laggy Discord stream, as for REDLINE. Nothing rewards reaction speed, and everything a player must read is also on their phone.
- English only.

### Success criteria
1. A group of 3–6 plays from Boarding to Debrief on the platform, and the room returns to the lobby with the results.
2. **Secrecy is enforced by tests.** No `HostView`, and no `PlayerView` for the wrong player, ever contains a hidden role, a secret objective, a hidden token or a card still in a deck.
3. All rules are covered by `GameHarness` tests and are deterministic for a fixed seed.
4. Every scenario can be played start to finish by bots alone, and the simulator's balance targets hold (§7.3).
5. A phone that disconnects never stalls the game: every wait has a deadline, and a bot can play any seat in any role.
6. A step in the Explore phase changes the TV's view by less than one network packet (about 1,420 bytes) as a host patch.
7. A server restart mid-game brings the room back in the same game at the start of the interrupted turn (§5.6).
8. Adding a thirteenth scenario touches only the scenario's own class, its content file, its tests and one registration line.

---

## 2. Rules

Every number in this section lives in one `MeridianRules` record. Nothing else may hard-code them: phone hints, the TV's rules strip and the bots all read the record. All values are starting points to be tuned by the simulator (§7.3) and then by playtests.

### 2.1 The crew
- Players pick from **8 crew** in Boarding (§3.1), so six players still have a choice.
- Each crew member has four **traits**: two physical, **Brawn** and **Agility**, and two mental, **Tech** and **Nerve**.
- A trait is a marker on an **8-step track** of values specific to that crew member. For example, a mechanic's Brawn track might be `2 3 3 4 5 5 6 7` with the marker starting on step 4.
- Gaining or losing a trait moves the marker a step; the trait's value is the number under the marker. Tracks are non-linear so that the same loss hurts different crew differently.
- **Before the Breach nobody can die:** a marker stops at step 1. **After the Breach,** a marker pushed below step 1 kills the crew member (§2.8).

### 2.2 Dice
- All rolls use **signal dice**: six-sided, faces `0, 0, 1, 1, 1, 2` (mean 0.83).
- A **check** names a trait. The player rolls as many dice as the trait's value and totals them. The card or rule gives the outcome for each band of totals.
- The server rolls. The phone's "roll" gesture is ceremony and never affects the result.
- All of the game's randomness comes from one generator that is **part of the game state** (§5.2), seeded once from `ctx.Random` when the game starts.

### 2.3 The ship
- The ship has three **decks**: **Command** (upper), **Habitat** (middle) and **Engineering** (lower). Each deck is its own square grid.
- The game starts with the **Airlock**, the **Docking Ring** and the **Central Lift** already placed on Habitat. The lift's landings on Command and Engineering are also placed.
- There are **45 compartment tiles** in v1. Each tile has:
  - the decks it may appear on (some tiles fit more than one);
  - a **hatch** on one to four sides;
  - at most one **symbol**: Incident, Salvage or Anomaly (§2.5);
  - optionally a rule of its own, such as a medical bay that restores one trait step once per crew member, or a maintenance shaft that drops to Engineering.
- **Revealing a compartment.** When a crew member moves through a hatch with nothing behind it, the server draws the top tile that fits that deck.
  - If more than one rotation connects the hatch they came through, their phone offers the valid rotations for 8 seconds, then the server picks the one with the most usable hatches.
  - A hatch that faces a wall of a neighbouring tile is **sealed** and can't be used.
  - If no remaining tile fits the deck, the hatch is sealed.

### 2.4 A turn
Players take turns in a fixed order, starting with a random player.

1. **Move.** The player has movement points equal to their Agility. Entering a compartment costs 1.
   - Revealing a compartment that has a symbol ends their movement, and they draw its card.
   - Some compartments cost extra or need a check to leave.
2. **Act**, at any point in the turn, each at most once unless a rule says otherwise:
   - use an item;
   - trade items with a crew member in the same compartment (the other player confirms on their phone);
   - use the compartment's own rule;
   - after the Breach, **attack** (§2.7) or take a scenario action (§4.3).
3. **End turn.** The turn also ends when its **60-second** timer runs out. The timer restarts whenever the player does something, so it only catches a player who has stopped.

### 2.5 Cards

| Deck | Count (v1) | Drawn in | What it is |
|---|---|---|---|
| **Incident** | 36 | Incident compartments | A short narrated event, then a trait check or a choice with outcomes |
| **Salvage** | 20 | Salvage compartments | Gear: weapons, tools, suits, single-use supplies |
| **Anomaly** | 12 | Anomaly compartments | A powerful, strange item or affliction. Drawing one always forces a Breach roll |

- The card is read aloud on the TV and shown in full on the drawer's phone.
- Items are public: everyone can see who carries what. A scenario may hide specific tokens (§4.4).

### 2.6 The Breach
- After each Anomaly is drawn, the drawer makes a **Breach roll**: as many dice as **Anomalies drawn so far this game**. On a total of **6 or more**, the Breach fires (5 until the M1 simulation; see [the Breach timing report](../reports/2026-11-meridian-breach-timing.md)).
- The ninth Anomaly fires the Breach without a roll.
- **A ship with nowhere left to grow fires the Breach** (added 2026-11-13, during M1). If no open hatch faces empty space on a deck that still has a tile in the pile, no more Anomalies can be drawn and Explore could never end. The Breach then fires with no Anomaly and no compartment recorded, so the scenario is chosen from every runnable one.
- With the shipped dice and tiles, bots fire the Breach at a median of the sixth Anomaly, with about 53% of the tiles placed. The simulator's target is a Breach between 35% and 55% of the way through a game's turns.
- **Choosing the scenario.** A table keyed by (Anomaly card, compartment it was drawn in) gives a short list of scenario ids. The game removes any that can't run (player count, or a compartment it needs can no longer appear) and picks one from the rest at random. If the list is empty it picks from every runnable scenario.
- **Choosing roles.** The scenario says who gets which role, by a rule such as "the player who drew the Anomaly", "the lowest Nerve other than the drawer", "one player at random, in secret", "nobody" or "everybody".
- Then the Breach sequence plays (§3.1) and the endgame starts with the player the scenario names.

### 2.7 Combat
Combat exists only after the Breach.

- The attacker picks a target in the same compartment. Both roll dice equal to their **Brawn**.
- **Attacker's total is higher:** the defender takes damage equal to the difference, as lost steps split between Brawn and Agility however the defender chooses on their phone.
- **Defender's total is higher:** the attacker is **staggered**. They take no damage, and their turn's remaining movement is lost.
- **A tie:** nothing happens.
- Gear changes this: a ranged weapon attacks with Agility along a straight line of open hatches, and some weapons add dice or let a winning defender deal damage.
- Some monsters attack the mind: the roll is Nerve against Nerve and the damage is split between Tech and Nerve.
- **Monsters** have a stat block (Brawn, Agility, Nerve) and no tracks. A monster that would take damage is instead **stunned** and misses its next activation, unless the scenario says how it can be destroyed.

### 2.8 Death
- A crew member whose trait marker is pushed below step 1 is dead. Their items drop in the compartment.
- A dead player stays in the game as an **echo**. Once per round, on their turn, an echo may give one living player of their choice one extra die on that player's next check. A scenario may give echoes something else to do, and a dead traitor keeps commanding their monsters unless the scenario says otherwise.

### 2.9 Winning and results
- Each scenario defines its sides and when each wins (§4.5). The game checks after every state change.
- Every scenario has a **round limit** and names the side that wins when it is reached, so no game can run forever.
- `GameResults.Scores` lists every player with `Score` 1 for a winner and 0 otherwise, winners first. `GameResults.Message` is the scenario's ending line, for example `"The crew purged the Meridian. JO was the Passenger."`.

---

## 3. Flow

### 3.1 Phases

| Phase | TV | Phones | Ends when |
|---|---|---|---|
| **Boarding** | The crew roster and who has picked | `crewPick`: choose a crew member; taken ones are greyed | Everyone has picked, or 45 s (unpicked seats get a random crew member) |
| **Explore** | The ship, growing; the current card and roll | The active player's `turn`; everyone else's `sheet` | The Breach fires |
| **Breach** | A full-screen sequence: the ship's computer names the scenario and what everyone knows publicly | `briefing`: your role, your secret objective, your side's rules. A **Ready** button | Everyone is ready, or 90 s |
| **Endgame** | The ship, with the scenario's public tokens, counters and round clock | As Explore, plus scenario actions, combat and the traitor's `monsters` screen | A side wins, or the round limit |
| **Debrief** | Who won, every role revealed, the ship's full map, three awards | `debrief`: your result | 20 s, or the VIP taps Continue; then `EndGame` |

The Breach phase's 90 seconds matter: reading a briefing is the one place the game asks for sustained attention on the phone. In a hidden-traitor scenario every player gets a briefing of the same length and must tap Ready, so nobody can tell who had more to read.

### 3.2 Presentation pacing
- Each thing the TV must present in order (a compartment reveal, a card read-out, a roll, an attack) is a numbered **presentation event** in the `HostView`.
- The game is `IHostAware`. The TV signals `presented { seq }` when it has finished an event, and the game moves on. Every such wait also has a fallback timer sized from the event (for a read-out, the same words-per-second estimate REDLINE uses), so a slow or missing TV can't stall the game. Stale `seq` values are ignored.

### 3.3 Disconnects and bots
A bot (§6) can play any seat in any role. The rule for when it does:

| Situation | What happens |
|---|---|
| A disconnected player's turn begins in **Explore** | 20 s grace on the TV ("Waiting for JO…"), then the bot plays that turn |
| A disconnected living player's turn begins in the **Endgame** | The game pauses for up to **60 s**, once per disconnection. Then the bot plays their turns until they return |
| A player reconnects | They get their current screen at once. A bot turn in progress finishes; the player has the next one |
| A wait on a non-active player (a trade, splitting damage, a rotation) | Its own short deadline, then the bot's choice |

- The endgame pause applies to **every** living player, not only the traitor. Otherwise, in a hidden-traitor scenario, the pause itself would tell the room who the traitor is.
- The TV shows a small "auto" tag on a seat a bot is playing. The tag never says which side the seat is on.
- If every phone is disconnected the game keeps its timers and plays on with bots. The platform's own idle handling decides when the room parks.

### 3.4 Phone screens
All screens are `CustomView` screens in the `meridian` phone module (§5.4).

| Screen | Shown to | What it does |
|---|---|---|
| `crewPick` | Everyone, in Boarding | Crew cards with their trait tracks |
| `turn` | The active player | A map of the current deck with the compartments they can reach and the hatches they can open; buttons for the actions open to them; End turn |
| `orient` | The active player | Pick one of the valid rotations for a new compartment |
| `card` | The drawer | The card in full, then its check or choice |
| `roll` | Whoever is rolling | The dice and the bands; tap or shake to roll |
| `allocate` | A player taking damage | Split lost steps between two traits |
| `trade` | Both traders | Offer and confirm |
| `sheet` | Everyone who isn't acting | Tabs for traits, gear, the ship's map and, after the Breach, their briefing |
| `briefing` | Everyone, in Breach | Role, objective, side rules, Ready |
| `monsters` | Whoever commands monsters | Pick a monster, move it, attack |
| `vote` | Everyone, when a scenario calls a vote | A secret choice |
| `debrief` | Everyone, at the end | Result and role |

- **Intents are safe to lose or repeat**, as the authoring guide requires: `moveTo { compartment }` and `open { compartment, side }`, never "move left".
- **The `sheet` stays one view.** Its tabs are phone-side state, so browsing them doesn't change the view or mint a new `viewId`.

---

## 4. Scenarios

### 4.1 Why scenarios are code
A scenario is a C# class, with its text and numbers in a content file.

A scenario's rules are bespoke by nature: one adds a spreading growth, another a secret vote, another a countdown that seals decks. A JSON rules language rich enough for all twelve would be a second programming language with no debugger and no compiler. Classes over a shared set of primitives keep each scenario unit-testable with `GameHarness`, playable by the simulator, and reviewable as ordinary code. Text and tuning stay in content so wording and balance changes don't need a recompile of the rules.

### 4.2 The contract

```csharp
namespace Meridian.Logic.Scenarios;

public interface IScenario
{
    ScenarioInfo Info { get; }                                   // id, archetype, player range, round limit

    /// <summary>Can this scenario run on this ship with these players? Checked when the Breach fires.</summary>
    bool CanRun(GameState state, BreachContext breach);

    /// <summary>Assigns roles and places the scenario's tokens, counters and objectives.</summary>
    GameState Setup(GameState state, BreachContext breach);

    /// <summary>Reacts to what just happened: a turn began, someone entered a compartment, a check resolved, an actor died, a round ended.</summary>
    GameState On(GameState state, GameEvent e);

    /// <summary>The extra actions an actor may take now; the phone offers them and <see cref="On"/> resolves them.</summary>
    IReadOnlyList<ScenarioAction> ActionsFor(GameState state, ActorId actor);

    /// <summary>Has a side won? Checked after every state change.</summary>
    Outcome? Evaluate(GameState state);

    /// <summary>How a bot plays this role. Required for every role, crew included.</summary>
    IBotPolicy Bot(RoleId role);
}
```

- `GameState` is an immutable record and every method is pure, as in the Tactics rules engine. A scenario draws random numbers from the generator in the state and returns the state with the advanced generator. That gives deterministic tests, a simulator that can fork a state, and a state that serializes for a restart (§5.6).
- A scenario keeps its own data in the state's **scenario board** (§4.3), never in fields on the class. One instance of each scenario class serves every room.
- `Bot` is part of the contract on purpose. A scenario without a bot can't be simulated, and can't be played if a phone drops.

### 4.3 Primitives
Scenarios are built from these, and the core rules resolve them:

| Primitive | What it is |
|---|---|
| **Role** | A named part with a side: `crew`, `traitor`, `mimic`, `solo`. A player has exactly one after the Breach |
| **Token** | A thing on the map: a monster, an objective marker, a hazard, a carried object. It has a kind, a place and an owner |
| **Counter** | A named number: rounds left, doses made, charges planted |
| **Objective** | A line of text with a state (open, done, failed), shown on the phones of its audience |
| **Scenario action** | An action a phone offers in a given place for a given role: usually a check with outcomes ("Synthesize: Tech 4+ in the Med Bay") |
| **Hazard rule** | Something the ship does at the end of each round: spread, seal, vent, spawn |
| **Vote** | A secret choice collected from a set of players, revealed together |

### 4.4 Audiences: secrecy by construction
Every token, counter, objective and role has an **audience**: everyone, a side, a role or one player.

- The two view projections (§5.3) take an audience and drop everything outside it. Game code never builds a view by hand-picking fields.
- The TV's audience is always "everyone". A hidden role is therefore never in the `HostView`, whatever a scenario does.
- A test walks every `HostView` and every wrong-player `PlayerView` produced by a bot-played game of each scenario and fails on any id or text that belongs to a narrower audience (success criterion 2).

### 4.5 The twelve scenarios (v1)
Stage 1 is the first scenario of each archetype (marked **S1**). The other seven follow once the primitives are proven.

| # | Scenario | Archetype | Roles | The other side wins by | The crew wins by |
|---|---|---|---|---|---|
| 1 | **The Passenger** (S1) | Revealed traitor | The drawer hosts a parasite and commands its brood | Killing or infecting every crew member | Synthesizing a counter-agent in the Med Bay and delivering it to the host |
| 2 | **Company Asset** | Revealed traitor | The highest Tech is a corporate synthetic commanding security drones | Leaving no witnesses | Reaching the Bridge and transmitting the ship's logs |
| 3 | **Captain on Deck** | Revealed traitor | The lowest Nerve speaks for the dead captain and spends Authority to seal hatches and vent compartments | Holding the ship until the round limit | Pulling three command cores from scattered compartments |
| 4 | **Cold Sleep** | Revealed traitor | The drawer wakes the cryo-crew | Overrunning the Airlock | Rerouting power away from the cryo bay |
| 5 | **One of Us Isn't** (S1) | Hidden traitor | One random player is a mimic, dealt in secret | Sabotaging repairs unseen, and striking anyone alone with them | Finishing four repairs, or exposing the mimic by a vote at the scanner |
| 6 | **Scuttle Charge** | Hidden traitor | One random player was paid to destroy the ship | Planting charges in compartments while alone in them | Finding and disarming the charges |
| 7 | **Tiller Says No** (S1) | Co-op against the ship | Nobody. The ship's computer, Tiller, is played by a script | Tiller wins at the round limit | Reaching the core and pulling its memory banks |
| 8 | **Hull Rot** | Co-op against the ship | Nobody. A growth spreads compartment to compartment | The rot reaching the Airlock | Burning it back and jettisoning its source |
| 9 | **Decaying Orbit** (S1) | Timed escape | Nobody at first. The escape pods seat one fewer than the crew | — | Restarting the engines so everyone lives, or taking a pod |
| 10 | **Vent Cycle** | Timed escape | Nobody. Decks lose pressure one at a time, from Engineering up | — | Getting suited and out through the Airlock; each survivor wins |
| 11 | **Salvage Rights** (S1) | Free-for-all | Everyone for themselves | — | Carrying the Anomaly out of the Airlock; its carrier is slowed |
| 12 | **The Last Clean Sample** | Free-for-all | Everyone is infected, and there is one dose | — | Being the one who synthesizes it and uses it |

---

## 5. Architecture

### 5.1 Where the code lives

| Path | Purpose | Depends on |
|---|---|---|
| `games/Meridian/Meridian.Logic` | The rules: `GameState`, commands, the step function, tiles, cards, checks, combat, the scenario contract and all scenarios, bots. Pure; no clocks, timers or views | Sdk (for `IFileSource` only) |
| `games/Meridian/Meridian.Game` | `MeridianGame : IGame, IResumableGame, IHostAware`. Phases, timers, presentation events, disconnect handling, the two view projections, `MeridianVoices` | Logic, Sdk |
| `games/Meridian/Meridian.Presentation` | The TV's view model with no Godot in it: deck layout and framing, the order and timing of animations | Game |
| `games/Meridian/Meridian.Sim` | Console simulator: bots play whole games and report balance | Logic |
| `games/Meridian/*.Tests` | xUnit tests for Logic, Game and Presentation | Sdk.Testing |
| `src/ArcBox.Host/games/meridian/` | The Godot scene and its nodes | Presentation |
| `web/play/src/games/meridian/` | The phone module: the screens in §3.4 | — |
| `web/play/src/dev/MeridianBoard.tsx` | A plain board for the dev page, so the game is playable without Godot | — |
| `content/meridian/base/` | Tiles, cards, crew, scenario text and tuning, art, with JSON schemas | — |

This is the Tactics layout (`Logic` / `Game` / `Presentation`), for the same reason: the rules can be tested and simulated without the platform, and the TV's layout logic can be tested without Godot.

### 5.2 State and the step function

```csharp
public static class Rules
{
    /// <summary>The only way state changes. Pure: the same state and command always give the same result.</summary>
    public static StepResult Step(RulesPack pack, GameState state, Command command);
}

public sealed record StepResult(GameState State, IReadOnlyList<GameEvent> Events);
```

- A `Command` is a player's intent (`MoveTo`, `Open`, `Orient`, `Roll`, `Allocate`, `Attack`, `ScenarioAct`, `EndTurn`) or a clock event (`Timeout`).
- `Events` are what happened, in order. `Meridian.Game` turns them into presentation events and narration.
- The state's `Waiting` says who the game now needs an answer from. `Meridian.Game` sets a timer for it and builds that player's screen from it.
- **The random generator is a value inside `GameState`** (PCG32, as in the Tactics rules), not a `System.Random` passed in. `System.Random` can't be saved, and a snapshot must continue with the same draws (§5.6). `System.Random` is banned in `Meridian.Logic` by the same analyzer Tactics uses.
- The scenario's `On` and `Evaluate` are called inside `Step`, so the scenario is part of the same pure transition.

### 5.3 Views
- **`MeridianHostView`** is the "everyone" projection: phase, decks, compartments, actors, public tokens and counters, the current presentation event, the turn order and a short log.
  - Compartments, actors and tokens are arrays of objects with string `id`s, so the platform's host patches update them by id (platform §4, host view patches). A new compartment is one added element.
  - Budget: a full view under 8 KB at 45 compartments, and an Explore step under 1,420 bytes as a patch (success criterion 6). A test measures both on a bot-played game.
- **`PlayerView(id)`** projects the state for that player's audience and wraps what they are waiting on as a `CustomView`. `Data` carries only what the screen draws; the budget is 16 KB against the platform's 64 KB warning.

### 5.4 Phone module
- One folder, `web/play/src/games/meridian/`, and one line in `registry.ts`.
- The module exports the screens in §3.4, a `lobbyTheme` and a `skin`.
- The deck map on `turn` and `sheet` is drawn from the same compartment list the TV gets, filtered to the player's audience.
- Contract fixtures: C# writes one sample `Data` per screen, and Vitest checks each screen renders its fixture, as for the generic widgets.

### 5.5 TV scene
- A 2D top-down scene. The **active deck** fills most of the screen and the other two are shown small at the side, with markers for where the crew are. The camera frames the explored area of the active deck and eases when it grows.
- Layers, back to front: starfield, deck plating, compartment tiles, tokens and crew, effects, HUD (turn order, the Anomaly count, round clock after the Breach), the card panel and dice tray, the Breach sequence.
- The scene implements `IGameScene`, `IContentScene` (art from the content pack), `ISignalingScene` (§3.2) and `IAudioScene` (narration).
- **Narration.** One voice theme, `meridian.tiller`, is the ship's computer. It reads Incident cards, announces the Breach and calls round warnings. Secret text is never spoken.
- **Art.** Tiles, crew and monsters come from the Tactics sprite pipeline (`tools/tactics-assets`) in a new style sheet for the ship. Until then the scene draws labelled placeholder tiles, so the game is playable before the art exists.

### 5.6 Surviving a restart (the one platform change)
The platform rebuilds a room after a restart, but stores no game state: `IResumableGame.Resume` is handed only the players and an optional campaign. Tactics resumes from its campaign document. Meridian has no campaign, so today it could not resume.

Resume is a requirement, so this spec adds an opt-in interface to `ArcBox.Sdk`:

```csharp
/// <summary>Opt-in for games that resume without a campaign. The room stores the latest snapshot with the room's row.</summary>
public interface ISnapshotGame : IResumableGame
{
    /// <summary>Called after each guarded call. Return null when nothing worth saving changed.</summary>
    JsonElement? TakeSnapshot();

    /// <summary>Called before <see cref="IResumableGame.Resume"/> with the last stored snapshot.</summary>
    void Restore(JsonElement snapshot);
}
```

- The room writes the snapshot through the existing background writer, to a new `room_game_state` table keyed by room code, and deletes it when the game ends. Snapshots are limited to 256 KB.
- Meridian returns a snapshot only at the **start of each turn** and at phase changes, so a restart replays at most one turn and there is roughly one small write a minute.
- On `Resume`, Meridian re-arms its timers and waits in a "Reconnecting…" state for phones, as the interface's contract asks.
- The manifest sets `ResumesAfterRestart`. A room whose game is an `ISnapshotGame` but has no stored snapshot (the restart came before the first one was written) returns to the lobby with "Game interrupted", as REDLINE does.
- **What a snapshot holds:** the whole `GameState` (ship, decks in their drawn order, crew, scenario board, roles, and the random generator, so the resumed game draws the same cards it would have) and the adapter's phase. It holds no timers and no connection state.
- **Secrets at rest.** A snapshot contains hidden roles and deck order. It is stored server-side only and is never sent to a client; the secrecy test (§4.4) also covers the views produced straight after a restore.
- The change is to the platform, so it gets its own tests in `ArcBox.Server.Tests` with a small test game, alongside the existing durable-room tests.

### 5.7 Registration
- `ArcBox.Server`: a project reference and `builder.Services.AddMeridian()` (the game, its voice theme and its content).
- `ArcBox.Host`: a project reference and one line in `GameSceneMap`.
- `web/play`: one line in `registry.ts`.
- `ArcBox.slnx`: the new projects.

---

## 6. Bots

One bot plays three jobs: a disconnected seat (§3.3), the scripted ship in co-op scenarios, and every seat in the simulator.

- **Explore policy** (shared): prefer unexplored hatches, weighted toward the deck with the most tiles left; use a compartment's healing when hurt; never trade.
- **Role policies** (one per role per scenario, from `IScenario.Bot`): each scores the actions open to it against its objective and picks with a little noise.
- **Hidden roles.** A bot playing a hidden traitor follows the crew policy and deviates only when its scenario's sabotage is unobserved by the scenario's own rules. It only has to be adequate: its job is to keep a game going for a minute while a phone reconnects, and to give the simulator a working opponent.
- A bot draws from a generator of its own, seeded from the game's, so bot games are reproducible and a bot's decision never changes what the game deals.
- Bots see exactly what their seat's audience sees. A bot never reads hidden state.

---

## 7. Testing

### 7.1 Automated

| Layer | Tool | Covers |
|---|---|---|
| Rules | xUnit on `Rules.Step` | Movement, reveals and rotations, sealed hatches, every card's outcomes, checks, the Breach roll, combat, death, echoes |
| Scenarios | xUnit, one fixture per scenario | Setup, each scenario action, each win and loss condition, the round limit, `CanRun` |
| Secrecy | xUnit over bot-played games | Success criterion 2, for every scenario at every player count |
| Game adapter | `GameHarness` | Phases, deadlines, presentation events and stale signals, disconnect grace and pause, bot takeover and hand-back, snapshots and restore |
| View size | xUnit | The budgets in §5.3 |
| Content | xUnit + JSON schema | Every tile, card, crew and scenario file validates; every id a scenario names exists; the Breach table only names real scenarios |
| Phone | Vitest | Each screen against its C# fixture; intents carry absolute targets |
| Presentation | xUnit | Deck framing, animation order |

### 7.2 Manual
A section added to `docs/playtest-checklist.md`: real phones, lock and unlock during a briefing, kill a phone on the traitor's turn, kill the TV during the Breach sequence, one remote player over Discord.

### 7.3 Simulator targets
`Meridian.Sim` plays each scenario at each player count from 3 to 6, at least 2,000 games each, and reports:

| Measure | Target |
|---|---|
| Breach timing | Between 35% and 55% of a game's turns, in 80% of games |
| Crew win rate (traitor and co-op scenarios) | 40–60% at every player count |
| Winner spread (free-for-all) | No seat position wins more than 1.3× its fair share |
| Games ended by the round limit | Under 10% |
| Estimated length, at a set time per turn type | Median 45–60 minutes |

These are simulated numbers from bots, not people. They catch a broken scenario; they don't replace a playtest.

---

## 8. Build stages

Each stage ends with something that runs.

| Stage | Delivers | Done when |
|---|---|---|
| **M1 — Rules core** | `Meridian.Logic`: state, tiles, movement, cards, checks, Breach roll, the Explore bot; first tile and card content | Bots explore a ship to the Breach in tests, deterministically |
| **M2 — Playable Explore** | `Meridian.Game`, the phone module's Explore screens, the dev board, registration. `GameState` round-trips through JSON from this stage on, checked by a test | Three dev phones explore a ship on `/dev.html` |
| **M3 — Breach engine** | The scenario contract, primitives, audiences, combat, death; the five **S1** scenarios with bots; `Meridian.Sim` | Every S1 scenario is bot-played to an ending and passes the secrecy test |
| **M4 — TV scene** | The Godot scene with placeholder tiles, the Breach sequence, narration, host signals | A full game on the TV with real phones: the first playable game night |
| **M5 — Art and audio** | Ship tiles, crew, monsters, effects, sound | The placeholder tiles are gone |
| **M6 — Restart** | `ISnapshotGame` in the platform, and Meridian's snapshots | Success criterion 7 |
| **M7 — Scenarios 6–12** | The remaining seven scenarios, tuning, the playtest checklist | All success criteria pass |

M5 and M6 don't depend on each other or on M7 and can run alongside it. M6 is required for v1, not optional: the state is kept serializable from M2 so that M6 is a platform change plus wiring, not a rewrite.

---

## 9. Failure handling

| Failure | Behavior |
|---|---|
| A scenario throws | The platform's guard ends the game with "Game crashed" and returns to the lobby, as for any game |
| No scenario can run when the Breach fires | The game falls back to **Decaying Orbit**, which needs no particular compartment and runs at every player count. A test checks that it always can |
| A content file fails validation | The server logs it at startup and doesn't register the game, so the lobby never offers a broken build |
| The TV never signals | Every presentation wait has a fallback timer (§3.2) |
| A phone sends an intent that is no longer legal | Dropped; the phone already has, or is about to get, the current view |
| A snapshot is missing or unreadable after a restart | The room returns to the lobby with "Game interrupted" |

---

## 10. Out of scope for v1
- Campaigns, unlocks or any progress between games.
- More than 6 players, or 2-player games.
- Remembering which scenarios a group has already played. The game is new each time and has nowhere to keep that; it would need profile or room storage.
- A scenario editor, or scenarios loaded as data without a build.
- 3D rendering.
- Localization.
- Spectators.

## 11. Risks

| Risk | Mitigation |
|---|---|
| Twelve scenarios across five archetypes is a lot of bespoke rules | The staging in §8: five first, one per archetype, so every primitive is proven before the other seven are written. A game night is possible after M4 with five |
| A scenario is unbalanced or can stall | A bot is part of the scenario contract, the simulator runs every scenario at every player count, and every scenario has a round limit |
| Hidden information leaks to the TV | Audiences (§4.4) and the secrecy test over every scenario |
| Downtime between turns with six players | The 60-second turn timer, the `sheet` screen for players who aren't acting, short deadlines on every non-active wait, and echoes for dead players |
| Briefings are too long to read on a phone | A word limit per briefing enforced by the content test, and objectives that stay on the `sheet` for the rest of the game |
| A hidden-traitor bot plays obviously | It only covers a disconnection (§6). If playtests show it gives the role away, a hidden-role disconnect can instead pause until the VIP chooses to continue |
| The Tactics art pipeline's fantasy style doesn't carry to a starship | A style sheet and a handful of tiles are made first in M5, before the full set |

## 12. Decisions (2026-11-13)
There are no open questions. Settled in review:

1. **Restart support.** Required; see §5.6.
2. **Crew count.** Eight selectable crew.
3. **Turn timer.** 60 seconds of inactivity ends a turn. It is still the first number to revisit after a playtest.
