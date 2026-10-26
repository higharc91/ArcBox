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

## Adding a game scene

1. Create `res://games/<id>/<Name>Scene.tscn` whose root script implements `IGameScene`:
   `Render(JsonElement view, JsonElement? previous)` gets the newest host view (views that arrive in the same frame
   are coalesced) and the one rendered before it; deserialize it with `ArcJson.Options`.
   `SetShots(Screenshotter?)` lets the scene capture its own phase changes.
2. Map the game ID in `Shell/GameSceneMap.cs`.

Until the scene exists, the TV shows a "scene not installed" screen with the raw view JSON, and nothing crashes.
