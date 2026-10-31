# Tactics Lore — Design Spec

- **Date:** 2026-10-31
- **Status:** Schema approved for drafting; the setting is still to be chosen (§6)
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

```bash
python3 tools/tactics-lore/check.py
python3 -m unittest discover -s tools/tactics-lore
```

## 6. Open: the setting

The bible's content waits on choosing a setting. Constraints any choice must meet:

- the five chapter cards in order: I Ashen Ford, II Mirewood, III Sunscar, IV Frostpeak Pass, V Ember Keep (infernal);
- the six companion presets: Aldric (dwarf cleric), Ilyra (elf wizard), Kara (human fighter), Sefa (tiefling warlock),
  Tamsin (half-elf ranger), Wren (halfling rogue), plus the Hooded Stranger;
- existing enemies: goblinoids, cultists and a cult leader, necromancer, lich, Black Knight, undead, elementals, a red
  dragon;
- the `warlord_bridge` lines already written in the rules pack.

## 7. Follow-ups (after the rules engine merges)

1. **`say.lineId`.** An optional key on the encounter `say` action that resolves to a lore line, so trigger dialogue
   gets ids, translations and pre-rendered voice. The literal `line` stays as the fallback. This is an engine change,
   so it waits for the branch.
2. **Faction crest art.** `crests/` is one atlas sheet per class and subclass, with no per-crest keys. Faction crests
   need their own art set before `factions[].crest` can point at real art.
3. **Port the checker into `Tactics.Logic` validation** once lore is loaded by the game, so a bad pack fails the
   same way a bad rules pack does (TV shows the reason, campaign won't start).
