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

**Handing off:** read [`2026-10-30-tactics-rules-engine.handoff.md`](2026-10-30-tactics-rules-engine.handoff.md) first. It has the workflow, the notes for each remaining task, every ruling, and the exact agent instructions.

**Status:** All 23 tasks are done. Tasks 18–21 were done in a local session and Task 22 from a cloud session writing into the local checkout. Task 23 (balance pass) was done on 2026-10-30: three of its four targets are met, and the role-band target is not (52 of 60 values in band; see the [final report](../reports/2026-10-30-tactics-balance.md)). The whole-branch final review is next.

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
| 10 | Remaining effect blocks, zones, summons and named hooks | done | `876902a`, `2c13938` | 1 |
| 11 | Tooltip generation | done | `6cc5a7f` | 0 |
| 12 | Sheet builder and encounter setup | done | `77f51dc` | 0 |
| 13 | Content wave 0: core files and fighter, cleric, rogue, wizard | done | `595c258`, `816acf7` | 1 |
| 14 | AI planner, odds and threat map | done | `ac23394`, `23d88d1` | 1 |
| 15 | Companion AI, timed-out turns, reactions, performance | done | `1feca9c`, `74cb44f` | 1 |
| 16 | `Tactics.Sim` and the first balance report | done | `8f81baa`, `d017b9c` | 1 |
| 17 | Ranger, paladin and monk | done | `25b6dcc` | 0 |
| 18 | Bard, druid and warlock | done | `44f2b6c` | |
| 18A | Barbarian and sorcerer (added 2026-10-30) | done | `0d89f8c` | |
| 19 | Martial subclasses (16, incl. barbarian's) and the hound | done | `7de4305` | |
| 20 | Caster subclasses (16, incl. sorcerer's), sapling and imp | done | `49e3c9d` | |
| 21 | Monster roster, maps and sample encounters | done | `7480329` | |
| 22 | Objectives, triggers and the showcase encounter | done | (this commit) | |
| 23 | Balance pass and final report | done | (this commit) | |
| – | Whole-branch final review | not started | | |

## Scope change (2026-10-30)

The user asked for every D&D class, so Barbarian (Defender) and Sorcerer (Striker) were added, bringing the game to 12 classes and 36 subclasses. Spec §4.5 and the plan were amended:
- **New Task 18A** covers both classes.
- **Tasks 19 and 20** take their subclasses:
  - Barbarian: Berserker, Earthshaker, Spiritwarden.
  - Sorcerer: Draconic, Chaos, Runeborn.
- **Role bands:** Defender is now fighter, paladin and barbarian; Striker is rogue, ranger, warlock, monk and sorcerer.
- **Sprites and the character creator** (`tools/tactics-assets`) were updated separately and merged in `9816277`: class kits, six subclass looks, rune/ward effects, and the 11×12 race/class picker.

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

**Characters and encounters (Task 12)**
- **Weapons and armour are content:** `weapons.json` and `armor.json`, with `ArmorDef(Id, Name, Kind, BaseAc)`. A shield is the `CharacterRecord.Shield` flag (+2 AC).
- **Injuries** surface as `injury:<id>` passives through `RulesPack.PassiveFor`. `StaticMods.MaxHp` is applied only when a sheet is built, and conditions may not use it.
- **Heavy armour's −2 initiative** is applied in `TurnOrder`, not added to the sheet.
- **`defaultPicks`:** the loadout at the highest listed level at or below the character's level.
- **The engine trusts a character's ability scores.** The portal validates point-buy (sub-project 4).
- **Warlock Pact regain** uses a passive (`onRoundStart` + `if` even round + `grantResource`), not the `resourceRegen` feature.
- **For Task 13:**
  - pick counts are enforced per `choose` feature;
  - class `defaultPicks` are validated at load;
  - encounters place fixed-tile units before filling zones.

**AI (Task 14)**
- **Legality.** `Battle.Check(state, command, rules)` returns the rejection `Apply` would give, without resolving anything. The AI only plans commands that pass it.
- **Profiles.** `ai-profiles.json` holds brute, skirmisher, archer, caster, healer, coward, boss and boss_cruel. Monster `aiProfile` references are validated strictly.
- **Caution fades.** Caution drops to 30% after round 5, so cautious sides don't stall each other. This is a balance setting for Tasks 16 and 23.
- **No Delay.** The planner never Delays, so `AllowDelay` and `RemainingOnly` currently do nothing; Task 15 must not rely on them. Objective scoring covers only protected units until Task 22.
- **Fair play.** The bonus action never targets a foe the main action may have downed, unless the profile is cruel.
- **Pathfinding fix.** `PathMap.Reachable` was indexed wrongly on non-square maps; it is fixed and has a regression test.

**Ranger, paladin and monk (Task 17)**
- **Divine Smite** is a bonus-action mark costing 1, 2 or 3 Oath. The paladin's next melee hit on that foe, before the end of its next turn, adds 2d8, 3d8 or 4d8 radiant (+1d8 against undead and fiends).
- **Lay on Hands** is a `lay_on_hands` resource equal to the paladin's level, spent at 5 HP per point.
- **Hunter's Mark** is an on-hit passive against a target carrying the `hunters_mark` condition.
- **Aura of Protection** is approximated: a fixed +2 to saves, re-applied at the end of each paladin turn.
- **AI valuation.** The AI values putting a condition on a foe by the damage the user's own on-hit passives would add against it, assuming 0.6 of attacks hit. **Tasks 18 and 19 must add a recursion guard** to `QuarryDamage` before content adds a passive that re-applies its own required condition (Hex, Avenger).

**Simulator (Task 16)**
- **Running it.** `dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --difficulty Normal --out <report>`. A full sweep of 1,800 battles takes about 2 minutes.
- **Damage is HP actually lost.** Overkill and temporary HP soaked don't count as damage dealt; soaked temporary HP counts as the target's mitigation.
- **Reactions** are left out of the ability-usage table.
- **Reports:** first `docs/superpowers/reports/2026-10-30-tactics-balance-v0.md`, final `docs/superpowers/reports/2026-10-30-tactics-balance.md`.
- **Intended levels.** Task 21 adds an optional intended `level` to encounters for the win-rate flag.

**Companion AI and reactions (Task 15)**
- **Reactions.** `Ai.React(state, offer, rules)` decides every AI reaction from the offer's serialisable `Cause` (a `ReactionTrigger`):
  - opportunity attacks are taken when expected value > 0;
  - self-targeted defensive reactions are used when they turn a hit into a miss, or give temporary HP of at least 25% of max HP;
  - counterspell is valued from the triggering spell.
- **Absent members.** The adapter must pass `AllowItems: true` for absent members. The companion gate then allows one healing item per battle, used on itself, only when the threat map predicts it would go down.
  - AI item use records `ItemUse(ByAi: true)` and increments `ItemsUsedByAi`.
- **Timeouts.** `Battle.Apply(..., aiFinished: true)` stamps `AiFinished` on the events up to the actor's `TurnEnded`. The flag is left out of the JSON when false.
- **Class tag.** Sheets carry a `class:<id>` tag, so class `aiTags` drive the companion's role weights. Once the Action is spent, the planner lets the bonus action decide where to move.

**Content wave 0 (Task 13)**
- **Items are used through `Use` with `Item` set and ability `common.use_item`.** The item's own ability supplies the cost and effects (a potion is a bonus action). One item leaves the bag and an `ItemUse` is recorded.
  - Refused with `NoItem`: "You don't have that item."
- **New `StaticMods` fields:**
  - `SaveEdges` (advantage or disadvantage per stat)
  - `SaveProficiencies`
  - `Evasion`
- **New zone flag `blocksSight`.** Lines of sight that touch an obscured tile are blocked. The smoke bomb uses it.
- **Resource grants:**
  - `grantResource` supports `overMax`, and a normal grant never removes an over-max gain.
  - Classes declare a `primaryResource`, which `grantResource "primary"` uses.
- **Effect options added to the engine:**
  - `save.dcStat` lets a save DC use STR, DEX or CON.
  - `extraAction "move"` exists.
  - Injuries can carry hooks.
- **Area size limit is now 10.**
- **Bug fix:** empty `when` lists in loaded content count as absent. They were failing silently, which disabled Sneak Attack.

**Effect blocks (Task 10)**
- **Effect order follows the content.** An ability's effect list runs in the order it's written, split into runs of consecutive blocks:
  - a run of tile blocks (zone, alterTerrain, hazard, summon, teleport, dash, jumpTo, reveal, dispel) runs once at the aim tile;
  - a run of unit blocks runs once per affected unit.
  - Tile blocks nested inside `if`, `random`, `repeat` or a branch run per unit.
- **Monster references are checked at load.** That covers `summon`, plus the monsters the named hooks need: Wild Shape's `args.monster`, `sheep` for Polymorph, `skeleton` for Animate Dead.
- **Concentration must match.** `concentration: true` on a zone, summon or statMod requires the ability's cost to have concentration.
- **Summons** join the turn order right after their owner, starting next round (the state's `Joining` set). Units leave the order only through `DownedRules.LeaveOrder`.
- **Two extra events:** `TempHpGained` and `Countered`.
- **Tile-targeted abilities:** Task 10 adds a once-per-ability run at the aim tile for zone, terrain, hazard, summon, teleport, dash and jumpTo blocks. It runs even when the area holds no units.

**Objectives and triggers (Task 22)**
- **Triggers are typed.** `TriggerDef(Id, TriggerWhen When, bool Once, ImmutableArray<TriggerAction> Do)` in `Content/TriggerDefs.cs`. A condition is a record whose present keys must all hold (like `WhenDef`); an action is a record with exactly one action key set. The loader still defaults `once` to true.
- **Effect blocks in triggers** are written `{ "effects": [ ... ], "tag" | "zone" | "at": ..., "source"?: <tag> }`, not as a bare block key beside the action. Runs of tile blocks go to each tile of the zone or tile (or each tagged unit's tile); unit runs go to each unit there or each tagged unit. The source is the first unit with `source`'s tag, else the target, else the first unit in the battle. They run at hook depth 1.
- **Checkpoints.** `Triggers.Check` runs in `Settle` before every step and when the queue empties (so at round start, after each step and at turn end). Conditions are read from the state at the checkpoint; actions are `TriggerActionStep`s pushed to the front, so their effects are seen at the next checkpoint.
- **Repeatable triggers** (`once: false`) fire on the rising edge: they stay in `FiredTriggers` while their condition holds and leave it when it stops.
- **Tags.** `party` (party members, not summons or protected NPCs) and `enemy` (enemies, not summons) are built-in tags for objectives and triggers; everything else is a sheet tag.
- **Objective leaves.** `defeat:<tag>` needs at least one tagged unit to have existed; `survive:n` holds from the start of round n + 1; `reach:<zone>:<k>` counts standing party members; `protect:<tag>` holds while every tagged unit is still in the battle. `party_out` no longer counts protected NPCs. A party-side protected NPC that is Out or Dead loses the battle whatever the lose expression says.
- **Phases.** Content writes `{ "phase": { "tag": "warlord", "number": 1 } }`; `Encounters.Setup` fills `PhaseAction.Def` from the tagged monster's `phases`, and the engine runs from `Def` (adds abilities and passives, swaps the AI profile).
- **Collapses.** `alterTerrain` gained `deep`: the tiles join `BattleState.Sunk` (null until one sinks, left out of the JSON) and whoever stands on them falls in (`Motion.FallIn`, which goes through `DownedRules.Drop`). `Terrain` takes the sunk set.
- **Spawned units** are named `<monster>.<n>` (lowest free n), are not summons, and join the order next round, even when they spawn during `Battle.Start` (only battle-start summons join round 1). An `ally` spawn is a `UnitState.Helper`: on the party's side, but it dies at 0 HP and counts for no objective, `party` tag or XP.
- **Tags** are read from a unit's own sheet (`TrueSheet ?? Sheet`), so a transformed unit keeps them.
- **Validation.** Trigger effect blocks get the full ability effect walk. Flags read must be set by some `setFlag` (objectives included), counters read must be added to, watched objective leaves must appear in the encounter's objectives (as written or via `setObjective`). A phase's tag must name exactly one placed or spawned monster that has the phase, and a trigger watching `hpBelow` on that tag must use the phase's percentage (`PhaseDef.HpBelow` does nothing else: phases come from triggers).
- **AI.** `Scorer.IsObjective` adds the objective weight for `defeat:<tag>` targets (party side) and `protect:<tag>` units (other sides), on top of protected units. Party units are drawn toward `reach` zones.
- **Showcase.** (Task 23 changed the numbers below: the warlord starts at (13, 5) with 240 HP, the bridge collapses at 1 party member, the win is survive 11 rounds, and the golden seed is 15.) `warlord_bridge` (level 5): goblin boss `warlord` with a phase at 50% (`goblin_boss.warcry`, `boss_cruel`), archers spawning on the ridge at round 3, a bridge that collapses into the chasm once 2 party members are on it, and stepping stones to the south. Win: defeat the warlord or survive 8 rounds. `goblin_boss` has 70 HP from level 5 (21 before), so one Fireball can't skip its phase. Its golden replay (`warlord-bridge`, seed 11, level-5 party of 5) is recorded from `Ai.Plan` with `UPDATE_GOLDENS=1` (`Replay.RecordWithAi`); that seed plays every trigger, and the test requires it. A collapse drops every unit on a sunk tile, including two sharing one mid-walk.

## Deferred minor findings (for the final review to triage)

| Task | Finding |
|---|---|
| 1 | Lemire rejection branch untested; `default(Rng)` is degenerate; Dice regex `$` accepts a trailing newline (use `\z`); `ByLevel` record equality is by reference; id converters are public |
| 2 | Paths tests lack an optimality case, a 2D enemy detour and a blocking-prop case; `Terrain`/`Paths` allocate per call (the AI should cache) |
| 3 | The final-cover test's Total comes from rays, not the prop rule; an attacker standing on a cover prop can give the target cover; `Columns` is O(bounding box); Ring of size 0 returns the aim tile |
| 4 | `Start` normalises only `Sheet.Resources`; records holding arrays compare by reference (golden tests compare JSON); rejection-text test checks only 3 of 16 strings; `CantAfford` with no resource gives "Not enough ."; `ObjectiveExpr` allows a leaf with a group; `UnitSheet.Score` defaults a missing stat to 10 |
| 5 | A self line/cone with no aim tile pays its cost and hits nothing; the converter's key detection could collide with a parameter named like a block; `Battle.cs` is about 320 lines (targeting could move out) |
| 6 | No HP event on deep-water drops (Task 8 addresses); prone applied after a lethal fall; Swap has no landing check; `Movement` depends on `Motion.OccupantAt` |
| 17 | Weapon-rider abilities use the ability's range, not the equipped weapon's (matters once players pick weapons); `QuarryDamage` recursion guard; Stunning Strike's rider is missing; Step of the Wind's Jump +2 needs a `StaticMods.Jump` field; thin tests for Lay on Hands, Hail of Thorns and Deflect Attacks |
| 16 | One unnamed test failure seen once, probably the AI speed test competing for CPU (simulator tests now run on their own); the temporary-HP soak path is untested |
| 14 | Self-area threat gets a high-ground range bonus; a bonus-action Dash is never planned; the bonus pass only excludes the direct target, not downed foes caught in an area |
| 13 | SRD approximations parked for the balance pass: manoeuvres apply to each attack with Extra Attack, Feint, Spiritual Weapon, Empowered Evocation, marked, Uncanny Dodge/Stone's Endurance as temporary HP, Lucky, Indomitable, instant Spirit Guardians, bleeding not ended by healing, untyped Sneak Attack damage |
| 12 | Repeated error when the party spawn zone overflows; a placement level below 1 is reported against the monster file; `MaxHp` on monster or zone passives is ignored; inline monster weapons accept `replaces` |
| 11 | statMod tooltips read awkwardly ("target gets can't act"; Task 13 fixes it); hook phrases not tested for every `HookKind`; "half damage on a save" missed for nested damage |
| 10 | A second unit's mark replaces the first's; counterspell picks the first queued tier ≥ 1 step; `TerrainAltered.Delta` reports the requested change; negative rounds and unbounded repeat/chain counts aren't validated; zone passives with the default `other` target never fire on enter/turn start; `Battle.cs` is 444 lines |
| 9 | Trigger `do` effect blocks are only checked for known keys (Task 22 must fully check them); a second `xp.json` reports its duplicate at a nonexistent `/id`; the pack label comes from the manifest id, not the folder name |
| 8 | Fall/collision damage has no source (no `OnKill` for the pusher); `RoundStarted.Order` can list a unit that goes Out right after; golden fixture lacks `TurnSkipped`/`UnitOut`/`BattleEnded`; `Invariants.Enabled` is a public static; `.json` line endings not pinned |
| 22 | `say` has no separate narration flag (narration is speaker `"narrator"`); the `warlord_bridge` collapse can drop two party members into the chasm at once, which Task 23 should check against the win-rate band (the AI party won all of seeds 1–60 with the 70 HP boss, in 3–9 rounds, so the encounter may be too easy); `ObjectiveState.Progress` is still unused |
| 7 | Several `Hooks.Fire` calls in one step run in reverse order; passive and ability `UsesLeft` share keys; `AttackStep` doesn't check the attacker is alive at Resolve; several `WhenDef` leaves and limits untested; `Modifiers.For` recomputed often (performance) |
| 23 | Unmet role bands (8 of 60: rogue 3, fighter 5, 7 and 10, paladin 7, barbarian 10, druid and wizard 10); mitigated counts attacks drawn and treats resistance as less mitigation; no encounter is built for level 10; 10 abilities are unused because of planner limits (empty-tile abilities, bonus-action Dash, extra actions, Twinned and Careful, Steady Aim, hook-only conditions); subclasses are not simulated; encounters do not scale with party size |
