# Milestone E — TV surfaces and reaction animation

User-facing date: **2026-11-03 (UTC)**. The user authorized E and extra time for animation polish. Work remains on `environmental-tiles`.

Rendering/tests: **`89d94e78`**. Reproducible TV demo: **`740aa1e7`**.

## Result

Every `TileReacted` rule now has a TV beat using the existing pack. Burning ground, ice sheets and oil use their hazard art;
spent ground draws `scorch/burnt` as a decal at the tile's current height. Ice overlays the existing ground rather than replacing
the water material. Ordinary spell zones retain their existing art and pacing.

| Reaction | Picture and sound | Timing |
|---|---|---|
| Conduct | `electrified` ground and over layers; `shock_storm` on hit figures; `spell_lightning`, existing lightning impact sounds | Event/BFS order, 40 ms per tile; compress very large fronts into 240 ms, with a 600 ms electrical tail |
| Ignite / spread / burn web | `flames_fire`, then persistent burning art; `spell_fire_aura` | Front capped at 100 ms; longest flame finishes by 958 ms |
| Freeze | `ice_frost` plus a delayed `dust_snow` puff; `spell_ice` | Frost settles over the ice sheet; minimum 600 ms hold |
| Thaw | `misty_mist`, removing the ice; `smoke_poof` | Manifest clip length plus a front of at most 100 ms |
| Douse | `poof_white`, removing the fire/coating; `smoke_poof` | Manifest clip length plus a front of at most 100 ms |

The front waits once for its whole reaction. Each conducted victim's save, damage and shock arrive with that tile instead of
adding a separate pause per target. Damage numbers and save badges may finish fading after the beat, as ordinary combat does.
Round-start spread finishes before the first turn begins. Zone changes occur during the arriving effect; replacement zones
and consumed web/oil tiles use the existing `DisplayedBattle.Apply` path.

Transient electrical ground sorts below figures and its rising arcs above them, using the same painter order as the board.
It respects tile height and foreground scenery, fades over its last 180 ms, and never becomes a saved zone. In staged overworld
fights its depth keys shift by the stamp origin, as unit and scenery keys do; its art stays in battle pixels.

The TV pass caught excessive additive overlap when several timber tiles ignited together. Ignition bursts now use **80% size
and 70% alpha**, scaled about the manifest anchor, while persistent flames keep their original size. Scorch uses **70% alpha** so burned boards retain their grain. This preserves the figures
and the board beneath the burst. These are presentation settings, not gameplay or balance changes.

## Reproducible motion check

Run the normal TV host without a server:

```sh
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64
"$GODOT" --path src/ArcBox.Host --audio-driver Dummy -- \
  --tactics-demo=environment --preset=low --res=1280x720 \
  --shots=/tmp/arcbox-environment --beat-log --audio-log
```

`EnvironmentDemo` feeds ordinary host views and beats through `TacticsScene`, `BeatLoop`, `BeatPlayer`, `BoardPlan` and
`BoardCanvas`. It is a scripted rendering fixture, not an enabled production encounter. The exposed stone ford of Warlord Bridge
makes the conduction/ice layers visible without River Crossing's foreground bridge prop hiding them. It then shows the timber
bridge igniting, spreading, being doused and retaining scorch, followed by oil ignition and burning webs.

[Full animation reel](e-reactions.mp4) · [Lightning](e-conduct.png) · [Frozen ford](e-freeze.png) · [Settled ice](e-freeze-settled.png) · [Burning bridge](e-fire.png) · [Scorched boards](e-scorch.png)

Motion was inspected at successive frames through arrival, peak and disappearance, including the ignition readability refinement.
The recording uses the actual Godot 4.7.2 Mono TV renderer, Xvfb, Mesa llvmpipe and the Compatibility renderer. Its exported movie
uses a fixed 30 fps clock; encoding time is not a real-time performance measurement. A separate non-movie run checks actual beat
completion and acknowledgement. No browser reconstruction or generated replacement art is used.

## Validation

- Full Release solution: **5,851 passed, 0 failed, 0 skipped**; **478 web tests** passed. Final host build: **0 warnings**, **0 errors**.
- Focused presentation checks: **192 passed**, including **16 new cases**.
- Final idle TV playback: **8 reaction beats**, **0.634–0.933 seconds** each, exit **0**; all **10** scripted beats completed.
- The loaded TV run overlapped full tests and media encoding; its slowest reaction took **1.085 seconds** wall time. All scripted reaction durations remain below one second. No application errors occurred.

[Validation and source/golden fingerprints](e-validation.json) · [Solution log](e-tests.txt) · [Web log](e-web.txt) · [Host build](e-host-build.txt) · [Live TV](e-tv-realtime.txt) · [Loaded TV](e-tv-loaded.txt) · [Movie capture](e-tv-movie.txt).

The software renderer reports unsupported V-Sync/screen-space AA at startup. Godot reports **8 ObjectDB instances at shutdown**;
the existing [board-only demo](e-tv-board-only.txt), without any reaction cues, reproduces the same warning. This is recorded,
not counted as a clean shutdown with no warnings. Web stderr contains existing happy-dom local-fetch/abort and intentional
error-boundary diagnostics (also present in D), while all web tests pass.

Tests cover all six burst rules, conduct ordering and tile-aligned saves/damage, 60 simultaneous lethal victims, round-start
pacing and deduplicated sound, height/depth/fade/cleanup of electrical layers, world-origin ordering, fallback completion for
an unknown rule, reconnect reset, anchored ignition scale/alpha, surface-art aliases and a raised scorched decal. Original area
attack and ordinary zone tests remain in the focused suite. The ignition readability test first failed with full-alpha bursts;
the new cue and world-order tests first failed to compile before their corresponding API was introduced.

The six existing golden replay/event files remain byte-identical. No gameplay code, encounter opt-ins, save fields, phone
contracts or generated art/audio were changed. F (phone risk/spread rendering) and G (encounter opt-ins and balance runs) remain.
The separate oil price/economy dependency from D remains deferred. No PR has been opened.
