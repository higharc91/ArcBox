# Platform v2 · issue #42 completion audit

Audited main: `00490381ba6d66a87f297ebd68163cfb566a8581`, 2026-11-05. The owner requested verification of the completed work, the normal/restart/phone-swap REDLINE runs, then checklist upkeep and closure if the evidence passes.

All sixteen child issues #26–#41 are closed as completed. Their PRs #44–#59 report merged, and every merge SHA is an ancestor of the audited main. [Machine-readable audit](task-audit.json).

| Task | Issue | Merged PR | Merge commit |
| --- | --- | --- | --- |
| 1 | #26 | [#44](https://github.com/higharc91/ArcBox/pull/44) | `ef1eb874` |
| 2 | #27 | [#45](https://github.com/higharc91/ArcBox/pull/45) | `1fc6ac59` |
| 3 | #28 | [#46](https://github.com/higharc91/ArcBox/pull/46) | `bed7167d` |
| 4 | #29 | [#47](https://github.com/higharc91/ArcBox/pull/47) | `ac4c1798` |
| 5 | #30 | [#48](https://github.com/higharc91/ArcBox/pull/48) | `a7f2a49e` |
| 6 | #31 | [#49](https://github.com/higharc91/ArcBox/pull/49) | `69dcbce0` |
| 7 | #32 | [#50](https://github.com/higharc91/ArcBox/pull/50) | `2bc25a35` |
| 8 | #33 | [#51](https://github.com/higharc91/ArcBox/pull/51) | `be57d674` |
| 9 | #34 | [#52](https://github.com/higharc91/ArcBox/pull/52) | `3d0e8ca4` |
| 10 | #35 | [#53](https://github.com/higharc91/ArcBox/pull/53) | `33c9508e` |
| 11 | #36 | [#54](https://github.com/higharc91/ArcBox/pull/54) | `562449ad` |
| 12 | #37 | [#55](https://github.com/higharc91/ArcBox/pull/55) | `477b790f` |
| 13 | #38 | [#56](https://github.com/higharc91/ArcBox/pull/56) | `a51a36e5` |
| 14 | #39 | [#57](https://github.com/higharc91/ArcBox/pull/57) | `a0e6efeb` |
| 15 | #40 | [#58](https://github.com/higharc91/ArcBox/pull/58) | `f021cffc` |
| 16 | #41 | [#59](https://github.com/higharc91/ArcBox/pull/59) | `0556f623` |

## Automated-suite evidence

The recent [full verification](../2026-11-04-ashen-ford-secrets/merge/README.md) passed all 6,376 .NET tests (zero failed/skipped), 517 phone tests, both builds and five art checks. Its source `183d16a9` has exactly the same runtime code, content and scenes as audited main `00490381`; intervening differences are documentation/art-provenance records only. No game or platform code was changed for this audit, so those suites were not repeated.

## Fresh end-to-end checks

All three required scenarios **passed**, exit 0, on actual Godot and three Chromium phone clients:

| Scenario | Verified behavior | Logs |
| --- | --- | --- |
| Normal | Three qualifying laps, expected bot grid order, first race Reveal, five start beeps/lights-out/question/answer audio events | [Harness](plain.txt), [TV](plain-tv.txt), [phones](plain-phones.txt), [audio](plain-audio.txt) |
| Server restart | Real server process restarted on the same database; every phone shows the interruption note and returns to the same room; TV parks the race, shows results then lobby; no race audio after parking | [Harness](restart.txt), [TV](restart-tv.txt), [phones](restart-phones.txt), [audio](restart-audio.txt) |
| Phone swap | Bot2 takes its offline seat through the picker during round 2; retains its car, live question and P9 at rejoin; old secret is refused; continues through all 15 rounds, podium, results and lobby; appears once in final results (NOR, 6 points, P18) | [Harness](swap.txt), [TV](swap-tv.txt), [phones](swap-phones.txt), [audio](swap-audio.txt) |

The full race also verifies the anthem, victory theme/sting and champagne spray/cork events. Screenshots were inspected: [normal TV Reveal](plain-reveal.png), [restart note](restart-results.png), [restart lobby](restart-lobby.png), [swap picker](swap-picker.png), [recovered question](swap-recovered.png), [old secret refused](swap-old-secret-refused.png), [final results](swap-results.png).

No game/platform/harness code changes were necessary. The prior task-16 gap—the three real TV runs unperformed in PR #59—is now covered. All processes started by the checks were stopped.

The cloud environment had Playwright 1.62.1 but no matching browser. Chromium was installed outside the repository. The first Forward+/Vulkan software attempt timed out while warming the 3D scene, before the host started the race. [The unsuccessful startup attempt is recorded separately](vulkan-startup-attempt.txt); it is not counted as a passing run. The functional runs use Godot's OpenGL compatibility renderer at 1280×720, Low preset, a 30 fps cap and Dummy audio, with unchanged scripts, assertions, pace and timeouts. There is a real server, TV and browser phone app; no TV stand-in is used. This is not a hardware frame-pacing test.

Physical-phone lock/unlock and audible speaker playback are not claimed by this browser/Dummy-audio audit. The earlier child-task PR #59 recorded those as manual checks; #42's stated completion criterion is the merged task set, green automated suites and these three end-to-end modes.

## Completion and reproduction

The child-task checklist and closure in [issue #42](https://github.com/higharc91/ArcBox/issues/42) are authorized by the owner. This documentation branch publishes evidence only; the audited implementation is already on main. No implementation PR or new package dependency was needed. The working-tree audit confirms no runtime changes.

With the built server/TV and phone distribution, use the existing harness:

```sh
ARCBOX_GAME=redline bash tools/tv-e2e.sh --skip-build
ARCBOX_GAME=redline ARCBOX_RESTART=1 bash tools/tv-e2e.sh --skip-build
ARCBOX_GAME=redline ARCBOX_SWAP=1 bash tools/tv-e2e.sh --skip-build
```

Set `GODOT` to Godot 4.7.2 .NET, and make Playwright/Chromium available to the script. This cloud run supplied `NODE_PATH` and `PLAYWRIGHT_BROWSERS_PATH` for an installation outside the repo, plus `TV_RENDERING_DRIVER=opengl3` and a Godot wrapper selecting `gl_compatibility`, 30 fps and Dummy audio. Each mode has a fresh database/output directory. The default three phones, bot pace, assertions and timeouts were unchanged. Raw execution logs retain UTC timestamps.
