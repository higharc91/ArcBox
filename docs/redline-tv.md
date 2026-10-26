# REDLINE on the TV

How the REDLINE scene renders on the TV PC, the keys and settings that control it, what performance to expect, and
how to get screenshots of a whole race without a GPU. The shell (connect, lobby, sessions) is in
[`tv-host.md`](tv-host.md); the manual pass for a real game night is [`playtest-checklist.md`](playtest-checklist.md).

## Render presets

Three presets, all on the Forward+ renderer (the Mobile renderer is a project setting that needs a restart, so Low
switches off the costly features instead). `project.godot` holds the Ultra baseline (Forward+, MSAA 4×, SMAA,
8192 directional shadow atlas with soft-high filtering, half-resolution GI, `max_fps=144`, VSync off, `canvas_items`
stretch on a 1920×1080 base) and `World/RenderPreset.cs` applies the chosen preset when the world is built and again
whenever it changes.

| | Ultra (default) | High | Low |
|---|---|---|---|
| Global illumination | SDFGI, half-res probes, 6 cascades | – | – |
| Screen-space reflections | on | – | – |
| SSAO + SSIL | on | on | – |
| Volumetric haze | on (thin) | on | – |
| Sun shadows | 4 splits, 8192 atlas, soft high | same | 2 splits, 2048 atlas, soft low |
| Anti-aliasing | MSAA 4× + SMAA, debanding | same | FXAA |
| Anisotropic filtering | 16× | 16× | 4× |
| Trees | 1,500 | 1,500 | 750 |
| Frame cap | 144, VSync off | same | same |

Exponential fog with aerial perspective, glow and AgX tonemapping are on in every preset.

**Which preset runs:** `--preset=ultra|high|low` on the command line, else the one saved in `user://settings.json`
(`"preset"`, written by F2), else Ultra. On Windows the file is `%APPDATA%\Godot\app_userdata\ArcBox\settings.json`, on
Linux `~/.local/share/godot/app_userdata/ArcBox/settings.json`; it also keeps the server address from the Connect
screen.

## Keys

| Key | Where | Does |
|---|---|---|
| **F2** | anywhere | Next preset, Ultra → High → Low → Ultra. Applies at once to the race on screen (or the scene warming up behind the lobby), shows "PRESET · HIGH" for two seconds, and saves the choice. |
| **F3** | anywhere | Performance overlay, top-left: fps and average frame time with the worst frame over the last quarter second, draw calls and primitives in the last frame, the preset and the window size. Press again to hide. |
| ← / → / Enter | lobby | Pick a game / start it. |
| Esc | in a game | End the game for everyone (host override). |

`--perf` starts with the F3 overlay on and also logs it every 5 s (`PERF 144 fps 6.9 ms (worst 8.1) | …`), which is
the easiest way to record numbers from a run.

## Performance

The target (REDLINE spec §1, criterion 6) is **144 fps at 2560×1440 on Ultra on the TV PC's RTX 5080** with all 22
cars. That can only be measured on that machine: this repository's sandbox has no GPU and renders with Mesa's CPU
rasteriser (llvmpipe), so its frame rates say nothing about the real thing. To measure:

1. Start a race (two phones, or the dev page) with the TV fullscreen at 2560×1440 and press **F3**.
2. Note fps and ms on Ultra for the grid, a helicopter shot of a pack in Reveal, the trackside follow-up and the
   Chequered podium; then F2 to High and Low and repeat. Fill in the table below.

| Preset @ 2560×1440, RTX 5080 | Grid | Reveal (heli) | Trackside | Podium |
|---|---|---|---|---|
| Ultra | _to measure_ | | | |
| High | | | | |
| Low | | | | |

If Ultra falls short, the usual suspects in order of cost are SDFGI, SSR, volumetric fog and the 8192 shadow atlas;
High drops the first two.

What the sandbox *can* tell: the scene's load. With the Low preset at 1280×720 the F3 counters read 300–1,000 draw
calls and 2.5–5.6 M primitives depending on the shot (the grid, looking down the main straight at the grandstand
and all 22 cars, is the heaviest). On llvmpipe a 1280×720 Low frame takes 0.6–1.3 s and a 1080p Ultra frame several
seconds.

**Getting ready in the lobby.** When REDLINE is selected in the lobby, the TV builds the world (on Ultra about 1 s of
main-thread work after the background geometry build) and draws it for a few frames behind the lobby, with each
phase's HUD panels drawn underneath the lobby too, then stops drawing it. That moves the world build and the
first-frame shader and pipeline compiles out of lights out: on the RTX 5080 that's a short hitch in the lobby instead
of at the start; in the sandbox it is the difference between a 25 s freeze at the start and about 4 s. After the
Chequered flag the TV keeps the race's scene (hidden) while REDLINE stays selected, so a rematch skips all of that
too: building a fresh scene would recompile every shader, because Godot frees a material's shader with the last
material that uses it.

**Wall-clock scene time.** Godot normally caps a frame's process delta at 8 physics ticks (133 ms). The TV raises
that cap, because its animations, the Reveal follow-up cut and the phase screenshots must keep pace with the
server's wall-clock phase timers even when a frame is slow.

## Screenshots

`--shots=<dir>` saves `<seq>-<label>.png` on every screen change and lobby change, and in REDLINE about a second into
every phase (`grid`, `throttle`, `question`, `reveal`, `chequered`…), on each Driver Select claim, at 2, 4 and 6 s into
Reveal, and on the Chequered podium. Each shot is taken two frames after it is asked for, so at very low frame rates
a shot near the end of a phase can show the start of the next one.

Three ways to get them:

- **Demo, no server:** `--redline-demo[=step]` plays a scripted race (see [`tv-host.md`](tv-host.md#redline-scene)).
  With Godot's `--fixed-fps` the result is deterministic however slow the renderer.
- **The real thing, scripted:** `tools/tv-e2e.sh` (below).
- **By hand:** run the TV with `--shots`, and play from phones or the dev page.

## `tools/tv-e2e.sh`: TV, server and phones together

```sh
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tools/tv-e2e.sh            # to the first Reveal
ARCBOX_FULL=1 GODOT=… tools/tv-e2e.sh                                           # a whole race and back to the lobby
```

It builds the phone app, the server and the TV host (skip with `--skip-build`), starts the server in Development (the
built phone app, bundled questions) on a free port, starts the TV on its own Xvfb display with software Vulkan:

```
--new-room --shots=<out>/tv --preset=low --res=1280x720 --server=<url> --autostart=redline:3
```

waits for the TV's `ROOM <CODE>` line, then runs `web/play/e2e/redline.e2e.mjs` with three bot phones in that room
(`ARCBOX_ROOM`, `ARCBOX_START=host`). The TV selects REDLINE at once, gets the scene ready behind the lobby, and starts
the race itself when all three are in (`--autostart=<game>:<players>`, a dev argument). When the phones are done it
gives the TV a few seconds for its last shots, stops the TV, Xvfb and the server, and fails if the phones reported a
problem or the TV log has an `error` or `exception` line other than the expected audio ones (there is no sound card).

Everything lands in one folder (`ARCBOX_E2E_OUT`, default `/tmp/arcbox-tv-e2e-<time>`): `tv/*.png`, `phones/*.png`
(phone 1 at each phase, plus phone 2 after its mid-game reload), `tv.log`, `phones.log`, `server.log`, `build.log`.

| Variable | Default | |
|---|---|---|
| `GODOT` | `godot` on PATH | Godot 4.7.2 .NET binary |
| `ARCBOX_FULL` | – | `1`: whole race, results, back to the lobby |
| `ARCBOX_PHONES` | 3 | bot phones |
| `ARCBOX_PACE` | 4 | bots' reading-time multiplier: 1–9 s per decision, so each phase stays on the TV long enough to be captured |
| `TV_RES`, `TV_PRESET` | 1280x720, low | anything bigger or prettier can't keep up with real-time phases in software |
| `TV_LINGER` | 12 | seconds the TV runs after the phones finish |
| `TV_ARGS` | – | extra TV arguments, e.g. `--perf` |

Linux only. Needs the .NET 10 SDK, Node 22 with Playwright's Chromium, `Xvfb`, Mesa's Vulkan driver
(`mesa-vulkan-drivers`) and `curl`. The TV always renders on the Xvfb display, so in software even on a machine
with a GPU: the script checks that everything works together, not how fast it runs.
