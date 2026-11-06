# Wardenflame Tactics: environmental tiles that react to spells

> **Agreed design.** Written as a draft on 2026-11-01. The user answered every question on 2026-11-02 (the table at the end). Build from `docs/superpowers/plans/2026-11-02-tactics-environmental-tiles.md`, which folds those answers in and wins where the two differ.

## Summary

Today a tile's `water` and `hazard` flags do nothing in the rules, and no spell changes a tile except the Geomancer's `alterTerrain`. This draft adds a small, bounded set of **tile reactions**: lightning runs through connected water, fire catches on dry fuel and spreads a tile a round, cold freezes shallow water into slippery ice, an oil flask makes ground flammable, fire burns webs away, and wading into water puts out burning.

The core idea is to **reuse the engine's zones** (`Zone` in `BattleState.Zones`) as the tile state. Zones are already saved, drawn on the TV with the hazard art, read by the AI and carried in `BoardChanges`. So the change is mostly one new queue step that looks at an ability's footprint after it resolves, one new event for the TV to animate, a content file of reactions, and some AI scoring. It is **opt-in per encounter**, so no existing battle, golden replay or balance number moves until an encounter turns it on. The first one would be River Crossing and Storming the Ford, on the Ashen Ford river.

---

## 1. Goals and non-goals

**Goals**
- Make the map matter to spell choice: where you aim a spell, and what you stand in, both count.
- Keep it **readable on a sofa**. Every reaction has one clear cause, and the TV can show it in under about 1 s from the beat's own events. The phone warns before you commit.
- Keep it **D&D 5e SRD-flavoured**: use SRD wording where the SRD has a rule (Web burns, Oil, Slippery Ice, the Burning hazard), and mark each fudge.
- Keep it **bounded and deterministic**. Every spread ends because fuel burns only once. Nothing draws dice outside the battle RNG, and the iteration order is fixed.
- Keep it **data-driven**: tile traits, durations and damage live in content JSON, and the geometry (flood fill, spread) lives in C#.
- Let the AI both **avoid** dangerous tiles and **exploit** them, through the planner's existing expected-value scoring.

**Non-goals (this slice)**
- No weather, wind, water flow, steam, smoke from fires, or combos of combos (fire on ice making fog, say).
- No change to the overworld. Tile states end with the battle, and nothing is written back to the region or the campaign document.
- No destructible or flammable **props** (crates, trees, braziers). Props stay as they are.
- No change to existing spells' numbers, the area ladder, `deep` and falling, or `alterTerrain`.
- No new art generated through Scenario. The draft reuses the hazard, fx and terrain art already in the pack.
- The `hazard` tile flag (lava in Hall of the Flame) stays harmless. See open question 8.

---

## 2. Today

### 2.1 Terrain and flags

`TileFlags` (`Tactics.Logic/Map/BattleMap.cs`) has `Water = 1`, `Difficult = 2`, `Impassable = 4`, `Hazard = 8` and `Deep = 16`. `Terrain` (`Map/Terrain.cs`) is the read-only view the rules use. It reads only `Impassable`, `Deep` and `Difficult`, plus the battle's height deltas, difficult zones and sunk tiles. **Nothing in the rules reads `Water` or `Hazard`.** The only other uses are in `Tactics.WorldTools` (painting) and validation. The spec (§3.7) promises that "water ... ends" burning, but the `burning` condition in `conditions.json` only has its 1d6 tick, so that rule was never built.

The base maps (decoded from `content/tactics/base/rules/maps/*.json`):

| Map | Water and hazard tiles | Fuel-like materials |
|---|---|---|
| `river_crossing` | 20 deep river tiles (`water/clear`, Water+Deep), 4 ford shallows (Water+Difficult) at (6–7, 7–8), which is the `ford` zone | none: `grass/lush` |
| `warlord_bridge` | 36 void chasm tiles; its `ford` is rock (Difficult), not water | 8 `mine_boards/oak` deck tiles (the `bridge` zone) |
| `crystal_cave` | 12 void tiles | 12 `mine_boards/oak`, and the `spider_nest` zone (a spawn zone with no web on it) |
| `hall_of_the_flame` | 14 `lava/molten` tiles, Hazard+Deep, so harmless except that they're deep | none |
| `forest_ruins`, `keep_gate`, `crypt_hall` | none | none (`grass/lush`, stone) |

No map uses `grass/dry`, `grass/autumn` or `forest_floor/leafy` yet, although the terrain art has them (`content/tactics/base/terrain/manifest.json`).

A deep tile can't be entered on foot. A party member pushed in is Downed and lies there (`Motion.FallIn`, and `Invariants.InDeepWater` allows it), so **the river only ever holds Downed units**. Standing units in water are only ever in the four ford tiles.

### 2.2 Zones and hazards

`Zone` (`Content/Defs.cs`) is the engine's lasting area effect: a serial, an owner, a kind, tiles, `RoundsLeft`, concentration, `Difficult`, `BlocksSight`, and passives that belong to every unit standing on its tiles (`ZoneRules.PassivesAt`). `ZoneRules` (`Effects/Blocks/MapBlocks.cs`) creates them with `ZoneCreated`, ends them with `ZoneEnded`, ticks them at their owner's turn end, and ticks orphaned ones at round start (`ZoneRules.TickOrphans`, called from `Battle.cs` right after `RoundStarted`).

The `hazard` block (`HazardEffect` / `HazardBlock`) is a zone whose kind is a hazard art key, with an `onEnterTile` passive. **No ability in the base pack uses it.** The zone kinds abilities lay today are `fire` (Wall of Fire), `ice` (Ice Storm, Sleet Storm), `grease`, `web`, `spikes`, `thorns`, `vines`, `entangle`, `caltrops`, `rubble`, `snap_trap`, `poison`, `holy`, `moonbeam`, `darkness`, `smoke`, `smoke_cloud`, `void`, `ember_rune`, `frost_rune`, `grand_sigil`, `grove` and `silence`.

### 2.3 Spells and damage types

Damage types are strings on `DamageEffect.Type` (or a weapon's `DamageType`). Resistances, immunities and vulnerabilities apply in `Hp.Damage` (`Effects/Blocks/Hp.cs`), and `WhenDef.DamageType` lets a passive test the type. The types that matter here:

| Type | Abilities (base pack) |
|---|---|
| **lightning** (12) | Shocking Grasp, Chromatic Orb, Witch Bolt, Lightning Bolt (line 10), Call Lightning (burst 1), Lightning Arrow, Stormcaller's Chain Tempest and Wrath of the Tempest, Wild Surge |
| **fire** (31) | Fire Bolt, Produce Flame, Burning Hands (cone 3), Scorching Ray, Fireball (burst 2), Wall of Fire (ring 2), Flame Strike, Alchemist's Fire (item), the kobold's Fire Pot, the goblin shaman's Cinder Bolt, dragon and drake breath, Hellish Rebuke, the dragonborn breath, the Runeborn's Ember Rune, Maren Sorrel's Ember Lance and Hearthfire, and more |
| **cold** (12) | Ray of Frost, Ice Storm (burst 2), Cone of Cold, the Runeborn's Frost Rune, the water elemental's Tidal Crush, Ivo Rime's Rime Grasp, the lich's Paralysing Touch, Hunger of Hadar |

At level 1 (River Crossing's level), a party has cantrips and tier 1: Fire Bolt, Ray of Frost, Shocking Grasp, Burning Hands, Produce Flame and Alchemist's Fire. Lightning Bolt and Call Lightning arrive at level 5.

### 2.4 Board changes

`BattleState` holds `HeightDelta` (raised and sunk tiles) and `Sunk` (tiles that became deep: a collapsed bridge, from `alterTerrain` with `deep`). `AlterTerrainBlock` emits `TerrainAltered`, and a unit on a tile that sinks falls in. `BoardChanges` (`Tactics.Game/TacticsGame.Views.cs`) carries `Zones` (as `ZoneView`: serial, kind, tiles), `Heights` and `Sunk` to the **TV only**. Phones don't get it. `DisplayedBattle.Apply` (`Tactics.Presentation/Beats`) keeps the TV's board in step with each event, and `BoardChangesOnWorld` moves it onto world tiles for a fight in the world. Zones "draw on the stage alone", so they never make the world rebuild its blocks. Height and sink changes do.

### 2.5 The TV's art and sound

- **Hazard art** (`content/tactics/base/hazards/manifest.json`, 8 frames at 8 fps, a 64×32 ground frame, some with an `over` layer drawn above units): `burning`, `poison`, `ice`, `web`, `thorns`, `consecrated`, `oil`, `caltrops`, `darkness`, `flooded`, `acid`, `spikes`, `fog`, `sleet`, `electrified`. The art's own notes already say "oil: ignites into burning ground", "flooded: conducts lightning", "web: burns away under fire", "sleet: puts out fire", and "electrified: lightning damage at turn start". Rules haven't caught up with them.
- `ZoneArt.HazardFor` (`Board/MaterialLook.cs`) maps zone kinds to hazard art: `fire`→`burning`, `ice`→`ice`, `grease`→`oil`, `web`→`web`, and so on. `BoardPlan.AddZones` draws each zone's ground frame on the `Ground` layer and its `over` frame on `Over`, or a violet tint for a kind with no art.
- **Terrain art**: `water/clear|swamp|bog` and `lava/molten` are 16-frame animated tops. There is also an `ice/clear` top, `grass/dry` and `grass/autumn` tops with lips, `planks`, and a `scorch/burnt` decal.
- **Fx** (`fx/manifest.json`): `shock_storm`, `shock_violet`, `lightning_storm`, `flames_fire`, `explosion_fire`, `frost_frost`, `ice_frost`, `dust_snow`, `misty_mist`, `poof_white`, and more.
- **Sfx** (`sfx/manifest.json`, 150 sounds): `spell_lightning`, `dmg_lightning`, `spell_fire_aura`, `spell_fire_explosion`, `dmg_fire`, `spell_ice`, `dmg_cold`, `step_ice`, `step_water`, `smoke_poof`, `zone_created`, `terrain_shift`, `amb_river`.

### 2.6 The AI and hazards

The planner values laying a zone (`Scoring.Worth`, `ZoneEffect` and `HazardEffect` cases: `onTurnStart` effects × rounds, `onEnterTile` at half) **but doesn't avoid standing in one**. `Planner.Position` scores threat, high ground, flanking and objectives, and the only zone input is static modifiers (`Scorer.Sources`). Path costs come from `Paths.StepCost`, which knows only Move. So an AI unit will end its turn in a Wall of Fire or walk through Spike Growth today. Fixing this helps the existing zones too, which is a behaviour change in its own right (§7).

---

## 3. The first set of interactions

Six rules. Each fires from an **ability use**, meaning a player's or the AI's `Use` command, never from a condition's tick or a zone's passive, apart from a fire's own spread. That one limit is what keeps cascades bounded. The footprint is the ability's area tiles (`Areas.Tiles(area, user.Pos, aim, terrain)`) for an area ability. For a single-target attack it is the target's tile, and only on a hit: Fire Bolt that misses doesn't light the grass behind the goblin.

"Shallow water" means `Water` and not `Deep`. "Fuel" is a content list of materials (§4.2), plus oil surfaces.

### R1. Lightning conducts through water

- **Trigger:** an ability that deals `lightning` damage, whose footprint includes at least one water tile (shallow or deep) that isn't iced.
- **Effect:** a flood fill from those tiles over 4-connected water tiles, not iced, at most **6 steps** from the footprint, in sorted `Tile` order, breadth first. Every unit **standing** in a filled tile that the ability didn't already affect (either side, the caster included) makes a **DEX save** against the caster's spell DC and takes the ability's lightning dice, **half on a success**. Downed units in the river are skipped (open question 1).
- **Tiles:** no lasting change. It is a flash.
- **Duration:** instant.
- **SRD basis:** a fudge. The SRD has no conduction rule. It is the user's example, and a staple of tactics games. The save and the half on a success follow Lightning Bolt's own wording.
- **On today's maps:** the ford's four tiles connect to the whole river, so lightning into the ford hits everyone wading. A party crossing in single file is the target, and so is a goblin who followed them in.

### R2. Fire catches on fuel and spreads

- **Trigger:** an ability that deals `fire` damage, whose footprint includes fuel tiles that aren't burning or scorched and aren't water.
- **Effect:** those tiles become a **burning** surface. A unit that enters a burning tile, or ends its turn on one, takes **1d6 fire**, at most once per turn (the `burning` condition's die, with the SRD Oil's "only once per turn"). Units already standing there when it catches aren't "entering": they get their turn to step out, as `ZoneRules.Create` already treats any new zone.
- **Spread:** at each round start, right after `ZoneRules.TickOrphans`, every burning tile lights each 4-adjacent fuel tile that hasn't burned yet. That's one tile a round, with no dice, so the phone and TV can show exactly where it will go next.
- **Duration:** a tile burns for **2 rounds** (the SRD Oil's "2 rounds"), then becomes **scorched**: no damage, and never fuel again. So every fuel tile burns at most once, and any fire ends within (the fuel patch's diameter + 2) rounds.
- **SRD basis:** Fire Bolt and Fireball "ignite flammable objects that aren't being worn or carried". The spread is a fudge, like a wildfire. The numbers are the existing `burning` condition's.
- **Fuel in the base pack:** `planks/*`, `mine_boards/*` (Warlord Bridge's deck, Crystal Cave's boards), `grass/dry`, `grass/autumn`, `forest_floor/leafy`, and oil (R4). **`grass/lush` and `grass/green` are not fuel**, or every grass map would become a bonfire.

### R3. Cold freezes shallow water

- **Trigger:** an ability that deals `cold` damage, whose footprint includes shallow water.
- **Effect:** those tiles get an **ice** surface. It is difficult terrain (the ford already is), and the first time a unit enters iced tiles on a turn it makes a **DEX save, DC 10**, or falls **prone**. Ice also **insulates**: R1 doesn't flow through an iced tile, so freezing the ford cuts the river's circuit.
- **Thaw:** fire damage whose footprint includes an iced tile ends the ice there, and lights nothing.
- **Duration:** **3 rounds**, a fudge.
- **SRD basis:** the SRD's Slippery Ice (difficult terrain, and a DC 10 Dexterity check or prone on first entering it in a turn). The engine has saves and no skill checks in battle, so it's a DEX save. Freezing water with a cantrip is a fudge.
- **Not in this rule:** freezing **deep** water into a walkable sheet. That is open question 2, because it rewrites River Crossing's pathing.

### R4. The oil flask

- **New item:** `oil_flask` ("Flask of Oil"), using `item.oil_flask`: thrown, range 4, at a tile or a unit, one Action.
- **Effect:** an **oil** surface on the aim tile and its four neighbours (burst 1) for **10 rounds**. A unit in the area makes a **DEX save** (`dcStat: dex`, as Alchemist's Fire does) or gains **oiled** (a new condition: the next fire damage it takes deals an extra **5 fire**, then it ends, or after 10 rounds). Oil tiles are **fuel**: R2 lights them, and lit oil burns like any fuel.
- **Duration:** 10 rounds unlit (the SRD's 1 minute), then 2 rounds burning, then scorched.
- **SRD basis:** SRD 5.2 Oil: douse a creature (a DEX save, or 5 extra fire damage if it takes fire damage within 1 minute), or douse a 5-ft square that burns for 2 rounds once lit. The burst 1 instead of one square is a fudge for a 14×12 board. The hazard art marks `oil` as difficult, but SRD oil isn't slippery, so it isn't difficult (Grease is, and Grease isn't flammable in SRD 5.2: open question 3).

### R5. Fire burns webs away

- **Trigger:** fire damage whose footprint includes tiles of a `web` zone (Web, from the wizard and sorcerer).
- **Effect:** those tiles leave the web zone and become **burning** for **1 round**. A unit on one at the start of its turn takes **2d4 fire**. A unit restrained only by that web is freed, because its passives no longer reach it. A web zone that loses only some tiles ends, and a zone of the tiles left is made in its place, so the TV needs no new event to draw it.
- **Duration:** 1 round.
- **SRD basis:** SRD Web: "Any 5-foot cube of webs exposed to fire burns away in 1 round, dealing 2d4 fire damage to any creature that starts its turn in the fire." That's direct.

### R6. Water puts out burning

- **Trigger:** a unit with the `burning` condition **enters** a water tile, by any means (walking, being pushed, falling in).
- **Effect:** `burning` is removed (`ConditionRemoved`). Burning surfaces never form on water.
- **Duration:** instant.
- **SRD basis:** the SRD Burning hazard: the fire "goes out if it is doused, submerged, or suffocated". This also builds what spec §3.7 already promises.

### Considered and left for later

Electrified water that lingers (the `electrified` art), freezing deep water, fire on props, a burning bridge deck making the bridge collapse, thunder shattering ice, sleet putting out fire, Gust of Wind moving fire or fog, acid pools, and steam.

---

## 4. How it fits the engine

### 4.1 Tile state: zones, with two optional fields

**Proposal: surfaces are zones.** A burning patch is a `Zone` of kind `burning`, ice is `ice_sheet`, oil is `oil`, and a burnt patch is `scorched`. It reuses everything zones already have: saving, `ZoneCreated` and `ZoneEnded`, `ZoneRules.PassivesAt` for the damage (an `onEnterTile` and `onTurnEnd` passive with `OncePerTurn`), `BoardChanges.Zones` for the TV, `BoardChangesOnWorld` (zones need no world block rebuild), and the AI's zone scoring.

`Zone` (`Content/Defs.cs`) gains two optional fields, both `WhenWritingDefault`, so old saves read unchanged:
- `Surface` (bool): it ticks once a **round**, at round start, not at its owner's turn end, and isn't dispelled by `dispel` with `zones`. A surface's `Owner` is whoever caused it (an R2 spread inherits its parent's owner), so kills and `OnKill` hooks credit the caster, but its life doesn't depend on that owner.
- `Source` (string, optional): the rule that made it (`"ignite"`, `"spread"`, `"freeze"`, `"oil"`), for the TV and tooltips.

A spread makes **one zone per source per round** that holds every tile that caught that round, so a big fire is a handful of zones, not one per tile. "Has this tile burned?" is answered by a `scorched` zone being on it.

**Rejected alternative:** a new `BattleState.Surfaces` map (`Tile → SurfaceState`), next to `Sunk`, with its own `BoardChanges.Surfaces`. It is cleaner per tile, but it duplicates the zones' passives, saving, drawing, the AI's and `DisplayedBattle`'s handling, and the TV's undo. It would be worth it only if the state stopped being "a set of tiles with effects", for instance if water levels changed.

The `Terrain` view (`Map/Terrain.cs`) doesn't change. Ice is difficult through its zone's `Difficult`, which `BattleWriter.Terrain` already folds in, and the tile's own `Water` flag stays.

### 4.2 Content: `rules/environment.json`

A new file in the base pack's manifest, with a schema in `rules/schema/` and checks in `Content/Validation/`:

```json
{
  "fuel": ["planks/*", "mine_boards/*", "grass/dry", "grass/autumn", "forest_floor/leafy"],
  "surfaces": {
    "burning":    { "rounds": 2, "spreads": true, "leaves": "scorched",
                    "passives": [ { "id": "surface.burning", "hook": "onEnterTile", "target": "self", "oncePerTurn": true,
                                    "effects": [ { "damage": { "dice": "1d6", "type": "fire" } } ] },
                                  { "id": "surface.burning.end", "hook": "onTurnEnd", "target": "self", "oncePerTurn": true,
                                    "effects": [ { "damage": { "dice": "1d6", "type": "fire" } } ] } ] },
    "ice_sheet":  { "rounds": 3, "difficult": true, "insulates": true,
                    "passives": [ { "id": "surface.ice.slip", "hook": "onEnterTile", "target": "self", "oncePerTurn": true,
                                    "effects": [ { "save": { "stat": "dex", "dc": 10 }, "onFail": [ { "apply": "prone" } ] } ] } ] },
    "oil":        { "rounds": 10, "fuel": true },
    "scorched":   { "rounds": null }
  },
  "reactions": [
    { "id": "conduct", "damage": "lightning", "tiles": "water",       "conduct": { "reach": 6, "save": "dex", "half": true } },
    { "id": "ignite",  "damage": "fire",      "tiles": "fuel",        "surface": "burning" },
    { "id": "burn_web","damage": "fire",      "tiles": "zone:web",    "surface": "burning", "rounds": 1, "consumes": true },
    { "id": "thaw",    "damage": "fire",      "tiles": "surface:ice_sheet", "ends": true },
    { "id": "freeze",  "damage": "cold",      "tiles": "shallows",    "surface": "ice_sheet" },
    { "id": "douse",   "enters": "water",     "remove": "burning" }
  ]
}
```

R5's 2d4 sits on a `surface.burning.web` passive variant. The exact shape is for the plan. What matters is that the numbers, durations, fuel list and which damage type does what are all content, checked at load (unknown material families, surface names and damage types are load errors with a JSON pointer). The flood fill, the spread and the ordering are C#. `RulesPack` (`Content/RulesPack.cs`) loads it as `Environment`, and it goes into the content hash.

**Opt-in:** `EncounterDef` gains `"environment": true` (default false), carried through `BattleSetup` into `BattleState` as a `WhenWritingDefault` bool. Until an encounter opts in, nothing reacts, so every current battle, golden replay and sim number stays the same (open question 5).

### 4.3 Where reactions run: a new step

`Battle.PlanUse` (`Rules/Battle.cs`) queues `PayCostStep` and then the ability's `RunEffectsStep`s. It would add one last step:

```
ReactStep(UnitId Source, AbilityId Ability, Tile Aim, ImmutableArray<Tile> Footprint, int Resolution)
```

- It runs after every effect of the use, reactions to it included. So Fireball's damage is dealt before the grass catches, and Lightning Bolt hits its line before the water carries it.
- It reads the ability's **static** damage types from its effect tree (`DamageEffect.Type`, or the weapon's type), the way `Scoring.Effects` already does. For a single-target attack, it reacts only if this use emitted a hit (`AttackResolved` with `Hit`, which it can read from `BattleWriter.Events`).
- It applies the reactions in the order the content lists them. A tile changed by one reaction isn't changed again by another in the same step, so fire on an iced fuel tile only thaws it.
- R1 queues, for each conducted unit in `UnitId` order, an ordinary `RunEffectsStep` with a `save` block (half on a success) around the ability's lightning damage. So saves, resistances, reactions, Downed handling and the events are the existing ones.
- R6 lives in `Motion.Entered` (`Effects/Blocks/Motion.cs`), which every way of arriving on a tile already goes through.
- R2's spread runs in the new-round branch of `Battle.cs`, next to `ZoneRules.TickOrphans`, through a new `Surfaces.RoundStart(BattleWriter)` in a new `Effects/Blocks/SurfaceRules.cs`.

`ReactStep` is a serialisable `Step` (a new `JsonDerivedType` on `Step`), so a save mid-reaction resumes exactly, like every other step.

### 4.4 Events for the TV

One new event, emitted **before** the ordinary events a reaction causes:

```
TileReacted(string Rule, UnitId? Source, ImmutableArray<Tile> Tiles)
```

- `Rule` is the content id (`conduct`, `ignite`, `spread`, `freeze`, `thaw`, `burn_web`, `douse`).
- `Tiles` are in the order to animate them. For `conduct` and `spread` that is breadth-first distance from the source, so the TV can ripple outwards.
- Everything else is existing events: `ZoneCreated` and `ZoneEnded` for surfaces, `Rolled`, `Damaged`, `ConditionApplied` and `ConditionRemoved`, `Downed` and `Died`.

This meets the binding rule "animate from each beat's own events, not from state": the TV knows that lightning ran through these six tiles in this order without diffing the board. Golden replays show reactions as readable event diffs.

### 4.5 Determinism

- The flood fill and the spread iterate over `ImmutableSortedSet<Tile>` in `Tile` order. There is no `Dictionary` iteration, and ties break on `Tile`, then `UnitId`.
- The spread uses no dice. Saves and damage roll from `BattleWriter.Roll`, in queue order.
- The AI scores reactions analytically and never draws from the RNG (spec §5.2).
- The existing determinism test (serialise, deserialise, apply, after every command of every golden) covers `ReactStep` once a golden has reactions in it.

### 4.6 Save compatibility

- `Zone`'s new fields and `BattleState`'s `environment` flag are optional and default-off. A battle saved before the change loads and plays the same.
- Adding `environment.json` changes the **content hash**, so a battle saved mid-fight under the old pack restarts from its start state with the TV notice (spec §2.5). That is today's behaviour for any content change.
- The campaign document doesn't change: surfaces end with the battle.
- New event and step `$type`s: the TV and server ship in one build. The phone doesn't read battle events by type.
- New invariants in `Rules/Invariants.cs`: surface tiles are in bounds; a tile has at most one of `burning`, `ice_sheet` and `oil`; nothing burns on water or on a scorched tile; an iced tile is shallow water. They run in Debug, as all invariants do (handoff decision 11).

### 4.7 AI awareness

**Exploit (offence).** `Scorer.Walk` and `Strike` (`Ai/Scoring.*.cs`) value an ability by its effects. They'd add the expected value of the reaction:
- R1: each conducted unit's expected damage (save odds × dice × resistances), through the same `Worth` that already weighs friend against foe. So the AI casts into the ford when foes are wading, and holds off when its own goblins are in the water, as far as its profile weights friendly losses.
- R2 and R4: a small value per fuel tile lit, for each foe standing on or next to it (they'll have to move or burn), capped like the zone scoring's three rounds.
- R3: the slip's prone odds for foes who'll cross, valued like the existing difficult-zone 0.3 × control.

**Avoid (defence).** `Planner.Position` gains a **tile danger** term: the expected damage of the passives a unit would meet by **ending its turn** on the tile (`onTurnEnd`, and `onTurnStart` for next turn), weighed like the threat term by caution and the unit's HP. Entering costs are added along the path for the tiles the planner already keeps (it prunes to 12). This covers Wall of Fire, Spike Growth, Moonbeam and the rest, so it **changes today's AI** on every encounter with those zones and needs its own sim run (§7). A danger-weighted path search is a later step, not part of this.

**Companions and bots.** The companion profile uses the same scoring. `bots.cjs --fight` already refuses "one that would hurt a friend" from the phone's previews, and conducted allies appear in those previews (§5.2), so the bots avoid it with no change.

**Budget.** A flood fill on a 14×12 map is at most 168 tiles. It is cached per (footprint, water state) in the `Scorer`. `AiPerformanceTests` must stay at or below 250 ms p95.

---

## 5. TV and phone

### 5.1 The TV

**Drawing the surfaces** (all reuse, no new art):

| Surface | Art | Notes |
|---|---|---|
| `burning` | hazard `burning`, with its `over` flames | `ZoneArt` gains `"burning" → "burning"` (Wall of Fire's `fire` already maps there) |
| `ice_sheet` | hazard `ice` over the water's animated top | the water keeps moving under it. Swapping the top for `ice/clear` would read better, but it is a material-look override that `BoardTerrain` and the world's blocks would have to rebuild for: later |
| `oil` | hazard `oil` | already mapped for `grease` |
| `scorched` | the `scorch/burnt` decal | `BoardPlan.AddZones` learns to draw a decal kind, which is small |
| conduction flash | hazard `electrified` (ground and `over`) for the beat only, with fx `shock_storm` on each unit hit | not a zone: drawn from `TileReacted` and gone after the beat |

**Animating it** (`Beats/BeatScript.cs` gets a `TileReacted` case, with timings in `BeatTiming`):
- `conduct`: the `electrified` frames ripple tile by tile in the event's order (about 40 ms a tile, like the `Ripple` collapse), with `spell_lightning` then `dmg_lightning`. The saves and damage pops play as an area's do, all together. Under 0.8 s.
- `ignite` and `spread`: `flames_fire` bursts on the new tiles, `spell_fire_aura`, then the zone appears through the existing `ZoneCreated` path. The spread happens at round start, so it plays in the `RoundStarted` beat, before the first turn.
- `freeze`: `ice_frost` and `dust_snow` bursts, `spell_ice`. `thaw` and `douse`: `misty_mist` or `poof_white`, `smoke_poof`.
- `burn_web`: `flames_fire` on the web tiles, then `ZoneEnded` and `ZoneCreated`, as now.

Frame indices come from the manifests, never hard-coded. Every reaction stays under the ~1 s attack budget, and a missing TV still never stalls the game: these are ordinary beat events, paced by the same `shown` signal and fallback.

`DisplayedBattle.Apply` needs no change for surfaces (they are zones). `Undo` treats `TileReacted` as nothing to undo.

### 5.2 The phone

**This is a server-to-phone contract change, which the handoff's binding rules say needs the user's OK** (open question 6). Proposed, each an optional field so older phone builds ignore it:

- **Move preview.** `MoveChoice` (`Rules/Choices.cs`) gains `Risk`: the expected damage on that path and from ending the turn there, plus a short `Warn` (`"burning"`, `"ice: may fall prone"`). The turn screen's button reads "Move here · 3 · burns ~4", and the minimap tints risky reach tiles amber. The phone has no zones today (`BoardChanges` is the TV's alone), so this is how it would learn of them.
- **Target preview.** Conducted units are added to `TargetChoice.Affected` as ordinary `UnitPreview`s with their expected damage. **That needs no shape change**, and the phone already shows allies who'd be hurt. `TargetChoice` gains optional `Spread`: the tiles the reaction would touch (the conduction fill, or the fuel that would catch). The minimap draws them in a second, dashed aim colour, so "this lightning reaches the whole ford" shows before you commit.
- **Tooltips.** `TooltipText` and `Tooltips` generate a line from the content: "Lightning: runs through connected water (6 tiles)", "Fire: lights dry grass, planks and oil". No new contract here, only new text.

If the contract change is refused, the fallback is a **tooltip-only** phone, with `Affected` still carrying conducted units, and the TV as the only place that shows surfaces.

Phone fixtures regenerate (`UPDATE_FIXTURES=1 ... --filter-class "*PhoneFixtureTests"`) and the diff is reviewed.

---

## 6. Content: where it's used first

1. **River Crossing and Storming the Ford** (`river_crossing` map, level 1, stamped in Ashen Ford at (100, 52)). The ford's four shallows join the 20-tile river. At level 1 the party has Shocking Grasp (R1, but only on a hit on someone in the water), Ray of Frost (R3 on a wading foe), Fire Bolt and Alchemist's Fire (R6 matters once someone's burning near the ford). Mother Skarrow already shouts "Shoot them while they wade!", so the ford is the scene's danger. **No map edit is needed**, so the world stamp still matches its map. Opt both encounters in, and run the sim first (§7). Storming the Ford is already hard for bots (about 5 of 10 from the "!" side), so any added enemy option needs the user's say (open question 7).
2. **Warlord Bridge** (level 5): the 8-tile oak deck is fuel. Burning the bridge under the warlord's men before they cross fits the scene. The `bridge_collapse` trigger fires when the party steps on, and whether a burnt deck should drop on its own is a later trigger question. At level 5, Lightning Bolt and Fireball are in play.
3. **Rime Tower** (`crystal_cave`, level 7): give `spider_nest` a web zone at battle start (an `effects` trigger action that already exists), so R5 has a natural target, and Ivo Rime's cold matters if a pool is added. That's a map edit, and the map isn't stamped in a region, so it's safe for the world checks.
4. **Forest Ruins** (level 3, not stamped): repaint a patch of `grass/lush` to `grass/dry` or `forest_floor/leafy` as the fire showcase. It is a balance-visible change, so it waits for the sim.

New content (an enemy with a lightning spell for the ford, oil flasks in loot) belongs to the user's balance calls.

---

## 7. Balance risks and testing

### 7.1 Risks

- **Friendly fire.** R1 hits allies and the caster in the water. That's the point, but a group can grief, and the AI might drown its own goblins. The previews and the `Worth` sides are the guard.
- **Level 1 fragility.** 1d6 burning against about 10 HP heroes is a third of a wizard. R2's once a turn and the 2-round burn cap it.
- **Snowballing fire** on a large fuel patch. It is bounded by fuel, and a load-time validation **warns** when a connected fuel patch on a map is over 12 tiles.
- **Cheese.** The AI blind to danger today (§2.6) means a party could kite foes through fire. The defence term fixes that, but it moves every encounter's numbers.
- **Prone in the ford under archers.** R3 plus Skarrow's archers could swing Storming the Ford hard against a party that freezes its own crossing.
- **Ability-use flags.** Fire and lightning spells will move in the "<2% of legal turns" list, and so will Ray of Frost.
- **Pacing.** Surfaces can stretch battles past the 8–12 round target if units stall at fire edges.
- **The area ladder** (`ContentLadderTests`) counts only direct damage, so conduction doesn't break it. Say so in the README's conventions.

### 7.2 Tests

| Kind | What it proves |
|---|---|
| **Unit** (`Tactics.Logic.Tests`, with the `AsciiMap` helper) | Each rule on small pictured maps: conduction's reach, ice insulating, both sides hit, the Downed skipped; spread order, one tile a round, scorched never relit; freezing only shallows; oil then fire; webs burning per tile; dousing on walk, push and fall. Plus tooltips for every reaction |
| **Model** (`SurfaceModelTests`, after `WalkModelTests`) | Random maps with fuel, water and webs; random fire, cold and lightning uses and moves. After every command: the invariants hold; total burning tile-rounds ≤ 2 × fuel tiles; no tile burns twice; every fire is out within (patch diameter + 2) rounds; serialise, deserialise and apply equals applying directly. Raised with `SURFACE_MODEL_EVENTS` |
| **Mutation** | Break a rule and see a test fail: let scorched tiles burn (termination fails), drop ice insulation (conduction test fails), reverse the `Tile` order (golden diff), let conditions trigger reactions (the cascade bound fails) |
| **Golden** | A new `river-ford-storm.replay.json`: lightning into the ford, a freeze, a douse. The existing goldens stay byte-identical while nothing opts in. That check is itself a test |
| **AI scenarios** (`Ai/PlannerTests.cs`) | Casts lightning into a ford with two foes and no ally in it; doesn't with an ally in it; doesn't end its turn on burning ground when a near-equal safe tile exists; walks round ice rather than through when it can. `AiPerformanceTests` at or below 250 ms p95 |
| **Sim** (`Tactics.Sim`) | `--encounters river_crossing,ford_assault --levels 1,3,5 --seeds 20` with `environment` on and off: win rate in 60–95%, mean rounds in 8–12. New tallies in `Report.cs`: reactions per battle, friendly damage from conduction, burning tile-rounds, deaths to surfaces. Then a full sweep for the AI defence change alone, since it touches every encounter |
| **Sweep harness** | `battle-sweep-harness.cs.txt` with `SWEEP_SEEDS=100` on the opted-in encounters, invariants on, for crashes |
| **Presentation** | `BeatScriptTests`: each `TileReacted` plays under 1 s, ripples in order and adds its sounds; `BoardPlanTests`: the scorch decal and the new `ZoneArt` keys draw; composed pictures of a burning deck and a frozen ford, compared, and judged in motion as well as frozen |
| **Phone** | Regenerated fixtures; web tests that the move button shows the risk and the minimap tints spread tiles |
| **Interaction review** | One reviewer thinking as friends on a sofa: can you tell why your friend took damage, and could you have seen it coming on your phone? |

---

## 8. Open questions for the user

1. **How strong is conduction?** As drafted, it's full lightning dice with a DEX save for half (your example), up to 6 water tiles, hitting allies and the caster too, and skipping Downed units in the river. Or should conducted damage be half to start with? And should it hit the Downed, which would tick their countdown?
2. **Freeze deep water?** A walkable ice sheet across the river would let Ray of Frost build a crossing at River Crossing. Fun, but it rewrites the map's pathing. If yes, what happens to someone on it when it thaws: they fall in and are Downed, or they're pushed to the nearest bank?
3. **Is Grease flammable?** SRD 5.2 says no, and players often expect yes. The draft says no. One content line flips it.
4. **Burning ground damage:** 1d6, like the `burning` condition (drafted), or a flat 5, like SRD Oil?
5. **Opt in per encounter** (drafted), or on for every battle once it's tuned?
6. **The phone contract change** (§5.2): may `MoveChoice` gain a risk and warning, and `TargetChoice` a spread? Or tooltips only?
7. **First content:** River Crossing and Storming the Ford, with no map edits? May the Ford get an enemy who uses it, such as a goblin storm-caller, given it's already hard for bots?
8. **The `hazard` flag** (lava) stays harmless here. Should lava light fuel next to it, or burn units pushed next to it, later?
9. **The oil flask:** who gets it? The starting kit, loot tables (sub-project 6), or enemies such as the kobold slingers?
10. **AI cruelty:** may enemies knowingly hit their own side through water when it nets out, at every difficulty? The draft says yes on Normal and Hard, and never on Story.
11. **The AI defence term** changes how every enemy treats Wall of Fire and Spike Growth today. Ship it with this work, or on its own first?

---

## 9. Rough plan, in milestones

| # | Milestone | Delivers | Gate |
|---|---|---|---|
| M0 | **This doc** | The user's answers to §8 | the user |
| M1 | **Engine core** | `environment.json`, its schema, loader and validation; `Zone.Surface` and `Source`; the encounter opt-in; `ReactStep`; R1, R2 (with spread and scorch), R3 (with thaw), R5 and R6; `TileReacted`; invariants; tooltips; unit, model and mutation tests. Nothing is opted in, so the goldens are unchanged | green build, existing goldens identical |
| M2 | **Oil flask** | the item, `item.oil_flask`, the `oiled` condition, R4 | tests |
| M3 | **AI** | reaction values in scoring; the tile danger term; scenario and performance tests; sim tallies; a full sweep for the defence term alone | sim report reviewed, question 11 |
| M4 | **TV** | the `TileReacted` beats, `ZoneArt` keys, the scorch decal, sounds; presentation tests; composed pictures; a `tv-e2e.sh` run where Godot is available (it may be missing in the cloud, so say what wasn't seen) | pictures judged in motion |
| M5 | **Phone** | `Risk`, `Warn`, `Spread`; minimap tints; fixtures | question 6 |
| M6 | **Content** | opt in River Crossing and Storming the Ford; sim at 20 seeds; a new golden; README conventions; balance report section. Then Warlord Bridge, and Rime Tower's web | question 7, sim in band |

**What can be cut, in order, if time is short:**
1. **M5 phone fields:** keep conducted units in `Affected` (no contract change) and tooltips only.
2. **M2 oil flask:** fire still has the decks.
3. **R5 webs** and the Rime Tower content.
4. **The scorch decal:** end the zone instead. Scorched then needs only to be remembered, not drawn.
5. **The path part of the AI defence term:** keep end-of-turn danger only.
6. **R3 freeze:** keep conduction and fire. That leaves the smallest version worth shipping: **R1 + R2 (decks only) + R6**, on River Crossing, Storming the Ford and Warlord Bridge.

---

## The user's answers (2026-11-02)

All eleven open questions are answered; the draft is now the agreed design, pending a build plan.

| # | Question | Answer |
|---|---|---|
| 1 | Conduction | Full damage to everyone in the connected water, DEX save for half; allies and the caster included; Downed units in the water aren't hit |
| 2 | Freezing | Shallow water only, into slippery ice (difficult terrain, slip check); deep water doesn't freeze |
| 3 | Burning ground | 1d6 fire on starting a turn in it, or entering it |
| 4 | Where it's on | Per encounter first; existing fights and balance don't move until each is tuned |
| 5 | Grease | **Flammable**: grease plus fire becomes burning ground (the user's choice, against the SRD) |
| 6 | Phone | Move warnings for hazards and a spread preview for targeted spells (small phone contract addition) |
| 7 | First content | River Crossing and Ford Assault; Ford Assault gains a goblin shaman who casts lightning |
| 8 | Lava | Harmless until Chapter 5's Hall of the Flame is tuned with it |
| 9 | Oil flasks | From loot (interactable chests) and future merchants, not starting kits |
| 10 | AI friendly fire | Allowed on Normal and Hard when it pays off; never on Story difficulty |
| 11 | AI hazard avoidance | Ship it on its own first, before environmental tiles (it changes AI in every encounter, so bot win rates will move) |
