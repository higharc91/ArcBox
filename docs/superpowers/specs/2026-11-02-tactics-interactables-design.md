# Wardenflame overworld: things to interact with

> **Status:** the engine core in §2 is built and tested (Tactics.Logic `World/`, Tactics.Game `TacticsGame.Interact.cs`). The TV's drawing and the
> phone's list are a second stage, after the TV-colours work lands. Nothing here changes a rule or a balance number: every place that would is
> a **USER DECISION** in §3 and §6, with the default the engine uses meanwhile. No art was made; §4 is the brief for the user's art AI.

- **Builds on:** [the overworld design](2026-10-31-tactics-overworld-design.md) (regions, stamps, beats, walking), the
  [5d story runtime](../plans/2026-10-31-tactics-overworld-5d.md) (scenes, votes, checks, Interact, the cursor), and the user's decisions in
  [the handoff §8](../plans/2026-10-31-tactics-overworld-cloud-handoff.md).
- **Doesn't overlap:** [environmental tiles](2026-11-01-tactics-environmental-tiles-draft.md) are battle-only tile reactions (lightning in water,
  fire on fuel). Things here live in the overworld, out of battle, and never enter a fight: the loader keeps them out of every stamp (§5).
- **Reference:** BG3's exploration layer: doors, chests, levers, containers, notes, loot on the ground, things found by passive Perception, traps.

## Summary

A region gets a hand-written file of **things**: `rules/interactables/<region>.json`, beside the region file the painting workspace generates.
Each thing is one of 30 **kinds** in 10 families (container, passage, switch, readable, pickup, shrine, ambient, floor trap, and since §7 notice
and hidden path). The kind is a small state machine in C#; content gives the tile, the words, the lock, the trap, the loot and the flags.

The leader uses a thing within one tile of them. Anything with nothing to decide (an unlocked chest, a lever, a pickup, a bark) happens at
once, in the world. Anything with a choice, a check or words to read is **a scene in 5d's own format**, played by the existing story runtime in
the Story phase: the leader chooses, the party picks who rolls, the roll is saved with the node it leads to, and the cursor is saved on every
line. So votes, roller picks, the TV's story view, the phones' dialogue, vote and check screens, and restart safety are all reused, not rebuilt.

State is kept per thing in the campaign (`WorldSave.Things`), and only ever moves forward, except a lever and the passages it works. Closed
doors, chests and seen traps are laid over the ground as blocking props, so walking, reach, landing, travel and the checks all see them.
Hidden things and unseen traps are never sent to the TV or phones until found.

---

## 1. The catalogue

What the player does is always the same: the leader walks up and taps **Interact**. The phone's button says the nearest thing's verb
("Open the chest"), and `targets` lists everything in reach for a picker (§2.5). Checks use the existing 5d flow: the leader picks the
approach, the **party** votes who rolls (20 s, unanswered goes to the best bonus), `d20 + bonus` with no natural 20 or 1 (5d default 7)
against the DC, and the roll shows for 4.5 s. Away characters can be picked, as in 5d.

"TV" below is what the TV is sent (`ExploreView.Things` and `ThingEvents`); how it draws is stage two. "Phone" is the leader's walk
screen, or the 5d screens during a scene.

### 1.1 Containers: chest, crate, barrel, sack, hidden cache. **Core now.**

| | |
|---|---|
| Does | Opens once. A chest, crate or barrel stands in the way; a sack and a cache don't. |
| Checks | Lock (§1.2) and trap (§1.6), if any, come first. None otherwise. |
| Outcomes | `open`: its loot goes to the leader's bag (USER DECISION 1), its `sets` flags are set, the TV shows "Inside: 2 × Potion of Healing." or the kind's empty line ("Nothing but straw."). |
| Kept | `state` closed → open, `unlocked`, `trap`, `spent`. |
| TV | The thing in its state, `opened` event. `trapped: true` once its trap is seen. |
| Phone | "Open the chest" on the walk screen; a vote screen for the lock or trap question; the check screen for the roll. |

### 1.2 Locks, on containers and passages. **Core now.**

Each way written is offered, in this order: **the key** (only when the party holds it), **pick** (Sleight of Hand, DEX), **force**
(Athletics, STR), **seal** (Arcana, INT, for a magic seal), then **Leave it**. The leader chooses (it's not a vote: 5d default 3), and a
choice nobody makes takes the first way with no roll, which is the key if held and otherwise "Leave it": a timer never spends a roll.

- A pass opens the lock ("The lock gives.") and goes straight on to opening the thing.
- A fail is **spent** for that lock: the next try offers what's left (USER DECISION 2). With nothing left and no key, the button just says
  "The door is locked, and there's nothing left to try."
- **Keys are story flags,** not items: a pickup, a container or a readable can set one, and a lock names it (`"key": "iron_key",
  "keyName": "iron key"`). The bag only holds the rules pack's consumables, and an unknown item there would fail the sheet builder.

### 1.3 Passages: door, gate, drawbridge, secret door. **Core now.**

| | |
|---|---|
| Does | Stands in the way while closed, open ground once open. A door opened stays open. |
| Checks | Lock and trap, as above. |
| Outcomes | `open`; its `sets`. A passage a lever works can't be opened by hand: "The gate won't budge. Something else works it." |
| Kept | `state` closed → open (both ways only when a lever works it). |
| TV | `blocks` follows the state; `opened` or `toggled` events. A secret door isn't sent until found. |

A **drawbridge** is a passage whose closed state is "raised": painted as a walkable deck, blocked while raised.

### 1.4 Levers and switches. **Core now; pressure plates later.**

| | |
|---|---|
| Does | Flips off ↔ on. Its `targets` (passages of the same region) are open exactly while it's on. Its `sets` flags are set when it goes on. |
| Checks | None. |
| Outcomes | Instant: `toggled` on the lever and on each passage. It won't shut a passage on the leader's tile: "Someone's standing in the way." |
| Kept | `state` off ↔ on, and its passages'. Flags never unset (flags only accrue in this engine). |

### 1.5 Readables: note, plaque, book, gravestone, signpost. **Core now.**

A scene of its `text`, one narrated line each, on the TV's dialogue panel and every phone's dialogue screen (the leader can skip). Marks it
`read` and sets its flags the first time. It can be read again. A gravestone stands in the way; the rest don't.

### 1.6 Traps. **Core now.**

On a container or passage, or on the ground (`kind: trap`).

| | |
|---|---|
| Seen | By the party's best **passive Perception** (10 + Perception bonus, SRD), whoever's here, as the leader passes within 3 tiles along x and y, if it meets `spot`. A trap on a hidden thing is seen only once the thing is found. |
| Seen, on a thing | The leader's question: **Disarm** (Sleight of Hand vs `disarm`), **Leave it**, or **Open it anyway** (springs it, then carries on to the lock). |
| Seen, on the ground | It stands in the way, so the party walks round it, and offers **Disarm** or **Leave it**. A floor trap must have a disarm DC, so a seen trap can always be cleared. |
| Unseen | Opening the thing, or stepping on the tile, springs it. The party walks on. |
| Disarm | A pass: `disarmed`. A fail springs it (D&D's "fail by 5" simplified to any fail; USER DECISION 2). |
| Sprung | Its `text` ("Click. A trap goes off, but it does no harm." by default) and `trap.sets`. **Harmless by default** (USER DECISION 3). |
| Kept | `trapSpotted`, `trap` null → disarmed or sprung, never back. |
| TV | `spotted`, `disarmed`, `sprung` events; a floor trap's art state is `armed`, `disarmed` or `sprung`. |

Thieves' tools aren't modelled: Sleight of Hand stands in for them (USER DECISION 7).

### 1.7 Hidden finds: cache, secret door, anything with `hidden`. **Core now; active Search built (§7).**

Any thing may carry `hidden: <DC>`; a cache and a secret door must. Passive Perception (as for traps) finds it as the party passes, with a
`revealed` event and "Ana spots a hidden cache." on the TV. Until then it isn't sent anywhere, and a hidden door is a wall. Since §7 a thing
may be hidden by any passive skill (`{ "dc": 14, "skill": "arcana" }`), the leader can **Search** (Investigation), and there are notices
(tracks, a draft…) and hidden paths.

### 1.8 Loot on the ground: pickup. **Core now.**

Lies there until the leader picks it up: its `loot` goes to the leader's bag, its `sets` (a key) are set, `taken`. "Found: Smoke Bomb." or
"You take the iron key." Never there twice.

### 1.9 Shrines, wells, altars. **Core now (flags); blessings later.**

Used once: a scene of its `text`, then `used` and its `sets`, which the story can read ("blessed"). Any mechanical blessing (a heal, a
boon for the next fight, an injury mended) is a rules change: USER DECISION 6.

### 1.10 Ambient objects. **Core now.**

"Look at the cart": its one line on the TV, nothing changes. It needs an existing art key (`props.cart`), since it has no art of its own.

### 1.11 Story hooks. **Later.**

A thing that starts a scene graph of the lore's: `"scene": "<scene id>"`, played as an interaction (§2.7) with the scene's own nodes, votes
and checks, its option `sets` applied as 5d does. The engine already plays interactions as 5d scenes, so the runtime part is small: look the
scene up by id instead of by beat. What it needs first is content plumbing: lore scenes today all belong to an arc beat (`scene.beat`), so a
hook scene needs `beat` optional in `scenes.schema.json`, `check.py` and `LoreBook` (with `check.py` parity tests), and a rule that a hook can't
set a choice's flags (the §5.6 check already does for things).

### 1.12 Later, noted

- **Pressure plates** (a switch worked by standing on it), **breakable walls** (Athletics, once), **bookshelf doors** (a hidden lever: hidden
  and lever-worked together is refused today).
- **Listening at doors.** (Searching is built: §7.4.)
- **Coins and gold**: no currency until sub-project 6 (`pickups.coin_pile` exists).
- **NPC barks on a thing**, using the voiced lines' clips.
- **Things in the painting workspace**: markers you drag in Godot, exported with the region (§2.1).

## 2. The engine model

### 2.1 Content

`rules/interactables/<region>.json` (schema: `rules/schema/interactables.schema.json`), a new file kind. It's hand-written beside the
generated region file, so an export never loses it, and it's **outside the battle hash**, like regions, so placing a chest never restarts a saved
fight. Several packs can each add a file for one region; ids are unique across the pack, since state is kept by id.

```json
{ "region": "ashen_ford", "interactables": [
  { "id": "ford_cellar_door", "kind": "door", "at": [41, 63], "facing": "sw", "name": "cellar door",
    "lock": { "key": "cellar_key", "keyName": "cellar key", "pick": 13, "force": 15 },
    "trap": { "spot": 12, "disarm": 13, "text": ["A needle springs from the lock."], "sets": ["needle_trap_sprung"] } },
  { "id": "ford_key", "kind": "pickup", "at": [36, 70], "name": "cellar key", "sets": ["cellar_key"] },
  { "id": "ford_chest", "kind": "chest", "at": [44, 61], "loot": [{ "item": "potion_of_healing", "count": 1 }], "requires": ["ch1_road_open"] }
] }
```

| Field | Meaning |
|---|---|
| `id`, `kind`, `at` | Required. `at` is a world tile. |
| `facing` | `ne`, `se`, `sw` (default) or `nw`. |
| `name`, `art`, `text` | Override the kind's. |
| `requires`, `unless` | Flags for it to be there (as scenes). A thing that isn't there blocks nothing and isn't sent. |
| `sets` | Flags its main outcome sets. |
| `lock` | `key`, `keyName`, `pick`, `force`, `seal`. |
| `trap` | `spot` (required), `disarm`, `text`, `sets`, `injury`, `lose`. |
| `hidden` | Passive Perception DC to find it, or `{ "dc", "skill" }` for another passive skill (§7.1). |
| `reveals` | A notice's: hidden things of its region found the moment it's noticed (§7.2). |
| `range` | 1 to 6: how far the party's passive skills find it or see its trap (default 3, §7.2). |
| `loot` | `[{ item, count }]` from `items.json`. |
| `targets`, `open` | A lever's passages; a passage that starts open, a lever that starts on. |
| `blocks` | Override the kind's (not for passages or floor traps). |

When the painting workspace gains markers for things, the export can write this same file; until then it's edited by hand, which the
README's "never edit the region file" rule doesn't forbid, because it's a different file.

### 2.2 The kinds library

`InteractableKinds.All` (C#): id, family, name, art key (`interactables.<id>`), states, whether it blocks, a stand-in art key per state
(§4.3), the default line, and whether it must be hidden. It's code, not a pack file, because each family is a state machine; content picks a kind
and overrides per thing. A pack-level `kinds` file is easy to add later if the user wants new kinds without code.

| Family | Kinds | States | Blocks |
|---|---|---|---|
| Container | chest, crate, barrel, sack, cache | closed, open | chest, crate, barrel |
| Passage | door, gate, drawbridge, secret_door | closed, open | while closed |
| Switch | lever | off, on | no |
| Readable | note, plaque, book, gravestone, signpost | unread, read | gravestone |
| Pickup | pickup | here, taken | no |
| Shrine | shrine, well, altar | ready, used | yes |
| Ambient | ambient | idle | no |
| Floor trap | trap | idle (its trap is `armed`, `disarmed`, `sprung`) | while seen and set |
| Notice (§7.2) | tracks, scratches, disturbed_earth, bloodstain, draft, carving, rune, remains | noticed | no |
| Hidden path (§7.3) | hidden_path | found | until found |

### 2.3 Saved state

`WorldSave.Things: { id → InteractableSave }`, written only once something changes, so documents from before read with none (no version
bump, as 5b and 5d did). `InteractableSave(State, Found, Unlocked, TrapSpotted, Trap, Spent, Searched)` (`Searched` since §7.4, not written
while false). A thing not in it is as it started; a state its
kind no longer has (a content change) reads as the start.

**It never goes back** (`Interactables.Regressed`): found stays found, unlocked stays unlocked, a trap stays dealt with, a failed way stays
spent, and the state only moves forward in its kind's order. Only a lever and the passages it works go both ways.

### 2.4 Walking: the overlay

`WorldGround.With(blocked)` lays a blocking prop on each tile a thing blocks now (`Interactables.Blocking`: closed passages, found or not,
standing things, seen floor traps), over the region's plain ground. The plain ground stays the region with every passage open, which is how
it's painted. The game's `WalkGround()` caches it by the blocked set, and **every** use of the ground in the world goes through it: steps,
Interact reach for beats and things, standing on a saved tile, landing after a fight, travel and the scene cast's tiles. Landing and travel
also take their reach (the flood from the start) on that ground, not the plain one, so the party is never put across a shut door; a saved
tile is still judged on the plain flood, so a party beyond a gate it shut itself isn't sent back to the road.

**Things that appear.** A thing whose `requires`/`unless` puts it there, and that would stand in the way, never appears on the leader's tile:
it waits until the leader steps off (`_appeared`), so nobody is put inside a door, and the TV never sees it land on them. After a restart the
same rule holds on the saved tile.

**Unseen walls.** A step refused by something the TV hasn't been told of (a door nobody has found) doesn't bump: `Bumps` doesn't go up and
`Blocked` stays false, so the bump can't give the door away. The party just stands.

### 2.5 Reach and choosing

A thing is in reach on its tile or the eight round it, within a climb (2) of its height (`Interactables.InReach`), so a chest up a cliff
isn't reached from its foot; diagonally only where walking could go diagonally (both corner tiles walkable), so nobody reaches round the corner
of a wall. In a region that has things, the leader's walk screen gains `targets: [{ id, label }]`, nearest first, then the one faced, then by id
(nearest first so the list doesn't reorder as the leader turns: the walk screen mustn't change under a thumb). `interact` stays the button: the
beat's word if a beat is in reach, else the first target's. The input `interact` takes an optional `{ target }`; with none it does the beat in
reach, else the first target, so today's button works unchanged. The server checks reach and the leader again.

### 2.6 What walking finds and springs

After every step, a floor trap on the new tile that nobody has seen goes off on the leader, and the party's best passive Perception finds
what's hidden within 3 tiles (`Interactables.Finds`). The same look runs as the party comes into the world (after Muster, a fight or a scene).
Either is saved at once. The party isn't halted, since the phone's next hold ping would only start it again.

### 2.7 Interacting

`InteractScenes.Plan(def, state, flags)` is pure and says what happens:

- **`InstantPlan(outcome, line)`**: applied at once in the world and saved: ambient, levers, pickups, an unlocked and untrapped container or
  passage, a lever-worked passage's bark, a lock with nothing left to try.
- **`ScenePlan(scene, outcomes)`**: a `SceneDef` in 5d's format (narrated lines, options with `SceneCheck`s, `next`/`end`), plus an
  **outcome per node**. The game enters the Story phase with a cursor whose `Thing` names the thing, its state when the interaction began and
  who began it; entering a node applies that node's outcome **in the same save as the node's first line**. The scene ends back in the world.
  A battle beat waiting at its fight (lost, fled, or one that wouldn't start) keeps its cursor through it: the interaction's cursor holds it as
  `Thing.Under` and puts it back when it ends, or when it's dropped after a content change, so the step into the stamp still goes straight to
  the fight, with no lead-in to replay. (Refusing scenes while a fight waits was the other way; it would stop a party reading a note after a
  loss, which is when it most wants to.)

Outcomes are absolute (`InteractOutcome`: the state it goes to, unlock, the trap's end, a way spent, loot, flags), so applying one twice is the
same as once, and loot and a trap's effect fire only on the step that changes the thing (`LootDue`, `SpringDue`).

**Phase:** the Story phase, reused, rather than a new Interact phase: walking can't run, and everything 5d built for a scene (votes,
tie-breaks, the leader's choice moving with the lead, presence holds, End session from a scene, Resuming) applies as it is. The changes to
`TacticsGame.Story.cs` are six small hooks: the current scene, the beat it's of, the outcome on entering a node, the end of the stage, and the
resume.

**Restart safety:** the scene is built again from the saved starting state with `again: true`, which ignores the flags the scene itself has
set since, so it's the same scene node for node. A restart mid-line replays the line; mid-roll shows the saved roll, never a new one; a node
already entered isn't entered again, so loot can't be given twice. A thing or node that's gone after a content update ends the interaction with
the story's "has changed" notice, undoing nothing.

### 2.8 Events

`ExploreView.ThingEvents`: the last 8 `ThingEventView(seq, id, what, by, line)`, with `what` one of `opened`, `unlocked`, `failed`,
`revealed`, `spotted`, `sprung`, `disarmed`, `toggled`, `taken`, `read`, `used`, `looked`, and since §7 `noticed`. `by` is the character's name. The TV animates
from these, as it does from walk legs, and takes state from `Things` when it joins late. They aren't saved; a new `Epoch` may restart the
numbers. The thing's line also shows as the TV's notice, which the next step clears.

### 2.9 What's sent

- **TV:** `ExploreView.Things`: `ThingView(id, kind, at, facing, art, state, standIn, name, blocks, trapped)` for what's there and has been
  seen, in file order; left out of the JSON for a region with none, as `ThingEvents` is, so Ashen Ford's view is byte for byte as before. During an interaction's scene, `StoryView` as for any scene (title = the thing's name).
- **Phones:** in a region that has things, the leader's walk screen gains `targets` and `search: true` (§7.4), and everyone else's an empty
  list and `search: false`. In any other region,
  Ashen Ford today, the walk screen is exactly as before: no field is added, and the phone fixtures are unchanged. In a scene, the 5d screens.
- **Never sent:** a hidden thing not found, an unseen floor trap, a thing whose flags don't hold, a thing in a state that draws nothing (a
  pickup taken, a cache emptied), and any event of a passage that isn't there (a lever still moves it, unseen). (The content pack's own files
  are on the content endpoint, as the region files are; that's the TV's cache, not the protocol.)

## 3. Effects and rules impact

Nothing below is decided. Each is what the engine supports, and the default it uses.

| # | USER DECISION | Default now | The choice |
|---|---|---|---|
| 1 | **Loot.** Found items go into a character's bag, which is a balance change (more potions in fights). Whose bag? | The leader's (whoever opened it). The base pack places no loot. | Leader; split round the party; a shared stash; or no consumables from the world until sub-project 6. |
| 2 | **Failing a check.** Can a lock or a disarm be tried again? | A failed way is spent for that lock, for good; a failed disarm springs the trap. A lock the story needs must have a sure way (§5). | Free retries (checks become a formality); a retry after leaving the region or a rest; retries that cost a consumable (thieves' tools). |
| 3 | **Traps out of battle.** Battles start at full HP, so HP damage means nothing. | **Harmless:** a line and flags, as hazards are harmless (handoff default). | An injury from the table (`trap.injury`, built; injuries never heal yet, so this is harsh); losing a consumable (`trap.lose`, built); a condition at the next fight's start; a story flag only. |
| 4 | **Passive Perception.** | The party's best, away characters included (5d default 8), within 3 tiles along x and y, no line of sight. | Only those here; the leader's alone; a shorter range; sight blocked by walls. |
| 5 | **Who rolls.** | The party picks, as 5d (20 s, best bonus if nobody picks). The leader picks the approach (30 s, "Leave it" if nobody does). | The leader rolls for speed on small checks. |
| 6 | **Shrines and wells.** | Flags only. | A blessing with a rules effect: mend one injury (an answer to "injuries never heal"), temp HP at the next fight, a reroll. |
| 7 | **Thieves' tools.** | Not modelled: Sleight of Hand. | Proficiency from the rogue's kit adding its bonus, or advantage. |
| 8 | **Keys.** | Story flags, shown by name on the button only. | A key ring on the character sheet. |
| 9 | **A lever and the party.** | It won't shut a passage on the leader's tile. Followers are only on the TV, so they can't be checked. | Also refuse when any walker could be there (the trail). |
| 10–17 | **Hidden things and passive checks** | See §7.7. | |

## 4. Art brief, for the user's art AI

### 4.1 Conventions to follow

From `content/tactics/base/props/manifest.json` and `tools/tactics-assets`: pixel art drawn at 1× and shown at 2×; each asset is
`size [w, h]` with a ground-contact `anchor [x, y]` that stands on the tile centre; animations are horizontal strips, `frames` at `fps`;
`blocks`, `cover` (`none` / `half` / `full`) and `height` in 16 px height steps; light sources as the props' `light` block. Props are drawn
once, front-facing, and the TV mirrors them for the other diagonal; there's no back view.

**A new set, `content/tactics/base/interactables/`,** whose `manifest.json` is `props/manifest.json`'s shape exactly: a `props` object of
assets, each with `variants`, `size`, `anchor`, one `frames`/`fps`, `blocks`, `cover`, `height`, `tags` and an optional `layer`, plus `notes`.
- **Each kind is an asset whose variants are its states,** one frame each: the keys are `interactables.<kind>.<state>`
  (`interactables.chest.open`), which is exactly `ThingView.Art` + `.` + `ThingView.State`, and what `ArtManifests` indexes today. A floor
  trap's states are `armed`, `disarmed`, `sprung`. Files: `<kind>_<state>.png`.
- **Each transition is an asset of its own,** `<kind>_<transition>` (`interactables.chest_opening`), with one variant, `strip`, and its own
  `frames` and `fps`, since props have one per asset. It plays once, from its kind's first state to its last (tagged `from:` and `to:`),
  and then the state holds (as `down` holds its last frame).

The JSON file below is that manifest: copy it to `content/tactics/base/interactables/manifest.json` and draw the files it names. A test copies it
into a set folder and loads it through `ArtManifests`, and checks it has every state of every kind.

Facing: draw everything facing **sw** (towards the bottom left of the screen). `se` mirrors it. `ne` and `nw` mirror too (no backs, as props).
A passage's facing says which way its wall runs: `sw`/`ne` along x, `se`/`nw` along y, so one sprite and its mirror cover both walls.

### 4.2 The keys

Sizes are proposals matched to the nearest existing prop; keep the anchor at the bottom centre of the footprint.

| Key | States (1 frame each) | Transitions (played once) | Size, anchor | Notes |
|---|---|---|---|---|
| `interactables.chest` | closed, open | opening 4 @ 10 fps | 26×26, [13, 25] | As `props.chest`/`chest_open`. A trapped glint is the TV's, not the art's. |
| `interactables.crate` | closed, open | opening 4 @ 10 | 26×28, [13, 27] | Lid off and leaning. |
| `interactables.barrel` | closed, open | opening 3 @ 10 | 18×26, [9, 25] | Lid off. |
| `interactables.sack` | closed, open | opening 3 @ 10 | 16×16, [8, 15] | Open = slumped and tied loose. Ground layer. |
| `interactables.cache` | closed, open | opening 4 @ 10 | 24×16, [12, 15] | A loose flagstone or a hollow; ground layer. Shown only once found; `open` is never drawn. |
| `interactables.door` | closed, open | opening 4 @ 10 | 32×56, [16, 48] | Wooden door in a one-tile frame, height 3, cover full when closed. Open = swung aside, the gap clear. |
| `interactables.gate` | closed, open | opening 6 @ 10 | 40×56, [20, 44] | Iron bars, matching `props.fence_iron`'s footprint; open = raised or swung. |
| `interactables.drawbridge` | closed, open | opening 8 @ 8 | 64×48, [32, 40] | Closed = raised (upright), open = lying flat across the tile. |
| `interactables.secret_door` | closed, open | opening 6 @ 8 | 32×56, [16, 48] | Matches the dressed-stone wall it hides in; open = a dark gap. |
| `interactables.lever` | off, on | pulling 3 @ 12 | 16×24, [8, 23] | A wall or post lever; on = thrown down. |
| `interactables.note` | unread, read | | 16×12, [8, 11] | Ground layer; read = no change needed, may reuse unread. |
| `interactables.plaque` | unread, read | | 20×20, [10, 19] | On a short post. |
| `interactables.book` | unread, read | | 24×30, [12, 29] | A book on a lectern. |
| `interactables.gravestone` | unread, read | | 18×24, [9, 23] | As `props.gravestone`; one sprite is enough. |
| `interactables.signpost` | unread, read | | 24×34, [12, 33] | As `props.signpost`. |
| `interactables.pickup` | here, taken | | 24×24, [12, 23] | A generic bundle. `taken` is never drawn (the TV isn't sent it): an empty frame is fine. Compose with the item icon as `pickups/` does. |
| `interactables.shrine` | ready, used | using 6 @ 8 | 36×52, [18, 40] | As `props.shrine`; used = candles out, light off. |
| `interactables.well` | ready, used | using 4 @ 8 | 34×40, [17, 39] | As `props.well`; the bucket raised when used. |
| `interactables.altar` | ready, used | using 6 @ 8 | 32×28, [16, 27] | As `props.altar`. |
| `interactables.trap` | armed, disarmed, sprung | springing 4 @ 12 | 32×16, [16, 15] | Ground layer: a plate or a pit cover, seen; `sprung` = jaws shut or a shallow pit. |
| `interactables.ambient` | | | | No art: an ambient thing names an existing prop. |
| `interactables.tracks` | noticed | | 32×16, [16, 15] | §7.6. Ground layer, as every notice. |
| `interactables.scratches` | noticed | | 24×16, [12, 15] | |
| `interactables.disturbed_earth` | noticed | | 32×16, [16, 15] | |
| `interactables.bloodstain` | noticed | | 28×14, [14, 13] | |
| `interactables.draft` | noticed | | 24×24, [12, 23] | Wisps at half opacity. |
| `interactables.carving` | noticed | | 24×16, [12, 15] | |
| `interactables.rune` | noticed | | 24×16, [12, 15] | |
| `interactables.remains` | noticed | | 32×20, [16, 19] | |
| `interactables.hidden_path` | found | | 32×16, [16, 15] | A trodden way through what hid it. |

The manifest is `docs/superpowers/specs/2026-11-02-tactics-interactables-art.json`: 29 kinds and 14 transition assets, 50 state frames and
65 transition frames, 115 frames in all. Its `notes.prompts` has a prompt for each asset §7 added, and for the `fx.notice` effect.

### 4.3 Meanwhile

`ThingView.StandIn` is an art key the pack has now, which the TV can draw until the set exists; null means a plain placeholder marker (stage
two: a gold diamond outline on the tile with the thing's name, which the TV-colours work should style). A stand-in is only ever named for the
very state it's drawn in, never borrowed from another state: an open crate is drawn as `props.crate` because the kind says so for `open`, and
a taken pickup or an emptied cache isn't sent at all.

| Kind | Stand-in now |
|---|---|
| chest | `props.chest`, open: `props.chest_open` |
| crate, barrel, sack | `props.crate`, `props.barrel`, `props.sack`, open as well (there's no open art yet) |
| cache, pickup | `pickups.loot_sack` while closed or there; not sent once open or taken |
| gravestone, signpost | `props.gravestone`, `props.signpost` |
| shrine, well, altar | `props.shrine`, `props.well`, `props.altar` |
| door, gate, drawbridge, secret door, lever, note, plaque, book, trap | none: the plain marker |
| ambient | its own `art` |
| remains | `props.ribcage` |
| tracks, scratches, disturbed earth, bloodstain, draft, carving, rune | none: the plain marker in clue blue (§7.6) |
| hidden path | none: the plain marker in trail green, once found (§7.6) |

A test checks every stand-in key is in the pack's art manifests.

## 5. Validation

At load (the loader, `Validation/Interactables.cs`), each with a failing test in `InteractableCheckTests`:

1. **Shape:** a known kind; a facing of the four; DCs 1 to 30; a lock with at least one way; locks and traps only where the kind allows;
   readables and shrines have words; an ambient thing names art; caches and secret doors are hidden; pickups give something (loot or a flag);
   loot only on containers and pickups, with counts 1 to 99 and known items; known injuries; no unknown fields.
2. **Ids** unique across the pack; a file's region must be loaded.
3. **Where it stands:** inside the region and not on void; on walkable ground (a passage is open ground while open); one thing a tile; not on a
   spawn or a beat's spot; **not inside any battle stamp**.
4. **Levers** work passages of the same region that start as the lever does; a passage has at most one lever, and no lock or `hidden` as well.
5. **Every thing is reachable** from the start with every passage open (things that stand in the way count as walls), and nothing that stands
   in the way walls off a spawn, a beat's spot or a stamp's party spawn.
6. **A lock always has a solution:** from the start, in the worst state the region can be in (every passage that starts shut, and every one a
   lever works, whichever way it was left), open only what's sure (unlocked passages, keys the party is sure to get, levers it can reach)
   until nothing more opens. A recovery spawn, beat spot or stamp party spawn that's only behind a lock that can be failed for good, or a
   hidden door, is an error naming the passages: give it a key or another way round. A key is sure only from a thing that's always there (no
   `requires` and no `unless`), in plain sight, and not behind a lock that can be failed.
6b. **The party can always walk back from where it's put:** the recovery spawn (after a loss), the landing by each stamp (after a fight) and
   each beat's spot (travel to a scene) are each flooded the same worst-case way, from there; each must reach the start. So a party that wakes
   beyond a gate whose lever is on the far side, or behind a key door before finding the key, is refused at load.
7. **A key is obtainable before its lock:** a key a thing of the region gives must come from a thing that's sure to give it in that closure (not
   behind its own door, not hidden, not in a container whose lock can be failed).
8. **Flags against the story** (`InteractableLore`, a base pack test, since the loader can't read lore): every `requires`, `unless` and key flag
   is set by the arc (beats, choices, scene nodes and options, encounter triggers) or by a thing; and no thing sets a flag a choice sets.
8b. **Hidden things (§7.5):** `hidden.skill` one of the eight passive skills; `range` 1 to 6 and only on something hidden or trapped; notices
   have words and are hidden, and never `blocks: true`; only a notice `reveals`, never itself, and only hidden things of its own region; a
   hidden path is hidden, gives its `facing`, has no `requires`/`unless`, no lock, and no `blocks`. Rule 6 counts a hidden path as shut, as a hidden door.
9. **check.py parity:** nothing here touches lore files, so no `check.py` change is needed now. Story hooks (§1.11) will need one, with parity
   tests, because hook scenes have no beat.

**Not checked yet:** `StoryOrder` (the export's replay of the story, branch by branch) walks the plain ground, so it doesn't know a beat is
behind a door until the door is open. Rule 6 already refuses a beat behind a lock that can be failed, so this only matters for order (a door
whose key comes from a later beat). When a region first has a door, `StoryOrder` should walk the sure closure of rule 6 at each step.

## 6. Open questions for the user

1. Everything in §3.
2. **Where things are edited:** hand-written files now. Do you want markers in the painting workspace (drag a chest in Godot, set its lock in
   the Inspector), which the export writes into this same file?
3. **Ashen Ford's first things** (2026-11-02, stand-in art): 16 in `interactables/ashen_ford.json`: a council notice by the notice board,
   the square's well and a hearthstone by the chapel, Coll's and Annet's graves, a signpost at the east fork (the well and signpost replace
   painted props), a crate, sack and barrel at the granary, a market crate, the reeve's key in Southfield and his locked chest (a Potion of
   Healing), a trapped crate (harmless) and a barrel on the piers, an Ash Market tally and a hidden cache (passive Perception 12, a Smoke
   Bomb) by the docks. No doors: a building's doorway opens onto its own blocked footprint, so a door thing would stand in the street.
   Every step's explore view now carries them, about 190 bytes each (2.9 KB), past the one-packet budget the walking tests keep for the
   walk's own part: sending things only when they change, or only near the party, would win it back.
4. **The phone's list:** a second button row under Interact, or a picker that opens from it? And should the walk screen show the keys the
   party holds?
5. **Sound:** an `sfx` per event (chest creak, lock click, trap snap). The pack has 150 sounds; which to reuse is a listening job.
6. **Story hooks** (§1.11): worth the lore schema change now, or after Chapter 1 is tuned?
7. **Mobs and fog of war** (your later ideas) would both read things: fog would hide them until explored, and roaming mobs could guard a chest.
   Nothing here blocks either.

---

## 7. Hidden things and passive checks

> **Status:** built and tested (2026-11-02): Tactics.Logic (`HiddenDef`, the notice and path kinds, `Interactables.Finds`/`Revealing`/
> `Searchable`, `InteractScenes.SearchScene`, the loader's checks), Tactics.Game (`TacticsGame.Interact.cs`: passives by skill, notices,
> the Search), the TV (`ThingShow`, `ThingsDemo`) and the phone (`walk.tsx`, `story.tsx`). No art: stand-ins and the art brief's prompts
> (§7.6). Every default below is a **USER DECISION** in §7.7.

The user asked for "hidden things and passive checks (again no art yet, just place holders and prompts for the ai model for art)".

### 7.1 Hidden by any passive skill

`hidden` stays a number, the Perception DC, so every file written before reads as it did. It may also be `{ "dc": 14, "skill": "arcana" }`,
with the skill one of **perception, investigation, insight, survival, nature, arcana, history, religion** (`HiddenDef.Skills`). The passive
score is 10 + the skill's bonus (SRD), the party's best in that skill, away characters included, within the thing's range along x and y, as
passive Perception was (USER DECISION 4). Whoever has that best score is the finder: "Bo notices: …" or "Bo senses a hidden door." (Arcana
and Religion "sense"; the rest "spot"). Traps are still seen by Perception alone.

### 7.2 Notices

A new family of things that are only ever found: **tracks, scratches, disturbed earth, a bloodstain, a draft (a breath of air from a wall),
a carving, a rune, remains**. A notice must be hidden and must have `text`, what is noticed ("Fresh wolf tracks lead north, into the
trees."). It never stands in the way and has one state, `noticed`, in which it's drawn once found.

- **Noticed:** a `noticed` event, "Ana notices: Fresh wolf tracks lead north, into the trees." on the TV's notice, its `sets` flags, and its
  optional `reveals: [ids]`: those hidden things of its region are found at the same moment, wherever they are and whatever their DC (the
  tracks lead there), each with its own event (`revealed`, or `noticed` for a notice, whose own `reveals` follow in turn, each thing once).
  Everything one look finds goes on the notice together, the notice's line first.
- A reveal target that isn't there yet (its `requires`) or is under the leader's feet stays hidden, and is found by the notice as soon as
  it is there or the leader steps off (every look checks the notices already noticed). Who noticed the notice isn't kept, so that later
  find names nobody: "The hidden cache is found." What a notice leads to at once is credited to its finder, and always "spotted", never
  "sensed" by the target's own skill.
- Nothing is found that isn't there **with the flags as they are by then**: a notice's `sets`, set as it's noticed, can take away a thing
  found in the same look (its `unless`), and that thing isn't found, evented or sent.
- **Look:** the leader's button "Look at the tracks" ("Feel the draft") says the line again (`looked`) and changes nothing.
- **Range:** any hidden or trapped thing may set `range`, 1 to 6 tiles (default 3), how far away the party's passive skills find it or see its
  trap: tracks across a field (6), a hairline crack you must be beside (1).

### 7.3 Hidden paths

`hidden_path`, a family of its own: a tile that **stands in the way until it's found**, then is ground the party walks, drawn as a path
(a ford through reeds, a gap in a hedge, a goat track). It must be hidden, can't be gated by flags (one that came and went would open and wall
the ground under the party), takes no lock and no `blocks`; its `sets` are set when it's found. The region paints the tile walkable, as a
passage is painted open, and it must give its `facing`: which way what it hides in runs. Until it's found:

- a step onto it is refused **without a bump** (the unseen-wall rule, §2.4), so nothing gives it away;
- the TV isn't sent it, but a **plug** for its tile (`ExploreView.Plugs`, as a secret door's): the TV draws the tile as its neighbours along
  its facing's run (`sw`/`ne` along x, `se`/`nw` along y), the hedge or the deep water it hides in, until it pops in found;
- **validation** treats it as shut (rule 6): what the story needs (the recovery spawn, a beat's spot, a stamp's party spawn, a key) can't lie
  only beyond one, and the error names it, as for a hidden door.

Found, the plug goes, it's walked like any ground, and it has no button.

### 7.4 Search

The leader's walk screen, in a region with things, has a **Search** button (`search: true`; `search` input). It's there whether or not
anything is hidden nearby, so it gives nothing away. It runs a **5d check scene** (the check-scene route, not the instant roll): one line
("The party searches the ground nearby."), then straight to the party's pick of who rolls **Investigation** (the leader isn't asked to choose
again: the option is decided as the scene starts, saved as a decided check), the roll saved with the node it leads to, then "Something turns
up." or "Nothing turns up."

- **What it looks for** is fixed as it begins and kept in the cursor (`ThingCursor.Search`): every hidden thing within **2 tiles** along x and
  y, not found, not already searched for, not under the leader. A restart plays the same scene and judges the same things.
- **The roll** is compared with each thing's own `hidden` DC (whatever skill hides it), **as it was when the search began**
  (`ThingCursor.SearchDcs`, the same DCs the check routes by, so "Something turns up." always finds something even across a content
  update; if all it would have found was removed from the pack meanwhile, the notice says "It was nothing after all."): each it meets is found ("Bo finds a loose
  flagstone.", a notice noticed and its reveals with it); each it misses is **searched** (`InteractableSave.Searched`) and is never searched
  for again (as a failed lock is spent, USER DECISION 2), though the party's passive skills may still find it later.
- **No DC is shown**, on the TV or a phone, before or after the roll (`StoryCheckView.Dc` and the phone's `dc` are null, the TV's roll has
  `Vs` 0): "DC hidden", then "11 + 5 = 16 · Found something" or "Nothing found". With nothing to find the scene plays exactly the same and
  fails. (The check routes by the lowest DC it looks for, or 99 with none, so it passes exactly when it finds something.)
- It costs nothing else: there's no clock in the world yet.

### 7.5 What's sent, and what isn't

Nothing hidden or unnoticed reaches the TV or a phone (§2.9): not a hidden thing, not a notice, not a reveal target, not an unfound path
(only its plug, which is drawn as its surroundings), not what a search looked for or missed, and not the DC. The walk screen's `search` is
added only in regions with things, so Ashen Ford's phone fixtures (and every region without things) are byte for byte as before; the vault's
walk fixtures gained `search`, and `check-search`/`check-search-rolled` were added.

### 7.6 Stand-ins, art and sound

- **TV meanwhile:** a notice is the plain marker in **clue blue** (`ThingShow.Clue`, `#7fc8e8`), flat on the ground with its name; remains use
  `props.ribcage`. A found path is the marker in **trail green** (`ThingShow.Trail`, `#9ad46a`). Noticing pops it in with a **"?"** emote (a
  find has "!?") and the effect `fx.notice`, which the pack doesn't have yet: the find's `fx.poof` plays meanwhile.
- **Art keys** (`interactables.<kind>.<state>`): `tracks`, `scratches`, `disturbed_earth`, `bloodstain`, `draft`, `carving`, `rune`,
  `remains` in `noticed`, and `hidden_path` in `found`, all ground layer, one frame, no transition; with a prompt each in the art JSON's
  `notes.prompts` (§4.2), plus `fx_notice` for the glint. Drop them in and the TV draws them by key.
- **Sound:** `noticed` wants `notice` (a quieter cue, not made) and plays `secret_found` meanwhile. No audio was generated.

### 7.7 USER DECISIONs (defaults the engine uses)

| # | USER DECISION | Default now | The choice |
|---|---|---|---|
| 10 | **Passive skills.** Which skills can hide things. | Perception, Investigation, Insight, Survival, Nature, Arcana, History, Religion; the party's best, as Perception (4). | Fewer (Perception and Investigation only); the finder's own range; a passive only for those here. |
| 11 | **Notices and reveals.** | A notice reveals its targets wherever they are, whatever their DC; a target that isn't there yet is revealed when it appears. | Reveal only within range; reveal by a second check. |
| 12 | **Range.** | 3 tiles, or the thing's own 1–6. | One range for everything. |
| 13 | **Hidden paths.** | Block until found; drawn as their surroundings meanwhile; can't be flag-gated; can't carry what the story needs. | Allow flags; a path that's always visible but "unsure" (a check to cross). |
| 14 | **Search route.** | A 5d check scene: the party picks who rolls Investigation (20 s, best bonus if nobody picks). | An instant roll by the best Investigation, with no scene. |
| 15 | **Search range and retries.** | 2 tiles; a thing a search missed can't be searched for again (passives still can). | Free retries; a retry after a rest; a larger range. |
| 16 | **Search cost.** | Nothing (no time model). | Time passing (once there's a clock), a wandering-monster roll, a limit per region. |
| 17 | **Hidden DCs.** | No DC is shown for a search, ever. | Show "DC ?" with the roll's verdict per thing. |

### 7.8 Not done

- **StoryOrder** (the export's replay) walks hidden paths as open, as it does doors (§5 "Not checked yet").
- **The plug of a low wall** (a hedge one step high) doesn't quite hide the gap on the TV: the flat tile under it still shows as a lighter
  notch without the hedge's front face (the shots of `--world-demo=things`, `path-0-unfound`). A secret door's plug in a tall wall shows a
  faint seam too. It's the plug renderer's (`WallPlugs`): building the plug as a world block (grading, decals) didn't change it, so the cause
  is deeper (likely how the plug's block is ordered against the world's own tile); left for a TV pass.
- **A search ignores unseen traps** on what it finds (the coordinator's review note 5), for the user to decide.
- **Ashen Ford** has no hidden things yet; the demo region shows each (`--world-demo=things`: a fourth gallery of the notices and the path,
  then tracks that lead to a cache, a rune noticed by Arcana, the other notices in a row, a goat track through a hedge found by Survival, and
  a Search finding a loose flagstone hidden by Investigation).

---

## How this fits the world-dressing plan

The user's local work has a world-dressing design and plan on branch `world-dressing-design`
(`docs/superpowers/specs/2026-11-01-tactics-world-dressing-design.md`, `docs/superpowers/plans/2026-11-01-tactics-world-dressing.md`).
The user chose to keep both pieces of work, so read this before changing `WorldGround` or a region's files.

- **Two layers, two files.** World dressing paints *props* into the region file the painting workspace exports
  (`rules/world/<region>.json`, `props`). Things to interact with live in a separate hand-written file,
  `rules/interactables/<region>.json`. Neither file edits the other.
- **One source of blocking.** Both block walking through `WorldGround`: painted props from the region and the props manifest, and things laid
  over the ground with `WorldGround.With` (closed doors, chests, seen traps). Every walking consumer (steps, reach, `CanStandOn`, landing,
  travel, the scene cast, region checks, `StoryOrder` once it learns about things) must see both.
- **No prop on a thing's tile.** The loader should refuse a painted prop on any interactable's tile, the same way it refuses props on battle
  stamps, and the export should warn about a blocking prop next to a door or lever that would make it unreachable. Add this check when world
  dressing lands (it can't be added here first, because `props` doesn't exist on this branch yet).
- **Overlap with world dressing's sub-project 6.** "Inventory with looting and Perception" is partly built here: containers, pickups, loot into
  the leader's bag and passive Perception exist now. Sub-project 6 should extend this engine (a real inventory, shared loot, active Search,
  gathering nodes) rather than build a second one. World dressing's "out of scope: containers, pickups" stays true for its own milestone.
- **Art.** Painted props use the pack's prop art; things use `interactables.*` keys (§4 and the art JSON), falling back to existing props
  meanwhile. The user's art AI works from both lists.
