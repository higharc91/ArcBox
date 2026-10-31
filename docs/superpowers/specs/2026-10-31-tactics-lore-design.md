# Tactics Lore — Design Spec

- **Date:** 2026-10-31
- **Status:** Approved and complete for the first campaign. Setting: *Embers of the Vale* (§6). Engine integration: §7
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
| `backgrounds.json` | each rules background's personal thread: a private **hook** for the player, the thread and payoff for writers, ties, and a secret only that player learns | |
| `arcs.json` | the campaign's spine: one arc per chapter as **beats** (story, choice, battle, discovery, camp) with places, rules encounters, the flags they need and set, and **choices** the party votes on; plus the **endings** and the sequel each leads to | base, or a chapter pack's own arc |
| `voices/<id>.json` | a **player voice set** chosen in the creator, with its casting brief and barks per event | one per voice |
| `barks/<speaker>.json` | barks for a named NPC or the narrator, same line format as voice sets | one per speaker |

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
6. When a rules pack sits beside the lore, checks bestiary subjects, region and beat encounters, and background ids
   against its monster, encounter and background ids.
7. Once the loaded packs have arcs, **every flag the lore waits on** (an `unlock.flag` or a beat's `requires`) must be
   set by a beat, a choice, an ending or a rules encounter's `setFlag`. A misspelt flag would otherwise leave a secret
   locked for ever.
8. Checks each arc's chapter against its region's, and beat places against the regions' places.

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

**What's written** (`content/tactics/base/lore/`, about 15,000 words of JSON): the world, a 16-event timeline, 9 factions,
5 regions with 20 places, 19 named NPCs on existing presets with voice briefs, 29 codex entries, a personal thread
for each of the 12 rules backgrounds, 5 chapter arcs of 5–7 beats with 7 party votes, 3 endings, and 6 player voice
sets (Steady, Fierce, Wry, Earnest, Scholarly, Grim) with 102 barks.

**Story flags.** `arcs.json` is the authority, and the checker keeps everything else in step with it. Engine and
dialogue code should set exactly these names:

| Flag | Set by (beat → choice) |
|---|---|
| `ch1_skarrow_slain` / `ch1_skarrow_spared` | `ch1_the_ford_choice` → `ch1_force` / `ch1_parley` |
| `ch1_met_maud` | `ch1_old_maud` |
| `ch1_halden_confessed` | `ch1_granary` (needs `ch1_skarrow_spared`) |
| `ch1_ember_found` | `ch1_the_ember` |
| `ch2_kindled_cell_broken` | `ch2_drowned_waystation` (`forest_ruins`) |
| `ch2_court_audience` | `ch2_court_audience` |
| `ch2_paid_memory` / `ch2_court_tricked` / `ch2_ember_left`; `ch2_ember_taken` | `ch2_the_bargain` → `ch2_pay` / `ch2_trick` / `ch2_leave`; pay and trick also set `ch2_ember_taken` |
| `ch2_bree_journal` | `ch2_bree_journal` |
| `ch2_corvin_page` | `ch2_corvin_page` |
| `truth_crossing` | `ch3_skarrow_messenger` (needs `ch1_skarrow_spared`), or `ch5_the_archive` |
| `ch3_bridge_taken` | `ch3_the_bridge` (`warlord_bridge`; its own trigger sets `bridge_down` mid-battle) |
| `ch3_vorga_spared` / `ch3_vorga_slain` | `ch3_vorgas_fate` → `ch3_spare_vorga` / `ch3_kill_vorga` |
| `truth_cost`, `ch3_marrowby_truth` | `ch3_buried_crypt` (`crypt_hall`) |
| `ch3_maren_parley` | `ch3_maren_parley` |
| `ch4_rime_found` | `ch4_crystal_caves` (`crystal_cave`) |
| `ch4_ivo_helped` / `ch4_ivo_stopped` | `ch4_ivos_choice` → `ch4_help_ivo` / `ch4_stop_ivo` |
| `ch4_pip_remembers`, `truth_fall` | `ch4_pip_remembers` |
| `ch5_gate_passed` + `ch5_halvard_slain` / `ch5_halvard_released` | `ch5_the_gate` → `ch5_fight_halvard` / `ch5_release_halvard` (release only makes sense with `truth_fall`) |
| `ch5_stranger_goal` | `ch5_the_stranger` |
| `ch5_ritual_broken` | `ch5_the_ritual` (encounter still to be written) |
| `ending_rekindled` / `ending_returned` / `ending_extinguished` | `ch5_the_last_vote` → `ch5_choose_rekindle` / `ch5_choose_return` / `ch5_choose_extinguish` |

## 7. Engine integration contract

For whoever builds the game side (rules engine, sub-project 5 exploration and dialogue, TV and phone UI). Lore is data
only; nothing here needs an engine change to merge, and the engine can adopt it a piece at a time.

**Loading.**
- Read `content/tactics/base/lore/pack.json`, then each file it lists, in order. Chapter packs ship their own
  `lore/pack.json` with `requires` and `replaces`, merged the same way the rules packs are (§2).
- The files arrive with the content pack already (Platform v2 §3.6); no server change.
- Until a C# loader exists, `tools/tactics-lore/check.py` is the validator. When the engine loads lore, port its
  checks into `Tactics.Logic` validation so a bad pack fails like a bad rules pack (follow-up 4).

**Flags.**
- Story flags are the engine's existing ones: encounter `setFlag`, `BattleResult.FlagsSet`, and whatever
  sub-project 5 adds for exploration and dialogue. Lore never sets flags; it only reads them.
- When a beat or choice in `arcs.json` happens, set its `sets` flags. The table in §6 lists them all.

**Unlocks.**
- An entry with `unlock` is visible once every key holds: `flag` is set, and the party has reached `chapter`.
- An entry without `unlock` is always visible.
- Secrets (`characters[].secret`, `backgrounds[].secret`) are never shown before their unlock. Background secrets go
  **only to the phone of the player whose character has that background**; that player chooses whether to share it.

**Arcs.**
- `arcs.json` is the outline sub-project 5 builds scenes from, not a script to execute line by line.
- A beat with `requires` only happens when those flags are set.
- `kind: choice` is a party vote: each option's `label` is the vote text, the leader breaks ties (mockup 02), and the
  chosen option's `sets` flags are set.
- `encounter` names the rules encounter to start.

**Barks.**
- A player character's barks come from the voice set its player picked in the creator (`voices/<id>.json`). Store the
  voice id on the character; it needs a Voice step in the creator.
- NPCs and the narrator use `barks/<speaker>.json`.
- Map engine events to bark events: `battle_start`, `turn_start`, `hit`, `miss`, `crit`, `kill`, `ally_down`
  (an ally is Downed), `low_hp`, `healed`, `revived`, `level_up`, `victory`, `defeat`, `camp`, `town`. An event
  with no lines says nothing.
- Pick by `weight` (default 1) from the battle RNG, so replays and resumed battles are identical.
- Show the speaker's portrait in the line's `mood`. Speak the line through `/api/tts` with the voice's `theme`, or
  play a pre-rendered file keyed by line id (§4).
- Rate-limit barks in the UI (at most one every few seconds, never over a `say` line) so battles don't get chatty.

**Text.** Line ids are stable; translation and pre-rendered audio key on them (§4). Everything else is shown as written.

## 8. Follow-ups

1. **`say.lineId`.** An optional key on the encounter `say` action that resolves to a lore line, so trigger dialogue
   gets ids, translations and pre-rendered voice. The literal `line` stays as the fallback. An engine change.
2. **Faction crest art.** `crests/` is one atlas sheet per class and subclass, with no per-crest keys. Faction crests
   need their own art set before `factions[].crest` can point at real art.
3. **Sub-project 5's dialogue format.** Branching conversations (nodes, tagged options for background, race and
   class, skill checks where the party picks who rolls), the quest journal, and the "Previously on…" recap are scene
   scripts built on `arcs.json`. They belong to sub-project 5, which owns the runtime; they get their own lore file
   kinds then.
4. **Port the checker into `Tactics.Logic` validation** once the game loads lore.
5. **A Chapter V encounter** for `ch5_the_ritual` (the Hall of the Flame with the red dragon), and encounters for the
   remaining story battles as chapters are built.
