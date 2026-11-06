# ArcBox

A Jackbox-style party game platform. A Godot TV host shows the game, players join from phone browsers with a room code, and the server runs each game's C# logic.

## Play Wardenflame

The included cooperative RPG, **Wardenflame: Embers of the Vale**, has an explorable Ashen Ford, branching story, tactical battles, shops, camp rests and quests.

Install .NET 10, Godot **4.7.2 .NET**, and Node 22+ with npm. From the repository root:

```sh
npm --prefix web/play ci
npm --prefix web/play run build
# Terminal 1: server; phones reach this computer’s LAN IP on port 5080.
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --project src/ArcBox.Server -- --urls http://0.0.0.0:5080
```

In terminal 2, set `GODOT` to your Godot .NET executable (or use `godot` on PATH):

```sh
export GODOT=/absolute/path/to/Godot_v4.7.2-stable_mono_linux.x86_64
# Omit the export when godot is already on PATH.
dotnet build src/ArcBox.Host
"${GODOT:-godot}" --headless --rendering-method gl_compatibility --import --path src/ArcBox.Host
SERVER_IP=192.168.1.20 # Replace with this computer’s LAN IP, so the TV’s QR works on phones.
"${GODOT:-godot}" --path src/ArcBox.Host -- --server="http://${SERVER_IP}:5080" --new-room
```

Open `http://<server-LAN-IP>:5080/` on two phones and join the TV’s room. The VIP selects Wardenflame, creates a campaign and starts; each phone chooses a class, then the party enters Ashen Ford.

For **TV + two bot phones** on Linux, install Xvfb/Mesa and run:

```sh
ARCBOX_GAME=tactics ARCBOX_PHONES=2 ARCBOX_WALK=1 \
  TV_RENDERING_DRIVER=opengl3 \
  TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy' \
  bash tools/tv-e2e.sh
```

See [Developing Wardenflame](games/Tactics/DEVELOPING.md) for prerequisite details, Windows notes, automated-run logs, Chapter I routes, the fast test loop and asset rebuilds. Checked-in assets are ready to play.

## Platform design

- Design spec: [docs/superpowers/specs/2026-10-18-arcbox-platform-design.md](docs/superpowers/specs/2026-10-18-arcbox-platform-design.md)
- M0–M1 implementation plan: [docs/superpowers/plans/2026-10-18-arcbox-m0-m1-platform-core.md](docs/superpowers/plans/2026-10-18-arcbox-m0-m1-platform-core.md)

Implementation work is tracked as GitHub issues, one per plan task, labelled `agent-ready`.
