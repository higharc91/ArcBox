# Tactics Lore — Design Spec

- **Date:** 2026-10-31
- **Status:** Approved. Setting chosen: *Embers of the Vale* (§6); the base bible is written
- **Builds on:** [Tactics rules engine](2026-10-30-tactics-rules-engine-design.md) (on branch `ccr-472d8660-sxpjlj`, not yet merged),
  [Platform v2](2026-10-29-platform-v2-design.md) §3.6 (content delivery), [REDLINE audio](2026-10-26-redline-audio-design.md) (voice themes)

## 1. Purpose

The engine spec says "subclasses, setting, monsters beyond the SRD and all story content are original" (§4.10), and
sub-project 7 plans Claude-assisted chapter drafting. Both need one written source of truth for the world: a **lore
bible** that people and tools read, and that the TV and phones can show. This spec defines its format. The setting
itself is chosen separately (§6).

## 2. Where it lives, and why not in `rules/`

Lore is a folder in the base content pack: `content/tactics/base/lore/`.

| Choice | Reason |
|---|---|
| Inside `content/tactics/base/` | Platform v2 §3.6 hashes and serves every file in the pack folder, so the TV and phones get lore with no server change. |
| Not inside `rules/` | The rules `PackLoader` refuses any file in `rules/manifest.json` that isn't one of its kinds, and its schema pins the file pattern. Lore there would need engine changes on an unmerged branch. |
| Index named `pack.json`, not `manifest.json` | `ArtManifests.Load` reads `<set>/manifest.json` for every folder except `rules` and turns its entries into art keys. A lore `manifest.json` would put fake `lore.*` keys into the art namespace. A folder with no `manifest.json` is skipped. |
| Chapter packs mirror the rules packs | `id`, `requires`, a file list, and `"replaces": "<id>"` to change a base id. Authors learn one model. |

Engine code on `main` is untouched, and nothing under `games/Tactics` is added, so this merges cleanly with the
rules-engine branch whichever lands first.

## 3. Files

| File | Holds | Count |
|---|---|---|
| `pack.json` | `id`, `loreVersion: 1`, `requires`, `files` | one per pack |
| `world.json` | name, pitch, premise, tone, themes, **pillars** (rules no chapter may break), magic (source, cost, rules), how each rules race fits (`peoples`), glossary, **avoid** list | base only |
| `timeline.json` | eras → events, each optionally `unlock`ed | |
| `factions.json` | goals, methods, leader, `stance` toward other factions, secret | |
| `regions.json` | one per biome, tied to its chapter card (`chapters.<id>`) and rules encounter ids; places with world-map icon kinds | |
| `characters.json` | the person behind a preset: role, wants, flaw, gated secret, relationships, **voice brief** | |
| `codex/*.json` | unlockable entries the phones show between battles | any |
| `barks/<speaker>.json` | short lines per battle/camp event, with a portrait mood, delivery direction and weight | one per speaker |

Schemas: `content/tactics/base/lore/schema/*.schema.json` (draft 2020-12).

## 4. Design decisions

- **Ids are snake_case and unique per kind across packs**, the same rule as the rules pack. A character's id defaults
  to its preset id, so `aldric` is the same person in art, rules and lore.
- **Every spoken line has a stable `id`.** Text can change without the id changing. Two later features key on it:
  - translations (`lang/<code>.json`: line id → text);
  - pre-rendered voice files: `audio/<lineId>.<sha8 of voice+text>.ogg`, so a text change means a new file and the
    content hash stays honest.
- **`direction` is for performers, never shown.** It briefs a voice actor or an expressive TTS model ("dry, under his
  breath").
- **`unlock` uses the engine's own story flags** (`setFlag`, `BattleResult.FlagsSet`) or a chapter number. Spoilers
  stay hidden until the campaign earns them, with no new state.
- **Bark `mood` is a portrait mood** (`portraits/manifest.json` → `moods`), so the TV can show the matching face.
- **Bark choice is weighted and drawn from the battle RNG**, so replays and resumed battles say the same thing.
- **Voice `theme` is a `VoiceTheme` id** registered with `AddVoiceThemes`. The `description` and `sample` brief a
  voice-design tool or an audition.

## 5. Checking

`tools/tactics-lore/check.py` (standard library only):

1. Validates each file against its schema, with a small validator for the subset of the draft the schemas use. The
   schemas stay the single source of truth.
2. Merges ids across packs, with `replaces` handled the same way as `PackLoader`.
3. Resolves references: characters, factions, codex `related`, bark speakers.
4. Checks art keys (presets, chapter cards, crests, icons) using the same harvest rule as `ArtManifests.Load`.
5. Checks moods and global line-id uniqueness.
6. When a rules pack sits beside the lore, checks bestiary subjects and region encounters against its monster and
   encounter ids.

```bash
python3 tools/tactics-lore/check.py
python3 -m unittest discover -s tools/tactics-lore
```

## 6. The setting: Embers of the Vale

Chosen 2026-10-31. The campaign's working title was already *Embers of the Vale* in the Platform v2 spec and the
mockups, and the bible builds on it. It's written in `content/tactics/base/lore/`; `world.json` is the place to start.

**In one paragraph.** For three centuries the Hearthflame in Ember Keep kept the Vale warm. A century ago the Keep fell
and the flame shattered into five embers, which changed the land where they fell: the Ashen Ford (I), Mirewood (II),
the Sunscar (III), Frostpeak Pass (IV) and the heart in the Ember Keep (V). The embers are fading. Out of Emberwick, the
party finds them before the Kindled cult does, and learns that the flame was stolen from beyond the Greywall and that
every use of ember-fire burns away a memory. The campaign ends with a party vote: rekindle the flame, return it, or let
it die.

**Why it extends** (pillars in `world.json`):
- each ending sets different flags, so a second campaign starts from a different world;
- the first campaign caps at level 10, so the same characters can carry on into a second;
- the Vale's mysteries are answered by Chapter V, but the Greywall and the Hooded Stranger's identity are not;
- every faction wants something beyond the Vale;
- a prequel, *The Night the Keep Fell*, is built into the timeline.

**What it fits.** All five chapter cards and their grades, and every NPC and enemy preset in the art. The mayor is
Halden, as in the mockups. Mockups 01 and 02 now label Emberwick and the Ashen Ford Chapter I, matching the chapter
cards. Sample player characters (Kara, Aldric, Wren, Ilyra, Tamsin, Sefa) are players' characters, so they are not in
`characters.json`.

**Story flags the bible uses.** These are the names encounter triggers (`setFlag`) and the exploration and dialogue
work (sub-project 5) should set:

| Flag | Set when |
|---|---|
| `ch1_skarrow_spared` | The party makes peace with Mother Skarrow instead of killing her |
| `ch1_met_maud` | The party talks to Old Maud |
| `ch1_halden_confessed` | Mayor Halden admits he knew the ford's ember was fading |
| `ch2_court_audience` | The Glimmer Court grants the party an audience |
| `ch2_bree_journal` | Bree shows the party her journal |
| `ch2_corvin_page` | Corvin finds the page he doesn't remember writing |
| `ch3_bridge_taken` | Vorga's bridge is taken (`warlord_bridge`) |
| `ch3_maren_parley` | The party parleys with Maren Sorrel |
| `ch3_marrowby_truth` | The Buried Crypt's records name Celandine Marrowby |
| `ch4_rime_found` | The party finds Ivo Rime's tower |
| `ch4_pip_remembers` | Pip remembers the night of the Fall |
| `ch5_gate_passed` | The party gets past Ser Halvard |
| `ch5_stranger_goal` | The Hooded Stranger reveals their goal |
| `truth_cost` | The party learns what ember-fire takes (Chapter III, the Buried Crypt) |
| `truth_crossing` | The party learns the flame was stolen (Skarrow song, or the Warden Archive) |
| `truth_fall` | The party learns what Oswin did on the night of the Fall |

## 7. Follow-ups (after the rules engine merges)

1. **`say.lineId`.** An optional key on the encounter `say` action that resolves to a lore line, so trigger dialogue
   gets ids, translations and pre-rendered voice. The literal `line` stays as the fallback. This is an engine change,
   so it waits for the branch.
2. **Faction crest art.** `crests/` is one atlas sheet per class and subclass, with no per-crest keys. Faction crests
   need their own art set before `factions[].crest` can point at real art.
3. **Voice sets for player characters.** Players make their own characters, so battle barks need to be keyed by a
   voice set chosen in the creator, not by named speaker: a `voices/*.json` kind and a Voice step in the creator.
   `barks/<speaker>.json` stays for NPCs and the narrator.
4. **Dialogue, quests, background hooks and recaps.** The narrative layer (branching conversations with tagged
   options and skill checks, the quest journal, a personal thread per rules background, "Previously on…") is designed
   with sub-project 5, which owns the runtime.
5. **Port the checker into `Tactics.Logic` validation** once lore is loaded by the game, so a bad pack fails the
   same way a bad rules pack does (TV shows the reason, campaign won't start).
