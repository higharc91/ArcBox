# Milestone A — AI hazard avoidance

Issue #69. Branch `environmental-tiles`; baseline `340e1de5`, runtime change `d8714796`, reproducible crash harness `cdace94e`.
This milestone contains no surfaces, encounter opt-ins, art, audio or balance-content edits. The owner merges A alone to
`ccr-18ad4510-q68nad` after reviewing this report. Milestone C remains gated on that review and merge.

## Behavior and numbers

The planner now charges expected damage from `onEnterTile` along its existing retained paths, `onTurnEnd` at the destination,
and the next `onTurnStart`. It uses the actual zone caster's save DC, exact dice distributions, resistances/immunities,
applicable hook predicates, and shared per-turn/per-round/per-battle limits. The next-turn projection resets turn and round
limits as appropriate. It neither draws RNG nor changes battle state. The existing path search and 12-position pruning stay intact.

In `games/Tactics/Tactics.Logic/Ai/Planner.Positions.cs`, hazard cost uses the existing threat urgency convention:
`profile.Caution × expectedDamage × Clamp(2 × expectedDamage / Max(1, HP + temporary HP), 0.5, 2)`.
The projected damage itself lives in `Ai/Scoring.Hazards.cs`. These are reused AI weighting constants, not new damage,
durations, profile weights or content numbers. Known ground damage does not fade with the late-battle wariness term.
Staying and entering damage are combined without charging destination urgency twice.

## Validation

[Validation counts and source, assembly and golden SHA256 values](ai-validation.json).

- Full Release solution: **5,727 passed, 0 failed**; build **0 warnings**. Web: **478 passed**.
- Planner scenarios: **33 passed**, including 18 new hazard cases. Five regression cases were also run against the immutable
  baseline Logic assembly and failed there, then passed with the change.
- All four AI performance cases passed the **250 ms p95** budget: hazard-heavy stress **128.7 ms**, ordinary stress **80.2 ms**,
  crypt **35.1 ms**, crystal **32.9 ms**. This measures the unchanged runtime source used by the after simulation.
- All six existing golden replay/event files remain byte-identical. No regeneration was necessary: these fixtures do not
  place damaging zones on the AI's choices (Warlord Bridge uses Entangle). The scenario tests exercise the changed decisions.
- Crash sweep: **16,200 cases, 0 crashes, 0 failures to start**; **228 unfinished** at the harness step limit. Details below.

## Before/after simulation

[Raw before report](ai-baseline.md) · [Raw after report](ai-after.md). Each run contains **16,200 battles**: all nine battle-list
encounters × levels **1, 3, 5, 7, 10** (the README matrix) × six sampled parties of each size **4, 5, 6** × seeds **1–20**.
Both use Normal difficulty, the same deterministic party compositions, and a 30-round cap. Baseline and after assemblies
were copied to separate immutable directories before running; neither used the environmental-core checkout's content.
Commands and the full party list are in each raw report.

Wins changed **10,924 → 10,909** (**67.43% → 67.34%, −0.09 percentage points**). Mean rounds remains **8.9 at the report's
one-decimal precision**. Capped losses changed **57 → 54**. Battles in the 8–12-round target changed **42.6% → 42.3%**.

The automated intended-level win flag remains Crystal Cave: **95.8% → 95.6%**, above 95% before and after. Crypt Hall's
intended-level mean remains long (**12.2 → 12.1**). No tested cell crossed the rounds target boundary at the report’s precision. Keep Gate level 7
crossed the win floor (**60.3% → 59.2%**), but its authored intended level is **9**, which is outside the prescribed matrix.
The largest win movement is Hall of the Flame level 5 (**12.8% → 9.7%**); its authored level is 10 and that cell stays **70.8%**.

At authored intended levels:

| Encounter | Level | Wins before → after | Mean rounds before → after |
|---|---:|---:|---:|
| crypt_hall | 5 | 93.3% → 93.3% | 12.2 → 12.1 |
| crystal_cave | 7 | 95.8% → 95.6% | 11.7 → 11.7 |
| ford_assault | 1 | 83.3% → 83.3% | 8.4 → 8.4 |
| forest_ruins | 3 | 88.1% → 88.1% | 10.6 → 10.6 |
| hall_of_the_flame | 10 | 70.8% → 70.8% | 8.7 → 8.7 |
| keep_gate | 9 | Not in prescribed matrix | Not measured |
| rime_tower | 7 | 92.8% → 92.5% | 11.3 → 11.1 |
| river_crossing | 1 | 90.3% → 90.3% | 9.5 → 9.5 |
| warlord_bridge | 5 | 89.7% → 89.7% | 10.9 → 10.9 |

The table below flags **every** grid cell outside 60–95% wins or 8–12 mean rounds, including deliberately under/over-level
parties. The raw report's win/length judgments are narrower: they judge only intended-level cells. Its length flags also include capped battles, even when the mean is in band. Differences in mean rounds below
use the raw report's rounded values, not unreported precision.

| Encounter | Level | Wins before → after / 360 | Δ pp | Mean rounds before → after | Δ rounds | After outside bands |
|---|---:|---:|---:|---:|---:|---|
| crypt_hall | 1 | 8 → 8 | +0.00 | 7.1 → 7.1 | +0.0 | wins low, short |
| crypt_hall | 3 | 102 → 102 | +0.00 | 12.8 → 12.8 | +0.0 | wins low, long |
| crypt_hall | 5 | 336 → 336 | +0.00 | 12.2 → 12.1 | -0.1 | long |
| crypt_hall | 7 | 350 → 350 | +0.00 | 11.0 → 10.9 | -0.1 | wins high |
| crypt_hall | 10 | 360 → 360 | +0.00 | 8.3 → 8.3 | +0.0 | wins high |
| crystal_cave | 1 | 0 → 0 | +0.00 | 6.7 → 6.7 | +0.0 | wins low, short |
| crystal_cave | 3 | 78 → 78 | +0.00 | 11.8 → 11.8 | +0.0 | wins low |
| crystal_cave | 5 | 314 → 314 | +0.00 | 12.5 → 12.7 | +0.2 | long |
| crystal_cave | 7 | 345 → 344 | -0.28 | 11.7 → 11.7 | +0.0 | wins high |
| crystal_cave | 10 | 358 → 358 | +0.00 | 8.2 → 8.2 | +0.0 | wins high |
| ford_assault | 1 | 300 → 300 | +0.00 | 8.4 → 8.4 | +0.0 |  |
| ford_assault | 3 | 359 → 359 | +0.00 | 5.3 → 5.3 | +0.0 | wins high, short |
| ford_assault | 5 | 360 → 360 | +0.00 | 4.8 → 4.8 | +0.0 | wins high, short |
| ford_assault | 7 | 360 → 360 | +0.00 | 4.6 → 4.6 | +0.0 | wins high, short |
| ford_assault | 10 | 360 → 360 | +0.00 | 3.6 → 3.6 | +0.0 | wins high, short |
| forest_ruins | 1 | 133 → 133 | +0.00 | 10.1 → 10.1 | +0.0 | wins low |
| forest_ruins | 3 | 317 → 317 | +0.00 | 10.6 → 10.6 | +0.0 |  |
| forest_ruins | 5 | 360 → 360 | +0.00 | 8.2 → 8.2 | +0.0 | wins high |
| forest_ruins | 7 | 360 → 360 | +0.00 | 7.6 → 7.5 | -0.1 | wins high, short |
| forest_ruins | 10 | 360 → 360 | +0.00 | 5.9 → 5.9 | +0.0 | wins high, short |
| hall_of_the_flame | 1 | 0 → 0 | +0.00 | 2.0 → 2.0 | +0.0 | wins low, short |
| hall_of_the_flame | 3 | 4 → 4 | +0.00 | 5.3 → 5.3 | +0.0 | wins low, short |
| hall_of_the_flame | 5 | 46 → 35 | -3.06 | 8.9 → 9.0 | +0.1 | wins low |
| hall_of_the_flame | 7 | 111 → 111 | +0.00 | 10.5 → 10.4 | -0.1 | wins low |
| hall_of_the_flame | 10 | 255 → 255 | +0.00 | 8.7 → 8.7 | +0.0 |  |
| keep_gate | 1 | 0 → 0 | +0.00 | 7.3 → 7.3 | +0.0 | wins low, short |
| keep_gate | 3 | 7 → 7 | +0.00 | 10.8 → 10.8 | +0.0 | wins low |
| keep_gate | 5 | 113 → 113 | +0.00 | 13.9 → 13.7 | -0.2 | wins low, long |
| keep_gate | 7 | 217 → 213 | -1.11 | 13.3 → 13.4 | +0.1 | wins low, long |
| keep_gate | 10 | 335 → 335 | +0.00 | 9.7 → 9.7 | +0.0 |  |
| rime_tower | 1 | 0 → 0 | +0.00 | 8.1 → 8.1 | +0.0 | wins low |
| rime_tower | 3 | 44 → 44 | +0.00 | 14.7 → 14.7 | +0.0 | wins low, long |
| rime_tower | 5 | 295 → 297 | +0.56 | 12.5 → 12.9 | +0.4 | long |
| rime_tower | 7 | 334 → 333 | -0.28 | 11.3 → 11.1 | -0.2 |  |
| rime_tower | 10 | 359 → 359 | +0.00 | 8.3 → 8.3 | +0.0 | wins high |
| river_crossing | 1 | 325 → 325 | +0.00 | 9.5 → 9.5 | +0.0 |  |
| river_crossing | 3 | 360 → 360 | +0.00 | 6.6 → 6.6 | +0.0 | wins high, short |
| river_crossing | 5 | 360 → 360 | +0.00 | 5.6 → 5.6 | +0.0 | wins high, short |
| river_crossing | 7 | 360 → 360 | +0.00 | 5.4 → 5.5 | +0.1 | wins high, short |
| river_crossing | 10 | 360 → 360 | +0.00 | 4.7 → 4.7 | +0.0 | wins high, short |
| warlord_bridge | 1 | 231 → 231 | +0.00 | 10.0 → 10.0 | +0.0 |  |
| warlord_bridge | 3 | 263 → 263 | +0.00 | 10.4 → 10.4 | +0.0 |  |
| warlord_bridge | 5 | 323 → 323 | +0.00 | 10.9 → 10.9 | +0.0 |  |
| warlord_bridge | 7 | 344 → 344 | +0.00 | 10.7 → 10.7 | +0.0 | wins high |
| warlord_bridge | 10 | 358 → 358 | +0.00 | 9.9 → 9.8 | -0.1 | wins high |

## Crash sweep

[Grouped outcomes](ai-sweep.tsv) · [Every seed and fingerprint](ai-sweep.cases.tsv).
The reproducible harness in `tools/tactics-playtest/hazard-sweep` ran all nine encounters with **100 seeds** for each
combination of **1, 2, 3, 4 or 6** players, **zero or one absent** player (one absent only for groups of at least two), and
**pass or timeout** inputs. Rules invariants were enabled. Elapsed time: **1h 19m 38s**.

**0 crashes; 0 battles failed to start.** Outcomes: **8,460 won, 7,512 lost, 0 fled**. The **228 unfinished** cases were still in battle after
**1,500 synthetic half-second steps**. They are reported separately, not counted as wins, losses or crashes. This harness
has players pass or let timers expire; it is not the balanced-party simulator above, and its unfinished total has not
been compared against a baseline sweep. Its per-case file preserves every reproduction seed.

| Encounter | Unfinished cases |
|---|---:|
| crypt_hall | 25 |
| crystal_cave | 26 |
| ford_assault | 73 |
| forest_ruins | 7 |
| keep_gate | 5 |
| rime_tower | 19 |
| river_crossing | 73 |

The case-file columns are encounter, player count, absent count, mode, seed, outcome, fingerprint and error message. Empty error messages are quoted as `""` to keep the TSV field without trailing whitespace.

## Questions for the owner

1. Accept the hazard avoidance changes at these measured rates, including the unchanged intended-level Crystal Cave high
   win rate and Crypt Hall long mean? No content or profile tuning has been applied.
2. Does Keep Gate need a separate level-9 balance run before a later content milestone? The required README grid does not
   measure that intended level; level 7's newly low win rate is recorded above.

After A's report has been read and A merged by the owner's session, milestone C may start. This is the explicit gate in
[the agreed build plan](../../plans/2026-11-02-tactics-environmental-tiles.md#milestones), not a new approval requirement.
