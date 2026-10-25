# REDLINE Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build everything in REDLINE that can be built and tested before the TV host (M3) and phone app (M2) exist:
- the two SDK/server platform changes;
- the complete game logic, with Driver Select, a 22-car AI field and scoring;
- the OpenTDB question pool with its offline fallback;
- the pure-C# presentation library (lap maths, track geometry, camera director) that the Godot scene will consume.

**Architecture:**
- `Redline.Logic` is a plain `IGame`. It never does I/O, and it receives questions synchronously through `IQuestionSource`.
- `ArcBox.Server/Games/Redline/` keeps a warm OpenTDB pool in a `BackgroundService` and tops up from an embedded fallback pack.
- `Redline.Presentation` holds every Godot-free calculation the TV needs, so it is unit-tested like game logic.

**Tech Stack:** .NET 10, C# latest, System.Text.Json (`ArcJson.Options`), xUnit v3 (`xunit.v3.mtp-v2` 4.0.2) on Microsoft.Testing.Platform, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Http`/`Hosting` (ASP.NET Core shared framework).

**Spec:** `docs/superpowers/specs/2026-10-25-redline-design.md` (§ numbers below refer to it). Platform spec: `docs/superpowers/specs/2026-10-18-arcbox-platform-design.md`.

**Not in this plan:** the Godot scene (§4.8 Track/Cars/HUD/Rendering) and the phone theme (§4.10). They depend on M3/M4 and M2, and get their own plan once those land. The mockups in `docs/superpowers/mockups/trivia-racer/` (`f1car2026.js`, `cota3d.js`) are their reference implementation.

## Global Constraints

- **Target frameworks:**
  - `Redline.Logic` and `Redline.Presentation` use `<TargetFrameworks>$(ArcSdkTargetFrameworks)</TargetFrameworks>` and reference only `ArcBox.Sdk` (Presentation also references `Redline.Logic`).
  - Test projects target `net10.0` and copy `Counter.Logic.Tests.csproj` exactly (`OutputType Exe`, `TestingPlatformDotnetTestSupport`, `xunit.v3.mtp-v2` 4.0.2, `<Using Include="Xunit" />`).
- **Build:** `Nullable` enabled and `TreatWarningsAsErrors` true (already in `Directory.Build.props`). Every new project goes into `ArcBox.slnx` under `/games/Redline/`.
- **Game rules (§2, §3.1):** these values are fixed, all in `RedlineRules`:
  - FinishLine 20; MaxRounds 15; CircuitId `"cota"`;
  - phases DriverSelect 20 s, Grid 6 s, Throttle 10 s, Question 20 s, Reveal 7 s, Chequered 10 s;
  - CRUISE +1/0, PUSH +2/−1, REDLINE +4/−2 plus spin out (pit limiter for the next round); distance floors at 0;
  - no throttle = Cruise; no answer = wrong.
- **AI (§2.6):**
  - base accuracy easy 0.72, medium 0.52, hard 0.32; `P(correct) = clamp(base × aiSkill, 0.08, 0.92)`;
  - throttle score Cruise p, Push 3p−1, Redline 6p−2−0.6;
  - softmax temperature 0.35; no Redline below p 0.3 unless chasing;
  - chase mode = gap to leader ≥ 6 and rounds left ≤ 5, adding +0.5 to Redline.
- **Field:** always 22 cars from `grid2026.json`. Humans claim seats and AI drives the rest. `GameResults` contains humans only.
- **Wire ids:**
  - throttle buttons `cruise` / `push` / `redline`;
  - answer options `a`–`d`;
  - Driver Select options are driver codes (e.g. `VER`).
- **User-facing strings:** exactly as written in spec §2.3 and §3.3, and in this plan where it adds new ones.
- **OpenTDB (§4.5):**
  - `amount=50&type=multiple&encode=url3986`;
  - request spacing ≥ 5.5 s; pool Target 150, LowWater 60; backoff from 5 s, doubling, capped at 5 min; dedupe window 500;
  - HTTP timeout 10 s and no retry handler; config section `Redline:OpenTdb`.
- **Determinism:** game code takes all randomness and time from `IGameContext`. No `Random.Shared` or `DateTime.Now` in `Redline.Logic` or `Redline.Presentation`.
- **Attribution:** every embedded third-party data file ships with a `THIRD_PARTY.md` beside it:
  - `grid2026.json` under CC BY 4.0 (F1DB);
  - `cota.json` under MIT (bacinger/f1-circuits).

  Copy the text from `docs/superpowers/mockups/trivia-racer/THIRD_PARTY.md`.
- **Commits:** every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

Five situations the spec implies but no main-path test hits. Each one gets a pinned test, and the task that adds it is in brackets.

1. **A player claims a seat, then disconnects before the Grid.** They keep that driver. On rejoin they are still that car, and nobody else can claim it. [Task 5]
2. **Every player disconnects during Driver Select.** The timer still fires, every player gets a random free seat, and the race runs on defaults with no exception. [Task 5]
3. **A pit-limited player during the Question phase.** The limiter only removes the throttle choice. That player still answers, and a correct answer pays Cruise +1. [Task 6]
4. **Two cars cross the line in the same Reveal.** The bigger overshoot wins. Equal distance and equal correct count share P1, and the message uses the "share the win" wording. [Task 7]
5. **OpenTDB returns a malformed question** (fewer than 3 incorrect answers, a blank string, or a duplicate answer after decoding). The client drops that question and keeps the rest of the batch. [Task 8]

---

## File Structure

```
src/ArcBox.Sdk/ControllerView.cs                          (modify) Option gains Hint
src/ArcBox.Server/Games/GameRegistrationExtensions.cs      (modify) factory overload
src/ArcBox.Server/ArcBox.Server.csproj                     (modify) reference Redline.Logic
src/ArcBox.Server/Games/Redline/
  OpenTdbOptions.cs  OpenTdbClient.cs  OpenTdbQuestionPool.cs  PooledQuestionSource.cs  RedlineServiceCollectionExtensions.cs
src/ArcBox.Server.Tests/Redline/
  OpenTdbClientTests.cs  OpenTdbQuestionPoolTests.cs  PooledQuestionSourceTests.cs  AddRedlineTests.cs
  Fixtures/opentdb-ok.json  Support/FakeHandler.cs
games/Redline/Redline.Logic/
  Redline.Logic.csproj  THIRD_PARTY.md
  Data/grid2026.json  Data/fallback-questions.json
  Grid/Grid.cs  Grid/Grid2026.cs
  Questions/TriviaQuestion.cs  Questions/IQuestionSource.cs  Questions/FallbackQuestions.cs
  RedlineRules.cs  AiPolicy.cs
  Race/Driver.cs  Race/Scoring.cs  Race/Ranking.cs
  Views/HostView.cs  Views/PlayerViews.cs
  RedlineGame.cs
games/Redline/Redline.Logic.Tests/
  Support/FakeQuestionSource.cs  Support/RedlineKit.cs
  GridTests.cs  FallbackQuestionsTests.cs  AiPolicyTests.cs  DriverSelectTests.cs  RoundTests.cs  FinishTests.cs
games/Redline/Redline.Presentation/
  Redline.Presentation.csproj  THIRD_PARTY.md  Data/cota.json
  CircuitData.cs  LapMath.cs  TrackGeometry.cs  CameraDirector.cs
games/Redline/Redline.Presentation.Tests/
  CircuitDataTests.cs  LapMathTests.cs  TrackGeometryTests.cs  CameraDirectorTests.cs
games/Redline/tools/refresh_grid.py                         F1DB snapshot refresher (stdlib Python)
```

---

### Task 1: Platform changes: `Option.Hint` and the DI-aware `AddArcGame`

**Files:**
- Modify: `src/ArcBox.Sdk/ControllerView.cs` (the `Option` record)
- Modify: `src/ArcBox.Server/Games/GameRegistrationExtensions.cs`
- Test: `src/ArcBox.Sdk.Tests/SerializationTests.cs`, `src/ArcBox.Server.Tests/GameRegistryTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record Option(string Id, string Label, string? Hint = null);`
  - `public static IServiceCollection AddArcGame<TGame>(this IServiceCollection s, Func<IServiceProvider, TGame> factory) where TGame : IGame;`

- [ ] **Step 1: Write the failing tests**

```csharp
// SerializationTests
[Fact]
public void Option_hint_serializes_camelCase_and_round_trips()
{
    ControllerView v = new ButtonsView(null, [new Option("redline", "REDLINE", "+4 if right · spin out if wrong")]) { ViewId = "v" };
    var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
    Assert.Equal("+4 if right · spin out if wrong", json.GetProperty("buttons")[0].GetProperty("hint").GetString());
    Assert.Equal(json.GetRawText(), JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<ControllerView>(json, ArcJson.Options), ArcJson.Options).GetRawText());
}

// GameRegistryTests
[Fact]
public void Factory_overload_resolves_dependencies_per_creation()
{
    var services = new ServiceCollection();
    var calls = 0;
    services.AddSingleton(new object());
    services.AddArcGame(sp => { Assert.NotNull(sp.GetRequiredService<object>()); calls++; return new CounterGame(); });
    services.AddSingleton<IGameRegistry, GameRegistry>();
    var registry = services.BuildServiceProvider().GetRequiredService<IGameRegistry>();
    Assert.Equal("counter", registry.Manifests.Single().Id);
    registry.Create("counter");
    Assert.Equal(2, calls);      // one manifest probe + one Create
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test src/ArcBox.Sdk.Tests` and `dotnet test src/ArcBox.Server.Tests`.
Expected: compile errors (no `Hint`, no overload).

- [ ] **Step 3: Implement both changes**

- Add the optional `Hint` parameter to `Option`.
- Implement the overload as `s.AddSingleton(sp => new GameRegistration(() => factory(sp)))`.
- Leave the existing `new()` overload unchanged.

- [ ] **Step 4: Run all existing tests and confirm they pass**

Run: `dotnet test ArcBox.slnx`
Expected: all green, including the existing `Every_widget_round_trips`.

- [ ] **Step 5: Commit** with `feat(sdk,server): Option.Hint and DI-aware AddArcGame factory`.

---

### Task 2: `Redline.Logic` project and the 2026 grid data

**Files:**
- Create: `games/Redline/Redline.Logic/Redline.Logic.csproj`, with embedded resources `<EmbeddedResource Include="Data\*.json" LogicalName="Redline.Logic.Data.%(Filename)%(Extension)" />`
- Create: `games/Redline/Redline.Logic/Data/grid2026.json` (copy `docs/superpowers/mockups/trivia-racer/grid2026.json` verbatim)
- Create: `games/Redline/Redline.Logic/THIRD_PARTY.md`, `Grid/Grid.cs`, `Grid/Grid2026.cs`
- Create: `games/Redline/Redline.Logic.Tests/Redline.Logic.Tests.csproj`, `GridTests.cs`
- Create: `games/Redline/tools/refresh_grid.py`
- Modify: `ArcBox.slnx` (add both projects under `/games/Redline/`)

**Interfaces:**
- Produces (namespace `Redline.Logic`):

```csharp
public sealed record Livery(string Top, string Side, string Stripe, string Nose, string Wing, string Endplate, string Halo, string Number);
public sealed record GridDriver(string Id, string Name, string Code, int Number, string Tcam, double Points, double AiSkill);
public sealed record GridTeam(string Id, string Name, string Chassis, string Finish, Livery Livery, IReadOnlyList<GridDriver> Drivers);
public sealed record Seat(GridTeam Team, GridDriver Driver);
public sealed record Grid(int Season, IReadOnlyList<GridTeam> Teams)
{
    public IReadOnlyList<Seat> Seats { get; }   // team order, then driver order (22)
    public Seat ByCode(string code);            // throws KeyNotFoundException
}
public static class Grid2026
{
    public static Grid Load();                  // embedded resource, cached, validated
    public static Grid Validate(Grid grid);     // throws InvalidDataException on a bad grid; returns it otherwise
}
```

- [ ] **Step 1: Write the failing tests** (`GridTests`)

```csharp
[Fact] public void Load_has_11_teams_and_22_unique_seats()
{
    var g = Grid2026.Load();
    Assert.Equal(11, g.Teams.Count);
    Assert.Equal(22, g.Seats.Select(s => s.Driver.Code).Distinct().Count());
    Assert.Equal(22, g.Seats.Select(s => s.Driver.Number).Distinct().Count());
}
[Fact] public void Champion_and_verstappen_numbers_match_2026()
{
    var g = Grid2026.Load();
    Assert.Equal(1, g.ByCode("NOR").Driver.Number);
    Assert.Equal(3, g.ByCode("VER").Driver.Number);
    Assert.Equal("red-bull", g.ByCode("VER").Team.Id);
}
[Fact] public void AiSkill_comes_from_the_standings_snapshot()
{
    var g = Grid2026.Load();
    Assert.Equal(1.15, g.ByCode("ANT").Driver.AiSkill, 3);
    Assert.Equal(0.85, g.ByCode("BOT").Driver.AiSkill, 3);
}
[Fact] public void Validate_rejects_duplicate_codes_and_numbers()
{
    var g = Grid2026.Load();
    var t = g.Teams[0];
    var dup = t with { Drivers = [t.Drivers[0], t.Drivers[0]] };
    Assert.Throws<InvalidDataException>(() => Grid2026.Validate(g with { Teams = [dup, .. g.Teams.Skip(1)] }));
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Logic.Tests`
Expected: compile errors.

- [ ] **Step 3: Implement the projects, the records and `Grid2026`**

- Deserialize with `ArcJson.Options` and ignore unknown top-level keys (`source`, `license`, `form`).
- `Validate` requires 11 teams with 2 drivers each, unique codes and numbers, and `AiSkill` in [0.85, 1.15].
- `refresh_grid.py` takes an F1DB checkout path and rewrites `grid2026.json`:
  - drivers, codes and numbers from the latest race result;
  - points from `driver-standings.yml`;
  - `aiSkill` with the §2.6 formula;
  - it keeps the existing `livery` blocks.

  Run it with `python3 -I`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test games/Redline/Redline.Logic.Tests`
Expected: 4 passed.

- [ ] **Step 5: Commit** with `feat(redline): Redline.Logic project and 2026 grid data (F1DB, CC BY 4.0)`.

---

### Task 3: Questions: `TriviaQuestion`, `IQuestionSource`, fallback pack

**Files:**
- Create: `games/Redline/Redline.Logic/Questions/{TriviaQuestion.cs, IQuestionSource.cs, FallbackQuestions.cs}`, `Data/fallback-questions.json`
- Create: `games/Redline/Redline.Logic.Tests/Support/FakeQuestionSource.cs`, `FallbackQuestionsTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum Difficulty { Easy, Medium, Hard }
public enum QuestionOrigin { OpenTdb, Fallback }
public sealed record TriviaQuestion(string Category, Difficulty Difficulty, string Text, string Correct, IReadOnlyList<string> Incorrect, QuestionOrigin Origin);
public interface IQuestionSource { IReadOnlyList<TriviaQuestion> Take(int count); }   // sync, never blocks or throws, returns exactly count
public static class FallbackQuestions
{
    public static IReadOnlyList<TriviaQuestion> All { get; }                    // lazy, validated, Origin = Fallback
    public static void Validate(IReadOnlyList<TriviaQuestion> questions);      // throws InvalidDataException
}
// test support
public sealed class FakeQuestionSource(Difficulty difficulty = Difficulty.Medium) : IQuestionSource
{   // question i: Category "Science & Nature", Text $"Q{i}", Correct $"right{i}", Incorrect ["w1","w2","w3"], Origin OpenTdb
    public int TakeCalls { get; }
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Pack_has_at_least_60_questions_across_8_categories()
{
    Assert.True(FallbackQuestions.All.Count >= 60);
    Assert.Equal(8, FallbackQuestions.All.Select(q => q.Category).Distinct().Count());
    Assert.All(FallbackQuestions.All, q => Assert.Equal(QuestionOrigin.Fallback, q.Origin));
}
[Fact] public void Every_question_has_three_distinct_wrong_answers_and_no_blanks()
    => FallbackQuestions.Validate(FallbackQuestions.All);   // must not throw
[Fact] public void Validate_rejects_duplicate_texts_and_two_incorrect_answers()
{
    var q = FallbackQuestions.All[0];
    Assert.Throws<InvalidDataException>(() => FallbackQuestions.Validate([q, q]));
    Assert.Throws<InvalidDataException>(() => FallbackQuestions.Validate([q with { Incorrect = ["x", "y"] }]));
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter FallbackQuestions`
Expected: compile errors.

- [ ] **Step 3: Write the 60 original questions and the loader**

- The categories are exactly: General Knowledge, Science & Nature, Geography, History, Film, Music, Sports, Video Games. Four have 8 questions and four have 7.
- Difficulty is roughly 20 / 20 / 20.
- Write the questions yourself. Don't copy them from OpenTDB or elsewhere.
- In JSON, `difficulty` is lowercase (`"easy"`) and `origin` is omitted (the loader sets `Fallback`).
- `Validate` rejects:
  - blank strings;
  - a count of incorrect answers other than 3;
  - duplicate answers within a question (case-insensitive);
  - duplicate question texts.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter FallbackQuestions`
Expected: 3 passed.

- [ ] **Step 5: Commit** with `feat(redline): question model, IQuestionSource and 60-question fallback pack`.

---

### Task 4: `RedlineRules` and `AiPolicy`

**Files:**
- Create: `games/Redline/Redline.Logic/{RedlineRules.cs, AiPolicy.cs}`
- Test: `games/Redline/Redline.Logic.Tests/AiPolicyTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum Throttle { Cruise, Push, Redline }
public sealed record AiRules
{
    public double Easy { get; init; } = 0.72;  public double Medium { get; init; } = 0.52;  public double Hard { get; init; } = 0.32;
    public double MinP { get; init; } = 0.08;  public double MaxP { get; init; } = 0.92;
    public double Temperature { get; init; } = 0.35;  public double LimiterCost { get; init; } = 0.6;
    public double RedlineFloor { get; init; } = 0.3;  public int ChaseGap { get; init; } = 6;  public int ChaseRoundsLeft { get; init; } = 5;
    public double ChaseBonus { get; init; } = 0.5;
}
public sealed record RedlineRules
{   // every value in Global Constraints, plus DriverSelect = 20 s
    public int FinishLine { get; init; } = 20;  public int MaxRounds { get; init; } = 15;  public string CircuitId { get; init; } = "cota";
    public TimeSpan DriverSelect { get; init; } = TimeSpan.FromSeconds(20);  /* Grid 6, Throttle 10, Question 20, Reveal 7, Chequered 10 */
    public int CruiseGain { get; init; } = 1;  public int PushGain { get; init; } = 2;  public int PushLoss { get; init; } = 1;
    public int RedlineGain { get; init; } = 4;  public int RedlineLoss { get; init; } = 2;
    public AiRules Ai { get; init; } = new();
}
public static class AiPolicy
{
    public static double Accuracy(double aiSkill, Difficulty difficulty, AiRules rules);
    public static Throttle ChooseThrottle(double p, int gapToLeader, int roundsLeft, AiRules rules, Random random);
    public static int Answer(double p, int correctIndex, Random random);   // correct with prob p; else uniform over the 3 wrong indices
}
```

`roundsLeft` counts the current round (round 15 of 15 → 1).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Accuracy_is_base_times_skill_clamped()
{
    var r = new AiRules();
    Assert.Equal(0.72 * 1.15, AiPolicy.Accuracy(1.15, Difficulty.Easy, r), 6);
    Assert.Equal(0.92, AiPolicy.Accuracy(2.0, Difficulty.Easy, r), 6);
    Assert.Equal(0.32 * 0.85, AiPolicy.Accuracy(0.85, Difficulty.Hard, r), 6);
}
[Fact] public void Answers_hit_p_within_two_percent_over_10000_draws()
{
    var rnd = new Random(7); var p = 0.52 * 1.09;
    var hits = Enumerable.Range(0, 10_000).Count(_ => AiPolicy.Answer(p, 2, rnd) == 2);
    Assert.InRange(hits / 10_000.0, p - 0.02, p + 0.02);
}
[Fact] public void Wrong_answers_never_pick_the_correct_index_and_spread_over_all_three()
{
    var rnd = new Random(3);
    var wrong = Enumerable.Range(0, 3000).Select(_ => AiPolicy.Answer(0.0001, 1, rnd)).ToList();
    Assert.DoesNotContain(1, wrong);
    Assert.Equal([0, 2, 3], wrong.Distinct().Order());
}
[Fact] public void No_redline_below_floor_unless_chasing()
{
    var rnd = new Random(1); var r = new AiRules();
    Assert.DoesNotContain(Throttle.Redline, Enumerable.Range(0, 2000).Select(_ => AiPolicy.ChooseThrottle(0.29, 0, 10, r, rnd)));
    Assert.Contains(Throttle.Redline, Enumerable.Range(0, 2000).Select(_ => AiPolicy.ChooseThrottle(0.29, 6, 5, r, rnd)));
}
[Fact] public void Chase_mode_boundary_is_gap_6_and_5_rounds_left()
{   // compare Redline frequency: (gap 6, left 5) chases; (gap 5, left 5) and (gap 6, left 6) do not
    double Freq(int gap, int left) { var rnd = new Random(9); return Enumerable.Range(0, 4000).Count(_ => AiPolicy.ChooseThrottle(0.5, gap, left, new AiRules(), rnd) == Throttle.Redline) / 4000.0; }
    Assert.True(Freq(6, 5) > Freq(5, 5) + 0.05);
    Assert.True(Freq(6, 5) > Freq(6, 6) + 0.05);
}
[Fact] public void Same_seed_same_decisions()
{
    var a = new Random(42); var b = new Random(42);
    Assert.Equal(Enumerable.Range(0, 100).Select(_ => AiPolicy.ChooseThrottle(0.6, 0, 10, new(), a)),
                 Enumerable.Range(0, 100).Select(_ => AiPolicy.ChooseThrottle(0.6, 0, 10, new(), b)));
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter AiPolicy`
Expected: compile errors.

- [ ] **Step 3: Implement**

- `ChooseThrottle` scores the three throttles with the Global Constraints formulas.
- If not chasing, it removes Redline when `p < RedlineFloor`.
- When chasing, it adds `ChaseBonus` to Redline.
- It samples with softmax weights `exp(score / Temperature)`, drawing once from `random.NextDouble()`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter AiPolicy`
Expected: 6 passed.

- [ ] **Step 5: Commit** with `feat(redline): rules record and data-driven AI policy`.

---

### Task 5: `RedlineGame`: views, the 22-car field, Driver Select and Grid

**Files:**
- Create: `games/Redline/Redline.Logic/{RedlineGame.cs, Race/Driver.cs, Views/HostView.cs, Views/PlayerViews.cs}`
- Create: `games/Redline/Redline.Logic.Tests/{Support/RedlineKit.cs, DriverSelectTests.cs}`

**Interfaces:**
- Consumes: Tasks 2–4.
- Produces: the `RedlineHostView`, `QuestionCard`, `DriverRow`, `FinishSummary` and `Award` records, and the `DriverStatus`, `RacePhase` and `RoundOutcome` enums, **exactly as written in spec §4.4**. Reuse `Throttle` from Task 4; don't redeclare it. Also produces:

```csharp
public sealed class RedlineGame(IQuestionSource questions, RedlineRules? rules = null, Grid? grid = null) : IGame
{   // Manifest: Id "redline", Name "REDLINE", MinPlayers 2, MaxPlayers 8,
    // Description "Racing trivia: bet your throttle on the category, then answer. 22-car 2026 grid."
    public RedlineRules Rules { get; }
}
// test support (RedlineKit)
public static class RedlineKit
{
    public static (GameHarness<RedlineGame> H, FakeQuestionSource Q) Start(int players = 4, int seed = 1, RedlineRules? rules = null);
    public static void Claim(GameHarness<RedlineGame> h, int player, string code);   // Input(player, new { selected = new[] { code } })
    public static void SeatAllAndGoToRound1(GameHarness<RedlineGame> h, params string[] codes);   // claims, then advances through Grid
    public static RedlineHostView View(GameHarness<RedlineGame> h);
    public static DriverRow Row(GameHarness<RedlineGame> h, int player);             // the row whose Player == Players[player].Id
}
```

**Rules for this task:**
- **`Start`:**
  - calls `questions.Take(Rules.MaxRounds)` exactly once;
  - builds 22 `Driver`s from `grid.Seats`, all unclaimed;
  - enters `RacePhase.DriverSelect` with `SetTimer("phase", 20 s)`.
- **Constructor:** must not call `Take`.
- **Claims:** a claim of a free code seats the player.
  - A claim for a taken code re-sends that player's `ChoiceView` with `Header = "{CODE} was just taken: pick another"`.
  - A player who is already seated is ignored.
  - Unknown codes are ignored.
- **Choosing view:** `ChoiceView("Pick your driver", options)` with one option per free seat in grid order: `new Option(code, $"#{number} {name}", $"{team.Name} · {team.Chassis}")`.
  - Rebuild a player's view only when the free set changes. Otherwise return the same instance, so `ViewId` stays stable.
- **Seated view:** `WaitView($"You're {name}. Waiting for the grid…")`.
- **Early advance:** advance once every connected player is seated.
- **On timeout:** seat each unseated player in a random free seat (`ctx.Random`). All other seats become AI. Then shuffle the start order with `ctx.Random`.
- **Grid (6 s):** the phone shows `WaitView($"You're {name}, #{number}, {team}. Lights out in a moment…")`.
- **HostView:**
  - `Drivers` lists all 22, in team order during Driver Select and in ranking order afterwards;
  - `Question` is null during DriverSelect and Grid;
  - `Attribution` is set per §4.6.

- [ ] **Step 1: Write the failing tests** (`DriverSelectTests`)

```csharp
[Fact] public void Manifest_and_take_contract()
{
    var q = new FakeQuestionSource();
    var game = new RedlineGame(q);
    Assert.Equal(("redline", 2, 8), (game.Manifest.Id, game.Manifest.MinPlayers, game.Manifest.MaxPlayers));
    Assert.Equal(0, q.TakeCalls);
    var h = new GameHarness<RedlineGame>(game); h.Start();
    Assert.Equal(1, q.TakeCalls);
}
[Fact] public void Starts_in_driver_select_with_22_free_seats_and_hints()
{
    var (h, _) = RedlineKit.Start();
    Assert.Equal(RacePhase.DriverSelect, RedlineKit.View(h).Phase);
    Assert.Equal(h.Now + TimeSpan.FromSeconds(20), RedlineKit.View(h).DeadlineUtc);
    var v = Assert.IsType<ChoiceView>(h.PlayerView(0));
    Assert.Equal(22, v.Options.Count);
    Assert.Equal(new Option("NOR", "#1 Lando Norris", "McLaren · MCL40"), v.Options[0]);
}
[Fact] public void Claim_seats_player_and_removes_seat_for_others_only()
{
    var (h, _) = RedlineKit.Start();
    var p2Before = h.PlayerView(2);
    RedlineKit.Claim(h, 0, "VER");
    Assert.IsType<WaitView>(h.PlayerView(0));
    Assert.DoesNotContain(((ChoiceView)h.PlayerView(1)).Options, o => o.Id == "VER");
    Assert.NotSame(p2Before, h.PlayerView(2));
    Assert.Equal(new PlayerId("p1"), RedlineKit.View(h).Drivers.Single(d => d.Code == "VER").Player);
}
[Fact] public void Taken_seat_is_rejected_with_header()
{
    var (h, _) = RedlineKit.Start();
    RedlineKit.Claim(h, 0, "VER");
    RedlineKit.Claim(h, 1, "VER");                          // raced: list was stale
    Assert.Equal("VER was just taken: pick another", h.PlayerView(1).Header);
    Assert.Equal(new PlayerId("p1"), RedlineKit.View(h).Drivers.Single(d => d.Code == "VER").Player);
}
[Fact] public void All_seated_advances_to_grid_and_ai_fills_the_rest()
{
    var (h, _) = RedlineKit.Start(players: 2);
    RedlineKit.Claim(h, 0, "LEC"); RedlineKit.Claim(h, 1, "ALO");
    var v = RedlineKit.View(h);
    Assert.Equal(RacePhase.Grid, v.Phase);
    Assert.Equal(22, v.Drivers.Count);
    Assert.Equal(20, v.Drivers.Count(d => d.Player is null));
    Assert.Equal("You're Charles Leclerc, #16, Ferrari. Lights out in a moment…", ((WaitView)h.PlayerView(0)).Message);
}
[Fact] public void Timeout_seats_unseated_players_randomly()
{
    var (h, _) = RedlineKit.Start(players: 3);
    RedlineKit.Claim(h, 0, "NOR");
    h.Advance(TimeSpan.FromSeconds(20));
    var v = RedlineKit.View(h);
    Assert.Equal(3, v.Drivers.Count(d => d.Player is not null));
    Assert.Equal("NOR", RedlineKit.Row(h, 0).Code);
}
[Fact] public void Attribution_credits_opentdb_when_any_question_came_from_it()
{
    var (h, _) = RedlineKit.Start();                                  // FakeQuestionSource → Origin OpenTdb
    Assert.Equal("Questions: Open Trivia DB (CC BY-SA 4.0)", RedlineKit.View(h).Attribution);
}
// Review Focus 1
[Fact] public void Seated_player_who_disconnects_keeps_their_driver()
{
    var (h, _) = RedlineKit.Start(players: 2);
    RedlineKit.Claim(h, 0, "HAM"); h.SetConnected(0, false);
    RedlineKit.Claim(h, 1, "HAM");
    Assert.Equal("HAM was just taken: pick another", h.PlayerView(1).Header);
    h.SetConnected(0, true);
    Assert.Equal("HAM", RedlineKit.Row(h, 0).Code);
}
// Review Focus 2
[Fact] public void Everyone_disconnected_still_starts_on_timeout()
{
    var (h, _) = RedlineKit.Start();
    for (var i = 0; i < 4; i++) h.SetConnected(i, false);
    h.Advance(TimeSpan.FromSeconds(20 + 6));
    Assert.Equal(RacePhase.Throttle, RedlineKit.View(h).Phase);
    Assert.Equal(4, RedlineKit.View(h).Drivers.Count(d => d.Player is not null));
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter DriverSelect`
Expected: compile errors.

- [ ] **Step 3: Implement the views, `Driver`, the game skeleton, Driver Select and Grid**, following the rules above. Wrap input parsing in `try/catch (JsonException)` like `CounterGame`. The Grid timer leads into round 1, which Task 6 fills in. Until then, the Grid timer may simply set `Phase = Throttle`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter DriverSelect`
Expected: 9 passed.

- [ ] **Step 5: Commit** with `feat(redline): game skeleton, 22-car field, Driver Select and Grid`.

---

### Task 6: Rounds: Throttle → Question → Reveal, with scoring, AI and secrecy

**Files:**
- Create: `games/Redline/Redline.Logic/Race/Scoring.cs`
- Modify: `games/Redline/Redline.Logic/{RedlineGame.cs, Views/PlayerViews.cs, Views/HostView.cs}`
- Test: `games/Redline/Redline.Logic.Tests/RoundTests.cs`

**Interfaces:**
- Consumes: Tasks 4–5.
- Produces: `public static class Scoring { public static (int NewDistance, RoundOutcome Outcome, bool SpinOut) Apply(int distance, Throttle throttle, bool? correct, RedlineRules rules); }` (`correct` is null for no answer), plus the `RedlineKit` helpers:

```csharp
public static void Throttle(GameHarness<RedlineGame> h, int player, string id);     // Input(player, new { pressed = id })
public static void AnswerRight(GameHarness<RedlineGame> h, int player);              // picks the option whose Label == current question's Correct
public static void AnswerWrong(GameHarness<RedlineGame> h, int player);
```

**Rules for this task:**
- **At the start of each phase,** AI drivers decide immediately via `AiPolicy`:
  - the throttle at Throttle start, with `gapToLeader` and `roundsLeft = MaxRounds − Round + 1`;
  - the answer at Question start.
- **Pit-limited drivers** (human or AI) are forced to Cruise and show as `Limited`.
- **Early advance:** Throttle and Question end early when every **connected human** has decided; AI never blocks. The check also runs in `OnPlayerConnectionChanged`.
- **Answers:** each question's four answers are shuffled once in `Start` with `ctx.Random`, and option ids `a`–`d` follow the shuffled order.
- **Reveal** applies `Scoring` to all 22 cars and computes `PositionChange` from `PositionAtRoundStart`. The phone shows the exact §3.3 Reveal strings.
- **Secrecy:**
  - `DriverRow.Throttle` is null until the Question phase;
  - `AnswerIndex` and `Outcome` are null until Reveal;
  - `QuestionCard.Text` and `Answers` are null during Throttle.
- **A pit-limited human still answers** in the Question phase.
- **Phone views and headers** are exactly as in §3.3. A view instance is reused while that player's state is unchanged.

- [ ] **Step 1: Write the failing tests** (`RoundTests`). Each test uses `RedlineKit.SeatAllAndGoToRound1(h, "NOR", "VER", "LEC", "ANT")` unless noted.

```csharp
[Theory]
[InlineData(Throttle.Cruise, true, 5, 6, RoundOutcome.Correct, false)]
[InlineData(Throttle.Cruise, false, 5, 5, RoundOutcome.Wrong, false)]
[InlineData(Throttle.Push, true, 5, 7, RoundOutcome.Correct, false)]
[InlineData(Throttle.Push, false, 5, 4, RoundOutcome.Wrong, false)]
[InlineData(Throttle.Redline, true, 5, 9, RoundOutcome.Correct, false)]
[InlineData(Throttle.Redline, false, 5, 3, RoundOutcome.SpunOut, true)]
[InlineData(Throttle.Redline, null, 1, 0, RoundOutcome.SpunOut, true)]          // floor at 0; no answer counts as wrong
public void Scoring_table(Throttle t, bool? correct, int from, int to, RoundOutcome outcome, bool spin)
    => Assert.Equal((to, outcome, spin), Scoring.Apply(from, t, correct, new RedlineRules()));

[Fact] public void Throttle_phase_hides_question_text_and_all_throttles()
{   // after seating: Phase Throttle, Question.Text null, Question.Answers null, every Drivers[i].Throttle null, deadline now+10s
}
[Fact] public void Question_phase_reveals_all_throttles_but_no_answers()
{   // Throttle(h,0,"redline") ×4 humans → phase advances early; every row has Throttle != null; every AnswerIndex == null
}
[Fact] public void Reveal_moves_players_by_the_table_and_reports_on_phone()
{   // P0 redline+right, P1 push+wrong, P2 no throttle (Cruise)+right, P3 cruise+no answer → advance 20s
    // distances 4,0,1,0; outcomes Correct,Wrong,Correct,NoAnswer; P0 phone message starts "Correct! +4 · now P"
}
[Fact] public void Ai_never_blocks_early_advance()
{   // 4 humans lock → Phase == Question without advancing time; 4 humans answer → Phase == Reveal
}
[Fact] public void Spin_out_sets_pit_limiter_for_one_round_only()
{   // P0 redline + wrong → next Throttle: Row(P0).Status == Limited, phone WaitView "Pit limiter: you're on Cruise this round.",
    // Throttle(h,0,"redline") ignored, Row(P0).Throttle == Cruise in Question; following round: Status == Choosing again
}
// Review Focus 3
[Fact] public void Pit_limited_player_still_answers_and_gains_cruise()
{   // as above, then AnswerRight(h,0) in the limited round → Distance += 1, Outcome Correct
}
[Fact] public void Disconnect_of_last_undecided_player_advances_immediately()
{   // 3 humans lock, SetConnected(3,false) → Phase == Question
}
[Fact] public void Views_are_reference_stable_while_state_unchanged()
{   // in Throttle: Assert.Same(h.PlayerView(1), h.PlayerView(1)) after P0 locks (P1's state unchanged); P0's view changes to the locked WaitView
}
[Fact] public void Malformed_and_wrong_phase_input_is_ignored()
{   // Input(0, new { nope = 1 }), an answer during Throttle, a throttle during Question → no state change, no exception
}
```

Write each commented test with the stated actions and assertions.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter Round`
Expected: failures.

- [ ] **Step 3: Implement `Scoring` and the round loop** following the rules above.

- [ ] **Step 4: Run the whole Logic test suite and confirm it passes**

Run: `dotnet test games/Redline/Redline.Logic.Tests`
Expected: all green, including the Task 5 tests.

- [ ] **Step 5: Commit** with `feat(redline): throttle/question/reveal loop with AI field and secrecy`.

---

### Task 7: Finish: race end, ranking, results, awards, determinism

**Files:**
- Create: `games/Redline/Redline.Logic/Race/Ranking.cs`
- Modify: `games/Redline/Redline.Logic/RedlineGame.cs`
- Test: `games/Redline/Redline.Logic.Tests/FinishTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class Ranking
{   // sort by Distance desc, then Correct desc; equal (Distance, Correct) share a position (1,2,2,4)
    public static IReadOnlyList<(Driver Driver, int Position)> Rank(IEnumerable<Driver> field);
    public static IReadOnlyList<Award> Awards(IReadOnlyList<(Driver Driver, int Position)> ranked);   // humans only, §2.4
    public static GameResults Results(IReadOnlyList<(Driver Driver, int Position)> ranked);          // humans only, §2.3
}
```

**Rules for this task:**
- After each Reveal, the race goes to Chequered if any car has `Distance ≥ FinishLine` or the round is `MaxRounds`. Otherwise it starts the next round.
- Chequered lasts 10 s and then calls `ctx.EndGame(Ranking.Results(...))`.
- `PlayerScore.Name` is `"{Player} ({CODE})"` and `Score` is the distance.
- **Message:**
  - human winner: `"{Player} ({CODE}) wins the race!"`;
  - AI winner: `"{Surname} (AI) wins the race · best player: {Player} ({CODE}), P{n}"`;
  - humans sharing P1: `"{A} ({CODE}) and {B} ({CODE}) share the win!"`.

  "Surname" is the last token of the driver's name.
- The `Award` titles are "Boldest driver", "Sharpest mind" and "Biggest crash". The `Detail` strings are `"{n} Redlines"`, `"{n} of {rounds} right"` and `"{n} spin-outs"`.

- [ ] **Step 1: Write the failing tests** (`FinishTests`)

```csharp
[Fact] public void Race_ends_after_reveal_when_a_car_reaches_20()      // drive P0 with redline+right ×5 → after the 5th Reveal: Phase Chequered; Results null until +10s
[Fact] public void Race_ends_after_round_15_without_a_finisher()       // all humans cruise, no answers, rules with AI accuracy forced to 0 (Easy=Medium=Hard=0, MinP=0) → Chequered after round 15
[Fact] public void Results_list_only_humans_in_race_order()             // 4 humans → Results.Scores.Count == 4; names "Player1 (NOR)" etc.; ordered by distance
[Fact] public void Ai_winner_message_names_best_player()               // rules: AI accuracy forced to 1 (MinP=MaxP=1); humans never answer → message "… (AI) wins the race · best player: Player1 (NOR), P"…
[Fact] public void Awards_go_to_humans_and_are_omitted_when_unearned()  // nobody spins → no "Biggest crash"; Boldest = most human Redlines
// Review Focus 4
[Fact] public void Two_finishers_same_reveal_bigger_overshoot_wins_equal_shares()
{   // Ranking.Rank on hand-built drivers: (22, 6 correct) beats (21, 6); (21, 6) and (21, 6) share position 1;
    // Results message for two humans sharing P1: "Player1 (NOR) and Player2 (VER) share the win!"
}
[Fact] public void Same_seed_gives_identical_host_views_every_step()
{   // two RedlineKit.Start(seed: 5) harnesses driven by the same inputs; after every input/Advance,
    // JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options) is equal; continue to the end of the race
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Logic.Tests --filter Finish`
Expected: failures.

- [ ] **Step 3: Implement `Ranking` and the end of the race** following the rules above.

- [ ] **Step 4: Run the whole Logic test suite and confirm it passes**

Run: `dotnet test games/Redline/Redline.Logic.Tests`
Expected: all green.

- [ ] **Step 5: Commit** with `feat(redline): chequered flag, ranking, humans-only results, awards`.

---

### Task 8: `OpenTdbClient`

**Files:**
- Create: `src/ArcBox.Server/Games/Redline/{OpenTdbOptions.cs, OpenTdbClient.cs}`
- Modify: `src/ArcBox.Server/ArcBox.Server.csproj` (add a `ProjectReference` to `games/Redline/Redline.Logic/Redline.Logic.csproj`)
- Create: `src/ArcBox.Server.Tests/Redline/{OpenTdbClientTests.cs, Support/FakeHandler.cs, Fixtures/opentdb-ok.json}` (the fixture is copied to output)

**Interfaces:**
- Produces:

```csharp
public sealed class OpenTdbOptions
{
    public string BaseUrl { get; set; } = "https://opentdb.com";
    public int Target { get; set; } = 150;  public int LowWater { get; set; } = 60;
    public TimeSpan MinRequestSpacing { get; set; } = TimeSpan.FromSeconds(5.5);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
    public int DedupeWindow { get; set; } = 500;  public bool Enabled { get; set; } = true;
}
public abstract record OpenTdbResult
{
    public sealed record Ok(IReadOnlyList<TriviaQuestion> Questions) : OpenTdbResult;
    public sealed record TokenEmpty : OpenTdbResult;        // response_code 3 or 4 → caller requests a new token
    public sealed record RateLimited : OpenTdbResult;       // response_code 5 or HTTP 429
    public sealed record Failed(string Reason) : OpenTdbResult;
}
public sealed class OpenTdbClient(HttpClient http)
{
    public Task<string?> RequestTokenAsync(CancellationToken ct);                         // GET api_token.php?command=request
    public Task<OpenTdbResult> FetchAsync(string? token, int amount, CancellationToken ct); // GET api.php?amount=..&type=multiple&encode=url3986[&token=..]
}
// test support
public sealed class FakeHandler : HttpMessageHandler { public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } public List<(HttpRequestMessage Req, DateTimeOffset At)> Requests { get; } public TimeProvider? Clock { get; set; } }
```

**Decoding:**
- Every string field is URL-decoded with `Uri.UnescapeDataString`.
- `difficulty` maps to the `Difficulty` enum, and the category drops a leading `"Entertainment: "` or `"Science: "`.
- **A question is dropped** if its decoded `type` isn't `multiple`, it doesn't have exactly 3 incorrect answers, any string is blank, or any two answers are equal (case-insensitive).
- `Origin = OpenTdb`.
- **Error mapping:** `response_code` 1, 2 or other codes → `Failed`. HTTP non-2xx (except 429), `HttpRequestException`, a timeout (`TaskCanceledException` without the caller's token being cancelled) and `JsonException` → `Failed`.

- [ ] **Step 1: Write the failing tests**
  - `Decodes_fixture_including_unicode_and_quotes`: the fixture holds 3 url3986-encoded questions, one containing `%22` and `%C3%A9`. Assert the decoded text, difficulty, normalised category `"Video Games"` and 3 incorrect answers.
  - `Maps_response_codes`: a theory over (3 → TokenEmpty), (4 → TokenEmpty), (5 → RateLimited), (1 → Failed).
  - `Maps_http_429_500_and_timeout`.
  - `Request_uses_amount_type_encode_and_token`: assert the query string.
  - **Review Focus 5:** `Malformed_questions_are_dropped_rest_kept`. A batch of 4 has one question with 2 incorrect answers, one with a blank text, and one whose correct answer equals an incorrect one ignoring case. Assert `Ok` with exactly 1 question.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test src/ArcBox.Server.Tests --filter OpenTdbClient`
Expected: compile errors.

- [ ] **Step 3: Implement the options and the client** per the decoding and error-mapping rules above. Deserialize into private DTOs (`response_code`, `results[]` with `type`, `difficulty`, `category`, `question`, `correct_answer` and `incorrect_answers`) using `JsonSerializerOptions` with `JsonNamingPolicy.SnakeCaseLower`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test src/ArcBox.Server.Tests --filter OpenTdbClient`
Expected: green.

- [ ] **Step 5: Commit** with `feat(redline): OpenTDB client with url3986 decoding and malformed-question filter`.

---

### Task 9: `OpenTdbQuestionPool` (warm pool `BackgroundService`)

**Files:**
- Create: `src/ArcBox.Server/Games/Redline/OpenTdbQuestionPool.cs`
- Test: `src/ArcBox.Server.Tests/Redline/OpenTdbQuestionPoolTests.cs`

**Interfaces:**
- Consumes: Task 8.
- Produces:

```csharp
public sealed class OpenTdbQuestionPool(OpenTdbClient client, IOptions<OpenTdbOptions> options, TimeProvider time, ILogger<OpenTdbQuestionPool> log) : BackgroundService
{
    public int Count { get; }
    public int TryTake(int count, List<TriviaQuestion> into);   // non-blocking ConcurrentQueue dequeues; returns how many were added
}
```

**Loop rules:**
- Fetch 50 when `Count < LowWater`, and keep fetching until `Count ≥ Target`.
- Wait at least `MinRequestSpacing` between requests, using `Task.Delay(..., time, ct)`.
- When idle, re-check every `MinRequestSpacing`.
- Request a token on the first fetch and again after `TokenEmpty`.
- On `RateLimited` or `Failed`, back off 5 s → 10 s → … capped at `MaxBackoff`, and reset the backoff after an `Ok`.
- Dedupe on the SHA-256 of the trimmed, lower-cased text, keeping a bounded FIFO of the last `DedupeWindow` hashes.
- Log at Warning on failures.
- Never throw out of `ExecuteAsync`, except `OperationCanceledException` on stop.

- [ ] **Step 1: Write the failing tests**

All of these use `FakeTimeProvider` and `FakeHandler`. The handler serves 50 unique questions per call unless a test says otherwise. Add a test helper `async Task Eventually(Func<bool> cond)` that polls with short real-time waits up to 5 s.
  - `Fills_to_target_in_spaced_requests`: start, then repeatedly `time.Advance(5.5 s)` until `Count ≥ 150`. Assert exactly 3 fetch requests (excluding the token request), and every gap between them is ≥ 5.5 s by `FakeHandler.Requests[i].At`.
  - `Requests_new_token_after_token_empty`: the first fetch returns code 4. Assert a second `api_token.php?command=request` call, then an Ok.
  - `Backs_off_and_caps_on_rate_limit`: always code 5. Assert the gaps go 5, 10, 20, 40… capped at 300 s.
  - `Dedupes_repeated_texts`: the handler returns the same 50 texts every time. Assert `Count` stays at 50 after 3 fetches.
  - `TryTake_dequeues_without_blocking_and_reports_count`: assert it takes at most the number available.
  - `Stops_cleanly_on_cancellation`: `StopAsync` completes, and no exception escapes.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test src/ArcBox.Server.Tests --filter OpenTdbQuestionPool`
Expected: compile errors.

- [ ] **Step 3: Implement the pool** per the loop rules above.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test src/ArcBox.Server.Tests --filter OpenTdbQuestionPool`
Expected: green, and stable over 5 repeated runs (`--repeat 5` if the runner supports it; otherwise run it 5 times).

- [ ] **Step 5: Commit** with `feat(redline): warm OpenTDB question pool with spacing, backoff and dedupe`.

---

### Task 10: `PooledQuestionSource` and `AddRedline`

**Files:**
- Create: `src/ArcBox.Server/Games/Redline/{PooledQuestionSource.cs, RedlineServiceCollectionExtensions.cs}`
- Test: `src/ArcBox.Server.Tests/Redline/{PooledQuestionSourceTests.cs, AddRedlineTests.cs}`

**Interfaces:**
- Consumes: Tasks 1, 3, 9.
- Produces:

```csharp
public sealed class PooledQuestionSource(OpenTdbQuestionPool pool, IOptions<OpenTdbOptions> options, ILogger<PooledQuestionSource> log) : IQuestionSource;
public static class RedlineServiceCollectionExtensions
{   // binds "Redline:OpenTdb"; TryAddSingleton(TimeProvider.System); AddHttpClient<OpenTdbClient>(BaseUrl, Timeout 10 s);
    // singleton OpenTdbQuestionPool always (PooledQuestionSource depends on it); AddHostedService resolving that same instance only when Enabled;
    // IQuestionSource → PooledQuestionSource; AddArcGame(sp => new RedlineGame(sp.GetRequiredService<IQuestionSource>()))
    public static IServiceCollection AddRedline(this IServiceCollection services, IConfiguration configuration);
}
```

**Rules:**
- `Take(count)` first calls `pool.TryTake` (skip this when `Enabled` is false). It fills the rest from `FallbackQuestions.All` in a shuffled order that cycles through the whole pack before repeating. The cursor is guarded by a `lock`, because rooms call in from different threads.
- Log at Warning with the number of fallback questions used.
- It always returns exactly `count` and never throws.

- [ ] **Step 1: Write the failing tests**
  - `Takes_from_pool_first_then_fallback`: a pool holding 10 and `Take(15)` gives 10 `OpenTdb` + 5 `Fallback`.
  - `Disabled_uses_fallback_only`.
  - `Always_returns_exactly_count`: try 0, 1, 15 and 200 (more than the pack, which forces a cycle).
  - `Parallel_takes_never_share_a_pool_question`: seed the pool with 400 unique questions. Run `Parallel.For(0, 8, _ => source.Take(15))` in 3 waves. Assert all `OpenTdb` texts are distinct.
  - `AddRedline_registers_game_and_source`: build from `ConfigurationBuilder` with `Redline:OpenTdb:Enabled=false`, plus `services.AddLogging()` and `services.AddSingleton<IGameRegistry, GameRegistry>()`. Also assert that no `IHostedService` is registered. Assert `GetRequiredService<IGameRegistry>()` (registered as in Task 1's test) lists `redline`, and that `IQuestionSource` resolves to `PooledQuestionSource`.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test src/ArcBox.Server.Tests --filter "PooledQuestionSource|AddRedline"`
Expected: compile errors.

- [ ] **Step 3: Implement** both classes per the rules and signatures above.

- [ ] **Step 4: Run the whole server test suite and confirm it passes**

Run: `dotnet test src/ArcBox.Server.Tests`
Expected: all green.

- [ ] **Step 5: Commit** with `feat(redline): pooled question source with fallback top-up and AddRedline`.

---

### Task 11: `Redline.Presentation`: `CircuitData` and `LapMath`

**Files:**
- Create: `games/Redline/Redline.Presentation/{Redline.Presentation.csproj, THIRD_PARTY.md, Data/cota.json, CircuitData.cs, LapMath.cs}`. Copy `cota.json` from the mockups folder and embed it as `Redline.Presentation.Data.cota.json`.
- Create: `games/Redline/Redline.Presentation.Tests/{Redline.Presentation.Tests.csproj, CircuitDataTests.cs, LapMathTests.cs}`
- Modify: `ArcBox.slnx`

**Interfaces:**
- Produces (namespace `Redline.Presentation`; `Vector2` is `System.Numerics.Vector2` in x = east, y = south metres):

```csharp
public sealed record CircuitData(string Name, IReadOnlyList<Vector2> Points, IReadOnlyList<(double S, double H)> Elevation)
{
    public double LapLength { get; }               // closed polyline length
    public static CircuitData Load(string id);     // "cota" → embedded resource; unknown id → KeyNotFoundException
}
public readonly record struct PackSlot(double Lateral, double Back);   // metres; +Lateral = left of travel; Back = metres behind
public static class LapMath
{
    public static double MetersAlongLap(double distance, int finishLine, double lapLength);   // distance / finishLine × lapLength, wrapped to [0, lapLength)
    public static PackSlot Pack(int indexInGroup, int groupSize);   // 1: (0,0); 2: (−3.4,0),(+3.4,0); ≥3: lateral [−4.4,0,+4.4][i%3], back 9×(i/3)
    public static PackSlot Grid(int gridPosition);                  // 0-based: lateral −3.6 (even) / +3.6 (odd), back 8×position
    public static double Elevation(CircuitData c, double s);        // cosine interpolation between keyframes, s wrapped
}
```

- [ ] **Step 1: Write the failing tests**
  - `Cota_loads_170_points_and_5512m_lap`: `LapLength` in [5507, 5517].
  - `Distance_0_and_20_are_the_same_point`: `MetersAlongLap(20, 20, L) == 0`, and `MetersAlongLap(10, 20, L) == L / 2`.
  - `Pack_slots`: a theory covering (1 → 0,0), (2 → ±3.4), (3 → −4.4, 0, 4.4, back 0) and (6 → index 3..5 back 9).
  - `Grid_two_columns_8m_apart`: `Grid(0) == (-3.6, 0)`, `Grid(1) == (3.6, 8)` and `Grid(21) == (3.6, 168)`.
  - `Elevation_turn1_apex_is_41m`: `Elevation(c, 327) == 41`, and `Elevation(c, 3459) == 0`.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Presentation.Tests`
Expected: compile errors.

- [ ] **Step 3: Implement both classes** per the signatures above.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test games/Redline/Redline.Presentation.Tests`
Expected: green.

- [ ] **Step 5: Commit** with `feat(redline): presentation library with COTA data and lap maths`.

---

### Task 12: `TrackGeometry`: resampling, curvature, width and the fold guard

**Files:**
- Create: `games/Redline/Redline.Presentation/TrackGeometry.cs`
- Test: `games/Redline/Redline.Presentation.Tests/TrackGeometryTests.cs`

**Interfaces:**
- Produces:

```csharp
public readonly record struct TrackSample(double S, Vector2 Position, Vector2 Tangent, Vector2 Left, double Height, double Curvature, int Turn);
public static class TrackGeometry
{
    public const double Step = 2.0;  public const int Window = 15;  public const double CornerThreshold = 0.22;
    public static IReadOnlyList<TrackSample> Resample(CircuitData c);       // closed centripetal Catmull-Rom (alpha 0.5), arc-length spaced every Step m
    public static double Width(double s);                                    // 15 + 9 × bump(s; 120, 200, 400, 470)
    public static bool IsCorner(TrackSample t) => t.Curvature > CornerThreshold;
    public static double LocalRadius(TrackSample t) => 2 * Window * Step / Math.Max(t.Curvature, 1e-3);
    public static bool WallAllowed(TrackSample t, int side, double offsetFromCentre);   // side +1 = left, −1 = right
}
```

These conventions must match the mockup's `cota3d.js` exactly:
- `Left = (Tangent.Y, −Tangent.X)`.
- `Curvature` = the angle between the tangents `Window` samples behind and ahead.
- `Turn = Math.Sign(a.X*b.Y − a.Y*b.X)`, which is +1 for a right turn.
- In `WallAllowed`, inner means `side != (t.Turn > 0 ? +1 : −1)`. A wall is disallowed when it is inner **and** `offsetFromCentre > 0.7 × LocalRadius(t)`.
- `bump` is a smoothstep ramp up from a to b, a plateau from b to c, and a ramp down from c to d.

- [ ] **Step 1: Write the failing tests**
  - `Resample_is_closed_and_evenly_spaced`: about 2,756 samples (±2), consecutive positions 2 m ± 0.05 apart, and the last sample wraps to the first.
  - `Turn1_is_a_left_hairpin_corner`: at the sample nearest s = 327, `IsCorner` is true and `Turn == -1`.
  - `Main_straight_is_not_a_corner`: the sample at s = 5300.
  - `Width_is_24_at_turn1_and_15_elsewhere`: `Width(300) == 24` and `Width(1000) == 15`.
  - `Fold_guard_drops_inner_wall_at_turn1_only`: at s ≈ 327, the inner side with offset `Width/2 + 19` is false and the outer side is true. At s = 5300, both sides are true.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Presentation.Tests --filter TrackGeometry`
Expected: compile errors.

- [ ] **Step 3: Implement** per the conventions above.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test games/Redline/Redline.Presentation.Tests`
Expected: green.

- [ ] **Step 5: Commit** with `feat(redline): track geometry with curvature, width profile and wall fold guard`.

---

### Task 13: `CameraDirector`

**Files:**
- Create: `games/Redline/Redline.Presentation/CameraDirector.cs`
- Test: `games/Redline/Redline.Presentation.Tests/CameraDirectorTests.cs`

**Interfaces:**
- Consumes: `RedlineHostView` and `DriverRow` (Task 5).
- Produces:

```csharp
public abstract record Shot;
public sealed record LineupShot(string? Subject) : Shot;                    // Driver Select; Subject = newly claimed code
public sealed record GridShot : Shot;
public sealed record HeliShot(IReadOnlyList<string> Subjects) : Shot;       // 1–2 driver codes
public sealed record TracksideShot(string Subject) : Shot;
public sealed record FinishShot : Shot;
public static class CameraDirector
{
    public static Shot Pick(RedlineHostView view, RedlineHostView? previous);
    public static Shot RevealFollowUp(RedlineHostView view);                // Heli on the overall leader, 3.5 s into Reveal
}
```

**Rules (§4.9):**
- A candidate must involve a human (`Player != null`). AI-only candidates are used only when no human candidate exists.
- **DriverSelect:** `LineupShot` of a code that has `Player` set now but not in `previous`. If there is none, the subject is null.
- **Grid:** `GridShot`.
- **Throttle:** `HeliShot([best-placed human])`.
- **Question:** the adjacent pair in ranking order with the smallest `Distance` gap that includes a human. Ties go to the higher position. Returns `HeliShot([a, b])`.
- **Reveal:** `TracksideShot` of the best-placed human with `Outcome == SpunOut`. Otherwise, the human with the largest `Distance − PreviousDistance`, with ties to the better position.
- **Chequered:** `FinishShot`.

- [ ] **Step 1: Write the failing tests** with hand-built `RedlineHostView`s, one per rule:
  - `DriverSelect_follows_the_new_claim`
  - `Grid_and_chequered`
  - `Throttle_heli_on_best_placed_human`
  - `Question_closest_battle_with_a_human`
  - `Question_tie_goes_to_higher_position`
  - `Ai_only_battle_never_chosen_while_a_human_battle_exists`: AI gap 0 vs human gap 1 → the human pair.
  - `Reveal_spin_out_beats_biggest_gainer`
  - `Reveal_biggest_human_gainer`
  - `Reveal_follow_up_is_leader`

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test games/Redline/Redline.Presentation.Tests --filter CameraDirector`
Expected: compile errors.

- [ ] **Step 3: Implement the director** per the rules above.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test ArcBox.slnx`
Expected: the whole solution is green.

- [ ] **Step 5: Commit** with `feat(redline): players-first camera director`.

---

### Task 14: Register REDLINE in the server (gated on M1 Task 10)

**Precondition:** M1 Task 10 (`ArcHub` + `Program.cs` with `AddArcGame<CounterGame>()`) is merged. If `Program.cs` is still the hello-world template, stop and report that this task is blocked.

**Files:**
- Modify: `src/ArcBox.Server/Program.cs` (one line after `AddArcGame<CounterGame>()`)
- Modify: `src/ArcBox.Server/appsettings.Development.json` (add `"Redline": { "OpenTdb": { "Enabled": false } }`, so local dev and CI never call opentdb.com)
- Test: the existing hub integration tests in `src/ArcBox.Server.Tests`

- [ ] **Step 1: Write the failing test.** Add `Lobby_lists_redline` to the hub integration tests: a joined phone's `LobbyChanged.Games` contains the id `redline`, using the existing `RoomFixture`.
- [ ] **Step 2: Run it and confirm it fails.** Run `dotnet test src/ArcBox.Server.Tests --filter Lobby_lists_redline`. Expected: FAIL (no `redline`).
- [ ] **Step 3: Add `builder.Services.AddRedline(builder.Configuration);`** and the development setting.
- [ ] **Step 4: Run the whole suite and confirm it passes.** Run `dotnet test ArcBox.slnx`. Expected: all green.
- [ ] **Step 5: Commit** with `feat(server): register REDLINE`.
