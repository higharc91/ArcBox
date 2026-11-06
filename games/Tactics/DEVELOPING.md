# Developing Wardenflame

Wardenflame is ArcBox’s cooperative RPG. The server owns the campaign, overworld, story, shops, rest, quests and battles; Godot draws the TV, and browser phones control the heroes. The included campaign, *Embers of the Vale*, starts in Ashen Ford.

## Prerequisites

| Tool | Required for | Version / setup |
|---|---|---|
| .NET SDK | Server, game, TV C# and tests | .NET 10; tested with 10.0.403. `global.json` selects Microsoft.Testing.Platform, not an SDK version. |
| Godot **.NET** edition | TV | 4.7.2 mono; set `GODOT` to its executable or put `godot` on PATH. The standard non-.NET edition cannot run the TV. |
| Node.js and npm | Phone build, bots | Node 22 or newer; tested with Node 24.19.0. Install dependencies with `npm ci`. |
| Python | Full checks and optional art tools | Python 3.11+; tested with 3.12.14. Install Pillow and numpy for the asset tests. |
| Bash and curl | Helper scripts | Linux/macOS shell or Git Bash. On Windows point `GODOT` at the mono **console** executable. |
| Xvfb and Mesa graphics | Linux TV automation | Install `xvfb`, `libgl1-mesa-dri` (and `libvulkan1`, `mesa-vulkan-drivers` for Vulkan); interactive TV play uses your normal display. |

Clone the repository and run the following from its root. Checked-in art and audio are sufficient to play; no art generation or speech-provider credentials are required.

## Run on a TV and phones

Build the phone once:

```sh
npm --prefix web/play ci
npm --prefix web/play run build
```

Start the server in one terminal. Binding to all interfaces lets phones on your Wi-Fi reach it:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --project src/ArcBox.Server -- --urls http://0.0.0.0:5080
```

In another terminal, build/import the TV and open a new room:

```sh
export GODOT=/absolute/path/to/Godot_v4.7.2-stable_mono_linux.x86_64
# Omit the export if godot is already on PATH.
dotnet build src/ArcBox.Host
"${GODOT:-godot}" --headless --rendering-method gl_compatibility --import --path src/ArcBox.Host
SERVER_IP=192.168.1.20 # Replace with this computer’s LAN IP, so the TV’s QR works on phones.
"${GODOT:-godot}" --path src/ArcBox.Host -- --server="http://${SERVER_IP}:5080" --new-room
```

If the import is interrupted, rerun `bash tools/godot-import.sh` before launching; `bash tools/godot-import.sh --check` confirms that all imported assets are ready.

On each phone, open `http://<server-LAN-IP>:5080/`, enter the TV’s room code and a name, and join. Use the server computer’s LAN address rather than `localhost`; allow port 5080 through its firewall. Keep phones and server on the same reachable network (guest Wi-Fi may isolate devices).

The first phone is VIP: select **Wardenflame**, create a campaign, and start. Each phone picks a class; the party appears in Ashen Ford. The leader steers while everyone watches the TV. Campaigns persist in the server’s SQLite storage; keep that storage when restarting. Omit `--new-room` when reconnecting the TV to its remembered room.

For phone UI iteration, `npm --prefix web/play run dev -- --host 0.0.0.0` serves Vite; its hub proxy expects the server on port 5080. Use the built server root URL for ordinary game-night hosting.

## TV plus two bot phones

Complete the TV build/import block above once on a fresh clone. The existing runner builds everything, imports Godot assets, starts an isolated server/database and TV, then joins bots. On Linux it supplies its own Xvfb display and software renderer. It stops the processes it started and retains logs/screenshots:

```sh
ARCBOX_GAME=tactics ARCBOX_PHONES=2 ARCBOX_WALK=1 \
  TV_RENDERING_DRIVER=opengl3 \
  TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy' \
  ARCBOX_E2E_OUT=/tmp/wardenflame-two-phones \
  bash tools/tv-e2e.sh
```

This command explicitly selects the software-compatible OpenGL renderer; omit the two `TV_` settings on a Vulkan-capable machine for the default renderer and audio. It starts in Ashen Ford and exercises walking, a server restart, TV reconnection and a dropped phone. See `phones.log`, `tv.log`, `server.log`, and `tv/` inside the output folder. For a shorter battle smoke test, omit `ARCBOX_WALK=1`; the server’s development battle picker supplies the encounter and AI fills missing combat seats.

| Goal | Settings before `bash tools/tv-e2e.sh` |
|---|---|
| Battle to results | `ARCBOX_GAME=tactics` |
| Restart server during battle | `ARCBOX_GAME=tactics ARCBOX_RESTART=1` |
| Kill and resume TV | `ARCBOX_GAME=tactics ARCBOX_RECONNECT=1` |
| Whole Chapter I, force path | `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force` |
| Whole Chapter I, parley path | `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=parley` |
| Shops / duplicate trades | `ARCBOX_SHOP=1` |
| Camp during Chapter I | `ARCBOX_CHAPTER=1 ARCBOX_CAMP=1` |

Set `GODOT` when needed. `--skip-build` reuses a successful build; `TV_ENGINE_ARGS='--audio-driver Dummy'` silences automated TV output. If Vulkan is unavailable, use `TV_RENDERING_DRIVER=opengl3 TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy'`. `bash tools/tv-e2e.sh --help` documents all modes and expected checks. Browser-based REDLINE automation additionally needs its browser dependencies; Tactics bot phones use Node directly.

## Test loop

```sh
# Game behavior while iterating; MTP arguments follow the second --.
dotnet test games/Tactics/Tactics.Game.Tests -c Release -- --filter-class '*RestTests'
# Phone changes.
npm --prefix web/play test
npm --prefix web/play run build
# Tactics-focused repository checks, then the complete merge gate.
bash tools/check-all.sh --fast
bash tools/check-all.sh --full
```

Both checker modes include phone, Node, asset/audio and lore checks; `--fast` narrows the .NET test projects. Godot TV automation is separate. GitHub Actions is currently intentionally disabled; run checks locally without enabling the workflow.

## Content and asset tools

- `content/tactics/base/rules/`: classes, abilities, encounters, regions, interactables, shops and other game definitions.
- `content/tactics/base/lore/`: campaign arcs, scenes, barks, schemas and voice clips. Run `python3 tools/tactics-lore/check.py` after lore changes.
- Other directories under `content/tactics/base/`: the baked art/audio pack and manifests.
- `games/Tactics/Tactics.Game/`: campaign and session behavior; `Tactics.Logic/` contains the deterministic rules and world helpers.
- `web/play/src/games/tactics/`: phone screens; `Tactics.Presentation/` supplies TV presentation models.

The [asset tools guide](../../tools/tactics-assets/README.md) lists source sets, build order and explicit previews. Install Python dependencies with `python3 -m pip install Pillow numpy`. To inspect a rebuild, use a disposable worktree, since generation changes tracked content:

```sh
git worktree add --detach ../arcbox-art-review HEAD
cd ../arcbox-art-review
python3 tools/tactics-assets/build.py
git diff --stat -- content/
# Optional review images, after the corresponding source sets are built:
python3 tools/tactics-assets/build.py previews showcase gallery
```

The measured cold default-build attempt took **342.97 seconds (5m43s)** before the existing icon-builder error. A complete deterministic rebuild is still blocked by [#203](https://github.com/higharc91/ArcBox/issues/203) / [draft #230](https://github.com/higharc91/ArcBox/pull/230), including generated-art drift; the figure is a failed-attempt time, not a successful build benchmark. Playing and testing use the checked-in pack. Review generated diffs before adopting any art output.

The [tracker index](../../docs/superpowers/README.md) identifies live work. The [overworld issue board](../../docs/superpowers/plans/overworld-issue-board.md) links the feature specs and work items. Historical simulation and bot-session notes remain in [the original game guide](README.md); this guide is the path for running the whole game.
