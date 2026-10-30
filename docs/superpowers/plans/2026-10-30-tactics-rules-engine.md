# ArcBox Tactics Rules Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Tactics.Logic`, a deterministic C# battle engine for ArcBox Tactics. It covers:
- a 5e chassis tuned for tactics;
- rounds ordered by Speed;
- height, flanking and cover;
- abilities as JSON effect blocks;
- a utility AI;
- triggers.

Alongside it come its rules content (10 classes at levels 1–10, 30 subclasses, 11 races, the monsters and encounters) and `Tactics.Sim`, a balance simulator.

**Architecture:**
- **State and transitions.** The battle state is an immutable record graph. `Battle.Apply(state, command, rules)` returns a new state plus an ordered list of events.
- **Inside `Apply`.** A mutable `BattleWriter` works on a copy, drains a resolution queue of steps that's stored in the state, and freezes the result.
- **Determinism.**
  - The RNG (PCG32) lives in the state.
  - Every dictionary and set in the state is sorted.
  - So the same state plus the same command gives byte-identical JSON.
- **Content.** Abilities, passives, conditions, monsters, maps and encounters are JSON in `content/tactics/base/rules/`. They're validated at load time and executed by about 35 C# effect blocks.
- **AI.** A one-ply utility planner that scores plans by analytic expected value.

**Tech Stack:** .NET 10 (`net10.0`), C# latest, `System.Text.Json` (`ArcJson.Options`), `System.Collections.Immutable`, xUnit v3 (`xunit.v3.mtp-v2` 4.0.2). JSON Schema files are documentation only; the C# loader does the validation, so no schema library is added.

**Spec:** [`docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md`](../specs/2026-10-30-tactics-rules-engine-design.md). The § references below point into it.

## Global Constraints

- **Projects:** `games/Tactics/Tactics.Logic`, `games/Tactics/Tactics.Logic.Tests` and `games/Tactics/Tactics.Sim`, all added to `ArcBox.slnx` under `/games/Tactics/`.
  - `Tactics.Logic` references only `ArcBox.Sdk`. No new NuGet packages anywhere.
  - Tests use the `Redline.Logic.Tests.csproj` layout: `OutputType Exe`, `xunit.v3.mtp-v2` 4.0.2, `<Using Include="Xunit" />`.
- **Build:** `TreatWarningsAsErrors`, nullable, implicit usings (from `Directory.Build.props`). Public types get XML doc comments in the style of `ArcBox.Sdk`.
- **Determinism:**
  - **No `System.Random` and no `DateTime`** anywhere in `Tactics.Logic`.
  - **Sorted collections only.** Every map or set in the state, and in anything serialised or iterated for rules, is `ImmutableSortedDictionary` / `ImmutableSortedSet` with ordinal ordering. .NET string hashes are randomised per process, so hash-ordered collections would change JSON and iteration order between runs.
  - **The step queue is `ImmutableList<Step>`, with the head at index 0.** This is a plan decision that replaces the spec's `ImmutableStack`: System.Text.Json round-trips `ImmutableStack` in reversed order.
  - **Floating point is used only in the AI and the simulator.** Rules and geometry use integers; LOS uses fixed point ×1024.
- **JSON:**
  - State, commands and events serialise with `ArcJson.Options`. Polymorphic records use `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]` with `[JsonDerivedType]`.
  - Effect blocks use their **single-key** content form (`{"damage": {...}}`) through `EffectJsonConverter`, both in content and in the state.
- **Rules values (exact, from the spec):**

| Rule | Value |
|---|---|
| Proficiency bonus | +2 at levels 1–4, +3 at 5–8, +4 at 9–10 |
| Ability modifier | ⌊(score − 10) / 2⌋ |
| Point-buy | 27 points, scores 8–15 before bonuses |
| HP | Level 1: full hit die + CON. Then per level: ⌈die / 2⌉ + 1 + CON (the SRD average, rounded up) |
| AC caps | Light armour: DEX uncapped. Medium: DEX capped at +2. Heavy: no DEX. Shield: +2 |
| Initiative | 10 + DEX modifier + bonuses. Heavy armour −2, hasted +5, slowed −5 |
| Initiative ties | Player-controlled first, then higher DEX, then ordinal `UnitId` |
| Move | 4 tiles; 3 for dwarf, halfling and gnome |
| Jump | 2; 3 for monk and rogue |
| Climbing | +1 Move per level climbed beyond the first in a step. Difficult terrain: 2 Move per tile |
| Falling | More than 3 levels: 1d6 per level beyond 3, then prone. A push into a blocked tile or a unit: both take 1d4, push stops |
| High ground | Attacker ≥ 2 levels above target: +2 to hit. Ranged range: +⌊height difference / 2⌋ tiles |
| Melee reach | Height difference ≤ 2, or ≤ 3 with a reach weapon. Reach weapons also reach 2 tiles in a straight line |
| Flanking | An ally that can act on the tile exactly opposite across the target: melee advantage |
| Cover rays | Eye at tile top + 1.5 levels. Targets at tile top + 0.25 / 1.0 / 1.75. Rays blocked: 1 → half (+2 AC and DEX saves), 2 → three-quarters (+5), 3 → no line of sight. A grazing tie counts as not blocked. A standing unit in the way gives half cover against ranged attacks |
| Crits | Natural 20: damage dice rolled twice, modifiers once. Natural 1 always misses. Contest ties go to the defender |
| Concentration | One effect per caster. On damage: CON save against DC max(10, ⌊damage ÷ 2⌋) |
| Downed | Countdown 3. −1 at the start of each new round. −1 per hit taken (−2 on a crit). Healing or an adjacent Help revives (Help gives 1 HP). At 0 the unit is Out and gains an Injury |
| AI | Keep the best 12 tiles plus the current tile. ≤ 250 ms p95 on 6 vs 10 units. Companion: at most 1 healing item per battle, only when the threat map predicts Downed next round. Reactions: take an opportunity attack when EV > 0; take a defensive reaction when it turns a hit into a miss or saves ≥ 25% of max HP |
| Simulator | Levels 1, 3, 5, 7, 10. Role bands ±15% of the band mean. Flag abilities used in < 2% of the turns where legal. Flag encounters won < 60% or > 95% of the time on Normal at their level. Target battle length 8–12 rounds |
| XP | `xp.json` thresholds: 1000 XP per level (cumulative 0, 1000, …, 9000). Default encounter XP: 250, so 2 encounters a session gives about one level every 2 sessions |

- **Role bands** (for the simulator):
  - Defender: fighter, paladin.
  - Striker: rogue, ranger, warlock, monk.
  - Healer: cleric, bard.
  - Control: wizard, druid.
- **Rejection strings** (exact, shown on the phone). `{…}` is substituted.

| Code | Text |
|---|---|
| `NotYourTurn` | "It's not your turn." |
| `NoAction` | "You've already used your action." |
| `NoBonus` | "You've already used your bonus action." |
| `NoReaction` | "You've already used your reaction." |
| `NoMove` | "You don't have enough movement." |
| `OutOfRange` | "Out of range." |
| `NoLineOfSight` | "No line of sight." |
| `CantAfford` | "Not enough {resource}." |
| `NoUses` | "No uses left this battle." |
| `PathBlocked` | "You can't move there." |
| `InvalidTarget` | "That's not a valid target." |
| `Incapacitated` | "You can't act right now." |
| `ReactionPending` | "Answer the reaction first." |
| `NothingToReact` | "There's nothing to react to." |
| `UnknownAbility` | "You don't have that ability." |
| `BattleOver` | "The battle is over." |

- **Content conventions** (plan decisions that turn SRD 5.2 into this game; every content task follows them):
  - **Spell tier** = SRD spell level. **Mana cost = tier**; cantrips cost 0.
  - **Max tier by level:** full casters (wizard, cleric, bard, druid) can cast tier ⌈level / 2⌉, capped at 5. Half casters (paladin, ranger) can cast tier ⌈level / 4⌉.
  - **Pools:**
    - Full-caster Mana by level: 4, 6, 8, 10, 11, 12, 13, 14, 15, 16. Bard and druid get 3, 4, 6, 7, 8, 9, 10, 10, 11, 12 because they have a second resource.
    - Every other resource interpolates linearly between the spec §4.5 endpoints (level 1 → level 10), rounding down.
  - **Spell ranges:**
    - Touch → 1.
    - SRD range ≤ 30 ft → 4.
    - Any longer ranged spell → 6.
  - **Weapon ranges:**
    - Thrown → 4.
    - Shortbow and light crossbow → 6.
    - Longbow and heavy crossbow → 8.
  - **Areas:**
    - Radius r ft → `burst` ⌈r / 10⌉.
    - Cone r ft → `cone` ⌈r / 10⌉ + 1.
    - Line r ft → `line` ⌈r / 10⌉.
  - **Durations:**
    - 1 minute → 10 rounds.
    - 1 hour or longer → the whole battle.
    - "Save ends" stays `until save`.
  - **Per-rest features:** short-rest and long-rest uses both become `usesPerBattle`. Pools such as Lay on Hands refill each battle.
  - **Left out:** spells with no battle use (utility, ritual, travel, divination).
  - **`defaultPicks`:** every class lists a preset loadout per level, used by the simulator and by AI-built characters. Real players choose their own in the portal (sub-project 4).
  - **Content may add, never override:** an id defined twice without `"replaces"` is a load error.
- **Attribution:** `games/Tactics/THIRD_PARTY.md` carries the SRD 5.2 CC-BY-4.0 notice (spec §4.10).
- **Commits:** each task ends with a commit on the working branch. Every message ends with the session's attribution lines (`Co-Authored-By: …` and `Claude-Session: …`).
- **Verification:** `dotnet test ArcBox.slnx` is green at the end of every task. Use `$HOME/.dotnet/dotnet` if `dotnet` isn't on PATH.

## Review Focus

These are the five failure modes most likely to bite a real table that no task's main tests naturally exercise. Each has a pinned test in the task in brackets.

1. **A turn comes up for a unit that can't act** (Downed, Out, stunned, paralysed). The engine emits `TurnSkipped` and moves to the next unit in the same `Apply`; it never waits for a command that can't come. [Task 8]
2. **A unit leaves the battle mid-round** (a summon whose concentration broke, a unit killed by a trigger). It's removed from `Order`, whether it was before or after the current index, and the next turn goes to the right unit with no one skipped or repeated. [Task 10]
3. **A push sends a unit off the map edge or into a `deep` tile.** Off-map counts as impassable (collision rule, no crash). `deep` Downs a party member or kills an enemy, with a `Fell` event. [Task 6]
4. **One area effect drops the last standing party member while more targets are still queued.** The battle ends after that step, the rest of the queue is dropped, and `BattleEnded` is emitted exactly once. [Task 8]
5. **A chapter pack redefines a base id without `"replaces"`.** The load fails, and the error names both files and the JSON pointer of the duplicate. [Task 9]

---

## File Map

```
games/Tactics/
  THIRD_PARTY.md  README.md
  Tactics.Logic/Tactics.Logic.csproj
    Core/      Ids.cs  Dice.cs  Rng.cs  Stats.cs  ByLevel.cs
    Map/       BattleMap.cs  Terrain.cs  Paths.cs  Sight.cs  Reach.cs  Areas.cs
    State/     UnitSheet.cs  UnitState.cs  BattleState.cs  Controller.cs  Commands.cs  Events.cs  Rejection.cs
               BattleSetup.cs  BattleResult.cs  Pending.cs
    Rules/     Battle.cs  BattleWriter.cs  Steps.cs  TurnOrder.cs  Economy.cs  Rolls.cs  Movement.cs
               Reactions.cs  Conditions.cs  Hooks.cs  Modifiers.cs  Downed.cs  Objectives.cs  Triggers.cs
               Invariants.cs
    Effects/   EffectDef.cs  EffectJsonConverter.cs  BlockRegistry.cs  IEffectBlock.cs  EffectContext.cs
               Blocks/*.cs   (one file per block group: Rolls, Hp, ConditionBlocks, Motion, MapBlocks, Units,
                              Combat, TurnOrderBlocks, Flow)
               NamedHooks/WildShape.cs  Counterspell.cs  Polymorph.cs  AnimateDead.cs
    Content/   Defs.cs (AbilityDef, PassiveDef, ConditionDef, ClassDef, SubclassDef, RaceDef, BackgroundDef,
               MonsterDef, ItemDef, MapDef, EncounterDef, TriggerDef, AiProfileDef, InjuryDef)
               RulesPack.cs  PackLoader.cs  Validation.cs  ContentError.cs  ContentHash.cs  Tooltips.cs
               SheetBuilder.cs  Encounters.cs
    Ai/        Ai.cs  Planner.cs  Scoring.cs  Odds.cs  ThreatMap.cs  AiProfile.cs  Companion.cs
  Tactics.Logic.Tests/Tactics.Logic.Tests.csproj
    Support/   AsciiMap.cs  TestBattle.cs  TestSheets.cs  Replay.cs  ContentPaths.cs
    Core/ Map/ Rules/ Effects/ Content/ Ai/ Golden/ (*.replay.json, *.events.json)
  Tactics.Sim/Tactics.Sim.csproj  Program.cs  SimRunner.cs  Report.cs
content/tactics/base/rules/
  manifest.json conditions.json races.json backgrounds.json injuries.json xp.json ai-profiles.json items.json
  abilities/common.json abilities/<class>.json abilities/races.json abilities/monsters-<family>.json
  classes/<class>.json  monsters/<family>.json  maps/<id>.json  encounters/<id>.json  schema/*.schema.json
docs/superpowers/reports/2026-xx-xx-tactics-balance.md   (written by Tasks 16 and 23)
ArcBox.slnx*
```
`*` = modified.

## Order and parallelism

```
Phase 1 core:     T1 ─► T2 ─► T3 ─► T4 ─► T5 ─► T6 ─► T7 ─► T8
Phase 2 content:  T8 ─► T9 ─► T10 ─► T11 ─► T12 ─► T13
Phase 3 AI:       T13 ─► T14 ─► T15 ─► T16
Phase 4 content:  T16 ─► T17 ─┬─► T19 ─┐
                  T16 ─► T18 ─┴─► T20 ─┼─► T22 ─► T23
                  T16 ─► T21 ──────────┘
```

| Task | Blocked by |
|---|---|
| 1–8 | the previous task |
| 9 | 8 |
| 10, 11 | 9 (10 then 11; both touch `BlockRegistry`) |
| 12 | 11 |
| 13 | 12 |
| 14 | 13 |
| 15 | 14 |
| 16 | 15 |
| 17, 18, 21 | 16 (can run in parallel: separate content files) |
| 19 | 17 (and 13 for fighter, rogue, ranger, paladin and monk base classes) |
| 20 | 18 |
| 22 | 19, 20, 21 |
| 23 | 22 |

---

## Phase 1: Engine core

### Task 1: Projects, ids, dice and the RNG

**Files:**
- Create:
  - `games/Tactics/Tactics.Logic/Tactics.Logic.csproj`, `games/Tactics/Tactics.Logic.Tests/Tactics.Logic.Tests.csproj`
  - `games/Tactics/THIRD_PARTY.md`, `games/Tactics/README.md`
  - `Tactics.Logic/Core/{Ids.cs, Dice.cs, Rng.cs, Stats.cs, ByLevel.cs}`
- Modify: `ArcBox.slnx` (add a `/games/Tactics/` folder with both projects)
- Test: `Tactics.Logic.Tests/Core/{RngTests.cs, DiceTests.cs, ByLevelTests.cs, IdsJsonTests.cs}`

**Interfaces:**
- Produces:

```csharp
namespace Tactics.Logic;
// Ids: readonly record structs wrapping string, IComparable (ordinal), JSON as plain string (one converter each,
// registered via [JsonConverter] on the type so ArcJson.Options needs no change).
public readonly record struct UnitId(string Value) : IComparable<UnitId>;
public readonly record struct AbilityId(string Value) : IComparable<AbilityId>;
public readonly record struct ConditionId(string Value) : IComparable<ConditionId>;
public readonly record struct InjuryId(string Value);  public readonly record struct ItemId(string Value);
public readonly record struct LootTableId(string Value);
public readonly record struct Tile(int X, int Y) : IComparable<Tile>;     // JSON as [x, y]
public enum Stat { Str, Dex, Con, Int, Wis, Cha }
public static class Stats { public static int Mod(int score); public static int Proficiency(int level); }

public readonly record struct Dice(int Count, int Sides, int Bonus)    // JSON as "2d6+1" | "1d4" | "3"
{
    public static Dice Parse(string text);            // throws FormatException with the text in the message
    public double Average { get; }                    // Count*(Sides+1)/2 + Bonus
    public override string ToString();                // canonical "2d6+1", "1d4-1", "3"
}

public readonly record struct Rng(ulong State, ulong Inc)   // PCG32 (XSH RR), O'Neill reference constants
{
    public static Rng Seeded(ulong seed, ulong stream = 54);
    public (uint Value, Rng Next) NextUInt();
    public (int Value, Rng Next) Below(int bound);    // unbiased (Lemire rejection), bound >= 1
    public (int Value, Rng Next) Roll(int sides);     // 1..sides
}

/// <summary>Level-keyed table: the highest key ≤ level wins. JSON: {"1": x, "5": y}.</summary>
public sealed record ByLevel<T>(ImmutableSortedDictionary<int, T> Steps)
{
    public T At(int level);                           // throws if no key ≤ level
    public static ByLevel<T> Constant(T value);
}
```

- [ ] **Step 1: Write the failing tests**
  - `RngTests.Pcg32_matches_reference_sequence`: `Rng.Seeded(42, 54)` gives the first 6 `NextUInt` values `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e` (the pcg32-demo output for seed 42, stream 54).
  - `RngTests.Same_seed_same_rolls` and `RngTests.Roll_stays_in_range_and_hits_every_face`: 10 000 `Roll(20)` calls stay in 1..20 and every face appears.
  - `DiceTests.Parse_and_format_round_trip`: `"2d6+1"`, `"1d4-1"`, `"3"` and `"10d10"` round-trip; `Average` of `"2d6+1"` is `8.0`; `"d6"`, `"2x6"` and `""` throw `FormatException`.
  - `ByLevelTests.At_picks_highest_key_at_or_below`: `{1:"1d6",3:"2d6",5:"3d6"}` gives `At(4) == "2d6"` and `At(10) == "3d6"`.
  - `IdsJsonTests.Ids_and_tiles_serialise_compactly`: `UnitId("kara")` → `"kara"`, `Tile(3,4)` → `[3,4]`, and both round-trip.
  - `StatsTests`: `Mod(8) == -1`, `Mod(15) == 2`, `Proficiency(4) == 2`, `Proficiency(5) == 3`, `Proficiency(9) == 4`.
- [ ] **Step 2: Run `dotnet test games/Tactics/Tactics.Logic.Tests`.** Expected: build failure (the types don't exist).
- [ ] **Step 3: Create both csproj files.** Logic copies `Redline.Logic.csproj` without the embedded data. Tests copies `Redline.Logic.Tests.csproj`, referencing `Tactics.Logic` and `ArcBox.Sdk.Testing`. Add both to `ArcBox.slnx`. Write `THIRD_PARTY.md` with the SRD 5.2 notice:

  > This work includes material from the System Reference Document 5.2 ("SRD 5.2") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

  `README.md` gets a one-paragraph overview and a link to the spec.
- [ ] **Step 4: Implement the Core types.**
- [ ] **Step 5: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(tactics): project skeleton, ids, dice and PCG32`.

### Task 2: Battle maps, terrain and pathfinding

**Files:**
- Create: `Tactics.Logic/Map/{BattleMap.cs, Terrain.cs, Paths.cs}`, `Tactics.Logic.Tests/Support/AsciiMap.cs`
- Test: `Tactics.Logic.Tests/Map/{AsciiMapTests.cs, PathsTests.cs}`

**Interfaces:**
- Consumes: `Tile` (Task 1).
- Produces:

```csharp
[Flags] public enum TileFlags { None = 0, Water = 1, Difficult = 2, Impassable = 4, Hazard = 8, Deep = 16 }
public enum Cover { None, Half, ThreeQuarters, Total }
public sealed record TileInfo(int Height, int Material, TileFlags Flags);
public sealed record PropPlacement(string Id, Tile At, bool Blocks, Cover Cover);
public sealed class BattleMap
{
    public string Id { get; }  public int Width { get; }  public int Height { get; }
    public ImmutableArray<string> Materials { get; }  public ImmutableArray<PropPlacement> Props { get; }
    public ImmutableSortedDictionary<string, ImmutableArray<Tile>> Zones { get; }
    public bool InBounds(Tile t);  public TileInfo this[Tile t] { get; }
    public static BattleMap FromBytes(string id, int w, int h, ReadOnlySpan<byte> tiles /* 3 bytes per tile, row-major */,
                                      ImmutableArray<string> materials, ImmutableArray<PropPlacement> props,
                                      ImmutableSortedDictionary<string, ImmutableArray<Tile>> zones);
}
/// <summary>The map as it is now: base map + height deltas + zones (difficult) + blocking props. Read-only view.</summary>
public sealed class Terrain
{
    public Terrain(BattleMap map, ImmutableSortedDictionary<Tile, int> heightDelta, ImmutableSortedSet<Tile> difficultZones);
    public BattleMap Map { get; }
    public int HeightAt(Tile t);  public bool Walkable(Tile t);  public bool Difficult(Tile t);  public bool Deep(Tile t);
    public PropPlacement? PropAt(Tile t);
}
public sealed record Mover(int Move, int Jump, ImmutableSortedSet<Tile> Allies, ImmutableSortedSet<Tile> Enemies);
public sealed class PathMap   // result of Dijkstra from one origin
{
    public Tile Origin { get; }
    public IReadOnlyCollection<Tile> Reachable { get; }        // tiles the mover may END on, cost ≤ Move
    public int CostTo(Tile t);                                  // int.MaxValue if unreachable
    public ImmutableArray<Tile> PathTo(Tile t);                 // excludes origin; empty if unreachable
}
public static class Paths
{
    public static PathMap From(Terrain terrain, Tile origin, Mover mover);
    public static int StepCost(Terrain terrain, Tile from, Tile to, int jump);   // int.MaxValue if illegal
}
```

- **The step-cost rules** (spec §3.5), checked in this order:
  1. The step is illegal if the tile is out of bounds, not walkable (impassable, deep or a blocking prop), occupied by an enemy, or the climb is more than `jump` levels.
  2. Otherwise the cost is 1, or 2 if difficult, plus `max(0, climb − 1)`. A drop has no extra cost.
- Allies can be passed through but not ended on.
- Neighbours are always expanded in the order N, E, S, W, with ties broken by ordinal `Tile`, so `PathTo` is deterministic.

- [ ] **Step 1: Write `AsciiMap.Parse(string)`.** Each row is a run of cells. Each cell is one height digit `0`–`9` followed by one marker:

  | Marker | Meaning |
  |---|---|
  | `.` | plain |
  | `~` | water, difficult |
  | `#` | impassable |
  | `^` | difficult |
  | `x` | deep |
  | `c` | half-cover crate prop |
  | `C` | full-cover blocking prop |

  Cells are separated by spaces. `AsciiMapTests` checks a 3×2 map's heights, flags and props.
- [ ] **Step 2: Write `PathsTests`:**
  - `Flat_move_4_reaches_diamond_of_radius_4`: 41 tiles on an open 9×9 map from the centre.
  - `Climb_2_costs_2_and_climb_3_is_blocked_at_jump_2`.
  - `Drop_any_height_is_free`.
  - `Difficult_costs_2`.
  - `Can_pass_through_ally_but_not_end_there`.
  - `Enemy_blocks`.
  - `Path_is_deterministic`: same map, same result twice, N/E/S/W tie order.
- [ ] **Step 3: Run the tests.** Expected: FAIL.
- [ ] **Step 4: Implement `BattleMap`, `Terrain` and `Paths`.** `Paths` is Dijkstra on a `PriorityQueue<Tile,(int cost,int order)>`.
- [ ] **Step 5: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(tactics): battle maps, terrain and pathfinding`.

### Task 3: Line of sight, cover, reach, flanking and areas

**Files:**
- Create: `Tactics.Logic/Map/{Sight.cs, Reach.cs, Areas.cs}`
- Test: `Tactics.Logic.Tests/Map/{SightTests.cs, ReachTests.cs, AreasTests.cs}`

**Interfaces:**
- Consumes: `Terrain`, `Tile`, `Cover`.
- Produces:

```csharp
public sealed record SightResult(int RaysBlocked, Cover Cover)  { public bool HasLineOfSight => RaysBlocked < 3; }
public static class Sight
{
    public const int Fixed = 1024;
    /// <summary>Three rays, eye = top+1.5 → target top+0.25/1.0/1.75 (fixed point). unitTiles: standing units between
    /// (excluding both ends) give half cover when ranged. A prop with cover adjacent to target on the attacker side
    /// guarantees at least that cover.</summary>
    public static SightResult Trace(Terrain terrain, Tile from, Tile to, ImmutableSortedSet<Tile> unitTiles, bool ranged);
}
public static class Reach
{
    public static bool Melee(Terrain t, Tile attacker, Tile target, bool reachWeapon);   // 4-adjacent (or 2 straight w/ reach) + height rule
    public static int HighGroundBonus(Terrain t, Tile attacker, Tile target);            // 2 or 0
    public static int RangeBonus(Terrain t, Tile attacker, Tile target);                 // max(0, ⌊Δh/2⌋)
    public static int Distance(Tile a, Tile b);                                          // Manhattan
    public static Tile? OppositeOf(Tile attacker, Tile target);                          // null unless 4-adjacent
}
public enum AreaShape { Single, Self, Burst, Line, Cone, Cross, Ring, AllAllies, AllEnemies }
public sealed record AreaDef(AreaShape Shape, int Size = 0, bool IncludeSelf = false, bool FriendlyFire = false);
public static class Areas
{
    /// <summary>Tiles covered, sorted. Burst/Ring use Manhattan radius; Line/Cone extend from caster toward aim along
    /// the dominant axis; Cone widens by 1 each side per step (step 1 = 1 tile, step 2 = 3, …).</summary>
    public static ImmutableArray<Tile> Tiles(AreaDef area, Tile caster, Tile aim, Terrain terrain);
}
```

- **Ray tracing algorithm (this is not determined by the signature).**
  - Walk the 2D supercover line of tile columns from `from` to `to`, excluding both ends.
  - For each column, compute the ray's height at the column's entry and exit (fixed-point linear interpolation).
  - The ray is blocked if `min(entry, exit) < column top × 1024`, or the column holds a blocking prop whose top (column top + 2 levels) is above `min(entry, exit)`.
  - Equality counts as **not** blocked (the grazing rule).

- [ ] **Step 1: Write `SightTests`** with ASCII maps:
  - `Open_ground_no_cover`.
  - `Low_wall_one_level_gives_half` (attacker and target on height 0 with a height-1 column between them blocks only the feet ray).
  - `Two_level_wall_gives_three_quarters`.
  - `Tall_wall_blocks_sight`.
  - `High_ground_sees_over_wall` (attacker on height 3).
  - `Grazing_corner_is_not_blocked`.
  - `Unit_between_gives_half_only_for_ranged`.
  - `Crate_adjacent_to_target_gives_half`.
- [ ] **Step 2: Write `ReachTests` and `AreasTests`:**
  - `Melee_blocked_by_height_3_without_reach`.
  - `Reach_weapon_hits_2_straight`.
  - `High_ground_bonus_needs_2_levels`.
  - `RangeBonus_floor_half_height`.
  - `Burst_2_is_13_tiles`.
  - `Cone_2_east_is_4_tiles`.
  - `Line_3_north_is_3_tiles`.
  - `Cross_1_is_5_tiles`.
  - `Ring_2_is_8_tiles`.
- [ ] **Step 3: Run the tests.** Expected: FAIL.
- [ ] **Step 4: Implement `Sight`, `Reach` and `Areas`.**
- [ ] **Step 5: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(tactics): line of sight, cover, reach and areas`.

### Task 4: State model, serialisation, turn order and the `Apply` loop

**Files:**
- Create:
  - `Tactics.Logic/State/*` (all files listed in the File Map)
  - `Tactics.Logic/Rules/{Battle.cs, BattleWriter.cs, Steps.cs, TurnOrder.cs, Economy.cs}`
  - `Tactics.Logic/Content/RulesPack.cs` (an in-memory pack, with no loading yet)
  - `Tactics.Logic.Tests/Support/{TestBattle.cs, TestSheets.cs}`
- Test: `Tactics.Logic.Tests/Rules/{TurnOrderTests.cs, SerialisationTests.cs}`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:

```csharp
public enum Side { Party, Enemy, Neutral }
public enum LifeState { Up, Downed, Out, Dead }
public enum ArmorKind { None, Light, Medium, Heavy }
public sealed record WeaponDef(string Id, Dice Damage, string DamageType, Stat Stat, bool Finesse, bool Ranged,
                               int Range, bool Reach, ImmutableSortedSet<string> Tags);
public sealed record UnitSheet(
    string Name, Side Side, int Level, ImmutableSortedDictionary<Stat, int> Scores, int MaxHp, int Ac, ArmorKind Armor,
    int Move, int Jump, int InitiativeBonus, ImmutableSortedSet<Stat> SaveProficiencies,
    ImmutableSortedSet<string> Skills, ImmutableSortedDictionary<string, int> Resources,   // max per battle
    ImmutableArray<AbilityId> Abilities, ImmutableArray<string> Passives, WeaponDef Weapon, int Attacks,
    ImmutableSortedSet<string> Resist, ImmutableSortedSet<string> Immune, ImmutableSortedSet<string> Vulnerable,
    ImmutableSortedSet<string> Tags, string? Sprite, string? AiProfile, string? CastingStat, int Xp = 0);
public sealed record TurnEconomy(int MoveLeft, bool Action, bool Bonus, bool Reaction, int ExtraActions);
public sealed record ActiveCondition(ConditionId Id, UnitId? Source, int? RoundsLeft, bool UntilSave, int SaveDc,
                                     Stat SaveStat, bool Concentration);
public sealed record UnitState(UnitId Id, UnitSheet Sheet, Controller Controller, Tile Pos, int Hp, int TempHp,
    ImmutableSortedDictionary<string, int> Resources, ImmutableArray<ActiveCondition> Conditions, TurnEconomy Economy,
    LifeState Life, int Countdown, ImmutableSortedDictionary<string, int> UsesLeft, AbilityId? Concentrating,
    ImmutableArray<ItemStack> Bag, int ItemsUsedByAi, UnitId? SummonedBy, bool Protected);
public sealed record ItemStack(ItemId Item, int Count);
public sealed record BattleState(
    string ContentHash, string MapId, ImmutableSortedDictionary<UnitId, UnitState> Units, int Round,
    ImmutableArray<UnitId> Order, int TurnIndex, Rng Rng, ImmutableList<Step> Queue, Pending? Pending,
    ImmutableSortedDictionary<Tile, int> HeightDelta, ImmutableArray<Zone> Zones, ObjectiveState Objectives,
    ImmutableSortedSet<string> Flags, ImmutableSortedDictionary<string, int> Counters,
    ImmutableSortedSet<string> FiredTriggers, BattleOutcome? Outcome, ImmutableArray<RecordedInjury> Injuries,
    ImmutableArray<ItemUse> ItemsUsed, int NextSerial)
{ public UnitId? Active { get; } }        // Order[TurnIndex] while no Outcome
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")] public abstract record Controller;
public sealed record PlayerControlled(ProfileId Owner) : Controller;
public sealed record AiControlled(string Profile) : Controller;
// Commands exactly as spec §2.2 (Move, Use, Delay, React, EndTurn) + Target:
public sealed record Target(UnitId? Unit = null, Tile? Tile = null);
public sealed record Resolution(BattleState State, ImmutableArray<BattleEvent> Events, Rejection? Rejected);
public sealed record Rejection(RejectCode Code, string Reason);     // strings: Global Constraints table
public sealed record UnitPlacement(UnitId Id, UnitSheet Sheet, Controller Controller, Tile At,
                                   ImmutableArray<ItemStack> Bag, bool Protected = false);
public sealed record BattleSetup(BattleMap Map, ImmutableArray<UnitPlacement> Units, ObjectivesDef Objectives,
                                 ImmutableArray<TriggerDef> Triggers, Difficulty Difficulty, int Xp,
                                 ImmutableArray<LootTableId> Loot);
public enum Difficulty { Story, Normal, Hard }
/// <summary>Objective expression: a leaf string ("defeat_all", "survive:8", …) or {"any": […]} / {"all": […]}.
/// JSON: a bare string is a leaf. Task 8 evaluates defeat_all and party_out; Task 22 adds the rest.</summary>
public sealed record ObjectiveExpr(string? Leaf, ImmutableArray<ObjectiveExpr> Any, ImmutableArray<ObjectiveExpr> All);
public sealed record ObjectivesDef(ObjectiveExpr Win, ObjectiveExpr Lose);      // defaults: defeat_all / party_out
public sealed record ObjectiveState(ObjectivesDef Current, ImmutableSortedDictionary<string, int> Progress);
/// <summary>Kept raw until Task 22 gives it typed When/Do records; Task 4–21 battles carry none.</summary>
public sealed record TriggerDef(string Id, JsonElement When, bool Once, JsonElement Do);
public static class Battle
{
    public static Resolution Start(BattleSetup setup, RulesPack rules, ulong seed);   // emits RoundStarted, TurnStarted
    public static Resolution Apply(BattleState state, Command command, RulesPack rules);
}
public static class TurnOrder { public static ImmutableArray<UnitId> Sort(BattleState s, RulesPack rules); }
public sealed class RulesPack   // Task 9 adds Load(); Task 4 builds in memory
{
    public string Hash { get; }  public BattleMap? MapFor(string id);
    public ImmutableSortedDictionary<AbilityId, AbilityDef> Abilities { get; }  /* + Passives, Conditions, Classes, … */
    public static RulesPack InMemory(string hash, IEnumerable<AbilityDef>? abilities = null, IEnumerable<PassiveDef>? passives = null,
                                     IEnumerable<ConditionDef>? conditions = null, IEnumerable<BattleMap>? maps = null);
}
```

- **`BattleWriter`** is internal and mutable over a frozen state:
  - `UnitState Unit(UnitId)`, `void Set(UnitState)`, `int Roll(int sides)` (advances the RNG), `void Emit(BattleEvent)`;
  - `void PushFront(params Step[])`, `Step? PopFront()`;
  - `Terrain Terrain`, `BattleState Freeze()`.
- **The `Apply` loop:**
  1. Validate the command against `Active`, `Pending` and `Outcome`. On failure, return a `Rejection` and the **same state instance**.
  2. Translate the command into steps.
  3. Drain the queue until it's empty, a `Pending` is set, or there's an `Outcome`.
  4. If the turn has ended, advance to the next turn, skipping units that can't act (Task 8 fills in the skip rules), and start a new round when the order runs out (re-sort, `RoundStarted`).
- **Events in this task:** `RoundStarted(int Round, ImmutableArray<UnitId> Order)`, `TurnStarted(UnitId Unit, int Round)`, `TurnEnded(UnitId Unit)`, `Delayed(UnitId Unit, int ToIndex)`.

- [ ] **Step 1: Write `TestSheets`** (fighter, rogue, cleric and goblin `UnitSheet`s with fixed numbers) and the `TestBattle` builder: `TestBattle.On(ascii).Party("kara", TestSheets.Fighter, at).Enemy(...).Start(seed) → (BattleState, ImmutableArray<BattleEvent>)`.
- [ ] **Step 2: Write `TurnOrderTests`:**
  - `Sorted_by_10_plus_dex_plus_bonuses`.
  - `Heavy_armor_minus_2`.
  - `Ties_players_then_dex_then_id`.
  - `Order_resorts_each_round` (change a unit's `InitiativeBonus` mid-round; the order changes from the next round only).
  - `Delay_moves_turn_later_this_round_only`.
  - `EndTurn_by_wrong_unit_is_rejected_NotYourTurn`: the exact string, and the same state instance is returned.
- [ ] **Step 3: Write `SerialisationTests.State_round_trips_byte_identical`:** serialise, deserialise, serialise again, and the strings are equal. Use a state with conditions, a queue entry and a `Pending`; after Task 5, extend it with real steps.
- [ ] **Step 4: Run the tests.** Expected: FAIL.
- [ ] **Step 5: Implement the State, Battle, BattleWriter, TurnOrder and Economy types** (economy reset at turn start: `MoveLeft = Move`, `Action`, `Bonus` and `Reaction` all `true`).
- [ ] **Step 6: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 7: Commit** `feat(tactics): battle state, turn order and the apply loop`.

### Task 5: Rolls, the effect pipeline and the first blocks

**Files:**
- Create:
  - `Tactics.Logic/Rules/Rolls.cs`
  - `Tactics.Logic/Effects/{EffectDef.cs, EffectJsonConverter.cs, BlockRegistry.cs, IEffectBlock.cs, EffectContext.cs}`
  - `Tactics.Logic/Effects/Blocks/{Rolls.cs, Hp.cs, ConditionBlocks.cs}`
  - `Tactics.Logic/Content/Defs.cs` (`AbilityDef`, `CostDef`, `TargetDef`, `PresentationDef`, `AiHints`)
- Test: `Tactics.Logic.Tests/Rules/RollsTests.cs`, `Tactics.Logic.Tests/Effects/{UseCommandTests.cs, EffectJsonTests.cs}`

**Interfaces:**
- Consumes: Task 4.
- Produces:

```csharp
public enum RollKind { Attack, Save, Contest, Check, Damage, Countdown }
public enum Edge { None, Advantage, Disadvantage }
public sealed record RollResult(ImmutableArray<int> D20s, int Kept, int Modifier, int Total, int Vs, bool Success,
                                bool Crit, bool Fumble);
public static class Rolls
{
    public static Edge Combine(int advantages, int disadvantages);      // any+any → None
    public static RollResult D20(BattleWriter w, int modifier, int vs, Edge edge, bool critOn20, bool autoMissOn1);
    public static int Damage(BattleWriter w, Dice dice, bool crit);    // crit doubles Count, not Bonus
}
// Content records (Defs.cs)
public sealed record CostDef(int Action = 0, int Bonus = 0, int Reaction = 0, int Move = 0,
    ImmutableSortedDictionary<string, int>? Resource = null, int? Uses = null, bool Concentration = false);
public enum TargetKind { Self, Ally, Enemy, AnyUnit, DownedAlly, Tile, EmptyTile }
public sealed record TargetDef(TargetKind Kind, int Range, bool Los, AreaDef Area, int MinRange = 0, int? HeightTolerance = null);
public sealed record PresentationDef(string? Anim, string? Fx, string? Projectile);
public sealed record AiHints(ImmutableArray<string> Tags);
public sealed record AbilityDef(AbilityId Id, string Name, int Tier, CostDef Cost, TargetDef Target,
    ImmutableArray<EffectDef> Effects, PresentationDef? Presentation = null, AiHints? Ai = null,
    string? Flavor = null, AbilityId? Replaces = null, bool IsWeaponAttack = false);
// Effects
public abstract record EffectDef;                       // JSON: {"<key>": {...}} via EffectJsonConverter + BlockRegistry
public sealed record AttackEffect(string Kind /*weapon|spell*/, Stat? Stat, ImmutableArray<EffectDef> OnHit,
    ImmutableArray<EffectDef> OnMiss, ImmutableArray<EffectDef> OnCrit, int Bonus = 0) : EffectDef;    // key "attack"
public sealed record SaveEffect(Stat Stat, int? Dc, ImmutableArray<EffectDef> OnFail,
    ImmutableArray<EffectDef> OnSuccess, bool HalfOnSuccess = false) : EffectDef;                        // "save"
public sealed record DamageEffect(ByLevel<Dice>? Dice, string? Type, ImmutableArray<string> Bonus,
    bool Weapon = false) : EffectDef;                                                                    // "damage"
public sealed record HealEffect(ByLevel<Dice> Dice, ImmutableArray<string> Bonus) : EffectDef;          // "heal"
public sealed record ApplyEffect(ConditionId Condition, int? Rounds, bool UntilSave = false) : EffectDef; // "apply"
public sealed record RemoveEffect(ConditionId Condition) : EffectDef;                                    // "remove"
public interface IEffectBlock { Type DefType { get; } string Key { get; } void Run(EffectDef def, EffectContext ctx); }
public sealed class EffectContext
{
    public BattleWriter W { get; }  public UnitId Source { get; }  public UnitId? Target { get; }  public Tile? Aim { get; }
    public AbilityDef? Ability { get; }  public RollResult? Last { get; set; }  public int Level { get; }  public RulesPack Rules { get; }
}
public static class BlockRegistry { public static IEffectBlock For(string key); public static IEffectBlock For(EffectDef def);
                                    public static IReadOnlyCollection<string> Keys { get; } }
```

- **Named addends** in a `Bonus` list are `"mod.<stat>"`, `"mod.cast"` (the caster's `CastingStat`), `"prof"` and `"level"`.
- **Attack modifier:** the stat modifier (the weapon's stat, or the higher of STR/DEX for finesse; `CastingStat` for spells) + proficiency + `Bonus` + high ground + static modifiers. It's rolled against AC + cover.
- **Save DC:** `8 + prof + mod.cast` unless `Dc` is set.
- **Order of a `Use` command:**
  1. Validate:
     - the ability is known (`UnknownAbility`);
     - the cost is affordable (`NoAction` / `NoBonus` / `CantAfford` / `NoUses`);
     - the target kind fits (`InvalidTarget`);
     - range, including `RangeBonus` (`OutOfRange`);
     - line of sight when `Los` is set (`NoLineOfSight`).
  2. Push the steps `PayCost`, then `RunEffects(...)` once per target in the area, in sorted `UnitId` order.
- **Events:** `AbilityUsed(UnitId Source, AbilityId Ability, Target Target, PresentationDef? Presentation)`, `Rolled(UnitId Roller, RollKind Kind, RollResult Result)`, `Damaged(UnitId Unit, int Amount, string? Type, int HpAfter)`, `Healed(UnitId Unit, int Amount, int HpAfter)`, `ConditionApplied(UnitId Unit, ConditionId Condition)`, `ConditionRemoved(UnitId Unit, ConditionId Condition)`.
- **The basic Attack action** is the built-in ability `common.attack` (`IsWeaponAttack`): one `attack{kind: weapon}` with `onHit: [{damage: {weapon: true}}]`, repeated `Sheet.Attacks` times in one Action. `RulesPack.InMemory` always includes it; Task 13 moves it to `abilities/common.json`.

- [ ] **Step 1: Write `RollsTests`.** Each test seeds an `Rng` so the d20s are known: `Advantage_keeps_higher`, `Adv_and_disadv_cancel`, `Nat20_crits_and_doubles_dice_not_bonus`, `Nat1_misses_even_if_total_beats_AC`, `Contest_tie_defender_wins`.
- [ ] **Step 2: Write `UseCommandTests`:**
  - `Guiding_bolt_hits_damages_and_applies_lit` (the spec §4.2 ability, built in C#).
  - `Out_of_range_rejected` (exact string).
  - `No_los_rejected`.
  - `Cant_afford_names_resource` ("Not enough devotion.").
  - `Action_spent_twice_rejected_NoAction`.
  - `Burst_hits_each_unit_in_sorted_order`.
  - `Save_half_on_success`.
- [ ] **Step 3: Write `EffectJsonTests.Single_key_form_round_trips`.** Parse the spec §4.2 JSON into `AbilityDef`, serialise it, and the result is equal to the input after normalising whitespace. An unknown key throws `JsonException` naming the key.
- [ ] **Step 4: Run the tests.** Expected: FAIL.
- [ ] **Step 5: Implement everything listed in Files.**
- [ ] **Step 6: Run `dotnet test ArcBox.slnx`.** Expected: PASS. Also extend `SerialisationTests` with a mid-queue state.
- [ ] **Step 7: Commit** `feat(tactics): d20 rolls and the effect pipeline`.

### Task 6: Movement, pushes, falling, opportunity attacks and reactions

**Files:**
- Create: `Tactics.Logic/Rules/{Movement.cs, Reactions.cs}`, `Tactics.Logic/Effects/Blocks/Motion.cs`
- Modify: `Tactics.Logic/State/Pending.cs` (reaction offer)
- Test: `Tactics.Logic.Tests/Rules/{MoveCommandTests.cs, ReactionTests.cs}`, `Tactics.Logic.Tests/Effects/MotionTests.cs`

**Interfaces:**
- Consumes: `Paths`, `Reach`, `BattleWriter`, `Rolls`.
- Produces:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")] public abstract record Pending;
public sealed record ReactionOffer(UnitId Reactor, ImmutableArray<ReactionChoice> Options, string Trigger) : Pending;
public sealed record ReactionChoice(string Id, AbilityId? Ability, UnitId? Target);  // Id "pass" always present
public sealed record PushEffect(int Distance, bool AwayFromSource = true) : EffectDef;   // key "push"; "pull" = PullEffect
public sealed record PullEffect(int Distance) : EffectDef;
public sealed record TeleportEffect(int Range, bool Self = true) : EffectDef;           // "teleport"
public sealed record SwapEffect : EffectDef;                                             // "swap"
public sealed record DashEffect(int Tiles) : EffectDef;                                  // "dash": move self toward aim
public sealed record JumpToEffect(int Range) : EffectDef;                                // "jumpTo": ignore Jump limit
public enum ReactionTriggerKind { LeavesReach, BeforeHit, SpellCast }
public sealed record ReactionTrigger(ReactionTriggerKind Kind, UnitId Actor, UnitId? Target, AbilityId? Ability, RollResult? Roll);
public static class Reactions
{
    /// <summary>Reactions any unit may take for this trigger, in sorted UnitId order (opportunity attack, or abilities
    /// with cost.reaction and a matching "reactsTo" in AbilityDef.Ai tags: "beforeHit", "leavesReach", "spellCast").</summary>
    public static ImmutableArray<ReactionOffer> OffersFor(BattleWriter w, ReactionTrigger trigger, RulesPack rules);
}
```

- **A `Move` command** must start at the actor's tile and be legal step by step under `Paths.StepCost`, with total cost ≤ `MoveLeft` (`PathBlocked` / `NoMove`). It becomes one `MoveStep` per tile. Before each step that leaves a hostile's melee reach (unless `Disengaged` is set this turn), every eligible reactor in sorted order gets a `ReactionOffer`.
- **AI-controlled reactors** are never offered. The engine resolves them with the Task 15 policy. Until Task 15, AI reactors take every opportunity attack.
- `React(pass)` clears the offer.
- **Pushes:**
  - A push moves one tile at a time, using the direction from source to target along the dominant axis.
  - A blocked tile (out of bounds, impassable, a blocking prop or a unit) stops the push and deals 1d4 to the target, and also to the unit it hit.
  - Moving onto a lower tile falls: more than 3 levels deals 1d6 per level beyond 3 and the unit is knocked prone.
  - A `deep` tile: a party member is Downed (countdown 3), and an enemy goes to `Dead`.
  - Pushes never provoke.
- **Events:** `Moved(UnitId Unit, ImmutableArray<Tile> Path)`, `Pushed(UnitId Unit, Tile From, Tile To)`, `Fell(UnitId Unit, int Levels, int Damage, bool IntoDeep)`, `Collided(UnitId Unit, UnitId? With, int Damage)`, `ReactionOffered(ReactionOffer Offer)`, `Reacted(UnitId Reactor, string Choice)`.

- [ ] **Step 1: Write `MoveCommandTests`:** `Path_cost_spends_MoveLeft`, `Illegal_step_rejected_PathBlocked`, `Too_long_rejected_NoMove`, `Split_move_before_and_after_action`.
- [ ] **Step 2: Write `ReactionTests`:**
  - `Leaving_reach_offers_player_reaction_and_pauses`: `Pending` is set; any other command is rejected with `ReactionPending`.
  - `React_attack_resolves_then_move_continues`.
  - `Disengage_prevents_offer`.
  - `React_without_pending_rejected_NothingToReact`.
  - `Pending_survives_serialisation`: serialise mid-offer, deserialise, `React`, and the events are identical to the run without serialising.
- [ ] **Step 3: Write `MotionTests`:** `Push_2_moves_two_tiles_away`, `Push_into_wall_1d4_both`, `Push_off_ledge_4_levels_deals_1d6_and_prone`.
  - **Review Focus 3:** `Push_off_map_edge_collides_like_wall` and `Push_into_deep_downs_party_member_and_kills_enemy`.
  - `Teleport_needs_empty_walkable_tile`.
  - `Swap_exchanges_positions`.
- [ ] **Step 4: Run the tests.** Expected: FAIL.
- [ ] **Step 5: Implement the movement, motion blocks and reactions.**
- [ ] **Step 6: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 7: Commit** `feat(tactics): movement, pushes, falling and reactions`.

### Task 7: Conditions, hooks, passives and modifiers

**Files:**
- Create: `Tactics.Logic/Rules/{Conditions.cs, Hooks.cs, Modifiers.cs}`
- Modify: `Tactics.Logic/Content/Defs.cs` (`PassiveDef`, `ConditionDef`, `StaticMods`, `WhenDef`), `Rolls.cs` and `Battle.cs` (consult modifiers and fire hooks)
- Test: `Tactics.Logic.Tests/Rules/{ConditionTests.cs, HookTests.cs, ConcentrationTests.cs, FlankingCoverTests.cs}`

**Interfaces:**
- Consumes: Tasks 4–6.
- Produces:

```csharp
public enum HookKind { OnBattleStart, OnRoundStart, OnTurnStart, OnTurnEnd, BeforeAttackRoll, OnAttackHit, OnAttackMiss,
                       OnDamaged, OnSave, OnMove, OnEnterTile, OnDowned, OnKill, OnAllyDowned }
public sealed record StaticMods(int Ac = 0, int Attack = 0, int Damage = 0, int Move = 0, int Initiative = 0,
    ImmutableSortedDictionary<Stat, int>? Saves = null, bool Incapacitated = false, bool CantMove = false,
    Edge OwnAttacks = Edge.None, Edge AttackedMelee = Edge.None, Edge AttackedRanged = Edge.None,
    ImmutableSortedSet<Stat>? AutoFailSaves = null, bool CritWhenAdjacent = false, bool Invisible = false,
    ImmutableSortedSet<string>? Resist = null, ImmutableSortedSet<string>? Immune = null, int ExtraActions = 0);
public sealed record WhenDef(...);   // all/any/not + leaves: weapon[], advantage, flanked, targetHasCondition, hpBelow,
                                     // heightAbove, isSpell, damageType, targetTag  (JSON: object with one key per leaf)
public sealed record PassiveDef(string Id, HookKind? Hook, WhenDef? When, ImmutableArray<EffectDef> Effects,
    StaticMods? Static, bool OncePerTurn = false, bool OncePerRound = false, int? UsesPerBattle = null, string? Replaces = null);
public sealed record ConditionDef(ConditionId Id, string Name, StaticMods? Static, ImmutableArray<PassiveDef> Passives,
    ImmutableSortedSet<string> Tags /* "magical","physical","fear" for cleanse */, ConditionId? Implies = null);
public static class Modifiers { public static StaticMods For(BattleWriter w, UnitId unit, RulesPack rules); }  // sum of passives + conditions
public static class Hooks { public static void Fire(BattleWriter w, HookKind hook, HookArgs args, RulesPack rules); }
public sealed record HookArgs(UnitId Owner, UnitId? Other = null, RollResult? Roll = null, int Amount = 0, Tile? Tile = null);
```

- **Hook firing order:** for each unit in sorted `UnitId` order, passives in sheet order, then condition passives in the order they were applied.
- **Hook effects run immediately**, pushed to the front of the queue. Recursion depth is capped at 8, and going past the cap breaks an invariant (Task 8).
- **Condition ticks:**
  - `RoundsLeft` drops at the end of the source unit's turn. Conditions with no source tick at the end of the affected unit's turn.
  - `UntilSave` conditions roll their save at the end of the affected unit's turn.
  - `Concentration` conditions end when the caster's concentration ends.
- **Flanking and cover** are added to attacks here, through `Reach.OppositeOf` and `Sight.Trace`. The attack roll adds the cover bonus to AC.
- **Events:** `ConditionSaved(UnitId Unit, ConditionId Condition, RollResult Roll)`, `ConcentrationBroken(UnitId Caster, AbilityId Ability)`, `PassiveTriggered(UnitId Owner, string Passive)`.

- [ ] **Step 1: Write `ConditionTests`.** Use C#-built `ConditionDef`s for the SRD conditions:
  - `Prone_melee_adv_ranged_disadv`.
  - `Stunned_skips_actions_and_autofails_str_dex`.
  - `Restrained_cant_move`.
  - `Invisible_attacks_have_advantage`.
  - `Rounds_tick_on_source_turn_end`.
  - `Until_save_rolls_each_turn_end`.
  - `Burning_deals_1d6_at_turn_start`.
- [ ] **Step 2: Write `HookTests`:**
  - `Sneak_attack_once_per_turn_with_advantage_or_flank` (the spec §4.4 passive).
  - `Static_resist_halves_fire`.
  - `Hook_recursion_cap_throws_invariant`.
- [ ] **Step 3: Write `ConcentrationTests`:** `Second_concentration_ends_first`, `Damage_forces_con_save_dc_max_10_half`, `Breaking_removes_linked_conditions`.
- [ ] **Step 4: Write `FlankingCoverTests`:** `Ally_opposite_gives_melee_advantage`, `Downed_ally_does_not_flank`, `Half_cover_adds_2_ac_to_ranged_attack`, `High_ground_plus_2_hit`.
- [ ] **Step 5: Run the tests.** Expected: FAIL.
- [ ] **Step 6: Implement.**
- [ ] **Step 7: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 8: Commit** `feat(tactics): conditions, hooks, passives and concentration`.

### Task 8: Downed, Out, injuries, battle end, invariants and replays

**Files:**
- Create:
  - `Tactics.Logic/Rules/{Downed.cs, Objectives.cs, Invariants.cs}` (`Objectives` supports `defeat_all` and `party_out` only; Task 22 adds the rest)
  - `Tactics.Logic/State/BattleResult.cs`
  - `Tactics.Logic.Tests/Support/Replay.cs`
- Test: `Tactics.Logic.Tests/Rules/{DownedTests.cs, BattleEndTests.cs, SkipTests.cs, InvariantTests.cs}`, `Tactics.Logic.Tests/Golden/{GoldenTests.cs, skirmish-basic.replay.json, skirmish-basic.events.json}`

**Interfaces:**
- Consumes: Tasks 4–7.
- Produces:

```csharp
public enum BattleOutcome { Won, Lost, Fled }
public sealed record RecordedInjury(UnitId Unit, InjuryId Injury);
public sealed record ItemUse(UnitId Unit, ItemId Item, bool ByAi);
public sealed record BattleResult(BattleOutcome Outcome, ImmutableSortedDictionary<UnitId, int> Xp,
    ImmutableSortedDictionary<UnitId, ImmutableArray<InjuryId>> Injuries,
    ImmutableSortedDictionary<UnitId, ImmutableArray<ItemUse>> ItemsUsed, ImmutableArray<LootTableId> Loot,
    ImmutableArray<string> FlagsSet);
public static class Battle { public static BattleResult? Result(BattleState state, RulesPack rules); }   // non-null once Outcome set
public sealed class RulesInvariantException(string message) : Exception(message);
public static class Invariants { public static void Check(BattleState s, RulesPack rules); }   // run at the end of every Apply (DEBUG and tests: always)
// Test support
public sealed record ReplayFile(string Name, ulong Seed, string Setup /* TestBattle preset name */, ImmutableArray<Command> Commands);
public static class Replay { public static (BattleState Final, ImmutableArray<BattleEvent> Events) Run(ReplayFile file, bool roundTripEachStep); }
```

- **Downed rules** follow spec §3.8 and the Global Constraints values.
  - The injury is chosen with `RulesPack.Injuries` (weighted) and the battle RNG.
  - Until Task 13 adds `injuries.json`, `InMemory` packs carry a single test injury.
- **`Help` revive** is the built-in ability `common.help`: an adjacent ally that's Downed gets up at 1 HP; otherwise it gives the ally advantage on their next roll, through the `helped` condition.
- **Turn skipping (Review Focus 1).** A unit whose turn comes up is skipped if it is Downed, Out or Dead, or its modifiers say `Incapacitated`. Skipping emits `TurnSkipped(UnitId Unit, string Reason)` and still runs `OnTurnEnd` condition ticks for incapacitated units, so a stun can wear off.
- **Battle end (Review Focus 4).** Objectives are checked after every step. Once an `Outcome` is set, the queue is cleared, `BattleEnded(BattleOutcome Outcome)` is emitted exactly once, and every later command is rejected with `BattleOver`.
- **Invariants:**
  - every Up unit is on a walkable, in-bounds tile, with at most one unit per tile;
  - HP is between 0 and max;
  - no resource is negative;
  - `Order` contains every Up or Downed unit exactly once;
  - `TurnIndex` is in range.
- **Events:** `Downed(UnitId Unit)`, `CountdownTicked(UnitId Unit, int Left)`, `Revived(UnitId Unit, int Hp)`, `UnitOut(UnitId Unit, InjuryId Injury)`, `Died(UnitId Unit)`, `TurnSkipped`, `BattleEnded`.

- [ ] **Step 1: Write `DownedTests`:**
  - `Zero_hp_party_member_downed_with_countdown_3`.
  - `Countdown_ticks_at_round_start`.
  - `Hit_while_downed_minus_1_crit_minus_2`.
  - `Heal_revives_with_healed_hp`.
  - `Help_revives_at_1hp`.
  - `Countdown_0_out_with_weighted_injury`.
  - `Enemy_at_zero_dies`.
- [ ] **Step 2: Write `SkipTests`:** `Downed_unit_turn_is_skipped_in_same_apply` and `Stunned_unit_skipped_and_save_rolled_at_turn_end`.
- [ ] **Step 3: Write `BattleEndTests`:**
  - `Last_enemy_dies_won`.
  - `Fireball_downing_last_two_party_members_ends_once_and_drops_queue`: one `BattleEnded`, the remaining targets are untouched.
  - `Commands_after_end_rejected_BattleOver`.
  - `Result_gives_full_xp_to_every_party_member_including_ai_controlled`.
- [ ] **Step 4: Write `InvariantTests.Corrupted_state_throws`** (two units on one tile, built by hand).
- [ ] **Step 5: Write `GoldenTests`:**
  - `Every_replay_matches_events`: for each `Golden/*.replay.json`, run with `roundTripEachStep: true` and compare the events JSON to `*.events.json`. With `UPDATE_GOLDENS=1` set, rewrite the file instead.
  - `Replay_is_deterministic`: two runs give identical final state JSON.
  - Record the `skirmish-basic` fixture: 2 test heroes vs 3 goblins, 10–20 commands covering move, attack, a reaction, a down and a revive.
- [ ] **Step 6: Run the tests.** Expected: FAIL.
- [ ] **Step 7: Implement.**
- [ ] **Step 8: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 9: Commit** `feat(tactics): downed and out, battle end, invariants and golden replays`.

---

## Phase 2: Content system

### Task 9: Pack loader, validation and the content hash

**Files:**
- Create:
  - `Tactics.Logic/Content/{PackLoader.cs, Validation.cs, ContentError.cs, ContentHash.cs}`
  - `Tactics.Logic.Tests/Support/ContentPaths.cs`
  - `content/tactics/base/rules/manifest.json` (empty lists)
  - `content/tactics/base/rules/schema/*.schema.json` (one per file kind, documentation only)
- Modify: `Tactics.Logic/Content/RulesPack.cs` (add `Load`), `Defs.cs` (all remaining defs)
- Test: `Tactics.Logic.Tests/Content/{LoaderTests.cs, ValidationTests.cs}` with fixtures in `Tactics.Logic.Tests/Content/Fixtures/<case>/`

**Interfaces:**
- Consumes: every Def type so far.
- Produces:

```csharp
public sealed record ContentError(string File, string Pointer, string Message);   // ToString(): "file#pointer: message"
public sealed class ContentException(ImmutableArray<ContentError> errors) : Exception(...) { public ImmutableArray<ContentError> Errors { get; } }
public sealed record PackManifest(string Id, int RulesVersion, ImmutableArray<string> Requires, ImmutableArray<string> Files);
public sealed partial class RulesPack
{
    /// <summary>Loads packs in dependency order ("base" first). Throws ContentException with every error found.</summary>
    public static RulesPack Load(IEnumerable<string> packDirectories, ArtManifest? art = null);
    public ImmutableSortedDictionary<string, ClassDef> Classes { get; }  /* + Races, Backgrounds, Monsters, Items, Maps,
        Encounters, AiProfiles, Injuries, Xp, Conditions, Passives, Abilities */
}
public sealed record ArtManifest(ImmutableSortedSet<string> Keys);  // "signature.radiant_bolt", "characters.goblin", …
public static class ArtManifests { public static ArtManifest Load(string contentBaseDir); }  // reads each set's manifest.json
public static class ContentHash { public static string Of(IEnumerable<(string RelativePath, byte[] Bytes)> files); } // SHA-256 hex, sorted by "<pack>/<path>"
```

- **Remaining Defs** (all in `Defs.cs`; fields follow spec §4.5–4.8):
  - `ClassDef(Id, Name, Role, HitDie, Saves, ArmorProf, WeaponProf, SkillChoices, StartingKit, Resources: ImmutableSortedDictionary<string, ByLevel<int>>, Features: ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>>, Subclasses, DefaultPicks: ImmutableSortedDictionary<int, ImmutableArray<AbilityId>>, AiTags, CastingStat, Jump)`.
  - `FeatureRef` is one of `{ability}`, `{passive}`, `{choose: n, from: [...]}`, `{resourceRegen}` or `{attacks: n}`.
  - `SubclassDef(Id, Name, Features at 3/6/10, Companion?)`, `RaceDef`, `BackgroundDef`, `MonsterDef`, `ItemDef(Id, Name, Use: AbilityId, Tags)`, `MapDef` (JSON form of `BattleMap` with base64 tiles).
  - `EncounterDef`, `ObjectivesDef`, `TriggerDef`, `AiProfileDef`, `InjuryDef(Id, Name, Weight, StaticMods)`, `XpTable`.
- **Validation checks** (spec §4.10). Each failure is a `ContentError` with a JSON pointer:
  - every id resolves;
  - a duplicate id without `replaces` is an error naming both files;
  - `byLevel` covers level 1 wherever it's used;
  - every class has features at levels 1–10;
  - each subclass has features at 3, 6 and 10;
  - no unknown block keys or hooks;
  - range ≤ 12 and area size ≤ 6;
  - `presentation` keys exist in the `ArtManifest` (skipped when `art` is null);
  - every zone and unit tag that triggers reference exists in their encounter;
  - map tile bytes match `w × h × 3`;
  - every material index is in range.

- [ ] **Step 1: Write the fixtures and `LoaderTests`:**
  - `Loads_minimal_pack`.
  - `Chapter_adds_ability`.
  - `Chapter_replaces_with_explicit_replaces`.
  - `Hash_is_stable_and_order_independent_of_file_listing`.
  - `Hash_changes_when_any_byte_changes`.
- [ ] **Step 2: Write `ValidationTests`.** Each fixture case asserts the exact `ContentError` (file, pointer, message):
  - `Unknown_block_key`.
  - `Unresolved_condition_id`.
  - `Duplicate_without_replaces_names_both_files` (Review Focus 5).
  - `Class_missing_level_7_features`.
  - `Subclass_missing_level_6`.
  - `Missing_art_key`.
  - `Map_bytes_length_mismatch`.
  - `Collects_all_errors_not_just_first`.
- [ ] **Step 3: Run the tests.** Expected: FAIL.
- [ ] **Step 4: Implement.** `PackLoader` parses with `JsonDocument` to track pointers, then deserialises each element with `ArcJson.Options` plus `EffectJsonConverter`.
- [ ] **Step 5: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(tactics): content pack loader, validation and content hash`.

### Task 10: The remaining effect blocks, zones, summons and named hooks

**Files:**
- Create: `Tactics.Logic/Effects/Blocks/{MapBlocks.cs, Units.cs, Combat.cs, TurnOrderBlocks.cs, Flow.cs}`, `Tactics.Logic/Effects/NamedHooks/{INamedHook.cs, WildShape.cs, Counterspell.cs, Polymorph.cs, AnimateDead.cs}`
- Modify: `Tactics.Logic/Effects/Blocks/{Hp.cs, ConditionBlocks.cs}` (add `tempHp`, `revive`, `cleanse`, `dispel`), `BattleState` (`Zone`)
- Test: `Tactics.Logic.Tests/Effects/{MapBlockTests.cs, UnitBlockTests.cs, CombatBlockTests.cs, TurnOrderBlockTests.cs, FlowBlockTests.cs, NamedHookTests.cs}`

**Interfaces:**
- Consumes: `IEffectBlock`, `EffectContext`, `BlockRegistry`.
- Produces: one `EffectDef` record and one block per JSON key. Keys added here:

| Key | Def (fields) |
|---|---|
| `tempHp` | `ByLevel<Dice> Dice, ImmutableArray<string> Bonus` |
| `revive` | `int Hp` |
| `cleanse` | `ImmutableSortedSet<string> Tags` |
| `dispel` | `bool Concentration, bool Zones` |
| `zone` | `string Kind, AreaDef Area, int? Rounds, bool Concentration, bool Difficult, ImmutableArray<PassiveDef> Passives /* OnEnterTile, OnTurnStart */` |
| `alterTerrain` | `AreaDef Area, int Delta /* ±levels, clamp 0–9 */` |
| `hazard` | `string HazardArt, AreaDef Area, int Rounds, ImmutableArray<EffectDef> OnEnter` |
| `summon` | `string Monster, int Count, int? Rounds, bool Concentration, string? Zone` |
| `transform` | `string Hook, JsonElement Args` |
| `grantResource` | `string Resource, int Amount` |
| `extraAction` | `string Kind /* action|bonus */` |
| `refreshReaction` | none |
| `statMod` | `StaticMods Mods, int Rounds, bool Concentration` |
| `mark` | `int Rounds` |
| `taunt` | `int Rounds` |
| `reveal` | `AreaDef Area` |
| `turnOrder` | `string Op /* delay|hasten|swap */, int Slots` |
| `chain` | `int Times, int Range, ImmutableArray<EffectDef> Effects` |
| `repeat` | `int Times, ImmutableArray<EffectDef> Effects` |
| `if` | `WhenDef When, ImmutableArray<EffectDef> Then, ImmutableArray<EffectDef> Else` |
| `random` | `ImmutableArray<(int Weight, ImmutableArray<EffectDef> Effects)> Table` |

```csharp
public sealed record Zone(int Serial, UnitId? Owner, string Kind, ImmutableArray<Tile> Tiles, int? RoundsLeft,
                         bool Concentration, bool Difficult, ImmutableArray<PassiveDef> Passives);
public interface INamedHook { string Name { get; } void Run(JsonElement args, EffectContext ctx); void Validate(JsonElement args, Action<string> error); }
```

- **Summons and removal (Review Focus 2):**
  - A summoned unit gets the id `"<owner>.<monster>.<serial>"`, its `SummonedBy` set and the owner's `Controller`. It joins `Order` **immediately after its owner**, starting next round.
  - When the owner's concentration ends or `Rounds` runs out, the summon is removed.
  - **Removing any unit from `Order`** shifts `TurnIndex` down by 1 when the removed unit sits before the current index, so no turn is repeated or skipped.
- **`wild_shape`** swaps the unit's sheet for a beast sheet: the monster id in its args, with `tempHp` equal to the beast's HP. The beast sheet keeps the druid's INT, WIS and CHA. When the temp HP runs out, the druid reverts.
- **`counterspell`** is a reaction to the `spellCast` trigger. It cancels the queued `RunEffects` for that ability when `tier ≤ 3`; a higher tier needs a check of d20 + `mod.cast` against `10 + tier`.
- **`polymorph`** gives the target a `sheep` sheet with `tempHp` 10 and a WIS save each turn end.
- **`animate_dead`** summons a `skeleton` from a Dead enemy tile within range.
- **Events:** `ZoneCreated(Zone Zone)`, `ZoneEnded(int Serial)`, `TerrainAltered(ImmutableArray<Tile> Tiles, int Delta)`, `Spawned(UnitId Unit, Tile At, string? Sprite)`, `Despawned(UnitId Unit)`, `Transformed(UnitId Unit, string Into)`, `TurnOrderChanged(ImmutableArray<UnitId> Order)`, `Marked(UnitId Unit, UnitId By)`.

- [ ] **Step 1: Write one test class per group.** Each block gets at least one behaviour test, including:
  - `Zone_fire_damages_on_enter_and_turn_start`.
  - `Alter_terrain_raises_and_reprojects_units_on_top`.
  - `Summon_acts_after_owner_next_round`.
  - **Review Focus 2:** `Removing_summon_before_current_index_keeps_next_turn_correct` and `Removing_unit_after_current_index_keeps_next_turn_correct`.
  - `Concentration_break_despawns_summon`.
  - `Chain_bounces_to_nearest_new_targets_in_range`.
  - `If_else_branches_on_target_condition`.
  - `Random_table_weighted_by_rng`.
  - `TurnOrder_swap_changes_this_round`.
  - `Wild_shape_reverts_at_zero_temp_hp`.
  - `Counterspell_cancels_tier_2`.
  - `Every_registered_key_has_a_test`: reflection over `BlockRegistry.Keys` against a list of tested keys.
- [ ] **Step 2: Run the tests.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics): remaining effect blocks, zones, summons and named hooks`.

### Task 11: Tooltip generation

**Files:**
- Create: `Tactics.Logic/Content/Tooltips.cs`
- Test: `Tactics.Logic.Tests/Content/TooltipTests.cs`

**Interfaces:**
- Produces: `public static class Tooltips { public static string For(AbilityDef a, int level, RulesPack rules); public static string For(PassiveDef p, int level, RulesPack rules); public static string For(ConditionDef c, RulesPack rules); }`.
  - Each block registers a phrase through `IEffectBlock.Describe(EffectDef def, int level, RulesPack rules) → string?`. Add this member to `IEffectBlock`.
  - A block that returns `null` makes `For` throw `InvalidOperationException` naming the block key. This is what Task 13's content tests rely on.
- **Exact formats:**
  - **Header:** `"{Kind} {attack|save}, {range} tiles"`.
  - **Area suffix:** `", {shape} {size}"`.
  - **Effects:** `": "` + effects joined with `"; "`.
  - **Flavour:** appended as `" — " + Flavor` when present.
  - **Spec §4.2 example at level 1:** `"Spell attack, 6 tiles: 4d6 radiant; target is Lit for 1 round"`.

- [ ] **Step 1: Write the failing tests:**
  - `Guiding_bolt_level_1_exact_text`.
  - `Guiding_bolt_level_5_shows_5d6`.
  - `Fireball_save_half_burst_2`.
  - `Sneak_attack_passive_text`.
  - `Missing_describe_throws_naming_block`.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics): tooltips generated from effect blocks`.

### Task 12: Sheet builder and encounter setup

**Files:**
- Create: `Tactics.Logic/Content/{SheetBuilder.cs, Encounters.cs}`
- Test: `Tactics.Logic.Tests/Content/{SheetBuilderTests.cs, EncounterSetupTests.cs}`

**Interfaces:**
- Consumes: the `RulesPack` defs.
- Produces:

```csharp
public sealed record CharacterRecord(string Name, string Race, string Class, string? Subclass, string Background,
    int Level, ImmutableSortedDictionary<Stat, int> BaseScores /* point-buy, before background */,
    ImmutableSortedDictionary<Stat, int> Increases /* levels 4/8 */, ImmutableArray<AbilityId> Picks /* chosen spells etc. */,
    ImmutableArray<InjuryId> Injuries, string Weapon, string Armor, bool Shield, ImmutableArray<ItemStack> Bag);
public static class SheetBuilder
{
    public static UnitSheet ForCharacter(CharacterRecord c, RulesPack rules);   // throws ContentException on bad ids
    public static UnitSheet ForMonster(string monsterId, RulesPack rules, int? level = null);
    public static CharacterRecord DefaultCharacter(string name, string race, string cls, string? subclass, int level, RulesPack rules);
}
public sealed record PartyMember(UnitId Id, CharacterRecord Character, Controller Controller);
public static class Encounters { public static BattleSetup Setup(string encounterId, ImmutableArray<PartyMember> party, Difficulty d, RulesPack rules); }
```

- **`ForCharacter`** applies:
  - background boosts;
  - increases;
  - race traits (Move, Jump, resistances, racial ability);
  - HP as in the Global Constraints;
  - AC from armour, DEX cap and shield;
  - Initiative bonus (heavy armour −2);
  - class resources `byLevel`;
  - features up to the character's level;
  - the subclass features at 3, 6 and 10 at or below the level;
  - `Picks`, falling back to `DefaultPicks` when empty;
  - injuries as static mods.
- **`DefaultCharacter`** uses the standard array `15, 14, 13, 12, 10, 8`, assigned in the class's `aiTags` priority order (the order is in `ClassDef.AiTags.Priority`).
- **`Encounters.Setup`** places the party members on `party_spawn` tiles in order and the enemies at their tiles or zones. It returns a `BattleSetup` with the encounter's objectives, triggers, XP and loot.

- [ ] **Step 1: Write the tests, using a fixture pack with one class, race and background:**
  - `Level_1_hp_is_full_die_plus_con`.
  - `Level_5_hp_adds_average_per_level`.
  - `Proficiency_by_level`.
  - `Medium_armor_caps_dex_2`.
  - `Heavy_armor_initiative_minus_2`.
  - `Dwarf_move_3`.
  - `Features_only_up_to_level`.
  - `Subclass_features_at_3_6_10`.
  - `Injury_applies_static_mods`.
  - `Unknown_race_throws_content_error`.
  - `Encounter_places_party_on_spawn_tiles`.
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics): sheet builder and encounter setup`.

### Task 13: Content wave 0: core files and the first four classes

**Files:**
- Create under `content/tactics/base/rules/`:
  - `conditions.json`, `races.json`, `backgrounds.json`, `injuries.json`, `xp.json`, `items.json`
  - `abilities/{common, races, fighter, cleric, rogue, wizard}.json`
  - `classes/{fighter, cleric, rogue, wizard}.json`
  - `monsters/{goblinoids, undead, beasts-1}.json` (10 monsters)
  - `maps/river_crossing.json`, `encounters/river_crossing.json`
- Modify: `manifest.json` (list the files)
- Test: `Tactics.Logic.Tests/Content/BasePackTests.cs`, `Golden/river-crossing.replay.json` + `.events.json`

**Content:**
- **`conditions.json`:** the 12 SRD conditions plus burning, bleeding, slowed, hasted, marked, lit, taunted, helped, dodging and disengaged (the last two are what the Dodge and Disengage actions apply). Mechanics follow spec §3.7 and Task 7's `StaticMods`.
- **`races.json`:** all 11 races, with the racial abilities from spec §4.6.
- **`backgrounds.json`:** 12 backgrounds: acolyte, criminal, folk hero, noble, sage, soldier, outlander, entertainer, guild artisan, hermit, sailor and urchin. Each gets a +2/+1 pair and two skills.
- **`injuries.json`:** 8 injuries:

| Injury | Weight | Effect |
|---|---|---|
| Cracked ribs | 3 | Max HP −5 |
| Sprained ankle | 3 | Move −1 |
| Concussion | 2 | −1 to INT and WIS saves |
| Deep cut | 2 | Start each battle bleeding for 1 round |
| Wrenched shoulder | 2 | −1 to attack |
| Black eye | 3 | Disadvantage on Perception (sub-project 5) |
| Bruised pride | 3 | −1 Initiative |
| Shaken | 2 | Frightened at the start of the first round |

- **`xp.json`:** the thresholds from the Global Constraints.
- **`items.json`:**

| Item | Effect |
|---|---|
| Potion of healing | 2d4+2, tag `healing` |
| Greater healing | 4d4+4, tag `healing` |
| Alchemist's fire | Ranged 4, 1d4 fire, applies burning |
| Smoke bomb | Burst 1 zone that blocks line of sight, 2 rounds |

- **`abilities/common.json`:** attack, dash, disengage, dodge, help, shove and `use_item`. Loaded packs take these from `common.json`. `RulesPack.InMemory` keeps its C# copies for unit tests, and `BasePackTests.Builtins_match_common_json` checks that the two agree.
- **Classes:** the four classes follow the SRD 5.2 progression to level 10 under the content conventions, with one subclass each: Vanguard, Life Domain, Shadow and Evoker. Every level from 1 to 10 has at least one feature, and `defaultPicks` covers every level.
- **Monsters:** goblin, goblin archer, goblin boss, hobgoblin, skeleton, zombie, wolf, dire wolf, giant spider and kobold. All have `sprite` keys from the art manifests and `aiProfile` set.
- **Map and encounter:** `river_crossing`, a 14×12 map built from the spec §4.8 example (a ford, a ridge, crates), with 6 goblins against a level-1 party. XP 250.

- [ ] **Step 1: Write `BasePackTests`:**
  - `Base_pack_loads_with_zero_errors_including_art_keys`.
  - `Every_ability_passive_and_condition_has_a_tooltip_at_levels_1_to_10`.
  - `Every_class_builds_a_default_character_at_levels_1_to_10`.
  - `Every_monster_builds`.
  - `River_crossing_sets_up_for_4_5_and_6_party_members`.
  - `Builtins_match_common_json`.
- [ ] **Step 2: Run them.** Expected: FAIL (no content yet).
- [ ] **Step 3: Write the content files.**
- [ ] **Step 4: Run the tests and fix the content until they pass.**
- [ ] **Step 5: Record the `river-crossing` golden replay.** Use 4 default characters (fighter, cleric, rogue, wizard), seed 7, and hand-written commands for the first 2 rounds.
- [ ] **Step 6: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 7: Commit** `feat(tactics-content): core rules files and fighter, cleric, rogue, wizard`.

---

## Phase 3: AI and simulator

### Task 14: The planner, odds and threat map

**Files:**
- Create: `Tactics.Logic/Ai/{Ai.cs, Planner.cs, Scoring.cs, Odds.cs, ThreatMap.cs, AiProfile.cs}`, `content/tactics/base/rules/ai-profiles.json`
- Test: `Tactics.Logic.Tests/Ai/{OddsTests.cs, PlannerTests.cs}`

**Interfaces:**
- Consumes: `BattleState`, `RulesPack`, `Paths`, `Sight`, `Reach`, `Areas`, `Modifiers`.
- Produces:

```csharp
public sealed record AiProfile(string Id, double Damage, double Kill, double Control, double Heal, double Revive,
    double Caution, double FocusFire, double Objective, double Hoard, bool Cruel, double FleeBelowHp);
public sealed record TurnPlan(ImmutableArray<Command> Commands, double Score, string Explain);   // Explain for logs/tests
public static class Ai
{
    public static TurnPlan Plan(BattleState s, UnitId unit, RulesPack rules, AiOptions? options = null);
    public static ReactionChoice React(BattleState s, ReactionOffer offer, RulesPack rules);       // Task 15
}
public sealed record AiOptions(bool AllowItems = false, bool AllowDelay = true, bool RemainingOnly = false,
                               string? ProfileOverride = null);
public static class Odds
{
    public static double Hit(int modifier, int ac, Edge edge);                 // P(d20+mod ≥ ac), nat1 miss, nat20 hit
    public static double Crit(Edge edge);
    public static double SaveFail(int saveBonus, int dc, Edge edge);
    public static double ExpectedDamage(Dice dice, double pHit, double pCrit);
}
public sealed class ThreatMap { public static ThreatMap For(BattleState s, Side against, RulesPack rules); public double At(Tile t); }
```

- **Planning steps:**
  1. Reachable tiles from `Paths.From` with `MoveLeft`.
  2. Positional score per tile: cover from the nearest threats, high ground, flank setup, distance to allies who heal, and `−Caution × threat`. Keep the 12 best plus the current tile.
  3. For each kept tile, each affordable ability × each legal target or aim (area aims are limited to tiles within range that cover at least one enemy), score: EV(damage, kill, control, heal, revive) + position + objective − `Hoard × resource cost`.
  4. Also score "act from the current tile, then move to the best remaining tile".
  5. Add the best affordable bonus action greedily.
  6. Ties break on the ordinal (ability id, tile, target id).
- **Fair play:** kill and damage value against a `Downed` target is 0 unless `Cruel`.
- **Difficulty multipliers:**

| Setting | FocusFire | Caution | Hoard |
|---|---|---|---|
| Story | ×0.5 | ×0.5 | ×1.5 |
| Normal | ×1 | ×1 | ×1 |
| Hard | ×1.5 | ×1.25 | ×0.75 |

- **`ai-profiles.json`:** brute, skirmisher, archer, caster, healer, coward (`FleeBelowHp` 0.3), boss and boss_cruel, with their weights. Companion is added in Task 15.

- [ ] **Step 1: Write `OddsTests`:**
  - `Hit_plus5_vs_ac15_is_55_percent`.
  - `Advantage_raises_to_79.75_percent`.
  - `Nat20_always_hits_even_vs_ac30` (0.05).
  - `Crit_with_advantage_is_9.75_percent`.
  - `Save_fail_matches_formula`.
- [ ] **Step 2: Write `PlannerTests`** with ASCII scenarios:
  - `Archer_takes_high_ground_and_cover`.
  - `Brute_finishes_low_hp_enemy`.
  - `Healer_heals_ally_below_half`.
  - `Avoids_ending_on_high_threat_tile_when_cautious`.
  - `Never_targets_downed_unless_cruel`.
  - `Fireball_aim_avoids_allies`.
  - `Coward_flees_below_30_percent`.
  - `Plan_is_deterministic` (run 3 times, same commands).
  - `Every_plan_command_is_accepted_by_Apply` (fuzz: 50 seeds on river_crossing, apply each command, no `Rejection`).
- [ ] **Step 3: Run the tests.** Expected: FAIL.
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(tactics): utility AI planner, odds and threat map`.

### Task 15: Companion AI, timed-out turns, reactions and the performance budget

**Files:**
- Create: `Tactics.Logic/Ai/Companion.cs`
- Modify: `ai-profiles.json` (add `companion`), `Ai.cs` (`React`), `Rules/Reactions.cs` (AI reactors resolved through `Ai.React`)
- Test: `Tactics.Logic.Tests/Ai/{CompanionTests.cs, TimeoutTests.cs, AiReactionTests.cs, AiPerformanceTests.cs}`

**Interfaces:**
- Consumes: Task 14.
- Produces: no new public types.
  - **Companion behaviour** runs when the unit's `Controller` is `AiControlled("companion")`.
  - **Timed-out turns:** the adapter calls `Ai.Plan(state, unit, rules, new AiOptions(AllowItems: false, AllowDelay: false, RemainingOnly: true, ProfileOverride: "companion"))`. Every event the resulting commands produce must carry `AiFinished = true`. Add `bool AiFinished` to `Battle.Apply` as an optional parameter that is stamped onto the events.
- **Companion rules (spec §5.5):**
  - Revive weight is the highest.
  - Protecting the healer and protected NPCs adds to positional value.
  - Role weights come from the class `aiTags`.
  - **Items:** only items tagged `healing` from the unit's own `Bag`, only when `ItemsUsedByAi == 0` and the threat map's expected damage at the unit's tile next round is at least its current HP + temp HP. Using one increments `ItemsUsedByAi` and records `ItemUse(ByAi: true)`.
- **`Ai.React`:**
  - an opportunity attack when its EV is above 0;
  - a defensive reaction when it turns the pending hit into a miss (against the known roll) or prevents at least 25% of max HP;
  - otherwise `pass`.

- [ ] **Step 1: Write `CompanionTests`:**
  - `Revives_downed_ally_first`.
  - `Uses_one_healing_potion_only_when_threat_would_down_it`.
  - `Never_uses_second_item_in_same_battle`.
  - `Never_uses_non_healing_item`.
  - `Spends_class_resources_freely`.
- [ ] **Step 2: Write `TimeoutTests`:**
  - `Finishes_only_remaining_economy` (the action is already used, so the plan has only a move and a bonus).
  - `No_items_no_delay`.
  - **Review Focus extra:** `Nothing_left_returns_single_EndTurn`.
  - `Events_flagged_AiFinished`.
- [ ] **Step 3: Write `AiReactionTests`:** `Takes_opportunity_attack`, `Shield_when_it_turns_hit_to_miss`, `Passes_when_shield_wouldnt_matter`.
- [ ] **Step 4: Write `AiPerformanceTests.P95_under_250ms_on_6_vs_10`.** Build a 6-hero vs 10-monster setup on `river_crossing` at level 10, warm up 5 plans, time 100 plans across units, and assert p95 ≤ 250 ms. Mark it `[Trait("Category","Perf")]`; it runs in the normal suite.
- [ ] **Step 5: Run the tests.** Expected: FAIL.
- [ ] **Step 6: Implement.**
- [ ] **Step 7: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 8: Commit** `feat(tactics): companion AI, timed-out turns and AI reactions`.

### Task 16: `Tactics.Sim` and the first balance report

**Files:**
- Create: `games/Tactics/Tactics.Sim/{Tactics.Sim.csproj, Program.cs, SimRunner.cs, Report.cs}`
- Modify: `ArcBox.slnx`
- Test: `Tactics.Logic.Tests/Ai/SimSmokeTests.cs` (references Sim)
- Output: `docs/superpowers/reports/2026-10-30-tactics-balance-v0.md` (use the run date)

**Interfaces:**
- Produces:

```csharp
public sealed record SimConfig(ImmutableArray<string> Encounters, ImmutableArray<int> Levels, int PartiesPerSize,
                               ImmutableArray<int> PartySizes, int Seeds, Difficulty Difficulty, int RoundCap = 30);
public sealed record SimBattle(string Encounter, int Level, ImmutableArray<string> Classes, ulong Seed, BattleOutcome Outcome,
    int Rounds, int Downs, int Outs, ImmutableSortedDictionary<string, ClassStats> PerClass,
    ImmutableSortedDictionary<AbilityId, (int Used, int Legal)> Abilities, int AiItemUses);
public sealed record ClassStats(double DamagePerRound, double HealingPerRound, double MitigatedPerRound, double TurnsDenied);
public static class SimRunner { public static IEnumerable<SimBattle> Run(SimConfig config, RulesPack rules); }
public static class Report { public static string Markdown(IReadOnlyList<SimBattle> battles, SimConfig config); }
```

- **Command line:** `dotnet run --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --difficulty Normal --out docs/superpowers/reports/<date>-tactics-balance.md`.
- **Party sampling:** the sampled class combinations always include at least one healer or defender, use a fixed `Rng` seed and are listed in the report.
- **Both sides are AI.** Party members use their class's profile (`caster`, `healer`, `brute`, `skirmisher` or `archer`, from `aiTags`).
- **A battle stops** at `RoundCap`, counted as `Lost` and flagged in the report.
- **Report sections:**
  1. Summary.
  2. Win rate by encounter and level, with flags for < 60% and > 95%.
  3. Rounds per battle, compared with the 8–12 target.
  4. Role bands: a table of per-class value against the band mean, with ±15% flags.
  5. Abilities used in < 2% of legal turns.
  6. Downs, Outs and AI item use.
  7. Configuration and seeds.

- [ ] **Step 1: Write `SimSmokeTests.Every_encounter_ends_with_3_seeds`** (level 1, party size 4, `RoundCap` 30). Every battle has an outcome, and the report renders with all 7 headings.
- [ ] **Step 2: Run the test.** Expected: FAIL.
- [ ] **Step 3: Implement the Sim.**
- [ ] **Step 4: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 5: Run the full sweep for the 4 classes.** Commit the report as `-v0` along with any obvious data fixes it shows (for example, an ability nobody uses because its cost is wrong). Deeper tuning waits for Task 23.
- [ ] **Step 6: Commit** `feat(tactics): balance simulator and first report`.

---

## Phase 4: Content waves

Every content task follows the **content conventions** in Global Constraints. It finishes with `BasePackTests` green, which already covers every class and subclass at levels 1–10, tooltips and default characters. It also runs the Sim for its classes at levels 1, 5 and 10 with 5 seeds and fixes anything that's flagged before committing.

### Task 17: Ranger, paladin and monk

**Files:** `content/tactics/base/rules/{classes,abilities}/{ranger,paladin,monk}.json`, `manifest.json`
- **Classes:**
  - **Ranger.** Focus 2→6. Hunter's Mark (bonus action, concentration, +1d6 on hit). Archery style (+2 ranged attack). Extra Attack at 5. Vanish at 10. Half-caster list.
  - **Paladin.** Oath 2→8. Lay on Hands pool of 5 × level per battle. Divine Smite: an `onAttackHit` passive spends Oath to add 2d8 radiant, +1d8 per extra Oath, up to 3. Aura of Protection at 6 (allies within burst 2 add CHA mod to saves). Extra Attack at 5. Half-caster list.
  - **Monk.** Ki 2→10. Martial Arts (unarmed d6→d8 at 5; bonus unarmed attack). Flurry of Blows (1 Ki: 2 bonus attacks). Step of the Wind (1 Ki: dash or disengage as a bonus action, Jump +2). Stunning Strike at 5 (1 Ki, CON save or stunned until your next turn). Evasion at 7. Jump 3.
- [ ] **Step 1:** Write the three class and ability files with no subclasses (they're Task 19's). Temporarily set `"subclassesPending": true`, which validation accepts only while this task and Task 19 are open; Task 19 removes the flag and its support.
- [ ] **Step 2:** Run `dotnet test ArcBox.slnx` and the Sim at levels 1, 5 and 10. Fix the flags.
- [ ] **Step 3: Commit** `feat(tactics-content): ranger, paladin and monk`.

### Task 18: Bard, druid and warlock

**Files:** `content/tactics/base/rules/{classes,abilities}/{bard,druid,warlock}.json`, `monsters/wildshape.json`, `manifest.json`
- **Classes:**
  - **Bard.** Inspiration 2→6 plus Mana (the bard/druid table). Bardic Inspiration (bonus action: an ally gets a d6 reaction die to add to an attack, save or AC; d8 at 5, d10 at 10). Song of Rest (healing added to `BattleResult`, applied by the adapter). Countercharm at 6 (an aura that gives advantage against charmed and frightened).
  - **Druid.** Wild charges 2→3 plus Mana. Wild Shape through `transform{hook: wild_shape}` into wolf (level 2), bear (level 4) or dire wolf (level 6), all from `monsters/wildshape.json` with the art's `wolf_wildshape` and `bear_wildshape` sprites. Thorn Whip (pull 2).
  - **Warlock.** Pact 2→4, which also **regains 1 at the start of every second round**: an `onRoundStart` passive with `if {round: even}`, using `grantResource`. Eldritch Blast: 1 beam, 2 at 5, 3 at 10; each beam is a separate attack. Hex (bonus action, concentration, +1d6 necrotic). Invocations as `choose` groups at 2, 5 and 9.
- [ ] **Step 1:** Write the content, with the same `subclassesPending` handling as Task 17.
- [ ] **Step 2:** Add `if` leaves `roundEven` and `roundAtLeast` to `WhenDef` if Task 10 didn't, with a test in `FlowBlockTests`.
- [ ] **Step 3:** Run `dotnet test ArcBox.slnx` and the Sim. Fix the flags.
- [ ] **Step 4: Commit** `feat(tactics-content): bard, druid and warlock`.

### Task 19: Martial subclasses (13) and the hound companion

**Files:** the `subclasses` sections of `classes/{fighter,rogue,ranger,paladin,monk}.json`, the matching `abilities/*.json`, `monsters/companions.json` (hound)
- **Subclasses:** Weaponmaster, Warlord, Trickster, Duelist, Sharpshooter, Beastmaster, Warden, Guardian, Crusader, Avenger, Open Hand, Windstep and Iron Body. Each has features at 3, 6 and 10 that deliver the identity in spec §4.5.
- **The Beastmaster's hound** is summoned at battle start by `onBattleStart → summon{monster: "hound"}`, with no `Rounds` and no concentration. It's controlled by the owner and acts right after them (Task 10 rule). It scales with the owner's level via `byLevel` in its stat block.
- [ ] **Step 1:** Write the subclass content and remove `subclassesPending` from these classes.
- [ ] **Step 2:** Add `CompanionTests.Hound_spawns_at_battle_start_and_acts_after_owner`.
- [ ] **Step 3:** Run `dotnet test ArcBox.slnx` and the Sim. Fix the flags.
- [ ] **Step 4: Commit** `feat(tactics-content): martial subclasses and the hound`.

### Task 20: Caster subclasses (13), the sapling and imp companions

**Files:** the `subclasses` sections of `classes/{wizard,cleric,bard,druid,warlock}.json`, the matching `abilities/*.json`, `monsters/companions.json` (sapling, imp)
- **Subclasses:** Chronomancer, Geomancer, War Domain, Light Domain, Valor, Lore, Siren, Shapeshifter, Stormcaller, Grovekeeper, Hexblade, Fiendbound and Void.
  - **Chronomancer** uses `turnOrder` and the hasted/slowed conditions.
  - **Geomancer** uses `alterTerrain`.
  - **Lore** gets `counterspell` (named hook) at 6.
  - **Shapeshifter** gets wild shape at level 3 with stronger forms.
- **Companions:** Grovekeeper gets the sapling, Fiendbound gets the imp, both summoned the same way as the hound.
- [ ] **Step 1:** Write the content and remove `subclassesPending`. Delete the flag's support from `Validation.cs`.
- [ ] **Step 2:** Add tests: `GeomancerTests.Raise_tile_blocks_los_next_attack` and `ChronomancerTests.Swap_moves_ally_turn_before_enemy`.
- [ ] **Step 3:** Run `dotnet test ArcBox.slnx` and the Sim. Fix the flags.
- [ ] **Step 4: Commit** `feat(tactics-content): caster subclasses, sapling and imp`.

### Task 21: Monster roster, maps and sample encounters

**Files:** `content/tactics/base/rules/monsters/*.json`, `abilities/monsters-*.json`, `maps/{forest_ruins, crypt_hall, crystal_cave}.json`, `encounters/{forest_ruins, crypt_hall, crystal_cave}.json`
- **Monsters:**
  - every enemy recipe, beast and boss in the art manifests (`characters`, `beasts`, `bosses`) gets a stat block from CR ¼ up to CR 10 for the dragons;
  - each has a `sprite` key and an `aiProfile`;
  - bosses get `phases`, written as triggers in their encounters (Task 22 uses them);
  - beasts reuse the existing wildshape stat blocks where their ids match.
- **Maps:** the three maps take their materials from the terrain, dungeon and town art ids, the same ones the dioramas use (`tools/tactics-assets/parts/dioramas.py` and `dungeon.py`).
- **Encounters:**

| Encounter | Party level | Setting |
|---|---|---|
| `forest_ruins` | 3 | Wolves and bandits |
| `crypt_hall` | 5 | Undead and a necromancer |
| `crystal_cave` | 7 | Kobolds, a spider and a chasm with `deep` tiles |

- [ ] **Step 1:** Write the content.
- [ ] **Step 2:** Extend `BasePackTests`: `Every_art_monster_has_a_stat_block` (compare against the art manifest's enemy, beast and boss ids) and `Every_encounter_sets_up_for_4_to_6`.
- [ ] **Step 3:** Run `dotnet test ArcBox.slnx` and the Sim on the new encounters. Fix the flags.
- [ ] **Step 4: Commit** `feat(tactics-content): monster roster and sample encounters`.

---

## Phase 5 and 6: Objectives, triggers and balance

### Task 22: Objectives, triggers and the showcase encounter

**Files:**
- Create: `Tactics.Logic/Rules/Triggers.cs`, `content/tactics/base/rules/{maps,encounters}/warlord_bridge.json`
- Modify: `Rules/Objectives.cs` (all objectives), `Content/Validation.cs` (trigger checks), `Content/Defs.cs` (`TriggerDef` and condition and action records)
- Test: `Tactics.Logic.Tests/Rules/{ObjectiveTests.cs, TriggerTests.cs}`, `Golden/warlord-bridge.replay.json` + `.events.json`

**Interfaces:**
- Produces: the objective grammar from spec §4.8 (`defeat_all`, `defeat:<tag>`, `survive:<n>`, `reach:<zone>:<k>`, `protect:<tag>`, `flag:<name>`, `party_out`, with `any`/`all`), and triggers with their conditions and actions, also from §4.8.
  - **Trigger checks** happen at round start, after each step and at turn end, in file order.
  - `once` defaults to `true`.
  - Actions run as steps pushed to the front of the queue.
  - A trigger's actions can't satisfy another trigger's condition within the same step: newly true conditions are checked at the next checkpoint.
- **Validation** (in addition to Task 9):
  - every tag, zone, flag, counter and objective name a trigger references exists in the encounter or is set by some trigger;
  - a flag read but never set is an error;
  - a `spawn` unit id exists.
- **Events:** `Said(string Speaker, string Line)`, `TriggerFired(string Id)`, `ObjectiveChanged(ObjectivesDef Objectives)`, `CameraHint(Tile Tile)`.

- [ ] **Step 1: Write `ObjectiveTests`:** one test each for `defeat_tag_wins`, `survive_8_wins_at_round_9_start`, `reach_exit_with_2_wins`, `protect_npc_out_loses`, `any_of_defeat_or_survive`, `flag_objective`.
- [ ] **Step 2: Write `TriggerTests`:**
  - `Round_3_spawns_and_says`.
  - `Hp_below_50_triggers_phase_2_once`.
  - `Enter_zone_collapses_bridge_with_alterTerrain`.
  - `Trigger_set_flag_seen_next_checkpoint_not_same_step`.
  - `Validation_rejects_flag_never_set`.
- [ ] **Step 3: Build the `warlord_bridge` showcase.**
  - A goblin warlord boss with two phases.
  - Archers that arrive at round 3.
  - A bridge that collapses (`alterTerrain −9` to `deep`) when 2 party members are on it.
  - Win: defeat the warlord or survive 8 rounds.
  - Record its golden replay with seed 11, a level-5 party of 5, and AI commands from `Ai.Plan` for both sides (the replay file stores the resulting commands).
- [ ] **Step 4: Run the tests.** Expected: FAIL until the code is implemented.
- [ ] **Step 5: Implement.**
- [ ] **Step 6: Run `dotnet test ArcBox.slnx`.** Expected: PASS.
- [ ] **Step 7: Commit** `feat(tactics): objectives, scripted triggers and the warlord bridge showcase`.

### Task 23: Balance pass and final report

**Files:** any `content/tactics/base/rules/**` numbers, `docs/superpowers/reports/<date>-tactics-balance.md`, `games/Tactics/README.md`
- [ ] **Step 1:** Run the full sweep: all encounters, levels 1, 3, 5, 7 and 10, party sizes 4, 5 and 6, 6 parties per size, 20 seeds, Normal.
- [ ] **Step 2:** Tune the data, only numbers in content (dice, resource tables, costs, AI weights), until:
  - every class is within ±15% of its role band at every level;
  - no ability is under 2% use (or it's documented in the report as situational, with the reason);
  - every sample encounter is won 60–95% of the time on Normal at its level;
  - median battle length is 8–12 rounds.
  - Re-record any golden replays whose events change, with `UPDATE_GOLDENS=1`, and review each diff.
- [ ] **Step 3:** Commit the final report, with a "What changed" section listing each tuning change and its reason. Update `games/Tactics/README.md`: how to run the Sim, how to add a chapter pack, and the content conventions (copied from this plan).
- [ ] **Step 4:** Check the spec's success criteria one by one in the report's summary (§1 criteria 1–6), with the evidence for each.
- [ ] **Step 5:** Run `dotnet test ArcBox.slnx`. Expected: PASS.
- [ ] **Step 6: Commit** `chore(tactics): balance pass and final report`.
