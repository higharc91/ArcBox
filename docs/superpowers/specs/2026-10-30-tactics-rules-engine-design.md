# ArcBox Tactics: Rules Engine Design Spec

- **Date:** 2026-10-30
- **Status:** Draft, awaiting review
- **Author:** higharc91 with Claude
- **Sub-project:** 2 of the ArcBox Tactics breakdown. Sub-project 1 ("Foundation") shipped as [Platform v2](2026-10-29-platform-v2-design.md).
- **Builds on:** [Platform v2](2026-10-29-platform-v2-design.md) (campaign documents, `IResumableGame`, content delivery, host signals). [Tactics mockups](../mockups/tactics/README.md). Art in `content/tactics/base/` (from `tools/tactics-assets`).

---

## 1. Purpose

ArcBox Tactics is a persistent co-op tactics RPG. A group of 4–6 plays for about 2 hours a week, on the TV, with each player controlling their character from their phone. This spec covers the **rules engine**. It is a pure C# library that runs battles:
- the d20 core;
- the grid;
- turn order;
- abilities, defined as data;
- the AI that plays enemies and absent players' characters;
- a simulator that balances it all.

It also defines the **rules content**: all 12 classes at levels 1–10, all 36 subclasses, 11 races, the backgrounds, a monster roster matching the art, and sample encounters.

### Decisions already made (from the brainstorming sessions)

| Area | Decision |
|---|---|
| Rules | A **5e chassis tuned for tactics**, based on the D&D 5e SRD 5.2 (CC-BY-4.0, attributed in §4.10). The changes: class resources refill **every battle**, a rest means healing between battles while injuries persist, and levels cap at **10** for the first campaign |
| Turn order | **Rounds ordered by Speed.** Everyone acts once per round, sorted by a fixed Initiative. This replaces the earlier FFT charge-time idea. The turn-order strip in mockup 01 now shows this round's order |
| Action economy | Move, Action, Bonus action, Reaction (as in mockups 01 and 04) |
| Positioning | **Height, flanking and cover.** No facing |
| Going down | **A visible 3-round countdown.** Revived by any healing or a Help action. At 0 the character is Out for the battle and gains an **Injury**. No permadeath |
| Turn timer | **60 s.** When it runs out, **the AI finishes the rest of that turn** |
| Absent players | The AI plays their character with class resources. It may use **one healing item per battle, only in an emergency**. It never spends points, picks a subclass or trades |
| Content scope | **All 12 classes, levels 1–10, all 36 subclasses**, in this sub-project. Barbarian and Sorcerer were added on 2026-10-30 to cover all 12 SRD 5.2 classes |
| Objectives | The **core five** (defeat all, defeat a target, survive N rounds, reach an exit, protect an NPC) plus **scripted triggers** |
| Authoring | Abilities and features are **JSON lists of effect blocks**. Only the blocks are code |

Earlier decisions recorded for context (owned by other sub-projects): light exploration with a rotating party leader; party votes with the leader breaking ties; the party picks who attempts a skill check; subclass picked at level 3 and locked; need/greed loot; personal inventories with trading; prewritten weekly chapter packs.

### Success criteria
1. Every class and subclass at levels 1–10 loads, validates, and has tooltip text generated from its data.
2. `Tactics.Sim` runs every sample encounter with mixed parties of 4–6, and every class's damage, healing and mitigation per round sits inside its role band (§5.6).
3. A battle saved after **any** command and restored finishes identically to one never saved: same states, same events.
4. An AI turn takes ≤ 250 ms at the 95th percentile in the worst sample encounter (6 heroes vs 10 enemies).
5. The showcase encounter's triggers fire as scripted, checked by a golden replay.
6. `dotnet test ArcBox.slnx` stays green.

---

## 2. Architecture

### 2.1 Projects

| Project | What it is | References |
|---|---|---|
| `games/Tactics/Tactics.Logic` | The rules engine: state, commands, resolution, geometry, content loader, AI | `ArcBox.Sdk` only for shared ids and JSON settings. No ASP.NET, no Godot |
| `games/Tactics/Tactics.Logic.Tests` | Unit, determinism, golden replay, content and AI performance tests | Logic, `ArcBox.Sdk.Testing` |
| `games/Tactics/Tactics.Sim` | A console app that plays AI-vs-AI battles and writes balance reports to `docs/superpowers/reports/` | Logic |

All three target `$(ArcSdkTargetFrameworks)` (net10.0), like `Redline.Logic`.

Rules content lives in **`content/tactics/base/rules/`** beside the art, so the Platform v2 content delivery (§3.6) serves one pack to both the server and the TV. Weekly chapters ship as further packs (§4.9).

```
content/tactics/base/rules/
  manifest.json           pack id, rulesVersion, file list
  conditions.json         SRD conditions + game extras
  races.json  backgrounds.json  injuries.json  xp.json  ai-profiles.json  items.json
  classes/<class>.json    one per class, its subclasses inline
  abilities/<owner>.json  abilities and passives, grouped by class/race/monster
  monsters/<family>.json  stat blocks
  maps/<id>.json          battle maps
  encounters/<id>.json    placements, objectives, triggers
  schema/*.schema.json    JSON Schemas for every file kind
```

### 2.2 Immutable state, pure transitions

```csharp
public static class Battle
{
    public static BattleState Start(BattleSetup setup, RulesPack rules, ulong seed);
    public static Resolution Apply(BattleState state, Command command, RulesPack rules);
}

public sealed record Resolution(BattleState State, ImmutableArray<BattleEvent> Events, Rejection? Rejected);
```

- **`BattleState`** is an immutable record graph:
  - **Units:** id, unit sheet snapshot, position, HP, temp HP, resources, conditions, economy used this turn, countdown.
  - **The battle:** map overlay (zones, altered heights), round, turn order and index, objective and flag state, the resolution queue, the RNG state, and the content hash.
  - It's built with `ImmutableArray` / `ImmutableDictionary` so that each transition shares structure with the previous state.
- **Commands** are what a phone or the AI sends:

```csharp
public abstract record Command(UnitId Actor);
public sealed record Move(UnitId Actor, ImmutableArray<Tile> Path) : Command(Actor);
public sealed record Use(UnitId Actor, AbilityId Ability, Target Target) : Command(Actor);   // abilities, items, Dash, Shove, Help…
public sealed record Delay(UnitId Actor, int Slots) : Command(Actor);
public sealed record React(UnitId Actor, ReactionChoice Choice) : Command(Actor);          // answers a Pending.Reaction
public sealed record EndTurn(UnitId Actor) : Command(Actor);
```

- **Events** are an ordered list. The TV animates them, the phone shows them, and golden tests diff them. Examples: `TurnStarted`, `Moved`, `Rolled{kind, dice, kept, modifier, vs, outcome}`, `Damaged`, `Healed`, `Pushed`, `Fell`, `ConditionApplied/Removed`, `ZoneCreated`, `TerrainAltered`, `Downed`, `CountdownTicked`, `Revived`, `UnitOut`, `Spawned`, `Said`, `ObjectiveChanged`, `TriggerFired`, `BattleEnded`.
  - Each event carries the presentation keys from the ability's data (animation, effect, projectile), so the TV needs no rules knowledge.
  - The adapter (sub-project 3) paces each event chain with the Platform v2 host signal "animation done" (§3.7).
- **The RNG is part of the state.** It's PCG32, with the state and increment stored as two `ulong`s. Every die rolled comes from it, so **the same state plus the same command always gives the same result.** Replays, golden tests, resume and the simulator all depend on this. `System.Random` is not used anywhere in Logic.

### 2.3 The resolution queue and reactions

An action is not resolved in one recursive call. `Apply` pushes **steps** onto a queue held in the state (`ImmutableStack<Step>`) and runs them until the queue empties or a step needs a decision:

1. `Use(Guiding Bolt)` pushes `[PayCost, AttackRoll, OnHit…]`.
2. Before `AttackRoll`, the engine checks for reactions that can interrupt (e.g. **Shield**). If the target's controller has one, the engine stops with `State.Pending = Reaction{unit, options}` and returns.
3. The next command must be `React(choice)`. The engine continues from the same step.

Opportunity attacks work the same way at each `Move` step that leaves an enemy's reach. Because the pause is just data in the state, **a restart in the middle of a reaction resumes exactly there.**

### 2.4 Boundaries

The engine doesn't know about phones, seats, profiles or wall-clock time. Each unit has a `Controller`:

```csharp
public abstract record Controller;
public sealed record PlayerControlled(ProfileId Owner) : Controller;
public sealed record AiControlled(string Profile) : Controller;   // enemies; absent members use "companion"
```

The sub-project 3 adapter, `TacticsGame : IGame, IResumableGame`, owns:
- the 60 s timer, and calling the AI to finish a timed-out turn (§5.5);
- the reaction prompt window;
- mapping seats to controllers when a member goes offline or comes back;
- persisting the battle.

This spec defines that seam (`Battle`, `Ai.Plan`, `BattleResult`) but doesn't build the adapter.

### 2.5 Persistence

- **After every applied command**, the adapter persists `BattleState` as JSON (`ArcJson` options, polymorphic records with `$type` tags) in the game's durable state. A restart loses at most the command in flight.
- **The campaign document changes only at battle end.** `BattleResult` carries:
  - the outcome;
  - XP for each character (absent characters earn it too, with points banked);
  - injuries gained;
  - items used;
  - which loot tables to roll (rolled by sub-project 6).
- **Content pinning.** The adapter pins the rules packs at session start and stores their **content hash** in the save. Resuming a battle under a different hash (a pack changed mid-battle, which should never happen in normal use) restarts that battle from its start state with a TV notice, rather than mixing rule versions.

---

## 3. Core combat rules

### 3.1 The character sheet
- **Ability scores:** STR, DEX, CON, INT, WIS and CHA, from 27-point point-buy (8–15 before bonuses). Modifier = ⌊(score − 10) / 2⌋.
- **Background:** gives +2/+1 to two scores (SRD 5.2) and two skill proficiencies. **Race** gives traits (§4.6).
- **Proficiency bonus:** +2 at levels 1–4, +3 at 5–8, +4 at 9–10.
- **HP:** the full hit die + CON at level 1, then the die's average (rounded up) + CON per level.
- **AC:** armour base + DEX, capped by armour type (light: no cap, medium: +2, heavy: none). Shields give +2.
- **Ability score increases** at levels 4 and 8: +2 / +1+1, or a feat from the small feat list. **Only the owner spends these, in the portal.** The AI never does.
- **Class resources** (Mana, Ki, Superiority, Devotion, …, see §4.4) **refill to full at battle start**. Basic weapon attacks and cantrips cost nothing.
- **Concentration, lightly:** a caster sustains at most one concentration effect. Taking damage forces a CON save against DC max(10, ⌊damage ÷ 2⌋). Failing ends the effect.

### 3.2 Rounds and initiative
- At the start of each round, every active unit is sorted by **Initiative = 10 + DEX modifier + bonuses**:
  - heavy armour: −2;
  - hasted: +5;
  - slowed: −5;
  - class and race features as their data says.
- **Ties** go to player-controlled units first, then higher DEX, then the fixed unit-id order. There are no initiative rolls, so the phone's "you're up in 3" is exact for the current round.
- The order is **re-sorted every round**, so speed changes take effect next round. The Chronomancer's turn manipulation (§4.2, `turnOrder` block) edits the current round's order directly.
- **Delay(n)** moves your turn n places later in the current round, without using any of the turn's economy.
- A unit spawned mid-round joins the order from the next round, unless the spawn says otherwise.

### 3.3 A turn
- At the start of a turn, the unit's **Reaction** comes back. "Turn start" hooks run, and ongoing effects tick (burning, bleeding, zone damage).
- The turn's economy is one **Move** (up to the unit's Move in tiles, splittable before and after the Action), one **Action** and one **Bonus action**.
- **Standard actions** (built into `rules/abilities/common.json`):

| Action | Effect |
|---|---|
| Attack | One weapon attack. The Extra Attack feature raises the count |
| Dash | Gain Move equal to your Move |
| Disengage | Your movement provokes no opportunity attacks this turn |
| Dodge | Attacks against you have disadvantage, and you have advantage on DEX saves, until your next turn |
| Help | Give an adjacent ally advantage on their next roll, **or revive an adjacent Downed ally at 1 HP** |
| Shove | Contested Athletics against Athletics/Acrobatics: push 1 tile, or knock prone |
| Use Item | Use a consumable from your bag |

- **End of turn:** "turn end" hooks run, and "save ends" conditions roll their saves.
- **The turn ends** on `EndTurn`, or when the unit has nothing left to spend. The adapter can also end it (timer, §5.5).

### 3.4 Rolls
- **Attack:** d20 + ability modifier + proficiency (if proficient) + bonuses, against AC including cover. A **natural 20** is a critical hit: the damage dice are rolled twice, modifiers once. A **natural 1** always misses.
- **Saving throw:** d20 + save bonus against **DC = 8 + proficiency + casting modifier**, or the fixed DC set in the data.
- **Contest:** both sides roll and the higher total wins. Ties go to the defender.
- **Advantage and disadvantage:** if any source of each is present, they cancel. Neither stacks.
- **Every roll emits a `Rolled` event** with both dice when there's advantage or disadvantage, so the TV can show the struck-out die (mockup 01).

### 3.5 Grid, movement and height
- **A square grid with 4-way adjacency** for movement, melee reach, flanking and area shapes, as in FFT. This matches the diagonal d-pad in mockup 04.
- **Move** (tiles per turn) comes from race: 4, or 3 for dwarves, halflings and gnomes. Features can raise it.
- **Height** is stored as an integer level per tile. **Jump** is the most levels a unit can climb in one step: 2 by default, 3 for monks and rogues, raised by features. Dropping down any height is allowed. Each level climbed beyond the first costs 1 extra Move.
- **Difficult terrain** (water, rubble, zones) costs 2 Move per tile. Impassable tiles and blocking props can't be entered.
- **Units:** allies can be passed through but not stopped on, and enemies block.
- **Falling:** a unit pushed or dropped more than 3 levels takes **1d6 per level beyond 3** and lands prone. A push into an impassable tile or another unit stops the push, and both units take 1d4.
- **Pathfinding** is Dijkstra over tiles, with costs from height, terrain, zones and units, and edges limited by Jump. The adapter sends a whole path in each `Move`, and the engine re-checks it step by step.

### 3.6 High ground, reach, flanking, cover
- **High ground:** an attacker **2+ levels above** the target gets **+2 to hit**. Ranged attacks gain **+1 tile of range for every 2 levels** above the target.
- **Melee reach:** a melee attack reaches an adjacent tile only if the height difference is **≤ 2**, or ≤ 3 with a reach weapon. Reach weapons also reach 2 tiles in a straight line.
- **Flanking:** a melee attacker has **advantage** if an ally that can act (not Downed or incapacitated) stands on the tile **directly opposite** across the target.
- **Line of sight and cover:**
  - The engine traces three 3D rays from the attacker's eye (tile top + 1.5 levels) to the target's feet, centre and head (tile top + 0.25 / 1 / 1.75 levels).
  - The traces use fixed-point integer maths (×1024) through tile columns and blocking props, so they're deterministic on every platform.
  - Blocked rays decide the result:

    | Rays blocked | Result |
    |---|---|
    | 0 | Clear |
    | 1 | **Half cover**, +2 AC and DEX saves |
    | 2 | **Three-quarters cover**, +5 |
    | 3 | **No line of sight** |

  - Props marked `cover: half` or `three_quarters` give at least that much to a unit standing directly behind them.
  - **Units in the way** give half cover against ranged attacks.
- **Opportunity attacks:** leaving an enemy's reach provokes one melee attack as a Reaction, unless the mover took **Disengage** or was moved by an effect (pushes don't provoke).

### 3.7 Conditions
- **SRD conditions:** blinded, charmed, frightened, grappled, incapacitated, invisible, paralysed, poisoned, prone, restrained, stunned, unconscious.
- **Game additions:**
  - **burning:** 1d6 fire at turn start; water or an action ends it;
  - **bleeding:** 1d4 at turn start; any healing ends it;
  - **slowed** and **hasted** (Initiative ∓5, Move ∓1, one fewer or extra Action, per data);
  - **marked:** a named source gets advantage against you;
  - **lit:** the next attack against you has advantage;
  - **taunted:** you have disadvantage against anyone but the taunter.
- **Duration:** a condition has a duration in rounds, or `until save` (its save is rolled at the end of the affected unit's turn), or `while concentrating` (tied to its caster). Each one is defined in `conditions.json` as hooks and modifiers, using the same blocks as abilities (§4.3).

### 3.8 Downed, Out and injuries
- A player character at **0 HP** becomes **Downed**: prone and incapacitated, with a **countdown of 3**. The TV shows the countdown over them.
- The countdown drops by 1 at the **start of each new round**, and by 1 for each hit taken while Downed (2 for a critical hit).
- **Revive:** any healing (HP = the amount healed) or **Help** from an adjacent ally (1 HP) clears the countdown. The unit stays prone.
- **When the countdown reaches 0**, the character is **Out**: removed from the battle with a `UnitOut` event, and given an **Injury** from `injuries.json`. The table is weighted, the injury is rolled from the battle RNG, and examples include "Cracked ribs: −5 max HP" and "Concussion: −1 INT and WIS saves". Injuries go in `BattleResult` and persist in the campaign until healed in town (sub-project 5).
- **Enemies** die at 0 HP. Bosses get their phases from triggers (§4.8), for example "at 0 HP the first time, heal to 50% and gain Phase 2 abilities".
- **NPC allies** marked `protect` follow the player rules, so the Downed countdown buys the party time.

### 3.9 Battle end
- Objectives are checked after every step. When a win or lose condition holds, the queue is cleared and `BattleEnded` is emitted.
- **`BattleResult`:**

```csharp
public sealed record BattleResult(
    BattleOutcome Outcome,                                  // Won, Lost, Fled
    ImmutableDictionary<UnitId, int> Xp,                    // every party member, absent or not
    ImmutableDictionary<UnitId, ImmutableArray<InjuryId>> Injuries,
    ImmutableDictionary<UnitId, ImmutableArray<ItemUse>> ItemsUsed,
    ImmutableArray<LootTableId> Loot,                       // rolled by sub-project 6
    ImmutableArray<string> FlagsSet);                       // story flags for the chapter
```

- **XP** is fixed per encounter (its data sets it) and given in full to every party member. A character the AI played for an absent owner earns the same XP, and its level-up choices are **banked** for the owner (Platform v2 portal). The pace is in `xp.json`, with a target of **one level about every 2 sessions**.

---

## 4. Content data format

### 4.1 Principles
1. **Nothing is code except the building blocks.** Abilities, passives, conditions, monsters and triggers are all data.
2. **No formula language.** Anything that varies by level is a `byLevel` table (`{"1": "1d6", "5": "2d6"}`). The highest key at or below the level wins. Values can refer to `mod.<stat>`, `prof` and `level` only as named addends (`"bonus": ["mod.wis"]`), never as arithmetic strings.
3. **Every file is validated at load** against its JSON Schema and by the C# loader (§4.10).
4. **Tooltips are generated** from the blocks, with optional flavour text added, so the description can't drift from the rules.

### 4.2 Abilities

```json
{
  "id": "cleric.guiding_bolt", "name": "Guiding Bolt", "tier": 1,
  "cost": { "action": 1, "resource": { "devotion": 1 } },
  "target": { "kind": "enemy", "range": 6, "los": true, "area": { "shape": "single" } },
  "effects": [
    { "attack": { "kind": "spell", "stat": "wis" },
      "onHit":  [ { "damage": { "dice": { "byLevel": { "1": "4d6", "5": "5d6", "9": "6d6" } }, "type": "radiant" } },
                  { "apply": "lit", "rounds": 1 } ] }
  ],
  "presentation": { "anim": "cast", "fx": "signature.radiant_bolt", "projectile": "holy" },
  "ai": { "tags": ["damage", "single"] }
}
```

- `cost` can use any of `action`, `bonus`, `reaction`, `move` (tiles), `resource{}`, `uses` (per battle) and `concentration: true`.
- **Target kinds:** `self`, `ally`, `enemy`, `any_unit`, `downed_ally`, `tile`, `empty_tile`.
- **Target options:** `range`, `minRange`, `los`, and `heightTolerance` for melee.
- **Area shapes:** `single`, `burst{radius}` (4-way steps), `line{length}`, `cone{length}`, `cross{arm}`, `ring{radius}`, `all_allies`, `all_enemies`. Areas can also set `includeSelf` and `friendlyFire`.
- `presentation` holds keys into the asset manifests: rig animation, `signature` / `fx` effect ids, and `projectiles` ids. The loader checks that they exist in the art pack.

### 4.3 Building blocks

There are about 35 blocks, each a C# class implementing `IEffectBlock` and registered by its JSON key.

| Group | Blocks |
|---|---|
| Rolls | `attack`, `save`, `contest` (each with `onHit`/`onMiss` or `onFail`/`onSuccess` branches, plus `onCrit`) |
| HP | `damage`, `heal`, `tempHp`, `revive` |
| Conditions | `apply`, `remove`, `cleanse` (by tag), `dispel` (end concentration/zone) |
| Movement | `push`, `pull`, `teleport`, `swap`, `dash` (move self as part of the effect), `jumpTo` |
| Map | `zone` (persistent area with its own hooks: fire, darkness, difficult, sanctuary), `alterTerrain` (raise/sink tiles: Geomancer), `hazard` (spawn a hazard from the hazards art set) |
| Units | `summon` (a monster id under the caster's control, lasting rounds or concentration), `transform` (named C# hook, e.g. `wild_shape`) |
| Combat | `grantResource`, `extraAction`, `refreshReaction`, `statMod` (timed bonus to AC, attack, saves, Move, Initiative, damage), `mark`, `taunt`, `reveal` |
| Turn order | `turnOrder` (`delay`, `hasten`, `swap` two units: Chronomancer) |
| Flow | `chain` (bounce to the nearest new target, n times), `repeat`, `if` (on outcome, target tags, conditions, HP%, height difference), `random` (weighted table) |

**Named C# hooks** cover the few features that don't fit the blocks. Each is a class registered by name, called from data (`{"hook": "wild_shape", "args": {...}}`), and validated like a block. The proposed list is `wild_shape`, `counterspell`, `polymorph` and `animate_dead`. Adding one requires a deploy; content can't add hooks.

### 4.4 Passives and hooks

A passive is a set of modifiers on a hook:

```json
{ "id": "rogue.sneak_attack", "hook": "onAttackHit", "oncePerTurn": true,
  "when": { "all": [ { "weapon": ["finesse", "ranged"] }, { "any": [ { "advantage": true }, { "flanked": true } ] } ] },
  "effects": [ { "damage": { "dice": { "byLevel": { "1": "1d6", "3": "2d6", "5": "3d6", "7": "4d6", "9": "5d6" } } } } ] }
```

- **Hooks:** `onBattleStart`, `onRoundStart`, `onTurnStart`, `onTurnEnd`, `beforeAttackRoll`, `onAttackHit`, `onAttackMiss`, `onDamaged`, `onSave`, `onMove`, `onEnterTile`, `onDowned`, `onKill`, `onAllyDowned`.
- **Static modifiers** need no hook: `{"static": {"ac": 1}}`, `{"static": {"resist": ["fire"]}}`, `{"static": {"move": 1}}`.
- **Limits:** `oncePerTurn`, `oncePerRound`, `usesPerBattle`.

### 4.5 Classes and subclasses

Each `classes/<id>.json` holds:
- hit die, saves, armour and weapon proficiencies, skill choices and starting kit;
- the **resource table** (levels 1–10);
- the **feature list per level**, as ability ids and passive ids, with "choose N of" groups (e.g. spells known);
- the three subclasses, picked at level 3 and gaining features at **3, 6 and 10**;
- `role` and `aiTags` for the simulator's role bands and the companion AI.

The class roster comes from the asset pack's `creator.json`. The numbers in the tables below are starting points for the balance pass (§5.6).

| Class | Role | Resource (refills each battle) | Signature features (levels 1–10) |
|---|---|---|---|
| Fighter | Front line | **Superiority** dice (2→5, d8→d10) | Second Wind, Action Surge (1/battle), Extra Attack (5), Indomitable (9) |
| Rogue | Striker | **Edge** (2→4): fuels Cunning Action tricks | Sneak Attack, Cunning Action, Uncanny Dodge (5), Evasion (7) |
| Wizard | Control | **Mana** (4→16), tier costs 1/2/3/4/5 | Arcane spell list, Arcane Recovery (1/battle regain mana), Sculpt (5) |
| Cleric | Healer | **Devotion** (3→12) | Divine spell list, Channel Divinity, Preserve Life-style burst heals, Divine Strike (8) |
| Ranger | Ranged | **Focus** (2→6) | Hunter's Mark, Archery style, Extra Attack (5), Vanish (10) |
| Paladin | Defender | **Oath** (2→8) | Lay on Hands pool, Divine Smite (spend Oath on hit), Aura of Protection (6), Extra Attack (5) |
| Bard | Support | **Inspiration** (2→6) and **Mana** (3→12) | Bardic Inspiration (reaction add die), Song of Rest (heal after battle), Countercharm (6) |
| Druid | Shapeshifter | **Wild** charges (2→3) and **Mana** (3→12) | Wild Shape (`wild_shape` hook, beast forms from the beasts art), nature spells, Thorn Whip |
| Warlock | Blaster | **Pact** points (2→4), **refill 1 every 2 rounds** | Eldritch Blast (scales by level), Invocations, Hex |
| Monk | Skirmisher | **Ki** (2→10) | Martial Arts (bonus unarmed), Flurry, Step of the Wind, Stunning Strike (5), Evasion (7) |
| Barbarian | Defender | **Rage** (2→4 uses per battle) | Rage (bonus action: resistance to bludgeoning/piercing/slashing, +2→+3 melee damage, 3 rounds), Reckless Attack, Danger Sense, Extra Attack (5), Brutal Strike (9). d12 hit die, unarmoured defence (10 + DEX + CON) |
| Sorcerer | Blaster | **Sorcery** points (2→10) and **Mana** (full caster) | Innate Sorcery (bonus action: +1 spell DC and advantage on spell attacks, 3 rounds), Metamagic (spend Sorcery points: Twinned, Extended, Careful, Quickened, Distant), Font of Magic (convert Sorcery ↔ Mana) |

| Class | Subclasses (identity) |
|---|---|
| Fighter | **Vanguard:** charge and hold the line (push, taunt) · **Weaponmaster:** manoeuvres and crits · **Warlord:** commands that give allies extra moves and attacks |
| Rogue | **Shadow:** stealth, teleport between shadows · **Trickster:** illusions, swaps, confusion · **Duelist:** riposte reactions, single-target melee |
| Wizard | **Evoker:** big areas that spare allies · **Chronomancer:** turn-order manipulation, haste and slow · **Geomancer:** raise and sink terrain, walls, quakes |
| Cleric | **Life Domain:** stronger and area heals, revive at range · **War Domain:** extra attacks, guided strike · **Light Domain:** radiant bursts, blinding, *lit* |
| Ranger | **Sharpshooter:** long range, ignore half cover, called shots · **Beastmaster:** a hound companion unit · **Warden:** traps, zones, roots |
| Paladin | **Guardian:** intercept attacks on adjacent allies, auras · **Crusader:** smite-heavy damage, charge · **Avenger:** marks a foe, pursues it, can't be escaped |
| Bard | **College of Valor:** armour, weapon attacks, inspiration to damage · **College of Lore:** counter-spell-like cutting words, extra spells · **College of the Siren:** charm, pull, crowd control |
| Druid | **Shapeshifter:** stronger and earlier wild shapes · **Stormcaller:** lightning, wind pushes, storms · **Grovekeeper:** a sapling companion, healing groves, roots |
| Warlock | **Hexblade:** CHA melee, curse-boosted crits · **Fiendbound:** an imp companion, fire, temp HP on kill · **Void:** teleport, gravity pulls, fear |
| Monk | **Way of the Open Hand:** push, knock prone, deny reactions · **Way of the Windstep:** high Jump and Move, dash attacks · **Way of Iron Body:** high AC, deflect missiles, stand firm |
| Barbarian | **Path of the Berserker** (SRD): frenzy for an extra attack while raging, reckless strikes · **Earthshaker:** ground slams that knock prone and push, cracking tiles into difficult terrain · **Spiritwarden:** ancestral spirits shield adjacent allies (damage reduction) and punish attackers |
| Sorcerer | **Draconic Bloodline** (SRD Draconic Sorcery): a chosen dragon element adds bonus damage of that type, scaly AC, wings at 10 · **Chaos Bloodline:** wild surges from a weighted `random` table, mostly helpful · **Runeborn:** lays glowing runes on tiles as `zone`s that trigger or detonate when enemies enter |

**Companion units** (hound, sapling, imp) are summoned at battle start by a `summon` passive. They take their turn right after their owner's, and their owner's phone controls them. Their stat blocks are in `monsters/companions.json`.

### 4.6 Races and backgrounds

`races.json` lists the 11 races from `creator.json`: human, elf, half-elf, dwarf, halfling, gnome, half-orc, tiefling, aasimar, goliath and dragonborn. Each entry sets:
- size and Move;
- darkvision;
- resistances;
- trait passives;
- **one racial ability**, in the same format as any other ability.

| Race | Racial ability |
|---|---|
| Dragonborn | Breath weapon (cone, damage type from ancestry) |
| Goliath | Stone's Endurance (reaction, reduce damage) |
| Halfling | Lucky (reroll natural 1s) |
| Tiefling | Hellish Rebuke |
| Aasimar | Healing Hands |
| Half-orc | Relentless Endurance (once per battle, drop to 1 HP instead of being Downed) |
| Elf | Fey Step |
| Dwarf | Stoneskin |
| Gnome | Gnome Cunning |
| Half-elf | Skill Versatility (+1 skill) and Fey Ancestry |
| Human | Resourceful (+1 resource at battle start) |

`backgrounds.json` lists **12 backgrounds**, each with a +2/+1 ability pair, two skills and a starting trinket. Backgrounds feed skill checks (sub-project 5) and the character sheet.

### 4.7 Monsters

`monsters/<family>.json` uses the unit schema plus:
- `cr`, `xp`;
- `aiProfile`;
- `sprite`: an art id and recolour variant from the `characters`, `beasts` or `bosses` manifests;
- `abilities`;
- `phases` (bosses only).

**Roster for this sub-project:**
- the **19 enemy recipes** in the art pack (goblins, bandits, cultists, skeletons, kobolds, orcs and so on);
- the **beasts** (wolves, bears, boars, spiders, slimes, drakes, dusk bats);
- the **bosses** (dragons, elementals and the rest of the bosses set);
- the three companions.

Each monster gets a stat block from CR ¼ up to CR 10 for the dragons, enough for a level 1–10 campaign.

### 4.8 Maps, encounters, objectives and triggers

**Maps** (`maps/<id>.json`) are reused by exploration in sub-project 5:

```json
{ "id": "river_crossing", "size": [14, 12],
  "tiles": "base64 of one height byte + one material byte + one flags byte per tile",
  "materials": ["grass/lush", "dirt/dark", "water/river", "stone/cobble"],
  "props":  [ { "id": "crate", "at": [5, 4], "blocks": true, "cover": "three_quarters" } ],
  "zones":  { "party_spawn": [[1, 9], [2, 9], [1, 10], [2, 10], [3, 10], [2, 11]],
              "ridge_spawn": [[11, 2], [12, 2]], "exit": [[13, 5], [13, 6]] } }
```

- Tile flags are `water`, `difficult`, `impassable`, `hazard` and `deep` (a chasm or deep water: impassable on foot; a unit pushed in is Downed if it's a party member, or dies if it's an enemy).
- Material ids map to the terrain art, so the TV renders exactly what the engine walks on.

**Encounters** (`encounters/<id>.json`) set:
- the map;
- party size limits;
- enemy placements (unit, level or variant, tile or zone, `aiProfile` override);
- protected NPCs;
- XP and loot tables;
- **objectives** and **triggers**.

Objectives are `win` and `lose` expressions built from `any`/`all` over:
- `defeat_all`;
- `defeat:<unitTag>`;
- `survive:<rounds>`;
- `reach:<zone>:<count>`;
- `protect:<unitTag>`;
- `flag:<name>`.

The default `lose` is `party_out` (every party member Downed or Out). When the protected NPC is Out, that also counts as a loss.

**Triggers:**

```json
{ "id": "ridge_ambush", "when": { "round": 3 }, "once": true,
  "do": [ { "say": { "speaker": "warlord", "line": "Archers, to the ridge!" } },
          { "spawn": { "unit": "goblin_archer", "at": "ridge_spawn", "count": 2 } },
          { "setObjective": { "win": { "any": ["defeat:warlord", "survive:8"] } } } ] }
```

- **Conditions** (`when`, combined with `all`/`any`):
  - `round`;
  - `unitEntersZone{tag, zone}`;
  - `hpBelow{tag, percent}`;
  - `unitDies{tag}`;
  - `flag`;
  - `counter{name, atLeast}`;
  - `objective{name, state}`.
- **Actions** (`do`):
  - `say` (a dialogue line on the TV, with optional narration);
  - `spawn`;
  - `setFlag`;
  - `addCounter`;
  - `setObjective`;
  - `cameraHint` (focus a tile for the TV);
  - `endBattle`;
  - **any effect block** applied to tagged units or zones (e.g. `alterTerrain` to collapse a bridge).
- `once` defaults to `true`. Triggers are checked at round start, after each step and at turn end, in file order.
- **There are no variables, no loops and no trigger firing another trigger in the same step**, so the loader can check every reference and cycles are impossible.

### 4.9 Packs and pinning
- A pack is a folder with a `manifest.json`: `id`, `rulesVersion`, `requires` (other pack ids) and the file list. `base` is always loaded. Each weekly chapter is its own pack (`chapter-03`, and so on).
- **A chapter may add ids.** Changing a base id needs an explicit `"replaces": "<id>"`, so balance changes are deliberate and easy to review.
- **The content hash** is SHA-256 over the loaded packs' files in a fixed order. It's stored in every saved battle (§2.5).

### 4.10 Validation and attribution
- **The JSON Schemas** in `rules/schema/` check structure. They're also usable in an editor while writing a chapter.
- **The C# loader** checks meaning:
  - every id resolves (abilities, conditions, monsters, zones, art keys);
  - `byLevel` tables cover levels 1–10 where they're used;
  - no unknown blocks or hooks;
  - ranges and areas are within the map limits;
  - every class has a feature at each level;
  - each subclass has features at 3, 6 and 10;
  - every trigger's references exist.
- Errors carry `file` and a **JSON pointer** (`classes/wizard.json#/subclasses/2/features/6/0`). A pack with errors doesn't load. The TV shows the reason, and the campaign won't start.
- **Attribution:** the SRD 5.2 text and mechanics are used under CC-BY-4.0. `games/Tactics/THIRD_PARTY.md` and the title screen credits carry the required attribution. Subclasses, setting, monsters beyond the SRD and all story content are original.

---

## 5. AI

### 5.1 One planner
`Ai.Plan(BattleState, UnitId, AiProfile, RulesPack) → TurnPlan` returns the commands for a whole turn. Enemies, absent players' characters and timed-out turns all use it, each with a different profile.

### 5.2 Choosing a turn
1. **Reachable tiles.** Dijkstra from the unit, using its remaining Move, Jump, terrain and zones.
2. **Prune.** Score each tile on position alone and keep the **best 12** plus the current tile.
3. **Enumerate plans.** For each kept tile: each usable ability (cost affordable) × each legal target or aim tile. Plans include "move, act" and "act, then move to the best tile within the remaining Move".
4. **Score** each plan by **expected value**, computed analytically from the data, with no Monte Carlo:
   - **Hit and save chance:** exact d20 maths including advantage, cover and high ground.
   - **Expected damage and healing:** from dice averages, crits and resistances.
   - **Kills and downs:** P(target reaches 0), weighted by the target's **threat** (its expected damage per round).
   - **Conditions:** a value per condition from the profile (stunning a caster is worth more than slowing a brute).
   - **Position:** cover gained, high ground, setting up flanking for allies, staying in reach of allies who heal.
   - **The threat map:** each enemy's expected damage onto each tile next round. Ending a turn on a dangerous tile costs score, weighted by the profile's caution.
   - **Objectives:** steps toward an exit, distance from a protected NPC, and the boss's danger.
5. **Bonus action and remaining Move** are planned greedily after the main plan.
6. **Ties** break on a fixed ordering (ability id, then tile, then target id). **The AI never draws from the battle RNG**, so the same state always gives the same plan.

The AI does one-ply planning plus the threat map, with no deeper search. The time budget is ≤ 250 ms p95 (§1), well inside the platform's 2 s watchdog.

### 5.3 Profiles
`ai-profiles.json` holds weight sets: `brute`, `skirmisher`, `archer`, `caster`, `healer`, `coward` (flees below 30% HP), `boss` and `companion`. Chapters can add profiles. The weights cover:
- damage;
- kills;
- control;
- healing;
- caution;
- focus fire;
- objective;
- how much resource to save for later.

### 5.4 Fair play and difficulty
- **Downed targets:** enemies give attacking a Downed character **zero** value unless their profile is `cruel` (set on some bosses), so standing targets always come first.
- **Difficulty**, a campaign setting, scales the focus-fire weight, the threat-map weight and resource saving:

| Setting | AI behaviour |
|---|---|
| Story | Spreads damage out and rarely retreats |
| Normal | The default weights |
| Hard | Focus fires, uses cover well and times its big abilities |

### 5.5 Absent players and timed-out turns
- **Absent members' characters** run the `companion` profile, weighted by their class's `aiTags`:
  - **Priorities:** revive Downed allies first, then protect the healer and the protected NPC, then do role damage.
  - **Resources:** class resources are spent freely, since they refill every battle.
  - **Items:** at most **one healing item per battle**, from the owner's bag, and only when the threat map says the character would be Downed next round. Its use is recorded in `BattleResult.ItemsUsed`.
  - **Off limits:** the AI never spends points, picks a subclass, trades or uses any other item.
- **A timed-out turn** (a present player's 60 s ran out): the adapter calls `Ai.Plan` with the `companion` profile, restricted to the Move, Action and Bonus not yet used, with **no items and no Delay**. The events carry `AiFinished` so the TV can show the badge.
- **Reactions:**
  - **AI-controlled units** take an opportunity attack when its expected value is above 0. They use a defensive reaction (Shield, Uncanny Dodge, Parry) when it turns a hit into a miss or saves ≥ 25% of their HP.
  - **Players** get a prompt on their phone. The adapter decides the window, and the fallback when there's no answer is this same rule.

### 5.6 Simulator and balance targets
`Tactics.Sim` runs every encounter × party compositions (all 4–6 member combinations of class roles, sampled) × seeds × levels 1, 3, 5, 7, 10. Both sides are AI-controlled.

**Reports** (`docs/superpowers/reports/YYYY-MM-DD-tactics-balance.md`):
- win rate per encounter;
- rounds per battle;
- downs and Outs;
- damage, healing and mitigation per round per class and level;
- how often each ability is used;
- how often the companion AI used an item.

**Role bands:**

| Role | Measure | Band |
|---|---|---|
| Strikers and blasters | Damage per round | Within ±15% of the band's mean at each level |
| Healers | Healing and revives per round | Within ±15% of the band's mean at each level |
| Defenders | Damage prevented and taken | Within ±15% of the band's mean at each level |
| Control | Enemy turns denied | Within ±15% of the band's mean at each level |

**Flags:**
- an ability used in < 2% of the turns where it was legal;
- an encounter won < 60% or > 95% of the time at its intended level on Normal.

**Session pacing target:** a battle takes 8–12 rounds, or about 25–40 minutes of play with 5 players.

---

## 6. Build phases

Each phase ends green and demoable through the simulator or golden replays.

| # | Phase | Delivers |
|---|---|---|
| 1 | **Engine core** | State, PCG32, serialisation; grid, Dijkstra with Jump, 3-ray LOS and cover; rounds and Initiative, Delay; action economy; commands, events, `Rejection`; resolution queue with reactions and opportunity attacks; conditions; Downed, countdown and Out; `BattleResult`. Exercised with hand-built units in C# |
| 2 | **Content system** | Pack loader, schemas, semantic validation, content hash; the ~35 blocks, hooks and modifiers; tooltip generation; **Fighter, Cleric, Rogue, Wizard at levels 1–10 with one subclass each**; 10 monsters; one map and encounter |
| 3 | **AI and simulator** | Planner, scoring, threat map; profiles, difficulty, fair-play rule; companion rules; `Tactics.Sim` and the first balance report |
| 4 | **Content waves** | The other 6 classes; the remaining 26 subclasses with companions; 11 races and 12 backgrounds; injuries; XP table; the full monster roster with stat blocks and sprite ids; three sample encounters |
| 5 | **Objectives and triggers** | The core five objectives; trigger conditions and actions, flags and counters, trigger validation; a **showcase encounter** with reinforcements, a collapsing bridge and a two-phase boss |
| 6 | **Balance pass** | Simulator sweeps, tuning to the role bands, final report |

---

## 7. Testing

| Kind | What it proves |
|---|---|
| **Unit** | Each block, hook, condition and geometry rule. Geometry tests use an **ASCII map** helper (`"..^^#"` rows with height digits) so a test reads like a picture |
| **Determinism** | Replaying a command log twice gives identical states and events. `serialise → deserialise → apply` equals applying directly, checked after **every** command of every golden replay |
| **Golden replays** | Recorded command logs with their expected event JSON, checked into `Tactics.Logic.Tests/Golden/`. A rule change shows up as a readable diff. They're regenerated with an explicit `UPDATE_GOLDENS=1` |
| **Content** | Every pack loads with zero errors. Every ability, passive and condition generates tooltip text. Every `presentation` key exists in the art manifests. Broken-pack fixtures test that each validation error names the right file and pointer |
| **AI** | Scenario tests: a planner that heals a dying ally, takes high ground, avoids a dangerous tile, never targets the Downed (unless `cruel`), uses at most one item for an absent player, and finishes a timed-out turn without items. A **performance test** checks ≤ 250 ms p95 on the worst sample encounter |
| **Simulator smoke** | CI runs every sample encounter with 3 seeds and checks that each one ends. The full sweeps run locally and their reports are committed |

---

## 8. Error handling
- **An invalid command** (out of range, no line of sight, can't afford it, not this unit's turn, path blocked, wrong pending decision) returns `Rejected{reason, code}` with **no state change**. The phone shows the reason.
- **Invalid content** fails at load time, naming the file and JSON pointer. It never fails in the middle of a battle.
- **A broken internal invariant** (e.g. a unit on an impassable tile, a negative resource) throws `RulesInvariantException`. The adapter's watchdog catches it, keeps the **state from before the command**, logs the command log and seed for reproduction, and the TV shows "Something went wrong with that move". A bad rule can't corrupt a save.

---

## 9. Out of scope (separate specs)
- **Sub-project 3:** the Godot battle renderer and event pacing, the phone battle screens (`CustomView`), and the `TacticsGame` adapter (timer, reaction prompts, seat-to-controller mapping, persistence wiring).
- **Sub-project 4:** character creation and level-up in the portal (point-buy, subclass pick, banked points, feats), and the layered sprite composition in Godot.
- **Sub-project 5:** exploration, dialogue, party votes, skill checks, healing injuries in town, and the Resuming… phase.
- **Sub-project 6:** loot rolling, need/greed, inventories, trading and shops.
- **Sub-project 7:** the chapter authoring workflow and the Claude-assisted drafting tools.

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| **Content volume:** about 360 abilities across 12 classes, 36 subclasses, races and monsters | Data only, with generated tooltips and strict validation. Phase 2 proves the format on 4 classes before the bulk is written. Waves are reviewed with simulator numbers, not by eye |
| **The blocks don't cover a feature** | Named C# hooks (§4.3) as an escape hatch, kept to a short list. Adding a block is cheap because it's one class plus its schema |
| **Balance across 12 classes and 10 levels** | Role bands, simulator sweeps, flags for unused abilities and lopsided encounters. `replaces` lets a chapter retune without editing base |
| **AI cost with 16 units and area spells** | Tile pruning, analytic expected value, one-ply planning and a performance test in CI. The prune width (12) can be tuned |
| **Speed-ordered rounds make DEX dominant** | Initiative only changes order, not the number of turns. The simulator tracks win rate against party DEX, and armour and race modifiers can be tuned |
| **Fixed-point LOS edge cases** (rays grazing corners) | Rays are tested against hand-built ASCII maps. Ties count as *not blocked*, the rule that favours the player |
| **SRD licence** | CC-BY-4.0 attribution in `THIRD_PARTY.md` and the credits. Only SRD 5.2 content is used, never non-SRD 5e material |
