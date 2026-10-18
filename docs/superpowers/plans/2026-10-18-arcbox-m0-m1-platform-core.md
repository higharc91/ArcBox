# ArcBox M0–M1: Setup and Platform Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Set up the toolchain and repo, prove that a Godot .NET app can talk to a SignalR hub, then build the server-authoritative platform core: the SDK, a test harness, a Counter game, rooms, the hub, reconnects and failure handling. Everything is covered by automated tests.

**Architecture:**
- An ASP.NET Core (.NET 10) server hosts one SignalR hub.
- Each room is an actor: a single loop draining a `Channel<Action>`.
- The room owns its members and a `GameSession` that wraps one `IGame`. All game calls are guarded by try/catch and a watchdog.
- Games depend only on `ArcBox.Sdk`, which has no ASP.NET or Godot references.
- Views go out as pre-serialized `JsonElement`s, so the polymorphic `kind` discriminators survive.

**Tech Stack:**
- .NET 10 SDK, ASP.NET Core SignalR, System.Text.Json
- `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`)
- xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.AspNetCore.SignalR.Client` 10.0.x
- Godot 4.x .NET (for the M0 spike only)

**Spec:** `docs/superpowers/specs/2026-10-18-arcbox-platform-design.md`. The section numbers below (§) refer to it.

## Global Constraints

- **Target frameworks:**
  - `ArcBox.Server` and all test projects target `net10.0`.
  - `ArcBox.Sdk`, `ArcBox.Sdk.Testing` and every `*.Logic` project use `<TargetFrameworks>$(ArcSdkTargetFrameworks)</TargetFrameworks>`, defined once in `Directory.Build.props` and set by Task 2.
- **Dependencies:**
  - `ArcBox.Sdk` references only the BCL.
  - `*.Logic` projects reference only `ArcBox.Sdk`.
  - `ArcBox.Sdk.Testing` references only `ArcBox.Sdk` (no xUnit).
- **Build settings:** `Nullable` enabled, `ImplicitUsings` enabled, `TreatWarningsAsErrors` true, in every project via `Directory.Build.props`.
- **Room codes:** exactly 4 letters drawn from `BCDFGHJKLMNPQRSTVWXZ`. Input is normalized by trimming and upper-casing.
- **Player names:** trimmed, 1–12 characters, unique case-insensitively within a room.
- **Limits:**
  - at most 20 concurrent rooms;
  - at most 8 players per room;
  - a game call longer than 2 s closes the room;
  - a room is disposed after 10 minutes with no host connection, or 10 minutes with no connections at all.
- **Tokens:** player secrets and host tokens are 128-bit random values from `RandomNumberGenerator`, encoded as base64url and compared with `CryptographicOperations.FixedTimeEquals`.
- **Hub:**
  - path `/hub`, JSON protocol;
  - `AllowStatefulReconnects = true`;
  - `MaximumReceiveMessageSize = 256 * 1024`;
  - about 20 hub invocations per second per connection, with excess dropped.
- **Concurrency rule:** game code and room state are only ever touched on the room's loop. The only things readable from other threads are `Room.BusySince` and `Room.ForceClose`.
- **User-facing error strings** are exactly as written in this plan.
- **Commits:** every commit message made by Claude ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

Five edge cases the spec implies that no task's main tests would naturally hit. Each one gets a pinned test, and the task that adds it is in brackets.

1. **A stale connection disconnecting after a rejoin.** A phone rejoins on a new connection, and later the old connection's disconnect arrives. The player must stay connected. [Task 6, Task 8]
2. **A timer firing after its game is over, or into the next game.** A timer from an ended or replaced session must never call `OnTimer`. [Task 7]
3. **A game calling `ctx.EndGame` inside `Start` or inside `OnTimer`.** After that, there must be no further game calls and no view publishing, and the room returns to the lobby. [Task 7, Task 8]
4. **A double `StartGame`** (a double-tap by the VIP). The second call fails with "A game is already running." and the running game is unaffected. [Task 8]
5. **A room code typed as `" qxrt "`.** It resolves to room `QXRT` for both Join and Rejoin. [Task 9]

---

## File Map

```
ArcBox.slnx
Directory.Build.props
.gitignore  .editorconfig
docs/decisions/0001-sdk-target-frameworks.md
src/ArcBox.Sdk/
  PlayerId.cs  Player.cs  GameManifest.cs  GameResults.cs
  PlayerInput.cs            # PlayerInput + TextEntryInput/ChoiceInput/ButtonsInput/DrawInput/Stroke
  ControllerView.cs         # ControllerView base + WaitView/TextEntryView/ChoiceView/ButtonsView/DrawView + Option
  IGame.cs  IGameContext.cs
  ArcJson.cs                # shared JsonSerializerOptions + PlayerIdJsonConverter
  Protocol/ClientMethods.cs  Protocol/Messages.cs   # CreateRoomResult, JoinResult, LobbyState, LobbyPlayer
src/ArcBox.Sdk.Testing/GameHarness.cs
src/ArcBox.Sdk.Tests/       # Sdk serialization tests
games/Counter/Counter.Logic/CounterGame.cs  CounterHostView.cs
games/Counter/Counter.Logic.Tests/CounterGameTests.cs
src/ArcBox.Server/
  Program.cs  RoomOptions.cs  RoomException.cs
  Rooms/RoomCodes.cs  Rooms/Secrets.cs
  Rooms/RoomMembers.cs  Rooms/PlayerSlot.cs
  Rooms/GameSession.cs  Rooms/ViewUpdates.cs
  Rooms/Room.cs  Rooms/RoomManager.cs  Rooms/RoomSweeper.cs
  Games/IGameRegistry.cs  Games/GameRegistry.cs  Games/GameRegistrationExtensions.cs
  Hubs/ArcHub.cs  Hubs/IClientSender.cs  Hubs/HubClientSender.cs  Hubs/RateLimitHubFilter.cs
  wwwroot/index.html        # placeholder until M2
src/ArcBox.Server.Tests/
  TestGames/ThrowerGame.cs  TestGames/HangerGame.cs
  Support/RecordingSender.cs  Support/RoomFixture.cs  Support/TestClient.cs  Support/ArcServerFactory.cs
  RoomCodesTests.cs  SecretsTests.cs  GameRegistryTests.cs  RoomMembersTests.cs  GameSessionTests.cs
  RoomTests.cs  RoomManagerTests.cs  HubIntegrationTests.cs
src/ArcBox.Host/            # Godot project skeleton (from the Task 2 spike; built out in M3)
```

---

# Milestone 0: Setup

### Task 1: Toolchain, repo and solution skeleton

**Files:**
- Create: `.gitignore`, `.editorconfig`, `Directory.Build.props`, `ArcBox.slnx`

**Interfaces:**
- Produces: `Directory.Build.props` defining `ArcSdkTargetFrameworks` (initial value `net10.0`; Task 2 may change it). Every later project builds under these shared settings.

- [ ] **Step 1: Install the .NET 10 SDK and xUnit v3 templates**

```bash
winget install Microsoft.DotNet.SDK.10
dotnet new install xunit.v3.templates
```

Verify: `dotnet --list-sdks` lists a `10.0.x` SDK.

- [ ] **Step 2: Add the ignore and editor files**

The repo already exists (github.com/higharc91/ArcBox, with the spec and plan committed), so don't run `git init`.

```bash
dotnet new gitignore
dotnet new editorconfig
```

Append these lines to `.gitignore`: `.godot/`, `node_modules/`, `web/play/dist/`, `src/ArcBox.Server/wwwroot/assets/`. Do **not** ignore `*.import` files, because Godot needs them committed.

- [ ] **Step 3: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <ArcSdkTargetFrameworks>net10.0</ArcSdkTargetFrameworks>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Create the solution**

Run `dotnet new sln -n ArcBox --format slnx`, then `dotnet build ArcBox.slnx`.
Expected: the build succeeds (the solution is empty).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: repo skeleton and shared build props"
```

---

### Task 2: Godot ↔ SignalR spike and target-framework decision

This is a throwaway spike. Its only lasting outputs are the decision record, the `ArcSdkTargetFrameworks` value, and a clean `src/ArcBox.Host` skeleton for M3.

**Files:**
- Create (temporary): `spikes/HelloHub/` (ASP.NET Core minimal app) and `spikes/SpikeLib/` (class library)
- Create (kept): `src/ArcBox.Host/project.godot`, `src/ArcBox.Host/ArcBox.Host.csproj`, `src/ArcBox.Host/Main.tscn`, `src/ArcBox.Host/Main.cs`
- Create: `docs/decisions/0001-sdk-target-frameworks.md`
- Modify: `Directory.Build.props` (`ArcSdkTargetFrameworks`)

**Interfaces:**
- Produces: the final `ArcSdkTargetFrameworks` value (`net10.0`, or `net8.0;net10.0`), and the `ArcBox.Host` csproj's `TargetFramework`.

- [ ] **Step 1: Install Godot**

Download the latest stable **Godot 4.x .NET** build from godotengine.org, or run `winget install GodotEngine.GodotEngine.Mono`. Note its exact version, for example `4.7.0`. It's shown in the exe name and in Help → About.

- [ ] **Step 2: Create `spikes/HelloHub`**

Run `dotnet new web`. Add a hub `PingHub` with `public string Ping(string s) => "pong:" + s;` and map it at `/hub`. Pin `--urls http://localhost:5199`.

- [ ] **Step 3: Create `spikes/SpikeLib`**

Run `dotnet new classlib` targeting `net10.0`, containing `public static class SpikeInfo { public const string Value = "spikelib-ok"; }`.

- [ ] **Step 4: Create the Godot project in `src/ArcBox.Host` by hand (no editor GUI needed)**

Write these files:
- `project.godot` containing `config_version=5`, `[application] config/name="ArcBox.Host"`, `run/main_scene="res://Main.tscn"` and `config/features=PackedStringArray("4.x", "C#")`;
- `[dotnet] project/assembly_name="ArcBox.Host"`;
- `Main.tscn`, a root `Node` with `Main.cs` attached;
- `ArcBox.Host.csproj` with `<Project Sdk="Godot.NET.Sdk/<exact Godot version from Step 1>">`.

Then edit the csproj:
- set `<TargetFramework>net10.0</TargetFramework>`;
- add `<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.*" />`;
- add a `ProjectReference` to `../../spikes/SpikeLib/SpikeLib.csproj`.

`Main.cs` (attached to the root `Node` of `Main.tscn`, set as the main scene) should do the following in `_Ready`:
1. build a `HubConnection` to `http://localhost:5199/hub`;
2. `await StartAsync()`;
3. `var r = await InvokeAsync<string>("Ping", "hello")`;
4. `GD.Print($"{r} {SpikeInfo.Value}")`;
5. `GetTree().Quit()`.

- [ ] **Step 5: Run the spike**

Start the hub with `dotnet run --project spikes/HelloHub`. In a second terminal:

```bash
dotnet build src/ArcBox.Host/ArcBox.Host.csproj
godot --headless --path src/ArcBox.Host
```

(Use the console exe, `Godot_v4.x-stable_mono_win64_console.exe`, if `godot` isn't on PATH.)

Expected output contains: `pong:hello spikelib-ok`.

- [ ] **Step 6: If Step 5 fails to build or load assemblies, retry with .NET 8**

Set the Host `TargetFramework` to `net8.0` and SpikeLib to `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`, then re-run Step 5.
Expected: the same output.

- [ ] **Step 7: Record the decision**

Write `docs/decisions/0001-sdk-target-frameworks.md` with:
- the Godot version;
- which configuration worked;
- the resulting `ArcSdkTargetFrameworks` value: `net10.0` if Step 5 passed, `net8.0;net10.0` if only Step 6 passed.

Set that value in `Directory.Build.props`.

- [ ] **Step 8: Clean up the spike**

- Delete `spikes/`.
- Remove the SpikeLib reference from the Host csproj.
- Reduce `Main.cs` to an empty `Node` script. Keep the SignalR package reference.

Verify: `dotnet build src/ArcBox.Host/ArcBox.Host.csproj` succeeds. Add the Host project to `ArcBox.slnx`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "chore: Godot host skeleton; decide SDK target frameworks (ADR 0001)"
```

---

# Milestone 1: Platform core

### Task 3: `ArcBox.Sdk`: game contract, widgets, protocol and JSON

**Files:**
- Create: every file under `src/ArcBox.Sdk/` listed in the File Map, plus `src/ArcBox.Sdk.Tests/SerializationTests.cs`
- Create projects:
  - `dotnet new classlib -o src/ArcBox.Sdk`, then replace `TargetFramework` with `<TargetFrameworks>$(ArcSdkTargetFrameworks)</TargetFrameworks>`;
  - `dotnet new xunit3 -o src/ArcBox.Sdk.Tests` (net10.0, referencing Sdk).

  Add both to the solution.

**Interfaces (namespace `ArcBox.Sdk` unless noted). Produces:**

```csharp
[JsonConverter(typeof(PlayerIdJsonConverter))]
public readonly record struct PlayerId(string Value) { public override string ToString() => Value; }
public sealed record Player(PlayerId Id, string Name, bool IsVip);
public sealed record GameManifest(string Id, string Name, string Description, int MinPlayers, int MaxPlayers);
public sealed record PlayerScore(PlayerId PlayerId, string Name, int Score);
public sealed record GameResults(IReadOnlyList<PlayerScore> Scores, string? Message = null);

public sealed record PlayerInput(JsonElement Payload) { public T Read<T>(); }   // deserializes with ArcJson.Options; throws JsonException on mismatch
public sealed record TextEntryInput(string Text);
public sealed record ChoiceInput(IReadOnlyList<string> Selected);
public sealed record ButtonsInput(string Pressed);
public sealed record Stroke(string Color, float Width, IReadOnlyList<float> Points); // x,y pairs normalized 0..1
public sealed record DrawInput(IReadOnlyList<Stroke> Strokes);

public sealed record Option(string Id, string Label);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WaitView), "wait")] [JsonDerivedType(typeof(TextEntryView), "textEntry")]
[JsonDerivedType(typeof(ChoiceView), "choice")] [JsonDerivedType(typeof(ButtonsView), "buttons")]
[JsonDerivedType(typeof(DrawView), "draw")]
public abstract record ControllerView
{
    public string ViewId { get; init; } = "";          // assigned by the server; games leave it empty
    public DateTimeOffset? DeadlineUtc { get; init; }
    public string? Header { get; init; }
}
public sealed record WaitView(string Message) : ControllerView;
public sealed record TextEntryView(string Prompt, int MaxLength, string? Placeholder = null) : ControllerView;
public sealed record ChoiceView(string Prompt, IReadOnlyList<Option> Options, bool AllowMultiple = false, int? MaxSelections = null) : ControllerView;
public sealed record ButtonsView(string? Prompt, IReadOnlyList<Option> Buttons) : ControllerView;
public sealed record DrawView(string Prompt, IReadOnlyList<string>? Colors = null) : ControllerView;

public interface IGame      { /* exactly as spec §4 */ }
public interface IGameContext { /* exactly as spec §4 */ }

public static class ArcJson { public static JsonSerializerOptions Options { get; } }  // JsonSerializerDefaults.Web + JsonStringEnumConverter; read-only after creation

// namespace ArcBox.Sdk.Protocol
public static class ClientMethods { public const string LobbyChanged = "LobbyChanged", HostView = "HostView",
    ControllerView = "ControllerView", GameEnded = "GameEnded", RoomClosed = "RoomClosed", Kicked = "Kicked"; }
public sealed record CreateRoomResult(string Code, string HostToken);
public sealed record JoinResult(string Code, PlayerId PlayerId, string Secret);
public sealed record LobbyPlayer(PlayerId Id, string Name, bool IsVip, bool Connected);
public sealed record LobbyState(string Code, IReadOnlyList<LobbyPlayer> Players, string? SelectedGameId,
    IReadOnlyList<GameManifest> Games, bool InGame, bool CanStart, GameResults? LastResults);
```

`ControllerView` needs this XML doc comment, because it's the authoring rule that the stale-input guard depends on:

> Any change to a player's view gives it a new ViewId and invalidates input already in flight. Keep a view unchanged while it should accept repeated input.

- [ ] **Step 1: Write the failing tests in `SerializationTests.cs`**

```csharp
[Fact] public void PlayerId_serializes_as_plain_string()
    => Assert.Equal("\"p1\"", JsonSerializer.Serialize(new PlayerId("p1"), ArcJson.Options));

[Fact] public void ControllerView_includes_kind_discriminator_and_camelCase()
{
    ControllerView v = new ChoiceView("Pick", [new Option("a", "A")]) { ViewId = "v3" };
    var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
    Assert.Equal("choice", json.GetProperty("kind").GetString());
    Assert.Equal("v3", json.GetProperty("viewId").GetString());
    Assert.Equal("A", json.GetProperty("options")[0].GetProperty("label").GetString());
}

[Theory] [MemberData(nameof(AllKinds))]
public void Every_widget_round_trips(ControllerView view)
{
    var json = JsonSerializer.Serialize(view, ArcJson.Options);
    var back = JsonSerializer.Deserialize<ControllerView>(json, ArcJson.Options);
    Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));
}
// AllKinds: one instance each of WaitView, TextEntryView, ChoiceView, ButtonsView, DrawView

[Fact] public void PlayerInput_reads_typed_payload()
{
    var input = new PlayerInput(JsonSerializer.SerializeToElement(new { pressed = "go" }));
    Assert.Equal("go", input.Read<ButtonsInput>().Pressed);
}
```

- [ ] **Step 2: Run and confirm failure**

Run `dotnet test src/ArcBox.Sdk.Tests`.
Expected: compile failure (the types don't exist yet).

- [ ] **Step 3: Implement every type in the Interfaces block**

`PlayerIdJsonConverter` reads and writes a JSON string. `IGame` and `IGameContext` copy spec §4 verbatim, including the comments.

- [ ] **Step 4: Run and confirm success**

Run `dotnet test src/ArcBox.Sdk.Tests`.
Expected: all tests PASS. `dotnet build` also passes for every framework in `ArcSdkTargetFrameworks`.

- [ ] **Step 5: Commit**

Commit message: `feat(sdk): game contract, controller widgets, protocol messages`.

---

### Task 4: `GameHarness` and the Counter game

**Files:**
- Create: `src/ArcBox.Sdk.Testing/GameHarness.cs` (classlib on `$(ArcSdkTargetFrameworks)`, references Sdk)
- Create: `games/Counter/Counter.Logic/{CounterGame.cs, CounterHostView.cs}` (classlib on `$(ArcSdkTargetFrameworks)`, references Sdk only)
- Test: `games/Counter/Counter.Logic.Tests/CounterGameTests.cs` (xunit3 on net10.0, references Counter.Logic and Sdk.Testing)

**Interfaces:**
- Consumes: Task 3 types.
- Produces:

```csharp
namespace ArcBox.Sdk.Testing;
public sealed class GameHarness<TGame> where TGame : IGame
{
    public GameHarness(TGame game, int playerCount = 4, int seed = 1);
    public TGame Game { get; }
    public IReadOnlyList<Player> Players { get; }        // ids "p1".."pN", names "Player1".."PlayerN", p1 IsVip
    public DateTimeOffset Now { get; }                    // starts 2026-01-01T00:00:00Z
    public GameResults? Results { get; }                 // set by ctx.EndGame
    public bool IsOver { get; }
    public IReadOnlyCollection<string> ActiveTimers { get; }
    public void Start();
    public void Input(int playerIndex, object payload);  // payload → JsonElement via ArcJson.Options
    public void Advance(TimeSpan by);                    // fires due timers in deadline order, Now = each deadline as it fires
    public void SetConnected(int playerIndex, bool connected);
    public THostView HostView<THostView>();
    public ControllerView PlayerView(int playerIndex);
}

namespace Counter.Logic;
public sealed class CounterGame : IGame { public static readonly TimeSpan Duration = TimeSpan.FromSeconds(20); }
public sealed record CounterRow(string Name, int Count);
public sealed record CounterHostView(IReadOnlyList<CounterRow> Rows, DateTimeOffset? EndsAt);
```

**Harness behavior:**
- After `IsOver`, both `Input` and `Advance` are no-ops. This mirrors the room.
- The context's `Random` is `new Random(seed)`.

**Counter game:**
- Manifest: `("counter", "Counter", "Mash the button. Most presses in 20 seconds wins.", 1, 8)`.
- `Start` sets timer `"end"` for `Duration`.
- Every player's view is always `ButtonsView("Mash it!", [new Option("press", "+1")]) { DeadlineUtc = ctx.TimerDeadline("end") }`. It stays constant so rapid taps are never treated as stale.
- `OnInput` with `Pressed == "press"` increments that player's count. Other values, or a malformed payload (catch `JsonException`), are ignored.
- `OnTimer("end")` calls `EndGame` with scores sorted by count descending, then by name.
- `HostView` lists rows in player order.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Start_sets_end_timer()
{ var h = new GameHarness<CounterGame>(new()); h.Start();
  Assert.Contains("end", h.ActiveTimers);
  Assert.Equal(h.Now + CounterGame.Duration, h.HostView<CounterHostView>().EndsAt); }

[Fact] public void Press_increments_only_that_player()
{ var h = new GameHarness<CounterGame>(new()); h.Start();
  h.Input(0, new { pressed = "press" }); h.Input(0, new { pressed = "press" }); h.Input(2, new { pressed = "press" });
  Assert.Equal([2, 0, 1, 0], h.HostView<CounterHostView>().Rows.Select(r => r.Count)); }

[Fact] public void Unknown_button_and_malformed_payload_are_ignored()
{ var h = new GameHarness<CounterGame>(new()); h.Start();
  h.Input(0, new { pressed = "nope" }); h.Input(0, new { wrong = 5 });
  Assert.All(h.HostView<CounterHostView>().Rows, r => Assert.Equal(0, r.Count)); }

[Fact] public void Player_view_is_stable_between_presses()
{ var h = new GameHarness<CounterGame>(new()); h.Start();
  var before = h.PlayerView(0); h.Input(0, new { pressed = "press" });
  Assert.Equal(before, h.PlayerView(0)); }

[Fact] public void Timer_ends_game_with_sorted_scores()
{ var h = new GameHarness<CounterGame>(new(), playerCount: 3); h.Start();
  h.Input(1, new { pressed = "press" });
  h.Advance(CounterGame.Duration);
  Assert.True(h.IsOver);
  Assert.Equal(["Player2", "Player1", "Player3"], h.Results!.Scores.Select(s => s.Name));
  h.Input(0, new { pressed = "press" });                       // no-op after end
  Assert.Equal(0, h.Results.Scores.Single(s => s.Name == "Player1").Score); }

[Fact] public void Disconnected_player_keeps_count()
{ var h = new GameHarness<CounterGame>(new()); h.Start();
  h.Input(0, new { pressed = "press" }); h.SetConnected(0, false);
  Assert.Equal(1, h.HostView<CounterHostView>().Rows[0].Count); }
```

- [ ] **Step 2: Confirm failure**

Run `dotnet test games/Counter/Counter.Logic.Tests`.
Expected: compile failure.

- [ ] **Step 3: Implement `GameHarness` and `CounterGame`**

`GameHarness` keeps timers in a `Dictionary<string, DateTimeOffset>`. `Advance` repeatedly pops the earliest due timer, sets `Now` to its deadline and calls `OnTimer`, then sets `Now` to the target time.

- [ ] **Step 4: Confirm success**

Run `dotnet test games/Counter/Counter.Logic.Tests`.
Expected: all PASS.

- [ ] **Step 5: Commit**

Commit message: `feat: GameHarness test kit and Counter game`.

---

### Task 5: Server project, room codes, secrets, options and game registry

**Files:**
- Create the projects:
  - `dotnet new web -o src/ArcBox.Server` (references Sdk and Counter.Logic);
  - `dotnet new xunit3 -o src/ArcBox.Server.Tests` (references Server, Sdk.Testing). Packages: `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.SignalR.Client`.
- Create: `RoomOptions.cs`, `RoomException.cs`, `Rooms/RoomCodes.cs`, `Rooms/Secrets.cs`, `Games/IGameRegistry.cs`, `Games/GameRegistry.cs`, `Games/GameRegistrationExtensions.cs`
- Create: `src/ArcBox.Server.Tests/TestGames/{ThrowerGame.cs, HangerGame.cs}`
- Test: `RoomCodesTests.cs`, `SecretsTests.cs`, `GameRegistryTests.cs`

**Interfaces (namespace `ArcBox.Server`). Produces:**

```csharp
public sealed class RoomOptions
{
    public int MaxRooms { get; set; } = 20;
    public int MaxPlayers { get; set; } = 8;
    public int NameMaxLength { get; set; } = 12;
    public TimeSpan GameCallTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public int InvocationsPerSecond { get; set; } = 20;
}
public sealed class RoomException(string message) : Exception(message);   // message is shown to the user

public static class RoomCodes
{
    public const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ";
    public static string Generate(Func<string, bool> isTaken);   // RandomNumberGenerator.GetInt32; retries while taken
    public static string Normalize(string input);                // Trim().ToUpperInvariant()
}
public static class Secrets
{
    public static string NewToken();                              // 16 bytes → base64url (no padding)
    public static bool Matches(string expected, string provided); // FixedTimeEquals over UTF-8 bytes; false on length mismatch
}

public interface IGameRegistry
{
    IReadOnlyList<GameManifest> Manifests { get; }
    bool Contains(string gameId);
    IGame Create(string gameId);                                  // new instance per call; throws KeyNotFoundException
}
public sealed class GameRegistry(IEnumerable<GameRegistration> registrations) : IGameRegistry;
public sealed record GameRegistration(Func<IGame> Factory);
public static class GameRegistrationExtensions
{ public static IServiceCollection AddArcGame<TGame>(this IServiceCollection s) where TGame : IGame, new(); }
```

**Test games** (namespace `ArcBox.Server.Tests.TestGames`):
- **`ThrowerGame`:**
  - manifest `("thrower", "Thrower", "", 1, 8)`;
  - every view is `ButtonsView(null, [new("boom","Boom"), new("end","End")])`;
  - `"boom"` throws `InvalidOperationException("boom")`;
  - `"end"` calls `EndGame(new([]))`;
  - a public static `bool ThrowOnStart` makes `Start` throw.
- **`HangerGame`:**
  - manifest `("hanger", "Hanger", "", 1, 8)`;
  - `OnInput` sets the static `Entered` (`ManualResetEventSlim`), then blocks on the static `Gate` (`ManualResetEventSlim`);
  - static `Reset()` recreates both events.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Generate_uses_alphabet_and_length_4()
{ var c = RoomCodes.Generate(_ => false); Assert.Equal(4, c.Length); Assert.All(c, ch => Assert.Contains(ch, RoomCodes.Alphabet)); }

[Fact] public void Generate_skips_taken_codes()
{ var taken = new HashSet<string>(); for (int i = 0; i < 200; i++) Assert.True(taken.Add(RoomCodes.Generate(taken.Contains))); }

[Fact] public void Normalize_trims_and_uppercases() => Assert.Equal("QXRT", RoomCodes.Normalize(" qxrt "));

[Fact] public void Tokens_are_128_bit_and_unique()
{ var a = Secrets.NewToken(); Assert.Equal(22, a.Length); Assert.NotEqual(a, Secrets.NewToken()); }

[Fact] public void Matches_is_exact()
{ var t = Secrets.NewToken(); Assert.True(Secrets.Matches(t, t)); Assert.False(Secrets.Matches(t, t + "x")); Assert.False(Secrets.Matches(t, "")); }

[Fact] public void Registry_lists_manifests_and_creates_fresh_instances()
{ var r = new GameRegistry([new(() => new CounterGame())]);
  Assert.Equal("counter", r.Manifests.Single().Id); Assert.True(r.Contains("counter")); Assert.False(r.Contains("nope"));
  Assert.NotSame(r.Create("counter"), r.Create("counter")); Assert.Throws<KeyNotFoundException>(() => r.Create("nope")); }
```

- [ ] **Step 2: Confirm failure**

Run `dotnet test src/ArcBox.Server.Tests`.
Expected: compile failure.

- [ ] **Step 3: Implement the Interfaces block and the two test games**

`GameRegistry` builds the manifests by creating one instance per registration.

- [ ] **Step 4: Confirm success**

Run `dotnet test src/ArcBox.Server.Tests`.
Expected: PASS.

- [ ] **Step 5: Commit**

Commit message: `feat(server): project, room codes, secrets, options, game registry`.

---

### Task 6: `RoomMembers`, the player slots

**Files:**
- Create: `src/ArcBox.Server/Rooms/{PlayerSlot.cs, RoomMembers.cs}`
- Test: `src/ArcBox.Server.Tests/RoomMembersTests.cs`

**Interfaces:**
- Consumes: `RoomOptions`, `Secrets`, `PlayerId`, `LobbyPlayer`.
- Produces:

```csharp
public sealed class PlayerSlot
{
    public required PlayerId Id { get; init; }          // "p1", "p2", … in join order, never reused within a room
    public required string Name { get; init; }
    public required string Secret { get; init; }
    public required int JoinOrder { get; init; }
    public string? ConnectionId { get; set; }
    public bool Connected => ConnectionId is not null;
}
public sealed class RoomMembers(RoomOptions options)
{
    public IReadOnlyList<PlayerSlot> All { get; }       // join order
    public PlayerId? VipId { get; }                     // earliest-joined *connected* slot; null if none connected
    public bool AnyConnected { get; }
    public bool TryAdd(string rawName, string connectionId, out PlayerSlot? slot, out string? error);
    public PlayerSlot? FindBySecret(PlayerId id, string secret);   // uses Secrets.Matches
    public PlayerSlot? FindByConnection(string connectionId);
    public PlayerSlot? Find(PlayerId id);
    public void Bind(PlayerSlot slot, string connectionId);
    public PlayerSlot? Unbind(string connectionId);     // returns slot only if connectionId is that slot's CURRENT connection
    public bool Remove(PlayerId id);
    public IReadOnlyList<LobbyPlayer> ToLobby();
}
```

**VIP rule** (this makes §5.2's "next player becomes VIP" concrete): the VIP is the earliest-joined player who is currently connected. If the original VIP reconnects, they get VIP back.

**Error strings (exact):**
- `"Name must be 1–12 characters."` (built from `NameMaxLength`)
- `"That name is taken."`
- `"Room is full."`

- [ ] **Step 1: Write the failing tests**

```csharp
static RoomMembers New() => new(new RoomOptions());

[Fact] public void Adds_trimmed_name_with_sequential_ids()
{ var m = New(); Assert.True(m.TryAdd("  Ann ", "c1", out var a, out _)); m.TryAdd("Bob", "c2", out var b, out _);
  Assert.Equal(("p1", "Ann"), (a!.Id.Value, a.Name)); Assert.Equal("p2", b!.Id.Value); }

[Theory] [InlineData("")] [InlineData("   ")] [InlineData("ThirteenChars")]
public void Rejects_bad_name_length(string name)
{ Assert.False(New().TryAdd(name, "c1", out _, out var e)); Assert.Equal("Name must be 1–12 characters.", e); }

[Fact] public void Rejects_duplicate_name_case_insensitive()
{ var m = New(); m.TryAdd("Ann", "c1", out _, out _);
  Assert.False(m.TryAdd("ANN", "c2", out _, out var e)); Assert.Equal("That name is taken.", e); }

[Fact] public void Rejects_ninth_player()
{ var m = New(); for (int i = 0; i < 8; i++) m.TryAdd($"P{i}", $"c{i}", out _, out _);
  Assert.False(m.TryAdd("Late", "c9", out _, out var e)); Assert.Equal("Room is full.", e); }

[Fact] public void Vip_moves_to_next_connected_and_back()
{ var m = New(); m.TryAdd("Ann", "c1", out var a, out _); m.TryAdd("Bob", "c2", out _, out _);
  Assert.Equal("p1", m.VipId!.Value.Value);
  m.Unbind("c1"); Assert.Equal("p2", m.VipId!.Value.Value);
  m.Bind(a!, "c1b"); Assert.Equal("p1", m.VipId!.Value.Value); }

[Fact] public void FindBySecret_requires_matching_secret()
{ var m = New(); m.TryAdd("Ann", "c1", out var a, out _);
  Assert.Same(a, m.FindBySecret(a!.Id, a.Secret)); Assert.Null(m.FindBySecret(a.Id, "wrong")); }

// Review Focus #1
[Fact] public void Unbinding_a_stale_connection_does_not_disconnect_player()
{ var m = New(); m.TryAdd("Ann", "old", out var a, out _); m.Bind(a!, "new");
  Assert.Null(m.Unbind("old")); Assert.True(a!.Connected); Assert.Equal("new", a.ConnectionId); }
```

- [ ] **Step 2: Confirm failure**

Run `dotnet test src/ArcBox.Server.Tests --filter RoomMembersTests`.
Expected: compile failure.

- [ ] **Step 3: Implement `PlayerSlot` and `RoomMembers`**

Ids come from a counter that never decrements. The name length check is applied after trimming.

- [ ] **Step 4: Confirm success**

Run the same filter.
Expected: PASS.

- [ ] **Step 5: Commit**

Commit message: `feat(server): room membership, VIP and stale-connection safety`.

---

### Task 7: `GameSession`, the guarded game host with timers and view publishing

**Files:**
- Create: `src/ArcBox.Server/Rooms/{GameSession.cs, ViewUpdates.cs}`
- Test: `src/ArcBox.Server.Tests/GameSessionTests.cs`

**Interfaces:**
- Consumes: `IGame`, `IGameContext`, `ControllerView`, `ArcJson`, `GameResults`, `RoomOptions`.
- Produces:

```csharp
public sealed record PlayerViewUpdate(PlayerId PlayerId, string ViewId, JsonElement View);
public sealed record ViewUpdates(JsonElement? Host, IReadOnlyList<PlayerViewUpdate> Players)
{ public static readonly ViewUpdates None = new(null, []); }

public sealed class GameSession : IDisposable
{
    public GameSession(IGame game, IReadOnlyList<Player> players, TimeProvider time, int seed,
                       Action<Action> postToRoom, ILogger logger);
    public string GameId { get; }
    public bool IsOver { get; }
    public GameResults? Results { get; }                 // EndGame results, or new([], "Game crashed")
    public DateTimeOffset? BusySince { get; }            // thread-safe (Interlocked on ticks); non-null only during a game call
    public event Action? TimerFired;                     // raised on the room loop after a timer's OnTimer + publish is ready
    public void Start();
    public bool Input(PlayerId from, string viewId, JsonElement payload);  // false = dropped (stale/unknown/over)
    public void ConnectionChanged(PlayerId id, bool connected);
    public ViewUpdates TakeViewUpdates();                // only views changed since last take; None when IsOver
    public JsonElement? CurrentHostView { get; }
    public PlayerViewUpdate? CurrentPlayerView(PlayerId id);
    public void Dispose();                               // cancels all timers; IsOver stays as-is
}
```

**Behavior to pin:**
- **Every call into game code is guarded.** That covers `Start`, `OnInput`, `OnTimer`, `OnPlayerConnectionChanged`, `HostView` and `PlayerView`. Each is wrapped by `Guard(Action)`, which:
  1. sets `BusySince`;
  2. runs the call in try/catch;
  3. clears `BusySince`.

  On an exception it logs at Error level with the game ID, sets `Results = new GameResults([], "Game crashed")` and sets `IsOver`. After `IsOver`, no game method is called again.
- **After each guarded mutating call that leaves the game running,** the session recomputes all views, then serializes and compares them:
  - the **host view** is serialized with its runtime type;
  - a **player view** is serialized as `ControllerView` with `ViewId = ""`, and compared with the last content JSON;
  - when a player view changes, assign the next `ViewId` (`"v1"`, `"v2"`, … per session) and store the serialized `view with { ViewId = id }`. Changed items queue for `TakeViewUpdates`.
- **`Input` returns false without calling the game when:**
  - the session is over;
  - the player is unknown;
  - `viewId` isn't that player's current `ViewId`.
- **Timers:**
  - `SetTimer` creates `time.CreateTimer(...)`, stored by key; it replaces and disposes any previous timer with that key;
  - the callback calls `postToRoom(() => FireTimer(key, timerInstance))`;
  - `FireTimer` returns immediately if `IsOver`, or if the stored timer for `key` isn't the same instance. This prevents stale and cross-game timers;
  - otherwise it removes the key, calls the guarded `OnTimer`, recomputes views, and raises `TimerFired`.
- **`IGameContext` implementation** (private nested class):
  - `Now` is `time.GetUtcNow()`;
  - `Random` is `new Random(seed)`;
  - `TimerDeadline` returns the stored deadline;
  - `EndGame` sets `Results`, sets `IsOver` and disposes timers. Calling `EndGame` inside `Start` or `OnTimer` is allowed.

- [ ] **Step 1: Write the failing tests**

Shared setup: `FakeTimeProvider time` (start 2026-01-01Z); a `postToRoom` that **queues** actions into a list the test drains with `Pump()`, imitating the room loop; players p1–p3; a `NullLogger`.

```csharp
[Fact] public void Start_publishes_host_and_all_player_views_with_kind()
{ var s = NewSession(new CounterGame()); s.Start(); var u = s.TakeViewUpdates();
  Assert.NotNull(u.Host); Assert.Equal(3, u.Players.Count);
  Assert.Equal("buttons", u.Players[0].View.GetProperty("kind").GetString());
  Assert.Equal(u.Players[0].ViewId, u.Players[0].View.GetProperty("viewId").GetString()); }

[Fact] public void Unchanged_views_are_not_republished()
{ var s = NewSession(new CounterGame()); s.Start(); var first = s.TakeViewUpdates();
  Assert.True(s.Input(P1, first.Players[0].ViewId, Press));
  var u = s.TakeViewUpdates(); Assert.NotNull(u.Host); Assert.Empty(u.Players); }

[Fact] public void Stale_view_id_is_dropped()
{ var s = NewSession(new CounterGame()); s.Start(); s.TakeViewUpdates();
  Assert.False(s.Input(P1, "v999", Press)); Assert.Null(s.TakeViewUpdates().Host); }

[Fact] public void Throwing_game_ends_session_as_crashed()
{ var s = NewSession(new ThrowerGame()); s.Start(); var v = s.TakeViewUpdates().Players[0].ViewId;
  s.Input(P1, v, Json(new { pressed = "boom" }));
  Assert.True(s.IsOver); Assert.Equal("Game crashed", s.Results!.Message); Assert.Same(ViewUpdates.None, s.TakeViewUpdates()); }

[Fact] public void Throw_in_Start_is_contained()
{ ThrowerGame.ThrowOnStart = true; try { var s = NewSession(new ThrowerGame()); s.Start(); Assert.True(s.IsOver); }
  finally { ThrowerGame.ThrowOnStart = false; } }

[Fact] public void Timer_fires_through_room_queue_and_raises_TimerFired()
{ var s = NewSession(new CounterGame()); var fired = 0; s.TimerFired += () => fired++; s.Start();
  time.Advance(CounterGame.Duration); Assert.False(s.IsOver);   // queued, not yet run
  Pump(); Assert.True(s.IsOver); Assert.Equal(1, fired); }

// Review Focus #2
[Fact] public void Timer_after_dispose_or_end_never_calls_game()
{ var s = NewSession(new CounterGame()); s.Start(); time.Advance(CounterGame.Duration); s.Dispose(); Pump();
  Assert.False(s.IsOver); }   // OnTimer never ran, so EndGame was never called

// Review Focus #3
[Fact] public void EndGame_inside_Start_stops_further_calls()
{ var s = NewSession(new EndsImmediatelyGame()); s.Start();       // test-local IGame whose Start calls ctx.EndGame(new([]))
  Assert.True(s.IsOver); Assert.Same(ViewUpdates.None, s.TakeViewUpdates()); Assert.False(s.Input(P1, "v1", Press)); }

[Fact] public void BusySince_is_null_outside_game_calls()
{ var s = NewSession(new CounterGame()); s.Start(); Assert.Null(s.BusySince); }
```

`EndsImmediatelyGame` is a private test class in this file whose `Start` calls `ctx.EndGame(new([]))`. Its `PlayerView` throws if it's ever called, which proves no calls happen after the end.

- [ ] **Step 2: Confirm failure**

Run `dotnet test src/ArcBox.Server.Tests --filter GameSessionTests`.
Expected: compile failure.

- [ ] **Step 3: Implement `GameSession` and `ViewUpdates` per the behavior above**

- [ ] **Step 4: Confirm success**

Run the same filter.
Expected: PASS.

- [ ] **Step 5: Commit**

Commit message: `feat(server): guarded GameSession with timers and view diffing`.

---

### Task 8: `Room` actor

**Files:**
- Create: `src/ArcBox.Server/Rooms/Room.cs`, `src/ArcBox.Server/Hubs/IClientSender.cs`
- Create: `src/ArcBox.Server.Tests/Support/{RecordingSender.cs, RoomFixture.cs}`
- Test: `src/ArcBox.Server.Tests/RoomTests.cs`

**Interfaces:**
- Consumes: `RoomMembers`, `GameSession`, `IGameRegistry`, `RoomOptions`, `RoomException`, `Secrets`, and the protocol types.
- Produces:

```csharp
public interface IClientSender { void Send(string connectionId, string method, params object?[] args); } // fire-and-forget, thread-safe

public enum RoomStatus { Lobby, InGame, Closed }
public sealed class Room
{
    public Room(string code, string hostToken, IGameRegistry registry, IClientSender sender,
                TimeProvider time, RoomOptions options, ILogger<Room> logger);
    public string Code { get; }
    public Task RunAsync(CancellationToken ct);           // the actor loop; returns when closed
    public Task FlushAsync();                             // completes after all previously queued work ran
    public DateTimeOffset? BusySince { get; }             // thread-safe; from current GameSession
    public void ForceClose(string reason);                // thread-safe; sends RoomClosed to every known connection, completes the inbox
    public Task<RoomStatus> GetStatusAsync();
    public Task BindHostAsync(string connectionId, string hostToken);
    public Task<JoinResult> JoinAsync(string connectionId, string name);
    public Task RejoinAsync(string connectionId, PlayerId playerId, string secret);
    public Task SelectGameAsync(string connectionId, string gameId);
    public Task StartGameAsync(string connectionId);
    public Task ReturnToLobbyAsync(string connectionId);
    public Task KickAsync(string connectionId, PlayerId target);
    public Task SendInputAsync(string connectionId, string viewId, JsonElement payload);
    public Task ConnectionLostAsync(string connectionId);
    public Task<bool> IsExpiredAsync();                   // host absent ≥ IdleTimeout, or no connections ≥ IdleTimeout
}
```

**Actor:**
- The inbox is an unbounded `Channel<Action>` with a single reader.
- Each public method enqueues a closure and returns a `TaskCompletionSource` task, created with `RunContinuationsAsynchronously`. A `RoomException` thrown inside the closure faults that task, which surfaces as an error to the caller. Any other exception is logged, and the caller gets `RoomException("Something went wrong.")`.
- Thread-safe fields are `BusySince` and a `ConcurrentDictionary<string, byte>` of known connection IDs used by `ForceClose`. Everything else is loop-only.
- On `Closed`, every enqueue fails with `RoomException("Room has closed.")`.

**Rules:**
- **`BindHostAsync`:**
  - a wrong token throws `"Room not found."`;
  - success sets the host connection and clears host-absent time;
  - it sends the host `LobbyChanged`, plus `HostView(gameId, view)` if a game is running.
- **`JoinAsync`:**
  - during `InGame` it throws `"Game in progress, join after this round."`;
  - `TryAdd` errors are thrown as `RoomException`;
  - on success it returns `JoinResult(Code, id, secret)` and broadcasts `LobbyChanged`.
- **`RejoinAsync`:**
  - a bad id or secret throws `"Couldn't rejoin. Please join again."`;
  - on success it binds and sends this connection `LobbyChanged`;
  - if a game is running it also sends `ControllerView` with the current player view (a full snapshot);
  - it calls `session.ConnectionChanged(id, true)` if it was disconnected, then publishes;
  - it broadcasts `LobbyChanged`.
- **Permissions:** the host or current VIP may select or start, otherwise `"Only the VIP or host can do that."`. Only the host may return to the lobby or kick, otherwise `"Only the host can do that."`.
- **`SelectGameAsync`:**
  - an unknown game throws `"Unknown game."`;
  - while `InGame` it throws `"A game is already running."`;
  - on success it broadcasts the lobby.
- **`StartGameAsync`, checked in this order:**
  1. while `InGame`, throws `"A game is already running."`;
  2. with no game selected, throws `"Pick a game first."`;
  3. if the player count is outside min..max (capped by `MaxPlayers`), throws `"This game needs {min}–{max} players."`;
  4. otherwise it builds `Player`s from the slots (`IsVip` from `VipId`), creates a `GameSession` (seed `Random.Shared.Next()`), subscribes `TimerFired += Publish`, calls `Start`, sets `InGame`, broadcasts the lobby and publishes.
- **`Publish()`:**
  - takes the updates;
  - sends `HostView(gameId, host)` to the host connection;
  - sends `ControllerView(view)` to each **connected** player's connection;
  - then, if `session.IsOver`, calls `EndGame()`.
- **`EndGame()`:**
  - disposes the session;
  - sets `Lobby` and `LastResults`;
  - sends `GameEnded(results)` to the host and all connected players;
  - broadcasts the lobby.
- **`ReturnToLobbyAsync`** does nothing in the lobby. During `InGame` it ends the game with `new GameResults([], "Game ended by host")`.
- **`KickAsync`:**
  - outside the lobby it throws `"Can only kick in the lobby."`;
  - otherwise it sends `Kicked` to the target's connection, removes the slot and broadcasts.
- **`SendInputAsync`** silently ignores unknown connections and anything that isn't `InGame`. When `session.Input` returns true it publishes.
- **`ConnectionLostAsync`:**
  - if it's the host connection, clears it and records the host-absent time;
  - otherwise `Unbind` it. Only when a slot is returned (Review Focus #1) does it mark `ConnectionChanged(id, false)`, publish and broadcast;
  - if nothing is connected, records the all-absent time.

**Test support:**
- `RecordingSender` keeps a thread-safe list of `Sent(string ConnectionId, string Method, object?[] Args)`, with `Of(conn, method)` returning matching messages in order.
- `RoomFixture` builds a `Room` (code `"TEST"`, host token `"host-token"`) over a `RecordingSender`, `FakeTimeProvider` and a registry of Counter + Thrower, runs `RunAsync` on `Task.Run`, and cancels on dispose. It has helpers `HostAsync()` (binds `"host"`) and `JoinAsync(name, conn)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Host_bind_with_wrong_token_fails()
{ await using var f = new RoomFixture();
  var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.BindHostAsync("h", "nope"));
  Assert.Equal("Room not found.", ex.Message); }

[Fact] public async Task Join_broadcasts_lobby_to_host_and_players()
{ await using var f = new RoomFixture(); await f.HostAsync(); await f.JoinAsync("Ann", "c1");
  var lobby = (LobbyState)f.Sender.Of("host", ClientMethods.LobbyChanged).Last().Args[0]!;
  Assert.Equal("Ann", lobby.Players.Single().Name); Assert.NotEmpty(f.Sender.Of("c1", ClientMethods.LobbyChanged)); }

[Fact] public async Task Vip_can_select_and_start_non_vip_cannot()
{ await using var f = new RoomFixture(); await f.HostAsync(); await f.JoinAsync("Ann", "c1"); await f.JoinAsync("Bob", "c2");
  var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.SelectGameAsync("c2", "counter"));
  Assert.Equal("Only the VIP or host can do that.", ex.Message);
  await f.Room.SelectGameAsync("c1", "counter"); await f.Room.StartGameAsync("c1");
  Assert.Equal(RoomStatus.InGame, await f.Room.GetStatusAsync());
  Assert.NotEmpty(f.Sender.Of("c2", ClientMethods.ControllerView)); Assert.NotEmpty(f.Sender.Of("host", ClientMethods.HostView)); }

// Review Focus #4
[Fact] public async Task Double_start_fails_and_game_keeps_running()
{ await using var f = await RoomFixture.InGameAsync("counter", players: 2);
  var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.StartGameAsync("host"));
  Assert.Equal("A game is already running.", ex.Message); Assert.Equal(RoomStatus.InGame, await f.Room.GetStatusAsync()); }

[Fact] public async Task Start_checks_player_count()
{ await using var f = new RoomFixture(); await f.HostAsync(); await f.Room.SelectGameAsync("host", "counter");
  var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.StartGameAsync("host"));
  Assert.Equal("This game needs 1–8 players.", ex.Message); }

[Fact] public async Task Join_during_game_is_rejected()
{ await using var f = await RoomFixture.InGameAsync("counter", players: 1);
  var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.JoinAsync("c9", "Late"));
  Assert.Equal("Game in progress, join after this round.", ex.Message); }

[Fact] public async Task Rejoin_mid_game_sends_full_controller_snapshot()
{ await using var f = await RoomFixture.InGameAsync("counter", players: 2);
  var p = f.Joined["c1"]; await f.Room.ConnectionLostAsync("c1");
  await f.Room.RejoinAsync("c1b", p.PlayerId, p.Secret);
  var view = (JsonElement)f.Sender.Of("c1b", ClientMethods.ControllerView).Single().Args[0]!;
  Assert.Equal("buttons", view.GetProperty("kind").GetString()); }

// Review Focus #1 (room level)
[Fact] public async Task Stale_connection_loss_after_rejoin_keeps_player_connected()
{ await using var f = await RoomFixture.InGameAsync("counter", players: 1);
  var p = f.Joined["c1"]; await f.Room.RejoinAsync("c1b", p.PlayerId, p.Secret);
  await f.Room.ConnectionLostAsync("c1");
  var lobby = (LobbyState)f.Sender.Of("host", ClientMethods.LobbyChanged).Last().Args[0]!;
  Assert.True(lobby.Players.Single().Connected); }

[Fact] public async Task Crashing_game_returns_room_to_lobby_with_message()
{ await using var f = await RoomFixture.InGameAsync("thrower", players: 1);
  var viewId = f.LastViewId("c1");
  await f.Room.SendInputAsync("c1", viewId, Json(new { pressed = "boom" }));
  Assert.Equal(RoomStatus.Lobby, await f.Room.GetStatusAsync());
  var results = (GameResults)f.Sender.Of("c1", ClientMethods.GameEnded).Single().Args[0]!;
  Assert.Equal("Game crashed", results.Message); }

// Review Focus #3 (room level)
[Fact] public async Task Timer_ending_game_returns_to_lobby()
{ await using var f = await RoomFixture.InGameAsync("counter", players: 1);
  f.Time.Advance(CounterGame.Duration); await f.Room.FlushAsync();
  Assert.Equal(RoomStatus.Lobby, await f.Room.GetStatusAsync()); Assert.Single(f.Sender.Of("host", ClientMethods.GameEnded)); }

[Fact] public async Task Kick_only_in_lobby_and_only_by_host()
{ await using var f = new RoomFixture(); await f.HostAsync(); await f.JoinAsync("Ann", "c1"); await f.JoinAsync("Bob", "c2");
  await Assert.ThrowsAsync<RoomException>(() => f.Room.KickAsync("c1", new PlayerId("p2")));
  await f.Room.KickAsync("host", new PlayerId("p2"));
  Assert.Single(f.Sender.Of("c2", ClientMethods.Kicked)); }

[Fact] public async Task Expires_after_idle_timeout_without_host()
{ await using var f = new RoomFixture(); await f.HostAsync(); await f.Room.ConnectionLostAsync("host");
  f.Time.Advance(TimeSpan.FromMinutes(9)); Assert.False(await f.Room.IsExpiredAsync());
  f.Time.Advance(TimeSpan.FromMinutes(1)); Assert.True(await f.Room.IsExpiredAsync()); }

[Fact] public async Task ForceClose_notifies_everyone_and_rejects_further_calls()
{ await using var f = new RoomFixture(); await f.HostAsync(); await f.JoinAsync("Ann", "c1");
  f.Room.ForceClose("game stopped responding");
  Assert.Equal("game stopped responding", f.Sender.Of("c1", ClientMethods.RoomClosed).Single().Args[0]);
  await Assert.ThrowsAsync<RoomException>(() => f.Room.JoinAsync("c2", "Bob")); }
```

Add these to `RoomFixture` so the tests compile:
- `InGameAsync(gameId, players)`: host plus N players `c1..cN` named `P1..PN`, then select and start as the host;
- `Joined` (connection → `JoinResult`);
- `Time` (the `FakeTimeProvider`);
- `LastViewId(conn)`: reads `viewId` from the last `ControllerView` sent to `conn`.

`Json(object)` is a test helper that serializes the object to a `JsonElement`.

- [ ] **Step 2: Confirm failure**

Run `dotnet test src/ArcBox.Server.Tests --filter RoomTests`.
Expected: compile failure.

- [ ] **Step 3: Implement `IClientSender`, `Room`, `RecordingSender` and `RoomFixture` per the rules above**

- [ ] **Step 4: Confirm success**

Run the same filter.
Expected: PASS. Run it 5 times (`for i in 1 2 3 4 5; do dotnet test ... ; done`) to confirm there's no flakiness.

- [ ] **Step 5: Commit**

Commit message: `feat(server): Room actor with lobby, VIP, game lifecycle, rejoin snapshots`.

---

### Task 9: `RoomManager`, sweeper and watchdog

**Files:**
- Create: `src/ArcBox.Server/Rooms/{RoomManager.cs, RoomSweeper.cs}`
- Test: `src/ArcBox.Server.Tests/RoomManagerTests.cs`

**Interfaces:**
- Consumes: `Room`, `RoomCodes`, `Secrets`, `RoomOptions`, `IGameRegistry`, `IClientSender`, `TimeProvider`.
- Produces:

```csharp
public sealed class RoomManager : IAsyncDisposable
{
    public RoomManager(IGameRegistry registry, IClientSender sender, TimeProvider time,
                       IOptions<RoomOptions> options, ILoggerFactory loggers);
    public int Count { get; }
    public Task<(Room Room, CreateRoomResult Result)> CreateRoomAsync(string hostConnectionId);  // binds host
    public Room? Find(string rawCode);                    // RoomCodes.Normalize
    public void Track(string connectionId, Room room);    // connection → room index
    public Room? RoomFor(string connectionId);
    public void Untrack(string connectionId);
    public Task SweepAsync();                             // watchdog + idle expiry
    public ValueTask DisposeAsync();                      // cancels all room loops
}
public sealed class RoomSweeper(RoomManager manager, [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time) : BackgroundService; // PeriodicTimer(1s, time) → SweepAsync
```

**Room clock:** `RoomManager` has `public const string ClockKey = "rooms";`, and its constructor takes `[FromKeyedServices(RoomManager.ClockKey)] TimeProvider time`.

Rooms use this **keyed** `TimeProvider`, never the global one. That way the tests can fake room time without also faking SignalR's own keep-alive and long-polling timers.

**Rules:**
- **`CreateRoomAsync`:**
  - when `Count >= MaxRooms`, throws `RoomException("Server is full, try again later.")`;
  - otherwise generates a free code and a host token, starts `room.RunAsync` on `Task.Run`, then binds the host and tracks it.
- **`SweepAsync`, for each room:**
  - **Watchdog check (first, without enqueueing):** if `BusySince` is set and `now - BusySince > GameCallTimeout`, log a warning, `ForceClose("game stopped responding")` and remove the room.
  - **Expiry check:** otherwise `await room.IsExpiredAsync()` with a 1 s `WaitAsync` timeout, so one hung room can't stall the sweep. If it's expired, `ForceClose("Room closed after being idle.")` and remove it.
  - Removing a room also drops its connection index entries.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Creates_rooms_up_to_limit()
{ await using var m = NewManager(maxRooms: 2);
  await m.CreateRoomAsync("h1"); await m.CreateRoomAsync("h2");
  var ex = await Assert.ThrowsAsync<RoomException>(() => m.CreateRoomAsync("h3"));
  Assert.Equal("Server is full, try again later.", ex.Message); }

// Review Focus #5
[Fact] public async Task Find_normalizes_code()
{ await using var m = NewManager(); var (room, r) = await m.CreateRoomAsync("h1");
  Assert.Same(room, m.Find($" {r.Code.ToLowerInvariant()} ")); }

[Fact] public async Task Sweep_removes_idle_rooms()
{ await using var m = NewManager(); var (room, _) = await m.CreateRoomAsync("h1");
  await room.ConnectionLostAsync("h1"); time.Advance(TimeSpan.FromMinutes(10));
  await m.SweepAsync(); Assert.Equal(0, m.Count); Assert.Null(m.RoomFor("h1")); }

[Fact] public async Task Watchdog_closes_room_with_hung_game()
{ HangerGame.Reset();
  try {
    await using var m = NewManager(withHanger: true); var (room, _) = await m.CreateRoomAsync("h1");
    await room.JoinAsync("c1", "Ann"); await room.SelectGameAsync("h1", "hanger"); await room.StartGameAsync("h1");
    var viewId = sender.LastViewId("c1");
    _ = room.SendInputAsync("c1", viewId, Json(new { pressed = "x" }));
    Assert.True(HangerGame.Entered.Wait(TimeSpan.FromSeconds(5)));
    time.Advance(TimeSpan.FromSeconds(3)); await m.SweepAsync();
    Assert.Equal(0, m.Count);
    Assert.Equal("game stopped responding", sender.Of("c1", ClientMethods.RoomClosed).Single().Args[0]);
  } finally { HangerGame.Gate.Set(); } }
```

`HangerGame`'s view is a `ButtonsView` (as in Task 5). Add `LastViewId(conn)` to `RecordingSender`, moving it from `RoomFixture` so both can use it.

- [ ] **Step 2: Confirm failure**

Run `dotnet test src/ArcBox.Server.Tests --filter RoomManagerTests`.
Expected: compile failure.

- [ ] **Step 3: Implement `RoomManager` and `RoomSweeper` per the rules**

Store rooms in a `ConcurrentDictionary<string, Room>`, and connections in a `ConcurrentDictionary<string, Room>`.

- [ ] **Step 4: Confirm success**

Run the same filter.
Expected: PASS.

- [ ] **Step 5: Commit**

Commit message: `feat(server): RoomManager with capacity limit, idle sweep and hang watchdog`.

---

### Task 10: `ArcHub`, `Program`, rate limiting and end-to-end integration tests

**Files:**
- Create: `src/ArcBox.Server/Hubs/{ArcHub.cs, HubClientSender.cs, RateLimitHubFilter.cs}`, `src/ArcBox.Server/wwwroot/index.html`
- Modify: `src/ArcBox.Server/Program.cs` (replace the template; end with `public partial class Program;`)
- Create: `src/ArcBox.Server.Tests/Support/{ArcServerFactory.cs, TestClient.cs}`
- Test: `src/ArcBox.Server.Tests/HubIntegrationTests.cs`

**Interfaces:**
- Consumes: `RoomManager`, `Room`, the protocol types, `RoomException`.
- Produces the hub methods (the M2 phone app and M3 Godot host call exactly these):

```csharp
public sealed class ArcHub(RoomManager rooms) : Hub
{
    public Task<CreateRoomResult> CreateRoom();
    public Task ResumeHost(string code, string hostToken);
    public Task<JoinResult> Join(string code, string name);
    public Task Rejoin(string code, string playerId, string secret);
    public Task SelectGame(string gameId);
    public Task StartGame();
    public Task ReturnToLobby();
    public Task Kick(string playerId);
    public Task SendInput(string viewId, JsonElement payload);
}
```

**Rules:**
- **`RoomException` → `HubException`.** Every hub method catches `RoomException` and rethrows `new HubException(ex.Message)`, so clients see the exact message.
- **Room lookup:**
  - an unknown code for `Join`, `Rejoin` or `ResumeHost` throws `HubException("Room not found.")`;
  - methods without a code use `rooms.RoomFor(Context.ConnectionId)`, and throw `HubException("You're not in a room.")` when it's missing.
- **`Join`, `Rejoin` and `ResumeHost`:**
  - if this connection is already tracked to a *different* room, call `ConnectionLostAsync` on the old room first;
  - then call `Track`.
- **`OnDisconnectedAsync`:** `RoomFor` → `ConnectionLostAsync`, then `Untrack`.
- **`HubClientSender`** wraps `IHubContext<ArcHub>`: `Clients.Client(id).SendCoreAsync(method, args)`. It's fire-and-forget, with exceptions logged.
- **`RateLimitHubFilter : IHubFilter`:**
  - one `TokenBucketRateLimiter` per connection (`TokenLimit = InvocationsPerSecond`, `TokensPerPeriod = InvocationsPerSecond`, `ReplenishmentPeriod = 1s`, `QueueLimit = 0`), disposed in `OnDisconnectedAsync`;
  - when a lease is denied: `SendInput` returns `null` silently, and any other method throws `HubException("Slow down.")`.
- **`Program.cs`:**
  - `services.Configure<RoomOptions>(builder.Configuration.GetSection("Rooms"))`;
  - `AddKeyedSingleton<TimeProvider>(RoomManager.ClockKey, TimeProvider.System)`, `RoomManager`, `IClientSender → HubClientSender`, `IGameRegistry → GameRegistry`, and `AddArcGame<CounterGame>()`;
  - `AddHostedService<RoomSweeper>()`;
  - `AddSignalR(o => { o.MaximumReceiveMessageSize = 256 * 1024; o.AddFilter<RateLimitHubFilter>(); })`, then `.AddJsonProtocol(o => <copy ArcJson.Options settings and converters>)`;
  - `MapHub<ArcHub>("/hub", o => o.AllowStatefulReconnects = true)`;
  - `MapGet("/healthz", () => "ok")`;
  - `UseDefaultFiles()` plus `UseStaticFiles()`.
- **`wwwroot/index.html`** is a placeholder containing the text `ArcBox server is running. Phone app arrives in M2.`

**Test support:**
- `ArcServerFactory : WebApplicationFactory<Program>`:
  - in `ConfigureTestServices`, replaces the **keyed** `RoomManager.ClockKey` `TimeProvider` with a shared `FakeTimeProvider` (exposed as `Time`), and leaves the global one alone;
  - adds `AddArcGame<ThrowerGame>()`.
- `TestClient` wraps `HubConnection`:
  - it's built with `WithUrl(new Uri(server.BaseAddress, "hub"), o => { o.HttpMessageHandlerFactory = _ => server.CreateHandler(); o.Transports = HttpTransportType.LongPolling; })`;
  - it records every server→client message (`On` for each `ClientMethods` name, with args as `JsonElement`) into a `Channel`;
  - it has `Task<JsonElement> WaitForAsync(string method, Func<JsonElement, bool>? match = null)` with a 5 s timeout, and `InvokeAsync`/`ThrowsHubAsync` helpers.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Healthz_and_placeholder_page_respond()
{ using var f = new ArcServerFactory(); var http = f.CreateClient();
  Assert.Equal("ok", await http.GetStringAsync("/healthz"));
  Assert.Contains("ArcBox server is running", await http.GetStringAsync("/")); }

[Fact] public async Task Full_counter_round_over_signalr()
{ using var f = new ArcServerFactory();
  await using var host = await TestClient.ConnectAsync(f); await using var ann = await TestClient.ConnectAsync(f);
  var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
  var join = await ann.InvokeAsync<JoinResult>("Join", room.Code.ToLowerInvariant(), "Ann");
  await ann.InvokeAsync("SelectGame", "counter"); await ann.InvokeAsync("StartGame");
  var view = await ann.WaitForAsync(ClientMethods.ControllerView);
  Assert.Equal("buttons", view.GetProperty("kind").GetString());
  await ann.InvokeAsync("SendInput", view.GetProperty("viewId").GetString(), new { pressed = "press" });
  var hostView = await host.WaitForAsync(ClientMethods.HostView, a => a.GetProperty("rows")[0].GetProperty("count").GetInt32() == 1);
  f.Time.Advance(CounterGame.Duration);
  var ended = await ann.WaitForAsync(ClientMethods.GameEnded);
  Assert.Equal("Ann", ended.GetProperty("scores")[0].GetProperty("name").GetString()); }

[Fact] public async Task Room_errors_reach_client_as_exact_messages()
{ using var f = new ArcServerFactory(); await using var c = await TestClient.ConnectAsync(f);
  Assert.Equal("Room not found.", await c.ThrowsHubAsync("Join", "ZZZZ", "Ann"));
  Assert.Equal("You're not in a room.", await c.ThrowsHubAsync("StartGame")); }

[Fact] public async Task Disconnect_then_rejoin_on_new_connection_gets_snapshot()
{ using var f = new ArcServerFactory();
  await using var host = await TestClient.ConnectAsync(f); var ann = await TestClient.ConnectAsync(f);
  var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
  var join = await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");
  await host.InvokeAsync("SelectGame", "counter"); await host.InvokeAsync("StartGame");
  await ann.DisposeAsync();
  await host.WaitForAsync(ClientMethods.LobbyChanged, l => !l.GetProperty("players")[0].GetProperty("connected").GetBoolean());
  await using var ann2 = await TestClient.ConnectAsync(f);
  await ann2.InvokeAsync("Rejoin", room.Code, join.PlayerId.Value, join.Secret);
  Assert.Equal("buttons", (await ann2.WaitForAsync(ClientMethods.ControllerView)).GetProperty("kind").GetString()); }

[Fact] public async Task Host_resume_on_new_connection_keeps_room()
{ using var f = new ArcServerFactory(); var host = await TestClient.ConnectAsync(f);
  var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom"); await host.DisposeAsync();
  await using var host2 = await TestClient.ConnectAsync(f);
  await host2.InvokeAsync("ResumeHost", room.Code, room.HostToken);
  Assert.Equal(room.Code, (await host2.WaitForAsync(ClientMethods.LobbyChanged)).GetProperty("code").GetString()); }

[Fact] public async Task Crashing_game_over_signalr_returns_everyone_to_lobby()
{ using var f = new ArcServerFactory();
  await using var host = await TestClient.ConnectAsync(f); await using var ann = await TestClient.ConnectAsync(f);
  var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom"); await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");
  await host.InvokeAsync("SelectGame", "thrower"); await host.InvokeAsync("StartGame");
  var v = await ann.WaitForAsync(ClientMethods.ControllerView);
  await ann.InvokeAsync("SendInput", v.GetProperty("viewId").GetString(), new { pressed = "boom" });
  Assert.Equal("Game crashed", (await host.WaitForAsync(ClientMethods.GameEnded)).GetProperty("message").GetString()); }

[Fact] public async Task Flooding_non_input_methods_is_rate_limited()
{ using var f = new ArcServerFactory(); await using var c = await TestClient.ConnectAsync(f);
  var room = await c.InvokeAsync<CreateRoomResult>("CreateRoom");
  var messages = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => c.TryInvokeMessageAsync("SelectGame", "counter")));
  Assert.Contains("Slow down.", messages); }
```

`TryInvokeMessageAsync` returns `null` on success or the `HubException` message on failure.

- [ ] **Step 2: Confirm failure**

Run `dotnet test src/ArcBox.Server.Tests --filter HubIntegrationTests`.
Expected: compile failure.

- [ ] **Step 3: Implement the hub, sender, filter, `Program.cs`, `wwwroot/index.html` and the test support classes per the rules**

- [ ] **Step 4: Confirm success**

Run `dotnet test src/ArcBox.Server.Tests --filter HubIntegrationTests`.
Expected: PASS.

- [ ] **Step 5: Run the full M1 verification**

Run `dotnet test ArcBox.slnx`.
Expected: every test project passes (Sdk.Tests, Counter.Logic.Tests, Server.Tests).

Then smoke-test the real server:
1. Run `dotnet run --project src/ArcBox.Server --urls http://localhost:5080`.
2. `curl http://localhost:5080/healthz` should print `ok`.
3. Stop the server.

- [ ] **Step 6: Commit**

Commit message: `feat(server): ArcHub, rate limiting, Program wiring and SignalR integration tests`.

---

## Done criteria for M0–M1

- `dotnet test ArcBox.slnx` is green.
- ADR 0001 records the Godot version and the `ArcSdkTargetFrameworks` value.
- `src/ArcBox.Host` builds.
- The hub surface in Task 10 matches spec §5.3, so M2 (phone app) and M3 (Godot host) can start in parallel against it.
