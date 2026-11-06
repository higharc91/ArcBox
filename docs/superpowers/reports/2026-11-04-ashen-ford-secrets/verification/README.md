# Final committed-state verification

Verified content/code commit: `36e4530cbfc3ed8fe2945ef00c86f05942bc5253` on `ashen-ford-secrets`, fetched from the remote branch on 2026-11-05. The supplied Windows worktree is not mounted in the cloud executor; verification used a clean Linux worktree at `/workspace/arcbox-secrets`. No code, content, fixtures, audio, or art was changed during this pass. Follow-up changes are verification records and board/handoff updates only.

| Check | Result | Evidence |
| --- | --- | --- |
| Full .NET solution suite | 6,084 passed, zero failed/skipped; 10m 16s | [Complete test output](dotnet-suite.txt) |
| Full solution build | Zero warnings and errors | [Build output](solution-build.txt) |
| Phone suite | 504 tests in 24 files passed | [Complete test output](web-tests.txt) |
| Chapter 1, force | Passed: won Storming the Ford, all 94 authored lines said once, end card, four walking steps afterward | [Harness output](chapter-force.txt), [TV log](chapter-force-tv.txt), [card](chapter-force-card.png), [walk afterward](chapter-force-walking-after.png) |
| Chapter 1, parley (additional attempt) | Incomplete: lost River Crossing twice; stopped before a third fight, not an end-card pass | [Harness output](chapter-parley.txt), [TV log](chapter-parley-tv.txt) |

The known `ThingShowTests.Fifty_things_cost_a_frame_little_and_allocate_little` timing test passed inside the full solution run; no isolated rerun or assertion relaxation was needed. The final reed-track coordinates and fixtures were included in that run. All ten .NET test projects passed.

Reproduction (with .NET 10 on PATH and `DOTNET_PROCESSOR_COUNT=2`):

```sh
dotnet build ArcBox.slnx --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test ArcBox.slnx --no-build --no-restore --verbosity normal --max-parallel-test-modules 1
cd web/play
npm test -- --run
```

The initial invocation attached MSBuild switches directly to the test command and stopped with runner handshake/exit-code-5 errors before any tests ran. The corrected test command above ran all 6,084 tests successfully. Test modules ran serially to avoid competing with each other's performance measurements; the TV playthroughs started afterward.

Live verification uses `tools/tv-e2e.sh --skip-build` with the branch's freshly built server and TV, four actual SignalR bot-phone connections, and a fresh unseeded campaign. The TV runs Godot 4.7.2 under Xvfb/software OpenGL compatibility at 1280×720, with Dummy audio output; recorded clips are checked through the TV's audio log. Set `ARCBOX_CHAPTER=1`, `ARCBOX_BRANCH=force` or `parley`, `ARCBOX_LINE=0.1`, and `TV_LINGER=2`. The harness checks scene graph/order, lines said exactly once, battle victory, chapter card, continued walking, screenshots, and TV errors.

In the force run, `AUDIO Voice clip af_bank_claw_marks.eb0e3450.ogg` appears after the Maud scene: the new Chapter 1 gated notice plays its recorded narrator clip in the actual game. The normal route to Maud remains usable with hidden shortcuts treated as closed by the route tool. One reroute after the battle's formation completed successfully; it is recorded by the harness.

The optional parley run reached the expected mayor/ford/parley/young-ones scenes and fought River Crossing. Both attempts ended in defeat, paying zero XP; recovery and walking back to the encounter worked. It was deliberately terminated before a third attempt (exit 130), with phones, TV, screenshot watcher, Xvfb, and server cleaned up. No TV `ERROR` lines were reported, but no complete parley scene-graph/end-card validation is claimed. No combat tuning or gameplay changes were made to make this verification pass. The full solution run includes the Chapter 1 companion tests for both story branches.

Environment limitations: software OpenGL reports unsupported V-Sync/screen-space-AA notices. These runs verify rendering and progression, not hardware frame pacing. The phone suite exits successfully but emits happy-dom teardown aborts and localhost:3000 connection errors. The production phone build also succeeds, with a Vite bundle-size advisory. These outputs are not represented as warning-free browser/runtime runs; the zero-warning result above applies to the full .NET build.

Part 2 art remains separate. The existing TV stand-ins and the general low-wall plug/unseen-trap decisions in interactables design §7.8 are unchanged.
