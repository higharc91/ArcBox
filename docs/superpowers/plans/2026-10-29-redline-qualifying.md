# REDLINE Qualifying Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three easy, timed questions before the grid; the field lines up by correct answers, then total time.

**Architecture:**
- **Pure logic:** timing clamp, classification and AI laps live in `Redline.Logic/Race/Qualifying.cs`. `RedlineGame` only adds the three phases and their views.
- **SDK:** `ChoiceInput` gains an optional `ElapsedMs`; the phone's generic `Choice` widget measures it.
- **Presentation:** `Redline.Presentation` maps the new host-view data to tower rows, cues, commentary and shots (tested). Godot draws them.

**Tech Stack:** .NET 10 (xUnit via Microsoft.Testing.Platform), Vite + TS + Preact (vitest), Godot 4.7.2 .NET.

**Spec:** `docs/superpowers/specs/2026-10-29-redline-qualifying-design.md` (read it first; every value below comes from it).

## Global Constraints
- **Commands:**
  - .NET tests: `dotnet test --solution ArcBox.slnx`; one project with `--project <csproj>`, one class with `--filter-class <FQN>`.
  - Web: `cd web/play && npx vitest run`.
- **Determinism:** all game randomness comes from `ctx.Random`; nothing reads the wall clock except through `ctx.Now`.
- **Secrecy:** no lap time, correctness or answer index reaches the TV or a phone before `QualifyingResult`. `HostViews` enforces it, as it does for the race.
- **Existing behaviour:** existing tests must stay green without edits.
  - `RedlineKit.Start` turns `Qualify` off by default, like `reading` does for `ReadQuestions`.
  - The race's 15 questions are taken first, so `FakeQuestionSource` numbering (Q1..Q15) is unchanged.
- **Spec values:**

  | Value | Setting |
  |---|---|
  | Laps | `QualifyingLaps` 3 |
  | Answer window | `QualifyingAnswer` 10 s |
  | Result screen | `QualifyingResult` 5 s |
  | Lower clamp | `max(200 ms, s − 1000 ms)` |
  | AI time medians | 3.6 s at skill 0.85 → 1.9 s at 1.15 |
  | AI time spread | σ 0.25 |
  | AI time clamp | 0.6 s .. window |

- **Commits:** end with the session's `Co-Authored-By` and `Claude-Session` lines; no model names.

## Review Focus
1. **A player answers in the last ~50 ms of the window and the answer arrives after the timer fired.** It's ignored, the lap is deleted, and nothing throws (Task 3: `Answer_after_the_window_is_ignored`).
2. **A phone sends `elapsedMs` as NaN, negative or huge** (bad JSON or a broken clock). It's clamped into `[lower, s]`; non-finite counts as absent (Task 2: `LapTime_handles_non_finite_and_negative`).
3. **A player disconnects mid-qualifying and reconnects.** They see the current qualifying view, and their earlier laps are kept (Task 3: `Reconnect_mid_qualifying_shows_current_lap_and_keeps_laps`).
4. **Every player answers wrong on every lap.** The players are classified behind all AI cars with at least one correct lap, in the seeded tie order, and the race starts normally (Task 2: `All_deleted_laps_fall_back_to_the_seeded_order`).
5. **Fewer than 3 easy questions are available from OpenTDB and the fallback pack runs low** (after many rooms). The source still returns exactly `count`, repeating fallback questions rather than failing (Task 1: `Easy_take_wraps_the_fallback_pack`).

---

### Task 1: Easy-only questions

**Files:**
- Modify: `games/Redline/Redline.Logic/Questions/IQuestionSource.cs`, `src/ArcBox.Server/Games/Redline/{OpenTdbQuestionPool.cs, PooledQuestionSource.cs, NarratedQuestionSource.cs}`, `games/Redline/tools/Redline.Sim/SyntheticQuestionSource.cs`
- Test: `src/ArcBox.Server.Tests/Redline/{OpenTdbQuestionPoolTests.cs, PooledQuestionSourceTests.cs}`, `games/Redline/Redline.Logic.Tests/FallbackQuestionsTests.cs`

**Interfaces:**
- Produces:
  - `IReadOnlyList<TriviaQuestion> IQuestionSource.Take(int count, Difficulty difficulty)`: a default interface method returning `Take(count)`.
  - `int OpenTdbQuestionPool.TryTake(int count, List<TriviaQuestion> into, Difficulty difficulty)`.

- [ ] **Step 1: Write the failing tests**
  - `Pool_TryTake_by_difficulty_keeps_only_matches_and_requeues_the_rest`:
    - Queue E, M, E, H, E; `TryTake(2, into, Easy)`.
    - Expect `into` == first two E, `Count` == 3, and a later plain `TryTake(3)` returns M, H, E (the rest in their order).
  - `Pool_TryTake_by_difficulty_scans_at_most_Count_items`: an all-Medium queue of 5 returns 0 and `Count` stays 5 (no infinite loop).
  - `Pool_concurrent_easy_takes_never_duplicate`: 8 parallel `TryTake(3, …, Easy)` over 100 mixed questions; the union has no duplicate texts.
  - `Pooled_Take_easy_fills_shortfall_from_the_fallback_pack_easy_questions`: pool disabled; `Take(3, Easy)` returns 3, all `Difficulty.Easy`.
  - `Easy_take_wraps_the_fallback_pack` (Review Focus 5): `Take(25, Easy)` with the pool disabled returns 25 easy questions.
  - `Default_interface_Take_by_difficulty_returns_plain_Take`: a minimal `IQuestionSource` (Logic tests).

- [ ] **Step 2: Run, expect FAIL** (methods missing).
  Run: `dotnet test --project src/ArcBox.Server.Tests/ArcBox.Server.Tests.csproj`

- [ ] **Step 3: Implement**
  - **Pool:** dequeue up to `Count` items (the count read at entry). Keep matches until `count` is reached; enqueue non-matches back at the tail.
  - **Pooled:** override with the pool first, then fallback easy. Fallback easy uses its own shuffled cursor over the easy subset of `FallbackQuestions.All`, guarded by `_gate`, wrapping and reshuffling like `TakeFallback`.
  - **Narrated:** override to pass through to `inner.Take(count, difficulty)` and prewarm like `Take`.
  - **Synthetic (sim):** override to return easy questions.

- [ ] **Step 4: Run, expect PASS** (both test projects).

- [ ] **Step 5: Commit** `feat(redline): easy-only question takes for qualifying`

### Task 2: Qualifying rules and maths (pure)

**Files:**
- Create: `games/Redline/Redline.Logic/Race/Qualifying.cs`
- Modify: `games/Redline/Redline.Logic/RedlineRules.cs` (rules, `AiRules`), `games/Redline/Redline.Logic/Race/Driver.cs`
- Test: `games/Redline/Redline.Logic.Tests/QualifyingMathTests.cs`

**Interfaces:**
- Produces:
  - **`RedlineRules`:** `bool Qualify = true`, `int QualifyingLaps = 3`, `TimeSpan QualifyingAnswer = 10 s`, `TimeSpan QualifyingResult = 5 s`.
  - **`AiRules`:** `double QualiSlowMedian = 3.6`, `double QualiFastMedian = 1.9`, `double QualiSigma = 0.25` (seconds).
  - **`Driver`:**
    - `List<int?> QualiLaps`: one entry per completed lap; null = deleted.
    - `int? PendingLapMs`, `int? PendingAnswer`: the current lap, before Result.
    - `int QualiCorrect`, `int QualiTotalMs`.
    - `int QualiTieOrder`: the seeded order.
  - **`static class Qualifying`:**
    - `int LapTimeMs(double? reportedMs, int serverMs)`: the spec §2.1 clamp; non-finite or null → `serverMs`.
    - `(bool Correct, int? Ms) AiLap(double aiSkill, int correctIndex, RedlineRules rules, Random random)`: Ms null when it reaches the window (no answer). Correctness uses `AiPolicy.Accuracy(aiSkill, Easy, rules.Ai)`.
    - `IReadOnlyList<Driver> Classify(IEnumerable<Driver> field)`: `QualiCorrect` desc, `QualiTotalMs` asc, `QualiTieOrder` asc.
    - `string? FastestLap(IEnumerable<Driver> classified)`: the code with the lowest correct lap of the latest lap; ties go to the better classified.

- [ ] **Step 1: Write the failing tests**
  - `LapTime_uses_phone_time_inside_the_window`: `LapTimeMs(2410, 2600)` == 2410.
  - `LapTime_clamps_to_server_time`: `LapTimeMs(3000, 2600)` == 2600.
  - `LapTime_lower_bound_is_server_minus_one_second`: `LapTimeMs(500, 2600)` == 1600.
  - `LapTime_lower_bound_never_below_200ms`: `LapTimeMs(50, 900)` == 200 (also when `s − 1000 < 200`).
  - `LapTime_without_phone_time_is_server_time`: `LapTimeMs(null, 2600)` == 2600.
  - `LapTime_handles_non_finite_and_negative` (Review Focus 2): NaN → 2600, +∞ → 2600, −50 → 1600.
  - `AiLap_medians_follow_skill`: 4000 draws, seed 7, skill 0.85 and 1.15, `Accuracy` forced to 1 via `AiRules { MinP = 1, MaxP = 1 }`. The median Ms is within 5% of 3600 and 1900.
  - `AiLap_accuracy_matches_AiPolicy_for_easy`: 4000 draws at skill 1.0; the correct rate is within 0.03 of 0.72.
  - `AiLap_is_deterministic_for_a_seed`.
  - `Classify_orders_by_correct_then_total_then_tie_order`: hand-built drivers covering (3, 7000), (3, 6500), (2, 3000), and (0, 0) × 2 with tie orders 5 and 2. Expected order: 6500, 7000, 3000, tie 2, tie 5.
  - `All_deleted_laps_fall_back_to_the_seeded_order` (Review Focus 4).
  - `FastestLap_ignores_deleted_laps_and_breaks_ties_by_classification`.

- [ ] **Step 2: Run, expect FAIL.** `--filter-class Redline.Logic.Tests.QualifyingMathTests`

- [ ] **Step 3: Implement**
  - **AI time:** log-normal `median × exp(σ·z)`, with z from Box–Muller over `random.NextDouble()`, clamped to [600, window ms].
  - **AI median:** linear in skill between the two medians, clamped at the ends.

- [ ] **Step 4: Run, expect PASS.**

- [ ] **Step 5: Commit** `feat(redline): qualifying lap timing, AI laps and classification`

### Task 3: Qualifying phases in `RedlineGame`

**Files:**
- Modify:
  - `games/Redline/Redline.Logic/RedlineGame.cs`: Start, DriverSelect end, OnInput / OnTimer / connection, views.
  - `games/Redline/Redline.Logic/Views/HostView.cs`: enum values, records, `HostViews.Build`.
  - `games/Redline/Redline.Logic/Views/PlayerViews.cs`: qualifying views and the `PlayerViewKey` fields.
  - `games/Redline/Redline.Logic.Tests/Support/RedlineKit.cs`: the `qualify` flag and helpers.
- Test: `games/Redline/Redline.Logic.Tests/QualifyingTests.cs`

**Interfaces:**
- Consumes: Task 1 `Take(int, Difficulty)`; everything Task 2 produces.
- Produces:
  - **Phases:** `RacePhase` adds `QualifyingReading`, `QualifyingQuestion` and `QualifyingResult`, inserted after `DriverSelect` in the enum order.
  - **Host-view records:** exactly spec §5. `RedlineHostView` gets the new last parameter `Qualifying? Qualifying = null`. `Qualifying(int Lap, int Laps, IReadOnlyList<QualifyingRow> Rows)` and `QualifyingRow(string Code, int Position, int PositionChange, int Correct, int TotalMs, int? LastLapMs, bool Answered, bool FastestLap)`.
  - **Header:** the phone header text is `QUALIFYING · LAP n/3`.
  - **`RedlineKit`:**
    - `Start(..., bool qualify = false)`.
    - `QualiAnswer(h, player, bool right, double? elapsedMs)`: sends `{ selected, elapsedMs }`.
    - `SeatAllAndGoToQualifying(h, codes)`.

- [ ] **Step 1: Write the failing tests**
  - **Flow:**
    - `DriverSelect_goes_to_qualifying_when_on`.
    - `Qualify_off_keeps_the_seeded_shuffle_grid`: same standings as before for seed 1.
    - `AI_only_room_skips_qualifying`.
    - `Reading_then_question_then_result_three_times_then_grid`.
    - `ReadQuestions_off_skips_QualifyingReading`.
  - **Questions:**
    - `Qualifying_uses_three_easy_questions_after_the_race_fifteen`: the fake records the difficulty asked for, and the race questions are still Q1..Q15.
  - **Answering:**
    - `Question_ends_early_when_every_connected_player_answered`.
    - `First_answer_counts_later_ignored`.
    - `Answer_after_the_window_is_ignored` (Review Focus 1).
    - `Unparseable_answer_is_ignored`.
  - **Timing:** `Phone_time_is_clamped_against_server_time`. Advance 2.6 s, send 500 → lap 1600 shown in Result.
  - **Deleted laps:** `Wrong_or_missing_answer_deletes_the_lap`.
  - **Secrecy:** `Nothing_revealed_before_Result`. During QualifyingQuestion:
    - every row has `LastLapMs` null, `FastestLap` false and Correct/Total from completed laps only;
    - `CorrectIndex` is null;
    - `Answered` is true only for players who answered.
  - **Grid order:**
    - `Grid_order_equals_the_classification`: three players with times 1.0, 2.0, 3.0 s on all laps, and AI forced wrong via `AiRules { MinP = 0, MaxP = 0, Easy = 0 }`. Grid positions are 1, 2, 3 in that order.
    - `Positions_carry_into_round_1_tiebreak`.
  - **Phone views:**
    - `Phone_views_follow_spec_4_2`. Headers and prompts:
      - "Answers in a moment…";
      - "Lap time 2.41 s — checking…";
      - "✔ 2.41 s · P3 so far";
      - "✔ 2.41 s · P1 so far · Purple!";
      - "✖ Lap deleted · P14 so far";
      - Grid "Qualified P4".
    - `Reconnect_mid_qualifying_shows_current_lap_and_keeps_laps` (Review Focus 3).

- [ ] **Step 2: Run, expect FAIL.** `--filter-class Redline.Logic.Tests.QualifyingTests`

- [ ] **Step 3: Implement**
  - **Start:** take the race questions as now, then `Take(QualifyingLaps, Easy)` when `Rules.Qualify`, with their own shuffled answers.
  - **EndDriverSelect:** keep the seeded shuffle; it becomes `QualiTieOrder` and the fallback grid. If qualifying applies, begin lap 1, else Grid.
  - **QualifyingQuestion start:**
    - record the phase start (`ctx.Now`);
    - draw AI laps (`Qualifying.AiLap`) in team order into `PendingLapMs` / `PendingAnswer`, hidden until Result.
  - **OnInput:** the human's lap uses `LapTimeMs(input.ElapsedMs, (ctx.Now − start) ms)`.
  - **Result:**
    - commit the pending laps;
    - `Classify`;
    - set `Position` and `PositionChange` from the previous classification;
    - `FastestLap`.
  - **After the last Result:** `_standings` = classification, then Grid.
  - **Views:** `PlayerViewKey` gains `QualiLap` and `LapMs` so views change only when they should.

- [ ] **Step 4: Run the class, then the whole solution; expect PASS** (existing tests untouched).

- [ ] **Step 5: Commit** `feat(redline): qualifying phases, host view and phone views`

### Task 4: SDK `ElapsedMs` and the phone stopwatch

**Files:**
- Modify: `src/ArcBox.Sdk/PlayerInput.cs`, `web/play/src/protocol.ts`, `web/play/src/widgets/Choice.tsx`
- Test: `src/ArcBox.Sdk.Tests/ContractFixtureTests.cs` (+ a new fixture `web/play/test/fixtures/choiceInput.json`), `web/play/test/contract.test.ts`, `web/play/test/widgets.test.tsx`

**Interfaces:**
- Produces:
  - **C#:** `ChoiceInput(IReadOnlyList<string> Selected, double? ElapsedMs = null)`.
  - **TS:** `interface ChoiceInput { selected: string[]; elapsedMs?: number }`.
  - **Widget:** `Choice` sends `elapsedMs` on every send.

- [ ] **Step 1: Write the failing tests**
  - **C#:** `ChoiceInput_round_trips_elapsedMs_and_reads_old_payloads`: `{"selected":["a"]}` → `ElapsedMs` null. The fixture pins `{"selected":["b"],"elapsedMs":2410.5}`.
  - **TS contract:** the fixture parses as `ChoiceInput`.
  - **vitest (fake timers, mocked `performance.now` and `requestAnimationFrame`):**
    - `Choice starts the clock on the second animation frame after mount`: mount at t=0, frames at 16 and 33, tap at 2433. Sends `elapsedMs` 2400.
    - `Choice clock survives re-render of the same viewId`.
    - `Choice clock restarts for a new viewId`.
    - `Choice starts the clock when a hidden page becomes visible`.
    - `multiple choice sends elapsedMs on Lock in`.

- [ ] **Step 2: Run, expect FAIL** (`dotnet test --project src/ArcBox.Sdk.Tests/…`; `npx vitest run`).

- [ ] **Step 3: Implement**
  - The start time is a `useViewState<number | null>(view.viewId, null)`, set in an effect via a double rAF, or on `visibilitychange` when hidden.
  - A tap before the start is set sends `elapsedMs` 0. The server clamps it anyway.

- [ ] **Step 4: Run, expect PASS** (.NET SDK tests, all vitest).

- [ ] **Step 5: Commit** `feat(sdk): time-to-answer on choice inputs, measured on the phone`

### Task 5: Presentation — tower, cues, commentary, shots, demo

**Files:**
- Modify:
  - `games/Redline/Redline.Presentation/Hud/Tower.cs`
  - `games/Redline/Redline.Presentation/Audio/{AudioCues.cs, Commentary.cs, Synth/Sfx.cs}`
  - `games/Redline/Redline.Presentation/CameraDirector.cs`
  - `games/Redline/Redline.Presentation/Demo/DemoRace.cs`
- Create: `games/Redline/Redline.Presentation/Hud/QualifyingText.cs`
- Test: `games/Redline/Redline.Presentation.Tests/{HudTests.cs, Audio/AudioCuesTests.cs, Audio/CommentaryTests.cs, Audio/SynthTests.cs, CameraDirectorTests.cs, DemoRaceTests.cs}`

**Interfaces:**
- Consumes: Task 3's records and phases.
- Produces:
  - **`Tower.Rows` in the qualifying phases:**
    - Gap = `FormatTotal` (e.g. "6.82").
    - Chip = lap ("2.41", `ChipKind.Purple` when `FastestLap`, else `Good`; "DELETED" `Bad`; null before Result).
    - Mark = Answering / Answered during QualifyingQuestion.
    - New `ChipKind.Purple`.
  - **`QualifyingText`:**
    - `Lap(int ms)` → "2.41"; `Total(int ms)` → "6.82";
    - `Header(int lap, int laps)` → "QUALIFYING · LAP 1/3";
    - `ProvisionalPole(RedlineHostView view, RedlineHostView? previous)` → string? (the name when P1 changed at a Result);
    - `Pole(RedlineHostView lastResult)` → (Name, TeamId, Total).
  - **`AudioCues`:**
    - `Purple = "purple"`, a new `Sfx.PurpleChime()`.
    - `ForView` adds Whoosh at QualifyingQuestion start (as Question does).
    - At QualifyingResult it adds Correct or Wrong (any human lap correct → Correct), then Purple when a human has `FastestLap`.
  - **Commentary** (copy exactly as spec §6, names via `SpokenName`; the narrator's existing queue rules apply):
    - session start at the first QualifyingReading;
    - purple lap;
    - provisional pole at a Result whose P1 changed;
    - pole at the last Result.
  - **`CameraDirector.Pick`:**
    - QualifyingReading / QualifyingQuestion → `TracksideShot(P1 code)`;
    - QualifyingResult → `HeliShot([P1 code])`.
  - **`DemoRace`:** a `qualifying` step (lap 1 with one deleted human lap, lap 2 with a pole change, lap 3), inserted between Driver Select and Grid.

- [ ] **Step 1: Write the failing tests**
  - **Tower:** `Tower_qualifying_shows_total_lap_chip_purple_and_deleted`.
  - **QualifyingText:** `QualifyingText_formats_times_to_hundredths_rounding_half_up` (2405 → "2.41", 6815 → "6.82").
  - **AudioCues:** `AudioCues_qualifying_result_plays_correct_then_purple`.
  - **Commentary:** `Commentary_qualifying_lines`, with the four lines word for word and a provisional-pole line only when P1 changed.
  - **Synth:** `PurpleChime_renders_within_length_and_peak` (≤ 1.2 s, peak ≤ −1 dBFS like the other clips).
  - **Camera:** `CameraDirector_qualifying_shots`.
  - **Demo:** `DemoRace_qualifying_step_has_a_deleted_lap_and_a_pole_change`.

- [ ] **Step 2: Run, expect FAIL.**

- [ ] **Step 3: Implement** to the interfaces above.

- [ ] **Step 4: Run, expect PASS.**

- [ ] **Step 5: Commit** `feat(redline): qualifying tower, cues, commentary and camera`

### Task 6: TV (Godot)

**Files:**
- Modify:
  - `src/ArcBox.Host/games/redline/RedlineScene.cs`
  - `src/ArcBox.Host/games/redline/Hud/{RedlineHud.cs, TimingTower.cs, QuestionPanel.cs, TrackMap.cs}`
  - `src/ArcBox.Host/games/redline/Audio/{SfxBank.cs, AudioDirector.cs}`
  - `src/ArcBox.Host/games/redline/Demo/RedlineDemoFeeder.cs`
  - `src/ArcBox.Host/AppArgs` handling for `--redline-demo=qualifying`
  - `docs/redline-tv.md`, `docs/tv-host.md`
- Create: `src/ArcBox.Host/games/redline/Hud/PoleCard.cs`

**Interfaces:**
- Consumes: everything Task 5 produces.

- [ ] **Step 1: Wire the new pieces in**
  - **Tower:** qualifying columns (POS · DRIVER · LAP · TOTAL) from `Tower.Rows`; `ChipKind.Purple` in `HudStyle` (#a020f0 on white text).
  - **QuestionPanel:** uses `QualifyingText.Header`, with LISTEN during QualifyingReading as in Reading.
  - **TrackMap:** hidden in the qualifying phases.
  - **PoleCard:** "PROVISIONAL POLE · {name}" (3 s) and "POLE POSITION" (name, team colour bar, total), in `HudStyle`.
  - **Audio:** `SfxBank` registers "purple".
  - **Demo:** the demo feeder plays the `qualifying` step.
  - **Grid log line:** when Grid starts, the scene logs `REDLINE grid <CODE> <CODE> …` in grid order, once per race. Task 7's e2e check reads it.

- [ ] **Step 2: Build.** `dotnet build src/ArcBox.Host` succeeds with no new warnings.

- [ ] **Step 3: Capture demo shots.** Run `xvfb-run … --fixed-fps 8 -- --redline-demo=qualifying --shots=<scratch>/quali --res=1920x1080 --preset=high --quit-after=60`. Expect, with no error lines in the log:
  - shots of all three qualifying phases;
  - the purple chip;
  - DELETED;
  - the provisional pole card;
  - the pole card;
  - the grid in the new order.

- [ ] **Step 4: Docs.** Add qualifying to `docs/redline-tv.md` (flow, tower, cards, sounds) and the demo step to `docs/tv-host.md`.

- [ ] **Step 5: Commit** `feat(redline): qualifying on the TV`

### Task 7: Sim, end-to-end and integration

**Files:**
- Modify:
  - `games/Redline/tools/Redline.Sim/{RaceRunner.cs, Report.cs, Bots.cs}`
  - `web/play/e2e/redline.e2e.mjs`
  - `tools/tv-e2e.sh`
- Create: `docs/superpowers/reports/2026-10-29-redline-qualifying-sim.md`

- [ ] **Step 1: Sim.**
  - Run qualifying in each simulated race. Bots answer with log-normal times: median 2.6 s, σ 0.3, accuracy 0.8.
  - Report how often the best of three bots takes pole and their median grid slot, over 2000 races (seeded).
  - **Expected:** pole for the best bot in 10–40% of races. If it falls outside, adjust `QualiFastMedian` / `QualiSlowMedian` within ±0.4 s and record the change and the numbers in the report.

- [ ] **Step 2: Phone bots** (`redline.e2e.mjs`).
  - New phases: `quali-reading`, `quali-question`, `quali-result`.
  - Bot k answers after (1 + k) s; bot 3 answers lap 2 wrong.
  - Fail unless phone 1 saw all three qualifying phases.

- [ ] **Step 3: `tools/tv-e2e.sh` assertions.**
  - The TV log shows QualifyingReading(1)…QualifyingResult(3) before Grid.
  - The AUDIO log has the qualifying start line and a "Pole position for" line.
  - The three bots' grid order equals their qualifying order (from the TV's `REDLINE grid` log line, Task 6).

- [ ] **Step 4: Verify.**
  - `dotnet test --solution ArcBox.slnx`, `npx vitest run`.
  - `ARCBOX_FULL=1 GODOT=… tools/tv-e2e.sh` passes.
  - Send the user the qualifying shots.

- [ ] **Step 5: Commit** `test: qualifying in the sim and the TV end-to-end run`; push.
