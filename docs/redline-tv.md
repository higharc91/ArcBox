# REDLINE on the TV

How the REDLINE scene renders and sounds on the TV PC, the keys and settings that control it, what performance to
expect, and how to get screenshots of a whole race without a GPU. The shell (connect, lobby, sessions) is in
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
| Surroundings: parked cars, shrubs, groves, distant clumps | 990 cars, 1,300 shrubs, 750 groves, 3,600 clumps | same | half of each, no shadows |
| Trackside: barrier blocks, armco and fence posts | full models within 140 m, LOD beyond | same | LOD everywhere, every other post |
| Big screen at the main grandstand | live broadcast feed (640×360, every 3rd frame, only while in shot) | same | static REDLINE card |
| Cars ([Blender model](blender-models.md)) | LOD0 (~68k tris) within 60 m, LOD1 (~16k) beyond | same | LOD1 everywhere |
| Frame cap | 144, VSync off | same | same |

Exponential fog with aerial perspective, glow and AgX tonemapping are on in every preset.

The surroundings (`World/Surroundings.cs`, placed by `Redline.Presentation.Course.Surroundings`) are the same in every
preset apart from that row: the dry-grass terrain material, the far land out to 8.5 km, service roads, car parks,
hospitality compounds, the Grand Plaza and amphitheatre by the Observation Tower, treelines and woods out to the
horizon and the Austin skyline. They draw about 0.74 M triangles in all on Ultra and High (about 0.42 M on Low): the
props instanced, one MultiMesh per variant, about 50 draw calls before shadow passes. Planning and building them
takes 1–2 s of main-thread work in the sandbox when the world is first built (cached for the next race).
`REDLINE_NO_SURROUNDINGS=1` leaves them out (comparison shots).

The barriers and trackside furniture (`World/Trackside.cs`, placed by `Redline.Presentation.Course.Trackside`) follow
the corner data: TecPro blocks (red, white and blue) or tyre walls with a conveyor-belt face on the corner outsides,
double armco rails on posts along the back straight and the armco corners, and the concrete wall with its ad boards
elsewhere; a catch fence with posts, cables and a chain-link mesh that cranks over at corner outsides; 18 numbered
marshal posts, corner boards T1–T20, brake boards 300/200/100 before the medium and heavy braking corners, four TV
camera platforms and the big screen at the end of the main grandstand. The props are MultiMeshes in 100 m cells
(culled per cell): about 0.57 M triangles if every prop drew its full model (the test's budget is 0.7 M), much less
with the LOD; about 0.18 M on Low. In the sandbox's F3 counters (demo, 1920×1080) the trackside adds about 130 draw
calls and 0.06 M primitives at the grid on High and Ultra (2,060 against 1,929; 2.88 against 2.82 M) and 85 calls and
0.26 M on the next shot; on Low about 95 calls and 0.05 M at the grid, 55 calls and 0.17 M on the next shot. Planning
takes about 0.2 s. `REDLINE_NO_TRACKSIDE=1` leaves them out (comparison shots).

With the whole COTA upgrade in (track surface, kerbs and run-off, surroundings, trackside), the full `tools/tv-e2e.sh`
race (`ARCBOX_FULL=1`, Low, 1280×720, software Vulkan) passes: the TV opens its room in about 3 s and the scene is ready
for Driver Select about 65 s later, the 15 rounds and the podium take about 9 minutes, and the busiest frame draws
about 1,420 calls and 2.0 M primitives (`TV_ARGS=--perf`). Sandbox frame times say nothing about the RTX 5080; check
144 fps at 1440p with F3 on the real machine.

**Which preset runs:** `--preset=ultra|high|low` on the command line, else the one saved in `user://settings.json`
(`"preset"`, written by F2), else Ultra. On Windows the file is `%APPDATA%\Godot\app_userdata\ArcBox\settings.json`, on
Linux `~/.local/share/godot/app_userdata/ArcBox/settings.json`; it also keeps the server address from the Connect
screen.

## Keys

| Key | Where | Does |
|---|---|---|
| **F2** | anywhere | Next preset, Ultra → High → Low → Ultra. Applies at once to the race on screen (or the scene warming up behind the lobby), shows "PRESET · HIGH" for two seconds, and saves the choice. |
| **F3** | anywhere | Performance overlay, top-left: fps and average frame time with the worst frame over the last quarter second, draw calls and primitives in the last frame, the preset and the window size, and the narrator's voice (`VOICE GOOGLE`, `FALLBACK` or `OFF`, `MUTED`) with the last line it spoke. Press again to hide. |
| **M** | anywhere (not while typing the server address) | Mute or unmute all sound (music, effects and voice), shows "SOUND OFF" / "SOUND ON" and saves the choice. |
| ← / → / Enter | lobby | Pick a game / start it. |
| Esc | in a game | End the game for everyone (host override). |

`--perf` starts with the F3 overlay on and also logs it every 5 s (`PERF 144 fps 6.9 ms (worst 8.1) | …`), which is
the easiest way to record numbers from a run.

## Sound

REDLINE is narrated, commentated and scored (audio spec, `docs/superpowers/specs/2026-10-26-redline-audio-design.md`).
Everything plays on three buses under Master, made in code at start-up: **Music**, **Sfx** and **Voice**.

- **The question** is read out when the Reading phase starts (the HUD shows the question and its answers with
  **LISTEN** in place of the clock, and the phones show the question with "Answers open in a moment…"); the answer
  clock starts when Reading ends. With `ReadQuestions` off, it is read when the Question phase starts instead.
- **The commentator** calls the race: the welcome on the grid, "And it's lights out!", each round's category, the
  answer and the round's biggest player moment in Reveal, and the winner. It never talks over the question: a line
  that comes up while the question is being read waits, and is dropped if it has waited 4 s.
- **Effects:** a beep per start light (in step with the gantry), the lights-out launch, a click when a player locks a
  throttle and a tick when one answers, countdown ticks for the last 5 s of Throttle and Question, a whoosh as the
  question appears, the correct sting or wrong buzz in Reveal, tyres and a crunch for a spin-out on camera, a cheer for
  a new leader, and the fanfare and crowd at the flag. A low crowd murmur runs under the race.
- **Engines:** a synthesised V6 on each car the camera is on (two or three), revving with the start lights, a low
  burble while players think, and a roar during the Reveal drive that follows each car's speed and is louder the
  bigger the round's best gain.
- **Music:** a lobby loop while REDLINE waits in the lobby and through Driver Select, then a low race bed to the flag.
  All effects, engines and music are synthesised from code; there are no sound files (see `games/redline/THIRD_PARTY.md`).
- **The podium** (podium spec, `docs/superpowers/specs/2026-10-28-redline-podium-design.md`): the anthem and the
  victory theme are synthesised in the background when the flag falls (about a second of CPU each). In the Anthem the
  commentator names it ("The national anthem of Great Britain, for Jo in the McLaren!") and the brass band follows the
  line; if they would overlap the band starts on its timpani roll under the end of the line (ducked), so the anthem is
  never cut short, and its last chord may ring a second into the Champagne. A country whose anthem has no melody yet
  (Monaco, Argentina, New Zealand) or never will (Brazil, Mexico, Thailand) gets the REDLINE victory theme instead,
  still captioned with the country. The Champagne gets the theme (looping) and a cheer; every spray pops a cork and
  hisses at its strength, and the first one brings the victory sting.
- **Ducking:** while a voice speaks, Music dips 12 dB and Sfx 6 dB (200 ms down, 600 ms back up), the built-in voice
  included.

### Voices: Google, fallback, off

The narrator asks the server's `/api/tts` for each line (the questions in the `redline.questions` voice, the
commentator in `redline.announcer`); with a Google key on the server these are **Chirp 3: HD** neural voices
([`speech-setup.md`](speech-setup.md) sets that up). If the server has no key, Google fails, or a clip takes longer than
2.5 s, the line is spoken at once by the **TV's built-in voice** (Windows' own voices; on Linux, speech-dispatcher)
with each theme's fallback rate and pitch, and the server is not asked again for 60 s. The game never waits for audio:
the Reading phase runs on its own timer either way. With no built-in voices either, the narrator is **off** and lines
are only logged. F3 shows which (`VOICE GOOGLE`, `FALLBACK`, `OFF`); the log says why (`NARRATOR server voice
unavailable (Unavailable 503); built-in voice for 60 s`).

### Volumes

Volumes live in `user://settings.json` next to the preset, linear from 0 to 1 (missing keys use the defaults):

```json
{
  "masterVolume": 1,
  "musicVolume": 0.55,
  "sfxVolume": 0.85,
  "voiceVolume": 1,
  "muted": false,
  "narrationVoice": "Zira"
}
```

`muted` is what **M** toggles. `narrationVoice` picks the built-in fallback voice by part of its name or id (for
example `Zira` or `David` on Windows); without it, the first English voice is used. Edit the file with the TV app
closed; it is read at start-up.

### Audio log

`--audio-log` prints a line for every sound the TV plays, so a run without a sound card still shows what would have
been heard and when:

```
AUDIO Sfx start-beep #1          … #5
AUDIO Sfx lights-out
AUDIO Voice redline.announcer: And it's lights out!
AUDIO Sfx whoosh
AUDIO Voice redline.questions: Which U.S. state has the longest coastline?
AUDIO Sfx correct
AUDIO Voice redline.announcer: The answer is Alaska.
AUDIO Sfx screech VER
AUDIO Voice redline.announcer: Sam spins the Red Bull!
AUDIO Sfx crunch
AUDIO Sfx fanfare
```

plus `AUDIO Music lobby-loop|race-bed`, `AUDIO Sfx engine <car>` when an engine voice moves to a new car, and
`AUDIO Sfx countdown <n>`. Narration is logged when it starts, whichever voice speaks it (or none). The podium adds:

```
AUDIO Music anthem gb                  (or AUDIO Music victory-theme for a theme-only country)
AUDIO Sfx cheer
AUDIO Voice redline.announcer: Let the champagne fly!
AUDIO Music victory-theme
AUDIO Sfx victory-sting                (the first spray only)
AUDIO Sfx cork-pop step 1
AUDIO Sfx spray-hiss step 1 1.00       (step, strength)
```

and the scene logs each spray it launches: `REDLINE spray #<seq> step <step> strength <s>`. Qualifying adds
`AUDIO Sfx purple` after a Result's sting when a player set the lap's fastest time.

## Qualifying

Between Driver Select and the Grid, three easy questions set the starting order (qualifying spec,
`docs/superpowers/specs/2026-10-29-redline-qualifying-design.md`): each is a lap, and the field lines up by correct
laps, then the total time of the correct ones. It is on by default (`RedlineRules.Qualify`) and skipped in a room
with no players.

- **Flow, per lap:** QualifyingReading (the question read out, **LISTEN** in place of the clock), QualifyingQuestion
  (10 s, ends early when every player has answered) and QualifyingResult (5 s). After the third Result, the Grid with
  the cars in their new slots. The header reads "QUALIFYING · LAP n/3" with the category and the difficulty; the
  answers A–D sit along the bottom and the correct one turns green at the Result. The track map is off (every car is
  on the grid); the camera holds the provisional pole car trackside during the question and from the helicopter at the
  Result.
- **Tower:** the timing board, POS · DRIVER · LAP · TOTAL, in classification order. TOTAL is the sum of the correct
  laps so far ("6.82", "—" before a first correct lap), with the count of correct laps in small grey after a deleted
  one ("5.90 · 2/3"), since correct laps count before time. During the question a player row shows a tick once they have
  answered (pulsing dots until then). At the Result the LAP column shows this lap: the time on green, the session's
  fastest on **purple** (`#a020f0`, white text), or a red-outlined **DELETED** for a wrong or missing answer, at full
  strength on AI rows too; the rows then slide to the new order with ▲▼ movement. P1's box turns white once it has a time.
- **Cards:** at a Result where P1 changed, a **PROVISIONAL POLE** lower-third with the new leader's name and team bar
  slides in for 3 s; after the last lap, **POLE POSITION** with the name, a wide team-colour bar and the total
  ("6.82s" on white) stays up for the rest of the Result. Both sit in the Reveal lower-third's place, right of the
  tower and above the answers. Names are as the player typed them (upper-cased), or the driver's surname for an AI.
- **Sound:** the question is read through `redline.questions` as in the race; the commentator opens with
  "Qualifying! Three questions, fastest correct answers take pole.", then calls "{Name} goes purple!", "Provisional
  pole for {Name}." and finally "Pole position for {Name}, {total} seconds in total!". Each Result plays the correct
  sting when any player's lap counted (else the wrong buzz), then a bright two-note **purple** chime when a player has
  the lap's fastest time; answer ticks and the last-5-seconds countdown run as in the race.
- **Log:** when the Grid starts the scene prints the starting order once per race, `REDLINE grid NOR PIA LEC …`
  (scripted checks read it).

`--redline-demo=qualifying` plays the scripted session: PIA takes provisional pole with lap 1's purple, JO takes it
in lap 2 with another purple, ALEX goes purple in lap 3, and JO ends on pole; CHRISTABELLA's first lap and several AI
laps are deleted.

## The podium

After the Chequered flag the race ends on an F1-style podium (podium spec §5) on the pit side of the start straight,
150 m before the line: a stage with three stepped blocks (2 left, 1 centre and highest, 3 right) carrying their
numerals, a REDLINE step-and-repeat backdrop framed by light towers and LED strips, a lighting truss, and the three
podium players' cars parked nose-in in front (every other car is off air). A driver in team colours stands on each
step holding a lathed champagne bottle with team-colour foil, shaking it harder as the player's charge rises.

- **Anthem (16 s):** a slow push-in from the whole set to the three drivers; labels over the steps (place, name, car
  number, code and team, and the bottle's pressure gauge), and the "NATIONAL ANTHEM · {Country}" lower-third with
  "for {Name} · {Driver}, {Team}".
- **Champagne (16 s):** a cut to a wider shot that drifts across the front; the "CHAMPAGNE!" banner; confetti cannons
  on the backdrop fire and paper keeps falling; the labels move up to the top of the picture. Each new spray in the
  host view (by `Seq`) pops that step's cork (a cork flies off with a little flash) and throws a golden, sparkling
  jet of foam that arcs and falls, the bottle raised and waved about; size, speed, duration and amount follow the
  spray's strength (`SprayLook`), and foam drips off the bottle afterwards.

`--redline-demo=anthem` (or `=champagne`) plays just the ceremony, with scripted charges and sprays from all three
steps at different strengths, including one triple spray. `--redline-demo-winner=PER` makes KAI in the Cadillac win
instead of JO in the McLaren, so the podium plays the theme only (Mexico).

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

What the sandbox *can* tell: the scene's load, which doesn't depend on the GPU. On Low the F3 counters read 270–1,000
draw calls and 2.5–5.6 M primitives depending on the shot (the grid, looking down the main straight at the
grandstands and all 22 cars, is the heaviest); on High the Driver Select and grid shots read 900–1,300 draw calls and
about 11 M primitives, most of it the 4-split shadow passes. On llvmpipe a 1280×720 Low frame takes 0.55–1.7 s.

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
every phase (`grid`, `throttle`, `question`, `reveal`, `chequered`, `anthem`, `champagne`…; in qualifying with the
lap, `qualifyingreading-lap1`, `qualifyingresult-lap3`), on each Driver Select claim, 2.2 s into each qualifying
Result with its pole card in (`qualifyingresult-lap2-card`), at 2, 4 and 6 s into Reveal, on the Chequered podium,
12 s into the Anthem (`anthem-pushin`), and in the Champagne just after the first spray (`champagne-first-spray`), when all three bottles are spraying at once
(`champagne-triple`) and at 12 s (`champagne-confetti`). Each shot is taken two frames after it is asked for, so at the 1–2 fps of
software rendering a shot shows a moment 1–3 s after its label: `reveal-2s` often already has the follow-up cut to the
leader (due at 3.5 s), and a shot near the end of a short phase can show the start of the next one.

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
--new-room --shots=<out>/tv --preset=low --res=1280x720 --server=<url> --autostart=redline:3 --audio-log
```

waits for the TV's `ROOM <CODE>` line, then runs `web/play/e2e/redline.e2e.mjs` with three bot phones in that room
(`ARCBOX_ROOM`, `ARCBOX_START=host`). The TV selects REDLINE at once, gets the scene ready behind the lobby, and starts
the race itself when all three are in (`--autostart=<game>:<players>`, a dev argument). When the phones are done it
gives the TV a few seconds for its last shots, stops the TV, Xvfb and the server, and fails if the phones reported a
problem or the TV log has an `error` or `exception` line other than the expected audio ones (there is no sound card).

It then checks the TV's audio log for the race's sounds **in order**: the five start beeps, lights out, the
commentator's first line of round 1, the question read out (`AUDIO Voice redline.questions: …`), the Reveal sting or
buzz and "The answer is …" (and with `ARCBOX_FULL=1`, the fanfare and the winner's line), printing each line it
matched and failing on the first one missing. The phone script also fails if phone 1 never showed the Reading view.
With `ARCBOX_FULL=1` the phones also play the podium: on the Shake widget each bot taps "Tap to enable shaking" (desktop
Chromium has the iOS permission API but no motion, so the widget falls back to tapping), taps the bottle to charge it
(phone 1 most, so the sprays differ) and presses POP! through the Champagne; the script fails unless every podium
phone popped a cork. The TV log must then show the Anthem and the Champagne, `AUDIO Music anthem <id>` followed by
`AUDIO Music victory-theme` (or the theme alone for a theme-only country), the victory sting, and a `REDLINE spray`
line and a cork pop for every podium step.
The server runs without a Google key, so `/api/tts` answers 503 and the TV falls back to its built-in voice (or, on
a Linux box without speech-dispatcher, logs the lines with the narrator off); the audio lines are the same.

Everything lands in one folder (`ARCBOX_E2E_OUT`, default `/tmp/arcbox-tv-e2e-<time>`): `tv/*.png`, `phones/*.png`
(phone 1 at each phase, plus phone 2 after its mid-game reload), `tv.log`, `audio.log` (the TV's `AUDIO` lines),
`phones.log`, `server.log`, `build.log`.

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
