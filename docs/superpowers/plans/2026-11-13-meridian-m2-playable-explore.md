> Status: draft plan, awaiting review; no task has started. Live task state belongs on GitHub issues (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M2 Playable Explore Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** Put M1's rules on the platform, so a room can select Meridian and three or more phones can explore a ship from Boarding to the Breach on `/dev.html`, with bots covering anyone who drops.

**Architecture:**
- `Meridian.Game` is the `IGame` adapter. It owns everything with a clock, a connection or a view: phases, timers, presentation pacing, disconnects and bots, and the two view projections. Every state change still goes through `Rules.Step`; the adapter never edits `GameState` itself.
- The adapter's own state is one serializable record, `MeridianSession`, next to the `GameState`. Stage M6 snapshots both, so nothing the adapter needs may live only in fields or timers.
- Phones get `CustomView` screens from a `meridian` phone module. The dev page gets a plain `MeridianBoard` standing in for the TV until stage M4.

**Tech stack:** .NET 10 / C#, xUnit v3 with `GameHarness`; Preact + TypeScript + Vitest in `web/play`; the server's real SignalR hub for the integration test.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md), §3 and §5. **M1:** [rules-core plan](2026-11-13-meridian-m1-rules-core.md), including its *Implementation notes*. **Platform:** `docs/authoring-a-game.md` (custom phone screens, host signals, content packs).

**Not in this plan:**
- The Godot TV scene and narration (M4). Until then the TV shows the platform's missing-scene card; the dev board is the board.
- Scenarios, roles, combat and the endgame (M3). In M2 the Breach **ends the game** with a results message that says the scenario engine arrives in M3.
- Snapshots and resume (M6). The manifest leaves `ResumesAfterRestart` off; a restart returns the room to the lobby, as for REDLINE.
- Lobby theme art (#335). M2 uses the platform's default lobby look.

## Global constraints

- **Projects.** `Meridian.Game` targets `$(ArcSdkTargetFrameworks)` and references `Meridian.Logic` and `ArcBox.Sdk` only. `Meridian.Game.Tests` copies `Meridian.Logic.Tests.csproj`. Both go under `/games/Meridian/` in `ArcBox.slnx`.
- **The adapter is a translator.** It turns phone input and timers into `Command`s for `Rules.Step`, and `GameEvent`s into views. A rule that isn't in `Meridian.Logic` doesn't exist.
- **Determinism.** The game seeds its generators once, from `ctx.Random`, in `Start`. Everything after comes from the `Rng` in `GameState` or the bot `Rng` in `MeridianSession`. No `DateTime.Now`; time is `ctx.Now` and timers.
- **Timers carry a sequence number** in their key (`wait:17`, `turn:17`, `present:41`, `bot:9`). `OnTimer` ignores a key whose number isn't current, because `SetTimer` replaces by key and a fired timer can belong to a wait that already ended.
- **Intents are absolute** (`moveTo c12`, `open c4 e`, `orient 2`), as the authoring guide requires. A rejected intent changes nothing and is logged at Debug.
- **Deadlines** (all in `MeridianRules`, M1 has the first two): rotation 8 s; turn inactivity 60 s, restarted by every accepted command from the active seat; roll 20 s; choice 30 s; trade answer 20 s; Boarding 45 s; Explore disconnect grace 20 s; bot step 0.8 s, so a bot's turn is watchable.
- **View budgets:** a full `HostView` under 8 KB with 45 compartments placed; one Explore step under 1,420 bytes as a host patch; any phone screen's `Data` under 16 KB.
- **Wire names.** Screens and actions are camelCase (`crewPick`, `moveTo`). Ids and enums use M1's JSON: `"c12"`, `"habitat"`, `["habitat", 1, 1]`.
- **Commits.** `feat(meridian): …`, one per task, each closing its issue.

## Review focus

Situations a main-path test would not reach. Each gets a pinned test in the task in brackets.

1. **A stale timer.** A wait's deadline fires after the wait was answered. Nothing happens. [Task 2]
2. **A double tap.** The same intent arrives twice in one view. The first applies; the second is rejected and changes nothing. [Task 2]
3. **The active seat drops mid-wait.** Its rotation, roll or choice is answered by the bot once the grace runs out, and the turn carries on with the bot. [Task 4]
4. **A player comes back mid-bot-turn.** The bot's current command finishes; the player gets the next decision, not the next turn. [Task 4]
5. **Everyone drops.** The game keeps playing at bot pace to the Breach, and the room's own idle rules decide what happens to it. [Task 4]
6. **The TV never signals.** Every presentation wait ends on its fallback timer, and the game reaches the Breach with no TV at all. [Task 3]
7. **A stale signal.** A `presented` signal for an old `seq` is ignored. [Task 3]
8. **The Breach in M2.** The game ends cleanly with `GameResults`, every phone leaves its screen, and the room is back in the lobby. [Task 2]
9. **A pack that doesn't load.** The server still starts; the first Meridian room fails with every content error listed. [Task 8]

---

## File map

```
ArcBox.slnx                                               (modify) Meridian.Game and its tests
games/Meridian/Meridian.Game/
  Meridian.Game.csproj  MeridianGame.cs  MeridianSession.cs  Intents.cs  Timing.cs
  Presentation.cs  Seats.cs  Views/HostView.cs  Views/Screens.cs  Views/Projection.cs
games/Meridian/Meridian.Game.Tests/
  Support/Harness.cs  BoardingTests.cs  ExploreTests.cs  TimerTests.cs  PresentationTests.cs
  DisconnectTests.cs  HostViewTests.cs  ScreenTests.cs  PhoneFixtureTests.cs  SessionJsonTests.cs
games/Meridian/Meridian.Logic/Core/MeridianRules.cs        (modify) the remaining deadlines
src/ArcBox.Server/ArcBox.Server.csproj                     (modify) reference Meridian.Game
src/ArcBox.Server/Program.cs                               (modify) builder.Services.AddMeridian()
src/ArcBox.Server/Games/Meridian/
  MeridianServiceCollectionExtensions.cs  MeridianWarmup.cs
src/ArcBox.Server.Tests/Meridian/
  AddMeridianTests.cs  MeridianHubTests.cs
web/play/src/games/registry.ts                             (modify) meridian: () => import("./meridian")
web/play/src/games/meridian/
  index.ts  types.ts  DeckMap.tsx  screens.tsx  meridian.css
web/play/src/dev/MeridianBoard.tsx                         (new)  the stand-in TV board
web/play/src/dev/HostPanel.tsx                             (modify) show MeridianBoard for "meridian"
web/play/test/meridian.test.tsx  web/play/test/meridianBoard.test.tsx
web/play/test/fixtures/meridian/*.json                     written by PhoneFixtureTests
tools/meridian-playtest/bots.cjs                           bot phones over the real hub
docs/playtest-checklist.md                                 (modify) a Meridian section
```

## Order and parallelism

Tasks 1 → 2 → 3 → 4 are a chain. Task 5 (views) can start after Task 2. Task 6 (fixtures) needs 5. Task 7 (phone module) needs 6. Task 8 (server) needs 1 and can go alongside 3–7. Task 9 (dev board) needs 5 and 8. Task 10 needs everything.

---

### Task 1: `Meridian.Game`, the manifest, seats and Boarding

**Files:** create the project, `MeridianGame.cs`, `MeridianSession.cs`, `Seats.cs`, `Timing.cs`; `Meridian.Game.Tests` with `Support/Harness.cs` and `BoardingTests.cs`; modify `ArcBox.slnx` and `MeridianRules.cs`.

**Interfaces:**

```csharp
namespace Meridian.Game;

public sealed class MeridianGame(Lazy<RulesPack> pack) : IGame, IHostAware
{
    public const string Id = "meridian";
    public GameManifest Manifest { get; } = new(Id, "Derelict: ISV Meridian",
        "Explore a silent starship. Something aboard is waiting for the Breach.", MinPlayers: 3, MaxPlayers: 6);
    // Start, OnInput, OnTimer, OnPlayerConnectionChanged, HostView, PlayerView, OnHostSignal
}

/// <summary>Everything the adapter knows besides the rules state. Serializable, for stage M6's snapshots.</summary>
public sealed record MeridianSession(
    SessionPhase Phase,                     // Boarding, Explore, Over
    EqArray<SeatInfo> Seats,                // in join order: player id, name, crew (null until picked), connected, auto
    GameState? Game,                        // null during Boarding
    Rng BotRng,
    int Seq,                                // timer and presentation numbering
    EqArray<Presented> Queue,               // presentation events not yet shown (Task 3)
    EqArray<string> Log);                   // the last 8 lines, for the TV

public sealed record SeatInfo(PlayerId Player, string Name, CrewId? Crew, bool Connected, bool Auto);
```

- A seat's `SeatId` is its `PlayerId` string, so no lookup table is needed.
- **Boarding** (spec §3.1): everyone gets `crewPick`; a pick is `pick { crew }`. A crew member already taken is rejected. When all have picked, or after 45 s, unpicked seats get a random free crew member from the bot generator, and `Rules.NewGame` runs with seats in join order.
- `Start` takes the pack's `.Value`. A pack that failed to load throws there, which the room turns into "Game crashed" with the error logged (Task 8 tests the server side).

- [ ] **Step 1: Write the failing tests.** `Manifest_seats_three_to_six`; `Everyone_picks_from_the_crew_roster`; `A_taken_crew_member_is_rejected`; `The_last_pick_starts_the_game`; `Boarding_timeout_assigns_free_crew_and_starts`; `Seats_take_turns_in_join_order`.
- [ ] **Step 2–4:** fail, implement, pass. `Support/Harness.cs` wraps `GameHarness<MeridianGame>` with `Send(player, action, data)` and the test pack from `Meridian.Logic.Tests` (link the files rather than copy them).
- [ ] **Step 5: Commit** `feat(meridian): game adapter, seats and Boarding`.

### Task 2: Explore: intents, deadlines and the M2 ending

**Files:** create `Intents.cs`; extend `MeridianGame.cs`, `Timing.cs`; test `ExploreTests.cs`, `TimerTests.cs`.

**Rules implemented:**
- **Intents** map `CustomInput` to commands: `moveTo { to }`, `open { at, side }`, `orient { rotation }`, `roll`, `choose { option }`, `useItem { item }`, `useRoom`, `offerTrade { to, give, take }`, `answerTrade { accept }`, `endTurn`. An unknown action or malformed data is dropped at Debug; it never throws.
- After every `Step`, the adapter re-arms **one deadline**: the current wait's (rotation 8 s, roll 20 s, choice 30 s, trade 20 s) or, with nothing pending, the active seat's 60 s inactivity timer. Firing sends `Timeout(seat)`.
- **The M2 ending:** when the phase becomes `Breach`, the game ends with every seat scored 0 and the message `"The Breach fired in <compartment> after <n> Anomalies. Scenarios arrive in stage M3."` (or `"…the ship had nowhere left to explore…"` for the no-Anomaly Breach).

- [ ] **Step 1: Write the failing tests.** One per intent; `A_rejected_intent_changes_nothing`; `A_double_tap_applies_once` (Review focus 2); `Each_wait_has_its_deadline`; `Inactivity_restarts_on_every_accepted_command`; `A_stale_timer_does_nothing` (Review focus 1); `The_breach_ends_the_game_with_a_message` (Review focus 8).
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): explore intents, deadlines and the M2 ending`.

### Task 3: Presentation pacing and host signals

**Files:** create `Presentation.cs`; test `PresentationTests.cs`.

**Rules implemented** (spec §3.2):
- Some events are **presented** one at a time on the TV: a compartment reveal, a card draw, a roll, a sealed hatch, the Breach. Each gets the next `seq` and a fallback duration: reveal 1.2 s, card 1 s plus 60 ms a word of its text, roll 1.5 s, sealed hatch 0.8 s, Breach 4 s. Moves, trait changes and turn changes are not presented; they just update the board.
- While anything is queued, phones show a `watch` wait ("On the main screen…") and input is held. The front of the queue is in the `HostView`. `OnHostSignal("presented", { seq })` for the front item, or its fallback timer, pops it. When the queue empties, the waiting player's screen comes back and their deadline starts.
- A signal for any other `seq` is ignored (Review focus 7). A signal never extends a wait.

- [ ] **Step 1: Write the failing tests.** `A_reveal_is_presented_before_the_next_choice`; `The_signal_ends_the_wait_early`; `The_fallback_ends_it_without_a_tv` (Review focus 6); `A_stale_signal_is_ignored`; `Deadlines_start_after_the_queue_empties`; `A_whole_game_reaches_the_breach_with_no_tv` (bots, via `GameHarness.Advance`).
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): presentation pacing and host signals`.

### Task 4: Disconnects and bots

**Files:** extend `Seats.cs`, `MeridianGame.cs`; test `DisconnectTests.cs`.

**Rules implemented** (spec §3.3, Explore rows only; the Endgame pause is M3):
- A disconnected seat that **owes something** (its turn, or a rotation, roll, choice or trade answer) gets a 20 s grace, then `Auto = true` and the `ExploreBot` plays it, one command per bot step (0.8 s).
- `Auto` clears when the seat reconnects. The bot's command in flight finishes; the next decision is the player's (Review focus 4).
- A trade offered to an auto seat is declined by its bot, as `ExploreBot` does.
- With every seat disconnected, the game plays on at bot pace (Review focus 5).

- [ ] **Step 1: Write the failing tests.** `A_dropped_active_seat_is_played_after_the_grace`; `A_dropped_seat_mid_rotation_is_answered_by_the_bot` (Review focus 3); `Reconnecting_takes_the_next_decision_back`; `Everyone_dropped_still_reaches_the_breach`; `A_connected_seat_is_never_played_by_the_bot`.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): disconnect grace and bot seats`.

### Task 5: The views

**Files:** create `Views/HostView.cs`, `Views/Screens.cs`, `Views/Projection.cs`; test `HostViewTests.cs`, `ScreenTests.cs`, `SessionJsonTests.cs`.

**Interfaces:**

```csharp
public sealed record MeridianHostView(
    string Phase, string Banner,
    EqArray<SeatCard> Seats,                        // id = seat; name, crew, colour index, connected, auto, active
    EqArray<CompartmentCard> Compartments,          // id = "c12"; tile, name, cell, rotation, hatches, sealed sides, symbol
    EqArray<CrewMarker> Crew,                       // id = seat; compartment
    int TilesLeft, int AnomaliesDrawn, int Round,
    Presented? Now,                                 // the front of the presentation queue (Task 3)
    EqArray<string> Log);
```

- Every array of objects has a string `id`, so the platform patches them by id: a new compartment is one added element.
- Phone screens: `crewPick`, `turn`, `orient`, `card`, `roll`, `trade`, `sheet`, `watch` and `over`, each a record in `Screens.cs` serialized into `CustomView.Data`. `turn` carries the moves left, the reachable compartments with their costs, the openable hatches, usable items and the room action, all from `Rules.Legal`, so the phone offers exactly what `Step` accepts.
- In M2 nothing is secret; the audience filtering of spec §4.4 arrives with M3. The projections are still written so that a view is built from one seat's perspective.

- [ ] **Step 1: Write the failing tests.** `HostView_is_under_8_kb_with_45_compartments`; `An_explore_step_patches_in_under_one_packet` (measures `HostViewPatch.Diff` over a whole bot game, every step); `Every_screen_is_under_16_kb`; `Turn_offers_exactly_what_Legal_allows`; `Session_round_trips_through_json`.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): host view and phone screens`.

### Task 6: Phone fixtures

**Files:** test `PhoneFixtureTests.cs`; create `web/play/test/fixtures/meridian/*.json`.

- One fixture per screen and per notable state of it: `crewPick`, `crewPick-taken`, `turn`, `turn-no-moves`, `turn-with-items`, `orient`, `card-check`, `card-choice`, `roll`, `roll-breach`, `trade-offer`, `sheet`, `watch`, `over`. Each is a real view from a real `GameHarness` session.
- Copy Tactics' `PhoneFixtureTests.Check` pattern: `UPDATE_FIXTURES=1` rewrites them; otherwise a difference fails with that instruction.

- [ ] **Step 1: Write the test and generate the fixtures with `UPDATE_FIXTURES=1`.** Read every fixture before committing it.
- [ ] **Step 2: Commit** `test(meridian): pin phone screens as fixtures`.

### Task 7: The phone module

**Files:** create `web/play/src/games/meridian/{index.ts, types.ts, DeckMap.tsx, screens.tsx, meridian.css}`; modify `registry.ts`; test `web/play/test/meridian.test.tsx`.

- `DeckMap` draws one deck as a grid of labelled compartments, with hatches, sealed sides, crew markers in the platform's player colours, and tap targets for reachable compartments and openable hatches. A deck switcher shows the other two. Placeholder look only: real art arrives with #332 and #335.
- `turn` is the deck map plus the action buttons and End turn; `sheet` is traits (the marker on each track), gear and the map, with phone-side tabs so browsing never changes the view (spec §3.4).
- Every send is an absolute intent from the view's own data.

- [ ] **Step 1: Write the failing tests.** Every fixture renders without the fallback card; each action button sends exactly its intent; an unreachable compartment isn't tappable; the sheet's tabs don't send anything.
- [ ] **Step 2–4:** fail, implement, pass. `npm test` and `npm run build` in `web/play`.
- [ ] **Step 5: Commit** `feat(meridian): phone module`.

### Task 8: Server registration

**Files:** create `src/ArcBox.Server/Games/Meridian/{MeridianServiceCollectionExtensions.cs, MeridianWarmup.cs}`; modify the server csproj and `Program.cs`; test `src/ArcBox.Server.Tests/Meridian/AddMeridianTests.cs`.

- `AddMeridian()` copies `AddTactics()`'s shape: a `Lazy<RulesPack>` loaded from `<Content:Root>/meridian/base`, a hosted warmup that loads it before the server takes connections and logs, but doesn't throw, if it fails, and `AddArcGame` with a factory.
- The content folder is already published by the build, and the platform's content registry serves it to the TV as the `base` pack without further change.

- [ ] **Step 1: Write the failing tests.** `Meridian_is_registered`; `The_pack_loads_from_the_content_root`; `A_broken_pack_doesnt_stop_the_server_and_fails_the_first_room_with_every_error` (Review focus 9).
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): register the game on the server`.

### Task 9: The dev board

**Files:** create `web/play/src/dev/MeridianBoard.tsx`; modify `HostPanel.tsx`; test `web/play/test/meridianBoard.test.tsx`.

- Draws all three decks side by side from the `HostView`, the seat strip (with the "auto" tag), the Anomaly count, tiles left, the current presentation and the log.
- Plays the TV's part in pacing: once `Now` has been on screen for its fallback minus a moment, it signals `presented { seq }`, as `TacticsBoard` does for beats.

- [ ] **Step 1: Write the failing tests.** It renders a fixture host view; it signals each `seq` once; it ignores a view whose `Now` is unchanged.
- [ ] **Step 2–4:** fail, implement, pass.
- [ ] **Step 5: Commit** `feat(meridian): dev board`.

### Task 10: End to end: hub test, bot phones and the checklist

**Files:** create `src/ArcBox.Server.Tests/Meridian/MeridianHubTests.cs`, `tools/meridian-playtest/bots.cjs`; modify `docs/playtest-checklist.md`.

- **Hub test:** the in-memory server with three real SignalR phone clients and a host client. They create a room, select Meridian, pick crew and play `ExploreBot` choices from the phone views until the game ends at the Breach, and the room is back in the lobby. Mark it `Integration`.
- **Bot phones:** `node tools/meridian-playtest/bots.cjs --room=<CODE> --players=3`, modelled on `tools/tactics-playtest/bots.cjs`, so a person can play alongside bots or watch the dev board with no phones.
- **Checklist:** a Meridian section in `docs/playtest-checklist.md`: three real phones on the LAN; lock a phone during its own turn; reload one during a rotation choice; let a turn time out; reach the Breach.

- [ ] **Step 1: Write the hub test; make it pass.**
- [ ] **Step 2: Write the bot script; run it against a local server and the dev page.** Record the command and what was seen in the PR.
- [ ] **Step 3: Run `bash tools/check-all.sh --full`.** Expected: PASS apart from the 12 Tactics failures already on `main` (see PR #316), which this plan doesn't touch.
- [ ] **Step 4: Commit** `test(meridian): end-to-end explore over the hub`.

---

## Done when

- Three dev phones on `/dev.html` explore a ship from Boarding to the Breach, with the dev board showing it, and the room returns to the lobby.
- A game with every phone disconnected still reaches the Breach, at bot pace.
- A game with no TV at all reaches the Breach on fallback timers.
- The view budgets hold over whole bot games.
- `bash tools/check-all.sh --full` passes, apart from the pre-existing Tactics failures.
