# M3 TV Host Shell + M4 REDLINE 3D Scene Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Godot 4 .NET TV app: connect, lobby with QR code, game scene switching, results, resume-after-crash, a `counter` scene, and REDLINE's realistic 3D Circuit of the Americas race with 22 procedural 2026 cars, a players-first camera and the broadcast HUD.

**Architecture:** Everything that can be tested without Godot lives in plain .NET libraries: `ArcBox.Host.Core` (SignalR host client, session file, main-thread event queue) and new mesh builders in `Redline.Presentation` (track and car geometry as `System.Numerics` mesh data). The Godot project (`src/ArcBox.Host`) only maps that data onto nodes, materials, cameras and `Control`s. Verification of the Godot layer is a scripted run under Xvfb (software Vulkan works in the sandbox) that writes screenshots.

**Tech Stack:** Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`, net10.0, Forward+), `Microsoft.AspNetCore.SignalR.Client` 10, `QRCoder` (MIT) for the lobby QR, xUnit v3 for the libraries.

**Spec:** `docs/superpowers/specs/2026-10-18-arcbox-platform-design.md` §5.4 (host), §6.2, §9 M3; `docs/superpowers/specs/2026-10-25-redline-design.md` §4.8–4.9, §3.1 TV column. Reference renders: `docs/superpowers/mockups/trivia-racer/*.png`; reference geometry code: `f1car2026.js`, `cota3d.js` in that folder.

## Global Constraints

- Godot binary for local runs: download `Godot_v4.7.2-stable_mono_linux_x86_64.zip` from the godotengine GitHub release into the session scratchpad; never commit it. Run with `xvfb-run -a -s "-screen 0 2560x1440x24" <godot> --path src/ArcBox.Host --rendering-driver vulkan -- <app args>`.
- App command-line args (after `--`), all optional: `--server=<url>` (default from `user://settings.json`, else `http://localhost:5080`), `--new-room` (ignore the saved session), `--shots=<dir>` (save a PNG on every screen/phase change and every 2 s during Reveal, named `<seq>-<screen-or-phase>.png`), `--quit-after=<seconds>`, `--preset=ultra|high|low`, `--res=<w>x<h>`.
- Session file `user://session.json` = `{serverUrl, code, hostToken}` (platform §5.4); on failed `ResumeHost` delete it and create a new room.
- Hub callbacks are marshalled to the main thread through a `ConcurrentQueue` drained in `_Process`; no Godot API is touched off the main thread.
- QR target `"{serverUrl}/?room={CODE}"`.
- Scenes never decide outcomes; every visual derives from the latest `HostView` (and the previous one for transitions).
- REDLINE numbers from spec §4.8: packs ±3.4 m; three abreast −4.4/0/+4.4 m, rows 9 m back; grid two columns 8 m apart; Reveal tween 4 s ease-out; Reveal follow-up cut after 3.5 s; Driver Select holds a newly claimed car 2 s; track width 15 m, 24 m at Turn 1; catch fence 19 m from centre; car wheelbase 3.4 m, width 1.9 m, tyres ≈280 mm front / 375 mm rear wide.
- Rendering (Ultra): Forward+, `Engine.MaxFps = 144`, VSync off, SDFGI (half-res probes), SSR, SSAO + SSIL, light volumetric fog, AgX tonemap, one `DirectionalLight3D` with 4-split PSSM and an 8192 directional shadow atlas, MSAA 4× + screen-space AA. High = Ultra minus SDFGI and SSR. Low = no screen-space effects, 2048 shadows, trees halved (the Mobile renderer switch is a project setting and needs a restart, so Low keeps Forward+ but disables everything costly).
- No team, sponsor or series logos; plain helmets with a team band.
- Livery colours, chassis names and `finish` come from `games/Redline/Redline.Logic/Data/grid2026.json` (embedded resource already); do not duplicate them.

## Review Focus

1. **Server not reachable at launch / connection drops mid-game** — expected: Connect screen shows the error and retries every 5 s with automatic reconnect; on reconnect the host calls `ResumeHost` and redraws from the snapshot. Test in Task 1 (`Reconnect_resumes_host_and_redelivers_lobby`).
2. **HostView arrives before the scene finished loading / two views in one frame** — expected: the scene renders only the latest view, with the previous one for transitions; no exception. Test in Task 1 (`Queue_coalesces_host_views_per_frame`) and the Task 6 run.
3. **A human-claimed car off camera** — expected: always on the tower and the map; the director only frames shots containing a player when one exists (already tested in `CameraDirector`); name tags hidden when behind the camera. Checked by Task 7 screenshot review.
4. **Long player names (12 chars) and 22-row tower at 1280×720 and 2560×1440** — expected: no overflow or clipping; UI scales with the window (`content_scale_mode = canvas_items`, base 1920×1080). Checked in Task 7 screenshots at both sizes.
5. **Reveal where many cars share a distance** — expected: packs per `LapMath.Pack`, cars never intersect each other or the walls. Test in Task 3 (`Pack_offsets_stay_inside_track_width`).

---

## Milestone 3 — TV host shell

### Task 1: `ArcBox.Host.Core` — host client, session store, event queue

**Files:**
- Create: `src/ArcBox.Host.Core/{ArcBox.Host.Core.csproj, HostClient.cs, HostEvent.cs, SessionStore.cs, MainThreadQueue.cs}`
- Create: `src/ArcBox.Host.Core.Tests/{ArcBox.Host.Core.Tests.csproj, HostClientTests.cs, SessionStoreTests.cs, MainThreadQueueTests.cs}`
- Modify: `ArcBox.slnx`

**Interfaces:**
- Produces:
  - `abstract record HostEvent`; `LobbyEvent(LobbyState Lobby)`, `HostViewEvent(string GameId, JsonElement View)`, `GameEndedEvent(GameResults Results)`, `RoomClosedEvent(string Reason)`, `ConnectionEvent(HostConnectionState State, string? Error)`; `enum HostConnectionState { Disconnected, Connecting, Connected, Reconnecting }`.
  - `sealed class HostClient : IAsyncDisposable` — `HostClient(Uri server, Func<HttpMessageHandler>? handler = null)`; `Task<CreateRoomResult> CreateRoomAsync()`, `Task ResumeHostAsync(string code, string hostToken)`, `Task SelectGameAsync(string id)`, `Task StartGameAsync()`, `Task ReturnToLobbyAsync()`, `Task KickAsync(string playerId)`, `Task ConnectAsync(CancellationToken)`; `event Action<HostEvent>? Event` (raised on a pool thread). Uses the platform's JSON settings (`ArcJson.Options`), automatic reconnect with the platform backoff, and on `Reconnected` re-calls `ResumeHost` with the last code/token.
  - `sealed class SessionStore(string path)` — `HostSession? Load()`, `void Save(HostSession)`, `void Clear()`; `record HostSession(string ServerUrl, string Code, string HostToken)`; corrupt file ⇒ `Load` returns null and deletes it.
  - `sealed class MainThreadQueue` — `void Post(HostEvent)`, `IReadOnlyList<HostEvent> Drain()` that keeps only the **latest** `HostViewEvent` per drain but every other event in order (Review Focus 2).
- `ArcBox.Host.Core.Tests` references `ArcBox.Server` and builds a `WebApplicationFactory<Program>` like `src/ArcBox.Server.Tests/Support/ArcServerFactory.cs` (copy the minimal pieces; it uses LongPolling through `server.CreateHandler()`).

- [ ] **Step 1:** Tests: `Create_room_and_receive_lobby` (create → `LobbyEvent` with that code); `Host_sees_player_join` (a raw `HubConnection` joins → lobby has 1 player); `Resume_with_bad_token_throws_room_message`; `Reconnect_resumes_host_and_redelivers_lobby` (stop the underlying connection via a test hook `SimulateDropAsync()` and assert a fresh `LobbyEvent` after reconnect — Review Focus 1); `Host_view_event_for_counter_game` (select `counter`, start with one joined player → `HostViewEvent("counter", …)`); `SessionStore` round-trip, missing file, corrupt file; `Queue_coalesces_host_views_per_frame`.
- [ ] **Step 2–4:** fail → implement → `dotnet test --project src/ArcBox.Host.Core.Tests` green.
- [ ] **Step 5:** Commit `feat(host): host client core with session store and main-thread queue`.

### Task 2: Godot shell — Connect, Lobby with QR, scene switching, Results, Counter scene

**Files:**
- Modify: `src/ArcBox.Host/{ArcBox.Host.csproj, project.godot, Main.tscn, Main.cs}`
- Create: `src/ArcBox.Host/Shell/{AppArgs.cs, Screenshotter.cs, Theme.tres or Theme.cs, ConnectScreen.cs, LobbyScreen.cs, ResultsScreen.cs, QrTexture.cs, GameSceneMap.cs, IGameScene.cs}`, `src/ArcBox.Host/games/counter/{CounterScene.tscn, CounterScene.cs}`, `src/ArcBox.Host/fonts/` (Titillium Web OFL TTFs + `OFL.txt`), `docs/tv-host.md`
- Modify: `.gitignore` (`src/ArcBox.Host/.godot/`)

**Interfaces:**
- Consumes: Task 1 types; `ArcBox.Sdk` protocol records.
- Produces:
  - `interface IGameScene { void Render(JsonElement view, JsonElement? previous); void SetShots(Screenshotter? shots); }` (spec §6.2 says `Render(object)`; JSON keeps the shell game-agnostic, each scene deserializes its own view type with `ArcJson.Options`).
  - `static class GameSceneMap { static string? ScenePath(string gameId) }` with `counter → res://games/counter/CounterScene.tscn`, `redline → res://games/redline/RedlineScene.tscn`.
  - `Screenshotter(string dir)` — `void Capture(string label)` saves the viewport image after the next frame (`await ToSignal(RenderingServer.FrameDrawn)`).
  - `AppArgs.Parse(string[] userArgs)` reading `OS.GetCmdlineUserArgs()` per the Global Constraints.
- Behaviour: `Main` (root `Control`, full rect) owns the `HostClient`, drains the queue in `_Process`, and swaps one child screen: Connect (server URL field + status) → Lobby → game scene (on first `HostViewEvent` for the selected game, or when `lobby.inGame`) → Results (on `GameEndedEvent`, back to Lobby on the next `LobbyEvent` with `inGame == false` after 10 s or on a key press). Lobby shows the big room code, the QR (`QRCoder` → PNG bytes → `Image.LoadPngFromBuffer` → `ImageTexture`), the join URL, players with VIP crown and connection state, a game picker (buttons from `lobby.games`) and Start (enabled when `canStart`); `ui_accept` starts. Broadcast styling: background gradient `#1c1c26→#0b0b10`, red `#e10600` brand slab, Titillium Web. Window: 1920×1080 base, `canvas_items` stretch, `expand` aspect.
  - Counter scene: one big number per player row from the counter host view.
- `docs/tv-host.md`: run from the editor, run headless with screenshots, the args, and the session-resume behaviour.

- [ ] **Step 1:** `dotnet build src/ArcBox.Host` succeeds.
- [ ] **Step 2:** Run the server (`dotnet run --project src/ArcBox.Server --urls http://localhost:5080`), then the host under Xvfb with `--new-room --shots=<scratch>/m3 --quit-after=8`. Expected: `*-connect.png`, `*-lobby.png` exist; the lobby shot shows a 4-letter code and a QR that decodes (check with `npx --yes jsqr`-style decoding or by eye) to `http://localhost:5080/?room=CODE`.
- [ ] **Step 3:** Resume check: run again **without** `--new-room` → the lobby shows the same code (from `user://session.json`). Then restart the server and run again → the saved session fails, the file is deleted and a new code appears.
- [ ] **Step 4:** Counter loop: with the host running, join two players with a small Node script using `@microsoft/signalr` from `web/play/node_modules` (or the dev page), select `counter`, start, press, wait for the end → shots `*-counter.png`, `*-results.png`, then `*-lobby.png` again.
- [ ] **Step 5:** Commit `feat(host): Godot TV shell with lobby QR, scene switching, results and counter scene`.

## Milestone 4 — REDLINE scene

### Task 3: Track mesh builder (pure C#)

**Files:**
- Create: `games/Redline/Redline.Presentation/Meshes/{MeshData.cs, TrackMeshBuilder.cs, TerrainBuilder.cs}`
- Test: `games/Redline/Redline.Presentation.Tests/Meshes/TrackMeshBuilderTests.cs`

**Interfaces:**
- Consumes: `CircuitData`, `TrackGeometry.Resample/Width/IsCorner/WallAllowed`, `LapMath`.
- Produces:
  - `sealed class MeshData { List<Vector3> Positions; List<Vector3> Normals; List<Vector2> Uvs; List<Vector4> Colors; List<int> Indices; }` (right-handed, Y up, metres; X/Z from the circuit plane where circuit `(x, y)` → `(x, h, y)`).
  - `static class TrackMeshBuilder { static TrackMeshes Build(CircuitData c) }` → `record TrackMeshes(MeshData Asphalt, MeshData Lines, MeshData Curbs, MeshData Runoff, MeshData Walls, MeshData Fence, MeshData Grass)`. Walls are one-sided (normals face the track) on each side at `Width/2 + 4 m`, skipped where `WallAllowed` is false; fence at 19 m from centre on the outside, skipped by the same rule; curbs where `IsCorner`, 1.5 m wide alternating red `#d8001f` / white every 2 samples; runoff red/blue stripes 10 m wide on the corner outside; lines 0.25 m white on both edges; a start/finish chequered strip at s = 0.
  - `static Vector3 TrackPoint(IReadOnlyList<TrackSample> samples, double s, double lateral)` — interpolates position and height (used by car placement in Task 5).
  - `static class TerrainBuilder { static MeshData Build(CircuitData c, IReadOnlyList<TrackSample> samples, double extent = 900, double cell = 8) }` — height = track height blended to a gentle noise field with distance from the nearest track sample (smoothstep 30 m → 250 m), always 0.15 m below the asphalt within the track width.
- [ ] **Step 1:** Tests: `Asphalt_is_closed_ribbon_with_expected_triangle_count` (2 triangles × samples); `All_normals_are_unit_and_no_NaN` (every mesh); `Asphalt_width_matches_profile` (vertex pairs at s = 0 and s ≈ 300 are 15 m and 24 m apart ±0.1); `Walls_skip_inside_of_turn_one_hairpin` (no wall vertex within 6 m of the centreline anywhere); `Curbs_only_at_corners`; `Terrain_sits_below_asphalt_near_track`; `Pack_offsets_stay_inside_track_width` (for every sample, `Pack(i, 3)` and grid slots keep |lateral| + 1 m < Width/2 — Review Focus 5).
- [ ] **Step 2–4:** fail → implement → `dotnet test --project games/Redline/Redline.Presentation.Tests` green.
- [ ] **Step 5:** Commit `feat(redline): track and terrain mesh builders`.

### Task 4: 2026 car mesh builder (pure C#)

**Files:**
- Create: `games/Redline/Redline.Presentation/Meshes/{CarMeshBuilder.cs, Livery.cs, Loft.cs}`
- Test: `games/Redline/Redline.Presentation.Tests/Meshes/CarMeshBuilderTests.cs`

**Interfaces:**
- Consumes: `MeshData`; `Grid2026` / team livery data from `Redline.Logic` (check what `Grid.cs` exposes; add a public read-only `Livery` record there if it doesn't, keeping the JSON as the single source).
- Produces: `static class CarMeshBuilder { static CarMeshes Build(Livery livery, int number, TcamColour tcam) }` → `record CarMeshes(MeshData Body, MeshData Carbon, MeshData Tyres, MeshData Rims, MeshData Halo, MeshData Lights, MeshData Helmet, IReadOnlyList<Vector3> WheelCentres, IReadOnlyList<(Vector3 Pos, Vector3 Normal, float Size)> NumberDecals)`. Body vertex colours carry the livery zones (top, side, stripe, nose, wing, endplate, halo); `Livery.Finish` (matte/gloss) is applied by the Godot material, not here. **Origin at ground level, midway between the axles, +Z forward**, so placement can put the origin on the track point.
- Port the shapes from `docs/superpowers/mockups/trivia-racer/f1car2026.js` (lofted superellipse sections, NACA wing profiles, lathed tyres with shouldered profile and compound stripe, spoked rims, halo, wishbones and push/pull rods, mirrors, floor with edge fences, diffuser, active-aero flaps, T-cam, rain lights), densified for the TV: each lofted body ≥ 48 sections × 32 around; total ≈ 40–70k triangles.
- [ ] **Step 1:** Tests: `Dimensions_match_2026_regs` (wheelbase 3.4 ±0.05 from `WheelCentres`, overall width ≤ 1.9 + 0.01, rear tyre width 0.375 ±0.02, front 0.28 ±0.02); `Triangle_budget_between_40k_and_70k`; `Normals_unit_no_NaN`; `Livery_zone_colours_present` (body colours include top and side colours of McLaren); `Two_number_decals_on_rear_endplates_and_one_on_nose`; `Builds_for_all_22_drivers`.
- [ ] **Step 2–4:** fail → implement → green.
- [ ] **Step 5:** Commit `feat(redline): procedural 2026 car mesh builder`.

### Task 5: REDLINE scene — world, cars, placement, motion, camera

**Files:**
- Create: `src/ArcBox.Host/games/redline/{RedlineScene.tscn, RedlineScene.cs, World/{WorldBuilder.cs, MeshConvert.cs, Materials.cs, Landmarks.cs, Trees.cs, RenderPreset.cs}, Cars/{CarNode.cs, CarFactory.cs, SpinSmoke.cs}, Camera/{CameraRig.cs}, THIRD_PARTY.md}`
- Modify: `src/ArcBox.Host/ArcBox.Host.csproj` (reference `Redline.Logic`, `Redline.Presentation`)

**Interfaces:**
- Consumes: Task 3/4 builders, `CameraDirector.Pick/RevealFollowUp`, `LapMath`, `RedlineHostView` (deserialize with `ArcJson.Options`).
- Produces: `RedlineScene : Node3D, IGameScene`.
  - `MeshConvert.ToArrayMesh(MeshData) → ArrayMesh` (one surface; vertex colours on).
  - World built once on `_Ready` (cached in a static for scene reloads): sky shader (gradient dome + sun glow, also the reflection source), `WorldEnvironment` per `RenderPreset`, sun (`DirectionalLight3D`, 4 splits), track meshes with materials (asphalt rough 0.9 with subtle noise texture, curbs, lines, runoff, walls with generic non-brand ad panels in team-neutral colours, fence semi-transparent), terrain grass, landmarks as simple but clean primitives (main grandstand + pit building along the straight, start gantry with 5 red lights over s = 0, Turn 1 grandstand, Observation Tower with its red canopy), trees as `MultiMeshInstance3D` (live-oak style: a trunk + 3 squashed spheres, ~1,500 instances, halved on Low).
  - `CarFactory.Create(driverCode)` → `CarNode` with body material (`StandardMaterial3D`, vertex colour as albedo, metallic 0.2, roughness 0.25 gloss with clearcoat / 0.6 matte), carbon, tyres, rims, halo, lights (emissive red), helmet, and number decals as `Label3D` (Titillium Web 900 italic, livery `number` colour).
  - Placement: target `s = LapMath.MetersAlongLap(distance, finishLine, lapLength)`; group cars by distance, order inside a group by position, lateral/back from `LapMath.Pack`; Grid phase uses `LapMath.Grid(position)` behind s = 0; DriverSelect shows the line-up on the grid too. Car yaw from the track tangent, pitch from the height slope.
  - Motion: on a new Reveal view, tween every car from its `previousDistance` slot to its `distance` slot over 4 s (ease-out cubic, along the curve, not straight lines); wheels spin with speed; an outcome of `SpunOut` plays a 540° yaw over 1.6 s with `GPUParticles3D` smoke, then the slide back.
  - Camera: `CameraRig` turns a `Shot` into a camera transform with a smooth 0.6 s blend between shots of the same kind and a hard cut between kinds. `Grid`: high behind the grid looking down the straight; `Heli(subjects)`: 60 m out, 25 m up, framing the subjects' midpoint, slowly orbiting; `Trackside(subject)`: low front-quarter 9 m ahead, 5 m out, 1.2 m up, tracking the car; `Finish`: over the line, then the podium; `Lineup(subject)`: orbit the grid, holding 2 s on a newly claimed car. In Reveal, after 3.5 s switch to `CameraDirector.RevealFollowUp(view)`.
- [ ] **Step 1:** `dotnet build src/ArcBox.Host` succeeds.
- [ ] **Step 2:** Add a dev-only arg `--redline-demo` that feeds the scene a scripted sequence of `RedlineHostView`s built in C# (Driver Select → Grid → Throttle → Question → Reveal with a spin-out and a 3-car pack → Chequered), so it can be screenshotted without a server. Run under Xvfb with `--redline-demo --shots=<scratch>/m4 --res=1920x1080 --quit-after=60`.
- [ ] **Step 3:** Review every PNG against `09-cota-question.png`, `10-cota-reveal.png`, `11-cota-grid.png`, `15-full-grid-race.png`: cars on the asphalt, no z-fighting, walls not folding, liveries recognisable (papaya McLaren, red Ferrari, dark-blue matte Red Bull), shadows present. Fix and re-run until clean.
- [ ] **Step 4:** Commit `feat(redline): 3D COTA scene with procedural 2026 cars and camera rig`.

### Task 6: REDLINE HUD

**Files:**
- Create: `src/ArcBox.Host/games/redline/Hud/{RedlineHud.cs, TimingTower.cs, QuestionPanel.cs, TrackMap.cs, NameTags.cs, DriverSelectBoard.cs, GridStrip.cs, RoundResult.cs, ChequeredScreen.cs, HudStyle.cs}`

**Interfaces:**
- Consumes: `RedlineHostView`, `CarNode` positions, the active `Camera3D`.
- Produces: `RedlineHud : CanvasLayer` with `void Render(RedlineHostView view, RedlineHostView? previous)` and `void UpdateTags(Camera3D cam, IReadOnlyDictionary<string, Node3D> cars)` per frame.
  - Brand slab "REDLINE" top-left; question panel (`Round n/max`, category, difficulty pips, question text) and clock with bar from `deadlineUtc` (countdown uses the local clock and clamps to the phase length; the server drives the phase, so skew is cosmetic); answers A–D at the bottom; in Reveal the correct answer green, wrong ones dim, and the team-colour pips of who picked each.
  - Timing tower, 22 compact rows: position, team bar, code, player name in yellow (`#ffd400`) for humans, AI rows at 55% opacity, gap to leader (`LEAD`, `−n`, `TIED`), throttle chip from Question on (CRUISE grey / PUSH white / REDLINE red), status in Throttle/Question (LOCKED / CHOOSING / ANSWERED / PIT), ▲▼ movement in Reveal, row re-sort animated over 0.5 s.
  - Track map (`Line2D` of the centreline, 22 dots in team colours, players larger with a white ring, start/finish marker), top-right.
  - Name tags for player cars (team bar, position, code, name), projected with `UnprojectPosition`, hidden when `IsPositionBehind`.
  - Driver Select board (11 team rows × 2 seats; claimed seats white with the player name in red, like `14-driver-select.png`) and a claim lower-third (number, full name, team, chassis) for the latest claim.
  - Grid strip: the rules ("CRUISE +1 · PUSH +2/−1 · REDLINE +4/−2 + pit limiter", "First to {finishLine} or most distance after {maxRounds} rounds") and `attribution`.
  - Round result lower-third in Reveal for the biggest gainer or the spun car (matches the camera subject).
  - Chequered: podium top 3, full classification, awards cards (`title`, `name`, `detail`), attribution.
  - Style constants in `HudStyle` copied from `broadcast.css` (`--glass rgba(21,21,30,.86)`, `--edge`, `--red`, `--good`, `--bad`, `--dim`).
- [ ] **Step 1:** Extend `--redline-demo` with player names (incl. a 12-character one) and run at `--res=1280x720` and `--res=2560x1440`. Review the shots against `03-phase-throttle.png`, `04-phase-reveal.png`, `05-phase-finish.png`, `09-cota-question.png`, `14-driver-select.png` (Review Focus 4). Fix and re-run until clean.
- [ ] **Step 2:** Commit `feat(redline): broadcast HUD with timing tower, track map and name tags`.

### Task 7: Presets, settings and the full end-to-end run

**Files:**
- Modify: `src/ArcBox.Host/project.godot` (Forward+, MSAA 4×, screen-space AA, 8192 directional shadow atlas, `max_fps=144`, vsync off), `World/RenderPreset.cs`
- Create: `tools/tv-e2e.sh`, `docs/redline-tv.md`
- Modify: `docs/tv-host.md`

**Interfaces:**
- `RenderPreset.Apply(Environment env, DirectionalLight3D sun, Viewport vp, Preset p)` per Global Constraints; F2 cycles presets at runtime; F3 toggles an FPS/frame-time overlay.
- `tools/tv-e2e.sh`: builds web + server + host, starts the server (OpenTDB disabled), starts the host under Xvfb with `--new-room --shots`, reads the room code from the host log line `ROOM <CODE>`, runs `web/play/e2e/redline.e2e.mjs` against that room with 3 phones (`ARCBOX_ROOM=<CODE>`; extend the script to join an existing room when `ARCBOX_ROOM` is set instead of creating one), and leaves shots of every phase from both TV and phones.
- `docs/redline-tv.md`: presets, expected performance notes (to be measured on the RTX 5080 — this sandbox renders on a CPU rasteriser and cannot measure frame rate), and the screenshot workflow.
- [ ] **Step 1:** Run `tools/tv-e2e.sh` through at least the first Reveal (full race with `ARCBOX_FULL=1` if time allows); every TV phase has a screenshot; no exceptions in the Godot log (`grep -i "exception\|error"` returns only the known ALSA/audio lines).
- [ ] **Step 2:** `dotnet test --solution ArcBox.slnx` and `cd web/play && npm test` green.
- [ ] **Step 3:** Commit `feat(redline): render presets and TV end-to-end script`.

---

## Done criteria

- M3 (platform §9): full lobby → game → lobby loop on the TV with dev phones; killing and relaunching the host resumes the room.
- M4: a REDLINE race runs from Driver Select to Chequered with the TV rendering the 3D COTA scene and HUD and phones playing, all captured in screenshots. Frame-rate on the RTX 5080 remains to be measured by the user.
