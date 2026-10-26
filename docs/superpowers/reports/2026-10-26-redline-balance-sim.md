# REDLINE balance simulation: first report

- **Date:** 2026-10-26
- **Status:** First pass, before any playtest. The recommended package (§4 tweaks 1–3) is now applied; §6 has the re-run.
- **Spec:** [2026-10-25-redline-design.md](../specs/2026-10-25-redline-design.md)
- **Tool:** `games/Redline/tools/Redline.Sim` (run it with `dotnet run --project games/Redline/tools/Redline.Sim -c Release -- --help`)
- **Code under test:** `Redline.Logic` at `bf82a42`. Nothing in the game or the AI was changed. What-if runs pass a different `RedlineRules` into the unchanged `RedlineGame`.

---

## TL;DR

1. **Races are far too short.** With the shipped rules a race lasts a median of **9 rounds** with 1 human, **6** with 4 humans and **5** with 8 humans. Five is the minimum possible, because 5 × REDLINE +4 = 20. Round 15 is almost never reached. A race takes **3–5 minutes** at realistic answer speed, and **4–7 minutes** even if every phase runs to its timer. The spec's target is 8–12 minutes.
2. **PUSH is a dead button, so the throttle decision is shallow.** The expected-value-maximising bot *never* picks Push, for any skill or difficulty. For casual players it plays "Redline unless the question is hard". For strong players it plays "always Redline". So **AlwaysRedline is as good as playing sensibly**: 45.6% / 50.1% head to head (casual / strong). A strong AlwaysPush player wins only **6.3%** of solo races, against **62%** for a strong AlwaysRedline player.
3. **The win is often shared.** **38%** of races with 8 sensible humans end with a shared P1, as do 29% of mixed 8-human races and 15% of 4-human races. Usually two players answered all 5 questions right on Redline. A tie-break on difficulty-weighted correct answers removes at most 2 points of that, because everyone answers the same questions.
4. **AI chase mode is effectively dead code for balance.** It applies to only **0.1–5.5%** of AI throttle decisions, because races end before "≤ 5 rounds left". Forcing it on for the whole race moves the AI's win rate by **less than 1 point**.
5. **`AiPolicy` hard-codes the payout table.** It uses the literal scores `p`, `3p − 1` and `6p − 2 − LimiterCost` and never reads `RedlineRules` gains or losses. Any payout change also needs an AI change, or the AI plays the old game.
6. **Skill is amplified.** Redline turns a 0.78 vs 0.57 accuracy gap into roughly 2× the expected progress. A strong player finishes ahead of a casual one **77–81%** of the time. In a realistic group of 1 strong + 3 casual players, the strong player wins **56%** of races and each casual wins 9%.
7. **AI strength roughly meets the spec target.** Against 21 AI, a casual sensible player finishes at a median of **P6** (wins 18%) and a casual random player at **P12**, so the average AI sits mid-pack against an average player. With 4 or more humans including a strong one, AI almost never wins (2–12%).

**Recommended package** (all other numbers unchanged): **`PushGain` 2 → 3** and **`FinishLine` 20 → 28**, keeping `MaxRounds` at 15, plus making `AiPolicy` read its scores from `RedlineRules`. In simulation, this:

- lengthens races to a median of 9–13 rounds;
- makes all three throttles matter: casual sensible play becomes Redline on easy, Push on medium and hard;
- puts sensible play ahead of AlwaysRedline at 59% head to head;
- cuts shared wins from 38% to 20% (8 sensible humans) and from 29% to 8% (mixed 8).

It also makes the strong player more dominant (56% → 64% in the 1 strong + 3 casual group). §4 has the details and the trade-offs.

---

## 1. Method

### 1.1 What is simulated
- Every race is the **real `RedlineGame`**, driven through `ArcBox.Sdk.Testing.GameHarness` exactly as the unit tests drive it. Scoring, ranking, the pit limiter, early advance, timers, the AI, the 22-car field and seat assignment all come from the shipped code.
- **Human seats are bots.** Each bot only:
  - reads its own `ControllerView`. It claims a seat from the `ChoiceView` of free drivers, reads the difficulty off the throttle `ButtonsView` prompt (`"{Category} · {Difficulty}. How hard do you push?"`), and picks from the 4 shuffled options of the question `ChoiceView`;
  - sends the same JSON a phone sends: `{selected:[code]}`, `{pressed:"redline"}`, `{selected:["c"]}`.

  The only extra information a bot uses is what the TV shows everyone: distances and the round number from `RedlineHostView`. Pit-limited bots get a `WaitView` and do nothing, as a phone would.
- **Reaction times.** Each bot input is scheduled on the harness clock at a reaction time drawn from a log-normal: medians claim 6 s, throttle 3 s, answer 7 s, σ = 0.45. An input that would land after the phase deadline is never sent, so the code's defaults apply: Cruise, or no answer, counted as wrong. With these numbers about 0.4% of throttle picks and 1.0% of answers time out. Early advance therefore happens exactly as the code does it. The tool also has `--latency instant`.
- **Seeded and reproducible.** The race seed is a stable hash of (master seed, scenario name, race index). It seeds the harness's `ctx.Random` (AI and game), the question source and the bots, each with a separate `Random`. The same command gives byte-identical output; this was checked.
- **Answer key.** The synthetic question source remembers each question's correct answer, so a bot can find the right option on its phone and then roll its own accuracy for that difficulty. On a miss it picks uniformly among the 3 wrong options.

### 1.2 Assumptions

| Item | Choice | Note |
|---|---|---|
| Question difficulty mix | **1/3 easy, 1/3 medium, 1/3 hard**, drawn independently per question | The brief said "roughly 1/3 each, like OpenTDB's default mix". OpenTDB's real live mix wasn't checked from this sandbox. A medium-heavy mix (30/45/25) was run as a sensitivity check (§3.8). |
| Casual player | P(correct) easy 0.80 / medium 0.55 / hard 0.35 | Mean 0.567 |
| Strong player | 0.95 / 0.80 / 0.60 | Mean 0.783 |
| Correctness | Independent per player, given difficulty | Real questions are easy or hard *for everyone at once*. Correlated answers would raise tie rates and lower variance. See §5. |
| Seat choice | Random free seat (default); `--seats top` takes the free driver with the most 2026 points | Only changes which AI drivers fill the field |
| AI | Exactly `AiPolicy` with `RedlineRules.Ai` defaults and `aiSkill` from `grid2026.json` (0.85–1.15) | |

### 1.3 Human strategies
- **AlwaysCruise / AlwaysPush / AlwaysRedline:** always press that button. They are forced to Cruise on the pit limiter, which is why AlwaysRedline shows about 17–29% Cruise.
- **Random:** uniform over the three buttons.
- **Sensible:** picks the throttle with the highest expected distance for its own accuracy *p* at the shown difficulty:
  - Cruise = `p·CruiseGain`;
  - Push = `p·PushGain − (1−p)·PushLoss`;
  - Redline = `p·RedlineGain − (1−p)·(RedlineLoss + L)`.

  *L* is the value of the round lost to the pit limiter: `E[best EV] − E[Cruise EV]` over the difficulty mix, solved as a fixed point. Under the shipped rules L = 0.75 for casual and 1.58 for strong. The resulting policy:

  | | easy | medium | hard |
  |---|---|---|---|
  | casual | Redline | Redline | Cruise |
  | strong | Redline | Redline | Redline |

  It reads its numbers from `RedlineRules`, so it adapts in what-if runs.
- **Chaser:** Sensible, but presses REDLINE whenever it is "late" and at least 3 units behind the leader. "Late" means the leader is within 6 of the line, or 5 or fewer rounds remain.

### 1.4 Scenarios and sample size
29 scenarios:

- `ai-only` (22 AI);
- `solo-*` (1 human + 21 AI, all 6 strategies × 2 skills);
- `four-*` (4 humans + 18 AI: 2 casual + 2 strong, all on one strategy);
- `eight-*` (8 humans + 14 AI: 4 casual + 4 strong, all on one strategy);
- `mixed-casual` and `mixed-strong` (6 humans, one per strategy);
- `mixed-eight` (Sensible, Chaser and AlwaysRedline at both skills, plus Random and AlwaysCruise casual);
- `party-four` (1 strong + 3 casual, all Sensible).

**Baseline: N = 20,000 races per scenario** (580,000 races, about 2.5 minutes on 4 cores). What-if runs use N = 4,000 per scenario on a 10-scenario subset. At N = 20,000 the 95% interval on a 20% rate is about ±0.6 points; at N = 4,000 it is about ±1.2 points.

---

## 2. Baseline results (shipped rules)

All numbers are from the N = 20,000 baseline. The full output is in the appendix.

### 2.1 Race length and how races end

| Scenario | Rounds mean | p10 / p50 / p90 | Ended at round 5 (the minimum) | Ended by crossing the line | Ended at R15 with nobody across |
|---|---:|---|---:|---:|---:|
| ai-only | 9.6 | 7 / 9 / 13 | 2.7% | 97.9% | 2.1% |
| solo-sensible-casual | 9.3 | 6 / 9 / 13 | 3.7% | 98.1% | 1.9% |
| solo-sensible-strong | 7.7 | 5 / 8 / 11 | 28.8% | 99.7% | 0.3% |
| four-sensible | 6.7 | 5 / 6 / 10 | 47.5% | 100% | 0% |
| party-four (1 strong + 3 casual) | 7.5 | 5 / 7 / 10 | 30.3% | 99.8% | 0.2% |
| eight-sensible | 5.9 | 5 / 5 / 8 | 69.0% | 100% | 0% |
| mixed-eight | 6.2 | 5 / 5 / 8 | 60.7% | 100% | 0% |
| eight-cruise (nobody takes risks) | 10.3 | 7 / 10 / 14 | 1.8% | 94.8% | 5.2% |

`MaxRounds = 15` almost never matters. The race length is set by whoever answers 5 Redlines in a row: one strong player does that 0.78⁵ ≈ 29% of the time, and four strong players about 75% of the time.

### 2.2 Who wins: one human against 21 AI

| Strategy | Casual: win | Top 3 | Mean / median pos | Strong: win | Top 3 | Mean / median pos | Spin-outs per race (c / s) |
|---|---:|---:|---|---:|---:|---|---|
| AlwaysCruise | 0.0% | 0.0% | 15.9 / 16 | 0.0% | 0.1% | 13.1 / 13 | 0 / 0 |
| AlwaysPush | 0.3% | 2.6% | 13.2 / 13 | 6.3% | 25.8% | 6.7 / 6 | 0 / 0 |
| AlwaysRedline | 17.7% | 30.2% | 8.9 / 8 | 61.8% | 77.9% | 2.8 / 1 | 2.87 / 1.44 |
| Random | 3.1% | 10.8% | 11.7 / 12 | 19.9% | 42.7% | 5.8 / 4 | 1.23 / 0.65 |
| Sensible | 18.4% | 34.1% | 7.7 / 6 | 62.4% | 78.1% | 2.8 / 1 | 1.71 / 1.42 |
| Chaser | 19.0% | 33.8% | 8.2 / 7 | 62.0% | 78.1% | 2.8 / 1 | 1.99 / 1.43 |

Sensible beats AlwaysRedline only on mean position. That edge comes from Cruising on hard questions as a casual; a strong Sensible player *is* an AlwaysRedline player. Chaser adds nothing measurable over Sensible.

### 2.3 Mixed strategies in one race: head to head

Each cell is P(row finishes ahead of column), with ties counting half. Win share is per seat.

**mixed-casual** (6 casual humans + 16 AI):

| ↓ ahead of → | Cruise | Push | Redline | Random | Sensible | Chaser | Win share |
|---|---:|---:|---:|---:|---:|---:|---:|
| AlwaysCruise | – | 32.8% | 20.5% | 28.6% | 14.8% | 17.2% | 0.0% |
| AlwaysPush | 67.2% | – | 31.1% | 42.9% | 24.9% | 27.3% | 0.1% |
| AlwaysRedline | 79.5% | 68.9% | – | 62.5% | 45.6% | 46.9% | 16.0% |
| Random | 71.4% | 57.1% | 37.5% | – | 31.7% | 33.8% | 2.2% |
| Sensible | 85.2% | 75.1% | 54.4% | 68.3% | – | 51.9% | 15.9% |
| Chaser | 82.8% | 72.7% | 53.1% | 66.2% | 48.1% | – | 17.0% |

**mixed-strong** (6 strong humans + 16 AI): AlwaysRedline vs Sensible is **50.1% / 49.9%**, and Chaser vs Sensible 49.9%. All three win about 40% each. AlwaysPush wins 0.1% and AlwaysCruise 0.0%.

### 2.4 Groups

| Scenario | Human win | AI win | AI led (share of rounds) | Shared P1 | Lead changes | Strong ahead of casual |
|---|---:|---:|---:|---:|---:|---:|
| four-sensible | 87.9% | 12.1% | 18.6% | 15.5% | 1.8 | 78.7% |
| eight-sensible | 97.5% | 2.5% | 4.5% | 38.2% | 1.5 | 77.0% |
| mixed-casual | 49.8% | 50.2% | 47.5% | 4.8% | 3.0 | – |
| mixed-eight | 95.3% | 4.7% | 7.8% | 29.2% | 1.6 | ~78% |
| party-four | 77.7% | 22.3% | 29.3% | 7.1% | 2.3 | 80.6% |

In party-four, the strong player wins **55.6%** of races. Each casual player wins **8.9%**, and the AI wins 22.3%.

### 2.5 Closeness, rubber-banding and AI tiers
- **There is no rubber-banding for humans.** `aiSkill` is a fixed per-driver multiplier, not a catch-up mechanic. The only catch-up is the AI chase bonus, and it barely fires (below).
- **Final P1–P2 gap.** The mean is 2.9–4.5 units. The gap is ≤ 1 unit (shared wins included) in 17–30% of races, and up to 51% with 8 humans, mostly because of ties. A single correct Redline (+4) covers the typical winning margin. Lead changes run 1.5–3.4 per race.
- **AI tiers.** The top tier (ANT, RUS, HAM, VER, LEC, NOR; aiSkill ≥ 1.08) is 6 of 22 cars but takes **69%** of AI wins in `ai-only`. The back tier (≤ 0.90, 7 cars) takes **5%**. Mean finishing positions are 8.2 / 11.6 / 13.6 for top / mid / back. The skill spread is visible but not deterministic.
- **AI chase mode** (≥ 6 behind with ≤ 5 rounds left) applies to **5.5%** of AI throttle decisions in `ai-only`, 1.7% in `party-four` and **0.1%** in `eight-sensible`.

### 2.6 Spin-outs
- An AI car spins **1.11×** per race, about 0.12 per round; Redline makes up 32.6% of AI throttle picks.
- A casual sensible player spins 1.71× per race, and a casual AlwaysRedline player 2.87×. The latter spends 29% of its rounds on the pit limiter.
- A strong player spins about 1.4× per race.

### 2.7 Ties
- Shared P1 by scenario: solo about 4%, four-sensible 15.5%, eight-sensible **38.2%**, eight-redline 45.2%, mixed-strong 26.4%, mixed-eight 29.2%.
- A third tie-break key (correct answers weighted easy 1 / medium 2 / hard 3) would cut these by only **1–2 points** (e.g. 38.2% → 36.7%, 45.2% → 43.2%). Tied players nearly always answered the *same* questions right, so they have the same difficulty points.

### 2.8 Wall-clock time
**What the code does.**
- **Driver Select** ends early once every connected player has a seat.
- **Throttle** ends early once every connected human is locked. Pit-limited humans count as locked, and AI never blocks.
- **Question** ends early once every connected human has answered.
- **Grid (6 s), Reveal (7 s) and Chequered (10 s)** always run their full timers.
- **With no human connected**, everything runs on timers.

| Scenario | Measured (model latency) mean / p90 | Instant bots mean | All phases to timer, mean / p90 | Saved by early advance |
|---|---|---:|---|---:|
| ai-only | 6.5 / 8.6 min | – | 6.5 / 8.6 min | 0% |
| solo-sensible-casual | 3.1 / 4.1 | 1.3 | 6.3 / 8.6 | 51% |
| party-four | 3.3 / 4.5 | 1.1 | 5.2 / 6.8 | 36% |
| four-sensible | 3.0 / 4.1 | 1.0 | 4.7 / 6.8 | 36% |
| eight-sensible | 3.0 / 4.0 | 0.9 | 4.2 / 5.5 | 28% |
| mixed-casual | 4.1 / 5.6 | 1.3 | 6.1 / 8.0 | 32% |

Even with every timer running out, no baseline scenario's mean reaches the spec's 8–12 minute target. The cause is the round count, not the timers: 37 s × 6–10 rounds plus 36 s of fixed overhead.

**Update (Reading phase, audio spec §2):** with the shipped rules every round now also holds the question for about 4.2 s before answers open (fallback-pack question lengths), which lengthens a race by 0.6–0.9 minutes without changing any balance number (N = 2,000: party-four 4.6 → 5.3 min measured and 7.2 → 7.9 min with all timers; solo-sensible-casual 4.0 → 4.8; eight-sensible 4.5 → 5.1; ai-only 8.2 → 9.1).

---

## 3. What-if runs

Each row changes only the listed `RedlineRules` values. N = 4,000. **Caveat:** `AiPolicy` scores throttles with hard-coded `3p − 1` and `6p − 2 − 0.6` (§4, item 3). So in runs that change payouts, the AI still plays the *old* payout table. Human numbers are reliable there; AI-strength numbers are approximate.

### 3.1 Race length

| Variant | ai-only p50 / at line | solo casual p50 / at line | party-four p50 / at line | eight-sensible p50 | Shared P1 eight / mixed-eight | Strong win (party-four) | Time, party-four: measured / all timers |
|---|---|---|---|---:|---|---:|---|
| **Baseline** (20 / 15) | 9 / 98% | 9 / 98% | 7 / 100% | 5 | 38% / 29% | 55.6% | 3.3 / 5.2 min |
| FinishLine 26 | 13 / 78% | 13 / 80% | 10 / 96% | 8 | 25% / 17% | 61.1% | 4.4 / 6.9 |
| FinishLine 30 | 15 / 52% | 15 / 54% | 12 / 88% | 10 | 21% / 15% | 63.5% | 5.0 / 7.8 |
| FinishLine 36, MaxRounds 18 | 18 / 42% | 18 / 48% | 14 / 86% | 12 | 16% / 12% | 68.3% | 6.0 / 9.4 |
| **Push +3, FinishLine 28** | 13 / 79% | 13 / 81% | 10 / 97% | 9 | 20% / 8% | 63.7% | 4.6 / 7.2 |
| Push +3, FinishLine 30 | 14 / 67% | 14 / 68% | 11 / 93% | 10 | 19% / 7% | 65.2% | 4.9 / 7.7 |

A longer track lengthens races and cuts ties. It also lets skill show through more often (law of large numbers), and in AI-heavy lobbies more races hit MaxRounds without anyone crossing the line.

### 3.2 Fixing PUSH

| Variant | Casual Sensible picks (C / P / R) | Strong Sensible picks | Sensible vs AlwaysRedline (casual, mixed) | AlwaysPush/strong solo win | Sensible/casual solo win |
|---|---|---|---:|---:|---:|
| Baseline | 44 / 0 / 56% | 18 / 0 / 82% | 54.4% | 6.3% | 18.4% |
| **Push +3/−1** (`PushGain 3`) | 6 / 62 / 32% | 8 / 30 / 62% | (+F28) **59.1%** | 38.0% (+F28: 47.6%) | 13.3% (+F28: 14.0%) |
| Redline +4/−3 (`RedlineLoss 3`) | 37 / 31 / 32% | 8 / 30 / 62% | 57.6%¹ | 7.1% | **7.3%** |

¹ Under `RedlineLoss 3`, Sensible/casual finishes ahead of AlwaysRedline/casual more often, yet AlwaysRedline *out-wins* it against 21 AI: 14.6% vs 7.3% solo, with a worse mean position (9.9 vs 8.4). Beating a 21-car field rewards variance, and Sensible gets too cautious. It also halves a casual player's chance to win. **Rejected.**

With **Push +3/−1**:

- the EV crossovers become Cruise < 1/3 < Push < (1 + L)/(2 + L) < Redline, where that upper bound is about 0.65 for casual and 0.73 for strong;
- a casual sensible player plays **Redline on easy and Push on medium and hard**, and a strong one Redline on easy and medium, Push on hard;
- Cruise is still right when you are essentially guessing (p < 1/3, as with a weak AI on a hard question), and is still forced by the pit limiter.

### 3.3 AI chase mode

`--ai-chase-rounds 15` makes chase eligible all race. It then applies to 41–46% of AI decisions, yet AI win rate moves by under 1 point: solo casual 81.6% → 82.5%, party-four 22.3% → 23.5%. The AI cars that can win are never 6 behind; the ones that are behind can't catch up with +0.5 Redline weight. **Chase mode is flavour, not balance.**

### 3.4 Weaker AI (on top of the recommended package)

Base accuracy was cut by about 8% (0.66 / 0.48 / 0.29):

- AI win rate drops: solo casual 86% → 76%, mixed-casual 55% → 37%;
- but AI-only lobbies now reach the line only **56%** of the time instead of 79%;
- AI strength therefore also sets race length in small lobbies.

Not recommended before a playtest.

### 3.5 Sensitivity checks (baseline rules)
- **`--seats top`** (humans take the most popular drivers first) leaves weaker AI in the field. Solo casual win rises from 18.4% to 20.3%, mixed-casual human win from 49.8% to 64.2%, and eight-sensible AI win falls from 2.5% to 0.5%.
- **Medium-heavy mix (30 / 45 / 25):** races are slightly shorter (eight-sensible mean 5.7), with more ties (eight-sensible 43%). The conclusions don't change.
- **`--latency instant`:** balance numbers are unchanged within noise. A race takes about 1 minute: the floor is the fixed Grid, Reveal and Chequered timers.

---

## 4. Findings and recommended tweaks, ranked by impact

1. **Race length (critical).**
   - **Problem.** Median 5–9 rounds, a 5-round minimum, and 3–5 minutes against an 8–12 minute target. The 15-round cap and the 3D lap almost never get used.
   - **Recommend:** `FinishLine` **20 → 28**, `MaxRounds` stays **15**, together with tweak 2.
   - **Effect.** Median rounds: 13 solo, 10 for 4 humans, 9 for 8 humans. 79–81% of AI-only and solo races still end at the line, as do 85% of all-casual groups and 97–100% of groups with a strong player. The minimum race is 7 rounds.
   - **If tweak 2 is not adopted,** use `FinishLine` **26** for similar lengths.
   - **Expected time** with the package: 4.1–5.6 minutes at the modelled reaction speed, and 6.2–8.4 minutes if phases run to their timers.
   - **If playtests still come in under 8 minutes,** raise `MaxRounds` and `FinishLine` together (e.g. 18 / 34), not the phase timers. Expect more MaxRounds finishes and stronger skill dominance.

2. **PUSH is strictly dominated (high).**
   - **Problem.** Under +2/−1, Push beats Cruise only for p > 0.5. It beats Redline only for p < (1 + L)/(3 + L), about 0.47–0.56, so the window is empty or a few points wide. The sensible bot **never** pressed Push, in any scenario. Strategy collapses to "Redline unless hard", and AlwaysRedline equals Sensible.
   - **Recommend:** `PushGain` **2 → 3**, so PUSH is **+3 / −1**.
   - **Effect.**
     - Every throttle gets a band. Casual sensible players press Push 62% of the time.
     - Sensible beats AlwaysRedline 59% head to head (casual), up from 54%.
     - A strong AlwaysPush player becomes a real contender: 48% solo win, up from 6%.
     - Shared wins in mixed 8-player races fall from 29% to 8%.
   - **Also update the phone hint** to "+3 if right · −1 if wrong".

3. **`AiPolicy` ignores `RedlineRules` payouts (high; prerequisite for 2).**
   - **Problem.** `ChooseThrottle` scores `p`, `3p − 1` and `6p − 2 − LimiterCost`, and takes only `AiRules`. It also charges the limiter cost unconditionally, rather than weighting it by the chance of a spin-out.
   - **Recommend.** Compute the AI scores from the rules, the same way the Sensible bot does:
     - Cruise = `p·CruiseGain`;
     - Push = `p·PushGain − (1−p)·PushLoss`;
     - Redline = `p·RedlineGain − (1−p)·(RedlineLoss + LimiterCost)`.

     Then re-tune `LimiterCost`. Once it is weighted by (1−p), the bot-derived value is 0.75–0.84 for a casual-strength player.
   - **Then re-run the sim.** The what-ifs in §3 ran with the stale AI.

4. **Shared wins (medium).**
   - **Problem.** 15–38% of 4–8 human races end with a shared P1.
   - **Recommend:** tweaks 1 + 2, which bring eight-sensible to 20% and mixed-eight to 8%.
   - **Don't add a difficulty-weighted tie-break.** Measured, it separates at most 2 points of races.
   - **The remaining ties are inherent:** identical answer histories. The existing "… share the win!" message is the right handling. If a decisive single winner matters, the only non-speed keys left are arbitrary ones (e.g. grid slot), and a playtest should decide.

5. **AI chase mode (low; flavour).**
   - **Problem.** The trigger "≤ 5 rounds left" is tied to `MaxRounds`, which races don't reach.
   - **Recommend.** Also trigger when the leader is within `FinishLine − 6` of the flag, and drop `ChaseGap` to 4, purely for drama: late AI lunges and spin-outs near the flag.
   - **Expect no balance effect.** Always-on chase moved AI win rate by less than 1 point.

6. **Skill amplification (medium; a design question, no rule proposed).**
   - **Problem.** A strong player finishes ahead of a casual one 78–81% of the time, and the recommended package raises that (party-four strong win 55.6% → 63.7%; casual 8.9% → 5.0%).
   - **Open question.** Whether that's acceptable for a party game is a design call. The current rules have no catch-up for humans. Drafting was rejected in concept C, and the AI chase mode is AI-only.
   - **If wanted,** a human-facing catch-up needs a new rule, and the sim can measure it as soon as it exists as a `RedlineRules` knob.

7. **AI strength (low; keep).**
   - **Current state.** A casual sensible player's median is P6 against 21 AI, and a casual random player's is P12. That matches "average AI mid-pack against an average player".
   - **AI as a winner:** it matters only in solo and casual lobbies (it wins 50% of mixed-casual races).
   - **Recommend.** Don't weaken it. Under the recommended package, an 8% cut sends 44% of AI-only and 36% of solo-casual races to MaxRounds without anyone crossing the line. Re-tune base accuracy only together with `FinishLine`, after real playtest accuracy data.

8. **Phase timers (low).**
   - **Current state.** Early advance works and saves 28–51% of wall time. The 7 s Reveal and 6 s Grid are fixed costs.
   - **Recommend:** no change. Race length should come from the round count (tweak 1), not longer timers. A longer Throttle or Question timer only matters when someone is slow.

### Summary of recommended `RedlineRules` changes

| Setting | Now | Recommended | Why |
|---|---:|---:|---|
| `PushGain` | 2 | **3** | Push is never the best choice today |
| `FinishLine` | 20 | **28** | Median 5–9 rounds today; target 10–13 |
| `MaxRounds` | 15 | 15 | Fine once FinishLine is 28 |
| `RedlineGain` / `RedlineLoss` | 4 / 2 | 4 / 2 | `RedlineLoss 3` tested and rejected (§3.2) |
| `AiPolicy` scores | hard-coded | from `RedlineRules` | Needed for any payout change |
| `Ai.ChaseRoundsLeft` | 5 | + distance trigger (leader ≥ FinishLine − 6), `ChaseGap` 4 | Flavour only |
| Push hint text | "+2 if right · −1 if wrong" | "+3 if right · −1 if wrong" | Follows `PushGain` |

---

## 5. What this simulation cannot tell you

- **Fun, tension and pacing feel.** It measures lengths, gaps, ties and lead changes, not whether a 10-round race feels too long or a shared win feels cheap.
- **Real question difficulty.** OpenTDB's labels are noisy and US-centric, and a question that is "hard" for one group is easy for another.
- **Correlated answers.** Bots answer independently, given difficulty. In reality a question is easy or hard for the whole room and the AI at once, which would raise tie rates and shrink the spread.
- **Real behaviour.** Humans don't play fixed strategies. They tilt after a spin-out, copy the leader, chat, misread, and answer slower when the question is long. The reaction-time model (3 s / 7 s medians) is a guess and only affects wall-clock time and the roughly 1% timeout rate.
- **Disconnects and reconnects.** Not simulated: every bot stays connected.
- **TV animation time.** The Reveal tween and camera cuts are assumed to fit inside the 7 s Reveal timer.
- **The AI under new payouts.** All what-ifs ran the AI with its hard-coded payout scores (§4, item 3). AI strength numbers for changed payouts are approximate until `AiPolicy` is updated and the sim is re-run.

---

## 6. After applying the package (2026-10-26)

**What changed in the code:** `FinishLine` 20 → 28, `PushGain` 2 → 3 (`MaxRounds` stays 15), and `AiPolicy` now computes its throttle scores from `RedlineRules`: Cruise = p·CruiseGain, Push = p·PushGain − (1−p)·PushLoss, Redline = p·RedlineGain − (1−p)·(RedlineLoss + LimiterCost). The limiter cost is now weighted by the chance of a wrong answer. Temperature 0.35, the chase rule and the p < 0.3 Redline floor are unchanged, and `LimiterCost` stays 0.6. The phone hints are built from the rules too.

**Run:** the full 29-scenario baseline, N = 20,000 per scenario, seed 2026, default options (the defaults *are* the package now), 339 s on 4 cores. "Predicted" is the §3 package row (N = 4,000, stale AI). "Before" is the §2 baseline.

| Metric | Before (20 / +2) | Predicted (§3) | **After** | |
|---|---:|---:|---:|---|
| Median rounds: ai-only / solo casual / party-four / eight-sensible | 9 / 9 / 7 / 5 | 13 / 13 / 10 / 9 | **12 / 12 / 10 / 9** | ≈ (solo one round shorter) |
| Shortest race | 5 | 7 | **7** | ✓ |
| Ended at the line (rest hit R15): ai-only / solo casual / party-four / eight-sensible | 98 / 98 / 100 / 100% | 79 / 81 / 97 / –% | **85 / 86 / 97 / 100%** | ⚠ more races end at the line |
| R15 with nobody across: ai-only / mixed-casual | 2.1 / 1.2% | – | **14.8 / 12.1%** | |
| Casual Sensible picks C / P / R | 44 / 0 / 56% | 6 / 62 / 32% | **6 / 63 / 31%** | ✓ |
| Strong Sensible picks C / P / R | 18 / 0 / 82% | 8 / 30 / 62% | **8 / 31 / 61%** | ✓ |
| Sensible ahead of AlwaysRedline, mixed-casual / mixed-strong | 54.4 / 49.9% | 59.1 / –% | **59.6 / 52.7%** | ✓ |
| AlwaysPush/strong solo win | 6.3% | 47.6% | **41.5%** | ⚠ −6 pts |
| Shared P1: eight-sensible / mixed-eight / four-sensible | 38.2 / 29.2 / 15.5% | 20 / 8 / –% | **19.9 / 7.5 / 7.7%** | ✓ |
| Casual Sensible solo: win / median pos (mean) | 18.4% / P6 (7.7) | 14.0% / – | **12.1% / P7 (7.9)** | ⚠ −2 pts |
| Casual Random solo: win / median pos | 3.1% / P12 | – | **2.9% / P12** | ✓ |
| party-four: strong win / each casual / AI | 55.6 / 8.9 / 22.3% | 63.7 / 5.0 / –% | **61.4 / 4.7 / 25.5%** | ≈ |
| AI win: solo casual Sensible / mixed-casual | 81.6 / 50.2% | – | **87.9 / 61.1%** | ⚠ AI stronger |
| AI in chase mode, ai-only / party-four | 5.5 / 1.7% | – | **15.0 / 8.8%** | |
| Wall time, party-four: measured / all timers | 3.3 / 5.2 min | 4.6 / 7.2 | **4.6 / 7.2 min** | ✓ |
| Wall time, Sensible/Chaser, mixed and party scenarios: measured / all timers | 2.6–4.1 / 4.2–6.3 min | 4.1–5.6 / 6.2–8.4 (§4) | **3.6–5.5 / 6.2–8.2 min** | ✓ (ai-only: 8.2) |

**Deviations from the predictions**

- **The AI is stronger than the §3 runs assumed.** It now values Push at +3, and with the limiter cost weighted by (1−p), Redline looks better to it when it is likely to be right. AI win rate against a solo casual sensible player rises from 81.6% to 87.9%, and in mixed-casual from 50.2% to 61.1%. A casual sensible player now wins 12.1% of solo races (predicted 14.0%) and a strong AlwaysPush player 41.5% (predicted 47.6%). A casual sensible player's median finish is P7 and a casual random player's P12, so "average AI mid-pack against an average player" still roughly holds.
- **More AI-heavy races end at the line than predicted** (85–86% against 79–81%), for the same reason: the AI field now moves faster. Median rounds in ai-only and solo races is 12, not 13. Still, about 1 in 7 AI-heavy races ends at R15 with nobody across.
- **Chase mode now fires more often** (15% of AI decisions in ai-only, up from 5.5%), because more races get within 5 rounds of `MaxRounds`.
- Everything that depends only on the human side matched within noise: throttle mix, Sensible vs AlwaysRedline, shared wins, party-four timing.

**`LimiterCost` was not re-tuned.** §4 item 3 suggested 0.75–0.84 once the cost is weighted by (1−p). A what-if at 0.84 (N = 4,000: ai-only, solo-sensible-casual, mixed-casual, party-four) changed nothing beyond noise: AI Redline share 31% → 28%, AI win and race length within ±3 points. 0.6 stays until playtest data says otherwise.

**Next:** re-tune `FinishLine`, `PushGain` and the AI base accuracy after the first real playtest. If the stronger AI feels oppressive in small lobbies, lower the AI base accuracy together with `FinishLine` (§4 item 7), not on its own.

---

## 7. Reproduce

```bash
# full baseline (29 scenarios × 20,000 races, ~6 min on 4 cores); the defaults are the §6 package
dotnet run --project games/Redline/tools/Redline.Sim -c Release -- -n 20000 --out baseline.md

# the pre-package rules (§2), now with the rule-driven AI
dotnet run --project games/Redline/tools/Redline.Sim -c Release -- -n 4000 --push-gain 2 --finish 20

# the what-if subset used in §3
dotnet run --project games/Redline/tools/Redline.Sim -c Release -- -n 4000 \
  --only ai-only,solo-sensible,solo-redline,solo-push-strong,four-sensible,eight-sensible,mixed-casual,mixed-strong,mixed-eight,party-four \
  --push-gain 2 --finish 26   # or --finish 30 | --finish 36 --max-rounds 18 | --redline-loss 3 | --push-gain 3 [--finish 28|30]
                              # | --ai-chase-rounds 15 | --seats top | --mix 0.3,0.45,0.25 | --latency instant
```

§2 and §3 ran with the old defaults (`FinishLine` 20, `PushGain` 2) and the old hard-coded AI scores, so pass `--push-gain 2 --finish 20` (or the listed `--finish`) to compare against them. Their AI numbers will not reproduce exactly, because the AI now reads the rules.

The master seed defaults to 2026 (`--seed`). Output goes to stdout as Markdown, with progress on stderr.

---

## Appendix: full baseline output (N = 20,000 per scenario, pre-package rules and AI)

- Races per scenario: **20000** (580000 total, 144 s wall time), master seed 2026
- Rules: FinishLine 20, MaxRounds 15, Cruise +1/0, Push +2/−1, Redline +4/−2 + limiter
- AI: base 0.72/0.52/0.32, clamp [0.08, 0.92], temperature 0.35, limiter cost 0.60, Redline floor 0.30, chase gap 6 / rounds left 5 / bonus 0.50
- Question mix: easy 33 % / medium 33 % / hard 33 %; human latency: model; human seat pick: Random
- Skills: casual 0.80/0.55/0.35, strong 0.95/0.80/0.60; limiter cost used by Sensible/Chaser: casual 0.75, strong 1.58

### Scenarios

- `ai-only`: 22 AI, no humans (baseline for the AI field)
- `solo-cruise-casual`: 1 human (AlwaysCruise/casual) + 21 AI
- `solo-push-casual`: 1 human (AlwaysPush/casual) + 21 AI
- `solo-redline-casual`: 1 human (AlwaysRedline/casual) + 21 AI
- `solo-random-casual`: 1 human (Random/casual) + 21 AI
- `solo-sensible-casual`: 1 human (Sensible/casual) + 21 AI
- `solo-chaser-casual`: 1 human (Chaser/casual) + 21 AI
- `solo-cruise-strong`: 1 human (AlwaysCruise/strong) + 21 AI
- `solo-push-strong`: 1 human (AlwaysPush/strong) + 21 AI
- `solo-redline-strong`: 1 human (AlwaysRedline/strong) + 21 AI
- `solo-random-strong`: 1 human (Random/strong) + 21 AI
- `solo-sensible-strong`: 1 human (Sensible/strong) + 21 AI
- `solo-chaser-strong`: 1 human (Chaser/strong) + 21 AI
- `four-cruise`: 4 humans (AlwaysCruise: 2 casual, 2 strong) + 18 AI
- `four-push`: 4 humans (AlwaysPush: 2 casual, 2 strong) + 18 AI
- `four-redline`: 4 humans (AlwaysRedline: 2 casual, 2 strong) + 18 AI
- `four-random`: 4 humans (Random: 2 casual, 2 strong) + 18 AI
- `four-sensible`: 4 humans (Sensible: 2 casual, 2 strong) + 18 AI
- `four-chaser`: 4 humans (Chaser: 2 casual, 2 strong) + 18 AI
- `eight-cruise`: 8 humans (AlwaysCruise: 4 casual, 4 strong) + 14 AI
- `eight-push`: 8 humans (AlwaysPush: 4 casual, 4 strong) + 14 AI
- `eight-redline`: 8 humans (AlwaysRedline: 4 casual, 4 strong) + 14 AI
- `eight-random`: 8 humans (Random: 4 casual, 4 strong) + 14 AI
- `eight-sensible`: 8 humans (Sensible: 4 casual, 4 strong) + 14 AI
- `eight-chaser`: 8 humans (Chaser: 4 casual, 4 strong) + 14 AI
- `mixed-casual`: 6 casual humans, one per strategy, + 16 AI
- `mixed-strong`: 6 strong humans, one per strategy, + 16 AI
- `mixed-eight`: 8 humans: Sensible, Chaser, AlwaysRedline at both skills + Random and AlwaysCruise casual, + 14 AI
- `party-four`: 4 humans as a realistic group: 1 strong + 3 casual, all Sensible, + 18 AI

### Race overview

| Scenario | Humans | Rounds mean | p10/p50/p90 | Ended at line | Human win | Shared P1 | …with difficulty tie-break | AI led (of rounds) | Lead changes | P1−P2 gap | gap ≤1 | P1 dist | P11 dist |
|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ai-only | 0 | 9.6 | 7/9/13 | 97.9% | 0.0% | 4.0% | 2.4% | 100.0% | 3.3 | 2.91 | 30.1% | 21.0 | 9.4 |
| solo-cruise-casual | 1 | 9.7 | 7/9/13 | 97.5% | 0.0% | 3.8% | 2.3% | 99.7% | 3.3 | 2.96 | 29.1% | 21.0 | 9.2 |
| solo-push-casual | 1 | 9.6 | 7/9/13 | 97.4% | 0.3% | 3.6% | 2.2% | 97.5% | 3.4 | 2.96 | 29.3% | 21.0 | 9.4 |
| solo-redline-casual | 1 | 9.2 | 6/9/13 | 98.3% | 17.7% | 3.8% | 2.5% | 79.7% | 3.2 | 3.18 | 27.4% | 21.0 | 9.2 |
| solo-random-casual | 1 | 9.6 | 7/9/13 | 98.0% | 3.1% | 3.8% | 2.4% | 92.5% | 3.4 | 2.96 | 28.7% | 21.0 | 9.4 |
| solo-sensible-casual | 1 | 9.3 | 6/9/13 | 98.1% | 18.4% | 4.3% | 2.8% | 79.6% | 3.2 | 2.96 | 29.4% | 21.0 | 9.3 |
| solo-chaser-casual | 1 | 9.3 | 6/9/13 | 98.5% | 19.0% | 4.1% | 2.7% | 79.6% | 3.2 | 3.00 | 28.9% | 21.0 | 9.3 |
| solo-cruise-strong | 1 | 9.6 | 7/9/13 | 97.8% | 0.0% | 3.9% | 2.3% | 99.5% | 3.3 | 2.95 | 29.8% | 21.0 | 9.3 |
| solo-push-strong | 1 | 9.6 | 7/9/13 | 98.4% | 6.3% | 3.7% | 2.3% | 89.8% | 3.4 | 2.87 | 30.0% | 21.0 | 9.7 |
| solo-redline-strong | 1 | 7.8 | 5/8/11 | 99.7% | 61.8% | 3.7% | 2.8% | 49.4% | 2.4 | 4.50 | 17.3% | 21.0 | 8.0 |
| solo-random-strong | 1 | 9.2 | 7/9/12 | 98.9% | 19.9% | 3.6% | 2.3% | 79.2% | 3.2 | 3.03 | 28.3% | 21.0 | 9.4 |
| solo-sensible-strong | 1 | 7.7 | 5/8/11 | 99.7% | 62.4% | 3.6% | 2.7% | 49.2% | 2.4 | 4.52 | 17.6% | 21.0 | 8.0 |
| solo-chaser-strong | 1 | 7.7 | 5/8/11 | 99.7% | 62.0% | 3.8% | 2.9% | 49.5% | 2.4 | 4.50 | 18.0% | 21.0 | 8.0 |
| four-cruise | 4 | 9.9 | 7/10/14 | 96.9% | 0.0% | 3.4% | 1.9% | 98.8% | 3.3 | 3.14 | 27.3% | 21.0 | 8.7 |
| four-push | 4 | 9.7 | 7/10/13 | 98.7% | 13.1% | 3.4% | 2.1% | 80.1% | 3.5 | 2.91 | 28.7% | 21.0 | 9.8 |
| four-redline | 4 | 6.6 | 5/5/10 | 100.0% | 87.7% | 19.6% | 17.6% | 18.4% | 1.8 | 4.13 | 29.7% | 20.8 | 7.4 |
| four-random | 4 | 9.0 | 7/9/12 | 99.5% | 38.9% | 3.2% | 2.0% | 55.2% | 3.2 | 3.07 | 28.0% | 21.0 | 9.3 |
| four-sensible | 4 | 6.7 | 5/6/10 | 100.0% | 87.9% | 15.5% | 14.1% | 18.6% | 1.8 | 4.13 | 25.9% | 20.8 | 7.6 |
| four-chaser | 4 | 6.7 | 5/6/9 | 100.0% | 88.2% | 15.9% | 14.4% | 18.2% | 1.9 | 4.06 | 27.1% | 20.8 | 7.5 |
| eight-cruise | 8 | 10.3 | 7/10/14 | 94.8% | 0.2% | 2.8% | 1.3% | 97.9% | 3.3 | 3.42 | 24.9% | 20.9 | 8.2 |
| eight-push | 8 | 9.9 | 7/10/13 | 99.2% | 25.6% | 5.5% | 3.6% | 68.0% | 3.6 | 2.81 | 30.6% | 20.9 | 10.2 |
| eight-redline | 8 | 5.8 | 5/5/8 | 100.0% | 97.4% | 45.2% | 43.2% | 4.5% | 1.5 | 2.97 | 51.4% | 20.5 | 7.3 |
| eight-random | 8 | 8.7 | 6/9/11 | 99.9% | 61.0% | 3.0% | 1.9% | 32.8% | 3.2 | 3.02 | 27.8% | 21.0 | 9.4 |
| eight-sensible | 8 | 5.9 | 5/5/8 | 100.0% | 97.5% | 38.2% | 36.7% | 4.5% | 1.5 | 3.09 | 45.5% | 20.5 | 7.5 |
| eight-chaser | 8 | 5.8 | 5/5/8 | 100.0% | 97.7% | 38.9% | 37.3% | 4.4% | 1.4 | 2.99 | 46.0% | 20.5 | 7.4 |
| mixed-casual | 6 | 8.9 | 6/9/12 | 98.8% | 49.8% | 4.8% | 3.6% | 47.5% | 3.0 | 3.31 | 26.9% | 21.0 | 8.7 |
| mixed-strong | 6 | 6.2 | 5/5/8 | 100.0% | 93.3% | 26.4% | 24.6% | 10.4% | 1.6 | 3.83 | 34.8% | 20.7 | 7.2 |
| mixed-eight | 8 | 6.2 | 5/5/8 | 100.0% | 95.3% | 29.2% | 27.4% | 7.8% | 1.6 | 3.61 | 37.6% | 20.7 | 7.1 |
| party-four | 4 | 7.5 | 5/7/10 | 99.8% | 77.7% | 7.1% | 6.3% | 29.3% | 2.3 | 4.05 | 22.3% | 20.9 | 8.1 |

### Race length (share of races ending after round n)

| Scenario | <6 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | of which R15 with nobody at the line |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ai-only | 2.7% | 6.2% | 12.3% | 15.5% | 16.0% | 14.7% | 11.4% | 8.3% | 5.5% | 3.5% | 4.1% | 2.1% |
| solo-cruise-casual | 2.4% | 6.3% | 11.4% | 15.2% | 16.1% | 14.2% | 11.6% | 8.8% | 5.8% | 3.5% | 4.7% | 2.5% |
| solo-push-casual | 2.5% | 6.4% | 11.5% | 15.4% | 15.9% | 14.5% | 11.5% | 8.1% | 5.7% | 3.6% | 4.9% | 2.6% |
| solo-redline-casual | 7.3% | 5.8% | 12.2% | 16.5% | 15.1% | 15.0% | 9.5% | 7.9% | 4.5% | 3.0% | 3.1% | 1.7% |
| solo-random-casual | 2.4% | 6.7% | 12.2% | 15.4% | 16.1% | 14.4% | 11.4% | 8.3% | 5.6% | 3.5% | 4.1% | 2.0% |
| solo-sensible-casual | 3.7% | 8.3% | 12.5% | 16.2% | 16.3% | 14.0% | 10.4% | 7.4% | 5.1% | 2.8% | 3.3% | 1.9% |
| solo-chaser-casual | 3.8% | 7.9% | 13.0% | 16.2% | 16.6% | 13.9% | 10.4% | 7.5% | 4.8% | 3.0% | 3.0% | 1.6% |
| solo-cruise-strong | 2.4% | 6.6% | 12.0% | 15.2% | 15.6% | 14.4% | 11.5% | 8.4% | 5.7% | 3.8% | 4.4% | 2.2% |
| solo-push-strong | 2.3% | 6.3% | 11.6% | 15.0% | 16.5% | 16.0% | 11.5% | 8.6% | 6.0% | 2.8% | 3.4% | 1.6% |
| solo-redline-strong | 28.0% | 4.2% | 12.1% | 21.8% | 9.7% | 12.7% | 4.0% | 4.3% | 1.3% | 1.1% | 0.7% | 0.3% |
| solo-random-strong | 2.4% | 6.8% | 13.7% | 17.6% | 17.5% | 15.3% | 10.5% | 7.0% | 4.5% | 2.5% | 2.1% | 1.1% |
| solo-sensible-strong | 28.8% | 4.2% | 12.3% | 21.6% | 9.8% | 12.3% | 3.9% | 4.1% | 1.4% | 1.0% | 0.7% | 0.3% |
| solo-chaser-strong | 28.2% | 4.4% | 12.6% | 21.5% | 9.7% | 12.4% | 3.9% | 4.3% | 1.3% | 0.9% | 0.8% | 0.3% |
| four-cruise | 2.0% | 5.6% | 10.4% | 14.6% | 15.3% | 14.3% | 12.1% | 9.1% | 6.4% | 4.3% | 5.8% | 3.1% |
| four-push | 2.1% | 5.4% | 10.6% | 14.4% | 15.5% | 17.9% | 11.5% | 10.3% | 6.9% | 2.6% | 2.9% | 1.3% |
| four-redline | 50.8% | 2.2% | 11.9% | 19.4% | 5.3% | 7.3% | 1.0% | 1.5% | 0.3% | 0.2% | 0.1% | 0.0% |
| four-random | 2.4% | 7.1% | 14.5% | 19.8% | 19.1% | 15.0% | 10.0% | 6.2% | 3.2% | 1.6% | 1.1% | 0.5% |
| four-sensible | 47.5% | 4.3% | 12.2% | 19.7% | 5.9% | 7.0% | 1.2% | 1.6% | 0.3% | 0.2% | 0.1% | 0.0% |
| four-chaser | 47.4% | 4.6% | 12.4% | 19.7% | 6.0% | 6.8% | 1.2% | 1.3% | 0.3% | 0.2% | 0.1% | 0.0% |
| eight-cruise | 1.8% | 4.4% | 8.9% | 12.3% | 14.2% | 13.8% | 12.5% | 10.0% | 7.8% | 5.5% | 8.8% | 5.2% |
| eight-push | 1.5% | 4.7% | 8.7% | 12.5% | 14.4% | 22.2% | 10.7% | 12.9% | 7.9% | 2.1% | 2.2% | 0.8% |
| eight-redline | 73.2% | 1.1% | 8.8% | 12.5% | 1.8% | 2.2% | 0.1% | 0.3% | 0.0% | 0.0% | 0.0% | 0.0% |
| eight-random | 2.2% | 7.9% | 17.1% | 21.9% | 20.5% | 14.8% | 8.5% | 4.2% | 1.9% | 0.7% | 0.4% | 0.1% |
| eight-sensible | 69.0% | 2.9% | 10.0% | 12.7% | 2.4% | 2.5% | 0.2% | 0.2% | 0.0% | 0.0% | 0.0% | 0.0% |
| eight-chaser | 69.5% | 3.3% | 9.9% | 12.5% | 2.1% | 2.3% | 0.2% | 0.2% | 0.0% | 0.0% | 0.0% | 0.0% |
| mixed-casual | 9.2% | 8.6% | 13.4% | 16.7% | 14.5% | 14.0% | 8.3% | 6.7% | 3.9% | 2.2% | 2.5% | 1.2% |
| mixed-strong | 59.7% | 2.2% | 11.1% | 17.4% | 3.9% | 4.5% | 0.5% | 0.6% | 0.1% | 0.0% | 0.0% | 0.0% |
| mixed-eight | 60.7% | 2.9% | 10.8% | 16.2% | 3.9% | 4.2% | 0.5% | 0.6% | 0.1% | 0.0% | 0.0% | 0.0% |
| party-four | 30.3% | 7.4% | 13.7% | 19.9% | 9.6% | 10.6% | 3.4% | 2.9% | 1.1% | 0.6% | 0.5% | 0.2% |

### Human bots (per seat; positions out of 22)

| Scenario | Bot | Seats | Win | Top 3 | Top 5 | Mean pos | Median pos | Mean dist | Correct | Cruise/Push/Redline | Spin-outs/race | No answer/race |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|
| solo-cruise-casual | AlwaysCruise/casual | 1 | 0.0% | 0.0% | 0.1% | 15.9 | 16 | 5.4 | 56.1% | 100.0%/0.0%/0.0% | 0.00 | 0.10 |
| solo-push-casual | AlwaysPush/casual | 1 | 0.3% | 2.6% | 7.9% | 13.2 | 13 | 7.5 | 55.8% | 0.4%/99.6%/0.0% | 0.00 | 0.10 |
| solo-redline-casual | AlwaysRedline/casual | 1 | 17.7% | 30.2% | 40.1% | 8.9 | 8 | 11.9 | 55.9% | 29.3%/0.0%/70.7% | 2.87 | 0.03 |
| solo-random-casual | Random/casual | 1 | 3.1% | 10.8% | 18.9% | 11.7 | 12 | 9.0 | 56.1% | 41.4%/29.2%/29.4% | 1.23 | 0.06 |
| solo-sensible-casual | Sensible/casual | 1 | 18.4% | 34.1% | 46.4% | 7.7 | 6 | 13.0 | 56.1% | 44.5%/0.0%/55.5% | 1.71 | 0.04 |
| solo-chaser-casual | Chaser/casual | 1 | 19.0% | 33.8% | 44.4% | 8.2 | 7 | 12.6 | 56.1% | 41.0%/0.0%/59.0% | 1.99 | 0.04 |
| solo-cruise-strong | AlwaysCruise/strong | 1 | 0.0% | 0.1% | 1.0% | 13.1 | 13 | 7.5 | 77.6% | 100.0%/0.0%/0.0% | 0.00 | 0.10 |
| solo-push-strong | AlwaysPush/strong | 1 | 6.3% | 25.8% | 45.4% | 6.7 | 6 | 13.0 | 77.6% | 0.4%/99.6%/0.0% | 0.00 | 0.09 |
| solo-redline-strong | AlwaysRedline/strong | 1 | 61.8% | 77.9% | 85.7% | 2.8 | 1 | 18.4 | 77.4% | 18.2%/0.0%/81.8% | 1.44 | 0.01 |
| solo-random-strong | Random/strong | 1 | 19.9% | 42.7% | 58.5% | 5.8 | 4 | 14.5 | 77.6% | 38.0%/30.9%/31.0% | 0.65 | 0.06 |
| solo-sensible-strong | Sensible/strong | 1 | 62.4% | 78.1% | 85.9% | 2.8 | 1 | 18.5 | 77.6% | 18.0%/0.0%/82.0% | 1.42 | 0.01 |
| solo-chaser-strong | Chaser/strong | 1 | 62.0% | 78.1% | 85.7% | 2.8 | 1 | 18.4 | 77.6% | 18.1%/0.0%/81.9% | 1.43 | 0.01 |
| four-cruise | AlwaysCruise/casual | 2 | 0.0% | 0.0% | 0.1% | 15.8 | 16 | 5.6 | 56.2% | 100.0%/0.0%/0.0% | 0.00 | 0.09 |
| four-cruise | AlwaysCruise/strong | 2 | 0.0% | 0.3% | 2.0% | 12.0 | 12 | 7.7 | 77.6% | 100.0%/0.0%/0.0% | 0.00 | 0.10 |
| four-push | AlwaysPush/casual | 2 | 0.2% | 2.4% | 7.2% | 13.4 | 14 | 7.6 | 56.0% | 0.4%/99.6%/0.0% | 0.00 | 0.09 |
| four-push | AlwaysPush/strong | 2 | 6.6% | 27.8% | 47.8% | 6.6 | 6 | 13.1 | 77.5% | 0.4%/99.6%/0.0% | 0.00 | 0.10 |
| four-redline | AlwaysRedline/casual | 2 | 8.6% | 21.6% | 33.6% | 9.9 | 9 | 9.0 | 56.1% | 27.8%/0.0%/72.2% | 2.10 | 0.02 |
| four-redline | AlwaysRedline/strong | 2 | 45.3% | 67.9% | 79.3% | 3.7 | 2 | 16.0 | 77.6% | 17.2%/0.0%/82.8% | 1.23 | 0.01 |
| four-random | Random/casual | 2 | 2.1% | 8.5% | 16.3% | 12.2 | 12 | 8.4 | 56.0% | 41.4%/29.4%/29.2% | 1.16 | 0.06 |
| four-random | Random/strong | 2 | 17.5% | 40.7% | 56.3% | 6.0 | 5 | 14.2 | 77.6% | 37.9%/31.0%/31.1% | 0.62 | 0.06 |
| four-sensible | Sensible/casual | 2 | 6.0% | 23.5% | 36.5% | 9.1 | 8 | 9.6 | 56.1% | 44.0%/0.0%/56.0% | 1.24 | 0.03 |
| four-sensible | Sensible/strong | 2 | 45.2% | 67.2% | 78.5% | 3.8 | 2 | 16.1 | 77.5% | 17.3%/0.0%/82.7% | 1.25 | 0.01 |
| four-chaser | Chaser/casual | 2 | 6.4% | 23.8% | 35.8% | 9.4 | 8 | 9.4 | 56.3% | 39.5%/0.0%/60.5% | 1.45 | 0.03 |
| four-chaser | Chaser/strong | 2 | 45.3% | 67.2% | 78.6% | 3.8 | 2 | 16.1 | 77.7% | 17.3%/0.0%/82.7% | 1.24 | 0.01 |
| eight-cruise | AlwaysCruise/casual | 4 | 0.0% | 0.0% | 0.3% | 15.5 | 16 | 5.8 | 56.1% | 100.0%/0.0%/0.0% | 0.00 | 0.10 |
| eight-cruise | AlwaysCruise/strong | 4 | 0.1% | 0.7% | 4.8% | 10.6 | 10 | 8.0 | 77.6% | 100.0%/0.0%/0.0% | 0.00 | 0.10 |
| eight-push | AlwaysPush/casual | 4 | 0.2% | 2.5% | 7.1% | 13.7 | 14 | 7.7 | 56.1% | 0.4%/99.6%/0.0% | 0.00 | 0.09 |
| eight-push | AlwaysPush/strong | 4 | 7.1% | 29.1% | 49.4% | 6.5 | 6 | 13.4 | 77.6% | 0.4%/99.6%/0.0% | 0.00 | 0.10 |
| eight-redline | AlwaysRedline/casual | 4 | 6.6% | 15.7% | 27.3% | 10.8 | 10 | 8.0 | 56.2% | 27.1%/0.0%/72.9% | 1.84 | 0.02 |
| eight-redline | AlwaysRedline/strong | 4 | 34.8% | 54.9% | 70.6% | 4.7 | 3 | 14.1 | 77.7% | 16.3%/0.0%/83.7% | 1.07 | 0.01 |
| eight-random | Random/casual | 4 | 1.7% | 7.1% | 14.2% | 12.7 | 13 | 8.2 | 56.0% | 41.2%/29.5%/29.3% | 1.12 | 0.06 |
| eight-random | Random/strong | 4 | 13.8% | 35.8% | 52.2% | 6.5 | 5 | 13.6 | 77.5% | 37.8%/31.1%/31.1% | 0.61 | 0.06 |
| eight-sensible | Sensible/casual | 4 | 3.2% | 15.4% | 28.0% | 10.2 | 10 | 8.5 | 56.1% | 43.7%/0.0%/56.3% | 1.10 | 0.03 |
| eight-sensible | Sensible/strong | 4 | 34.7% | 54.1% | 69.1% | 4.8 | 3 | 14.3 | 77.6% | 16.5%/0.0%/83.5% | 1.10 | 0.01 |
| eight-chaser | Chaser/casual | 4 | 3.6% | 16.1% | 27.7% | 10.4 | 10 | 8.3 | 56.1% | 39.0%/0.0%/61.0% | 1.29 | 0.02 |
| eight-chaser | Chaser/strong | 4 | 34.7% | 53.7% | 69.4% | 4.8 | 3 | 14.2 | 77.6% | 16.5%/0.0%/83.5% | 1.09 | 0.01 |
| mixed-casual | AlwaysCruise/casual | 1 | 0.0% | 0.0% | 0.1% | 16.1 | 16 | 5.0 | 55.9% | 100.0%/0.0%/0.0% | 0.00 | 0.09 |
| mixed-casual | AlwaysPush/casual | 1 | 0.1% | 2.2% | 7.1% | 13.1 | 13 | 7.0 | 56.2% | 0.4%/99.6%/0.0% | 0.00 | 0.08 |
| mixed-casual | AlwaysRedline/casual | 1 | 16.0% | 29.1% | 39.8% | 9.0 | 8 | 11.5 | 56.0% | 29.0%/0.0%/71.0% | 2.76 | 0.03 |
| mixed-casual | Random/casual | 1 | 2.2% | 10.0% | 18.6% | 11.7 | 12 | 8.4 | 56.3% | 41.3%/29.4%/29.3% | 1.13 | 0.06 |
| mixed-casual | Sensible/casual | 1 | 15.9% | 33.2% | 46.1% | 7.8 | 6 | 12.4 | 56.0% | 44.4%/0.0%/55.6% | 1.64 | 0.04 |
| mixed-casual | Chaser/casual | 1 | 17.0% | 32.5% | 44.1% | 8.3 | 7 | 12.1 | 56.1% | 40.8%/0.0%/59.2% | 1.90 | 0.04 |
| mixed-strong | AlwaysCruise/strong | 1 | 0.0% | 0.0% | 0.4% | 14.3 | 14 | 4.8 | 77.7% | 100.0%/0.0%/0.0% | 0.00 | 0.06 |
| mixed-strong | AlwaysPush/strong | 1 | 0.1% | 6.6% | 23.1% | 8.9 | 8 | 8.6 | 77.6% | 0.4%/99.6%/0.0% | 0.00 | 0.06 |
| mixed-strong | AlwaysRedline/strong | 1 | 40.2% | 62.9% | 75.8% | 4.2 | 2 | 15.1 | 77.7% | 16.8%/0.0%/83.2% | 1.16 | 0.01 |
| mixed-strong | Random/strong | 1 | 2.7% | 22.1% | 39.8% | 7.9 | 7 | 9.9 | 77.6% | 37.6%/31.2%/31.2% | 0.44 | 0.04 |
| mixed-strong | Sensible/strong | 1 | 39.8% | 62.8% | 75.8% | 4.2 | 2 | 15.1 | 77.7% | 16.9%/0.0%/83.1% | 1.16 | 0.01 |
| mixed-strong | Chaser/strong | 1 | 39.8% | 62.8% | 76.0% | 4.2 | 2 | 15.1 | 77.6% | 16.8%/0.0%/83.2% | 1.16 | 0.01 |
| mixed-eight | Sensible/strong | 1 | 38.6% | 61.7% | 75.4% | 4.2 | 2 | 15.0 | 77.6% | 16.8%/0.0%/83.2% | 1.15 | 0.01 |
| mixed-eight | Sensible/casual | 1 | 4.2% | 19.0% | 32.5% | 9.5 | 8 | 8.9 | 56.1% | 44.1%/0.0%/55.9% | 1.14 | 0.03 |
| mixed-eight | Chaser/strong | 1 | 38.1% | 60.9% | 74.7% | 4.2 | 2 | 14.9 | 77.4% | 17.0%/0.0%/83.0% | 1.16 | 0.01 |
| mixed-eight | Chaser/casual | 1 | 4.4% | 19.7% | 32.7% | 9.7 | 9 | 8.8 | 56.2% | 39.3%/0.0%/60.7% | 1.35 | 0.02 |
| mixed-eight | AlwaysRedline/strong | 1 | 38.5% | 60.9% | 74.5% | 4.2 | 2 | 14.9 | 77.4% | 16.9%/0.0%/83.1% | 1.16 | 0.01 |
| mixed-eight | AlwaysRedline/casual | 1 | 7.4% | 18.6% | 30.8% | 10.3 | 10 | 8.4 | 56.1% | 27.5%/0.0%/72.5% | 1.96 | 0.02 |
| mixed-eight | Random/casual | 1 | 0.3% | 4.5% | 11.9% | 12.7 | 13 | 6.0 | 56.2% | 41.0%/29.5%/29.5% | 0.80 | 0.04 |
| mixed-eight | AlwaysCruise/casual | 1 | 0.0% | 0.0% | 0.1% | 16.3 | 17 | 3.5 | 56.1% | 100.0%/0.0%/0.0% | 0.00 | 0.06 |
| party-four | Sensible/strong | 1 | 55.6% | 73.1% | 82.0% | 3.2 | 1 | 17.8 | 77.4% | 18.0%/0.0%/82.0% | 1.38 | 0.01 |
| party-four | Sensible/casual | 3 | 8.9% | 26.5% | 39.1% | 8.8 | 8 | 10.6 | 56.1% | 44.1%/0.0%/55.9% | 1.38 | 0.03 |

### Head to head: four-cruise

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysCruise/casual | AlwaysCruise/strong | Win share |
|---|---:|---:|---:|
| AlwaysCruise/casual | – | 14.6% | 0.0% |
| AlwaysCruise/strong | 85.4% | – | 0.0% |

### Head to head: four-push

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysPush/casual | AlwaysPush/strong | Win share |
|---|---:|---:|---:|
| AlwaysPush/casual | – | 14.8% | 0.2% |
| AlwaysPush/strong | 85.2% | – | 6.6% |

### Head to head: four-redline

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysRedline/casual | AlwaysRedline/strong | Win share |
|---|---:|---:|---:|
| AlwaysRedline/casual | – | 20.1% | 8.6% |
| AlwaysRedline/strong | 79.9% | – | 45.3% |

### Head to head: four-random

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Random/casual | Random/strong | Win share |
|---|---:|---:|---:|
| Random/casual | – | 20.1% | 2.1% |
| Random/strong | 79.9% | – | 17.5% |

### Head to head: four-sensible

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Sensible/casual | Sensible/strong | Win share |
|---|---:|---:|---:|
| Sensible/casual | – | 21.3% | 6.0% |
| Sensible/strong | 78.7% | – | 45.2% |

### Head to head: four-chaser

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Chaser/casual | Chaser/strong | Win share |
|---|---:|---:|---:|
| Chaser/casual | – | 21.0% | 6.4% |
| Chaser/strong | 79.0% | – | 45.3% |

### Head to head: eight-cruise

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysCruise/casual | AlwaysCruise/strong | Win share |
|---|---:|---:|---:|
| AlwaysCruise/casual | – | 14.1% | 0.0% |
| AlwaysCruise/strong | 85.9% | – | 0.1% |

### Head to head: eight-push

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysPush/casual | AlwaysPush/strong | Win share |
|---|---:|---:|---:|
| AlwaysPush/casual | – | 14.3% | 0.2% |
| AlwaysPush/strong | 85.7% | – | 7.1% |

### Head to head: eight-redline

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysRedline/casual | AlwaysRedline/strong | Win share |
|---|---:|---:|---:|
| AlwaysRedline/casual | – | 22.1% | 6.6% |
| AlwaysRedline/strong | 77.9% | – | 34.8% |

### Head to head: eight-random

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Random/casual | Random/strong | Win share |
|---|---:|---:|---:|
| Random/casual | – | 20.9% | 1.7% |
| Random/strong | 79.1% | – | 13.8% |

### Head to head: eight-sensible

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Sensible/casual | Sensible/strong | Win share |
|---|---:|---:|---:|
| Sensible/casual | – | 23.0% | 3.2% |
| Sensible/strong | 77.0% | – | 34.7% |

### Head to head: eight-chaser

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Chaser/casual | Chaser/strong | Win share |
|---|---:|---:|---:|
| Chaser/casual | – | 23.0% | 3.6% |
| Chaser/strong | 77.0% | – | 34.7% |

### Head to head: mixed-casual

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysCruise/casual | AlwaysPush/casual | AlwaysRedline/casual | Random/casual | Sensible/casual | Chaser/casual | Win share |
|---|---:|---:|---:|---:|---:|---:|---:|
| AlwaysCruise/casual | – | 32.8% | 20.5% | 28.6% | 14.8% | 17.2% | 0.0% |
| AlwaysPush/casual | 67.2% | – | 31.1% | 42.9% | 24.9% | 27.3% | 0.1% |
| AlwaysRedline/casual | 79.5% | 68.9% | – | 62.5% | 45.6% | 46.9% | 16.0% |
| Random/casual | 71.4% | 57.1% | 37.5% | – | 31.7% | 33.8% | 2.2% |
| Sensible/casual | 85.2% | 75.1% | 54.4% | 68.3% | – | 51.9% | 15.9% |
| Chaser/casual | 82.8% | 72.7% | 53.1% | 66.2% | 48.1% | – | 17.0% |

### Head to head: mixed-strong

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | AlwaysCruise/strong | AlwaysPush/strong | AlwaysRedline/strong | Random/strong | Sensible/strong | Chaser/strong | Win share |
|---|---:|---:|---:|---:|---:|---:|---:|
| AlwaysCruise/strong | – | 15.0% | 7.3% | 14.8% | 7.3% | 7.5% | 0.0% |
| AlwaysPush/strong | 85.0% | – | 17.0% | 41.6% | 17.1% | 17.1% | 0.1% |
| AlwaysRedline/strong | 92.7% | 83.0% | – | 75.0% | 50.1% | 50.2% | 40.2% |
| Random/strong | 85.2% | 58.4% | 25.0% | – | 25.0% | 25.1% | 2.7% |
| Sensible/strong | 92.7% | 82.9% | 49.9% | 75.0% | – | 50.1% | 39.8% |
| Chaser/strong | 92.5% | 82.9% | 49.8% | 75.0% | 49.9% | – | 39.8% |

### Head to head: mixed-eight

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Sensible/strong | Sensible/casual | Chaser/strong | Chaser/casual | AlwaysRedline/strong | AlwaysRedline/casual | Random/casual | AlwaysCruise/casual | Win share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Sensible/strong | – | 78.1% | 50.4% | 78.1% | 50.1% | 78.9% | 88.7% | 96.1% | 38.6% |
| Sensible/casual | 21.9% | – | 22.7% | 50.8% | 22.6% | 53.3% | 65.5% | 80.6% | 4.2% |
| Chaser/strong | 49.6% | 77.3% | – | 77.5% | 49.7% | 78.6% | 88.3% | 96.1% | 38.1% |
| Chaser/casual | 21.9% | 49.2% | 22.6% | – | 22.7% | 52.6% | 64.2% | 79.1% | 4.4% |
| AlwaysRedline/strong | 49.9% | 77.5% | 50.3% | 77.3% | – | 78.3% | 88.6% | 96.1% | 38.5% |
| AlwaysRedline/casual | 21.1% | 46.7% | 21.4% | 47.4% | 21.7% | – | 61.3% | 76.0% | 7.4% |
| Random/casual | 11.3% | 34.5% | 11.7% | 35.8% | 11.4% | 38.7% | – | 68.9% | 0.3% |
| AlwaysCruise/casual | 3.9% | 19.4% | 3.9% | 20.9% | 3.9% | 24.0% | 31.1% | – | 0.0% |

### Head to head: party-four

P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.

| ↓ ahead of → | Sensible/strong | Sensible/casual | Win share |
|---|---:|---:|---:|
| Sensible/strong | – | 80.6% | 55.6% |
| Sensible/casual | 19.4% | – | 8.9% |

### AI cars by aiSkill tier (top ≥ 1.08, back ≤ 0.90)

| Scenario | AI wins | won by top tier | by mid | by back | Top-tier mean pos | Mid mean pos | Back mean pos | AI Redline share | AI spin-outs/car/race | AI in chase mode | Best AI dist |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ai-only | 100.0% | 69.0% | 25.8% | 5.2% | 8.2 | 11.6 | 13.6 | 32.6% | 1.11 | 5.5% | 21.0 |
| solo-cruise-casual | 100.0% | 69.0% | 25.7% | 5.3% | 8.0 | 11.4 | 13.5 | 32.6% | 1.12 | 5.8% | 21.0 |
| solo-push-casual | 99.7% | 68.9% | 25.7% | 5.3% | 8.1 | 11.5 | 13.6 | 32.6% | 1.12 | 5.7% | 21.0 |
| solo-redline-casual | 82.3% | 68.7% | 25.7% | 5.6% | 8.4 | 11.7 | 13.7 | 32.5% | 1.06 | 4.5% | 20.2 |
| solo-random-casual | 96.9% | 68.6% | 26.0% | 5.4% | 8.2 | 11.6 | 13.6 | 32.7% | 1.11 | 5.3% | 20.9 |
| solo-sensible-casual | 81.6% | 69.3% | 25.7% | 5.1% | 8.4 | 11.8 | 13.8 | 32.5% | 1.08 | 5.0% | 20.4 |
| solo-chaser-casual | 81.0% | 69.6% | 25.0% | 5.3% | 8.4 | 11.8 | 13.7 | 32.5% | 1.07 | 4.3% | 20.4 |
| solo-cruise-strong | 100.0% | 68.9% | 26.1% | 5.0% | 8.1 | 11.6 | 13.6 | 32.6% | 1.12 | 5.7% | 21.0 |
| solo-push-strong | 93.7% | 69.5% | 25.5% | 5.0% | 8.4 | 11.9 | 13.9 | 32.6% | 1.11 | 5.3% | 20.9 |
| solo-redline-strong | 38.2% | 72.0% | 23.7% | 4.3% | 8.9 | 12.0 | 13.7 | 32.1% | 0.88 | 1.9% | 17.6 |
| solo-random-strong | 80.1% | 70.0% | 25.2% | 4.8% | 8.5 | 11.9 | 13.9 | 32.3% | 1.06 | 4.2% | 20.3 |
| solo-sensible-strong | 37.6% | 71.4% | 24.1% | 4.5% | 8.9 | 11.9 | 13.7 | 32.1% | 0.88 | 1.9% | 17.5 |
| solo-chaser-strong | 38.0% | 72.0% | 23.8% | 4.3% | 8.8 | 12.0 | 13.7 | 32.1% | 0.88 | 1.9% | 17.6 |
| four-cruise | 100.0% | 66.7% | 27.1% | 6.1% | 7.6 | 11.0 | 13.2 | 32.8% | 1.15 | 6.2% | 21.0 |
| four-push | 87.0% | 68.1% | 26.3% | 5.6% | 8.5 | 11.9 | 14.0 | 32.5% | 1.12 | 5.5% | 20.6 |
| four-redline | 12.3% | 74.2% | 22.5% | 3.3% | 9.7 | 12.4 | 14.1 | 31.8% | 0.74 | 0.7% | 15.2 |
| four-random | 61.1% | 69.6% | 25.5% | 4.8% | 8.8 | 12.2 | 14.1 | 32.3% | 1.03 | 3.5% | 19.4 |
| four-sensible | 12.1% | 74.2% | 21.6% | 4.2% | 9.7 | 12.6 | 14.2 | 31.9% | 0.75 | 0.7% | 15.4 |
| four-chaser | 11.8% | 74.4% | 21.7% | 4.0% | 9.7 | 12.5 | 14.1 | 32.0% | 0.75 | 0.7% | 15.3 |
| eight-cruise | 99.8% | 64.0% | 28.7% | 7.3% | 6.9 | 10.4 | 12.7 | 32.8% | 1.21 | 7.1% | 20.9 |
| eight-push | 74.4% | 67.6% | 27.1% | 5.3% | 8.7 | 12.3 | 14.3 | 32.5% | 1.14 | 5.6% | 20.2 |
| eight-redline | 2.6% | 77.5% | 21.0% | 1.5% | 10.8 | 13.3 | 14.7 | 31.9% | 0.65 | 0.1% | 13.3 |
| eight-random | 39.0% | 69.5% | 25.9% | 4.6% | 9.4 | 12.6 | 14.5 | 32.0% | 0.98 | 2.3% | 18.2 |
| eight-sensible | 2.5% | 75.1% | 21.3% | 3.6% | 10.9 | 13.5 | 14.9 | 31.9% | 0.66 | 0.1% | 13.4 |
| eight-chaser | 2.3% | 78.8% | 19.2% | 2.0% | 10.9 | 13.4 | 14.9 | 31.9% | 0.66 | 0.1% | 13.3 |
| mixed-casual | 50.2% | 68.7% | 26.1% | 5.2% | 8.4 | 11.7 | 13.7 | 32.4% | 1.02 | 4.2% | 18.8 |
| mixed-strong | 6.7% | 75.0% | 21.8% | 3.3% | 10.3 | 13.0 | 14.6 | 31.9% | 0.70 | 0.3% | 14.3 |
| mixed-eight | 4.7% | 75.8% | 20.9% | 3.2% | 10.1 | 12.8 | 14.3 | 31.8% | 0.69 | 0.3% | 13.9 |
| party-four | 22.3% | 72.9% | 23.1% | 4.1% | 9.4 | 12.4 | 14.1 | 32.0% | 0.84 | 1.7% | 16.7 |

### Wall-clock game time (minutes)

Measured = the harness clock from Start to EndGame with the bots' reaction times (early advance as the code does it). All timers = every phase runs to its timer for the same number of rounds.

| Scenario | Measured mean | p10 | p90 | All timers mean | p90 | Saved by early advance | Throttle timeouts | Answer timeouts |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ai-only | 6.5 | 4.9 | 8.6 | 6.5 | 8.6 | 0.0% | – | – |
| solo-cruise-casual | 3.3 | 2.3 | 4.4 | 6.6 | 8.6 | 50.0% | 0.4% | 1.0% |
| solo-push-casual | 3.3 | 2.3 | 4.4 | 6.5 | 8.6 | 50.0% | 0.4% | 1.0% |
| solo-redline-casual | 3.0 | 2.1 | 4.0 | 6.3 | 8.6 | 52.2% | 0.4% | 1.0% |
| solo-random-casual | 3.2 | 2.3 | 4.2 | 6.5 | 8.6 | 51.0% | 0.4% | 0.9% |
| solo-sensible-casual | 3.1 | 2.2 | 4.1 | 6.3 | 8.6 | 51.3% | 0.4% | 1.0% |
| solo-chaser-casual | 3.1 | 2.2 | 4.1 | 6.3 | 8.6 | 51.5% | 0.4% | 0.9% |
| solo-cruise-strong | 3.3 | 2.3 | 4.4 | 6.5 | 8.6 | 50.0% | 0.4% | 1.0% |
| solo-push-strong | 3.3 | 2.3 | 4.3 | 6.5 | 8.6 | 50.0% | 0.4% | 1.0% |
| solo-redline-strong | 2.6 | 1.8 | 3.5 | 5.4 | 7.4 | 51.1% | 0.4% | 1.0% |
| solo-random-strong | 3.1 | 2.3 | 4.1 | 6.3 | 8.0 | 50.4% | 0.4% | 1.0% |
| solo-sensible-strong | 2.6 | 1.8 | 3.5 | 5.4 | 7.4 | 51.1% | 0.4% | 1.0% |
| solo-chaser-strong | 2.6 | 1.8 | 3.5 | 5.4 | 7.4 | 51.1% | 0.4% | 1.0% |
| four-cruise | 4.3 | 3.1 | 5.8 | 6.7 | 9.2 | 35.5% | 0.4% | 1.0% |
| four-push | 4.2 | 3.1 | 5.5 | 6.6 | 8.6 | 35.6% | 0.4% | 1.0% |
| four-redline | 3.0 | 2.2 | 4.1 | 4.7 | 6.8 | 36.2% | 0.4% | 1.0% |
| four-random | 4.0 | 2.9 | 5.1 | 6.2 | 8.0 | 35.8% | 0.4% | 1.0% |
| four-sensible | 3.0 | 2.2 | 4.1 | 4.7 | 6.8 | 35.9% | 0.4% | 1.0% |
| four-chaser | 3.0 | 2.3 | 4.1 | 4.7 | 6.2 | 35.9% | 0.4% | 1.0% |
| eight-cruise | 5.0 | 3.5 | 6.7 | 6.9 | 9.2 | 28.1% | 0.4% | 1.0% |
| eight-push | 4.8 | 3.5 | 6.1 | 6.7 | 8.6 | 28.1% | 0.4% | 1.0% |
| eight-redline | 3.0 | 2.5 | 4.0 | 4.2 | 5.5 | 28.7% | 0.4% | 1.0% |
| eight-random | 4.3 | 3.3 | 5.4 | 6.0 | 7.4 | 28.3% | 0.4% | 1.0% |
| eight-sensible | 3.0 | 2.5 | 4.0 | 4.2 | 5.5 | 28.4% | 0.4% | 1.0% |
| eight-chaser | 3.0 | 2.5 | 4.0 | 4.2 | 5.5 | 28.4% | 0.4% | 1.0% |
| mixed-casual | 4.1 | 2.8 | 5.6 | 6.1 | 8.0 | 31.6% | 0.4% | 1.0% |
| mixed-strong | 3.1 | 2.4 | 4.1 | 4.4 | 5.5 | 31.2% | 0.4% | 1.0% |
| mixed-eight | 3.2 | 2.5 | 4.2 | 4.4 | 5.5 | 28.3% | 0.4% | 1.0% |
| party-four | 3.3 | 2.3 | 4.5 | 5.2 | 6.8 | 35.9% | 0.4% | 1.0% |

