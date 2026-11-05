# OW-04 · current-main verification

The request was to merge the bridge fix, then do OW-04. Fetching current `main` revealed that **both fixes were already merged** in [PR #91](https://github.com/higharc91/ArcBox/pull/91) (`e114bdc5`). Bridge source `799e8f70` samples full frames and also sorts low bridge pieces over their actual tiles; `4d73328c` already replaces the stale timed walking route with navigation through `route.cs` and includes parser/navigation/metric regressions. The older `ow-08-bridge-clipping` branch also carries unmerged issue #81 work. No duplicate bridge change or issue #81 content was merged into main.

A clean verification branch, `ow-04-map-aware-walk`, starts at current main **`b5a552ec034fe57db0207249ae732860f6f4e1d7`**, including the newer roaming-mob changes. No game code, harness, rules, art or scene changed. No new implementation was needed, and OW-04 remains done.

## Fresh results · 2026-11-05

- [Full solution build](solution-build.txt): zero warnings/errors.
- [Existing walking-harness regressions](bot-regressions.txt): all six pass, zero failures/skips. These cover fresh-party snaps, LF/CRLF markers, route-token dispatch without opening shops, pause preservation, and displacement based on the initial snap plus actual rendered motion.
- [Default live walking test](default-walk.txt): **passed**, exit 0. Four real hub bot clients and Godot TV; no custom route or lowered test limits.
- 19 steps before server restart, 58 total; the leader visibly moved 5 tiles from spawn.
- Server restart restored tile [27,63] to [27.5,63.5]; the next step was adjacent at [26,62].
- Cold TV reconnect restored [24,56] to [24.5,56.5], and walking continued.
- All **20/20 timed presses** reached the TV: median **36 ms**, p90 53 ms, worst 58 ms; the unchanged limit is 100 ms median and at least 90% delivery.
- Phone disconnect: last step reached the TV 497 ms after the cut, stopped state 712 ms after it; within the unchanged 750 ms lease plus 250 ms slack. Leader change and disconnect events were observed.
- Walking/stopped screenshots and music were present; no TV errors. Expected input-cancellation/WebSocket messages occur when deliberately killing connections.

[TV log](tv.txt), [phone log](phones.txt), [walking after cold reconnect](walking-after-reconnect.png), [stopped party](stopped.png). The live screenshots were inspected. Software OpenGL/Xvfb at 1280×720 validates this harness and its latency limits; it is not a real-GPU frame-pacing check. The unrelated .NET and phone suites were not repeated for this documentation-only recheck. All processes started by the test were stopped.

## Reproduction

From the repository root with .NET 10 and Godot 4.7.2 .NET:

```sh
dotnet build ArcBox.slnx --no-restore
node --test tools/tactics-playtest/watch.test.cjs tools/tactics-playtest/walk-metrics.test.cjs
ARCBOX_GAME=tactics ARCBOX_WALK=1 bash tools/tv-e2e.sh --skip-build
```

The cloud run selected its Godot .NET binary, OpenGL compatibility, Dummy audio, Xvfb and a fresh output directory through environment settings. `ARCBOX_WALK_SCRIPT`, timing limits, press count and coverage limits were left at their defaults. Main's default script navigates to `af_market_herbalist`, `af_inn`, and back using the current map, with the server-restart and TV-reconnect pauses intact.

No PR was created: the requested fixes are already on main. This follow-up branch records verification only; issue #81 remains separate.
