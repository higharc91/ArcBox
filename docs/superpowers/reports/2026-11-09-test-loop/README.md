# #195: daily .NET checks and complete merge checks

Measured on the managed Linux environment with a four-core CPU quota and .NET SDK 10.0.403, on top of main `17dc7f76`. No competing build or test jobs ran during the daily measurement.

`bash tools/test-dotnet.sh` passed **7,308 tests across all 10 assemblies in 95.173 seconds including the incremental Release build**. Test execution took **86.832 seconds**. This is approximately the issue's 90-second daily-loop target; a clean build or slower machine can take longer. [Raw daily output](daily.txt) and [timings](timings.json) retain the exact measurement. The issue's historical 232-second unfiltered run used a different test count and scope, so it is not a same-workload speed comparison.

## Commands and lanes

| Command | Work |
| --- | --- |
| `bash tools/test-dotnet.sh` | Serial Release build of every test dependency, then functional checks in every solution test assembly. |
| `bash tools/test-dotnet.sh --perf` | Whole-solution build, then isolated performance assemblies and collections; original median/P95 output and thresholds. |
| `bash tools/test-dotnet.sh --full` | Whole-solution build and every .NET test, without trait filters. |
| `bash tools/check-all.sh --full` | Required whole-solution build, unfiltered .NET tests, phone tests/production build, Node tools, and Python asset/audio/lore checks. |

The daily lane excludes explicit Perf, Sim and Integration traits. Full battle/planner sweeps, multi-seed and 1,500-event interaction models, complete battle adapter playthroughs, chapter/restart paths, exhaustive export rejection cases and whole-sheet raster/connectivity proofs remain intact in the full lane. The daily lane keeps focused engine/adapter assertions, packet-size guards, content/world-tool behavior, short story/walk/interaction model traces, all 41 approved mob metadata rows, and a deterministic bound on stage-rebuild tile work.

All Stopwatch threshold assertions are outside the daily lane: AI planning, pen-search time, TTS timeout duration and storage shutdown duration are Perf. The existing stage rebuild measurement remains Perf, with an added short work-based bound in daily checks. The invariant-cost test only prints elapsed time and asserts no machine-time threshold. Socket end-to-end polling belongs to Integration.

## Review evidence

- [Assertion conservation](assertion-conservation.json): all **3,604 original Assert calls in 60 touched test files** remain, with nine added calls. The only normalization is the name of the immutable showcase fixture factory. All 41 original exhaustive mob theory rows retain their order and values.
- [Top-20 profiles](profile-top-20.json) identify the expensive work. These diagnostic profiles were taken before final lane assignments and are not isolated benchmarks.
- [Runner probes](runner-probe.json) verify all ten solution test projects enter the Release build filter, test/build failures propagate, temporary filters are removed, full tests have no filter, perf arguments isolate collections, and invalid/empty inputs fail.
- Pristine rules and art manifests load once per assembly, with separate with-art and logic-only variants. Edited packs and negative loader fixtures still load fresh. Immutable real-engine showcase samples are cached by pack instance and class; repeatability checks still call fresh factories. Mutable sprite catalogs stay within their serialized class collection.
- Reviewed scope is test fixtures, lane traits, short functional guards, scripts and documentation. GitHub Actions remains deliberately disabled. Every original full test workload and assertion is retained.

## Isolated performance validation

`bash tools/test-dotnet.sh --perf` passed **11 tests, zero failures/skips, zero build warnings** in 29.360 seconds including its whole-solution build. The seven AI scenarios reported P95 **8.0–65.9 ms**, all below their unchanged 250 ms bounds. [Raw performance output](perf.txt) and [per-scenario measurements](timings.json) include median, P95 and maximum values. Stage rebuild, pen lookup, TTS timeout and storage shutdown checks also passed.

## Complete merge gate

`bash tools/check-all.sh --full` passed **all five groups**: **7,736 .NET, 601 phone, 24 Node, 61 asset, 9 audio and 23 lore tests** (**8,454 total**), with **zero build warnings, failures or skipped tests**. The phone production build, lore validation and documentation drift checks passed. All long Perf/Sim/Integration workloads ran without filters. [Full validation](full-validation.json) and [concise gate output](full-check-summary.txt) record the result.

The .NET total is 50 cases above the pre-change 7,686: two story traces, one walk trace, three single-roll branch cases, one stage-work bound, 41 mob metadata cases and two interaction traces. The original models, seeds, pixel/connectivity assertions, timing limits and complete battle replays still run in full.
