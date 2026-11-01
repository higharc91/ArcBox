# ArcBox Tactics overworld: design

- **Sub-project:** 5 of the ArcBox Tactics breakdown (exploration, dialogue, party votes and skill checks). This spec covers its first slice: a playable Chapter 1.
- **Builds on:** the [rules engine](2026-10-30-tactics-rules-engine-design.md), the [battle adapter](2026-10-31-tactics-battle-adapter-design.md), the [lore format](2026-10-31-tactics-lore-design.md) and milestone 3b, the [TV scene](../plans/2026-10-31-tactics-tv-scene.md).
- **Visual reference:** [mockup 02](../mockups/tactics/02-tv-explore-vote.png), the party walking through Emberwick with dialogue and a vote, and [mockup 03](../mockups/tactics/03-tv-skill-check.png), the skill check.

## 1. Goal

The "Choose a battle" list becomes a world the party travels through. Players walk one continuous isometric world, meet the story where it happens, and fight on the ground they're standing on.

**Success means:** a group can start a campaign and play all of Chapter 1 on the TV and their phones, walking from Emberwick to the Ford Bridge and on through every beat, and fight River Crossing on the spot with no scene change.

### Decisions

| Question | Decision |
|---|---|
| How the party travels | Walks freely, steered by the leader |
| How the world joins up | One seamless world, streamed in chunks |
| How battles start | On the spot, in battle areas built into the world |
| First slice | Chapter 1 with its story, not walking alone |
| How the world is made | Hand-painted in Godot's editor |
| Who paints | Claude drafts Ashen Ford by script, and the user refines it |
| Art reference | Scenario concept art may guide painting. It doesn't replace tile data |

### Out of scope

- Chapters 2 to 5 of the world.
- Building interiors: doors can open dialogue or a menu.
- Random encounters away from stamped battle areas.
- Loot rolling and shops (sub-project 6).
- New terrain art: this slice uses the pack's art, and gaps are listed for the user.
- Removing the battle list: it stays as a developer shortcut.

## 2. How a session plays

**Starting.** After Everybody's in, the game goes to Exploration rather than the battle list. The party stands where it was at the end of the last session, or at the region's start spawn in a new campaign.

**The leader.** One player leads each session, in rotation. Their phone has the d-pad, and the party moves as one group behind them, wearing the crown from mockup 02. If they disconnect, the next connected player leads.

**Everyone else.** Their phones show the objective, their character and the party, and they can open their sheet.

**Story beats.** A beat that's available shows a "!" marker at its spot. The leader taps to interact.
- **Dialogue** runs on the TV with portraits, read out by the narrator.
- **Votes** run on every present player's phone with a timer. The leader breaks ties, and away players don't vote.
- **Skill checks:** the party picks who attempts one. Away players' characters are eligible.

**Battles.** Reaching a battle area, or a story choice that leads to one, starts the fight there. The party forms up on the map's `party_spawn` zone, the grid fades in and enemies step out. The battle then plays exactly as it does today. Afterwards the grid fades, Results show, and walking resumes from that spot.

**Defeat.** The campaign goes on. The party wakes at the region's recovery spawn with XP and injuries as they are today, and the battle can be tried again.

**Ending a session.** The party can stop anywhere. Position, flags and completed beats are saved with the campaign. A chapter ends when its last beat is done.

## 3. How the world is made

### 3.1 Format

A region file lives at `rules/world/<region>.json` in the content pack:

- **Tiles** use the battle map format, one height byte, one material byte and one flags byte per tile, split into 32×32 chunks.
- **Stamps** place battle maps: map id, the world tile of the map's corner and a height offset. A stamp doesn't name an encounter: encounters can share a map, as `ford_assault` and `river_crossing` do, so the arc's battle beat says which one plays.
- **Beat spots** tie each beat id in `lore/arcs.json` to a world tile.
- **Spawns:** `start` and `recovery`.
- **Edges** record where the region meets its neighbours.

Ashen Ford starts at about 160×120 tiles, about one to two minutes to walk end to end, to be tuned at milestone 5e.

### 3.2 The painting workspace

An editor scene in Godot holds each region as three tile layers on a **flat grid**: material, height and flags. The pack's terrain art is sampled across several tiles for the isometric mesh, so Godot's tile tools can't paint that view directly: the painter works on the flat grid and sees the isometric result in the game. Markers on top are draggable: beat spots, battle stamps, shown as locked outlines, and spawns.

An editor button exports the scene to the region file. **After the draft is handed over, the scene is the source of truth, and the region file is always generated from it.**

### 3.3 Claude's draft

A script writes the first pass of Ashen Ford into the editor scene: terrain, the river, the road, Emberwick's buildings and the River Crossing stamp. It refuses to overwrite a scene that already exists, so painted work can't be lost.

### 3.4 Checks

The pack loader validates each region, and a failure is a content error:

- a stamp's tiles match its battle map exactly, so tuned battles can't drift;
- every spot, stamp and spawn can be reached on foot from the start spawn;
- a region's edges line up with its neighbours where both exist.

Every beat in `arcs.json` for the region's chapter must also have a spot. Lore isn't part of the rules pack, so a base pack test checks this rather than the loader.

## 4. How the game runs it

### 4.1 Exploration phase

`TacticsGame` gets an Exploration phase between battles. The server is authoritative, as in battles, and the TV draws what it's sent.

Walking is a new input stream: the leader's phone sends a direction on press and a stop on release, and the server steps the party at a fixed rate through the game's timers. Each step is checked against the region's walkability. Hills use the rules engine's climbing limits. The TV interpolates between positions and loads chunks ahead of the party, and a slow TV doesn't hold up the game.

### 4.2 Story

- **Dialogue** is already written: `lore/scenes/ch<n>.json` holds every chapter's scenes as node graphs, with voiced lines, and [the lore spec](2026-10-31-tactics-lore-design.md) defines the format. This slice adds the runtime that plays them.
- **Beats** come from `lore/arcs.json`, with `needs` and `sets` flags. Lore never sets flags itself: choices and beats do.
- **Votes** are collected by the server, with its timer and the leader's tie-break. A vote nobody answers takes the first option. If the timer ends with no leader, the first connected player decides.
- **Skill checks** use the existing dice rules. This slice adds no new rule maths.

### 4.3 Battles on the spot

The arc's battle beat names the encounter, and the stamp holding that encounter's map says where it is. The game starts the battle through the existing encounter setup, with the same rules, balance and maps. The TV keeps the world on screen and draws the battle board in place, using the map and unit drawing from milestone 3b.

The stamp's offset converts between battle tiles and world tiles, and only the TV needs it. The engine still sees an ordinary battle map.

### 4.4 Saved state

The campaign document gains the region, position, flags and completed beats, with a version bump. Existing campaigns start at the start spawn. Existing battle saves are unaffected.

### 4.5 New views

**This changes the server-to-phone contract, with the user's approval.** It adds phone screens for walking, dialogue, voting and skill checks, and TV view data for exploration. Existing battle screens don't change. Phone fixtures are regenerated and their diffs checked.

### 4.6 When things go wrong

| Case | What happens |
|---|---|
| The leader disconnects | The party stops, and the next connected player leads |
| Nobody answers a vote or dialogue | Its timer decides |
| Nobody is connected | Exploration waits, as a battle's Resuming does |
| The server restarts | The party resumes at its last saved position |
| The TV reconnects | It snaps to the current state, as milestone 3b's catch-up does |
| The TV is slow or missing | The game isn't held up; only the picture is late |

## 5. Testing

- **Game logic:** walking, beats, votes and checks run on the game harness, as battles do.
- **Content:** the region checks in §3.4 each have a failing case.
- **Export:** a round trip from a small scene to a region file gives the expected tiles.
- **End to end:** the bots gain a walking mode that plays Chapter 1 against a real server and TV.
- **By eye:** screenshots at each milestone, then a real session on phones at 5e.

## 6. Build order

This starts after milestone 3b.

| # | Milestone | Done when |
|---|---|---|
| 5a | World format, painting workspace, export, checks and the Ashen Ford draft | The user can open the draft in Godot, paint it and export it |
| 5b | Exploration phase, leader's d-pad, TV chunk loading and saved position | The user can walk the whole region on the TV from a phone |
| 5c | Fights on the spot, and waking at the recovery spawn after a defeat | River Crossing starts in the world and returns to walking |
| 5d | The scene runtime, beats, votes and skill checks | All of Chapter 1 plays |
| 5e | Tuning, and a real session on phones | The user judges that it feels right |

**The user's part:** painting after 5a, which doesn't block 5b; concept art at any time; and the playtest at 5e.

## 7. Risks

- **Seamless terrain is the largest unknown.** Milestone 5b tests it before any story is written.
- **Weekly chapters depend on painting time.** A hand-painted world is slower to extend than a generated one, so the time Ashen Ford takes to paint should be noted as the guide for later chapters.
- **Painted terrain can drift from the rules.** The export and checks in §3 keep the data the game uses in step with what was painted.
- **Battle areas fix where fights happen.** The game has no fights away from a stamp in this slice.
