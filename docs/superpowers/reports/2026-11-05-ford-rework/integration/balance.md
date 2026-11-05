# Bridge layout: paired encounter results

Both encounters, Normal difficulty, environmental reactions enabled, levels 1–3,
session players 1–4, seeds 1–20: **480 battles per layout**. The old layout is
commit `217a2a5f` (content still identical to merged main); the new layout is the
installed Warden bridge. Enemy counts, HP, abilities, scaling, rewards, difficulty
and objectives are unchanged. The stormcaller moves from [9,7] to [8,5] to clear
the gatehouse; the camp spawn zone and one crate also move.

Each case uses identical classes, AI profiles and seed in both layouts. As in
the real game, 1–3 session players receive role-filling companions to meet the
unchanged four-hero minimum. Human-controlled slots use the class AI profile for
autoplay; filled slots use `companion`. This is a reproducible geometry comparison,
not a claim about human win rates. Caps at 30 rounds count as non-wins.

## Main comparison

| Encounter | Level | Players | Wins before → after /20 | Delta | Mean rounds before → after | Caps before → after |
|---|---:|---:|---:|---:|---:|---:|
| ford_assault | 1 | 1 | 10 → 15 | +25 pp | 10.05 → 10.75 | 0 → 1 |
| ford_assault | 1 | 2 | 10 → 9 | -5 pp | 11.80 → 11.90 | 0 → 1 |
| ford_assault | 1 | 3 | 11 → 13 | +10 pp | 9.70 → 12.10 | 0 → 1 |
| ford_assault | 1 | 4 | 12 → 13 | +5 pp | 7.40 → 9.20 | 0 → 0 |
| ford_assault | 2 | 1 | 20 → 20 | +0 pp | 7.05 → 8.25 | 0 → 0 |
| ford_assault | 2 | 2 | 20 → 19 | -5 pp | 7.30 → 7.55 | 0 → 0 |
| ford_assault | 2 | 3 | 20 → 20 | +0 pp | 7.90 → 7.75 | 0 → 0 |
| ford_assault | 2 | 4 | 20 → 20 | +0 pp | 7.25 → 6.75 | 0 → 0 |
| ford_assault | 3 | 1 | 20 → 20 | +0 pp | 7.10 → 6.40 | 0 → 0 |
| ford_assault | 3 | 2 | 20 → 20 | +0 pp | 6.65 → 8.00 | 0 → 0 |
| ford_assault | 3 | 3 | 20 → 20 | +0 pp | 6.00 → 6.60 | 0 → 0 |
| ford_assault | 3 | 4 | 20 → 20 | +0 pp | 5.90 → 6.85 | 0 → 0 |
| river_crossing | 1 | 1 | 16 → 14 | -10 pp | 10.35 → 14.00 | 0 → 3 |
| river_crossing | 1 | 2 | 16 → 13 | -15 pp | 12.15 → 12.85 | 2 → 2 |
| river_crossing | 1 | 3 | 15 → 18 | +15 pp | 12.10 → 12.25 | 0 → 2 |
| river_crossing | 1 | 4 | 17 → 14 | -15 pp | 9.70 → 11.35 | 0 → 1 |
| river_crossing | 2 | 1 | 20 → 19 | -5 pp | 9.35 → 10.80 | 0 → 0 |
| river_crossing | 2 | 2 | 19 → 18 | -5 pp | 10.75 → 10.75 | 0 → 0 |
| river_crossing | 2 | 3 | 20 → 20 | +0 pp | 9.75 → 10.30 | 0 → 0 |
| river_crossing | 2 | 4 | 20 → 20 | +0 pp | 8.20 → 10.30 | 0 → 0 |
| river_crossing | 3 | 1 | 20 → 20 | +0 pp | 6.90 → 8.25 | 0 → 0 |
| river_crossing | 3 | 2 | 20 → 20 | +0 pp | 7.80 → 7.40 | 0 → 0 |
| river_crossing | 3 | 3 | 20 → 20 | +0 pp | 7.00 → 8.25 | 0 → 0 |
| river_crossing | 3 | 4 | 20 → 20 | +0 pp | 7.90 → 8.20 | 0 → 0 |

At level 1, pooled over the four session sizes (80 battles each): Ford Assault
wins **43/80 → 50/80 (53.75% → 62.5%, +8.75 pp)** and takes **9.74 → 10.99**
rounds. River Crossing wins **64/80 → 59/80 (80% → 73.75%, −6.25 pp)** and takes
**11.07 → 12.61** rounds. Across all levels there are **426 → 425 wins /480**,
**2 → 11 caps**, and **zero rejected commands**. The almost unchanged aggregate
win count hides longer fights and more caps; those remain a material limitation.
With only 20 seeds per cell, a single result moves its win rate by five points.

## Environmental-tiles reference

The [final tuned environmental report](../../2026-11-02-environmental-tiles/completion.md)
reports level-1 environment-on River Crossing **329/360 (91.4%), 9.4 rounds**,
and Ford Assault **287/360 (79.7%), 8.1 rounds**. Its expanded Ford check reports
four-character parties at **62.5%**, versus five at 84.2% and six at 93.3%. Those
samples use 4–6 actual heroes and a different party/seed matrix; they cannot be
subtracted from this 1–4-session-player run as a paired layout effect. The exact
pre-bridge paired snapshot above is the primary comparison. Mother Skarrow still
has 66 HP, her scoped fire spell and the stormcaller's scoped fire spell remain
1d6, and the stormcaller retains 18 HP and its approved 1d8 Storm Hex.

## Proposed follow-up; no tuning applied

Keep encounter numbers unchanged pending owner review. First inspect the 11
capped seeds for route or progress stalls around the checkpoint and east-bank
landing, then repeat the affected level-1 cells with at least 100 seeds. An
advanced west-bank party spawn is a concrete geometry experiment to reduce
approach time before testing changes to enemy HP or damage. It needs its own
paired results and owner decision; it is not installed here. The longer River
Crossing and added caps should be reviewed even if the chapter smoke tests pass.

## Literal small-hero stress test

An additional 480 battles per layout remove test companions after canonical
encounter setup, leaving exactly 1–4 heroes without changing production content.
One-to-three-hero fights are unsupported stress scenarios, not the real game
experience for one-to-three players. The four-hero controls match the main
comparison's four-player cases byte-for-byte. Raw files: [before](stress-before.json)
and [after](stress-after.json).

| Encounter | Level | Actual heroes | Wins before → after /20 | Mean rounds before → after | Caps before → after |
|---|---:|---:|---:|---:|---:|
| ford_assault | 1 | 1 | 0 → 2 | 3.85 → 5.80 | 0 → 0 |
| ford_assault | 1 | 2 | 0 → 4 | 5.45 → 9.35 | 0 → 2 |
| ford_assault | 1 | 3 | 4 → 8 | 8.00 → 10.35 | 0 → 2 |
| ford_assault | 1 | 4 | 12 → 13 | 7.40 → 9.20 | 0 → 0 |
| ford_assault | 2 | 1 | 0 → 1 | 5.25 → 6.20 | 0 → 0 |
| ford_assault | 2 | 2 | 6 → 6 | 8.90 → 14.25 | 0 → 4 |
| ford_assault | 2 | 3 | 17 → 15 | 9.20 → 10.05 | 0 → 0 |
| ford_assault | 2 | 4 | 20 → 20 | 7.25 → 6.75 | 0 → 0 |
| ford_assault | 3 | 1 | 1 → 1 | 7.45 → 6.75 | 0 → 0 |
| ford_assault | 3 | 2 | 13 → 10 | 9.80 → 11.95 | 0 → 0 |
| ford_assault | 3 | 3 | 18 → 19 | 9.20 → 9.20 | 0 → 0 |
| ford_assault | 3 | 4 | 20 → 20 | 5.90 → 6.85 | 0 → 0 |
| river_crossing | 1 | 1 | 0 → 0 | 3.05 → 3.60 | 0 → 0 |
| river_crossing | 1 | 2 | 2 → 1 | 7.20 → 6.75 | 0 → 1 |
| river_crossing | 1 | 3 | 7 → 12 | 9.00 → 14.10 | 0 → 3 |
| river_crossing | 1 | 4 | 17 → 14 | 9.70 → 11.35 | 0 → 1 |
| river_crossing | 2 | 1 | 0 → 0 | 3.70 → 4.00 | 0 → 0 |
| river_crossing | 2 | 2 | 7 → 5 | 10.20 → 11.30 | 0 → 2 |
| river_crossing | 2 | 3 | 17 → 14 | 9.65 → 13.05 | 0 → 1 |
| river_crossing | 2 | 4 | 20 → 20 | 8.20 → 10.30 | 0 → 0 |
| river_crossing | 3 | 1 | 1 → 0 | 4.95 → 4.60 | 0 → 0 |
| river_crossing | 3 | 2 | 15 → 12 | 9.30 → 12.00 | 0 → 1 |
| river_crossing | 3 | 3 | 19 → 20 | 9.40 → 9.80 | 0 → 0 |
| river_crossing | 3 | 4 | 20 → 20 | 7.90 → 8.20 | 0 → 0 |

Stress totals are **236 → 237 wins /480**, caps **0 → 17**. Each stress layout
records one rejected AI command; those attempts are retried and reported rather
than silently removed. Full case data, classes and seeds are retained in the four
JSON files. Reproduce with `native/sim-impact.cs`; pass `--unfilled` only for stress.
