# Meridian art: the manifest format

The file format every Meridian art set follows. The style rules are #331's, and the samples the owner approved are in draft PR #357. `ArtContract` in `games/Meridian/Meridian.Presentation/Art/ArtContract.cs` is the authority; a test keeps this page in step with it.

Run `tools/meridian-assets/check --art <your content/meridian/base>` before opening a PR. It lists errors and warnings, then each set's coverage. Anything it rejects is drawn as its placeholder on the TV, so a set can land partly done.

## Every set

`content/meridian/base/<set>/manifest.json`, with an object under the set's map key and one entry per id:

```json
{ "version": 1, "<map key>": { "<id>": { "file": "<name>.<hash>.png", "sha256": "…", "frameSize": [w, h] } } }
```

- **An entry** is any object with a `file`, plus its `sha256` (the TV refuses a file whose bytes don't match).
- **A strip** adds `frames` (laid out left to right, so the width is frames × frame width), `fps` and `loop` (default true).
- **9-slice pieces** add `insets: [left, top, right, bottom]`.
- **Sprites** add `anchor: [x, y]`, the feet. `anchor` and `frameSize` may sit on the id's object, shared by its entries, as #357's crew does.
- **An id with several images** (states, animations, facings) holds named entries instead of a `file`, nested as deep as it needs: `"walk": { "n": {…}, "e": {…} }`.
- **No text in art, ever.** An entry with a `text` field is rejected: names, card text and numbers are drawn live.
- **Hash-named files never change** once shipped; new art means new files.

## The sets

| Set | Map key | Ids | Frames |
|---|---|---|---|
| `tiles` | `tiles` | every tile id in `rules/tiles.json` | 64×64, lit from overhead (the engine turns tiles) |
| `hatches` | `states` | `open`, `closed`, `sealed`, `none`, all four, drawn for the north edge | 64×64 |
| `crew` | `crew` | every crew id in `rules/crew.json` | 24×24; `idle`, `walk`, `hurt`, `down`, `echo` |
| `portraits` | `portraits` | every crew id | any size |
| `cards` | `frames` | `incident`, `salvage`, `anomaly` | any size, with `insets` |
| `icons` | `icons` | every Salvage and Anomaly card id, and the three kinds' symbols | 16×16 or 24×24 |
| `tokens` | `tokens` | `brood`, `repair`, `core`, `engine`, `pods`, `prize`, `item`, with states such as a repair point's `broken` and `fixed` as named entries | 24×24 or 64×64 |
| `ui` | `ui` | `logo`, `title`, `panel`, `seatSlot`, `anomalyCounter`, `roundClock`, `logPanel`, `cardPanel`, `diceTray`, `die`, `miniMap`, `breachPlate`, `debriefPlate` | any size; panels with `insets` |
| `fx` | `fx` | `reveal`, `hatchOpen`, `hatchSeal`, `hit`, `miss`, `stagger`, `stun`, `traitLoss`, `traitGain`, `breachAlarm`, `breachDistortion`, `vent`, `sparks`, `rot`, `pressure`, `echo`, each with a `reduced` entry beside its own (no flash, no shake) | 32×32 or 64×64 strips |
| `audio` | `audio` | `music.explore`, `music.breach`, `music.endgame`, `music.debriefWon`, `music.debriefLost`, and `sfx.*`: `footstep`, `hatchOpen`, `hatchSeal`, `reveal`, `cardIncident`, `cardSalvage`, `cardAnomaly`, `diceRoll`, `diceSettle`, `hit`, `miss`, `stagger`, `death`, `echo`, `vent`, `alarm`, `tick` | Ogg Vorbis at −16 LUFS (Tactics' level) |

**Deck variants.** A tile that fits more than one deck may have one variant per deck, in that deck's accent:

```json
"storage_bay": { "variants": { "habitat": {…entry…}, "engineering": {…entry…} } }
```

A single entry may say which deck it was drawn for with `"deck"`. Each deck is checked against the tile's decks, and a deck without a variant draws the placeholder, never another deck's art.

**The UI kit.** Every piece is drawn at 2× (one art pixel is two screen pixels at 1080p), so draw corners and edges at half their size on screen. A piece with `insets` is a 9-slice: the corners stay as drawn and the edges and middle stretch, so one piece fits any panel. A piece without `insets` stretches whole. Where a piece is missing, the TV falls back to `panel` and then to its own flat panel. The plates never fall back to `panel`.

| Piece | Where it's drawn |
|---|---|
| `panel` | The figures bar across the top, and any panel without art of its own |
| `seatSlot` | Each seat's chip in the strip (the active seat is tinted in its colour on top) |
| `logPanel` | Behind the log, bottom left |
| `cardPanel` | The card panel, when the card's deck has no frame in `cards`, and the notice, vote and death panels |
| `diceTray` | The panel behind a roll or a fight |
| `breachPlate` | Behind the Breach's words, the middle three quarters of the screen |
| `debriefPlate` | The Debrief's text column |

**The die** holds named entries rather than one file: `roll`, a tumbling strip (`frames`, `fps`), and `face0`, `face1` and `face2`, a still for each value a die can land on. Its faces read 0, 0, 1, 1, 1, 2. Never put the number on the die: drawing pips is fine.

```json
"die": { "roll": { "file": "…", "frames": 4, "fps": 12, "frameSize": [24, 24] }, "face0": {…}, "face1": {…}, "face2": {…} }
```

**Effects** hold a `full` and a `reduced` entry. A bare entry counts as `full`. With the TV's reduced effects on, only `reduced` plays: no flash and no shake. Where it's missing, nothing plays, never the full strip. A strip plays once and holds its last frame, except where it loops (the stun, the Breach's alarm). Effects are drawn upright, centred on their place, at the tiles' scale (64 px art at 64 px cells). The Breach's two are stretched to the screen in whole steps.

| Effect | Plays |
|---|---|
| `reveal` | On a compartment as it drops in |
| `hatchOpen`, `hatchSeal` | On a revealed compartment's open hatches, and on a welded compartment's sealed ones |
| `hit`, `traitLoss` | On a crew member who is hit in a fight |
| `hit`, `stun` (looping) | On a monster that is stunned |
| `stagger`, `miss` | On an attacker who is staggered, and on the target of a tie |
| `breachAlarm` (looping), `breachDistortion` | Over the screen during the Breach, under its words |
| `echo` | On a crew member as they die |
| `sparks`, `pressure` | On whoever draws Sparking Conduit or Jammed Bulkhead (sparks), or Hull Groan or Gravity Flicker (pressure) |
| `traitGain`, `vent`, `rot` | Not yet: kept for the hazard scenarios and a public trait event |

```json
"hit": { "full": { "file": "…", "frames": 4, "fps": 12, "frameSize": [64, 64] }, "reduced": {…} }
```

**Strict and loose sets.** In `tiles`, `hatches`, `crew`, `portraits`, `cards` and `icons`, an id the game doesn't have is an error. In `tokens`, `ui`, `fx` and `audio`, an unknown id is only a warning: later scenarios and pieces can arrive ahead of the code.
