# REDLINE — Racing Trivia Game Design Spec

- **Date:** 2026-10-25
- **Status:** Draft, awaiting review
- **Author:** higharc91 with Claude
- **Platform spec:** [2026-10-18-arcbox-platform-design.md](2026-10-18-arcbox-platform-design.md). Section numbers written as "platform §" refer to it.
- **Mockups:** [`docs/superpowers/mockups/trivia-racer/`](../mockups/trivia-racer/). The HTML sources and PNG renders are the visual spec. Re-render them with `npm install && node shoot.js`.
  - **TV (current):** `11-cota-grid.png`, `09-cota-question.png`, `10-cota-reveal.png`. These are realistic 3D at Circuit of the Americas, rendered with three.js as a stand-in for the Godot scene.
  - **HUD and phone layout:** `03-phase-throttle.png`, `04-phase-reveal.png`, `05-phase-finish.png`. These still define the Broadcast graphics and phone screens; the 3D scene replaces only the flat track area.
  - **Superseded:** `07`/`08` (the low-poly diorama) and the flat-track areas of `02b`–`05`.

---

## 1. Purpose

REDLINE is ArcBox's first game. It **replaces Prompt Duel** (platform §4.3) as the game that milestone M4 and success criterion 1 are measured against.

It is a racing trivia game for 2–8 players, shown on the TV as a **realistic 3D race at a real circuit** (Circuit of the Americas first). A race is one lap of the circuit, played over up to 15 **rounds**, one question each. Before seeing the question, each driver sees only its **category and difficulty** and secretly bets a **throttle**. Right answers move your car forward by your throttle. Wrong answers at high throttle cost ground. First past the flag wins.

### What we were told
- A trivia game with a twist, themed around racing.
- The twist is **Throttle**: bet on what you know before you see the question (chosen from three concepts, see `01-concepts.png`).
- Questions come **live from Open Trivia DB** (opentdb.com).
- A **clean, stylized UI**. The chosen visual direction is **Broadcast**: an F1-TV-style graphics package (see `02b-style-broadcast.png`).
- **3D cars on a real race track**, in a **realistic broadcast** look. The first circuit is **COTA** (Circuit of the Americas). Cars and track are **built in Godot**, with no external model assets.
- A race is **one circuit lap**, and each question is a **round**.
- The OpenTDB question pool lives **inside `ArcBox.Server`**.

### What we're assuming
- Groups play 2–8 players, and some are remote, watching the TV over a laggy Discord stream. No mechanic may reward reaction speed.
- One race should take about 8–12 minutes.
- Content is English only, matching OpenTDB's default.

### Success criteria
1. A group plays a full race on the platform from Grid to Chequered flag, and the room returns to the lobby with the results.
2. A race starts and completes with opentdb.com unreachable, using fallback questions.
3. No HostView sent during the Throttle or Question phase contains any driver's throttle or answer choice. This is enforced by tests.
4. Two rooms starting races at the same moment never receive the same OpenTDB question.
5. All game logic is covered by `GameHarness` tests that are deterministic for a fixed seed.
6. On the group's TV PC the 3D scene holds **60 fps at 1080p** on the High preset. The Low preset holds **≥ 30 fps on integrated graphics** (§4.8).
7. Every car is identifiable at all times: either on screen with its name tag, or as a dot on the track map.

---

## 2. Rules

### 2.1 The race
- The track is **20 units** long (`FinishLine`). Every car starts at 0.
- The track is drawn as **one lap of the circuit**: distance 0 is the start/finish line and 20 is the line again.
- A race has at most **15 rounds** (`MaxRounds`). One round is one question.
- The race ends after the Reveal of any round where at least one car's distance is **≥ 20**, or after round 15's Reveal, whichever comes first.
- Distance can overshoot the line (e.g. 21), and the overshoot counts for ranking.

### 2.2 Throttle and scoring

| Throttle | Correct | Wrong or no answer |
|---|---|---|
| **CRUISE** | +1 | 0 |
| **PUSH** | +2 | −1 |
| **REDLINE** | +4 | −2 and **spin out** |

- **Spin out:** the driver gets the **pit limiter** for the next round. They are locked to Cruise, and their phone skips the throttle screen.
- Distance never drops below 0.
- A driver who makes no throttle pick before the deadline is set to Cruise.
- A driver who makes no answer before the deadline is treated as wrong.
- All numbers above live in one `RedlineRules` record (§4.3).

### 2.3 Ranking and results
1. Order by distance, descending.
2. Break ties by the number of correct answers, descending.
3. Drivers still tied **share** a position number. The next position skips accordingly (1, 2, 2, 4).
4. `GameResults.Scores` are the final distances in ranking order. `PlayerScore.Score` is the distance.
5. `GameResults.Message` is `"{Name} wins!"`, or `"{Name} and {Name} share the win!"` for a tie at P1.

### 2.4 Awards (Chequered flag screen)

| Award | Winner | Requirement |
|---|---|---|
| **Boldest driver** | Most Redline picks | At least 1 Redline |
| **Sharpest mind** | Most correct answers | At least 1 correct |
| **Biggest crash** | Most spin-outs | At least 1 spin-out |

- An award with no qualifying driver is omitted.
- Ties go to the better-ranked driver.

### 2.5 Fairness and secrecy
- There's no speed bonus anywhere. The question and answers show **on the phone** as well as the TV, so stream lag can't cost remote players.
- Throttles are shown only as LOCKED during the Throttle phase. All throttles are revealed together when the Question phase begins.
- Answer picks are hidden until Reveal.

---

## 3. Flow

### 3.1 Phases

Each phase uses the single timer key `"phase"`. Advancing early cancels that timer.

| Phase | Duration | Ends early when | TV (HostView) | Phone (ControllerView) |
|---|---|---|---|---|
| **Grid** (once) | 6 s | — | Drivers on the start line, rules card, OpenTDB attribution | `WaitView("Engines on. Lights out in a moment…")` |
| **Throttle** | 10 s | Every connected driver is locked | Category, difficulty, LOCKED/CHOOSING per driver (`03-phase-throttle.png`) | `ButtonsView` (3 throttles with hints), or a wait view once locked or pit-limited |
| **Question** | 20 s | Every connected driver has answered | Question, 4 answers, all throttles revealed (`02b-style-broadcast.png`) | `ChoiceView` (4 options, single select), or a wait view once answered |
| **Reveal** | 7 s | — | Correct answer, who picked what, movement, ▲▼ re-sort (`04-phase-reveal.png`) | `WaitView` with your personal round result |
| **Chequered** (once) | 10 s | — | Podium, classification, awards (`05-phase-finish.png`) | `WaitView("You finished P{n} of {total}.")` |

After Reveal, the race either goes to the next round's Throttle phase or, if it has ended (§2.1), to Chequered. Chequered's timer calls `ctx.EndGame(results)`.

### 3.2 Early advance and disconnects
- "Every connected driver" counts only drivers whose connection is currently up. A pit-limited driver counts as locked from the start of the Throttle phase.
- If the last undecided driver disconnects, the phase advances immediately. The check runs in `OnPlayerConnectionChanged` too.
- Disconnected drivers keep their car and get the defaults from §2.2. A rejoining driver sees the current phase's view immediately (the platform re-sends the snapshot).
- If **no** drivers are connected, phases run on their timers alone, and the race finishes on defaults.

### 3.3 Phone views in detail

Every controller view sets `Header = "P{position} · ROUND {round}/{maxRounds}"`. Only the Grid view leaves it null.

**The live countdown comes from `DeadlineUtc`, never from the view itself.** Per the SDK, any change to a view mints a new `ViewId` and voids input already in flight. So a player's view changes only when that player's own state changes.

| State | View |
|---|---|
| Throttle, choosing | `ButtonsView(Prompt: "{Category} · {Difficulty}. How hard do you push?", Buttons: cruise / push / redline)`, with `Option.Hint` set to `"+1 if right · nothing lost"`, `"+2 if right · −1 if wrong"` and `"+4 if right · spin out if wrong"` |
| Throttle, locked | `WaitView("Locked in: {THROTTLE}. Waiting for the other drivers…")` |
| Throttle, pit limiter | `WaitView("Pit limiter: you're on Cruise this round.")` |
| Question, unanswered | `ChoiceView(Prompt: question text, Options: a/b/c/d in shuffled order)`. The Header becomes `"{THROTTLE} · P{position} · ROUND {round}/{maxRounds}"` |
| Question, answered | `WaitView("Answer locked. Eyes on the TV!")` |
| Reveal | `WaitView` with one of: `"Correct! +{n} · now P{p}"`, `"Wrong answer. No change · P{p}"` (Cruise), `"Wrong answer. −1 · now P{p}"` (Push), or `"Spun out! −2 · pit limiter next round · now P{p}"` (Redline). A missing answer uses the same wording with "No answer" in place of "Wrong answer". |

**Inputs accepted:**
- Throttle phase: `ButtonsInput.Pressed` ∈ {`cruise`, `push`, `redline`}.
- Question phase: `ChoiceInput.Selected[0]` ∈ {`a`, `b`, `c`, `d`}.

Anything else is ignored: wrong phase, already decided, pit-limited, an unknown id, or malformed JSON (caught as `JsonException`, as `CounterGame` does).

---

## 4. Architecture

See `06-architecture.png`.

### 4.1 Where the code lives

| Path | Contents | Depends on |
|---|---|---|
| `games/Redline/Redline.Logic` | `RedlineGame`, `RedlineRules`, view records, `IQuestionSource`, `TriviaQuestion`, `FallbackQuestions` (embedded `fallback-questions.json`) | `ArcBox.Sdk` |
| `games/Redline/Redline.Logic.Tests` | Game tests, `FakeQuestionSource` | Logic, `ArcBox.Sdk.Testing` |
| `src/ArcBox.Server/Games/Redline/` | `OpenTdbClient`, `OpenTdbQuestionPool`, `PooledQuestionSource`, `OpenTdbOptions`, `RedlineServiceCollectionExtensions.AddRedline()` | `Redline.Logic` |
| `src/ArcBox.Server.Tests/Redline/` | Client, pool and source tests | Server |

The TV-side projects (`Redline.Presentation` and the Godot scene) are listed in §4.8.

The `Redline.Logic` project targets `$(ArcSdkTargetFrameworks)` like every Logic project. `IQuestionSource` stays in `Redline.Logic`. If a second trivia game appears, it can move to the SDK.

### 4.2 The async/sync seam

```csharp
namespace Redline.Logic;

public enum Difficulty { Easy, Medium, Hard }
public enum QuestionOrigin { OpenTdb, Fallback }

public sealed record TriviaQuestion(
    string Category, Difficulty Difficulty, string Text,
    string Correct, IReadOnlyList<string> Incorrect,    // exactly 3 incorrect answers
    QuestionOrigin Origin);                             // drives the HostView attribution line

public interface IQuestionSource
{
    // Called inside the room actor under the 2 s watchdog: must return exactly `count`
    // questions from memory, never block, never throw.
    IReadOnlyList<TriviaQuestion> Take(int count);
}
```

- `RedlineGame` takes `IQuestionSource` in its constructor and calls `Take(rules.MaxRounds)` **in `Start`, never in the constructor**. `GameRegistry` constructs one throwaway instance just to read the manifest.
- The game shuffles each question's four answers with `ctx.Random` in `Start`. Answer order is therefore deterministic for a given seed.

### 4.3 Game

```csharp
public sealed record RedlineRules
{
    public int FinishLine { get; init; } = 20;
    public int MaxRounds { get; init; } = 15;
    public string CircuitId { get; init; } = "cota";   // which circuit the TV draws; data only, no logic effect
    public TimeSpan Grid { get; init; } = TimeSpan.FromSeconds(6);
    public TimeSpan Throttle { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan Question { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan Reveal { get; init; } = TimeSpan.FromSeconds(7);
    public TimeSpan Chequered { get; init; } = TimeSpan.FromSeconds(10);
    // (gain if right, loss if wrong) per throttle; a wrong REDLINE also spins out.
    public int CruiseGain { get; init; } = 1;
    public int PushGain { get; init; } = 2;   public int PushLoss { get; init; } = 1;
    public int RedlineGain { get; init; } = 4; public int RedlineLoss { get; init; } = 2;
}

public sealed class RedlineGame(IQuestionSource questions, RedlineRules? rules = null) : IGame
{
    // Manifest: Id "redline", Name "REDLINE", MinPlayers 2, MaxPlayers 8.
}
```

- Each driver's state lives in a private `Driver` class with these fields: `Distance`, `Throttle?`, `AnswerIndex?`, `PitLimiter`, `Correct`, `Redlines`, `SpinOuts`, `PositionAtRoundStart`, `Connected`, `Livery`.
- `Livery` is the player's join index, 0–7. The TV maps it to a team color.
- The phase lives in `RacePhase { Grid, Throttle, Question, Reveal, Chequered }`.
- The game follows the platform style rules: no I/O, no threads, no statics, and all time and randomness come from `ctx`.

### 4.4 HostView: secret by construction

```csharp
public sealed record RedlineHostView(
    RacePhase Phase, int Round, int MaxRounds, int FinishLine, string CircuitId,
    QuestionCard? Question,             // null in Grid; Throttle: Text and Answers are null
    int? CorrectIndex,                  // Reveal only
    IReadOnlyList<DriverRow> Drivers,   // sorted by ranking (§2.3)
    DateTimeOffset? DeadlineUtc,
    FinishSummary? Finish,              // Chequered only
    string Attribution);                // "Questions: Open Trivia DB (CC BY-SA 4.0)" or the fallback credit

public sealed record QuestionCard(string Category, Difficulty Difficulty, string? Text, IReadOnlyList<string>? Answers);

public sealed record DriverRow(
    PlayerId Id, string Name, int Livery, int Position, int PositionChange,
    int Distance, int PreviousDistance, DriverStatus Status,
    Throttle? Throttle,                 // null during Grid and Throttle
    int? AnswerIndex,                   // null until Reveal
    RoundOutcome? Outcome,              // Reveal only
    bool PitLimiter, bool Connected);

public enum DriverStatus { Ready, Choosing, Locked, Limited, Answering, Answered }
public enum Throttle { Cruise, Push, Redline }
public enum RoundOutcome { Correct, Wrong, NoAnswer, SpunOut }

public sealed record FinishSummary(IReadOnlyList<Award> Awards);
public sealed record Award(string Title, PlayerId Winner, string Name, string Detail);  // e.g. "Boldest driver", "6 Redlines"
```

- `PositionChange` is the driver's position at the start of the round minus their current position, so positive means they gained places. It is nonzero only during Reveal.
- `PreviousDistance` is the distance at the start of the round. The TV drives each car from `PreviousDistance` to `Distance` during Reveal.

### 4.5 Question pool (in `ArcBox.Server`)

**`OpenTdbClient`** is a typed `HttpClient`.
- It requests `GET {BaseUrl}/api.php?amount=50&type=multiple&encode=url3986&token={token}` and `api_token.php?command=request|reset`.
- It decodes every string with `Uri.UnescapeDataString`, so no HTML entities ever reach a client.
- It normalizes categories by stripping the `"Entertainment: "` and `"Science: "` prefixes, so "Entertainment: Video Games" becomes "Video Games".
- It returns a discriminated result: `Ok(questions)`, `TokenEmpty`, `RateLimited` or `Failed(reason)`.
- Its only `HttpClient` setting is a 10 s timeout. There's deliberately **no** retry handler, because OpenTDB reports rate limits and token exhaustion in the response body (`response_code` 4/5) and the pool owns retry policy. A generic handler would retry straight into the rate limit.

**`OpenTdbQuestionPool : BackgroundService`** keeps questions warm.
- It holds a `ConcurrentQueue<TriviaQuestion>`. The refill loop:
  - requests when the count is below `LowWater` (60) and stops at or above `Target` (150);
  - waits at least `MinRequestSpacing` (5.5 s) between requests, using `TimeProvider.Delay`;
  - on `TokenEmpty`, resets the token and continues;
  - on `RateLimited` or `Failed`, backs off exponentially from 5 s, doubling, capped at `MaxBackoff` (5 min), and resets after a success.
- It dedupes by the SHA-256 of the question text against a bounded recent set (`DedupeWindow`, 500).
- It exposes `int TryTake(int count, List<TriviaQuestion> into)`, which dequeues up to `count` questions without blocking.
- Rooms run on different threads, and `ConcurrentQueue.TryDequeue` is atomic, so two rooms can never receive the same question.
- It never throws out of `ExecuteAsync`, except `OperationCanceledException` on shutdown.

**`PooledQuestionSource : IQuestionSource`** (singleton) takes from the pool first. It tops up any shortfall from `FallbackQuestions` in a shuffled order, cycling through the whole pack before repeating. A shortfall is logged at Warning with the number of fallback questions used.

**`OpenTdbOptions`** binds from `Redline:OpenTdb` in configuration: `BaseUrl` (`https://opentdb.com`), `Target`, `LowWater`, `MinRequestSpacing`, `MaxBackoff`, `DedupeWindow` and `Enabled` (default true).
- When `Enabled` is false, the pool never runs and every question comes from the fallback pack. This is for offline development and sandboxes where opentdb.com is blocked.

**`AddRedline(IConfiguration)`** wires everything up:
- binds the options;
- registers `TimeProvider.System` if absent;
- adds the typed client;
- registers the pool as a singleton that is also the hosted service;
- registers `IQuestionSource` → `PooledQuestionSource`;
- calls `AddArcGame(sp => new RedlineGame(sp.GetRequiredService<IQuestionSource>()))`.

### 4.6 Fallback pack
- The fallback pack is `fallback-questions.json`, embedded in `Redline.Logic`. It holds **60 original questions** (not copied from OpenTDB), with the same shape as `TriviaQuestion`.
- That's 8 categories with 7–8 questions each: General Knowledge, Science & Nature, Geography, History, Film, Music, Sports and Video Games. Easy, medium and hard are mixed roughly evenly.
- `FallbackQuestions.All` loads the pack once (lazily) and validates it: exactly 3 incorrect answers, no blank strings, and no duplicate texts.
- Fallback questions carry `Origin = Fallback`. Attribution switches to "Questions: REDLINE fallback pack" when a race used only fallback questions. If any question has `Origin = OpenTdb`, the race shows the OpenTDB credit.

### 4.7 Platform changes

Both changes are additive and backward-compatible.

1. **`ArcBox.Sdk`:** `Option(string Id, string Label, string? Hint = null)`. Phones (M2) render the hint as a second line, and older views serialize the same way.
2. **`ArcBox.Server`:** `AddArcGame<TGame>(this IServiceCollection, Func<IServiceProvider, TGame> factory) where TGame : IGame`. It registers `GameRegistration` through a service-provider factory, so the closure can resolve dependencies. The existing `new()` overload is unchanged.

### 4.8 TV: realistic 3D race (Godot, built in M3/M4)

The TV scene is a **renderer of `RedlineHostView`**. It never decides outcomes (platform §6.2). Everything it shows is derived from the view's distances, outcomes and phase.

**Projects:**

| Path | Contents | Depends on |
|---|---|---|
| `games/Redline/Redline.Presentation` | **Pure C#, no Godot:** `CircuitData` (loads circuit JSON), `LapMath` (distance → metres along the lap, grid slots, side-by-side offsets), `TrackGeometry` (ribbon/offset maths, including the fold guard below), `CameraDirector` (§4.9) | `Redline.Logic` |
| `games/Redline/Redline.Presentation.Tests` | xUnit tests for all of the above | Presentation |
| `src/ArcBox.Host/games/redline/` | Godot scene, `@tool` track builder, car builder, HUD, `circuits/cota.json`, `THIRD_PARTY.md` | Presentation |

Keeping the maths out of Godot types means the hard parts (where a car sits, which shot to cut to, where geometry folds) are unit-tested like game logic. The Godot layer just maps the results onto nodes.

**Circuit data.** `circuits/cota.json` is the file built for the mockups:
- 170 centerline points in metres, from bacinger/f1-circuits (MIT, attributed in `THIRD_PARTY.md`), 5,512 m long, counter-clockwise, with point 0 on the main straight;
- an **approximate** elevation profile, including the 41 m climb to Turn 1.

A circuit is pure data: adding one means adding one JSON file, then picking it with `RedlineRules.CircuitId`.

**Track.**
- A `@tool` script builds an `ArrayMesh` from a closed `Curve3D` (centripetal Catmull-Rom through the points, resampled every 2 m) and saves it as a `.res`, so nothing is generated at runtime.
- The layers are: asphalt (variable width, 15 m and 24 m at Turn 1); white edge lines; red/white curbs where curvature exceeds a threshold; COTA's red/blue painted runoff on the outside of corners; and one-sided advertising walls with a catch fence at 19 m.
- `CSGPolygon3D` isn't used: it can't vary width or place features per corner, and a 5.5 km CSG extrusion is slow to rebuild.
- **Fold guard:** a wall offset beyond about 0.7 × the local radius of curvature folds back on itself, so it is skipped on the inside of tight corners (the Turn 1 hairpin). `TrackGeometry` owns and tests this rule.
- Terrain is a generated heightmap mesh that matches the track elevation near the asphalt and eases to rolling ground about 250 m out. Trees are `MultiMeshInstance3D` clusters of live oaks.
- **Landmarks:** the main grandstand and pit building, the start gantry with five lights, the Turn 1 grandstand, and the Observation Tower.

**Cars.**
- A `CarBuilder` assembles a modern F1 car at real scale (5.6 m long, 2.0 m wide) from primitive and extruded meshes, as in the mockup: a tub with an extruded side profile, a tapered nose, rounded sidepods, a halo tube, multi-element wings, wishbones, mirrors, and tyres with the compound sidewall stripe.
- Paint is `StandardMaterial3D` with clearcoat, tinted per `Livery` (8 team colours).
- A car is a single scene, so swapping in a modelled `.glb` later changes nothing else.

**Placement and motion.**
- `LapMath` maps distance *d* to metres along the lap: `s = d / FinishLine × lapLength`.
- Cars that share a distance sit side by side (±3.4 m); a third car drops back about 11 m.
- The Grid uses F1 stagger: two columns 8 m apart.
- **In Reveal**, each car tweens along the curve from `PreviousDistance` to `Distance` over 4 s. Ease-out is used, and higher gains drive visibly faster.
- A spin-out plays a yaw tween with `GPUParticles3D` tyre smoke and slides the car back.

**HUD** (`CanvasLayer` + `Control`, Broadcast style):
- the question panel, timing tower, answers, round result lower-third and rules strip, as mocked;
- a **live track map** (`Line2D` of the circuit plus coloured dots per car);
- **name tags** projected with `Camera3D.unproject_position`.

**Rendering:**
- Forward+ renderer.
- `WorldEnvironment`:
  - a sky shader (a gradient dome with a sun glow; the physical sky blows out at track exposure) that also feeds reflections;
  - ACES tonemapping, SSAO and subtle glow.
- One shadowed `DirectionalLight3D`, with shadow distance limited to the camera's subject.
- **Presets:**
  - **High:** SSAO on, 4096 shadows, MSAA 4×.
  - **Low:** Mobile renderer, no SSAO, 2048 shadows, FXAA, trees halved.
- **Livery colours**, in join order: `#2f6fd6`, `#d8001f`, `#ff7a00`, `#1ec8b0`, `#b9bcc4`, `#1f8f5f`, `#ffd12e`, `#b04cff`.

### 4.9 Camera director

`CameraDirector.Pick(RedlineHostView view, RedlineHostView? previous) → Shot` is a pure function. The Godot layer turns a `Shot` into a camera rig and cut.

- **Shot kinds:**
  - `Grid` (helicopter behind the grid looking down the straight);
  - `Heli(subjects)` (frames one or two cars from about 60 m);
  - `Trackside(subject, side)` (a low front-quarter camera relative to the car);
  - `Finish` (start/finish line, then podium).
- **Rules by phase:**
  - Grid → `Grid`.
  - Throttle → `Heli` on the leader.
  - Question → `Heli` on the **closest battle**: the pair of adjacent cars with the smallest gap, ties going to the higher position.
  - Reveal → `Trackside` on a spun-out car if there is one (drama first). Otherwise the **biggest gainer**, ties going to the better position. After 3.5 s, cut to `Heli` on the new leader.
  - Chequered → `Finish`.
- Cars off screen are always on the track map, so no rule needs to show everyone at once. That's success criterion 7.

### 4.10 Phones (web/play, M2)
- The root element gets `data-game="{SelectedGameId}"` while a game runs.
- `redline.css` restyles the kit, scoped to `[data-game="redline"]`: Titillium Web, the `#e10600` accent, square corners, and throttle buttons coloured by id (cruise grey, push white, redline red).
- This needs no SDK change.

Until M2/M3 ship, the mockups are the reference, and REDLINE is fully playable through the default controller kit.

---

## 5. Failure handling

| Failure | Behavior |
|---|---|
| opentdb.com unreachable, slow, or the server just cold-started | The pool is short, so `PooledQuestionSource` fills from the fallback pack. The race starts on time. Warning log. |
| OpenTDB `response_code` 5 or HTTP 429 | Backoff (§4.5). Rooms are unaffected. |
| OpenTDB `response_code` 4 (token exhausted) | Reset the token and continue. |
| Malformed OpenTDB payload | That batch is dropped with a Warning, and the request counts as `Failed`. |
| Fallback pack exhausted within one server session | It reshuffles and repeats. Repeats are better than a failed start. |
| Bad, stale or wrong-phase input | Ignored (§3.3). The platform already drops stale `viewId`s. |
| Driver disconnects mid-round | Defaults apply, and early advance re-checks (§3.2). |
| Game exception | Platform behavior (platform §5.4): log, end the game, return to the lobby. |

---

## 6. Testing

TDD: tests come before implementation.

**`Redline.Logic.Tests`** (`GameHarness` + `FakeQuestionSource`):
- Manifest values. The constructor never calls `Take`, and `Start` calls `Take(15)` exactly once.
- The phase sequence and every timer duration. Early advance in Throttle and Question. An early advance triggered by a disconnect.
- Each scoring-table cell: three throttles × right/wrong/no-answer. The 0-floor. A spin-out sets the pit limiter, and the limiter clears after one round. A pit-limited driver's throttle input is ignored.
- The race ends at ≥ 20 after Reveal, not mid-round. It ends after round 15. Overshoot counts.
- Ranking ties (correct-count tie-break, then shared positions). The `GameResults` order and message. Awards, including omission and tie rules.
- **Secrecy:** during Grid, Throttle and Question, every `DriverRow.Throttle`/`AnswerIndex` is null where §4.4 says so, and `QuestionCard.Text` is null during Throttle.
- Seeded determinism: two harnesses with the same seed produce identical HostView JSON at every step.
- Phone views per state (§3.3): ids, hints, headers. A view stays reference-stable while the player's state is unchanged.
- Malformed payloads, wrong-phase input and unknown ids are ignored.
- `FallbackQuestions.All` passes its own validation and has ≥ 60 questions across 8 categories.

**`ArcBox.Server.Tests/Redline`:**
- `OpenTdbClient`:
  - decodes a recorded fixture (url3986), including Unicode and quotes;
  - maps each `response_code`;
  - maps HTTP 429/500 and a timeout to results;
  - normalizes category prefixes.
- `OpenTdbQuestionPool`, with a fake handler and `FakeTimeProvider`:
  - fills to the target in spaced requests;
  - never issues two requests less than 5.5 s apart;
  - resets the token on code 4;
  - backs off on code 5, exceptions and 429, and caps at the max;
  - dedupes repeated texts;
  - stops cleanly on cancellation.
- `PooledQuestionSource`:
  - takes from the pool first;
  - tops up from the fallback;
  - always returns exactly `count`;
  - **parallel `Take` calls from 8 tasks never return a duplicate pool question**.
- `AddRedline` registers the game, and `GameRegistry.Manifests` contains `redline`.

**`Redline.Presentation.Tests`:**
- `CircuitData` loads `cota.json`: 170 points, a lap length of 5,512 ± 5 m, and a closed loop.
- `LapMath`:
  - distance 0 and 20 map to the same point;
  - side-by-side offsets for 2 and 3 cars sharing a distance;
  - the grid has two columns 8 m apart.
- `TrackGeometry`: the fold guard drops inner wall segments where the offset exceeds 0.7 × radius (Turn 1), and keeps them on straights.
- `CameraDirector`, one test per rule above: closest battle, spin-out priority, biggest gainer, tie-breaks, and Grid/Finish by phase.

**Manual** (added to `docs/playtest-checklist.md` in M5):
- one race against live OpenTDB from a real network;
- frame-rate check of the High and Low presets on the TV PC.

---

## 7. Out of scope
- Category selection or voting before a race. OpenTDB's mixed categories are the surprise.
- Items, sabotage and drafting (concept C was rejected), and any speed-based scoring (concept A was rejected).
- Audience participation, and more than 8 drivers.
- Persisting stats across races.
- Localization, and question moderation beyond OpenTDB's own curation.
- Circuits beyond COTA (each one is data only, so they can be added later), plus weather and night races.
- Modelled `.glb` cars. The procedural car is v1, and swapping it later changes nothing else.
- Surveyed elevation data. The COTA profile is approximate.

## 8. Risks

| Risk | Mitigation |
|---|---|
| OpenTDB disappears or changes its API | The fallback pack keeps the game playable. The client is about 100 lines and easy to swap. |
| OpenTDB questions vary in quality or are US-centric | The difficulty shown before betting makes this part of the gamble. Accept it for v1. |
| Scoring balance (Redline too strong or too weak) | All numbers are in `RedlineRules`, so tune after the first playtest. |
| Pool state is lost on Fly.io auto-stop | The first race after a wake may mix in fallback questions. Acceptable. |
| The 3D scene is too heavy for the TV PC | Ship the Low preset (§4.8). Geometry is baked at edit time. Measure early in M3 on the real machine. |
| Real scale makes cars small | The camera director frames subjects closely, name tags follow cars, and the track map shows everyone (criterion 7). |
| "Circuit of the Americas" and "COTA" are trademarks | Fine for a private hobby game. If REDLINE is ever distributed publicly, show the circuit as "Austin". |
| COTA elevation is approximate | It's labelled as approximate in the data file. Replace it with survey or DEM data whenever it matters. |
