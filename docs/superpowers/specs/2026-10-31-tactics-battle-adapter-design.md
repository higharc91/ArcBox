# ArcBox Tactics battle adapter: design spec

- **Sub-project:** 3 of the ArcBox Tactics breakdown. It builds on sub-project 2, the [rules engine](2026-10-30-tactics-rules-engine-design.md), and on [Platform v2](2026-10-29-platform-v2-design.md).
- **Status:** draft, written 2026-10-31 while the user was away. The decisions marked **(decided for the user)** in §9 are mine, made to keep the work moving. Each one says what changes if it's overruled.
- **Goal:** a group can sit down, pick a campaign, and play a full battle: phones choose moves, targets and reactions, the AI plays the enemies and anyone who's away, and the TV shows it. The battle survives a server restart, and its result is written to the campaign.

## 1. Scope

The rules-engine spec (§2.4, §6) gave sub-project 3 four parts. This spec splits them into two milestones, so that a playable battle exists before any art work:

| Milestone | Delivers | Can be verified in CI |
|---|---|---|
| **3a: the game** | `TacticsGame` (adapter, phases, timers, reactions, seats, persistence), campaign document v0, `TacticsCampaignRules`, server registration, phone screens (`web/play/src/games/tactics`) | Yes: `GameHarness` tests in C#, Vitest for the phone |
| **3b: the TV** | Godot battle scene: map, units, event playback with pacing signals, HUD | Partly: scene code compiles; visuals need a person at a TV |

**Out of scope** (other sub-projects): character creation beyond picking a class (4), exploration and dialogue between battles (5), loot and inventories (6), chapter authoring tools (7). Until sub-project 4, a member's character is a `SheetBuilder.DefaultCharacter` of the class they pick.

## 2. Shape of the code

```
games/Tactics/
  Tactics.Logic/           (sub-project 2; gains Battle.Hand, §5.3)
  Tactics.Game/            new: TacticsGame, phases, views, campaign document, TacticsCampaignRules
  Tactics.Game.Tests/      new: GameHarness tests
src/ArcBox.Server/Games/Tactics/   registration: loads the rules packs once, AddArcGame + AddCampaignRules
web/play/src/games/tactics/        phone module: screens + skin
src/ArcBox.Host/games/tactics/     3b: Godot scene
```

- `Tactics.Game` depends on `Tactics.Logic` and `ArcBox.Sdk` only. Like the engine, it never touches the clock or does I/O: time comes from `IGameContext.Now` and timers, and persistence goes through `ICampaignLease`.
- `TacticsGame : IGame, ICampaignGame, IResumableGame, IHostAware`. Its manifest is `("tactics", "Tactics", …, MinPlayers: 1, MaxPlayers: 6, UsesCampaigns: true, ResumesAfterRestart: true)`. 6 is the largest party the encounters accept.
- The `RulesPack` is loaded once by the server and passed in. Its content hash is pinned for the session (rules-engine §2.5).

## 3. Phases

```
Start ─► Muster ─► Encounter pick ─► Battle ─► Results ─┬─► Encounter pick (next battle)
                                        ▲               └─► EndGame
Resume ─► Resuming ─────────────────────┘ (or ─► Encounter pick when no battle was running)
```

1. **Muster.** Every connected member without a character picks a class on their phone, from the 12 (`ChoiceView`). An absent member without one is given the class that best fills the party's missing role (healer, then defender, then striker, then control), so the party is always complete. The members' characters are built at the campaign's party level (§6).
2. **Encounter pick.** The VIP's phone lists the pack's encounters whose party size fits, each with its intended level and a short label: "River Crossing · Lv 1 · 4–6". The others see "Waiting for <VIP> to choose a battle". The TV shows the list.
3. **Battle** (§4).
4. **Results.** The TV and phones show the outcome, the XP earned and any injuries. The result is applied to the campaign document and checkpointed (§6). The VIP chooses **Next battle** or **End session**; the other phones wait.
5. **Resuming** (§7). After a restart, wait until a battle's present players are back, or the VIP taps **Continue without them**.

## 4. The battle loop

The adapter is a loop around `Battle.Apply`. The engine does the rules, and the adapter decides who is asked and when.

### 4.1 Whose move it is

The battle is waiting on one of three things:

| State | Who acts | Adapter |
|---|---|---|
| `Pending is ReactionOffer` for a unit with `PlayerControlled` and a connected seat | that player | `react` screen on their phone; timer `react` (10 s). On expiry, `Ai.React`'s choice |
| `Active` is a connected player's unit | that player | `turn` screen; timer `turn` (60 s, rules-engine §5.5) |
| anything else (an enemy, an NPC, a helper, an absent member, a reaction for any of them) | the AI | `Ai.Plan` / `Ai.React`, one command at a time, paced (§4.3) |

A player's turn that times out is finished with `Ai.Plan(state, unit, rules, new AiOptions(AllowItems: false, AllowDelay: false, RemainingOnly: true, ProfileOverride: "companion"))` and applied with `aiFinished: true`, so the TV shows the badge.

### 4.2 Commands from the phone

`CustomInput(Action, Data)` from the `turn` and `react` screens maps to commands:

| Action | Data | Command |
|---|---|---|
| `move` | `{ "path": [[x,y], …] }` | `Move` |
| `use` | `{ "ability": id, "item"?: id, "unit"?: id, "tile"?: [x,y] }` | `Use` |
| `delay` | `{ "slots": n }` | `Delay` |
| `endTurn` | none | `EndTurn` |
| `react` | `{ "choice": id }` | `React` |

The adapter **never trusts the phone**: it checks that the sender's seat controls the actor. It then calls `Battle.Apply`, which refuses anything illegal. A refusal goes back to that phone as a toast with the engine's text (`Rejection.Reason`, "You can only delay at the start of your turn, once per round."), and the view stays the same so the player can try again.

### 4.3 Pacing and the TV

Every applied command adds its events to the battle log as one **beat** with a sequence number. The host view carries the most recent beats (§8), and the TV animates each one, then sends `HostSignal("shown", { "seq": n })`.

- **AI turns** step one command per beat. The next command waits for `shown` of the last beat, or for the fallback timer `pace`, whichever comes first. The fallback is `400 ms + 250 ms × events in the beat`, capped at 6 s. Signals only ever shorten a wait, and stale `seq`s are ignored (platform v2 §3.7).
- **A player's own commands** apply at once. Their phone gets the result straight away; the TV catches up.
- The phone's next `turn` view is sent once that player's beat is shown, or after the same fallback, so the TV never runs behind the phones.

### 4.4 Seats and controllers

The engine records a `Controller` on every unit, and `Reactions` uses it to decide whether a reactor is asked or answers by AI. So when a party member's seat changes, the adapter hands the unit over:

- **Seat disconnects** (`OnPlayerConnectionChanged(id, false)`): `Battle.Hand(state, unit, new AiControlled("companion"))`. If that unit's turn is under way, the AI finishes it from what's left, exactly as a timeout would.
- **Seat reconnects:** `Battle.Hand(state, unit, new PlayerControlled(profile))`. It takes effect from that unit's next turn or reaction; a turn the AI has started finishes on the AI.

Absent members are disconnected from the start (platform v2 §3.2), so they begin as `companion`.

### 4.5 Battle end

When `state.Outcome` is set, the adapter calls `Battle.Result(state, rules)`, which gives XP, injuries, items used and loot table ids. It then goes to Results (§3, step 4).

## 5. Engine additions

The adapter needs three small things from `Tactics.Logic`. They're added with tests, like any engine change.

1. **`Battle.Hand(BattleState, UnitId, Controller) → BattleState`** sets a unit's controller. It emits nothing, because control isn't a rules event, and it's refused for units that aren't party members.
2. **`Choices.For(BattleState, UnitId, RulesPack) → TurnChoices`** gives the phone everything for one turn:
   - the reachable tiles with their Move cost;
   - every usable ability and item, with its legal targets;
   - for each target, the preview odds (hit chance or save chance, expected damage) from `Odds`;
   - each option's tooltip.

   It's built on `Battle.Check`, so the phone never offers something `Apply` would refuse.
3. **Serialisable battle log entries** (`Beat(int Seq, UnitId? Actor, ImmutableArray<BattleEvent> Events)`) for the host view and for resume.

## 6. The campaign document (v0)

```jsonc
{
  "version": 0,
  "rulesHash": "…",                       // the pack hash the document was last written under
  "partyLevel": 1,
  "members": {                            // keyed by ProfileId
    "prof_ab12": { "character": { /* CharacterRecord */ }, "xp": 0, "injuries": [] }
  },
  "completed": ["river_crossing"],        // encounters won, in order
  "battle": null                          // or { "encounter", "seed", "state": BattleState, "log": [Beat] } while one runs
}
```

- **The level** is the party's, from the XP table. `partyLevel` goes up when every member has the XP for the next level, so a party never splits across levels. *(decided for the user; sub-project 4 may make it per member)*
- **After every applied command**, `battle.state` is replaced and `lease.Save` is called (rules-engine §2.5). Only the last 20 beats of the log are kept.
- **At battle end:** XP and injuries are applied, `battle` is cleared, and `lease.Checkpoint(doc, "After <encounter name>")` records a rollback point.
- **Size:** a 16-unit `BattleState` serialises to about 60–120 KB, well under the 2 MB document limit.

`TacticsCampaignRules`:
- `NewDocument` gives an empty v0 document.
- `CardFor` gives `"<name>", "<Race> <Class> · Lv n"`, or `"No character yet"`.
- `PortalView` and `Handle` only show the party for now; sub-project 4 adds commands.

## 7. Resume and content pinning

`Resume(ctx, players, lease)`:
- **No battle running:** go to Encounter pick (or Muster, if a member has no character).
- **A battle running:**
  - If `battle.state.ContentHash` matches the loaded pack, enter **Resuming**. It waits until every member who was connected before the restart is back, or until the VIP taps **Continue without them**. Then the battle continues from the stored state.
  - If the hash differs, restart that battle from its start, with the same encounter and seed, and show a TV notice: "The rules changed since this battle began, so it starts again." That's rules-engine §2.5.

Because the engine's state includes the RNG and any pending reaction, the battle continues exactly where it stopped. A restart loses at most the command in flight.

## 8. Views

**Host view** (`TacticsHostView`, serialised to the TV):
- the phase;
- the map id;
- the units as the TV draws them: id, sprite key, side, tile, HP, conditions, Downed countdown;
- the round, the turn order and the active unit;
- the objectives and flags;
- the last 20 beats;
- the current prompt banner ("Kara is choosing a target…", "The goblins are moving");
- in Muster, Encounter pick and Results: those screens' data.

**Phone screens** (`CustomView("tactics", screen, data)`):

| Screen | When | Data |
|---|---|---|
| `pickClass` | Muster, no character | the 12 classes with role and one line each |
| `pickEncounter` | VIP, Encounter pick | the encounters (§3) |
| `turn` | your turn | your unit, the economy pips, `TurnChoices` (§5), a mini-map of nearby tiles |
| `react` | your reaction | the trigger ("The goblin is leaving your reach"), the options with odds, and a 10 s deadline (`DeadlineUtc`) |
| `status` | anyone else's turn | whose turn it is, "you're up in n" (exact, rules-engine §3.2), and your HP and conditions |
| `results` | Results | outcome, XP, injuries; the VIP also gets Next battle / End session |

- The `turn` screen sends its whole `TurnChoices` once. Then the d-pad, ability list and target cursor run locally on the phone, which keeps the view unchanged while the player browses (platform v2 §3.3).
- A refused command comes back as a toast on the same view.

## 9. Decisions made for the user

| # | Decision | If overruled |
|---|---|---|
| 1 | Two milestones: 3a (game and phone) before 3b (TV) | The TV scene could start in parallel on the user's machine; nothing in 3a changes |
| 2 | Muster gives class choice only, race human, default picks | Sub-project 4 replaces Muster's screen; the document already stores a full `CharacterRecord` |
| 3 | The party levels together | Store the level per member instead; the engine already takes each character's level |
| 4 | The VIP picks the encounter from the whole pack | Gate by `completed` / chapter order once sub-project 5 adds a story map |
| 5 | Reaction window 10 s, turn 60 s (spec), AI pacing fallback 400 ms + 250 ms per event | Constants in one place (`TacticsTiming`) |
| 6 | `lease.Save` after every command | Throttle it (e.g. at most once a second, plus at every turn end) if storage load shows up |

## 10. Testing

- **`Tactics.Game.Tests` with `GameHarness`:** a full battle with every seat AI-controlled finishes and writes its result. Also covered:
  - a player turn: the commands and the refusal toast;
  - a turn timeout finished by the AI with `AiFinished`;
  - a reaction prompt that times out;
  - a seat that drops mid-turn and reconnects;
  - pacing that waits for `shown` but never stalls without it;
  - resume mid-battle, and mid-reaction, comes out identical to an uninterrupted run;
  - resume under a different content hash restarts the battle;
  - the Muster defaults for absent members;
  - a document round-trip.
- **Contract test:** C# writes a fixture for each phone screen; Vitest checks that the `tactics` module parses and renders each one.
- **Vitest:** the `turn` screen's local cursor and target picker; the `react` countdown.
- **3b:** the Godot scene compiles in CI; a playtest checklist entry covers the visuals.
