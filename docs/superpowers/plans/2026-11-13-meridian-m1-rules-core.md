> Status: draft plan, awaiting review; no task has started. Live task state belongs on GitHub issues (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M1 Rules Core Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** Build the part of Meridian that needs no platform, phone or TV: a pure rules library in which bots can explore a ship, from Boarding to the moment the Breach fires, deterministically and with the state serializable at every step.

**Architecture:**
- `Meridian.Logic` is a pure library. State is immutable records, and one function, `Rules.Step(state, command)`, is the only way it changes.
- Randomness is a small value-type generator stored **in the state**, so a state that round-trips through JSON continues with the same draws. This is what makes restart resume (spec §5.6) a wiring job later.
- Tiles, crew and cards are data in `content/meridian/base/`, loaded through `IFileSource` and validated at load.

**Tech stack:** .NET 10, C# latest, System.Text.Json (`ArcJson.Options`), xUnit v3 (`xunit.v3.mtp-v2` 4.0.2) on Microsoft.Testing.Platform, `Microsoft.CodeAnalysis.BannedApiAnalyzers` 3.3.4.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md). Section numbers below refer to it. This plan is build stage **M1** of spec §8.

**Not in this plan:**
- `Meridian.Game` (the `IGame` adapter, phases, timers, views), the phone module and the dev board: stage M2.
- Scenarios, roles, audiences, combat, death and echoes: stage M3. M1 stops at a `BreachFired` event.
- The TV scene, art, the `ISnapshotGame` platform change and the full card set: stages M4–M7.

## Global constraints

- **Projects.**
  - `Meridian.Logic` uses `<TargetFrameworks>$(ArcSdkTargetFrameworks)</TargetFrameworks>` and references only `ArcBox.Sdk` (for `IFileSource` and `ArcJson`). Copy `Tactics.Logic.csproj`, including the banned-API analyzer.
  - `Meridian.Logic.Tests` copies `Redline.Logic.Tests.csproj`. `Meridian.Sim` copies `Tactics.Sim.csproj`.
  - Every new project goes into `ArcBox.slnx` under `/games/Meridian/`.
- **Build.** `Nullable` is enabled and `TreatWarningsAsErrors` is true (already in `Directory.Build.props`).
- **Purity.** `Meridian.Logic/BannedSymbols.txt` starts as a copy of `games/Tactics/Tactics.Logic/BannedSymbols.txt` with the messages reworded for Meridian. No `System.Random`, clocks, `Guid.NewGuid` or file APIs in the rules.
- **Determinism.** Nothing may depend on dictionary or hash-set iteration order. Collections in the state are immutable arrays or sorted dictionaries, and every "pick one" goes through the state's `Rng`.
- **Numbers.** Every rule value lives in `MeridianRules` (spec §2). The values to start with:
  - die faces `0, 0, 1, 1, 1, 2`; trait tracks of 8 steps;
  - Breach threshold 5; forced Breach on Anomaly 9;
  - rotation choice 8 s; turn inactivity 60 s (M1 stores these, and the adapter in M2 runs the clocks);
  - players 3–6; crew roster 8.
- **Wire and file ids.** Lower-case ASCII with underscores: `med_bay`, `flare_gun`. Decks are `command`, `habitat`, `engineering`. Traits are `brawn`, `agility`, `tech`, `nerve`. Sides are `n`, `e`, `s`, `w`.
- **Content is original.** Every tile, crew member and card is written for this game. Nothing is transcribed or adapted from a published game's components.
- **Serialization.** `GameState` round-trips through `ArcJson.Options` with value equality at every point a test can reach, including while a `Pending` is open.
- **Commits.** `feat(meridian): …`, `test(meridian): …`, `docs(meridian): …`, one per task.

## Review focus

Situations the spec implies that a main-path test would not reach. Each gets a pinned test in the task named in brackets.

1. **No tile fits behind a hatch.** The hatch becomes sealed, the mover stays where they are, the movement point is not spent, and the turn continues. [Task 5]
2. **A rotation choice times out.** `Timeout` picks the rotation with the most usable hatches; ties go to the lowest rotation, not to chance. [Task 5]
3. **A trait loss at step 1 before the Breach.** The marker stays on step 1 and the loss is discarded, not remembered. [Task 6]
4. **A deck runs out.** Incident and Salvage decks reshuffle their discards. An empty Incident or Salvage deck with no discards draws nothing and the turn continues. [Task 7]
5. **A player with nowhere to go.** `EndTurn` is always legal for the active player, whatever else is. [Task 4]
6. **A state saved mid-wait.** A state serialized while an `Orient`, `Roll` or `Trade` is pending, then restored, accepts the same command and reaches the same state as the original. [Tasks 4, 5, 6, 9]
7. **A command from the wrong player or for a closed wait.** `Step` returns the state unchanged with a `Rejected` event. It never throws on player input. [Task 4]
8. **The ninth Anomaly.** It fires the Breach with no roll, and the event says so. [Task 8]

---

## File map

```
ArcBox.slnx                                              (modify) /games/Meridian/ folder
games/Meridian/README.md                                  one paragraph and a link to the spec
games/Meridian/Meridian.Logic/
  Meridian.Logic.csproj  BannedSymbols.txt
  Core/      Ids.cs  Rng.cs  SignalDice.cs  MeridianRules.cs
  Content/   Defs.cs  PackLoader.cs  Validation.cs  ContentError.cs  RulesPack.cs
  Ship/      Deck.cs  Cell.cs  Placement.cs  ShipState.cs  ShipBuilder.cs  Reach.cs
  State/     GameState.cs  CrewState.cs  DeckPiles.cs  Pending.cs  Commands.cs  Events.cs
  Rules/     Rules.cs  Rules.Move.cs  Rules.Cards.cs  Rules.Checks.cs  Rules.Breach.cs  Rules.Trade.cs
             Effects.cs  Invariants.cs
  Bots/      IBotPolicy.cs  ExploreBot.cs
games/Meridian/Meridian.Logic.Tests/
  Meridian.Logic.Tests.csproj
  Support/   TestPack.cs  Play.cs
  Core/  Content/  Ship/  Rules/  Bots/                   one test file per source file under test
games/Meridian/Meridian.Sim/
  Meridian.Sim.csproj  Program.cs  ExploreReport.cs
content/meridian/base/
  rules/tiles.json  rules/crew.json
  rules/cards/incidents.json  rules/cards/salvage.json  rules/cards/anomalies.json
  rules/schema/{tiles,crew,cards,common}.schema.json
docs/superpowers/reports/2026-11-meridian-breach-timing.md  written by Task 11
```

## Order and parallelism

Tasks 1 → 2 → 3 → 4 are a chain. After Task 4, Tasks 5–9 each add one slice of `Rules.Step` and can be developed in parallel on separate branches if they are merged in number order. Tasks 10 and 11 need all of 5–9.

---

### Task 1: Projects, ids, dice, the generator and the rules record

**Files:**
- Create: `Meridian.Logic.csproj`, `BannedSymbols.txt`, `Meridian.Logic.Tests.csproj`, `games/Meridian/README.md`, `Core/{Ids.cs, Rng.cs, SignalDice.cs, MeridianRules.cs}`
- Modify: `ArcBox.slnx`
- Test: `Core/{RngTests.cs, SignalDiceTests.cs, IdsJsonTests.cs, MeridianRulesTests.cs}`

**Interfaces:**

```csharp
namespace Meridian.Logic;

// Ids: readonly record structs over string, ordinal IComparable, JSON as a plain string.
public readonly record struct SeatId(string Value);        // a player's seat; the adapter maps PlayerId to it
public readonly record struct CrewId(string Value);
public readonly record struct TileId(string Value);        // a tile definition: "med_bay"
public readonly record struct CompartmentId(string Value); // a placed tile: "c12"
public readonly record struct CardId(string Value);
public enum DeckId { Command, Habitat, Engineering }
public enum Trait { Brawn, Agility, Tech, Nerve }
public enum Side { N, E, S, W }

/// <summary>PCG32. A value: every draw returns the next generator, and the state stores it.</summary>
public readonly record struct Rng(ulong State, ulong Inc)
{
    public static Rng Seeded(ulong seed, ulong stream = 54);
    public (uint Value, Rng Next) NextUInt();
    public (int Value, Rng Next) Below(int bound);                       // unbiased; bound >= 1
    public (ImmutableArray<T> Items, Rng Next) Shuffle<T>(ImmutableArray<T> items);
}

public static class SignalDice
{
    /// <summary>Rolls <paramref name="count"/> dice with the rules' faces. Count 0 gives an empty roll totalling 0.</summary>
    public static (DiceRoll Roll, Rng Next) Roll(Rng rng, int count, MeridianRules rules);
}
public sealed record DiceRoll(ImmutableArray<int> Faces) { public int Total { get; } }

public sealed record MeridianRules
{
    public ImmutableArray<int> DieFaces { get; init; } = [0, 0, 1, 1, 1, 2];
    public int TrackSteps { get; init; } = 8;
    public int BreachThreshold { get; init; } = 5;
    public int ForcedBreachAnomaly { get; init; } = 9;
    public int MinPlayers { get; init; } = 3;
    public int MaxPlayers { get; init; } = 6;
    public TimeSpan RotationChoice { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan TurnInactivity { get; init; } = TimeSpan.FromSeconds(60);
}
```

`Rng` is the same algorithm as `Tactics.Logic.Rng`. It is copied, not referenced: one game's rules must not depend on another's. Moving it into `ArcBox.Sdk` for both is a possible later cleanup and is not part of this plan.

- [ ] **Step 1: Write the failing tests.**
  - `RngTests.Pcg32_matches_reference_sequence`: `Rng.Seeded(42, 54)` gives `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e`.
  - `RngTests.Shuffle_is_a_permutation_and_repeatable`.
  - `RngTests.Round_trips_through_json_and_continues_the_sequence`.
  - `SignalDiceTests.Faces_come_only_from_the_rules`, `Zero_dice_total_zero`, and `Mean_of_100000_dice_is_near_five_sixths` (within 0.01).
  - `IdsJsonTests`: each id serializes as a bare string; enums serialize as the lower-case names in Global constraints.
  - `MeridianRulesTests.Defaults_match_the_spec`: one assertion per value in spec §2.
- [ ] **Step 2: Run `dotnet test games/Meridian/Meridian.Logic.Tests`.** Expected: build failure.
- [ ] **Step 3: Create the projects and add them to `ArcBox.slnx`.**
- [ ] **Step 4: Implement the Core types.**
- [ ] **Step 5: Run `bash tools/test-dotnet.sh`.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(meridian): project skeleton, ids, signal dice and PCG32`.

### Task 2: Content definitions, loader and validation

**Files:**
- Create: `Content/{Defs.cs, PackLoader.cs, Validation.cs, ContentError.cs, RulesPack.cs}`, the four schema files, `Support/TestPack.cs`
- Test: `Content/{PackLoaderTests.cs, ValidationTests.cs}`

**Interfaces:**

```csharp
public sealed record TileDef(TileId Id, string Name, ImmutableArray<DeckId> Decks, ImmutableArray<Side> Hatches,
                             CardKind? Symbol, int ExitCost = 0, TileRule? Rule = null, int Copies = 1);
public enum CardKind { Incident, Salvage, Anomaly }
public sealed record CrewDef(CrewId Id, string Name, string Role,
                             ImmutableSortedDictionary<Trait, TraitTrack> Tracks);
public sealed record TraitTrack(ImmutableArray<int> Values, int Start);       // Start is a 1-based step
public sealed record CardDef(CardId Id, CardKind Kind, string Title, string Text, CardBody Body);

// A card's body is one of: a check with banded outcomes, a choice between options, or an item.
public abstract record CardBody;
public sealed record CheckBody(Trait Trait, ImmutableArray<Band> Bands) : CardBody;
public sealed record ChoiceBody(ImmutableArray<ChoiceOption> Options) : CardBody;
public sealed record ItemBody(ImmutableArray<Effect> Passive, ItemUse? Use) : CardBody;
public sealed record Band(int Min, int? Max, string Text, ImmutableArray<Effect> Effects);

public sealed record RulesPack(MeridianRules Rules, ImmutableSortedDictionary<TileId, TileDef> Tiles,
                               ImmutableSortedDictionary<CrewId, CrewDef> Crew,
                               ImmutableSortedDictionary<CardId, CardDef> Cards, StartLayout Start);

public static class PackLoader
{
    /// <summary>Loads and validates a pack. Throws <see cref="ContentException"/> listing every error, not the first.</summary>
    public static RulesPack Load(IFileSource files, string root);
}
```

`Effect` is defined in Task 6; this task declares it as a polymorphic record with a `kind` discriminator and no cases yet.

**Validation rules** (each with a test using a broken in-memory pack):
- ids are unique and match `^[a-z][a-z0-9_]*$`;
- a tile has 1–4 distinct hatches and at least one deck;
- a track has exactly `TrackSteps` values, none negative, never decreasing, and `Start` is within it;
- a check's bands cover every total from 0 upward with no gap or overlap;
- the start layout names tiles that exist and connects them hatch to hatch.

- [ ] **Step 1: Write the failing tests**, including `TestPack.Minimal()`: an in-memory `IFileSource` with 6 tiles, 3 crew and one card of each kind.
- [ ] **Step 2: Run the tests.** Expected: build failure.
- [ ] **Step 3: Implement the definitions, the loader and the validator.** Write the JSON schemas beside the content for editor support; the C# validator is the authority.
- [ ] **Step 4: Run the tests.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(meridian): content definitions, loader and validation`.

### Task 3: The ship: decks, placement, rotation and sealed hatches

**Files:**
- Create: `Ship/{Deck.cs, Cell.cs, Placement.cs, ShipState.cs, ShipBuilder.cs}`
- Test: `Ship/{PlacementTests.cs, ShipBuilderTests.cs}`

**Interfaces:**

```csharp
public readonly record struct Cell(DeckId Deck, int X, int Y);                 // JSON as ["habitat", 3, -1]
public sealed record Compartment(CompartmentId Id, TileId Tile, Cell At, int Rotation,   // 0..3 quarter turns
                                 ImmutableArray<Side> Hatches);                // after rotation
public sealed record ShipState(ImmutableArray<Compartment> Compartments,       // in placement order, so ids are stable
                               ImmutableSortedSet<HatchRef> Sealed,
                               ImmutableArray<Link> Links);                    // lift landings and shafts
public readonly record struct HatchRef(CompartmentId Compartment, Side Side);

public static class Placement
{
    /// <summary>The rotations of <paramref name="tile"/> at <paramref name="at"/> that put a hatch facing <paramref name="from"/>.</summary>
    public static ImmutableArray<int> ValidRotations(ShipState ship, TileDef tile, Cell at, Side from);

    /// <summary>The rotation a timeout picks: most usable hatches, then the lowest rotation.</summary>
    public static int DefaultRotation(ShipState ship, TileDef tile, Cell at, Side from);

    /// <summary>Places the tile and seals every hatch, on it or its neighbours, that now faces a wall.</summary>
    public static ShipState Place(ShipState ship, TileDef tile, Cell at, int rotation);
}

public static class ShipBuilder { public static ShipState Start(RulesPack pack); }
```

- A **usable hatch** is one that faces an empty cell or a neighbour's hatch.
- The start layout is the Airlock, Docking Ring and Central Lift on Habitat, with the lift's landings on Command and Engineering joined by `Link`s (spec §2.3).

- [ ] **Step 1: Write the failing tests.**
  - `ValidRotations` for a one-hatch, a straight two-hatch, a corner and a four-hatch tile.
  - `Place_seals_hatches_that_face_a_wall`, in both directions (the new tile's hatch and the neighbour's).
  - `DefaultRotation_prefers_usable_hatches_then_lowest`.
  - `Start_has_three_decks_joined_by_the_lift`.
  - `Compartment_ids_are_stable_across_a_json_round_trip`.
- [ ] **Step 2–4: Fail, implement, pass.**
- [ ] **Step 5: Commit** `feat(meridian): ship model, placement and sealed hatches`.

### Task 4: State, commands, events and the `Step` loop

**Files:**
- Create: `State/{GameState.cs, CrewState.cs, DeckPiles.cs, Pending.cs, Commands.cs, Events.cs}`, `Rules/Rules.cs`, `Support/Play.cs`
- Test: `Rules/{SetupTests.cs, TurnOrderTests.cs, RejectionTests.cs, StateJsonTests.cs}`

**Interfaces:**

```csharp
public sealed record GameState(
    Rng Rng, Phase Phase, ShipState Ship,
    ImmutableArray<CrewState> Crew,                 // in turn order
    int Active, int Round, int MovesLeft,
    DeckPiles Tiles, DeckPiles Incidents, DeckPiles Salvage, DeckPiles Anomalies,
    int AnomaliesDrawn, Pending? Waiting, BreachInfo? Breach);
public enum Phase { Explore, Breach }               // Boarding is the adapter's; M3 adds Endgame

public sealed record CrewState(SeatId Seat, CrewId Crew, CompartmentId At,
                               ImmutableSortedDictionary<Trait, int> Steps,     // 1-based marker positions
                               ImmutableArray<CardId> Items);
public sealed record DeckPiles(ImmutableArray<string> Draw, ImmutableArray<string> Discard);

// What the game is waiting for, and from whom. Null means: the active player's free choice of move or action.
public abstract record Pending(SeatId From);
public sealed record OrientPending(SeatId From, TileId Tile, Cell At, Side Entered, ImmutableArray<int> Rotations) : Pending(From);
public sealed record RollPending(SeatId From, RollReason Reason, Trait? Trait, int Dice) : Pending(From);
public sealed record ChoicePending(SeatId From, CardId Card) : Pending(From);
public sealed record TradePending(SeatId From, SeatId Offerer, TradeOffer Offer) : Pending(From);

public abstract record Command(SeatId From);
public sealed record MoveTo(SeatId From, CompartmentId To) : Command(From);
public sealed record Open(SeatId From, CompartmentId At, Side Side) : Command(From);
public sealed record Orient(SeatId From, int Rotation) : Command(From);
public sealed record Roll(SeatId From) : Command(From);
public sealed record Choose(SeatId From, string Option) : Command(From);
public sealed record UseItem(SeatId From, CardId Item) : Command(From);
public sealed record UseCompartment(SeatId From) : Command(From);
public sealed record OfferTrade(SeatId From, SeatId To, TradeOffer Offer) : Command(From);
public sealed record AnswerTrade(SeatId From, bool Accept) : Command(From);
public sealed record EndTurn(SeatId From) : Command(From);
public sealed record Timeout(SeatId From) : Command(From);       // the adapter's clock; resolves whatever From owes

public sealed record StepResult(GameState State, ImmutableArray<GameEvent> Events);

public static class Rules
{
    public static GameState NewGame(RulesPack pack, IReadOnlyList<(SeatId Seat, CrewId Crew)> seats, ulong seed);
    public static StepResult Step(RulesPack pack, GameState state, Command command);
    /// <summary>Every command <paramref name="seat"/> may send now. The phone's buttons and the bots both come from this.</summary>
    public static ImmutableArray<Command> Legal(RulesPack pack, GameState state, SeatId seat);
}
```

- `NewGame` shuffles the four decks with the seeded generator, places every crew member in the Airlock, picks a random first player and sets `MovesLeft` from their Agility.
- `Step` rejects any command not in `Legal` by returning the same state and one `Rejected(reason)` event.
- In this task `Step` implements only `EndTurn` and `Timeout` with nothing pending (which ends the turn). Tasks 5–9 add the rest.

- [ ] **Step 1: Write the failing tests.**
  - `NewGame_is_deterministic_for_a_seed` and `Different_seeds_give_different_tile_orders`.
  - `Everyone_starts_in_the_airlock_on_their_starting_steps`.
  - `EndTurn_passes_to_the_next_seat_and_wraps_into_a_new_round`.
  - `EndTurn_is_always_legal_for_the_active_seat` (Review focus 5).
  - `A_command_from_a_seat_that_is_not_active_is_rejected_without_change` and `An_answer_to_no_question_is_rejected` (Review focus 7).
  - `State_round_trips_through_json_with_value_equality`.
- [ ] **Step 2–4: Fail, implement, pass.** `Play` is a test helper: `Play.Game(seed).Do(command…)` returning the state and all events.
- [ ] **Step 5: Commit** `feat(meridian): game state, commands and the step loop`.

### Task 5: Movement, opening hatches and revealing compartments

**Files:**
- Create: `Ship/Reach.cs`, `Rules/Rules.Move.cs`
- Test: `Ship/ReachTests.cs`, `Rules/{MoveTests.cs, RevealTests.cs}`

**Rules implemented** (spec §2.3, §2.4):
- `Reach.From(ship, at, moves)` gives every compartment reachable with the moves left and its cost: 1 per compartment entered, plus the `ExitCost` of each compartment left, across usable hatches and `Link`s.
- `MoveTo` is legal for any compartment in reach; it spends the cost.
- `Open` is legal on an unsealed hatch of the mover's compartment that faces an empty cell, with at least 1 move left. It draws the first tile in the tile pile that fits the deck (skipped tiles keep their order).
  - **No tile fits:** the hatch is sealed, nothing is spent, `HatchSealed` is emitted.
  - **One valid rotation:** the tile is placed at once.
  - **Several:** an `OrientPending` opens. `Orient` with a listed rotation places it; `Timeout` uses `Placement.DefaultRotation`.
- After placement the mover enters the new compartment for 1 move. If the tile has a symbol, `MovesLeft` becomes 0 and the matching card draw begins (Task 7 supplies it; until then a `CardDue(kind)` event is emitted and the turn continues).

- [ ] **Step 1: Write the failing tests.**
  - `Reach_counts_exit_costs_and_lift_links`.
  - `MoveTo_spends_the_path_cost` and `MoveTo_out_of_reach_is_rejected`.
  - `Open_places_the_first_tile_that_fits_the_deck_and_keeps_the_order_of_skipped_tiles`.
  - `Open_with_no_fitting_tile_seals_the_hatch_and_spends_nothing` (Review focus 1).
  - `Orient_timeout_takes_the_default_rotation` (Review focus 2).
  - `A_symbol_tile_ends_movement`.
  - `Saved_while_orienting_then_restored_accepts_the_same_orient` (Review focus 6).
- [ ] **Step 2–4: Fail, implement, pass.**
- [ ] **Step 5: Commit** `feat(meridian): movement and compartment reveals`.

### Task 6: Traits, checks and the effect vocabulary

**Files:**
- Create: `Rules/{Effects.cs, Rules.Checks.cs}`
- Test: `Rules/{TraitTests.cs, CheckTests.cs, EffectTests.cs}`

**Interfaces:**

```csharp
// What a card band, a choice option, an item or a compartment rule can do. JSON: { "kind": "lose", ... }.
public abstract record Effect;
public sealed record Gain(Trait Trait, int Steps) : Effect;
public sealed record Lose(TraitGroup Group, int Steps) : Effect;       // Physical | Mental | a named trait
public sealed record DrawCard(CardKind Kind) : Effect;
public sealed record MoveToTile(TileId Tile) : Effect;                 // only if that tile is placed
public sealed record ExtraDice(Trait Trait, int Dice) : Effect;        // passive, from items
public sealed record EndTurnNow : Effect;
```

- A **check** opens a `RollPending` with dice equal to the trait's current value plus passive `ExtraDice`. `Roll` (or `Timeout`) rolls, emits `Rolled(faces, total)`, finds the band and applies its effects in order.
- `Lose` of a group with more than one trait in M1 takes the steps from the trait with the higher current step (ties: the earlier in enum order). Letting the player split the loss is the `allocate` screen and arrives with combat in M3.
- **Before the Breach a marker never goes below step 1.** A loss that would is cut short and the remainder is dropped (Review focus 3). A gain stops at the top step.

This vocabulary is deliberately small. It covers cards, items and compartment rules. Anything a scenario needs beyond it is C# in the scenario (spec §4.1), not a new effect.

- [ ] **Step 1: Write the failing tests:** one per effect; `A_check_rolls_the_traits_value_in_dice`; `Bands_pick_by_total`; `Loss_at_step_one_before_the_breach_is_dropped`; `Gain_stops_at_the_top_step`; `Saved_while_a_roll_is_pending_then_restored_rolls_the_same_faces` (Review focus 6).
- [ ] **Step 2–4: Fail, implement, pass.**
- [ ] **Step 5: Commit** `feat(meridian): traits, checks and effects`.

### Task 7: Cards, items and compartment rules

**Files:**
- Create: `Rules/Rules.Cards.cs`
- Test: `Rules/{IncidentTests.cs, SalvageTests.cs, DeckTests.cs, CompartmentRuleTests.cs}`

**Rules implemented** (spec §2.5):
- Entering a newly revealed symbol tile draws from the matching pile and emits `CardDrawn(card)`.
  - **Incident:** a `CheckBody` opens a roll; a `ChoiceBody` opens a `ChoicePending` (`Timeout` takes the first option). The card is then discarded.
  - **Salvage:** the card joins the drawer's `Items`. Its `Passive` effects apply while carried. `UseItem` applies its `Use` effects and, if the use is `single`, discards it.
  - **Anomaly:** joins `Items`, increments `AnomaliesDrawn` and starts the Breach roll (Task 8).
- **Empty piles:** Incident and Salvage reshuffle their discards with the state's generator. With nothing to reshuffle, `DeckEmpty(kind)` is emitted and the turn continues (Review focus 4).
- **Compartment rules** (`TileRule`): `Heal(group, steps, oncePerCrew)` through `UseCompartment`, and `DropTo(deck)`, which adds a one-way `Link` when the tile is placed.

- [ ] **Step 1: Write the failing tests:** one per behavior above, plus `A_once_per_crew_rule_can_be_used_by_each_crew_member_once`.
- [ ] **Step 2–4: Fail, implement, pass.**
- [ ] **Step 5: Commit** `feat(meridian): cards, items and compartment rules`.

### Task 8: The Breach roll

**Files:**
- Create: `Rules/Rules.Breach.cs`
- Test: `Rules/BreachTests.cs`

**Rules implemented** (spec §2.6):
- After an Anomaly is added to `Items`, a `RollPending` with reason `Breach` opens for the drawer, with dice equal to `AnomaliesDrawn`.
- A total at or above `BreachThreshold` sets `Phase = Breach` and `Breach = new BreachInfo(drawer, anomaly, compartment, forced: false)`, and emits `BreachFired`.
- When `AnomaliesDrawn == ForcedBreachAnomaly` there is no roll: the Breach fires with `forced: true` (Review focus 8).
- Once `Phase == Breach`, `Legal` is empty for every seat. Choosing a scenario and everything after is stage M3.

- [ ] **Step 1: Write the failing tests:** `The_roll_uses_as_many_dice_as_anomalies_drawn`; `Below_the_threshold_play_continues`; `At_the_threshold_the_breach_fires_and_records_who_what_and_where`; `The_ninth_anomaly_fires_without_a_roll`; `Nothing_is_legal_after_the_breach`.
- [ ] **Step 2–4: Fail, implement, pass.**
- [ ] **Step 5: Commit** `feat(meridian): the Breach roll`.

### Task 9: Trading

**Files:**
- Create: `Rules/Rules.Trade.cs`
- Test: `Rules/TradeTests.cs`

**Rules implemented** (spec §2.4):
- `OfferTrade` is legal once per turn for the active seat, to a seat in the same compartment, offering and asking for items each side actually carries.
- It opens a `TradePending` for the other seat. `AnswerTrade(true)` swaps the items; `false` or `Timeout` declines. Either way the active seat's turn continues.
- Anomalies can be traded. The Breach roll already happened when the card was drawn and does not repeat.

- [ ] **Step 1: Write the failing tests:** accept, decline, timeout, wrong compartment, an item no longer carried, a second offer in one turn, and the saved-while-pending round trip (Review focus 6).
- [ ] **Step 2–4: Fail, implement, pass.**
- [ ] **Step 5: Commit** `feat(meridian): trading`.

### Task 10: Starting content, the Explore bot, invariants and replays

**Files:**
- Create: the five content files under `content/meridian/base/rules/`, `Bots/{IBotPolicy.cs, ExploreBot.cs}`, `Rules/Invariants.cs`
- Test: `Content/BasePackTests.cs`, `Bots/ExploreBotTests.cs`, `Rules/{InvariantTests.cs, ReplayTests.cs}`

**Content, first wave** (all original; names and text written for this game):

| File | Count | Notes |
|---|---|---|
| `tiles.json` | 45 placeable, plus the 5 start tiles | About 12 Command, 15 Habitat, 12 Engineering, 6 that fit more than one deck. About 12 Incident, 10 Salvage and 12 Anomaly symbols, the rest plain |
| `crew.json` | 8 | Two leaning toward each trait. Starting values sum to the same total for every crew member |
| `incidents.json` | 12 of the final 36 | At least two checks per trait and two choices |
| `salvage.json` | 8 of the final 20 | At least one passive, one single-use and one reusable item |
| `anomalies.json` | 12 | The full set, because the Breach table in M3 is keyed by them |

The remaining 24 Incidents and 12 Salvage cards are a content task in stage M7.

**Interfaces:**

```csharp
public interface IBotPolicy
{
    /// <summary>Picks one of the legal commands. Must not read anything the seat couldn't see.</summary>
    Command Choose(RulesPack pack, GameState state, SeatId seat, ImmutableArray<Command> legal, ref Rng rng);
}
```

- **`ExploreBot`:** answers whatever it owes; otherwise heals if hurt and standing on a healing tile; otherwise opens the nearest unopened hatch, preferring the deck with the most tiles left; otherwise ends the turn. It never trades.
- The bot draws from **its own** `Rng`, not the state's, so adding a bot decision never changes what the game deals.
- **`Invariants.Check(pack, state)`** returns every violation: two compartments in one cell; a hatch that is neither sealed, open to an empty cell nor matched by a neighbour's hatch; a marker outside 1..`TrackSteps`; a card or tile that exists twice or not at all across piles, discards, items and the ship; `Active` out of range; a `Pending` for a seat that doesn't exist.

- [ ] **Step 1: Write the failing tests.**
  - `BasePackTests.The_shipped_pack_loads_with_no_errors` and `Has_the_counts_this_plan_promises`.
  - `BasePackTests.Every_deck_can_grow`: each deck has a tile for every hatch arrangement the validator requires.
  - `ExploreBotTests.Bots_reach_the_breach`: for seeds 1–1000 and each player count 3–6, four `ExploreBot`s play from `NewGame` until `Phase == Breach`, within 400 commands, with `Invariants.Check` empty after every step.
  - `ReplayTests.A_recorded_game_replays_to_the_same_state`: the commands of a bot game applied again from the same seed give an equal final state.
  - `ReplayTests.A_game_saved_and_restored_at_every_step_matches`: the same game with a JSON round trip after every command reaches the same final state. Mark it `[Trait("Category", "Integration")]` so the fast lane skips it.
- [ ] **Step 2: Write the content.** Run the loader after each file; fix every validation error it reports.
- [ ] **Step 3: Implement the bot and the invariant checker; make the tests pass.**
- [ ] **Step 4: Run `bash tools/check-all.sh --fast`.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(meridian): starting content, Explore bot and invariants`.

### Task 11: `Meridian.Sim` and the Breach timing report

**Files:**
- Create: `Meridian.Sim/{Meridian.Sim.csproj, Program.cs, ExploreReport.cs}`, `docs/superpowers/reports/2026-11-meridian-breach-timing.md`
- Modify: `ArcBox.slnx`
- Test: `Bots/SimSmokeTests.cs` (`[Trait("Category", "Sim")]`)

**What it does:**
- `dotnet run --project games/Meridian/Meridian.Sim -- explore --games 2000 --players 3-6 --seed 1` plays bot games to the Breach and prints, per player count:
  - the Anomaly number at which the Breach fired (distribution, median, share forced);
  - rounds and turns to the Breach;
  - compartments placed, and the share of games in which a deck ran out of tiles;
  - cards drawn per player, and the lowest trait step anyone reached.
- `--threshold N` and `--faces 0,0,1,1,1,2` override the rules for a run, so the Breach threshold can be compared without editing content.

**The report** records the command lines, the tables for thresholds 4, 5 and 6, and a recommendation for `BreachThreshold`.

- The spec's target is a Breach between 35% and 55% of a **whole game's** turns (§7.3), which can't be measured until the endgame exists. The M1 proxy is the Breach landing with **55–75% of the tiles placed**. The report must say that this is a proxy, and that the numbers come from bots, not people.
- If the recommendation differs from 5, change `MeridianRules` and the spec's §2.6 in the same commit.

- [ ] **Step 1: Write `SimSmokeTests.Explore_runs_twenty_games_and_reports_every_player_count`.**
- [ ] **Step 2: Implement the simulator.**
- [ ] **Step 3: Run it for 2,000 games at each threshold and write the report from the real output.**
- [ ] **Step 4: Run `bash tools/check-all.sh --full`.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(meridian): explore simulator and Breach timing report`.

---

## Done when

- `bash tools/check-all.sh --full` passes.
- Bots explore a ship to the Breach for 1,000 seeds at every player count with no invariant violation.
- A game saved and restored after every command ends in the same state as one that never was.
- The Breach timing report exists and `BreachThreshold` matches its recommendation.
- Nothing outside `games/Meridian/`, `content/meridian/`, `ArcBox.slnx` and `docs/` has changed.
