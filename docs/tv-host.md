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
| `--audio-log` | Print `AUDIO <bus> <name>` for every sound played (narration: `AUDIO Voice <theme>: <text>`; a recorded line: `AUDIO Voice clip <file> <theme>: <text>`), for runs without a sound card. See [`redline-tv.md`](redline-tv.md#audio-log). |
| `--autostart=<game>[:<players>]` | Dev only: select that game, and start it once that many players (default 1) are connected and the scene is ready. First game only. Used by `tools/tv-e2e.sh`. |
| `--res=<w>x<h>` | Window size. The UI is laid out at 1920×1080 and scales (`canvas_items`, `expand`). |
| `--redline-demo[=<step>]` | Dev only: play a scripted REDLINE race into the TV without a server (see below). |
| `--drop-signals` | Dev only: the TV never sends a host signal, so a game runs on its fallback timers. Proves a game doesn't need them. |
| `--beat-log` | Dev only: print a `BEAT` line for each Tactics beat played: when it ran, how long it took and what was in it. |
| `--tactics-demo[=<map>[:changed\|:fight]]` | Dev only: show a Tactics board without a server, with the art from the repository's `content/` (default `river_crossing`). `:changed` adds zones, raised tiles and the bridge's tiles sunk; `:fight` adds one of every kind of unit. With `--shots` it saves `…-tactics-battle-<map>.png`. |
| `--world-demo[=<region>|hilly[:<block tiles>]]` | Dev only: open a whole region the way the TV will, without a server, with the art and region from the repository's `content/` (default `ashen_ford`; `hilly` is `DemoRegion`'s stress case), walk a stand-in the length of it at twice walking pace, print what it cost in `WORLD` lines and quit. Use `--res=1920x1080`. With `--shots` it saves `world-<spec>.png`, then one `world-<spot>.png` for each place `WorldSpots` finds: behind a cliff, behind a crate, water at a block edge, a block corner, the region's edge and both sides of the void. See [the world](#the-world). |
| `--walk-log` | Dev only: print `WALK <seq> <x> <y> <ms>` as each step reaches the TV, `<ms>` being the wall clock in Unix milliseconds, and a `WALKSTATS` line every 288 frames of walking: frame times, and how many pixels the leader and the ground moved a frame. See [walking](#walking). |
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
  (0, 1, 2, 5, 10, 10… s) and the host calls `ResumeHost` again. If the room is gone, the TV opens a new one. In a Tactics
  battle the server restarting shows "Resuming the battle…" until the players are back, and the TV picks the fight up
  from there ([Catching up](#tactics-scene)). `ARCBOX_GAME=tactics ARCBOX_RESTART=1 tools/tv-e2e.sh` runs that, and
  `ARCBOX_RECONNECT=1` in place of `ARCBOX_RESTART=1` kills and relaunches the TV mid-battle.
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

The battle plays from its beats. `BeatPlayer` in `Tactics.Presentation/Beats` takes each view's new beats (`BeatQueue`), plays them one
at a time from their own events (`BeatScript` turns a beat into timed cues) and holds the battle as the player has seen it
(`DisplayedBattle`), because the view's units and board are already past the beat on screen. Only with nothing left to play does
the view become the picture, which also heals anything that drifted. Each finished beat sends `shown`, which only shortens the
game's fallback wait, so a slow or missing TV costs a lagging picture and no more (`--drop-signals` proves it). At Results the view's
`LastBattle` carries the blow that ended the fight, and the TV plays out what's left before it shows the table. `say` lines
go through the shell's narrator in the `tactics.narrator` theme, a speaker's own or `tactics.character` (`TacticsVoices.ForSpeaker`), with a caption.

**Catching up.** A TV that can't play its way back drops to the newest beat (`BeatQueue.Late`, `BeatPlayer`): it takes the view
as it stands, conditions and board included, and plays that one beat from just before it (`DisplayedBattle.Before` stands the
units back where the beat found them), so no old beat or old line comes back. Three things bring it on: a scene joining a fight, whose
first view holds up to 20 beats it never saw (a battle's own start, its first two beats, isn't a join, and plays whole); a
view whose oldest beat is past the last one seen, as after a reconnect; and being behind, which is a beat waiting more
than `BeatQueue.MostLag` (8 s of real time) with others after it. Real play has bursts of 6 beats that drain in about 4 s, and those
play whole. A beat on stage finishes first. Beats dropped are never reported, and the newest still sends `shown`, so the
game hears only what it's waiting on. The TV logs `TACTICS catch-up to beat <n>, <m> skipped`, and each new screen as
`TACTICS screen <label>`, which `tools/tv-e2e.sh` reads.

`games/tactics/TacticsScene.tscn` shows the game's banner and each phase's table: the Muster roster, the encounter list and
the results, and in battle the board. All of it is worded by `PhaseScreen` in `games/Tactics/Tactics.Presentation`, with
tests; the Godot class only draws.

**The board** uses the pack's art the way its manifests describe it: tiles are 32 px, projected 2:1 with height steps of
16 px, tops sampled in world space (one texture over 4 x 4 tiles) with sides and lips below them, all drawn as textured
quads, at twice the art's size on the 1080p layout. `Tactics.Presentation/Board` works everything out: `BoardProjection` is
the only code that knows the projection, `BoardTerrain` is the map plus what the battle changed (a tile's `Centre` is where
anything stands on it), `BoardPlan` is every face and sprite in painter's order (`BoardOrder.Key` is how later things sort
in), `BoardFit` puts it on the screen, fixed from the map file for the whole battle, and `MaterialLook` picks the sides that go with a map's materials. `BoardCanvas` only
draws the plan. Zones, height changes and sunk tiles come from the host view's `battle.board`, which phones don't get.

**The units** are a second plan over the board's. `UnitPlan` turns the view's units into `BoardUnit`s with what `UnitArt` reads
from the manifests: `characters`, `beasts` or `bosses` by sprite key, the idle strip or the Downed frame, the HP bar and
digits from `ui`, `status` effects and `icons`. Each sorts into the board by `BoardOrder.Key` (`Merge`), so a tall tile in
front hides it; a sprite wider than a tile goes after its whole diagonal, and a unit between tiles (`UnitPlace`) sorts with
the nearer one. `BoardCanvas.SetUnits` swaps them without touching the board's plan. Bars, countdowns, icons and the turn arrow
go on top of everything, so a hidden unit still has its bar, and they stack down onto the unit rather than into the top
strip. The view has no facing, so a unit looks at its nearest foe. Characters have no sprite in the engine, so the host view
gives them their class's preset (`HostSprite`); phones still get null. With `--shots`, a battle's label gains `-conditions`
or `-downed` when those first show, so a real fight leaves a shot of each.

**The light** follows the pack's recipe (`dungeon/manifest.json`'s `lighting`): what's drawn is its art x (ambient + each light's colour x
power x flicker x falloff), at most 1.3. `BoardLighting`, in `Tactics.Presentation/Board`, builds the lights from the `BoardPlan`: a
prop's `light` and a texture's `emissive` scatter rule, so a tile that sinks takes its light with it, and they're rebuilt with the plan.
Flicker is a function of time and each light's phase, so tests can pin it. A room's ambient comes from a word in its map's id naming
one of the recipe's rooms (`crypt_hall`, `crystal_cave`, `hall_of_the_flame`), lifted by `AmbientLift` on every map alike, as the
recipe's dioramas have far more lights than the maps. The rest are open air: with no lights there, `Active` is false and the board
draws as if none of this existed. `LitBoard` decides what light does to each item, pure and safe off the main thread: a top in reach
becomes four cells so a pool can peak inside a tile, then the face once more for its glow and outline. `BoardCanvas` puts the light in
its vertex colours and redraws at its usual 20 Hz. That's no extra pass and no new shader.

Vertex colours were chosen for the picture, not for cost. A PointLight2D prototype on the finale drew about 100 fewer calls than the
first vertex version (505 against 605; the glow fix since brought that to 521), with no worse a frame on an RTX 5080. But the board is one
canvas item holding marks and art alike: CanvasModulate dimmed the HUD on the same canvas, colours blew out with no ceiling (the troll
went solid yellow), and flames were lit like anything else. Fixing that means splitting the canvas. Godot may also cap the lights on one
canvas item, at 16 as far as I recall; that's from memory, unchecked, and no map has over 10 lights. Neither approach has been measured on
a modest TV box.

The base pack now holds a region, `rules/world/ashen_ford.json`, which the TV downloads with the rest of the pack. Re-exporting a region changes the pack's download hash, so a TV fetches the whole pack again. A battle saved mid-fight doesn't restart, as `rules/world/` isn't in the hash a battle save compares.

A region's blocks can share one `BoardLighting` (`Of` takes several plans), so a pool doesn't stop at a block's edge. Reads go through a
grid of tile-sized buckets, and `MeanAt` reads each light at its mean for baked art, whatever `Step` has done. Only `Step` is unsafe
beside another thread's reads of "now".

`BoardPainter` paints `LitBoard` items for both. A board passes its lighting at every draw; a world's blocks bake from the whole region's lighting when they're prepared
(`LitFace.MeanColourAt`, `LitSprite.MeanLight`, which no `Step` moves) and paint with none.

What glows by itself (`GlowMask`, from the pack's masks or, where the pack has none, the brightest shades of the light's colour) is
baked once and drawn again unlit, in its place in the painter's order, so a unit in front still hides it. Sources get the pack's
additive halos, in place of the recipe's bloom. The recipe's banding and dither aren't done: they need a shader. Figures take the light
near them, never under `FigureFloor`. Rings, status effects, zones, marks (`UnitMarks`, over the light) and the HUD stay unlit. Halos
are a separate additive layer, so they sit over units and rings: one canvas item has one blend mode, so they can't go in the depth order.

**The HUD** (`HudLayer`, `TurnStrip`) sits in the corners the diamond leaves free, in the pack's navy glass and gold: the
encounter, round, whose turn it is and the objective at the top left, the turn order at the top right. `BattleHud` in
`Tactics.Presentation/Hud` words and orders all of it from `BeatPlayer.Shown`, so it can't give away a beat that hasn't played.
A reaction prompt needs two things: its `ReactionOffered` has played (`DisplayedBattle` folds it, and `Reacted` clears it), and the
live `battle.reacting`, set only for a player, says it's still waiting. So it can't lead the beat that caused it, and an offer answered
before its beat plays never shows. The words are the game's banner. `ObjectiveWords` turns the view's `battle.objectives` into a sentence, and
`ObjectiveChanged` changes it when that event plays. `HudArt` reads the panels, frames and portraits. An absent player's
character has the AI badge on the strip, and a beat whose events are flagged `AiFinished` puts an "AI finished" badge over its
actor for at least `BeatTiming.AiFinished`. A panel over a unit fades; a reaction prompt, which has to be read, moves down the
left side instead. With `--shots`, a prompt reaching the screen is the moment `reaction`, an area spell's last target is `area`, and a beat the AI finished is the moment
`ai-finished`. `BeatLoop` runs the frame. If a beat throws, it logs it, drops it (`BeatPlayer.Skip`), still sends its `shown`, and
carries on. If drawing fails, no beat is to blame: it logs that once and the beats play on unseen, so held Results still come up.

To run the TV against a real game without
phones or a browser, `tools/tactics-playtest/bots.cjs` joins a room as N players and plays a whole session over the real
hub; its header has the commands.

## The world

A region is drawn from cached blocks: `WorldPlan` cuts `BoardPlan`'s items into 16-tile squares (`BoardPlan.BuildBlock`, the same code with `WorldGround`'s real neighbours, so there's no
seam or skirt) that share one `BoardLighting`, and `WorldBlock` records each one's items in `_Draw` once, so moving the camera changes only a transform. `Blocks` is in draw order: `ZIndex` is
block column + row, with the column breaking ties. That agrees with `BoardOrder` for anything up to two tiles wide, and the pack's widest prop is 56 px. `BlockQueue` hands out unrecorded blocks
nearest first, around where the party will be in 2 s.

`WorldCanvas` is a `CanvasLayer` under the UI, because a block's Z sorts across its whole layer. It opens behind a fade: the plan and its baked colours off the main thread, then a texture a frame, then the blocks in view. Only then does the fade lift, and the rest go at 4 ms a frame, while the party is already moving. A block seen before its turn logs `WORLD late block`. Colours are baked at each light's mean, and the probes are then dropped, so floors don't flicker; flicker stays on halos and units, and halos are limited to the view. Water and flames go on moving in blocks recorded once because `FrameTextures` rewrites a single-frame texture in place, with a second one for what glows.

Over the blocks, a `BoardCanvas` (`SetDrawn`) paints each frame's units with what stands in front of them (`WorldPlan.Overlay`), the same pixels in the same baked light. That's what sorts after a unit and crosses it, and then, because an item is repainted whole, what sorts after a repainted item and crosses that, until nothing more does. Without those, a tile repainted for one corner would go over its neighbour in front. Items cross by the shapes they cover: on open ground every tile's box overlaps the next one's, so going by boxes would run away across the map, where diamonds only meet. It comes to a mean of 1.0 items a frame and at most 23 on a walk across Ashen Ford with three walkers, and 8.5 and 55 on `hilly`. `OverlayPictureTests` rasters such walks, blocks then overlay, against true painter's order: no pixel differs. Painting something twice would show if it were half see-through, but no lip or prop in the pack has a partly clear pixel. A unit's marks stand above everything, so a bar or the crown stays visible over a figure hidden by a cliff.

The pack's tops are square and lie turned 45 degrees on a tile, so a checker dither's diagonals run straight down the screen, and at scale 2 every patch of dither was a comb of vertical stripes. `DitherTexels` keeps a top's dither a checker: the TV gets the top at twice its size, which samples exactly as it did at any scale, and only texels that are part of a checker get a pattern that lands upright, in the same two colours. No filter or UV can do this, because outlines must stay where the turn puts them. A top with no dither (cobble, planks, water) is drawn as it is. See [the proof](superpowers/reports/2026-10-31-tactics-overworld/stripes-art-before-after.png): the art, then before, then after. A tall side clamps as the manifest says, so a drop deeper than `chasm_side` shows its art at its own size and goes on as its last row. Doubling costs four times the memory, so it's for tops with real dither: a frame needs `LeastPatch` (8) texels of dither touching, and the art's real dither comes in patches of 80 and up, while specks in sand, flagstone and dungeon floors, and the crypt's runes, are 7 at most.

Measured with `--world-demo` (2026-10-31, one run each, RTX 5080, 1920x1080, the usual Debug build, capped at the project's 144 fps, so 6.94 ms is the floor, with the owner's other programs running), on a full walk at twice walking pace that starts as the fade lifts. `_Draw` is a first recording, while the JIT is cold.

| | Ashen Ford (170 tiles) | `hilly` (120 tiles, 3840 props, 960 lights) |
|---|---|---|
| Items, lit | 30,434 | 85,563 |
| Off-thread build, with the bake | 449 ms | 658 ms |
| Textures, behind the fade: total, worst | 50, 10.2 ms | 24, 6.2 ms |
| Binding, behind the fade | 6 ms | 14 ms |
| Blocks still to record at reveal | 49 of 80 | 57 of 80 |
| `_Draw` per block, worst, median | 1.04, 0.25 ms | 2.37, 0.80 ms |
| Recording in a frame, worst, over how many frames | 4.23 ms, 4 | 4.77 ms, 12 |
| Frame during recording, worst | 12.34 ms | 7.14 ms |
| Frame after the first second, worst | 6.94 ms | 6.94 ms |
| Frames over 16 ms | 0 of 3,650 | 0 of 2,571 |
| Overlay a frame: query, draw, p99 | 0.03, 0.07 ms | 0.05, 0.09 ms |
| Render CPU, median, worst | 0.96, 2.33 ms | 1.80, 2.61 ms |
| Draw calls in view, at most | 2,936 | 3,839 |
| Added memory: managed, engine | 24, 11 MB | 63, 19 MB |

The overlay query's worst is about 6.5 ms, once, on its first call behind the fade, as the JIT compiles it.

So recording costs about 0.6 µs an item, and the cost that's left in a frame is the draw calls, as texture changes break the batches: a battle's board has about 500. No modest TV box has been measured.

A build that fails, on a bad or half-downloaded pack, logs its error once. The scene then frees the world and shows the header with the region's name, and fights still work.

## Walking

In Exploration the TV shows the world instead of the header and table, and the shell's background is hidden, as the world's layer is under it. The first view of Exploration opens the world behind its fade. It's built once for a scene: joining a party that's already walking, or a reconnect, only feeds it views, and a TV that comes back in a new scene builds its own while the old one's cancelled build goes nowhere. A fight or the battle list suspends it (`WorldCanvas.Suspend`): hidden and not running, with everything kept, and with nothing to do but fade back in. Its canvas item is hidden in the rendering server, not on the node, because Godot redraws a node each time it's shown: hiding the node recorded all 80 blocks again on the way back. It's freed when the scene is, with its textures disposed at once rather than left to the collector. Four sessions in a row end at the same memory: textures 36.7 MB, engine 80.8 MB.

`Render` only feeds `WalkShow`, 3 to 5 times a second, and touches no node that hasn't changed. Everything that moves runs in `TacticsScene.WalkOn` each frame, whether a view came or not: `WalkShow.Frame` advances `WalkTrack`, reads the places, follows with `WorldCamera` (`Snap` when `Cuts` changes), and builds the units. Nothing is sent back, so a slow or missing TV costs a late picture and nothing else. A connection lost mid-walk heals by itself: what's missed is filled in from `Legs` or skipped along the path, and a gap past `Legs` snaps.

**The party** stays on the screen's pixels. The camera rounds its origin, and a moving figure rounded on its own would shake against the frame by a pixel at the walk's beat, as often as 117 times in 288 frames. So what's moving lands where the unrounded camera puts it (`WorldCamera.OnPixels`): it holds still on the screen and the ground goes by under it in whole pixels. What's standing locks to the ground (`OnGround`), so it doesn't creep as the camera settles. The party keeps within `PartyReach` of the view's middle, which outranks `EdgeMargin`: a tile is 128 pixels wide, so at 16 tiles from an edge the party would otherwise walk off the screen. A walker whose phone has gone (`Here` false) is drawn a little dim. The leader's crown is the `crown` row of `emotes/manifest.json`, in `UnitMarks`, so it shows over a cliff.

**The panels** (`WorldHud`, worded by `WalkHud`) are the chapter and region at the top left and the leader at the top right, in the battle HUD's glass and gold. They go see-through over the party, as the battle's do. The chapter comes in the host view's `ExploreView.Chapter`, which phones don't get.

**The sound.** Footsteps go by distance, a foot every `FootstepTiles` of the leader's walk, by the ground underfoot (`SoundBank.Step`), at `FootstepDb` under a battle's. What's skipped or snapped past makes no sound. The Ashen Ford plays `town_emberwick`, as the manifest gives it.

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
