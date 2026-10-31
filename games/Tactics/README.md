# ArcBox Tactics

ArcBox Tactics is a turn-based tactical battle game for the ArcBox party platform. `Tactics.Logic` is its rules engine: a deterministic, pure C# library (no clock, no `System.Random`, sorted collections only) that takes a battle state and a command and returns the new state and the events that happened, so every battle can be replayed from its seed. `Tactics.Logic.Tests` covers it, and `Tactics.Sim` balance-tests the content: it plays every encounter with sampled AI parties, AI on both sides, and writes a Markdown report (the latest is in `docs/superpowers/reports/`):

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --out <report.md>
```

The design is in [docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md](../../docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md). Third-party notices are in [THIRD_PARTY.md](THIRD_PARTY.md).

## Running the simulator

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --difficulty Normal --out <report.md>
```

That full sweep is 9,000 battles and takes about 5 minutes. While iterating, use `--seeds 6` (about 1.5 minutes), or narrow it with `--encounters a,b` and `--levels`. `--help` lists every option. The rules are read at run time, so a content edit needs no rebuild.

Six seeds are noisy, so judge the final numbers at 20. The report flags:
- win rates outside 60–95%, and mean battle lengths outside 8–12 rounds, at each encounter's own level;
- role-band values more than ±15% from the band mean;
- abilities used in under 2% of the turns where they're legal.

Simulated parties have no subclass. The latest numbers and every tuning decision are in [the final balance report](../../docs/superpowers/reports/2026-10-30-tactics-balance.md).

If a content change alters a golden replay, re-record with `UPDATE_GOLDENS=1 dotnet test games/Tactics/Tactics.Logic.Tests`, review each diff, then run again without the flag. The recording run fails `Warlord_bridge_plays_the_showcase_triggers` if it runs before the replay is written; the second run must pass. To re-record `warlord-bridge` from the AI, empty its `commands` in `Golden/warlord-bridge.replay.json`.

## Adding a chapter pack

A pack is a folder with a `manifest.json`, and each weekly chapter is its own pack (`chapter-03`, and so on). `base` is always loaded.

1. Create the folder and its `manifest.json`: `id`, `rulesVersion`, `requires` (the pack ids it needs) and `files`, the list of its content files.
2. Add content in the same formats as `content/tactics/base/rules/`, with the schemas in `schema/`. Bring maps, monsters and encounters for the chapter.
3. **A chapter may add ids, but never redefine one by accident.** An id defined twice without `"replaces": "<id>"` is a load error that names both files and the JSON pointer, so balance changes stay deliberate.
4. Set an optional intended `level` on each encounter, so the simulator judges it at that level, and aim for no more than 10 enemies: that's the AI's tested worst case.
5. Run the simulator over the new encounters at their levels.

A pack with errors doesn't load: errors carry the file and a JSON pointer. The content hash covers every loaded pack and is stored in each saved battle, so changing a pack mid-campaign is detected.

## Content conventions

These turn SRD 5.2 into this game, and every content file starts from them. **The balance pass moved some numbers away from them on purpose**; the [balance report](../../docs/superpowers/reports/2026-10-30-tactics-balance.md) lists each change and its reason, and the differences are noted below.

- **Spell tier** = SRD spell level. **Mana cost = tier**; cantrips cost 0.
- **Max tier by level:** full casters (wizard, cleric, bard, druid) can cast tier ⌈level / 2⌉, capped at 5. Half casters (paladin, ranger) can cast tier ⌈level / 4⌉.
- **Pools:**
  - Full-caster Mana by level: 4, 6, 8, 10, 11, 12, 13, 14, 15, 16. Bard and druid get 3, 4, 6, 7, 8, 9, 10, 10, 11, 12 because they have a second resource.
  - Every other resource interpolates linearly between the spec §4.5 endpoints (level 1 → level 10), rounding down.
  - *After balancing:* the wizard keeps this Mana table. The bard, cleric, druid and sorcerer pools and the ranger's Focus differ, and warlock spells can cost more than 1 pact.
- **Spell ranges:** touch → 1. SRD range ≤ 30 ft → 4. Any longer ranged spell → 6.
- **Weapon ranges:** thrown → 4. Shortbow and light crossbow → 6. Longbow and heavy crossbow → 8.
- **Areas:** radius r ft → `burst` ⌈r / 10⌉. Cone r ft → `cone` ⌈r / 10⌉ + 1. Line r ft → `line` ⌈r / 10⌉.
- **Durations:** 1 minute → 10 rounds. 1 hour or longer → the whole battle. "Save ends" stays `until save`.
  - *After balancing:* Moonbeam lasts 1 round.
- **Per-rest features:** short-rest and long-rest uses both become `usesPerBattle`. Pools such as Lay on Hands refill each battle.
- **Left out:** spells with no battle use (utility, ritual, travel, divination).
- **`defaultPicks`:** every class lists a preset loadout per level, used by the simulator and by AI-built characters. Real players choose their own in the portal (sub-project 4).
- **Content may add, never override:** an id defined twice without `"replaces"` is a load error.
