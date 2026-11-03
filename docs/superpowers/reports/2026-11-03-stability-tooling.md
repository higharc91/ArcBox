# Issue #76: stability, tests and tooling

Branch: `stability-and-tooling`, based on main `9c41a7bd`. No balance values, phone data fields, generated art or generated audio were changed.

## Delivery and proof

| Requirement | Change | Verification |
|---|---|---|
| OW-03: chapter startup/Windows logs | Retain the initial `TACTICS walk snapped to` tile and split LF/CRLF before exact screen/marker comparisons. | The CRLF regression failed before the fix and passes afterward. Both Linux chapters reached their card and walked afterward: force 98 lines, parley 130. Exact Windows commands are in the second hardware-check document. |
| OW-04: stale walk route | Generate navigation to authored destinations through `route.cs`, retaining checked steps, server restart, TV reconnect and phone-drop tests. Use enough waypoints to preserve the original >12-step coverage assertion. | Final default walking mode passed on Mesa Vulkan: 19 steps before restart, correct saved positions, cold TV reopen, 20/20 latency presses, median 72 ms against the unchanged 100 ms budget, and dropped-leader checks. |
| OW-11: world/resume failures | Guard inputs, timers, reconnects, normal session reopening and server resume. Restore the pre-action document, clear broken cursors and transient notifications/timers, save directly without re-entering failed quest processing, and restore usable walking. | Failure tests cover walk, story, interactions, actual shop purchases, camp jobs, quest pinning/payouts, timers and both reopening paths. They check preserved purses/bags/stock, rolled-back XP/reward markers, a subsequent real walking step, and reopening the recovered save. |
| OW-15: stray screenshots | Reject relative `--shots` values with an absolute-path instruction. | Both argument tests pass. `src/ArcBox.Host/src/ArcBox.Host/docs` was absent when inspected: there were no contents to list and nothing was deleted. |
| OW-16: symlink privilege | Copy the test content tree using ordinary directories/files. | The rebuilt lore-warning test passes without creating symlinks. |
| OW-17: flaky frame timing | Retain the 64 KB/frame allocation cap and 50-object assertion; bound projection work instead of elapsed wall time. | Focused test passed with one to four projections per object, independent of machine load. |
| OW-20: documentation | Update the named handoffs, reports, environmental milestones, integration table, building count, hardware prompt, actual `PropFade` references and prop-editing guide. | Reviewed against current source and the 197 authored buildings in Ashen Ford. Historical measurements remain identified as historical. |
| OW-31: shops | Verify the shipped council gold source and remake exploit through real phone inputs and the authenticated portal API. | Live shop run split 200 gold into four 50-gold purses, bought/sold from one character's purse/bag, rejected a replayed trade, sold the starting kit, remade three times, reopened the shop and confirmed an empty bag and unchanged gold. Oil's shipped price is 10. Remaining owner-overridable defaults are recorded on the board. |
| Release invariants | Enable checks by default in all builds; remove forced enabling from logic test helpers. Validate saved battles before replay. | Three Release corruption/check tests passed. 10,000 checks of a 14-unit encounter measured **0.068336 ms/check** under the live-test workload. This measures the check, not an entire command. |
| Called-off battles | Preserve the approved “nothing won or lost” behavior and leave `CallOffBattle` unchanged. | Existing battle-failure tests and invalid-resume tests verify no XP, injuries, results/checkpoint or retained battle. Structured engine-fault logs remain available for invalid saved states. |
| Missing voice clips | Default Tactics to captions only; block live TTS for all `tactics.*` themes if a recording is absent or unreadable. Keep themes registered and log missing clips. | Story and narration policy tests pass. The narrator guard runs before either server TTS or built-in speech. |
| Chapter difficulty/seed | Chapter battles default to Story and seed 76; `ARCBOX_DIFFICULTY` overrides difficulty. Keep unconfigured Development and Production at Normal, with fresh seeds outside the harness. | Engine-setting test passed. The live parley save contained seed 76 and difficulty Story. Five configuration regressions verify missing settings and Development-only overrides; the missing-setting case reproduced an enum-zero bug before the nullable lookup fixed it. |

## Recovery details caught during review

Walking screens intentionally suppress ordinary toasts to avoid changing view IDs beneath a held thumb. Recovery therefore uses the existing common controller header, which remains stable across walk presses/releases and clears on the next deliberate non-walk action. Tests verify that phones and TV actually receive the notice.

A rolled-back quest payout initially left its transient “30 gold, 100 XP” completion event on the TV. The regression reproduced this and now verifies that the event and timer disappear along with the rolled-back gold, XP and paid marker. Failed recovery saves bypass quest/reward processing to avoid repeating the failing path. Logging failures cannot prevent recovery. Recovery epochs now strictly advance even for repeated failures in one clock tick; a regression reproduced the reused epoch before the fix and verifies that the TV always sees a new reset.

## Validation

- Complete reviewed solution build: **0 warnings, 0 errors**. A later host-only diagnostic build also had zero warnings/errors.
- Focused world/battle recovery and chapter settings: **30 passed**; configuration wiring: **5 passed**; screenshot/narration policy: **24 passed**; Release invariants: **3 passed**.
- Node parser/navigation/hazard/metric tests: **12 passed**, including delayed first-motion sampling, no-motion snapshots and legacy logs.
- Phone suite: **504 passed**. Production phone build succeeded; it retains the pre-existing 537.65 kB REDLINE flyover chunk-size warning. The warning threshold and REDLINE code were not changed.
- Force chapter: **OK**, 98 lines, Storming the Ford won, camp and inn through dawn, card, subsequent walking.
- Parley chapter: **OK**, 130 lines, River Crossing won, card, subsequent walking.
- Default walk: **OK**, original coverage/displacement/latency/drop assertions retained.
- Shop: **OK**, including three real portal remakes after selling the kit.
- Original full .NET suite: **6,107 passed, 0 failed, 0 skipped**, all ten modules, in 22m 03s. Command: `dotnet test --solution ArcBox.slnx --no-build --no-restore --max-parallel-test-modules 1`.

The first broad run competed with compilation and hit runner startup failures and web timeouts. The phone rerun passed. A later broad run exceeded two AI timing budgets; all five existing AI performance cases passed in a separate Debug process with unchanged 250 ms limits (stress p95 149.4 ms; overlapping hazards 145.1 ms). The final full suite passed after renderer/compiler activity stopped, including those same AI performance cases.

## Review follow-up: recovery failures

The outer world guard now protects the entire recovery block as well. If recovery itself throws (including inside `EnterWorld`), it first saves the pre-action party and progression with no battle, story, shop or camp and the position/facing reset to the region's authored start. This fallback bypasses world validation, quest processing, leader timers and callbacks before saving; it then resets in-memory cursors without host calls. Timer cancellation and diagnostic sinks are best effort after the save.

The new regression first failed with `EnterWorld` escaping from the recovery catch. Its two cases cover failure during both initial entry and recovery, and failure only during recovery after a later resume error. They verify the saved start position, cleared cursors, preserved party/stock/flags, phone/TV notices despite a throwing log sink, no stale timers, and successfully reopening and walking from the repaired save. Follow-up validation: the solution rebuild passed with **0 warnings/errors**, and **31 world/battle-fault tests passed**, including both new cases. The complete affected suites also passed: **638 gameplay tests** and **411 server tests**, with no failures or skips. These are follow-up results, separate from the original full-suite run. Logs: `/workspace/artifacts/issue76-recovery-fallback-{red,green,solution-build,gameplay,server}.log`.

## Linux rendering and measurement limits

These are automated Linux software-rendered runs, not the owner's Windows/GPU/real-phone/audio check (OW-19). Force, parley and shop ran with OpenGL compatibility at 640×360. Cold OpenGL TV reopening intermittently exceeded the opening watchdog; this was not fixed or hidden by increasing timeouts. Managed stack snapshots did not identify a managed deadlock. The timeout diagnostic now includes loader stage and progress for further investigation in the drawing issue.

The initially available Chromium SwiftShader Vulkan driver lacked X11 surface support. Mesa's matching `mesa-vulkan-drivers` package was extracted locally into `/workspace/.arcbox/vulkan`; a local ICD points to its `libvulkan_lvp.so`. The final walking run used the script's intended Vulkan driver, Mobile rendering method, 640×360 and no screenshots. It passed cold reopening with no renderer timeout or server watchdog closure. No test threshold was raised.

The first generated route exposed two runner assumptions: its first leg was too short, and a delayed first `WALKAT` sample was incorrectly treated as the origin. The route was extended to preserve coverage, and displacement now uses the initial snap while still requiring rendered motion. The regression reproduced 2.73 vs the actual 5 tiles; a snap without motion still measures zero.

Example commands used here (prefix with `source /workspace/.arcbox/activate.sh`):

```bash
ARCBOX_CHAPTER=1 ARCBOX_CAMP=1 ARCBOX_BRANCH=force ARCBOX_LINE=1 ARCBOX_SHOTS=0 TV_RES=640x360 TV_RENDERING_DRIVER=opengl3 TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy --position 160,100' tools/tv-e2e.sh --skip-build
ARCBOX_CHAPTER=1 ARCBOX_BRANCH=parley ARCBOX_LINE=1 ARCBOX_SHOTS=0 TV_RES=640x360 TV_RENDERING_DRIVER=opengl3 TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy --position 160,100' tools/tv-e2e.sh --skip-build
VK_ICD_FILENAMES=/workspace/.arcbox/vulkan/lvp-local.json ARCBOX_GAME=tactics ARCBOX_WALK=1 ARCBOX_SHOTS=0 TV_RES=640x360 TV_RENDERING_DRIVER=vulkan TV_ENGINE_ARGS='--rendering-method mobile --audio-driver Dummy --position 160,100' tools/tv-e2e.sh --skip-build
ARCBOX_SHOP=1 ARCBOX_LINE=1 ARCBOX_SHOTS=0 TV_RES=640x360 TV_RENDERING_DRIVER=opengl3 TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy --position 160,100' tools/tv-e2e.sh --skip-build
```

Session logs are under `/workspace/artifacts/issue76-*`. The owner hardware commands remain in `docs/superpowers/plans/2026-11-02-local-hardware-check-2.md`.
