# TV host (`src/ArcBox.Host`)

The Godot 4.7 .NET app that runs on the TV: Connect → Lobby (room code, QR, players, game picker) → game scene →
Results → Lobby. All networking lives in the plain .NET library `src/ArcBox.Host.Core` (unit-tested); the Godot
project only draws what the server sends.

## Run it from the editor

1. Install Godot 4.7.2 **.NET** and the .NET 10 SDK.
2. Start a server: `dotnet run --project src/ArcBox.Server --urls http://localhost:5080`.
3. Open `src/ArcBox.Host/project.godot` in Godot and press Play (F5). The first open imports the fonts and the
   Blender models.

Keys on the lobby: **← / →** pick a game, **Enter** starts (when the server says the room can start). On Results, any
key returns to the lobby once the room is back there. **Esc** during a game ends it for everyone (host override).
Anywhere: **F2** cycles the render preset (Ultra → High → Low, saved for the next launch) and **F3** toggles the
performance overlay; see [`redline-tv.md`](redline-tv.md).

## Run it headless with screenshots

There is no GPU in CI or the dev container, so runs go through Xvfb with Mesa's software Vulkan (llvmpipe; needs the
`mesa-vulkan-drivers` package, otherwise Godot falls back to OpenGL):

```sh
dotnet build src/ArcBox.Host
GODOT=$(command -v godot) tools/godot-import.sh               # imports fonts and models into .godot/ when needed
xvfb-run -a -s "-screen 0 2560x1440x24" godot --path src/ArcBox.Host --rendering-driver vulkan -- \
  --new-room --shots=/tmp/shots --quit-after=30
```

Every screen change writes `<seq>-<screen>.png` (`001-connect.png`, `002-lobby.png`, `008-counter.png`,
`010-results.png`…). Lobby changes (a player joins, a game is picked) and scene phases (for Counter, the last three
seconds) are captured too. ALSA errors in the log are expected: there is no sound card.

The app prints `ROOM <CODE>` on stdout whenever it creates or resumes a room, so scripts can find the code.
`tools/tv-e2e.sh` uses that to run the TV, the server and bot phones together and collect screenshots from both
([`redline-tv.md`](redline-tv.md#toolstv-e2esh-tv-server-and-phones-together)).

## Arguments

Everything after `--` on the Godot command line; all optional.

| Argument | Meaning |
|---|---|
| `--server=<url>` | Server base URL. Default: `user://settings.json` (written when you press Connect), else `http://localhost:5080`. |
| `--new-room` | Ignore the saved session and create a fresh room. |
| `--shots=<dir>` | Save a PNG on every screen or phase change (see above). |
| `--quit-after=<seconds>` | Quit after that many seconds. |
| `--preset=ultra\|high\|low` | Render preset for 3D scenes. Default: the one saved with F2 in `user://settings.json`, else `ultra`. |
| `--perf` | Start with the F3 performance overlay on, and log it every 5 s (`PERF …` lines). |
| `--audio-log` | Print `AUDIO <bus> <name>` for every sound played (narration: `AUDIO Voice <theme>: <text>`), for runs without a sound card. See [`redline-tv.md`](redline-tv.md#audio-log). |
| `--autostart=<game>[:<players>]` | Dev only: select that game, and start it once that many players (default 1) are connected and the scene is ready. First game only. Used by `tools/tv-e2e.sh`. |
| `--res=<w>x<h>` | Window size. The UI is laid out at 1920×1080 and scales (`canvas_items`, `expand`). |
| `--redline-demo[=<step>]` | Dev only: play a scripted REDLINE race into the TV without a server (see below). |
| `--tactics-demo[=<map>[:changed]]` | Dev only: show a Tactics board without a server, with the art from the repository's `content/` (default `river_crossing`). `:changed` adds zones, a raised tile and the bridge's tiles sunk. With `--shots` it saves `…-tactics-battle-<map>.png`. |
| `--demo=campaign-lobby` | Dev only: show the lobby with a built-in campaign game selected, its campaign list and a roster with seated and away members (`Shell/CampaignLobbyDemo.cs`), without a server. With `--shots` it saves `001-lobby.png`. |

Unknown arguments are reported as warnings and ignored.

## Campaigns in the lobby

When the selected game uses campaigns (`LobbyState.Campaigns` is set), the lobby adds a third column: the game's campaigns
(click one to select it, `SelectCampaign`; "New campaign is created on the VIP's phone"), and, once one is selected, its
roster: each member's card title and subtitle, a colour bar (the card's colour, else the seated player's colour) and
SEATED or AWAY. Start says "Pick a campaign first" until one is selected. Games without campaigns (REDLINE, Counter) get
the lobby exactly as before.

## Content packs

The server sends `ContentRequired(gameId, packs)` when a game is selected, when it starts and on resume (spec §3.6). The TV
caches each pack under `user://content/<gameId>/<packId>/<hash>/` (on Linux
`~/.local/share/godot/app_userdata/ArcBox/content/`), downloading it from the server it's connected to and verifying every
file; the newest 3 hashes of each pack are kept. A game with no packs (REDLINE, Counter) is shown at once, as before.
Otherwise, if a pack isn't cached yet when the game starts, the TV shows a "Loading content… 42%" card (the average
across the game's packs) and shows the game once every pack is in, with the newest view it was sent meanwhile. If a
download or its verification fails, the card says "Couldn't load game content" with **Retry** (click or Enter), and the
error is logged; **Esc** ends the game as usual.

## Sessions and resume

After creating a room the host writes `user://session.json` = `{serverUrl, code, hostToken}`
(on Linux `~/.local/share/godot/app_userdata/ArcBox/session.json`). On the next launch it calls `ResumeHost` with
it, so killing and relaunching the TV keeps the room, its players and any game in progress: the server re-sends the
lobby (and the current host view), and the TV redraws from that snapshot. If the resume fails (the server restarted,
or the room expired), the file is deleted and a new room is created. A session saved for a different server URL is
ignored.

While running:

- **Server unreachable at launch:** the Connect screen shows the error and retries every 5 s.
- **Connection drops:** a "reconnecting" banner appears; SignalR reconnects with the platform backoff
  (0, 1, 2, 5, 10, 10… s) and the host calls `ResumeHost` again. If the room is gone, the TV opens a new one.
- **`RoomClosed`:** the session is cleared and a new room opens.

## REDLINE scene

`games/redline/RedlineScene.tscn` draws the race in 3D: Circuit of the Americas with the 22 2026 cars. The cars are
the Blender-built `games/redline/models/car_2026*.glb` (see [`blender-models.md`](blender-models.md)), loaded once in
the background with the team colours bound to named material slots, and fall back to the procedural car when the model
is missing or broken. The rest of the geometry comes from the pure-C# builders in `games/Redline/Redline.Presentation`
(track, terrain, the fallback cars), built at scene load on the thread pool and cached for the process; placement (`Formation`), Reveal motion
(`RevealMotion`), shot geometry (`ShotFraming`) and shot choice (`CameraDirector`) are unit-tested there too. The scene
logs `REDLINE world ready in … ms` with the cold build times.

The HUD hangs off `RedlineScene.Hud` (`RedlineHud`, a `CanvasLayer`): it gets every view through `Render(view,
previous)` and, each frame, `UpdateTags(ActiveCamera, Cars)` with the live camera and the cars by driver code.

**HUD.** `games/redline/Hud/` is the broadcast graphics from the mockups (`03`–`05`, `09`–`11`, `14`, `15`), laid out on
the 1920×1080 base that `canvas_items` stretch scales to any 16:9 window:

| Phase | On screen |
|---|---|
| Driver Select | REDLINE slab; the claim board (11 teams × 2 seats, claimed seats white with the player in red) with the clock; the claim lower-third for the newest claim (the car the line-up camera holds) |
| Qualifying (Reading, Question, Result) | header "QUALIFYING · LAP n/3" with the category and LISTEN while the question is read; the tower as a timing board (POS · DRIVER · LAP · TOTAL); answers A–D, the correct one green at the Result; no track map; the provisional pole and pole lower-thirds (see [`redline-tv.md`](redline-tv.md#qualifying)) |
| Grid | header with the five start lights; 22-row timing tower; track map; rules strip with the payouts from the view and the question credit |
| Throttle | header (round, category, difficulty, clock); tower with LOCKED / CHOOSING / PIT; map; "n OF m LOCKED" strip with the payout chips |
| Question | header with the question; tower with every throttle and a tick for players who answered; map; answers A–D |
| Reveal | the correct answer green, wrong ones dimmed, team-colour pips of who picked each; tower result chips, ▲▼ and a 0.5 s re-sort; the round lower-third for the camera's subject until the follow-up cut |
| Chequered | "ALEX WINS", podium, full classification, awards, credit |

Name tags float over **player** cars only, and hide when the car is behind the camera, off screen, over 260 m away or
under a panel; overlapping tags stack. Everything worded, ordered or culled is pure C# in
`Redline.Presentation/Hud` (`Tower`, `HudText`, `HudClock`, `RoundResult`, `DriverSelectBoard`, `NameTagLayout`,
`TrackMapLayout`, `StartLights`) with tests; the Godot classes only draw. The clock reads `deadlineUtc` against the local
clock when a view arrives, clamps it to the phase length and then counts down on frame time; the demo stamps its
deadlines on the scene clock, so `--fixed-fps` captures show a sensible countdown.

**Demo without a server.** `--redline-demo` feeds the scene a scripted race (`DemoRace` in Redline.Presentation): Driver
Select with claims arriving, three laps of qualifying (a deleted player lap, a purple lap each lap, a pole change), the
Grid in the qualifying order, then round 4's Throttle, Question and Reveal (a human Redline spin-out, a three-car pack
at the front), the Chequered flag and the podium (Anthem, then Champagne with scripted sprays from all three steps).
`--redline-demo=<step>` starts at `driverselect`, `qualifying`, `grid`, `throttle`, `question`, `reveal`, `chequered`,
`anthem` or `champagne`; `--redline-demo-winner=<code>` crowns another player's car (see [`redline-tv.md`](redline-tv.md#the-podium)).
The script runs on the scene clock, so Godot's `--fixed-fps` makes a capture deterministic
however slowly frames render:

```sh
xvfb-run -a -s "-screen 0 1920x1080x24" godot --path src/ArcBox.Host --rendering-driver vulkan --fixed-fps 8 -- \
  --redline-demo --shots=/tmp/redline --res=1920x1080 --preset=high --quit-after=80
```

With `--shots` the scene saves each phase about a second after it starts, each Driver Select claim, Reveal at 2, 4
and 6 s, and each qualifying Result again at 2.2 s with its card in (`qualifyingresult-lap2-card`; the qualifying
shots carry the lap).

**Presets.** Ultra (default): SDFGI with half-resolution probes, SSR, SSAO + SSIL, volumetric haze, a 4-split
8192 shadow atlas, MSAA 4× + SMAA, 144 fps cap with VSync off. High: Ultra without SDFGI and SSR. Low: no
screen-space effects, 2-split 2048 shadows, FXAA, half the trees, no skid marks or marbles on the track, every other
armco and fence post, barrier blocks at LOD and a static big screen (still Forward+). F2 switches between them while the race runs. Details, keys and performance notes: [`redline-tv.md`](redline-tv.md).

**Lobby warm-up.** A scene that implements `IPreloadScene` is instantiated as soon as its game is selected in the
lobby and gets ready behind it; REDLINE builds its world and draws it (and its HUD panels, under the lobby) for a few
frames, so the world build and the first-frame shader compiles happen before lights out. When the game starts the TV
shows that same instance. `--autostart` waits for it. When a game ends its scene is kept the same way (parked) for as
long as that game stays selected, so a rematch starts at once; freeing it would also free its materials' compiled
shaders.

**Haze.** Distance haze comes from the exponential fog's aerial perspective; the volumetric fog is kept thin (density
0.0007, little GI inject) because at 60–70 m, the helicopter shots' distance to the cars, anything thicker lays a white veil
over the whole frame. AgX runs with white 8 and contrast 1.45 (Godot's defaults, 16.29 and 1.25, look flat here).

**Performance** must be measured on the target machine (RTX 5080, 2560×1440) with F3: this repository's sandbox renders
with Mesa's CPU rasteriser (llvmpipe), where a 1080p frame takes several seconds, so frame rates from it mean nothing.

## Tactics scene

`games/tactics/TacticsScene.tscn` shows the game's banner and each phase's table: the Muster roster, the encounter list and
the results, and in battle the board. All of it is worded by `PhaseScreen` in `games/Tactics/Tactics.Presentation`, with
tests; the Godot class only draws.

**The board** uses the pack's art the way its manifests describe it: tiles are 32 px, projected 2:1 with height steps of
16 px, tops sampled in world space (one texture over 4 x 4 tiles) with sides and lips below them, all drawn as textured
quads, at twice the art's size on the 1080p layout. `Tactics.Presentation/Board` works everything out: `BoardProjection` is
the only code that knows the projection, `BoardTerrain` is the map plus what the battle changed (a tile's `Centre` is where
anything stands on it), `BoardPlan` is every face and sprite in painter's order (`BoardOrder.Key` is how later things sort
in), `BoardFit` puts it on the screen and `MaterialLook` picks the sides that go with a map's materials. `BoardCanvas` only
draws the plan. Zones, height changes and sunk tiles come from the host view's `battle.board`, which phones don't get.

To run the TV against a real game without
phones or a browser, `tools/tactics-playtest/bots.cjs` joins a room as N players and plays a whole session over the real
hub; its header has the commands.

## Adding a game scene

1. Create `res://games/<id>/<Name>Scene.tscn` whose root script implements `IGameScene`:
   `Render(JsonElement view, JsonElement? previous)` gets the newest host view (views that arrive in the same frame
   are coalesced) and the one rendered before it; deserialize it with `ArcJson.Options`.
   `SetShots(Screenshotter?)` lets the scene capture its own phase changes.
2. Map the game ID in `Shell/GameSceneMap.cs`.
3. To speak, also implement `IAudioScene`: `SetNarrator(Narrator)` hands the scene the shell's shared narrator
   (`Audio/Narrator.cs`), before its first `Render`. Call `narrator.Say(text, "<id>.<theme>", NarrationPriority.Question
   or .Commentary)` with a voice theme your game registers on the server ([`speech-setup.md`](speech-setup.md#voice-themes-for-game-authors)).
   The narrator fetches the server voice with the room's host credentials, falls back to the TV's built-in voice,
   queues lines one at a time and ducks Music and Sfx while it speaks. Play your own sounds on the `Music` and `Sfx`
   buses (`AudioBuses`) and log them with `AudioLog.Play(bus, name)`.

4. To draw downloaded content (maps, sprite sheets, dialogue from `content/<id>/<packId>/` on the server), also implement
   `IContentScene`: `SetContentRoots(rootsByPackId)` gives each pack's local, verified directory before the first
   `Render` (empty for a game without packs).
5. To tell the game when something on the TV finished (spec §3.7), also implement `ISignalingScene`: the shell sets
   `Signal` when it creates the scene, and `Signal("done", new { seq = 41 })` reaches the game's `IHostAware.OnHostSignal`.
   It's fire-and-forget (failures are logged). A signal may only shorten a wait the game already has a fallback timer
   for, and should carry the sequence number of the event it acknowledges.

Until the scene exists, the TV shows a "scene not installed" screen with the raw view JSON, and nothing crashes.
