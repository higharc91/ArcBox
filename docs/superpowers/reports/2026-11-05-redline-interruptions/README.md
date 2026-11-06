# REDLINE interruption and phone-swap follow-up (#24)

Branch: `redline-issue24-completion`, based on main `00490381` (PR #93). Runtime and harness fix: `696ada39`. Physical-phone acceptance remains open at the owner's request on 2026-11-05.

## Fix

A restart during Champagne or Chequered was indistinguishable from a normal ending when the TV decided whether to stop the race's audio. The interruption result now reaches the parked scene through either event ordering: `GameEnded` first, or the rebuilt `LobbyChanged` first. An explicit server interruption stops narration, effects and music even in the last phase. A normal ending still allows the last line and cork to ring out.

The new regression tests failed for Champagne and Chequered before the fix (2 failed / 9 passed), then all 11 passed. The complete REDLINE presentation suite passes 671 tests. Server integration tests pass for restoring the same room with empty interruption scores and starting a new race, plus taking an offline seat mid-question and rotating its secret.

## Regression results

- Solution build: zero warnings/errors ([log](tests/build.log)).
- Full .NET solution: **6,383 passed**, zero failed/skipped ([log](tests/full-tests.log)).
- Phone suite: **517 passed** ([log](tests/phone-tests.log)); existing happy-dom iframe-loading diagnostics are emitted by dev-page tests.
- Restart-target helper: **8 passed** ([log](tests/restart-policy-tests.log)).
- Interruption policy: [failing before the fix](tests/interruption-red.log), [11 passing afterward](tests/interruption-green.log).
- Presentation: [671 passing](tests/presentation-tests.log). Server restart/swap: [2 passing](tests/server-restart-tests.log).

The first qualifying attempt failed its new assertion because graceful shutdown delivered a pending first-lap result. Recovery, room identity and off-air checks already passed. The final assertion accepts precisely the first reading/question and optional pending result, while still rejecting another lap or the grid; the rerun passed.

## TV and browser acceptance

| Case | Room | Result |
|---|---|---|
| [Qualifying restart](qualifying/harness.log) | VGXS | Passed: interrupted first lap, same room, clean park and results/lobby |
| [Question restart and immediate new race](question/harness.log) | QVVH | Passed: all phones recovered, VIP started again, TV and phones reached Driver Select |
| [Reveal restart](reveal/harness.log) | WDVM | Passed: interruption results, unchanged room, no race audio after park |
| [Anthem restart](anthem/harness.log) | JMLM | Passed: TV interrupted during Anthem, clean off-air park and same-room recovery |
| [Champagne restart](champagne/harness.log) | GKMM | Passed: final podium phase is explicitly interrupted, audio stopped, same-room results/lobby |
| [Phone swap and normal ending](swap/harness.log) | LCWF | Passed: same seat/current question, old secret rejected, Bot2 listed once at finish; anthem, victory theme, sting and all three bottle sprays/corks |

All six harness processes exited zero ([results](tv-results.json)). The normal full race logs `REDLINE parked for the next race` without the interruption suffix, preserving its regular ending. These runs used Godot 4.7.2 .NET, 1280×720 low preset, compatibility/OpenGL renderer at 30 FPS, three Chromium phones, and the standard bot pace of 4. No game timers or race length were shortened for the checks.

Phone-swap previews: [seat picker](swap/swap-pick-P2.png), [current question on the replacement](swap/swap-back-P2.png), [old phone rejected](swap/swap-old-phone-P2.png).

Phone previews: [interrupted results](question/restart-results.png), [same-room lobby](question/restart-lobby.png), [new race](question/restart-new-race.png).

Archived logs have trailing whitespace normalized; original files remain in `/workspace/issue24`.

Connection-refused/SignalR negotiation messages during the deliberate server outage are expected. The harness checks recovery and rejects page exceptions and TV engine errors.

## Reproduce

See [REDLINE TV testing](../../../redline-tv.md). With a Godot .NET binary in `GODOT`, run:

```sh
ARCBOX_RESTART=1 ARCBOX_RESTART_PHASE=qualifying tools/tv-e2e.sh
ARCBOX_RESTART=1 ARCBOX_RESTART_PHASE=question ARCBOX_RESTART_AGAIN=1 tools/tv-e2e.sh
ARCBOX_RESTART=1 ARCBOX_RESTART_PHASE=reveal tools/tv-e2e.sh
ARCBOX_RESTART=1 ARCBOX_RESTART_PHASE=anthem tools/tv-e2e.sh
ARCBOX_RESTART=1 ARCBOX_RESTART_PHASE=champagne tools/tv-e2e.sh
ARCBOX_SWAP=1 tools/tv-e2e.sh
node --test tools/tactics-playtest/redline-restart.test.cjs
```

Each restart target waits for the phone and TV phases to agree before requesting shutdown. A graceful shutdown may drain a pending qualifying answer; the qualifying assertion permits its result but rejects the second lap or grid. Other cases keep the original qualifying and audio-order checks. The immediate restart case requires all phones and the TV to enter the next Driver Select and permits new race audio only after that point.

## Remaining manual acceptance

- Lock a physical phone during a race, restart the server with the same database, then unlock it. Confirm the interrupted notice and the same room/seat, then dismiss to the lobby.
- Use another physical phone to take an offline seat mid-question. Confirm the current options, driver, score and position; the old phone must not reclaim the rotated secret.
- Listen to a normal podium ending and confirm the last narration/cork finishes naturally. Listen through interruptions during Anthem and Champagne for any lingering race sound.

The cloud tests use the compatibility renderer and dummy audio output. Audio scheduling and off-air behavior are checked through TV logs and presentation tests; physical audio playback and OS lock/background behavior are not claimed as verified. Forward+ software Vulkan timed out in the preceding #42 audit; hardware renderer/performance acceptance remains separate.
