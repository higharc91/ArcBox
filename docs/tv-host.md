# TV host (`src/ArcBox.Host`)

The Godot 4.7 .NET app that runs on the TV: Connect → Lobby (room code, QR, players, game picker) → game scene →
Results → Lobby. All networking lives in the plain .NET library `src/ArcBox.Host.Core` (unit-tested); the Godot
project only draws what the server sends.

## Run it from the editor

1. Install Godot 4.7.2 **.NET** and the .NET 10 SDK.
2. Start a server: `dotnet run --project src/ArcBox.Server --urls http://localhost:5080`.
3. Open `src/ArcBox.Host/project.godot` in Godot and press Play (F5). The first open imports the fonts.

Keys on the lobby: **← / →** pick a game, **Enter** starts (when the server says the room can start). On Results, any
key returns to the lobby once the room is back there. **Esc** during a game ends it for everyone (host override).

## Run it headless with screenshots

There is no GPU in CI or the dev container, so runs go through Xvfb with Mesa's software Vulkan (llvmpipe; needs the
`mesa-vulkan-drivers` package, otherwise Godot falls back to OpenGL):

```sh
dotnet build src/ArcBox.Host
godot --headless --editor --quit --path src/ArcBox.Host     # first run only: imports fonts into .godot/
xvfb-run -a -s "-screen 0 2560x1440x24" godot --path src/ArcBox.Host --rendering-driver vulkan -- \
  --new-room --shots=/tmp/shots --quit-after=30
```

Every screen change writes `<seq>-<screen>.png` (`001-connect.png`, `002-lobby.png`, `008-counter.png`,
`010-results.png`…). Lobby changes (a player joins, a game is picked) and scene phases (for Counter, the last three
seconds) are captured too. ALSA errors in the log are expected: there is no sound card.

The app prints `ROOM <CODE>` on stdout whenever it creates or resumes a room, so scripts can find the code.

## Arguments

Everything after `--` on the Godot command line; all optional.

| Argument | Meaning |
|---|---|
| `--server=<url>` | Server base URL. Default: `user://settings.json` (written when you press Connect), else `http://localhost:5080`. |
| `--new-room` | Ignore the saved session and create a fresh room. |
| `--shots=<dir>` | Save a PNG on every screen or phase change (see above). |
| `--quit-after=<seconds>` | Quit after that many seconds. |
| `--preset=ultra\|high\|low` | Render preset for 3D scenes (parsed now; applied by the REDLINE scene). |
| `--res=<w>x<h>` | Window size. The UI is laid out at 1920×1080 and scales (`canvas_items`, `expand`). |
| `--redline-demo[=<step>]` | Dev only: play a scripted REDLINE race into the TV without a server (see below). |

Unknown arguments are reported as warnings and ignored.

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

`games/redline/RedlineScene.tscn` draws the race in 3D: Circuit of the Americas with the 22 procedural 2026 cars. All
geometry comes from the pure-C# builders in `games/Redline/Redline.Presentation` (track, terrain, cars), built at scene
load on the thread pool and cached for the process (no baked `.res` files); placement (`Formation`), Reveal motion
(`RevealMotion`), shot geometry (`ShotFraming`) and shot choice (`CameraDirector`) are unit-tested there too. The scene
logs `REDLINE world ready in … ms` with the cold build times.

The HUD hangs off `RedlineScene.Hud` (`RedlineHud`, a `CanvasLayer`): it gets every view through `Render(view,
previous)` and, each frame, `UpdateTags(ActiveCamera, Cars)` with the live camera and the cars by driver code.

**Demo without a server.** `--redline-demo` feeds the scene a scripted race (`DemoRace` in Redline.Presentation): Driver
Select with claims arriving, Grid, then round 4's Throttle, Question and Reveal (a human Redline spin-out, a three-car pack
at the front) and the Chequered flag. `--redline-demo=<step>` starts at `driverselect`, `grid`, `throttle`, `question`,
`reveal` or `chequered`. The script runs on the scene clock, so Godot's `--fixed-fps` makes a capture deterministic
however slowly frames render:

```sh
xvfb-run -a -s "-screen 0 1920x1080x24" godot --path src/ArcBox.Host --rendering-driver vulkan --fixed-fps 8 -- \
  --redline-demo --shots=/tmp/redline --res=1920x1080 --preset=high --quit-after=48
```

With `--shots` the scene saves each phase about a second after it starts, each Driver Select claim, and Reveal at 2, 4
and 6 s.

**Presets.** `--preset=ultra` (default): SDFGI with half-resolution probes, SSR, SSAO + SSIL, volumetric haze, a 4-split
8192 shadow atlas, MSAA 4× + SMAA, 144 fps cap with VSync off. `high`: Ultra without SDFGI and SSR. `low`: no
screen-space effects, 2-split 2048 shadows, FXAA, half the trees (still Forward+).

**Performance** must be measured on the target machine (RTX 5080, 2560×1440): this repository's sandbox renders with
Mesa's CPU rasteriser (llvmpipe), where a 1080p frame takes several seconds, so frame rates from it mean nothing.

## Adding a game scene

1. Create `res://games/<id>/<Name>Scene.tscn` whose root script implements `IGameScene`:
   `Render(JsonElement view, JsonElement? previous)` gets the newest host view (views that arrive in the same frame
   are coalesced) and the one rendered before it; deserialize it with `ArcJson.Options`.
   `SetShots(Screenshotter?)` lets the scene capture its own phase changes.
2. Map the game ID in `Shell/GameSceneMap.cs`.

Until the scene exists, the TV shows a "scene not installed" screen with the raw view JSON, and nothing crashes.
