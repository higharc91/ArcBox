# Battle crash sweep

This console harness runs the issue #69 milestone A crash sweep through the game adapter: every encounter, 1/2/3/4/6 players, zero or one absent player, pass and timeout play, and 100 deterministic class/seed samples. It follows `docs/superpowers/reports/2026-10-31-tactics-overworld/battle-sweep-harness.cs.txt`, using the current `battleList: true` constructor and campaign membership. The historical `Objectives.ZzNoFix` comparison switch no longer exists.

Run from the repository root:

```sh
SWEEP_SEEDS=100 SWEEP_OUT=/tmp/hazard-sweep.tsv dotnet run -c Release --project tools/tactics-playtest/hazard-sweep
```

`SWEEP_WORKERS` defaults to 4; `SWEEP_ENCOUNTERS` can narrow the smoke run, for example `river_crossing`. The sweep enables rules invariants. Its report groups outcomes and crashes by encounter, player count, connectivity and mode; the `.cases` file preserves each seed and fingerprint. Crashes or battles that fail to start return exit code 1. Battles still running after the original 1,500 synthetic half-second steps are reported as unfinished, separately from crashes. This is a crash/determinism exercise, not the balanced-party win-rate simulator.
