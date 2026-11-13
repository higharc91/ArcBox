# Meridian: Breach timing (M1 simulation)

- **Date:** 2026-11-13
- **Build:** branch `ccr-de425e76-4vxzw2`, the commit that adds this report. Plan Task 11, issue [#330](https://github.com/higharc91/ArcBox/issues/330).
- **Players:** `ExploreBot` in every seat. **These are bot numbers, not people.** They catch a broken setting; they don't replace a playtest.
- **Content:** `content/meridian/base`, the first content wave (45 placeable tiles, 12 of them Anomaly compartments).

## What this measures, and the proxy

Spec §7.3 targets a Breach between 35% and 55% of a **whole game's** turns. The endgame doesn't exist until stage M3, so a whole game can't be measured yet. The M1 plan uses a proxy instead: **the Breach should land with 55–75% of the tiles placed** (25–34 of 45). Once M3 exists, re-run this against the real target.

## How it was run

```sh
dotnet build games/Meridian/Meridian.Sim -c Release
for t in 4 5 6 7; do
  dotnet run -c Release --no-build --project games/Meridian/Meridian.Sim -- \
    explore --games 2000 --players 3-6 --seed 1 --threshold $t
done
```

Each row is 2,000 games (seeds 1–2000) at one player count. Every run takes 10–18 s. Threshold 7 was added to the plan's 4, 5 and 6 because the trend pointed past 6. The default run after this change (`--threshold` omitted) is byte-identical to the threshold-6 run below.

**Columns.** *Anomaly at Breach* is how many Anomalies had been drawn when it fired. *Forced: 9th Anomaly* fired without a roll. *Forced: ship full* fired because the ship had nowhere left to grow (spec §2.6). *A deck ran out* means at least one deck had no tile left that fits it when the Breach fired. *Lowest step* is the lowest trait marker anyone reached.

## Results

### Threshold 4

Breach threshold 4; die faces [0, 0, 1, 1, 1, 2]; forced on Anomaly 9; 45 placeable tiles.

| Players | Games | Reached | Anomaly at Breach (p10 / median / p90) | Forced: 9th Anomaly | Forced: ship full | Rounds (median) | Turns (median) | Tiles placed (median) | Breach at 55–75% of tiles | A deck ran out | Cards per player | Lowest step |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 3 | 2000 | 100.0% | 3 / 4 / 6 | 0.1% | 0.0% | 4 | 12 | 16 | 6.1% | 10.4% | 3.8 | 1 |
| 4 | 2000 | 100.0% | 3 / 4 / 6 | 0.0% | 0.0% | 3 | 12 | 16 | 6.5% | 9.2% | 2.8 | 1 |
| 5 | 2000 | 100.0% | 3 / 4 / 6 | 0.0% | 0.0% | 3 | 13 | 16 | 6.6% | 7.7% | 2.3 | 1 |
| 6 | 2000 | 100.0% | 3 / 4 / 6 | 0.1% | 0.0% | 3 | 13 | 16 | 6.6% | 5.3% | 1.9 | 1 |

### Threshold 5 (the spec's original value)

Breach threshold 5; die faces [0, 0, 1, 1, 1, 2]; forced on Anomaly 9; 45 placeable tiles.

| Players | Games | Reached | Anomaly at Breach (p10 / median / p90) | Forced: 9th Anomaly | Forced: ship full | Rounds (median) | Turns (median) | Tiles placed (median) | Breach at 55–75% of tiles | A deck ran out | Cards per player | Lowest step |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 3 | 2000 | 100.0% | 4 / 5 / 7 | 0.8% | 0.0% | 5 | 15 | 20 | 20.6% | 27.4% | 4.7 | 1 |
| 4 | 2000 | 100.0% | 4 / 5 / 7 | 0.2% | 0.0% | 4 | 15 | 20 | 20.9% | 24.1% | 3.6 | 1 |
| 5 | 2000 | 100.0% | 4 / 5 / 7 | 0.4% | 0.0% | 4 | 16 | 20 | 21.0% | 20.8% | 2.9 | 1 |
| 6 | 2000 | 100.0% | 4 / 5 / 7 | 0.4% | 0.0% | 3 | 17 | 20 | 20.4% | 15.2% | 2.4 | 1 |

### Threshold 6 (recommended, now the default)

Breach threshold 6; die faces [0, 0, 1, 1, 1, 2]; forced on Anomaly 9; 45 placeable tiles.

| Players | Games | Reached | Anomaly at Breach (p10 / median / p90) | Forced: 9th Anomaly | Forced: ship full | Rounds (median) | Turns (median) | Tiles placed (median) | Breach at 55–75% of tiles | A deck ran out | Cards per player | Lowest step |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 3 | 2000 | 100.0% | 5 / 6 / 8 | 6.4% | 0.0% | 7 | 19 | 24 | 42.6% | 48.4% | 5.8 | 1 |
| 4 | 2000 | 100.0% | 5 / 6 / 8 | 5.4% | 0.0% | 5 | 19 | 24 | 41.3% | 42.3% | 4.3 | 1 |
| 5 | 2000 | 100.0% | 5 / 6 / 8 | 5.7% | 0.0% | 4 | 20 | 24 | 40.8% | 36.6% | 3.5 | 1 |
| 6 | 2000 | 100.0% | 5 / 6 / 8 | 6.0% | 0.0% | 4 | 20 | 24 | 40.2% | 29.9% | 2.9 | 1 |

### Threshold 7

Breach threshold 7; die faces [0, 0, 1, 1, 1, 2]; forced on Anomaly 9; 45 placeable tiles.

| Players | Games | Reached | Anomaly at Breach (p10 / median / p90) | Forced: 9th Anomaly | Forced: ship full | Rounds (median) | Turns (median) | Tiles placed (median) | Breach at 55–75% of tiles | A deck ran out | Cards per player | Lowest step |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 3 | 2000 | 100.0% | 6 / 7 / 9 | 23.4% | 0.0% | 8 | 22 | 28 | 58.8% | 65.7% | 6.8 | 1 |
| 4 | 2000 | 100.0% | 6 / 7 / 9 | 23.1% | 0.0% | 6 | 23 | 28 | 55.1% | 58.3% | 5.1 | 1 |
| 5 | 2000 | 100.0% | 6 / 7 / 9 | 24.5% | 0.0% | 5 | 24 | 27 | 55.1% | 51.8% | 4.1 | 1 |
| 6 | 2000 | 100.0% | 6 / 7 / 9 | 22.6% | 0.0% | 4 | 24 | 27 | 52.6% | 43.9% | 3.4 | 1 |

## Recommendation: 6

| Threshold | Median tiles at Breach | In the 55–75% band | Forced by the 9th Anomaly |
|---|---|---|---|
| 4 | 16 (36%) | ~6% | ~0% |
| 5 | 20 (44%) | ~21% | under 1% |
| **6** | **24 (53%)** | **~41%** | **~6%** |
| 7 | 27–28 (61%) | ~55% | ~23% |

- **5 is too early.** The Breach lands with under half the ship explored in most games, and only about one game in five is in the band.
- **7 hits the band most often, but costs the roll.** In nearly a quarter of games the ninth Anomaly fires the Breach with no roll at all. The roll is the game's main source of tension in Explore, and at 7 it often doesn't decide anything.
- **6 is the balance.** It roughly doubles the in-band rate over 5 and keeps the roll meaningful in about 94% of games.

`MeridianRules.BreachThreshold` and spec §2.6 change to 6 in the same commit as this report.

## Other findings

- **Every game reached the Breach** at every threshold and player count (32,000 games). No game needed the "ship full" rule, though it remains the guarantee that Explore always ends.
- **Player count barely moves the timing.** Tiles at the Breach and the Anomaly count are the same from 3 to 6 players; more players just means fewer rounds, so each player draws fewer cards (5.8 per player at 3 players, 2.9 at 6, with threshold 6).
- **A deck often runs dry before the Breach**: 30–48% of games at threshold 6, more with fewer players. Command is the likely deck (11 tiles plus shared ones). This is the main content follow-up: more Command-capable tiles, or fewer Command-only dead ends.
- **Someone always reaches step 1** of a trait before the Breach. Harmless now, because nobody can die before the Breach, but it means the endgame will start with someone one step from death. Revisit once M3's combat exists.
- **Explore lasts 19–20 turns** at threshold 6 (median). At 30–45 s a turn that is 10–15 minutes, which leaves room for a 45–60 minute game.

## What would change these numbers

- Real players explore less greedily than `ExploreBot`, which always heads for the nearest open hatch. That probably delays the Breach, so a playtest may argue for going back to 5.
- Closing the remaining gap to the band is better done with content (where Anomaly compartments sit, how many dead ends there are) than by pushing the threshold further.
