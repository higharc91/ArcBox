# REDLINE qualifying simulation

- **Date:** 2026-10-29
- **Status:** Qualifying is in the balance sim. The AI's qualifying pace keeps its defaults: the best of three players takes pole in **12.4%** of races, inside the 10–40% target.
- **Spec:** [2026-10-29-redline-qualifying-design.md](../specs/2026-10-29-redline-qualifying-design.md) (§2.3 AI cars, §7 testing)
- **Tool:** `games/Redline/tools/Redline.Sim` (`dotnet run --project games/Redline/tools/Redline.Sim -c Release -- --help`)
- **Code under test:** `Redline.Logic` at `88bde28`, with the defaults `AiRules.QualiSlowMedian` 3.6 s, `QualiFastMedian` 1.9 s and `QualiSigma` 0.25. Nothing in the game was changed.
- **Earlier report:** [2026-10-26-redline-balance-sim.md](2026-10-26-redline-balance-sim.md). This report reuses its method.

---

## TL;DR

1. **Pole odds are in range.** In the `party-three` scenario (3 casual players, 19 AI, 2000 seeded races), the best of the three players takes pole in **12.4%** of races.
   - Their best grid slot has a median of **P3** (p10 P1, p90 P8).
   - Over all three players, the median slot is **P8**.
   - Three other master seeds give 11.8%, 11.7% and 11.1%, and a median best slot of P4.
2. **No tuning.** 12.4% is inside the 10–40% target, so `QualiFastMedian` and `QualiSlowMedian` keep their defaults. `QualifyingMathTests`, which pins them, is unchanged. The odds are near the bottom of the band, so §3 shows what ±0.4 s would do.
3. **The AI usually takes pole, from its top tier.** When an AI car takes pole, about 92% of the time it is a top-tier car (aiSkill ≥ 1.08).
   - The median pole time is 5.9 s for three laps, about 1.95 s a lap.
   - A player's median lap is 2.6 s, so a player needs three right answers and quick laps to take pole.
4. **More players, more human poles.** A player takes pole in about 4% of races with 1 player, 15–18% with 4, 25% with 6, and about a third with 8. Race skill plays no part: every bot answers qualifying at 80%.
5. **Qualifying did not change the race balance.** It only sets the grid order, which breaks ties in the standings. With qualifying on, every race number is within Monte-Carlo noise of the same run with `--qualify off` (§4).
   - The largest change in a scenario's human win rate is 2.0 points (1.5σ).
   - Human mean positions move by at most 0.2 places.
6. **Qualifying takes about 36–41 s** with bots answering at 2.6 s, including the 3.5 s `QualifyingIntro` that lap 1's Reading adds for the commentator's opener. With every phase running to its timer it takes 48.5 s plus the read-outs. The intro changes only these times. Pole odds, grid slots and race numbers are identical with and without it. The tables in §2 and §4 were run before it was added.

---

## 1. Method

The method is the one in §1 of the 2026-10-26 report:
- every race is the real `RedlineGame`, driven through `GameHarness`;
- the human seats are bots that read only their own phone view.

The new part is that the bots now answer in qualifying.

| Item | Choice | Note |
|---|---|---|
| Human lap time | log-normal, median **2.6 s**, σ **0.3** (of ln t) | Drawn when the `QualifyingQuestion` view appears. The input is sent that long after the lap opened, on the harness clock. |
| Human `elapsedMs` | the drawn time | In the sim the phone's clock and the server's clock agree, so the §2.1 clamp keeps the phone's time as it is. |
| Human accuracy | **0.8** for every bot | Qualifying questions are all easy, and 0.8 is the casual player's easy accuracy. Strong bots also answer at 0.8, so pole odds depend only on the number of players. |
| Wrong answer | uniform over the three wrong options | As in the race. |
| AI laps | `Qualifying.AiLap` as shipped | Accuracy `AiPolicy.Accuracy(skill, Easy)` = 0.61–0.83. The median falls linearly from 3.6 s at aiSkill 0.85 to 1.9 s at 1.15, with σ 0.25, clamped to [0.6 s, 10 s]. |
| Random streams | qualifying draws come from their own `Random` | The race bots' decisions are drawn exactly as without qualifying. The game's own `ctx.Random` still draws the AI laps and the answer shuffles. So with qualifying on and off, the races after the Grid are different samples (§4). |

**Measures:**
- *Human pole:* the share of races where grid slot 1 is a player, i.e. the best of the players takes pole.
- *Best human slot:* the best-placed player's grid slot, read from the host view at the Grid phase.

**Headline scenario:** the new `party-three`, 3 casual players (all Sensible) and 19 AI. Race strategy does not affect qualifying.

Every scenario runs 2000 races with master seed 2026, and a re-run gives byte-identical output.

Reproduce:

```sh
dotnet run --project games/Redline/tools/Redline.Sim -c Release -- --out quali-on.md
dotnet run --project games/Redline/tools/Redline.Sim -c Release -- --qualify off --out quali-off.md
dotnet run --project games/Redline/tools/Redline.Sim -c Release -- --only party-three --ai-quali-fast 2.3   # what-if
```

**New options:** `--qualify on|off`, `--ai-quali-slow S`, `--ai-quali-fast S`, `--ai-quali-sigma X`.

**Changes to the main report:**
- a new **Qualifying** section;
- the **Wall-clock** table shows the qualifying time and counts it in its "all timers" bound.

## 2. Results (shipped defaults)

How to read the table:
- Grid slots are out of 22.
- "Grid → race" is the mean finishing position of a player who took pole.
- "Lap timeouts" counts player answers drawn past the 10 s window.

| Scenario | Humans | Human pole | Best human slot p10/p50/p90 | Human slot median (all seats) | Human mean slot | Human all 3/3 | Human correct laps | AI pole by tier top/mid/back | Pole time p50 (s) | Grid → race | Lap timeouts |
|---|---:|---:|---|---:|---:|---:|---:|---|---:|---:|---:|
| solo-cruise-casual | 1 | 4.1% | 2/7/17 | 7 | 8.6 | 52.5% | 2.41 | 91.1%/8.4%/0.5% | 5.84 | 17.1 | 0.0% |
| solo-push-casual | 1 | 3.6% | 3/8/17 | 8 | 8.8 | 49.9% | 2.39 | 93.4%/6.2%/0.4% | 5.82 | 9.2 | 0.0% |
| solo-redline-casual | 1 | 3.5% | 2/7/17 | 7 | 8.5 | 52.4% | 2.42 | 93.4%/6.4%/0.2% | 5.83 | 9.8 | 0.0% |
| solo-random-casual | 1 | 3.8% | 2/7/17 | 7 | 8.7 | 52.0% | 2.41 | 91.9%/7.7%/0.3% | 5.86 | 11.9 | 0.0% |
| solo-sensible-casual | 1 | 4.5% | 2/8/17 | 8 | 8.6 | 51.5% | 2.40 | 93.2%/6.3%/0.5% | 5.82 | 7.0 | 0.0% |
| solo-chaser-casual | 1 | 4.5% | 2/7/17 | 7 | 8.6 | 53.3% | 2.41 | 92.3%/7.5%/0.3% | 5.84 | 9.5 | 0.0% |
| solo-cruise-strong | 1 | 3.8% | 2/7/18 | 7 | 8.8 | 52.3% | 2.39 | 92.8%/7.0%/0.3% | 5.80 | 15.4 | 0.0% |
| solo-push-strong | 1 | 3.3% | 3/8/17 | 8 | 8.8 | 50.2% | 2.40 | 92.1%/7.5%/0.4% | 5.81 | 2.6 | 0.0% |
| solo-redline-strong | 1 | 3.9% | 2/8/17 | 8 | 8.7 | 50.7% | 2.40 | 92.1%/7.5%/0.4% | 5.83 | 2.6 | 0.0% |
| solo-random-strong | 1 | 4.2% | 2/7/17 | 7 | 8.6 | 51.9% | 2.41 | 92.0%/7.7%/0.3% | 5.81 | 5.8 | 0.0% |
| solo-sensible-strong | 1 | 4.9% | 2/7/17 | 7 | 8.7 | 51.1% | 2.40 | 92.2%/7.5%/0.4% | 5.80 | 2.9 | 0.0% |
| solo-chaser-strong | 1 | 3.3% | 3/8/17 | 8 | 9.0 | 50.4% | 2.39 | 92.7%/7.0%/0.3% | 5.80 | 2.0 | 0.0% |
| four-cruise | 4 | 15.5% | 1/3/6 | 8 | 9.1 | 51.1% | 2.40 | 92.2%/7.5%/0.3% | 5.88 | 15.6 | 0.0% |
| four-push | 4 | 15.2% | 1/3/6 | 8 | 9.2 | 50.2% | 2.38 | 92.9%/6.4%/0.6% | 5.88 | 6.7 | 0.0% |
| four-redline | 4 | 16.1% | 1/3/6 | 8 | 9.3 | 50.1% | 2.38 | 92.8%/7.0%/0.2% | 5.90 | 7.3 | 0.0% |
| four-random | 4 | 17.5% | 1/3/6 | 8 | 9.0 | 52.0% | 2.41 | 91.9%/7.8%/0.3% | 5.87 | 9.1 | 0.0% |
| four-sensible | 4 | 14.6% | 1/3/7 | 8 | 9.2 | 50.6% | 2.39 | 91.9%/7.9%/0.2% | 5.90 | 6.4 | 0.0% |
| four-chaser | 4 | 17.5% | 1/3/6 | 8 | 9.0 | 52.0% | 2.41 | 92.5%/7.0%/0.4% | 5.91 | 6.1 | 0.0% |
| eight-cruise | 8 | 33.0% | 1/2/4 | 9 | 9.6 | 50.6% | 2.40 | 93.4%/6.6%/0.1% | 5.92 | 14.1 | 0.0% |
| eight-push | 8 | 32.7% | 1/2/4 | 9 | 9.7 | 51.1% | 2.40 | 92.3%/7.4%/0.3% | 5.95 | 7.5 | 0.0% |
| eight-redline | 8 | 32.4% | 1/2/4 | 9 | 9.7 | 50.8% | 2.39 | 91.5%/8.3%/0.2% | 5.95 | 8.0 | 0.0% |
| eight-random | 8 | 33.1% | 1/2/4 | 9 | 9.6 | 51.2% | 2.40 | 93.2%/6.5%/0.3% | 5.91 | 9.1 | 0.0% |
| eight-sensible | 8 | 34.9% | 1/2/4 | 9 | 9.7 | 51.1% | 2.39 | 91.9%/7.6%/0.5% | 5.91 | 7.5 | 0.0% |
| eight-chaser | 8 | 33.1% | 1/2/4 | 9 | 9.6 | 51.1% | 2.40 | 92.8%/7.0%/0.2% | 5.93 | 8.0 | 0.0% |
| mixed-casual | 6 | 24.8% | 1/2/5 | 8 | 9.4 | 51.4% | 2.40 | 91.4%/8.6%/0.1% | 5.91 | 10.7 | 0.0% |
| mixed-strong | 6 | 24.9% | 1/2/5 | 9 | 9.4 | 50.9% | 2.40 | 92.3%/7.6%/0.1% | 5.91 | 6.6 | 0.0% |
| mixed-eight | 8 | 31.6% | 1/2/4 | 9 | 9.6 | 51.4% | 2.40 | 93.3%/6.6%/0.1% | 5.93 | 9.4 | 0.0% |
| party-three | 3 | 12.4% | 1/3/8 | 8 | 8.9 | 51.8% | 2.41 | 92.4%/7.3%/0.3% | 5.87 | 8.3 | 0.0% |
| party-four | 4 | 16.6% | 1/3/6 | 8 | 9.1 | 50.3% | 2.39 | 93.0%/6.6%/0.4% | 5.88 | 7.5 | 0.0% |

**Pole odds grow with the number of players:**

| Players | Human pole |
|---|---:|
| 1 | 3.3–4.9% |
| 3 | 12.4% |
| 4 | 14.6–17.5% |
| 6 | 24.8–24.9% |
| 8 | 31.6–34.9% |

With 8 players, the best of them starts in the top 2 half the time.

**About half the sessions are clean.** A player answers all 3 laps right in about 51% of sessions (0.8³ = 0.512) and 2.4 laps on average. One deleted lap usually drops a player well down the grid.

**No player times out.** At a 2.6 s median, a lap past 10 s is a 4.5σ event.

**Pole barely predicts the race result.** Grid order only breaks ties:
- a player who takes pole and then always cruises still finishes P14–P17 on average;
- strong Redline players average P2–P3 from pole, as they would from anywhere.

## 3. Sensitivity: the AI's qualifying pace (±0.4 s)

The default is 12.4%, near the bottom of the band. The table shows what the allowed ±0.4 s would do. Each row is 2000 races with master seed 2026, changing only the named median.

| AI medians (slow → fast) | 1 player (solo-sensible-casual) | **3 players (party-three)** | 4 players (party-four) | 8 players (eight-sensible) | Pole time p50, party-three |
|---|---:|---:|---:|---:|---:|
| 3.6 → 1.5 (fast −0.4) | 1.6% | 3.2% | 5.1% | 14.5% | 4.93 s |
| 3.2 → 1.9 (slow −0.4) | 3.5% | 9.2% | 12.3% | 28.6% | 5.74 s |
| **3.6 → 1.9 (shipped)** | **4.5%** | **12.4%** | **16.6%** | **34.9%** | **5.87 s** |
| 4.0 → 1.9 (slow +0.4) | 5.8% | 15.7% | 20.8% | 39.6% | 6.00 s |
| 3.6 → 2.3 (fast +0.4) | 11.5% | 29.3% | 36.1% | 59.2% | 6.61 s |
| 4.0 → 2.3 (both +0.4) | 13.9% | 35.0% | 40.1% | 64.8% | 6.73 s |

The fast median is the lever, because top-tier cars take almost every AI pole:
- slowing the fast end by 0.4 s more than doubles the players' pole odds;
- moving the slow median changes much less.

If playtests show that players feel shut out of pole, `QualiFastMedian` 2.1 s should put three players at roughly 20%. That value is interpolated, not run.

## 4. Did qualifying change the race balance?

It should not: qualifying only sets the grid slots and the tie-break order, and every car still starts at distance 0.

The check ran the full suite twice with the same seeds, 30 scenarios × 2000 races each time: once with `--qualify off` and once with qualifying on (the default).

With qualifying on, the game's random stream also draws the AI laps and the answer shuffles, so the races after the Grid are different samples. Any differences should look like noise, and they do.

| Scenario | Rounds mean off → on | Human win off → on | Shared P1 off → on | AI led off → on | Human win Δ / σ |
|---|---|---|---|---|---:|
| ai-only | 12.4 → 12.4 | 0.0% → 0.0% | 3.6% → 3.6% | 100.0% → 100.0% | +0.0 |
| solo-cruise-casual | 12.5 → 12.5 | 0.0% → 0.0% | 2.9% → 3.2% | 100.0% → 100.0% | +0.0 |
| solo-push-casual | 12.4 → 12.3 | 5.5% → 4.5% | 3.4% → 3.0% | 93.7% → 93.9% | -1.5 |
| solo-redline-casual | 12.2 → 12.2 | 9.9% → 9.6% | 3.4% → 3.4% | 84.5% → 84.4% | -0.3 |
| solo-random-casual | 12.4 → 12.4 | 2.7% → 2.3% | 3.1% → 2.9% | 94.2% → 94.4% | -0.8 |
| solo-sensible-casual | 12.2 → 12.2 | 12.8% → 13.4% | 3.1% → 3.5% | 85.5% → 85.4% | +0.6 |
| solo-chaser-casual | 12.2 → 12.2 | 12.0% → 11.7% | 3.1% → 2.9% | 85.9% → 85.8% | -0.3 |
| solo-cruise-strong | 12.4 → 12.4 | 0.0% → 0.0% | 3.5% → 2.9% | 99.9% → 100.0% | +0.0 |
| solo-push-strong | 11.8 → 11.7 | 41.8% → 41.7% | 2.3% → 2.3% | 70.1% → 70.4% | -0.1 |
| solo-redline-strong | 10.7 → 10.8 | 56.1% → 56.1% | 2.1% → 2.5% | 53.9% → 54.3% | +0.0 |
| solo-random-strong | 12.2 → 12.2 | 24.3% → 23.8% | 3.0% → 3.2% | 79.6% → 79.5% | -0.4 |
| solo-sensible-strong | 10.7 → 10.7 | 64.5% → 64.6% | 2.5% → 2.1% | 48.7% → 48.3% | +0.1 |
| solo-chaser-strong | 10.8 → 10.7 | 65.4% → 64.3% | 2.2% → 1.8% | 47.8% → 48.5% | -0.7 |
| four-cruise | 12.7 → 12.7 | 0.0% → 0.0% | 2.8% → 3.4% | 99.8% → 99.8% | +0.0 |
| four-push | 11.4 → 11.4 | 63.4% → 62.7% | 7.5% → 7.9% | 50.4% → 50.3% | -0.5 |
| four-redline | 9.7 → 9.7 | 81.9% → 82.3% | 9.0% → 8.9% | 24.7% → 23.9% | +0.3 |
| four-random | 11.7 → 11.8 | 45.1% → 44.3% | 1.9% → 1.8% | 56.3% → 56.4% | -0.5 |
| four-sensible | 9.9 → 9.9 | 87.8% → 86.9% | 7.5% → 7.3% | 20.4% → 20.9% | -0.9 |
| four-chaser | 9.8 → 9.8 | 87.1% → 87.0% | 6.2% → 6.2% | 20.5% → 20.3% | -0.1 |
| eight-cruise | 13.0 → 13.0 | 0.0% → 0.0% | 2.9% → 2.7% | 99.6% → 99.6% | +0.0 |
| eight-push | 11.0 → 11.1 | 80.1% → 81.0% | 18.6% → 18.4% | 34.7% → 34.8% | +0.7 |
| eight-redline | 8.7 → 8.6 | 95.6% → 95.0% | 27.8% → 26.8% | 8.3% → 8.1% | -0.9 |
| eight-random | 11.5 → 11.5 | 68.8% → 68.5% | 1.8% → 1.7% | 32.6% → 32.7% | -0.2 |
| eight-sensible | 9.1 → 9.1 | 97.0% → 96.8% | 21.6% → 21.3% | 6.4% → 6.4% | -0.4 |
| eight-chaser | 9.1 → 9.1 | 97.0% → 96.9% | 18.1% → 18.1% | 6.6% → 6.9% | -0.2 |
| mixed-casual | 12.2 → 12.1 | 40.6% → 38.6% | 2.9% → 3.1% | 54.6% → 55.1% | -1.3 |
| mixed-strong | 9.2 → 9.2 | 93.0% → 94.0% | 6.5% → 7.0% | 10.1% → 9.6% | +1.3 |
| mixed-eight | 9.4 → 9.3 | 93.8% → 93.6% | 7.7% → 7.8% | 10.2% → 10.2% | -0.3 |
| party-three | 12.1 → 12.1 | 31.9% → 31.9% | 5.0% → 3.8% | 67.8% → 67.4% | +0.0 |
| party-four | 10.7 → 10.6 | 75.0% → 74.2% | 2.6% → 2.9% | 33.8% → 34.0% | -0.6 |
- **Human win rate:**
  - every change is within ±1.5σ;
  - the largest moves are solo-push-casual (−1.0 point), mixed-casual (−2.0) and mixed-strong (+1.0);
  - their signs are mixed, as noise would be.
- **Mean finishing position:** within ±0.2 places for every bot in every scenario (Human bots table).
- **Rounds:** the mean number of rounds is within ±0.1.
- **ai-only:** identical, because qualifying is skipped for a room with no players.

**Conclusion:** qualifying did not change any race-balance number beyond sampling noise. The playtest checks in the 2026-10-26 report still apply.
