# Overworld checks on real Windows hardware

Checked on 2026-11-01 at `213ede0` of `ccr-18ad4510-q68nad`. No code, tests or content changed.

**Machine:** Windows 11, RTX 5080 (driver 617.42), Godot 4.7.2 .NET on Vulkan Forward+, .NET SDK 10.0.402. Steam and the Epic launcher were open. The TV ran at 1920×1080 on the `ultra` preset, capped at 144 fps, so 6.94 ms is the floor for every frame time here. It used `--audio-driver Dummy` so nothing played through the speakers, which means no real audio device was exercised.

## Summary

- **Tactics works on the real GPU.** Every Tactics e2e mode passed, including a fight in place with the seeded party.
- **Performance is well inside budget.** Walking and fights held 144 fps in both builds. Press to the TV's `WALK` line was a 4 to 5 ms median, against the cloud's 24 to 35 ms.
- **The fight lines up with the world.** There's no seam at the stamp's edges.
- **One test needs fixing:** `A_rebuild_on_the_largest_region_is_quick` fails in Release 5 runs of 6, and failed once in 3 full Debug runs.
- **REDLINE didn't run.** Playwright isn't installed on this PC, and I installed nothing. That's not a regression, but it's also not proof of none.
- **`ARCBOX_STAGE` doesn't exist on this branch.** I ran the script's documented seed instead, which starts a fight on the spot from the battle list.

## 1. Build and tests

| Step | Result |
|---|---|
| `dotnet build ArcBox.slnx -c Debug --no-incremental` | 0 warnings, 0 errors |
| `dotnet build ArcBox.slnx -c Release --no-incremental` | 0 warnings, 0 errors |
| `dotnet test --solution ArcBox.slnx`, run 1 | 4,419 of 4,420, 1 failed |
| The same, run 2 and run 3 | 4,420 of 4,420 |
| `npm --prefix web/play ci` | OK |
| `npm --prefix web/play test` | 394 of 394, 18 files |
| `npm --prefix web/play run build` | OK, with Vite's chunk-size notice |

**The failure, which doesn't repeat reliably:** `StagePlanTests.A_rebuild_on_the_largest_region_is_quick` (`StagePlanTests.cs:378`):

```text
a stage rebuild took 33.71 ms, against 35.91 ms reading the whole region
```

That's under the full solution's parallel load, where both timings grow about tenfold. Alone in Debug it passed 6 of 6. See problem 1. This is likely the unnamed failure from the handoff's §5.

## 2. tv-e2e

Run from Git Bash, one at a time, at 1920×1080 `ultra`, `--perf` on. This is the first full Windows run of `ARCBOX_WALK=1`.

| Mode | Result | Notes |
|---|---|---|
| `ARCBOX_GAME=tactics` | **OK** | 207 beats to Results, 409 effects, battle then defeat music |
| `ARCBOX_GAME=tactics ARCBOX_WALK=1` | **OK** | Saved tile 38 48 restored at 38.5 48.5. New TV snapped to 38.5 42.5. Press to `WALK` median 6, p90 8, worst 8 ms |
| `ARCBOX_GAME=tactics ARCBOX_RECONNECT=1` | **OK** | Killed at beat 30, caught up to beat 35 with 34 skipped, played on to Results |
| `ARCBOX_GAME=tactics ARCBOX_RESTART=1` | **OK** | Saw Resuming, then beat 32, 186 beats in all |
| `ARCBOX_GAME=tactics ARCBOX_STAGE=1` | **Skipped** | The script doesn't know it |
| Seeded fight (replaces it) | **OK** | `ARCBOX_SERVER_ENV` seed at (96, 61). `Forming in place`, then `Fighting in place`, `Fading in place`, `Results in place`. 158 beats, 330 effects |
| REDLINE default | **FAIL, not run** | `Cannot find module 'playwright'`. The TV opened a room first |
| REDLINE `ARCBOX_FULL=1` | **FAIL, not run** | The same |

**A cut phone, from the `WALK` run:** last step reached the TV 269 ms after the cut, party stopped at 637 to 639 ms, lead passed at 7.8 s, disconnect seen at 15.05 s. These match the cloud's numbers.

**Log checks across the five Tactics runs:** 0 `ERROR` lines, and no `Unable to create shader cache` lines. Three `WORLD late block` lines in the reconnect run: see problem 4.

REDLINE's last lines, the same in both:

```text
[tv-e2e 07:36:42] TV opened room DTGV
[tv-e2e 07:36:42] phones: 3 bots
Error: Cannot find module 'playwright'
Require stack:
- C:\Users\fatma\AppData\Roaming\npm\node_modules\noop.js
    at loadPlaywright (file:///C:/Users/fatma/Programming/dotnet/ArcBox/web/play/e2e/redline.e2e.mjs:759:56)
    at async file:///C:/Users/fatma/Programming/dotnet/ArcBox/web/play/e2e/redline.e2e.mjs:44:22
[tv-e2e 07:36:42] letting the TV finish its shots (12 s)
[tv-e2e 07:36:54] server stopped
[tv-e2e 07:36:54] screenshots: 3 from the TV in /tmp/hw/e2e/redline/tv, 0 from phone 1 in /tmp/hw/e2e/redline/phones
```

The REDLINE unit tests are in the 4,420 and pass, but the game hasn't run end to end here.

## 3. Performance

These runs had no `--shots`: `tv-e2e.sh` always takes screenshots, and they stall frames (problem 3). One walk and two fights in each build.

**"Release" isn't a true Release build.** There are no export presets or templates, so the TV ran under the Godot editor binary with `dotnet build src/ArcBox.Host -p:Optimize=true`. The server was a real `-c Release` build. Debug is what the TV runs today.

| Measure | Debug | Release | Cloud, software rendering |
|---|---|---|---|
| Frames while walking, median | 6.94 ms | 6.94 ms | 33 to 43 ms |
| Frames while walking, p99 | 7.14 ms at worst | 8.33 ms at worst | |
| Worst walking frame | 7.14 ms | 20.15 ms, once | |
| Walking frames over 16 ms | 0 of 4,608 | 1 of 4,645 | |
| Worst frame, in place fight, any 5 s window | 7.1 ms, then 6.9 ms | 6.9 ms, both | |
| Draw calls, walking / in place fight | 1,444 to 1,759 / 2,345 | the same | |
| First world load, off the main thread | 194 to 212 ms | 211 to 235 ms | |
| Fight start frame (`WORLD back`) | 4.9, 5.1 ms | 2.2, 3.1 ms | |
| Blocks re-recorded at a fight's start | 0 of 80 | 0 of 80 | |
| Hitch at a fight's start or end | none: every window's worst ≤ 7.1 ms | none: 6.9 ms | |
| Stage rebuild, stress region, alone | 2.8 to 3.0 ms | 2.7 ms | 5 ms |
| 4 world blocks rebuilt, stress region | 6.2 to 7.2 ms | 8.7 to 10.1 ms | 13 to 24 ms |
| Press to `WALK` line, median / p90 / worst | 5 / 7 / 8 ms | 4 / 7 / 7 ms | 24 to 35 ms median |
| Texture memory, world open | 38.2 MB | 38.2 MB | |

**Reading these:**

- **World load** splits as ground 34 to 48 ms, plan and light 138 to 165 ms, bake 21 to 30 ms. Release isn't faster, which matches Task 7's finding.
- **The rebuild rows come from the perf test,** not the running game. River Crossing changes no terrain, so `WORLD repainted` never fired in a real fight, and a block repaint has not run on this GPU.
- **`WORLD back`'s frame** is the only per-frame number the TV logs at a fight's start. The stage build isn't timed separately. The evidence for no hitch is that the 5 s windows' worst stayed at the cap, and in the screenshot runs those same windows did catch the stalls.
- **The `WALK` line is printed on receipt,** so add up to a frame to see the step: about 11 ms here, plus the panel and the phone's Wi-Fi. Bots ran on the same PC over loopback.
- **40 presses, 39 steps:** both builds logged `LATENCY press 0: no step`. The first press came right after the scripted walk, so the party was probably against something. I didn't confirm it.
- **Only this GPU was measured.** A modest TV box is still unknown.

## 4. By eye

I looked at the walk-in, grid, fight, Results and walking shots, and enlarged the stamp's edges 3×.

| Question | Answer |
|---|---|
| Is the fight lined up with the world? | **Yes.** World and board read as one picture, with no offset at any point I checked |
| Seams at the stamp's edges? | **None.** The grid stops exactly at the map's bounds, and grass, banks and water run straight across with no rectangle or colour step |
| Do the HUD and Results table overlap? | **No.** The header, DEFEAT banner and table each have their own space, and the fight's turn order and objective sit clear of the action. The `--perf` overlay covers the top-left title in these shots, but that's the flag |
| Do the walker rings read? | **Yes.** The dark outline holds on grass and dirt, at this distance on a monitor |
| Crown and party behind walls | Fine. The crown reads, and the party is cut off cleanly by buildings |

What looks wrong is in problems 5 to 7.

## 5. Problems

1. **A perf test depends on the build and the load.** `games/Tactics/Tactics.Presentation.Tests/World/StagePlanTests.cs:378`. `stageMs < wholeMs * 0.75` fails in Release 5 runs of 6, at 2.7 ms against 3.3 ms, a ratio of about 0.8. Debug alone is about 0.42, and under full parallel load it reached 0.94. In Release the whole-region read is cheap, so the window saves less. The rebuild itself is fast. Measuring the window's cost against a fixed budget, or comparing only items read, would fix it. Marking it `Perf` doesn't help: the solution run still includes it.

2. **REDLINE can't run here without Playwright.** `web/play/package.json` doesn't list it, and `redline.e2e.mjs:748` expects a project or global install. To be run once someone approves installing it. The user's decision, on the hardware check's list.

3. **Not a bug, but it'll mislead: `tv-e2e.sh` always takes shots.** With them, the same fight start logged `WORLD back … frame 216.7 ms`, a 13 fps window and 65 to 80 ms worst frames. Without them: 2 to 5 ms and no window over 7.1 ms. Don't judge hitches from an e2e log. An env var to turn shots off would help. Walking is unaffected unless a shot lands in it.

4. **A TV relaunched mid-fight logs late blocks and uses more texture memory.** `src/ArcBox.Host/games/tactics/WorldCanvas.cs:501`. After the kill the log has `TACTICS stage Fighting`, then `WORLD late block 41`, `56`, `62`, then `TACTICS world open … textures 55.6 MB`, against 38.2 MB on a normal open. The fight went on and the run passed. The late blocks are probably the world reaching the screen before those blocks are recorded, at a moment the plain board is covering it, but I didn't see it happen. The extra 17 MB may be the board's art loaded first. Worth checking that the takeover doesn't show a hole.

5. **A damage number merges with a Downed countdown.** Shot `058-tactics-beat-hit-72.png` shows "-102" on a level 1 fight. It's almost certainly "-10" beside a Downed digit "2": `UnitMarks.cs:61-63` draws the digit in the same spot and style family as the popup. The beat is a single `Damaged`. I didn't confirm the amount. A wrong-looking number is worth fixing, for instance by raising the popup or hiding the digit while it plays.

6. **HP bars stack during the walk-in.** Shot `022-tactics-beat-stage-walk-in.png`: 4 heroes walk in but 3 bodies show, and two bars sit together at the same spot, so one hero is hidden behind another. The seed puts the party close together, so this may not happen on a normal approach. The walk-in also shows bars on everyone while the rest of the world view has none, which reads a little busy.

7. **Small things at Results.** In `076-tactics-results-banner.png` a unit's HP bar pokes out bright just above the table, in the gap under the banner, and its Downed digits show faintly through the table. The table also runs about 200 px past its 4th row, empty. Harmless, but the bright bar draws the eye.

8. **The grid is faint on grass.** It's thin dark lines at 2×, fine at a monitor's distance. For the sofa check.

9. **Ground reads stronger than the fight.** At Results and in fights the world's saturated grass and water fill the screen, and the small figures sit in a lot of bright colour. For the sofa check. No change suggested.

## 6. Screenshots

These are local to this PC, in `C:\Users\fatma\AppData\Local\Temp\hw\`, and aren't committed. It's a temp folder, so copy them if they're wanted long term.

- **Every run:** `e2e\<mode>\tv\`, with logs at `e2e\<mode>\tv.log`, `phones.log`, `server.log`.
- **Perf runs' logs:** `perf\<debug|release>-<walk|fight|fight2>\`.
- **Walk-in:** `e2e\stage\tv\022-tactics-beat-stage-walk-in.png`
- **Grid on:** `e2e\stage\tv\033-tactics-beat-stage-grid.png`
- **Fight, the "-102":** `e2e\stage\tv\058-tactics-beat-hit-72.png`
- **Results over the world:** `e2e\stage\tv\076-tactics-results-banner.png`
- **Party stopped by the wall:** `e2e\walk\tv\022-tactics-world-stopped-39-43.png`
- **Walking after a relaunch:** `e2e\walk\tv-relaunched\008-tactics-world-walking-39-44.png`
- **Enlarged stamp edges:** `crops\grid-1.png`, `crops\grid-2.png`, `crops\grid-3.png`

## 7. Not checked

- REDLINE end to end.
- A fight started by walking into an armed stamp: the seeded run starts from the battle list. The story's fights weren't played here either.
- `WORLD repainted`, a raised or sunk tile in the real world.
- A true Release export, a modest TV box, 4K, and real audio output.
- Anything on a real phone.
