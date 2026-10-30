# ArcBox Tactics Rules Engine: progress against the plan

This tracker lets another session or agent pick the work up. It goes with:
- **Plan:** [`2026-10-30-tactics-rules-engine.md`](2026-10-30-tactics-rules-engine.md)
- **Spec:** [`../specs/2026-10-30-tactics-rules-engine-design.md`](../specs/2026-10-30-tactics-rules-engine-design.md)

**How the work is run.** Subagent-driven development. For each task:
1. A fresh implementer works from the plan's task text.
2. A reviewer checks the task against both the spec and code quality.
3. Fix rounds follow, each with a scoped re-review, until the review is clean.

When every task is done, one whole-branch review runs.

**Branch:** `ccr-472d8660-sxpjlj`. Every task is committed and pushed there.

**How to resume:**
1. Find the first task below that isn't `done`.
2. Read its section in the plan, plus the **Rulings** and **Binding additions** below. They change or extend the plan text.
3. Run `export PATH=$HOME/.dotnet:$PATH; dotnet test ArcBox.slnx`. It must be green before you start.

## Task status

| # | Task | Status | Commits | Fix rounds |
|---|---|---|---|---|
| 1 | Projects, ids, dice and the RNG | done | `c47d0c6` | 0 |
| 2 | Battle maps, terrain and pathfinding | done | `f458c9c` | 0 |
| 3 | Line of sight, cover, reach, flanking and areas | done | `a50a36e` | 0 |
| 4 | State model, serialisation, turn order and `Apply` | done | `c8d1c9f`, `0970852` | 1 |
| 5 | Rolls, the effect pipeline and the first blocks | done | `1f67040`, `59a3a71` | 1 |
| 6 | Movement, pushes, falling, opportunity attacks and reactions | done | `92b6d76`, `966d570` | 1 |
| 7 | Conditions, hooks, passives and modifiers | done | `bc7c8d6`, `7e2eb32` | 1 |
| 8 | Downed, Out, injuries, battle end, invariants and replays | done | `1c71e84`, `43f1895` | 1 |
| 9 | Pack loader, validation and the content hash | done | `c810425`, `6b1462c` | 1 |
| 10 | Remaining effect blocks, zones, summons and named hooks | in progress |  |  |
| 11 | Tooltip generation | not started | | |
| 12 | Sheet builder and encounter setup | not started | | |
| 13 | Content wave 0: core files and fighter, cleric, rogue, wizard | not started | | |
| 14 | AI planner, odds and threat map | not started | | |
| 15 | Companion AI, timed-out turns, reactions, performance | not started | | |
| 16 | `Tactics.Sim` and the first balance report | not started | | |
| 17 | Ranger, paladin and monk | not started | | |
| 18 | Bard, druid and warlock | not started | | |
| 19 | Martial subclasses and the hound | not started | | |
| 20 | Caster subclasses, sapling and imp | not started | | |
| 21 | Monster roster, maps and sample encounters | not started | | |
| 22 | Objectives, triggers and the showcase encounter | not started | | |
| 23 | Balance pass and final report | not started | | |
| – | Whole-branch final review | not started | | |

## Binding additions to the plan

These were decided during execution. Later tasks must follow them, alongside the plan's Global Constraints.

**Engine structure**
- **`Battle` is a `static partial class`.** Later tasks add their members in their own files.
- **`BattleWriter` and `Step.Run` are public.** `BattleWriter` is the engine's mutable working copy, used by effect blocks.

**Serialisation**
- **Ordinal string collections.** System.Text.Json rebuilds `ImmutableSorted{Set,Dictionary}<string,…>` with a culture-sensitive comparer, which breaks ordinal ordering.
  - Use the helpers in `Core/Ordinal.cs` (`Ordinal.Set`, `Ordinal.Map`).
  - Put `[property: JsonConverter(typeof(OrdinalJsonConverter))]` on every string-keyed sorted property.
  - Never create these with `.Empty` or collection expressions.
- **Effect lists** must be typed `EffectDef`, because the single-key converter only runs for that type.
- **Enums in JSON:** `Stat` and `AreaShape` serialise in lowercase / snake_case.
- **`AreaDef` uses a single `"size"` key for every shape.** The spec's `radius`, `length` and `arm` become `size`.
- **Turn hand-off is queue-borne.** `EndTurnStep` pushes a serialisable `StartNextTurnStep`. `Active` doesn't move until that step runs, so a save mid-hand-off resumes correctly.

**Battle state**
- **Setup data is stored.** `BattleState` keeps the encounter's `Xp`, `Loot`, `Difficulty` and `Triggers`.
- **Occupancy.** Only Up or Downed units occupy a tile. Out and Dead units occupy nothing and are removed from the turn order.

**Turns and Delay**
- **When Delay is allowed:** only at the start of the unit's turn (nothing spent yet), and once per round.
  - Otherwise it's rejected with `CantDelay`: "You can only delay at the start of your turn, once per round."
- **A resumed (delayed) turn:**
  - gets Move, Action and Bonus back, but keeps its Reaction as it was;
  - skips turn-start hooks and condition ticks.
  - The state tracks this with `StartedThisRound` and `DelayedThisRound`.

**Movement, pushes and falling**
- **High ground's range bonus** applies only to ranged abilities (range > 1).
- **Falling:** walking down a ledge is free; only pushes and pulls cause fall damage.
- **When a push stops:** at a wall, a unit, a tile 2 or more levels higher, a fall of more than 3 levels, or a deep tile.
- **Halted moves:** if a halted move leaves the mover on a shared tile, it goes back to the last tile on its path that it held alone. A halted move drops its remaining move steps.

**Reactions**
- **Reaction abilities** are tagged `reactsTo:<trigger>`. The triggers are `leavesReach`, `beforeHit` and `spellCast`.
- **`beforeHit`** is offered after the d20 is rolled and before the hit is decided.
- **`AttackResolved(Source, Target, Hit, Crit)`** follows every attack.
- **AI-controlled reactors** take every opportunity attack and pass on other reactions until Task 15 replaces this with `Ai.React`.

**Conditions and passives**
- **Condition durations:**
  - A condition doesn't count down at the end of the turn in which its source applied it, so `rounds: 1` lasts until the end of the source's next turn.
  - `rounds: 0` lasts until the end of the source's current turn. Disengage-style effects use this.
- **Passive targets.** `PassiveDef.Target` is `other` (the default) or `self`.
  - `other` is skipped when there's no other unit or when the other unit is the owner.
  - Content passives that affect their owner, such as turn-start or move hooks, must say `"target": "self"`.
  - Condition passives act as the unit that applied the condition.
- **Losing concentration.** Becoming Downed, Out, Dead or incapacitated ends that unit's concentration.

**Downed, Out and XP**
- **XP:** full on a win, half (rounded down) on a loss or a retreat. Absent members get it too.
- **One function for 0 HP.** Every drop to 0 HP goes through `DownedRules.Drop` in `Rules/Downed.cs`:
  - party members and protected NPCs go Downed;
  - everyone else dies;
  - summons die and get no XP.
- **Downed countdown:** a Downed unit loses 1 countdown per hit (2 on a crit), never more from extra damage in the same hit. It takes no HP damage.
- **A dropped unit stops acting.** A unit that drops while acting loses its remaining queued attacks and ability effects; its own hook effects still run.
- **Who can be targeted.** Abilities aimed at allies, enemies or any unit can target Downed units: healing revives them, hits tick their countdown.
- **Deep water.** A unit revived in deep water may stand there and swim out.
- **Runaway safety.** One command is capped at 100,000 steps, then throws an invariant error.

**Content loading (Task 9)**
- **Unknown JSON properties are errors.** Content is read with a copy of `ArcJson.Options` that sets `UnmappedMemberHandling.Disallow`.
- **No exception escapes the loader.** Every failure while loading becomes a `ContentError` that names the file and JSON pointer.
- **The content hash is a hash of hashes.** Each file is hashed over its path, a NUL byte and its bytes; the sorted per-file hashes are then hashed together. Manifests are included, so reordering a manifest's file list changes the hash.
- **Temporary flag.** `"subclassesPending": true` on a class is accepted until Task 20 removes it.
- **Monster `aiProfile` checks** only switch on once some pack defines AI profiles; Task 14 makes them strict.
- **Prop cover** must be none, half or three-quarters; `total` is rejected.
- **Bad values the loader must reject:**
  - empty ids;
  - an `apply` with `untilSave` outside a `save` block;
  - unknown addends;
  - unknown attack kinds.
- **Map content checks:** props and zones are in bounds, and tile flags are valid.

**Effect blocks (Task 10)**
- **Tile-targeted abilities:** Task 10 adds a once-per-ability run at the aim tile for zone, terrain, hazard, summon, teleport, dash and jumpTo blocks. It runs even when the area holds no units.

## Deferred minor findings (for the final review to triage)

| Task | Finding |
|---|---|
| 1 | Lemire rejection branch untested; `default(Rng)` is degenerate; Dice regex `$` accepts a trailing newline (use `\z`); `ByLevel` record equality is by reference; id converters are public |
| 2 | Paths tests lack an optimality case, a 2D enemy detour and a blocking-prop case; `Terrain`/`Paths` allocate per call (the AI should cache) |
| 3 | The final-cover test's Total comes from rays, not the prop rule; an attacker standing on a cover prop can give the target cover; `Columns` is O(bounding box); Ring of size 0 returns the aim tile |
| 4 | `Start` normalises only `Sheet.Resources`; records holding arrays compare by reference (golden tests compare JSON); rejection-text test checks only 3 of 16 strings; `CantAfford` with no resource gives "Not enough ."; `ObjectiveExpr` allows a leaf with a group; `UnitSheet.Score` defaults a missing stat to 10 |
| 5 | A self line/cone with no aim tile pays its cost and hits nothing; the converter's key detection could collide with a parameter named like a block; `Battle.cs` is about 320 lines (targeting could move out) |
| 6 | No HP event on deep-water drops (Task 8 addresses); prone applied after a lethal fall; Swap has no landing check; `Movement` depends on `Motion.OccupantAt` |
| 9 | Trigger `do` effect blocks are only checked for known keys (Task 22 must fully check them); a second `xp.json` reports its duplicate at a nonexistent `/id`; the pack label comes from the manifest id, not the folder name |
| 8 | Fall/collision damage has no source (no `OnKill` for the pusher); `RoundStarted.Order` can list a unit that goes Out right after; golden fixture lacks `TurnSkipped`/`UnitOut`/`BattleEnded`; `Invariants.Enabled` is a public static; `.json` line endings not pinned |
| 7 | Several `Hooks.Fire` calls in one step run in reverse order; passive and ability `UsesLeft` share keys; `AttackStep` doesn't check the attacker is alive at Resolve; several `WhenDef` leaves and limits untested; `Modifiers.For` recomputed often (performance) |
