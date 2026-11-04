#!/usr/bin/env bash
# TV end-to-end run: the ArcBox server, the Godot TV app and bot phones together, with screenshots from both.
#
#   GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tools/tv-e2e.sh            REDLINE to the first Reveal
#   ARCBOX_FULL=1 GODOT=… tools/tv-e2e.sh                                           a full race, results, back to the lobby
#   ARCBOX_RESTART=1 GODOT=… tools/tv-e2e.sh                                        the server restarts mid-race (issue #24)
#   ARCBOX_SWAP=1 GODOT=… tools/tv-e2e.sh                                           a phone is swapped mid-race (issue #24)
#   ARCBOX_GAME=tactics GODOT=… tools/tv-e2e.sh                                     a Tactics battle to its Results, with bot phones
#   ARCBOX_GAME=tactics ARCBOX_RECONNECT=1 GODOT=… tools/tv-e2e.sh                  the TV is killed mid-battle and comes back
#   ARCBOX_GAME=tactics ARCBOX_RESTART=1 GODOT=… tools/tv-e2e.sh                    the server restarts mid-battle
#   ARCBOX_GAME=tactics ARCBOX_WALK=1 GODOT=… tools/tv-e2e.sh                       the party walks Ashen Ford: a server restart, a
#                                                                                   killed TV, the press-to-TV time and a dropped phone
#   ARCBOX_STAGE=1 GODOT=… tools/tv-e2e.sh                                          a fight in the world: walk in, lose, wake, walk back
#                                                                                   in and fight it again (with ARCBOX_RECONNECT=1,
#                                                                                   ARCBOX_RESTART=1 or ARCBOX_DROP_SIGNALS=1 too)
#   ARCBOX_SHOP=1 GODOT=… tools/tv-e2e.sh                                          the herbalist shop, real phone trades and a duplicate id
#   ARCBOX_DECISIONS=1 GODOT=… tools/tv-e2e.sh                                     the owner's rule decisions (issue #83): a loot vote, a
#                                                                                   trap's injury mended at camp, a lock retried after the
#                                                                                   rest, the key ring and a haggle, on a copy of the pack
#                                                                                   with one e2e strongbox in Ashen Ford
#   ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force|parley|skip-assault GODOT=… tools/tv-e2e.sh all of Chapter 1 from a fresh campaign, on a branch
#                                                                                   (with ARCBOX_RESTART=1 and ARCBOX_RECONNECT=1 mid-scene)
#
# What it does (REDLINE; Tactics is below):
#   1. builds the phone app (web/play/dist), the server and the TV host (skip with --skip-build);
#   2. starts the server in Development (serves web/play/dist, OpenTDB off, Tactics:BattleList on, as the bots pick their fight
#      from it) on a free port on 127.0.0.1;
#   3. starts the TV on its own Xvfb display with software Vulkan:
#        --new-room --shots=<out>/tv --preset=<low> --res=<1280x720> --server=<url> --autostart=redline:<phones> --audio-log
#      and reads the room code from its `ROOM <CODE>` log line (the server keeps its rooms in <out>/arcbox.db);
#   4. runs web/play/e2e/redline.e2e.mjs with ARCBOX_ROOM=<CODE>, ARCBOX_START=host and <phones> bot phones;
#      the TV selects REDLINE and starts it once they have all joined (--autostart);
#   5. lets the TV finish its last shots, stops everything it started, and checks the TV log for errors;
#   6. checks the TV's `AUDIO <bus> <name>` lines (--audio-log) for the race's sounds in order: the five start beeps,
#      lights out, the commentator's first line, the question read out, the Reveal sting or buzz and the answer
#      line (and, with ARCBOX_FULL=1, the fanfare and the winner's line, then the podium: the TV reaching the
#      Champagne, the anthem — or the victory theme for a country without a melody — followed by the theme, the
#      victory sting, and a cork pop and spray from every podium phone). Without a Google key on the server,
#      /api/tts answers 503 and the narration comes from the TV's built-in voice, or is only logged when the TV has
#      none (Linux without speech-dispatcher): the AUDIO lines are the same either way.
#   7. checks qualifying: the TV's phases before the Grid are QualifyingReading, QualifyingQuestion and
#      QualifyingResult for laps 1–3; the AUDIO lines have the session's opening line and then "Pole position for …";
#      and the bot phones' order in the TV's `REDLINE grid` line is the one their answer times give (the phone
#      script's `QUALI order` line: bot 1 fastest, bot 3 last of them after its planned wrong answer on lap 2).
#   8. ARCBOX_RESTART=1: once the race is in round 2 the phone script logs `RESTART now`; the server is stopped and
#      started again on the same URL and database (spec §3.5). The rebuilt room is back in the lobby: the phones must all
#      show "Game interrupted: the server restarted." and then room <CODE>'s lobby (the phone script checks), and the TV
#      must log `ROOM <CODE>` again (never another code), park the race (`REDLINE parked for the next race
#      (interrupted)`), log `RESULTS Game interrupted: the server restarted.` and then `LOBBY <CODE>`, and start no
#      sound after the park but the lobby loop: no narration, anthem, effects or engines from the cut-off race.
#   9. ARCBOX_SWAP=1 (a full race): on a question in round 2 or later, bot 2's storage is cleared and a fresh page takes
#      its seat back through the picker (Peek, then TakeSeat); its old secret must fail to rejoin, and it must finish the
#      race in its own seat (the phone script checks; the TV's `RESULTS` line must list it once).
#
# Tactics (ARCBOX_GAME=tactics): steps 1, 2 and 5 are the same, and the TV starts the same way with --beat-log in place of
# --autostart. The phones are tools/tactics-playtest/bots.cjs, which needs no browser: they fight the encounter to its end,
# hold the results for TV_HOLD seconds, and end the session. REDLINE's audio and qualifying checks don't apply. It fails unless:
#   - the TV logs `TACTICS screen tactics-results` and saved a screenshot of them;
#   - its `AUDIO` lines show music from the start, effects during the battle, and at the Results a change to the victory or defeat
#     sting (or camp, if the party fled); and, on an encounter whose rules have `say` lines, a recorded clip for one of them:
#     `AUDIO Voice clip say_<encounter>_<n>`. To keep a run from playing through your speakers, add --audio-driver Dummy to TV_ENGINE_ARGS:
#     the lines are the same;
#   - ARCBOX_RECONNECT=1: once the TV has played beat ARCBOX_AT_BEAT, it's killed outright and relaunched after a few seconds,
#     with no --new-room, so it resumes through user://session.json and ResumeHost, and shots go in <out>/tv-relaunched. It must
#     log `ROOM <CODE>` again (never another code), then `TACTICS catch-up to beat <n>, <m> skipped`, and the first BEAT it
#     plays must be that n, newer than the last it played before it died: no old beat comes back. Then Results as above;
#   - ARCBOX_RESTART=1: at the same beat the server is stopped and started on the same URL and database. The TV must log
#     `ROOM <CODE>` again (never another code), show `TACTICS screen tactics-resuming`, and play a new beat after it. The
#     bots rejoin by themselves, and on Linux they're often all back before the TV, so Resuming is over before it hears of it:
#     then the first beat it plays once back must be newer than any it played before. Then Results as above.
#
# Walking (ARCBOX_GAME=tactics ARCBOX_WALK=1): the TV starts with --walk-log as well, and the bots walk the world after Muster rather
# than fighting (bots.cjs --walk=ARCBOX_WALK_SCRIPT --walk-latency=<presses> --walk-drop=kill --tv-log --go). At the script's
# pause:restart the server is stopped and started again, as ARCBOX_RESTART does; at pause:tv the TV is killed and relaunched, as
# ARCBOX_RECONNECT does. Each time the bots go on once the TV is back. It fails unless:
#   - the TV's WALK lines, which number the steps it was told of, climb by more than ARCBOX_WALK_STEPS (default 12) before the
#     restart, and only climb, between each restart and the next; and its WALKAT lines, where it drew the party, put the leader
#     somewhere else by the restart: --walk really moved the party on the TV;
#   - after the restart the TV says room <CODE> again, and the first step after it starts beside the tile of the last step before it,
#     and any WALKAT before that step has the leader on that tile: the party is on its saved tile;
#   - after the kill the new TV says room <CODE>, then `TACTICS walk snapped to` the middle of the party's tile, and takes steps again;
#   - --walk-drop: the TV is told of no step that began more than the lease (750 ms) after the leader's phone was cut, give or take
#     ARCBOX_WALK_SLACK ms (default 250: the TV's own frame, which is slow on software rendering), and hears that the party stopped;
#     and another phone sees the leader disconnect;
#   - the median press-to-WALK-line time of --walk-latency is at most ARCBOX_WALK_LATENCY ms (default 100, the plan's target), and
#     at least ARCBOX_WALK_LANDED percent (default 90) of its presses reached the TV at all: a press that never did isn't timed;
#   - the TV took screenshots of the party walking and stopped (not with ARCBOX_SHOTS=0), has an AUDIO Music line, and no errors.
# The run's numbers are in phones.log: `LATENCY`, `DROP … after … ms`.
#
# In the world (ARCBOX_STAGE=1): the server's Development seed (Tactics:Seed) starts the campaign after Ashen Ford's vote, four
# steps from River Crossing's stamp, with its fight on offer (ARCBOX_ENCOUNTER=ford_assault: the Ford Assault's, after the other
# vote). The TV has --walk-log, and the bots run --walk-to-stamp=2 --lose=1 --fight --tv-log: they walk into the stamp, lose that
# fight on purpose, wake at the recovery spawn, walk all the way back in, and fight it again. They fight to win, but a retry after
# a loss is weaker and is often lost too, so the run doesn't need the win. The beat's lead-in scene plays before the first fight
# and its aftermath after a win: the leader's bot skips each line after 1.5 s, and the bots answer votes and
# checks at once. It fails unless:
#   - both walk-ins started a fight (the bots' `STAMP the fight starts`), and the TV walked each in in place
#     (`TACTICS stage Forming in place`) on the encounter's map;
#   - the first fight was lost, and the TV then showed `TACTICS stage Waking` and snapped the party to the recovery spawn's middle;
#   - after the wake the TV showed a second battle on the map, walked in in place, and the bots' results name the same encounter;
#   - the second fight was fought to its end, won or lost: its results are on the TV, with the victory music at a win (defeat at
#     a loss), and after a win
#     `TACTICS stage FallingIn`, then `TACTICS stage Walking` and a step: the fall-in, and walking again;
#   - ARCBOX_RECONNECT=1: the TV is killed at ARCBOX_AT_BEAT of the first fight (its beats are counted from its battle screen to its
#     results, as each battle numbers its beats from 1, so a first fight that ends sooner gets no kill and fails) and relaunched; it says room <CODE>, catches up
#     (`TACTICS catch-up to beat <n>`), and the first beat it plays is n, newer than any it played before: no replay;
#   - ARCBOX_RESTART=1: the server restarts at that beat; the TV is back in the room and plays on from a newer beat;
#   - ARCBOX_DROP_SIGNALS=1: the TV runs with --drop-signals, so the game hears no `shown` and paces every beat on its fallbacks;
#     the TV logs the dropped signals, and all of the above still happens;
#   - the TV took shots of the walk-in, the fight in place and the results (not with ARCBOX_SHOTS=0), has music, and no errors.
#
# All of Chapter 1 (ARCBOX_CHAPTER=1, the 5d plan's ARCBOX_STORY): a fresh campaign, no seed. The TV has --walk-log and --story-log, and
# the bots run --chapter --branch=ARCBOX_BRANCH --fight --line=ARCBOX_LINE --tv-log: from the spawn they walk to each "!" the TV's
# `STORY marker` lines show, by route.cs, tap Interact, skip each line after ARCBOX_LINE seconds (default 2.5), vote for the branch at
# the ford and take the first option elsewhere, roll with the best bonus, fight each battle beat to win (a loss puts its "!" back, and
# they walk to it again), and once the chapter's card is up walk on a few steps and end the session. ARCBOX_SILENT=1 (force only:
# a vote nobody answers takes its first option) passes --silent, so nobody skips, votes or picks and the game's timers run every
# scene; it fails if the bots answered anything. ARCBOX_DROP_SIGNALS=1 runs the TV with --drop-signals, and checks it dropped them. With ARCBOX_RESTART=1 the server restarts once ARCBOX_AT_LINE (default
# 3) lines of ARCBOX_RESTART_SCENE (default ch1_maud) have been said; with ARCBOX_RECONNECT=1 the TV is killed and relaunched at that line
# of ARCBOX_RECONNECT_SCENE (default ch1_docks); both can be in one run. Shots of each scene, question, roll, fight, results, the card and
# the walk after it are taken from the X display into <out>/chapter (tools/tactics-playtest/chapter.cjs shots). It fails unless
# (tools/tactics-playtest/chapter.cjs check):
#   - the TV's lines come in the branch's scenes, in order, each scene's lines a walk of its graph to an end, and every line said once
#     (the line a relaunched TV comes back to is only shown);
#   - the battle beat's fight is on the TV between its lead-in and its aftermath, and the bots' last fight was won;
#   - the chapter's card is up after the last line (`STORY card`), and the party takes a step after it;
#   - ARCBOX_RESTART=1: the TV is back in room <CODE>, and its first line after the restart is the one it showed before: the saved line;
#   - ARCBOX_RECONNECT=1: the new TV is in room <CODE>, shows the line it came back to without saying it, and goes on;
#   - the TV log has no errors, and the phones exited 0.
#
# On Windows (Git Bash) there's no Xvfb: the TV opens a real window on your screen, at TV_RES, with Godot's default rendering
# driver, and processes are stopped with taskkill, as Windows has no TERM. Put the .NET SDK's dotnet first on PATH and set GODOT
# to the *_console.exe (the plain exe has no stdout to read ROOM from). Only Tactics was run there. Git Bash has no
# Playwright, so REDLINE's phone script won't run unless you install it.
# On Linux (Xvfb, software Vulkan) ARCBOX_GAME=tactics, with ARCBOX_RECONNECT=1, ARCBOX_RESTART=1 and ARCBOX_WALK=1, passed on
# 2026-11-01. The REDLINE modes are the same steps as before, with the TV's start in a function, but weren't rerun.
#
# Environment (all optional except GODOT when `godot` is not on PATH):
#   GODOT            Godot 4.7.2 .NET binary
#   ARCBOX_GAME      redline (default) or tactics
#   ARCBOX_RECONNECT 1 = (tactics) kill and relaunch the TV mid-battle, and check it catches up. A TV the kill didn't stop fails the
#                    run, and is stopped with everything else at the end
#   ARCBOX_AT_BEAT   (tactics) the beat at which to kill the TV or restart the server (default 30)
#   ARCBOX_WALK      1 = (tactics) walk the world rather than fight, with a server restart, a TV kill and a dropped phone (above)
#   ARCBOX_STAGE     1 = fights where the party walks into them, one lost and one fought again (above); implies ARCBOX_GAME=tactics
#   ARCBOX_CHAPTER   1 = all of Chapter 1 from a fresh campaign (above); implies ARCBOX_GAME=tactics
#   ARCBOX_CAMP      1 = with CHAPTER, camp after the ford and buy an inn room after the next story rest
#   ARCBOX_BRANCH    (chapter) force (default) or parley: the option the bots vote for at the ford. skip-assault is force with a
#                    Development seed that has the Ford Assault done (Tactics__Seed__Beats__0=ch1_ford_assault), so the ford choice goes
#                    straight on to Old Maud with no fight: the chapter to its card without the assault's coin toss (decision 31)
#   ARCBOX_SILENT    1 = (chapter, force) the bots answer nothing in a scene: the game's timers resolve every line, vote and check
#   ARCBOX_LINE      (chapter) seconds a line is up before the leader's bot skips it (default 2.5)
#   ARCBOX_AT_LINE   (chapter) the line of the scene at which to restart the server or kill the TV (default 3)
#   ARCBOX_RESTART_SCENE, ARCBOX_RECONNECT_SCENE (chapter) the scenes for those (default ch1_maud and ch1_docks: on both branches)
#   ARCBOX_DROP_SIGNALS 1 = (stage or chapter) the TV sends the game no signals, so every beat goes on its fallback
#   ARCBOX_WALK_SCRIPT (walk) the bots' --walk script; it must have pause:restart and then pause:tv
#   ARCBOX_WALK_PRESSES (walk) presses timed for the latency (default 20)
#   ARCBOX_WALK_LANDED  (walk) the percentage of those that must reach the TV (default 90)
#   ARCBOX_ENCOUNTER (tactics) the encounter (default river_crossing)
#   TV_HOLD          (tactics) seconds the bots leave the results up (default 6)
#   ARCBOX_DIFFICULTY Story|Normal|Hard (default Story for chapter runs, Normal otherwise).
#   ARCBOX_SERVER_ENV more KEY=value settings for the server, in every mode (REDLINE's too, which ignores Tactics ones), e.g. a Development seed that puts the party by the stamp, so the
#                    fight walks in rather than fading: Tactics__Seed__Region=ashen_ford Tactics__Seed__At__0=96 Tactics__Seed__At__1=61
#   ARCBOX_BOT_ARGS  (tactics) the bots' fighting options, in place of --fight; e.g. --sit-out=turn for a party that loses
#   ARCBOX_FULL      1 = race to the chequered flag and back to the lobby (passed to the phone script)
#   ARCBOX_RESTART   1 = restart the server mid-race and check everyone lands in the same room's lobby (not with ARCBOX_FULL);
#                    in Tactics, mid-battle or mid-scene, as above. The server gets a TERM, so it saves and shuts down cleanly.
#                    kill = (tactics, not walking) the same restart, but the server is killed outright, as a crash would, and
#                    saves nothing on the way down; the checks are the same
#   ARCBOX_SWAP      1 = swap bot 2's phone mid-race and check it finishes in its seat (implies ARCBOX_FULL=1; needs 2+ phones)
#   ARCBOX_PHONES    bot phones (default 3; 4 for tactics, as its encounters want a party of 4)
#   ARCBOX_PACE      bots' reading-time multiplier (default 4 here: 1–9 s per decision, so each phase stays on the
#                    TV long enough for its screenshot even when the TV draws a frame a second)
#   ARCBOX_E2E_OUT   output folder (default /tmp/arcbox-tv-e2e-<time>): tv/*.png, phones/*.png and the three logs
#   TV_RES           TV window size (default 1280x720)
#   TV_PRESET        render preset (default low; software rendering can't keep up with the phase timers otherwise)
#   TV_LINGER        seconds the TV keeps running after the phones finish, for its last shots (default 12)
#   TV_ARGS          extra TV app arguments, e.g. --perf (log fps every 5 s)
#   ARCBOX_SHOTS     0 = the TV takes no screenshots, and the checks for them are skipped (default 1). A shot stalls the TV's frames
#                    (a 216 ms fight start with them, 2 to 5 ms without, on an RTX 5080), so judge hitches and frame times only from a
#                    run with ARCBOX_SHOTS=0 and TV_ARGS=--perf. REDLINE's phones take theirs either way (their folder is set here)
#   TV_ENGINE_ARGS   extra Godot engine arguments, before the --, e.g. --position 160,100 to place the window; Godot counts from the left edge of the leftmost monitor, so x can't be negative
#   TV_RENDERING_DRIVER Linux renderer (default vulkan); use opengl3 with --rendering-method gl_compatibility on CI machines without Vulkan surface support
#   TV_REMATCH       1 (with ARCBOX_FULL=1) = after the race, the VIP phone starts a second one in the same room
#                    and plays it to its first Reveal (shots in <out>/phones-rematch)
#
# Exit code 0 when the phones reached their goal, the TV log has no errors beyond the expected audio-driver ones, the
# audio lines are all there in order, qualifying ran and set the bots' grid order, and (ARCBOX_RESTART, ARCBOX_SWAP)
# the TV saw the restart or the swap as described above.
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
GODOT=${GODOT:-$(command -v godot || true)}
SHOP=${ARCBOX_SHOP:-}
DECISIONS=${ARCBOX_DECISIONS:-}
STAGE=${ARCBOX_STAGE:-}
CHAPTER=${ARCBOX_CHAPTER:-}
CAMP=${ARCBOX_CAMP:-}
QUESTS=${ARCBOX_QUESTS:-}
SILENT=${ARCBOX_SILENT:-}
BRANCH=${ARCBOX_BRANCH:-force}
# Chapter automation uses Story difficulty and a reproducible battle seed; balance belongs in the simulator.
DIFFICULTY=${ARCBOX_DIFFICULTY:-$([[ "$CHAPTER" == "1" ]] && echo Story || echo Normal)}
BATTLE_SEED_ENV=$([[ "$CHAPTER" == "1" ]] && echo Tactics__BattleSeed=76 || true)
LINE_S=${ARCBOX_LINE:-2.5}
AT_LINE=${ARCBOX_AT_LINE:-3}
RESTART_SCENE=${ARCBOX_RESTART_SCENE:-ch1_maud}
RECONNECT_SCENE=${ARCBOX_RECONNECT_SCENE:-ch1_docks}
DROP_SIGNALS=${ARCBOX_DROP_SIGNALS:-}
GAME=${ARCBOX_GAME:-$([[ "$SHOP" == "1" || "$DECISIONS" == "1" || "$STAGE" == "1" || "$CHAPTER" == "1" ]] && echo tactics || echo redline)}
PHONES=${ARCBOX_PHONES:-$([[ "$GAME" == "tactics" ]] && echo 4 || echo 3)}
RES=${TV_RES:-1280x720}
PRESET=${TV_PRESET:-low}
LINGER=${TV_LINGER:-12}
SHOTS=${ARCBOX_SHOTS:-1}
OUT=${ARCBOX_E2E_OUT:-/tmp/arcbox-tv-e2e-$(date +%Y%m%d-%H%M%S)}
FULL=${ARCBOX_FULL:-}
RESTART=${ARCBOX_RESTART:-}
SWAP=${ARCBOX_SWAP:-}
RECONNECT=${ARCBOX_RECONNECT:-}
AT_BEAT=${ARCBOX_AT_BEAT:-30}
ENCOUNTER=${ARCBOX_ENCOUNTER:-river_crossing}
HOLD=${TV_HOLD:-6}
WALK=${ARCBOX_WALK:-}
# Navigate between authored shop targets using route.cs; stop for saves, server restart and TV reconnect.
WALK_SCRIPT=${ARCBOX_WALK_SCRIPT:-route:af_market_herbalist,route:af_inn,route:af_market_herbalist,stop:2,pause:restart,route:af_inn,stop:2,pause:tv,route:af_market_herbalist,stop:2}
WALK_PRESSES=${ARCBOX_WALK_PRESSES:-20}
WALK_STEPS=${ARCBOX_WALK_STEPS:-12}
WALK_SLACK=${ARCBOX_WALK_SLACK:-250}
WALK_LATENCY=${ARCBOX_WALK_LATENCY:-100}
WALK_LANDED=${ARCBOX_WALK_LANDED:-90}
LEASE_MS=750   # WalkTiming.HoldLease
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) WINDOWS=1 ;;
  *) WINDOWS= ;;
esac
BUILD=1
for arg in "$@"; do
  case "$arg" in
    --skip-build) BUILD=0 ;;
    -h|--help) sed -n '/^#/!q;p' "$0" | tail -n +2; exit 0 ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

if [[ -z "$GODOT" || ! -x "$GODOT" ]]; then
  echo "Set GODOT to the Godot 4.7.2 .NET binary (or put godot on PATH)." >&2
  exit 2
fi
for tool in dotnet node curl $([[ -n "$WINDOWS" ]] || echo Xvfb); do
  command -v "$tool" >/dev/null || { echo "$tool is required" >&2; exit 2; }
done
case "$GAME" in
  redline|tactics) ;;
  *) echo "ARCBOX_GAME is redline or tactics, not $GAME." >&2; exit 2 ;;
esac
RESTART_KILL=
case "$RESTART" in
  ""|1) ;;
  0) RESTART= ;;      # off, as it always was
  kill)
    if [[ "$GAME" != "tactics" || "$WALK" == "1" ]]; then
      echo "ARCBOX_RESTART=kill is for a Tactics battle, stage or chapter run, not REDLINE or ARCBOX_WALK=1." >&2
      exit 2
    fi
    RESTART=1 RESTART_KILL=1 ;;
  *) echo "ARCBOX_RESTART is 1 (a clean stop) or kill (a crash), not $RESTART." >&2; exit 2 ;;
esac
if [[ "$RECONNECT" == "1" && "$GAME" != "tactics" ]]; then
  echo "ARCBOX_RECONNECT=1 is for ARCBOX_GAME=tactics." >&2
  exit 2
fi
if [[ "$GAME" == "tactics" && ( "$FULL" == "1" || "$SWAP" == "1" || "${TV_REMATCH:-}" == "1" ) ]]; then
  echo "ARCBOX_FULL, ARCBOX_SWAP and TV_REMATCH are REDLINE's: a Tactics run always goes to the Results." >&2
  exit 2
fi
if [[ "$WALK" == "1" && ( "$GAME" != "tactics" || "$RECONNECT" == "1" || "$RESTART" == "1" ) ]]; then
  echo "ARCBOX_WALK=1 is for ARCBOX_GAME=tactics, and does its own restart and reconnect." >&2
  exit 2
fi
if [[ ( "$STAGE" == "1" || "$DROP_SIGNALS" == "1" ) && ( "$GAME" != "tactics" || "$WALK" == "1" ) ]]; then
  echo "ARCBOX_STAGE=1 and ARCBOX_DROP_SIGNALS=1 are for ARCBOX_GAME=tactics, without ARCBOX_WALK=1." >&2
  exit 2
fi
if [[ "$DROP_SIGNALS" == "1" && "$STAGE" != "1" && "$CHAPTER" != "1" ]]; then
  echo "ARCBOX_DROP_SIGNALS=1 goes with ARCBOX_STAGE=1 or ARCBOX_CHAPTER=1." >&2
  exit 2
fi
if [[ "$SHOTS" != "0" && "$SHOTS" != "1" ]]; then
  echo "ARCBOX_SHOTS is 1 (screenshots, the default) or 0 (none), not $SHOTS." >&2
  exit 2
fi
if [[ "$CHAPTER" == "1" && ( "$GAME" != "tactics" || "$WALK" == "1" || "$STAGE" == "1" ) ]]; then
  echo "ARCBOX_CHAPTER=1 is a Tactics run of its own, without ARCBOX_WALK=1 or ARCBOX_STAGE=1." >&2
  exit 2
fi
if [[ "$CHAPTER" == "1" && "$BRANCH" != "force" && "$BRANCH" != "parley" && "$BRANCH" != "skip-assault" ]]; then
  echo "ARCBOX_BRANCH is force, parley or skip-assault, not $BRANCH." >&2
  exit 2
fi
if [[ "$SILENT" == "1" && ( "$CHAPTER" != "1" || "$BRANCH" != "force" ) ]]; then
  echo "ARCBOX_SILENT=1 goes with ARCBOX_CHAPTER=1, on the force branch: a vote nobody answers takes its first option." >&2
  exit 2
fi
if [[ "$RECONNECT" == "1" && "$RESTART" == "1" && "$CHAPTER" != "1" ]]; then
  echo "ARCBOX_RECONNECT=1 and ARCBOX_RESTART=1 are separate runs." >&2
  exit 2
fi
if [[ "$RESTART" == "1" && ( "$FULL" == "1" || "$SWAP" == "1" ) ]]; then
  echo "ARCBOX_RESTART=1 cuts the race short: run it without ARCBOX_FULL and ARCBOX_SWAP." >&2
  exit 2
fi
if [[ "$SWAP" == "1" ]]; then
  FULL=1   # the swapped phone has to finish the race
  (( PHONES >= 2 )) || { echo "ARCBOX_SWAP=1 needs at least 2 phones." >&2; exit 2; }
fi

mkdir -p "$OUT/tv" "$OUT/phones"
log() { printf '[tv-e2e %s] %s\n' "$(date +%H:%M:%S)" "$*"; }

SERVER_PID=""
XVFB_PID=""
TV_PID=""
PHONES_PID=""
signal_pid() {   # signal_pid <TERM|KILL> <pid>: Windows has no TERM for a program that isn't Git Bash's own, so there it's taskkill, with its children
  if [[ -n "$WINDOWS" ]]; then
    local winpid
    winpid=$(cat "/proc/$2/winpid" 2>/dev/null || true)
    if [[ -z "$winpid" ]] || ! taskkill //F //T //PID "$winpid" > /dev/null 2>&1; then
      log "taskkill failed for pid $2 (Windows pid ${winpid:-unknown}): it may still be running"
    fi
  else
    kill "-$1" "$2" 2>/dev/null || true
  fi
}
stop_pid() {   # stop_pid <pid> <name>: TERM, wait up to 10 s, then KILL
  local pid=$1 name=$2
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null || return 0
  signal_pid TERM "$pid"
  for _ in $(seq 1 20); do
    kill -0 "$pid" 2>/dev/null || { log "$name stopped"; return 0; }
    sleep 0.5
  done
  log "$name did not stop; killing it"
  signal_pid KILL "$pid"
}
OLD_TVS=""   # TVs a kill didn't stop (kill_tv): they're stopped with the rest
OLD_TV_UP=0
# kill_tv: kills the TV outright, as a crash would. One still running 3 s later sets OLD_TV_UP, which fails the run (two TVs on one
# room prove nothing), and is stopped at the end, so it doesn't keep its window open until --quit-after.
kill_tv() {
  signal_pid KILL "$TV_PID"
  sleep 3
  if kill -0 "$TV_PID" 2>/dev/null; then
    log "reconnect: the old TV is still running beside the one about to start"
    OLD_TV_UP=1
    OLD_TVS="$OLD_TVS $TV_PID"
  fi
}
# stop_server_for_restart: a TERM, so the server saves and shuts down cleanly; with ARCBOX_RESTART=kill, a KILL, as a crash would.
stop_server_for_restart() {
  if [[ -z "$RESTART_KILL" ]]; then
    stop_pid "$SERVER_PID" "server"
    return
  fi
  log "restart: killing the server outright"
  signal_pid KILL "$SERVER_PID"
  for _ in $(seq 1 20); do
    kill -0 "$SERVER_PID" 2>/dev/null || return 0
    sleep 0.5
  done
  log "restart: the server is still running after a KILL"
}
cleanup() {
  if [[ -n "$PHONES_PID" ]] && kill -0 "$PHONES_PID" 2>/dev/null; then
    pkill -TERM -P "$PHONES_PID" 2>/dev/null || true   # the phone script and its tee (ARCBOX_RESTART runs them in the background)
    stop_pid "$PHONES_PID" "phones"
  fi
  stop_pid "${SHOTS_PID:-}" "shots"
  local old
  for old in $OLD_TVS; do stop_pid "$old" "old TV"; done
  OLD_TVS=""
  stop_pid "$TV_PID" "TV"
  stop_pid "$XVFB_PID" "Xvfb"
  stop_pid "$SERVER_PID" "server"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ---- 1. build
if (( BUILD )); then
  log "building the phone app"
  (cd "$ROOT/web/play" && { [[ -d node_modules ]] || npm ci --no-audit --no-fund; } && npm run build) > "$OUT/build.log" 2>&1
  log "building the server and the TV host"
  dotnet build "$ROOT/src/ArcBox.Server" >> "$OUT/build.log" 2>&1
  dotnet build "$ROOT/src/ArcBox.Host" >> "$OUT/build.log" 2>&1
fi
# Fonts and the Blender models: import whatever is missing or changed since its last import (a fresh clone, a re-export).
if ! GODOT="$GODOT" "$ROOT/tools/godot-import.sh" --check > /dev/null; then
  log "importing the Godot assets"
  GODOT="$GODOT" "$ROOT/tools/godot-import.sh" >> "$OUT/build.log" 2>&1 || log "import incomplete (the TV falls back where it must)"
fi

# ---- 2. server
PORT=$(node -e 'const s=require("net").createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close()})')
URL="http://127.0.0.1:$PORT"
SERVER_DLL="$ROOT/src/ArcBox.Server/bin/Debug/net10.0/ArcBox.Server.dll"
# ARCBOX_STAGE=1: a Development seed puts a new campaign four steps from the stamp of the encounter's map, with its beat on offer:
# River Crossing after the parley, the Ford Assault after the vote to fight. Region-less encounters aren't staged.
if [[ "$STAGE" == "1" ]]; then
  case "$ENCOUNTER" in
    river_crossing) STAGE_FLAG=ch1_skarrow_spared ;;
    ford_assault) STAGE_FLAG=ch1_skarrow_slain ;;
    *) echo "ARCBOX_STAGE=1 knows river_crossing and ford_assault, Ashen Ford's two fights, not $ENCOUNTER." >&2; exit 2 ;;
  esac
  STAGE_ENV="Tactics__Seed__Region=ashen_ford Tactics__Seed__At__0=96 Tactics__Seed__At__1=61 Tactics__Seed__Flags__0=$STAGE_FLAG"
  STAGE_ENV+=" Tactics__Seed__Beats__0=ch1_mayors_request Tactics__Seed__Beats__1=ch1_the_ford_choice"
fi
# ARCBOX_BRANCH=skip-assault: a new campaign with the Ford Assault done, which the bots play by force, so nothing is left to fight.
VOTE=$BRANCH
if [[ "$CHAPTER" == "1" && "$BRANCH" == "skip-assault" ]]; then
  STAGE_ENV="Tactics__Seed__Beats__0=ch1_ford_assault"
  VOTE=force
fi
# This run's own database, so a restart finds its rooms again (ARCBOX_RESTART) and no run sees another's.
DB="$OUT/arcbox.db"
# ARCBOX_DECISIONS=1: the server and route.cs read a copy of the content in which Ashen Ford has one more thing, an e2e strongbox by
# the ford's campfire (an unseen trap, and a lock whose only way is a DC 30 pick), so a lock fails every time. Everything else is
# linked, not copied; only the interactables file is written.
DECISIONS_ENV=
if [[ "$DECISIONS" == "1" ]]; then
  CONTENT="$OUT/content"
  BASE="$ROOT/content/tactics/base"
  rm -rf "$CONTENT"     # a reused ARCBOX_E2E_OUT: the links are made again
  mkdir -p "$CONTENT/tactics/base/rules/interactables"
  for entry in "$ROOT/content"/*; do [[ "$(basename "$entry")" == "tactics" ]] || ln -s "$entry" "$CONTENT/"; done
  for entry in "$BASE"/*; do [[ "$(basename "$entry")" == "rules" ]] || ln -s "$entry" "$CONTENT/tactics/base/"; done
  for entry in "$BASE/rules"/*; do [[ "$(basename "$entry")" == "interactables" ]] || ln -s "$entry" "$CONTENT/tactics/base/rules/"; done
  for entry in "$BASE/rules/interactables"/*; do [[ "$(basename "$entry")" == "ashen_ford.json" ]] || ln -s "$entry" "$CONTENT/tactics/base/rules/interactables/"; done
  node -e '
    const fs = require("fs");
    const [from, to, at] = process.argv.slice(1);
    const file = JSON.parse(fs.readFileSync(from, "utf8"));
    file.interactables.push({ id: "e2e_strongbox", kind: "chest", at: JSON.parse(at), name: "strongbox",
      trap: { spot: 30, disarm: 30 }, lock: { pick: 30 }, loot: [{ item: "smoke_bomb", count: 1 }] });
    fs.writeFileSync(to, JSON.stringify(file, null, 2) + "\n");
  ' "$BASE/rules/interactables/ashen_ford.json" "$CONTENT/tactics/base/rules/interactables/ashen_ford.json" "${ARCBOX_STRONGBOX_AT:-[117,62]}"
  DECISIONS_ENV="Content__Root=$CONTENT"
  export ARCBOX_CONTENT_RULES="$CONTENT/tactics/base/rules"
fi
# start_server <log>: the server on $URL and $DB, in the background; returns once it answers.
start_server() {
  local out=$1
  (cd "$ROOT/src/ArcBox.Server" && exec env ASPNETCORE_ENVIRONMENT=Development Tactics__BattleList=true Tactics__Difficulty="$DIFFICULTY" ${BATTLE_SEED_ENV:-} Storage__Path="$DB" ${STAGE_ENV:-} ${DECISIONS_ENV:-} ${ARCBOX_SERVER_ENV:-} dotnet "$SERVER_DLL" --urls "$URL") > "$out" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 60); do
    curl -sf -o /dev/null "$URL/" && return 0
    kill -0 "$SERVER_PID" 2>/dev/null || { log "the server exited"; tail -20 "$out"; exit 1; }
    sleep 0.5
  done
  curl -sf -o /dev/null "$URL/" || { log "the server did not come up"; exit 1; }
}
log "starting the server on $URL"
start_server "$OUT/server.log"

# ---- 3. TV
if [[ -z "$WINDOWS" ]]; then
  DISPLAY_NUM=90
  while [[ -e "/tmp/.X${DISPLAY_NUM}-lock" || -e "/tmp/.X11-unix/X${DISPLAY_NUM}" ]]; do DISPLAY_NUM=$((DISPLAY_NUM + 1)); done
  Xvfb ":$DISPLAY_NUM" -screen 0 "${RES}x24" -nolisten tcp > "$OUT/xvfb.log" 2>&1 &
  XVFB_PID=$!
  sleep 1
fi
QUIT_AFTER=$([[ "$FULL" == "1" ]] && echo 2700 || echo 900)
# start_tv <shots dir> <TV app arguments>…: the TV on Xvfb, or on Windows in a real window; it adds to tv.log. With ARCBOX_SHOTS=0
# it takes no screenshots.
start_tv() {
  local shots=(--shots="$1")
  shift
  [[ "$SHOTS" == "0" ]] && shots=()
  if [[ -n "$WINDOWS" ]]; then
    "$GODOT" ${TV_ENGINE_ARGS:-} --path "$ROOT/src/ArcBox.Host" -- "${shots[@]}" --preset="$PRESET" --res="$RES" --server="$URL" \
      --quit-after="$QUIT_AFTER" --audio-log "$@" ${TV_ARGS:-} >> "$OUT/tv.log" 2>&1 &
  else
    DISPLAY=":$DISPLAY_NUM" "$GODOT" ${TV_ENGINE_ARGS:-} --path "$ROOT/src/ArcBox.Host" --rendering-driver "${TV_RENDERING_DRIVER:-vulkan}" -- "${shots[@]}" --preset="$PRESET" --res="$RES" \
      --server="$URL" --quit-after="$QUIT_AFTER" --audio-log "$@" ${TV_ARGS:-} >> "$OUT/tv.log" 2>&1 &
  fi
  TV_PID=$!
}
log "starting the TV ($RES, preset $PRESET)"
: > "$OUT/tv.log"
if [[ "$GAME" == "tactics" ]]; then
  QUIT_AFTER=$([[ "$CHAPTER" == "1" ]] && echo 7200 || echo 1800)
  start_tv "$OUT/tv" --new-room --beat-log $([[ "$WALK" == "1" || "$SHOP" == "1" || "$DECISIONS" == "1" || "$STAGE" == "1" || "$CHAPTER" == "1" ]] && echo --walk-log) \
    $([[ "$CHAPTER" == "1" || "$SHOP" == "1" || "$DECISIONS" == "1" ]] && echo --story-log) $([[ "$DROP_SIGNALS" == "1" ]] && echo --drop-signals)
else
  start_tv "$OUT/tv" --new-room --autostart="redline:$PHONES"
fi

CODE=""
for _ in $(seq 1 360); do
  CODE=$(grep -m1 -oE '^ROOM [A-Z]{4}' "$OUT/tv.log" | cut -d' ' -f2 || true)
  [[ -n "$CODE" ]] && break
  kill -0 "$TV_PID" 2>/dev/null || { log "the TV exited"; tail -30 "$OUT/tv.log"; exit 1; }
  sleep 0.5
done
[[ -n "$CODE" ]] || { log "no ROOM line from the TV after 180 s"; tail -30 "$OUT/tv.log"; exit 1; }
log "TV opened room $CODE"

# ---- Shop: real bot phones walk to the herbalist, buy, retry, sell and leave.
if [[ "$GAME" == "tactics" && "$SHOP" == "1" ]]; then
  log "shop: bots earn the council advance, then trade at Wenna from their own purses and bags"
  node "$ROOT/tools/tactics-playtest/bots.cjs" --server="$URL" --room="$CODE" --players="$PHONES" --shop --hold=1 --line=0.1 --tv-log="$OUT/tv.log" --timeout=300 > "$OUT/phones.log" 2>&1
  grep -q 'SHOP OK' "$OUT/phones.log"
  sleep 2
  if grep -E 'SCRIPT ERROR|Unhandled exception|ERROR: ' "$OUT/tv.log" | grep -vE 'audio|ALSA|PulseAudio'; then
    log "FAILED: TV error"; exit 1
  fi
  log "OK: shop buy, idempotent retry, sell and leave"
  exit 0
fi

# ---- The owner's rule decisions (issue #83): real bot phones and the TV, on the copied pack with its strongbox.
if [[ "$GAME" == "tactics" && "$DECISIONS" == "1" ]]; then
  log "decisions: key ring, trap injury mended at camp, lock retried after the rest, loot votes and a haggle"
  set +e
  node "$ROOT/tools/tactics-playtest/bots.cjs" --server="$URL" --room="$CODE" --players="$PHONES" --decisions --hold=1 --line=0.1 --tv-log="$OUT/tv.log" --timeout=900 > "$OUT/phones.log" 2>&1
  BOTS=$?
  set -e
  grep -E 'DECISIONS|KEYS|LOCK|TRAP|CAMP|RETRY|LOOT|HAGGLE' "$OUT/phones.log" || true
  [[ $BOTS -eq 0 ]] && grep -q 'DECISIONS OK' "$OUT/phones.log" || { log "FAILED: the bots (exit $BOTS)"; tail -30 "$OUT/phones.log"; exit 1; }
  sleep 2
  grep -qE '^STORY [0-9]+ loot_vote\.' "$OUT/tv.log" || { log "FAILED: the TV never showed a loot vote"; exit 1; }
  grep -qE '^STORY [0-9]+ haggle\.' "$OUT/tv.log" || { log "FAILED: the TV never showed the haggle"; exit 1; }
  grep -qE '^SHOP haggle (passed|failed)' "$OUT/tv.log" || { log "FAILED: the TV's shop never showed the haggle's result"; exit 1; }
  if grep -E 'SCRIPT ERROR|Unhandled exception|ERROR: ' "$OUT/tv.log" | grep -vE 'audio|ALSA|PulseAudio'; then
    log "FAILED: TV error"; exit 1
  fi
  log "OK: the owner's rule decisions on the real TV"
  exit 0
fi

# ---- 4w. Tactics walking: the bots walk the world, the server restarts, the TV is killed, and the leader's phone is cut.
if [[ "$GAME" == "tactics" && "$WALK" == "1" ]]; then
  GO="$OUT/go"
  mkdir -p "$GO"
  log "phones: $PHONES bots walk: $WALK_SCRIPT, then $WALK_PRESSES timed presses, then the leader's phone is cut"
  set +e
  : > "$OUT/phones.log"
  node "$ROOT/tools/tactics-playtest/bots.cjs" --server="$URL" --room="$CODE" --players="$PHONES" --hold=1 --timeout=600 \
    --walk="$WALK_SCRIPT" --walk-latency="$WALK_PRESSES" --walk-drop=kill --go="$GO" --tv-log="$OUT/tv.log" > >(tee "$OUT/phones.log") 2>&1 &
  PHONES_PID=$!
  # last_step [<before line>]: the tile of the newest WALK line, as "x y", before a TV log line if given.
  last_step() { awk -v to="${1:-0}" '(to == 0 || NR < to) && /^WALK [0-9]+ / { t = $3 " " $4 } END { print t }' "$OUT/tv.log"; }
  # wait_tv <from line> <awk condition> <seconds>: the first TV log line after <from> that matches, waiting for it; or nothing.
  wait_tv() {
    local n=""
    for _ in $(seq 1 $(( $3 * 5 ))); do
      n=$(awk -v from="$1" -v code="$CODE" "NR > from && ($2) { print NR; exit }" "$OUT/tv.log")
      [[ -n "$n" ]] && break
      sleep 0.2
    done
    echo "$n"
  }
  RESTART_AT=0 SAVED="" KILL_AT=0 KILLED_ON="" OLD_TV_UP=0
  while kill -0 "$PHONES_PID" 2>/dev/null; do
    if (( RESTART_AT == 0 )) && grep -qE '^[0-9:]+ PAUSE restart$' "$OUT/phones.log"; then
      SAVED=$(last_step)
      RESTART_AT=$(wc -l < "$OUT/tv.log")
      log "restart: the party stopped on $SAVED; stopping the server (TV log line $RESTART_AT)"
      stop_pid "$SERVER_PID" "server"
      sleep 2
      start_server "$OUT/server-restarted.log"
      log "restart: the server is back; waiting for the TV to put the party somewhere"
      [[ -n "$(wait_tv "$RESTART_AT" '/^TACTICS walk snapped to /' 90)" ]] || log "restart: the TV didn't snap the party anywhere"
      touch "$GO/restart"
    fi
    if (( KILL_AT == 0 )) && grep -qE '^[0-9:]+ PAUSE tv$' "$OUT/phones.log"; then
      KILLED_ON=$(last_step)
      KILL_AT=$(wc -l < "$OUT/tv.log")
      log "reconnect: the party stopped on $KILLED_ON; killing the TV (log line $KILL_AT)"
      kill_tv
      start_tv "$OUT/tv-relaunched" --beat-log --walk-log
      [[ -n "$(wait_tv "$KILL_AT" '/^TACTICS world open:/' 180)" ]] || log "reconnect: the new TV didn't open the world"
      touch "$GO/tv"
    fi
    sleep 0.2
  done
  wait "$PHONES_PID"
  PHONES_EXIT=$?
  PHONES_PID=""
  set -e
  log "letting the TV finish its shots ($LINGER s)"
  sleep "$LINGER"
  cleanup
  TV_PID="" XVFB_PID="" SERVER_PID=""

  # ---- 5w. the checks
  WALK_FAIL=0
  walk_fail() { log "walk: MISSING $*"; WALK_FAIL=1; }
  after() { awk -v from="$1" -v code="$CODE" "NR > from && ($2) { print NR; exit }" "$OUT/tv.log"; }
  # at_middle <tile "x y"> <place "x y">: the place is the middle of the tile.
  at_middle() { awk -v s="$1" -v t="$2" 'BEGIN { split(s, a, " "); split(t, b, " "); dx = b[1] - a[1] - 0.5; dy = b[2] - a[2] - 0.5
    exit !(dx * dx < 0.0025 && dy * dy < 0.0025) }'; }
  ERRORS=$(awk '
    pending != "" { if (tolower($0) !~ /^ *at: .*(alsa|audio|pulse)/) print pending; pending = "" }
    /^ *\[[0-9]+\] / { next }
    tolower($0) ~ /exception|error/ && tolower($0) !~ /alsa|audio|pulse/ { pending = $0 }
    END { if (pending != "") print pending }' "$OUT/tv.log")
  if [[ -n "$ERRORS" ]]; then
    log "errors in the TV log:"
    echo "$ERRORS"
  fi
  END_LINE=$(( $(wc -l < "$OUT/tv.log") + 1 ))
  (( RESTART_AT > 0 )) || { walk_fail "PAUSE restart from the bots"; RESTART_AT=$END_LINE; }
  (( KILL_AT > 0 )) || { walk_fail "PAUSE tv from the bots"; KILL_AT=$END_LINE; }

  # Seq climbs: the WALK lines between one interruption and the next only go up, and there are enough before the first.
  BEFORE=$(awk -v to="$RESTART_AT" 'NR < to && /^WALK [0-9]+ / { n++ } END { print n + 0 }' "$OUT/tv.log")
  log "the TV was told of $BEFORE steps before the restart, $(grep -c '^WALK [0-9]' "$OUT/tv.log" || true) in all"
  (( BEFORE > WALK_STEPS )) || walk_fail "more than $WALK_STEPS steps on the TV before the restart"
  DOWN=$(awk -v a="$RESTART_AT" -v b="$KILL_AT" '
    NR == a || NR == b { last = 0 }
    /^WALK [0-9]+ / { if ($2 <= last) print NR ": " $0 " after " last; last = $2 }' "$OUT/tv.log")
  [[ -z "$DOWN" ]] || walk_fail "WALK lines that only climb; these didn't: $DOWN"
  # --walk moved the party, as the TV drew it.
  MOVED=$(awk -v to="$RESTART_AT" '
    NR < to && /^TACTICS walk snapped to / && !origin { x = $5; y = $6; origin = 1 }
    NR < to && /^WALKAT / { if (!origin) { x = $5; y = $6; origin = 1 } lx = $5; ly = $6; drawn = 1 }
    END { if (!drawn) { print 0; exit } dx = lx - x; dy = ly - y; if (dx < 0) dx = -dx; if (dy < 0) dy = -dy; print (dx > dy ? dx : dy) + 0 }' "$OUT/tv.log")
  log "the TV drew the leader $MOVED tiles from where it started, before the restart"
  awk -v m="$MOVED" 'BEGIN { exit !(m >= 4) }' || walk_fail "the leader drawn 4 tiles or more from the start"

  # The restart: the TV back in the room, and the party where it was saved.
  if [[ -n "$SAVED" ]] && (( RESTART_AT < END_LINE )); then
    [[ -n "$(after "$RESTART_AT" '$0 == "ROOM " code')" ]] && log "restart: the TV is back in room $CODE" || walk_fail "ROOM $CODE from the TV after the restart"
    SNAP=$(after "$RESTART_AT" '/^TACTICS walk snapped to /')
    if [[ -z "$SNAP" ]]; then
      walk_fail "TACTICS walk snapped to, after the restart"
    else
      AT=$(sed -n "${SNAP}p" "$OUT/tv.log" | awk '{ print $5 " " $6 }')
      log "restart: saved on $SAVED; the TV put the party on $AT"
      at_middle "$SAVED" "$AT" || walk_fail "the party on the middle of the saved tile $SAVED after the restart (it was on $AT)"
      FIRST=$(after "$SNAP" '/^WALK [0-9]+ /')
      NEXT=""
      [[ -z "$FIRST" ]] || NEXT=$(sed -n "${FIRST}p" "$OUT/tv.log" | awk '{ print $3 " " $4 }')
      log "restart: the first step after it went to ${NEXT:-nowhere}"
      [[ -n "$NEXT" ]] && awk -v s="$SAVED" -v n="$NEXT" 'BEGIN { split(s, a, " "); split(n, b, " "); dx = a[1] - b[1]; dy = a[2] - b[2]
        exit !(dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1 && (dx != 0 || dy != 0)) }' \
        || walk_fail "the first step after the restart beside the saved tile $SAVED (it went to ${NEXT:-nowhere})"
    fi
  fi

  # The killed TV: back in the room, snapped to where the party stood, and walking again.
  if [[ -n "$KILLED_ON" ]] && (( KILL_AT < END_LINE )); then
    (( OLD_TV_UP == 0 )) || walk_fail "the old TV gone: it was still running when the new one started"
    [[ -n "$(after "$KILL_AT" '$0 == "ROOM " code')" ]] && log "reconnect: the new TV is in room $CODE" || walk_fail "ROOM $CODE from the new TV"
    SNAP=$(after "$KILL_AT" '/^TACTICS walk snapped to /')
    if [[ -z "$SNAP" ]]; then
      walk_fail "TACTICS walk snapped to, from the new TV"
    else
      AT=$(sed -n "${SNAP}p" "$OUT/tv.log" | awk '{ print $5 " " $6 }')
      log "reconnect: the party stood on $KILLED_ON; the new TV snapped to $AT"
      at_middle "$KILLED_ON" "$AT" || walk_fail "the new TV's snap to the middle of $KILLED_ON (it was $AT)"
      [[ -n "$(after "$SNAP" '/^WALK [0-9]+ /')" ]] || walk_fail "a step on the new TV"
    fi
  fi

  # The dropped phone.
  CUT=$({ grep -oE 'DROP kill at [0-9]+' "$OUT/phones.log" || true; } | awk '{ print $4 }' | tail -1)
  if [[ -z "$CUT" ]]; then
    walk_fail "DROP kill from the bots"
  else
    LAST=$(awk -v cut="$CUT" '/^WALK [0-9]+ / && $5 > cut - 2000 { t = $5 } END { print t == "" ? "none" : t - cut }' "$OUT/tv.log")
    STOPPED=$(awk -v cut="$CUT" '/^WALKAT / && $2 > cut && $4 == 0 { print $2 - cut; exit }' "$OUT/tv.log")
    log "drop: the last step reached the TV $LAST ms after the cut; the TV heard the party had stopped ${STOPPED:-never} ms after it"
    [[ "$LAST" =~ ^-?[0-9]+$ ]] && (( LAST <= LEASE_MS + WALK_SLACK )) || walk_fail "no step later than the lease after the cut ($LAST ms > $LEASE_MS + $WALK_SLACK)"
    [[ -n "$STOPPED" ]] || walk_fail "a WALKAT line saying the party stopped after the cut"
    { grep -E 'DROP (party stopped|lead passed|disconnect) after' "$OUT/phones.log" || true; } | sed 's/^[0-9:]* //' | while read -r line; do log "drop: $line"; done
    grep -qE 'DROP disconnect after' "$OUT/phones.log" || walk_fail "the leader's disconnect, seen from another phone"
  fi

  # The press-to-TV time.
  LATENCY=$({ grep -E 'LATENCY [0-9]+ of' "$OUT/phones.log" || true; } | tail -1 | sed 's/^[0-9:]* //')
  if [[ -z "$LATENCY" ]]; then
    walk_fail "a LATENCY line from the bots"
  else
    log "latency: $LATENCY"
    MEDIAN=$(echo "$LATENCY" | sed -E 's/.*median (-?[0-9]+).*/\1/')
    [[ "$MEDIAN" =~ ^-?[0-9]+$ ]] && (( MEDIAN <= WALK_LATENCY )) || walk_fail "a median press-to-TV time of $WALK_LATENCY ms or less (it was $MEDIAN)"
    # The median is of the presses that landed: a press that never reached the TV has no time, so count them too.
    read -r LANDED PRESSED < <(echo "$LATENCY" | sed -E 's/^LATENCY ([0-9]+) of ([0-9]+) presses.*/\1 \2/')
    [[ "$LANDED" =~ ^[0-9]+$ && "$PRESSED" =~ ^[0-9]+$ ]] && (( PRESSED > 0 && LANDED * 100 >= PRESSED * WALK_LANDED )) ||
      walk_fail "at least $WALK_LANDED% of the timed presses reaching the TV (${LANDED:-?} of ${PRESSED:-?} did)"
  fi
  WALKSTATS=$({ grep -E '^WALKSTATS' "$OUT/tv.log" || true; } | awk '{ print $6 }' | sort -n | awk '{ a[NR] = $1 } END { if (NR) print a[int((NR + 1) / 2)] }')
  [[ -n "$WALKSTATS" ]] && log "the TV's frames while walking: median of the walks' median frame times $WALKSTATS ms"

  for moment in walking stopped; do
    [[ "$SHOTS" == "0" ]] && break
    SHOT=$(find "$OUT" -name "*tactics-world-$moment-*.png" | sort | tail -1)
    [[ -n "$SHOT" ]] && log "a shot of the party $moment: $SHOT" || walk_fail "a screenshot of the party $moment"
  done
  [[ -n "$(after 0 '/^AUDIO Music /')" ]] || walk_fail "an AUDIO Music line"

  if (( PHONES_EXIT != 0 )) || [[ -n "$ERRORS" ]] || (( WALK_FAIL )); then
    log "FAILED (phones exit $PHONES_EXIT)"
    exit 1
  fi
  log "OK"
  exit 0
fi

# ---- 4c. All of Chapter 1: the bots walk from the spawn to each "!", play every scene and fight, to the card and a walk after it.
if [[ "$GAME" == "tactics" && "$CHAPTER" == "1" ]]; then
  log "chapter: $PHONES bots play Chapter 1 by $BRANCH$([[ "$SILENT" == "1" ]] && echo ", answering nothing")$([[ "$DROP_SIGNALS" == "1" ]] && echo ", the TV sends no signals")$([[ "$RESTART" == "1" ]] && echo ", the server restarts at line $AT_LINE of $RESTART_SCENE")$([[ "$RECONNECT" == "1" ]] && echo ", the TV is killed at line $AT_LINE of $RECONNECT_SCENE")"
  SHOTS_PID=""
  if [[ -z "$WINDOWS" && "$SHOTS" != "0" ]] && command -v import > /dev/null; then
    node "$ROOT/tools/tactics-playtest/chapter.cjs" shots "$OUT/tv.log" ":$DISPLAY_NUM" "$OUT/chapter" > "$OUT/shots.log" 2>&1 &
    SHOTS_PID=$!
  elif [[ -z "$WINDOWS" && "$SHOTS" != "0" ]]; then
    log "chapter: no ImageMagick import on PATH, so no shots, and their checks are skipped"
  fi
  set +e
  : > "$OUT/phones.log"
  node "$ROOT/tools/tactics-playtest/bots.cjs" --server="$URL" --room="$CODE" --players="$PHONES" --chapter $([[ "$QUESTS" == "1" ]] && echo --quests) $([[ "$CAMP" == "1" ]] && echo --camp) --branch="$VOTE" \
    --line="$LINE_S" ${ARCBOX_BOT_ARGS:---fight} $([[ "$SILENT" == "1" ]] && echo --silent) --hold="$HOLD" --timeout=1800 \
    --tv-log="$OUT/tv.log" > >(tee "$OUT/phones.log") 2>&1 &
  PHONES_PID=$!
  said_in() { grep -cE "^STORY [0-9]+ $1_[a-z0-9_]+ said" "$OUT/tv.log" || true; }
  RESTART_AT=0 KILL_AT=0 OLD_TV_UP=0
  while kill -0 "$PHONES_PID" 2>/dev/null; do
    if [[ "$RESTART" == "1" ]] && (( RESTART_AT == 0 )) && (( $(said_in "$RESTART_SCENE") >= AT_LINE )); then
      RESTART_AT=$(wc -l < "$OUT/tv.log")
      log "restart: stopping the server at line $AT_LINE of $RESTART_SCENE, mid-scene (TV log line $RESTART_AT)"
      stop_server_for_restart
      sleep 2
      start_server "$OUT/server-restarted.log"
      log "restart: the server is back"
    fi
    if [[ "$RECONNECT" == "1" ]] && (( KILL_AT == 0 )) && (( $(said_in "$RECONNECT_SCENE") >= AT_LINE )); then
      KILL_AT=$(wc -l < "$OUT/tv.log")
      log "reconnect: killing the TV at line $AT_LINE of $RECONNECT_SCENE, mid-scene (log line $KILL_AT)"
      kill_tv
      start_tv "$OUT/tv-relaunched" --beat-log --walk-log --story-log $([[ "$DROP_SIGNALS" == "1" ]] && echo --drop-signals)
    fi
    sleep 0.2
  done
  wait "$PHONES_PID"
  PHONES_EXIT=$?
  PHONES_PID=""
  set -e
  log "letting the TV finish its shots ($LINGER s)"
  sleep "$LINGER"
  cleanup
  TV_PID="" XVFB_PID="" SERVER_PID=""

  # ---- 5c. the checks
  CHAPTER_FAIL=0
  chapter_fail() { log "chapter: MISSING $*"; CHAPTER_FAIL=1; }
  after() { awk -v from="$1" -v code="$CODE" "NR > from && ($2) { print NR; exit }" "$OUT/tv.log"; }
  line_of() { sed -n "${1}p" "$OUT/tv.log"; }
  ERRORS=$(awk '
    pending != "" { if (tolower($0) !~ /^ *at: .*(alsa|audio|pulse)/) print pending; pending = "" }
    /^ *\[[0-9]+\] / { next }
    tolower($0) ~ /exception|error/ && tolower($0) !~ /alsa|audio|pulse/ { pending = $0 }
    END { if (pending != "") print pending }' "$OUT/tv.log")
  if [[ -n "$ERRORS" ]]; then
    log "errors in the TV log:"
    echo "$ERRORS"
  fi
  if [[ "$QUESTS" == "1" ]]; then
    grep -q 'QUEST OK:' "$OUT/phones.log" || chapter_fail "grain quest completed and unpinned"
    grep -q 'QUEST journal sq_missing_grain' "$OUT/tv.log" || chapter_fail "journal shown on TV"
    grep -q 'QUEST tracker sq_missing_grain' "$OUT/tv.log" || chapter_fail "tracker shown on TV"
  fi
  CHECK_ARGS=()
  (( KILL_AT > 0 )) && CHECK_ARGS+=(--relaunched-at="$(after "$KILL_AT" '/^Godot Engine /')")
  (( RESTART_AT > 0 )) && CHECK_ARGS+=(--restarted-at="$RESTART_AT")
  CHECKED=$(node "$ROOT/tools/tactics-playtest/chapter.cjs" check "$OUT/tv.log" "$BRANCH" "${CHECK_ARGS[@]}") || CHAPTER_FAIL=1
  while read -r line; do log "chapter: $line"; done <<< "$CHECKED"
  OUTCOMES=$(grep -oE '^[0-9:]+ (Won|Lost|Fled) at .*:' "$OUT/phones.log" | sed -E 's/^[0-9:]+ //; s/:$//' || true)
  log "chapter: the fights: $(paste -sd';' <<< "$OUTCOMES" | sed 's/;/; /g')"
  if [[ "$BRANCH" == "skip-assault" ]]; then
    [[ -z "$OUTCOMES" ]] || chapter_fail "no fight (the assault was done)"
  else
    [[ "$(tail -1 <<< "$OUTCOMES")" == Won\ at\ * ]] || chapter_fail "the last fight won"
  fi
  log "chapter: $(grep -cE 'CHAPTER [0-9]+: ' "$OUT/phones.log" || true) walks to a \"!\", $(grep -c 'routing again' "$OUT/phones.log" || true) routed again; $(grep -E 'CHAPTER walking on' "$OUT/phones.log" | sed 's/^[0-9:]* //' || true)"

  # The restart: the TV back in the room, and the scene on from its saved line, which it showed again.
  if [[ "$RESTART" == "1" ]]; then
    if (( RESTART_AT == 0 )); then
      chapter_fail "a restart at line $AT_LINE of $RESTART_SCENE"
    else
      BEFORE=$(awk -v to="$RESTART_AT" 'NR <= to && /^STORY [0-9]+ [a-z]/ { l = $3 } END { print l }' "$OUT/tv.log")
      BACK=$(after "$RESTART_AT" '$0 == "ROOM " code')
      FIRST=$(after "${BACK:-$RESTART_AT}" '/^STORY [0-9]+ [a-z]/')
      GOT=$([[ -n "$FIRST" ]] && line_of "$FIRST" | awk '{ print $3 " " $4 }' || true)
      # The saved line is the last the TV showed, or the next: the server can move on to it, and save it, before the TV hears.
      NEXT=$(awk '/^STORY [0-9]+ [a-z]/ && $3 != l { l = $3; print l }' "$OUT/tv.log" | awk -v b="$BEFORE" 'p { print; exit } $0 == b { p = 1 }')
      log "restart: the TV showed $BEFORE before it, was back at line ${BACK:-never}, and then showed ${GOT:-nothing}"
      [[ -n "$BACK" ]] || chapter_fail "ROOM $CODE from the TV after the restart"
      [[ "${GOT%% *}" == "$BEFORE" || "${GOT%% *}" == "$NEXT" ]] \
        || chapter_fail "the saved line first after the restart: $BEFORE or the one after it, $NEXT (it was ${GOT:-nothing})"
      [[ -z "$(after "$RESTART_AT" '$0 == "TACTICS screen tactics-resuming"')" ]] || log "restart: the TV showed Resuming"
    fi
  fi
  # The killed TV: the new one in the room, and on with the scene.
  if [[ "$RECONNECT" == "1" ]]; then
    if (( KILL_AT == 0 )); then
      chapter_fail "a TV kill at line $AT_LINE of $RECONNECT_SCENE"
    else
      (( OLD_TV_UP == 0 )) || chapter_fail "the old TV gone: it was still running when the new one started"
      BACK=$(after "$KILL_AT" '$0 == "ROOM " code')
      BORN=$(after "$KILL_AT" '/^Godot Engine /')
      FIRST=$(after "${BORN:-$KILL_AT}" '/^STORY [0-9]+ [a-z]/')
      log "reconnect: the new TV was in room $CODE at line ${BACK:-never}; its first line: $([[ -n "$FIRST" ]] && line_of "$FIRST")"
      [[ -n "$BACK" && -n "$FIRST" ]] || chapter_fail "ROOM $CODE and a line of the scene from the new TV"
      [[ -z "$FIRST" || "$(line_of "$FIRST" | awk '{ print $4 }')" == "shown" ]] || chapter_fail "the new TV's first line only shown, not said"
    fi
  fi
  if [[ "$DROP_SIGNALS" == "1" ]]; then
    DROPPED=$(grep -c "^Host signal 'shown' dropped" "$OUT/tv.log" || true)
    (( DROPPED > 0 )) && log "chapter: the TV dropped $DROPPED shown signals, and the fight went on without them" || chapter_fail "dropped shown signals in the TV log"
  fi
  if [[ "$SILENT" == "1" ]]; then
    ANSWERED=$(grep -cE ' (skips line|votes|picks .* to roll) ' "$OUT/phones.log" || true)
    (( ANSWERED == 0 )) && log "chapter: nobody skipped, voted or picked: the game's timers ran every scene" \
      || chapter_fail "a silent run: the bots answered $ANSWERED times"
  fi
  # The shots, when there were any to take (ARCBOX_SHOTS=0, Windows or no ImageMagick: none).
  if [[ -n "$SHOTS_PID" ]]; then
    for shot in chapter-card walking-after; do
      SHOT=$({ find "$OUT/chapter" -name "*$shot.png" 2> /dev/null || true; } | sort | tail -1)
      [[ -n "$SHOT" ]] && log "a shot of the $shot: $SHOT" || chapter_fail "a shot of the $shot"
    done
    log "chapter: $({ find "$OUT/chapter" -name '*.png' 2> /dev/null || true; } | wc -l) shots in $OUT/chapter"
  fi

  if (( PHONES_EXIT != 0 )) || [[ -n "$ERRORS" ]] || (( CHAPTER_FAIL )); then
    log "FAILED (phones exit $PHONES_EXIT)"
    exit 1
  fi
  log "OK"
  exit 0
fi

# ---- 4s. Tactics in the world: the bots walk into the stamp, lose, wake at the recovery spawn, walk back in and fight it again.
if [[ "$GAME" == "tactics" && "$STAGE" == "1" ]]; then
  log "stage: $PHONES bots walk into $ENCOUNTER's stamp twice, losing the first fight$([[ "$RECONNECT" == "1" ]] && echo ", the TV is killed at beat $AT_BEAT")$([[ "$RESTART" == "1" ]] && echo ", the server restarts at beat $AT_BEAT")$([[ "$DROP_SIGNALS" == "1" ]] && echo ", the TV sends no signals")"
  MAP=$(node -e 'console.log(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).map)' "$ROOT/content/tactics/base/rules/encounters/$ENCOUNTER.json")
  set +e
  : > "$OUT/phones.log"
  node "$ROOT/tools/tactics-playtest/bots.cjs" --server="$URL" --room="$CODE" --players="$PHONES" --encounter="$ENCOUNTER" \
    --walk-to-stamp=2 --lose=1 ${ARCBOX_BOT_ARGS:---fight} --hold="$HOLD" --timeout=1500 --tv-log="$OUT/tv.log" > >(tee "$OUT/phones.log") 2>&1 &
  PHONES_PID=$!
  # The first fight's highest beat, and 1 once its results are up. Each battle numbers its beats from 1, so they're counted from the
  # first battle screen to the results after it: a first fight that ends early can't hand the kill or restart to the second.
  first_fight() { awk -v screen="TACTICS screen tactics-battle-$MAP" '
    !start && index($0, screen) == 1 { start = NR }
    start && $0 == "TACTICS screen tactics-results" { over = 1; exit }
    start && /^BEAT [0-9]+ / && $2 > n { n = $2 }
    END { print n + 0, over + 0 }' "$OUT/tv.log"; }
  MARK=0 LAST_BEAT=0 OLD_TV_UP=0 FIRST_OVER=0
  if [[ "$RECONNECT" == "1" || "$RESTART" == "1" ]]; then
    while kill -0 "$PHONES_PID" 2>/dev/null; do
      read -r LAST_BEAT FIRST_OVER < <(first_fight)
      (( LAST_BEAT < AT_BEAT && FIRST_OVER == 0 )) || break
      sleep 0.2
    done
    MARK=$(wc -l < "$OUT/tv.log")
    read -r LAST_BEAT FIRST_OVER < <(first_fight)
    if (( LAST_BEAT < AT_BEAT )); then
      log "stage: the first fight ended at beat $LAST_BEAT, before beat $AT_BEAT: no $([[ "$RECONNECT" == "1" ]] && echo kill || echo restart)"
    elif [[ "$RECONNECT" == "1" ]]; then
      log "reconnect: killing the TV at beat $LAST_BEAT, mid-fight (log line $MARK)"
      kill_tv
      start_tv "$OUT/tv-relaunched" --beat-log --walk-log $([[ "$DROP_SIGNALS" == "1" ]] && echo --drop-signals)
    else
      log "restart: stopping the server at beat $LAST_BEAT, mid-fight (log line $MARK)"
      stop_server_for_restart
      sleep 2
      start_server "$OUT/server-restarted.log"
      log "restart: the server is back"
    fi
  fi
  wait "$PHONES_PID"
  PHONES_EXIT=$?
  PHONES_PID=""
  set -e
  log "letting the TV finish its shots ($LINGER s)"
  sleep "$LINGER"
  cleanup
  TV_PID="" XVFB_PID="" SERVER_PID=""

  # ---- 5s. the checks
  STAGE_FAIL=0
  stage_fail() { log "stage: MISSING $*"; STAGE_FAIL=1; }
  after() { awk -v from="$1" -v code="$CODE" "NR > from && ($2) { print NR; exit }" "$OUT/tv.log"; }
  line_of() { sed -n "${1}p" "$OUT/tv.log"; }
  ERRORS=$(awk '
    pending != "" { if (tolower($0) !~ /^ *at: .*(alsa|audio|pulse)/) print pending; pending = "" }
    /^ *\[[0-9]+\] / { next }
    tolower($0) ~ /exception|error/ && tolower($0) !~ /alsa|audio|pulse/ { pending = $0 }
    END { if (pending != "") print pending }' "$OUT/tv.log")
  if [[ -n "$ERRORS" ]]; then
    log "errors in the TV log:"
    echo "$ERRORS"
  fi
  RECOVERY=$(node -e 'const fs = require("fs"), path = require("path"); const dir = process.argv[1];
    for (const f of fs.readdirSync(dir)) { const r = JSON.parse(fs.readFileSync(path.join(dir, f), "utf8"));
      if ((r.stamps || []).some((s) => s.map === process.argv[2])) { console.log(r.spawns.recovery.join(" ")); break; } }' \
    "$ROOT/content/tactics/base/rules/world" "$MAP")
  OUTCOMES=$(grep -oE '^[0-9:]+ (Won|Lost|Fled) at .*:' "$OUT/phones.log" | sed -E 's/^[0-9:]+ //; s/:$//' || true)
  log "stage: the fights: $(paste -sd';' <<< "$OUTCOMES" | sed 's/;/; /g')"

  # Each walk-in: the bots' step into the stamp started a fight, and the TV drew it in place.
  STARTS=$(grep -c 'STAMP the fight starts' "$OUT/phones.log" || true)
  (( STARTS == 2 )) && log "stage: both walk-ins started a fight" || stage_fail "two walk-ins that start a fight (the bots logged $STARTS)"
  # The beat's lead-in scene plays before the first walk-in's fight only (a retry skips it); the bots skip its lines and answer it.
  log "stage: scenes before a fight: $(grep -c 'STAMP a scene starts' "$OUT/phones.log" || true); lines skipped: $(grep -c ' skips line ' "$OUT/phones.log" || true)"
  FIGHTS=$(grep -nE "^TACTICS screen tactics-battle-$MAP(-|$)" "$OUT/tv.log" | cut -d: -f1 | paste -sd' ' || true)
  INPLACE=$(grep -c '^TACTICS stage Forming in place$' "$OUT/tv.log" || true)
  log "stage: the TV showed the battle at lines ${FIGHTS:-none}, and walked in in place $INPLACE times"
  (( INPLACE >= 2 )) || stage_fail "TACTICS stage Forming in place for each fight (it was $INPLACE)"

  # The first fight was lost, and the party woke at the recovery spawn.
  FIRST=$(sed -n 1p <<< "$OUTCOMES")
  SECOND=$(sed -n 2p <<< "$OUTCOMES")
  [[ "$FIRST" == Lost\ at\ * ]] || stage_fail "the first fight lost, with --lose=1 (it was ${FIRST:-nothing})"
  RESULTS1=$(after "$MARK" '$0 == "TACTICS screen tactics-results"')
  [[ -n "$RESULTS1" ]] || RESULTS1=$(after 0 '$0 == "TACTICS screen tactics-results"')
  WOKE=$(after "${RESULTS1:-0}" '/^TACTICS stage Waking/')
  SNAP=$(after "${WOKE:-0}" '/^TACTICS walk snapped to /')
  WANT=$(awk -v t="$RECOVERY" 'BEGIN { split(t, a, " "); printf "%.1f %.1f", a[1] + 0.5, a[2] + 0.5 }')
  if [[ -n "$WOKE" && -n "$SNAP" ]]; then
    GOT=$(line_of "$SNAP" | awk '{ print $5 " " $6 }')
    log "stage: woke (line $WOKE), and snapped to $GOT; the recovery spawn is $RECOVERY"
    [[ "$GOT" == "$WANT" ]] || stage_fail "the party woken on the recovery spawn's middle, $WANT (it was $GOT)"
  else
    stage_fail "TACTICS stage Waking after the first results, then a snap to the recovery spawn"
  fi

  # The second walk-in played the same encounter, in place, after the wake.
  SECOND_FIGHT=$(after "${SNAP:-0}" "index(\$0, \"TACTICS screen tactics-battle-$MAP\") == 1")     # with a moment after it, or not
  [[ -n "$SECOND_FIGHT" ]] || stage_fail "a second battle on $MAP after the wake"
  [[ -n "$SECOND" && "${SECOND#* at }" == "${FIRST#* at }" ]] && log "stage: the second fight was the same encounter: $SECOND" \
    || stage_fail "the same encounter twice (first ${FIRST:-nothing}, then ${SECOND:-nothing})"
  if [[ -n "$SECOND_FIGHT" ]]; then
    n=$(after "$SECOND_FIGHT" '/^TACTICS stage Forming/')
    [[ -n "$n" && "$(line_of "$n")" == "TACTICS stage Forming in place" ]] || stage_fail "the second fight walked in in place"
  fi

  # The second fight to its end, won or lost (the bots' retry after a loss is about a coin toss): the music at its results, and
  # after a win, the fall-in and walking again.
  RESULTS2=$(after "${SECOND_FIGHT:-0}" '$0 == "TACTICS screen tactics-results"')
  if [[ -z "$RESULTS2" ]]; then
    stage_fail "the second fight's results on the TV"
  else
    ENDING=$(awk -v to="$RESULTS2" 'NR < to && /^AUDIO Music / { m = $3 } END { print m }' "$OUT/tv.log")
    log "stage: music at the second results: $ENDING"
    case "$SECOND" in
      Won\ *)
        [[ "$ENDING" == "victory" ]] || stage_fail "the victory music at a win (it was $ENDING)"
        FELL=$(after "$RESULTS2" '/^TACTICS stage FallingIn/')
        WALKED=$(after "${FELL:-$RESULTS2}" '/^TACTICS stage Walking/')
        STEP=$(after "${WALKED:-$RESULTS2}" '/^WALK [0-9]+ /')
        [[ -n "$FELL" && -n "$WALKED" && -n "$STEP" ]] && log "stage: fell in (line $FELL), walking (line $WALKED), and a step: $(line_of "$STEP")" \
          || stage_fail "the fall-in after the win, then walking and a step"
        ;;
      Lost\ *)
        log "stage: the second fight was lost; a retry is often lost, and the run doesn't need the win"
        [[ "$ENDING" == "defeat" ]] || stage_fail "the defeat music at a loss (it was $ENDING)"
        ;;
      *) stage_fail "the second fight won or lost (it was ${SECOND:-nothing})" ;;
    esac
  fi

  # The TV killed mid-fight: back in the room, a catch-up, and no beat it had played before.
  if [[ "$RECONNECT" == "1" ]]; then
    (( LAST_BEAT >= AT_BEAT )) || stage_fail "beat $AT_BEAT before the first fight ended"
    (( OLD_TV_UP == 0 )) || stage_fail "the old TV gone: it was still running when the new one started"
    BACK=$(after "$MARK" '$0 == "ROOM " code')
    CAUGHT=$(after "${BACK:-$MARK}" '/^TACTICS catch-up to beat /')
    BEAT=$(after "${BACK:-$MARK}" '/^BEAT [0-9]+ /')
    if [[ -z "$BACK" || -z "$CAUGHT" || -z "$BEAT" ]]; then
      stage_fail "ROOM $CODE, a catch-up and a beat from the relaunched TV"
    else
      WANT_BEAT=$(line_of "$CAUGHT" | awk '{ sub(",", "", $5); print $5 }')
      GOT_BEAT=$(line_of "$BEAT" | awk '{ print $2 }')
      log "reconnect: $(line_of "$CAUGHT") · then beat $GOT_BEAT; beat $LAST_BEAT was the last before it died"
      [[ "$GOT_BEAT" == "$WANT_BEAT" ]] || stage_fail "the catch-up's beat first (it said $WANT_BEAT and played $GOT_BEAT)"
      (( GOT_BEAT > LAST_BEAT )) || stage_fail "a beat newer than $LAST_BEAT first: an old beat came back ($GOT_BEAT)"
      n=$(after "$BACK" '/^TACTICS stage /')
      log "reconnect: the new TV's first stage: $(line_of "${n:-0}")"
    fi
  fi

  # The server restarted mid-fight: the TV back in the room and on from a newer beat, as ARCBOX_RESTART=1 checks a plain fight.
  if [[ "$RESTART" == "1" ]]; then
    (( LAST_BEAT >= AT_BEAT )) || stage_fail "beat $AT_BEAT before the first fight ended"
    RESUMING=$(after "$MARK" '$0 == "TACTICS screen tactics-resuming"')
    BACK=$(awk -v from="$MARK" -v to="${RESULTS1:-0}" -v code="$CODE" 'NR > from && (to == 0 || NR < to) && $0 == "ROOM " code { n = NR } END { print n + 0 }' "$OUT/tv.log")
    BEFORE_BACK=$(awk -v to="$BACK" 'NR < to && /^BEAT [0-9]+ / && $2 > n { n = $2 } END { print n + 0 }' "$OUT/tv.log")
    n=$(after "$BACK" '/^BEAT [0-9]+ /')
    GOT_BEAT=$([[ -n "$n" ]] && line_of "$n" | awk '{ print $2 }' || true)
    if (( BACK > 0 )) && [[ -n "$GOT_BEAT" ]] && (( GOT_BEAT > BEFORE_BACK )); then
      log "restart: $([[ -n "$RESUMING" ]] && echo "Resuming at line $RESUMING; ")the TV was back at line $BACK and played on from beat $GOT_BEAT, after $BEFORE_BACK"
    else
      stage_fail "the TV back in room $CODE after the restart, and on from a newer beat than $BEFORE_BACK"
    fi
  fi

  [[ -n "$(after 0 '/^AUDIO Music /')" ]] || stage_fail "an AUDIO Music line"
  if [[ "$DROP_SIGNALS" == "1" ]]; then
    DROPPED=$(grep -c "^Host signal 'shown' dropped" "$OUT/tv.log" || true)
    (( DROPPED > 0 )) && log "stage: the TV dropped $DROPPED shown signals, and the fights went on without them" || stage_fail "dropped shown signals in the TV log"
  fi
  for shot in stage-forming stage-fighting stage-results tactics-results; do
    [[ "$SHOTS" == "0" ]] && break
    SHOT=$(find "$OUT" -name "*$shot*.png" | sort | tail -1)
    [[ -n "$SHOT" ]] && log "a shot of $shot: $SHOT" || stage_fail "a screenshot of $shot"
  done

  if (( PHONES_EXIT != 0 )) || [[ -n "$ERRORS" ]] || (( STAGE_FAIL )); then
    log "FAILED (phones exit $PHONES_EXIT)"
    exit 1
  fi
  log "OK"
  exit 0
fi

# ---- 4t. Tactics: bot phones fight it out, and the TV or the server goes away part way through.
if [[ "$GAME" == "tactics" ]]; then
  log "phones: $PHONES bots on $ENCOUNTER$([[ "$RECONNECT" == "1" ]] && echo ", the TV is killed at beat $AT_BEAT")$([[ "$RESTART" == "1" ]] && echo ", the server restarts at beat $AT_BEAT")"
  set +e
  run_bots() {
    node "$ROOT/tools/tactics-playtest/bots.cjs" --server="$URL" --room="$CODE" --players="$PHONES" --encounter="$ENCOUNTER" \
      ${ARCBOX_BOT_ARGS:---fight} --hold="$HOLD" --timeout=1500 --tv-log="$OUT/tv.log" 2>&1 | tee "$OUT/phones.log"
    return "${PIPESTATUS[0]}"
  }
  beats_played() { awk '/^BEAT [0-9]+ / && $2 > n { n = $2 } END { print n + 0 }' "$OUT/tv.log"; }
  : > "$OUT/phones.log"
  run_bots &
  PHONES_PID=$!
  MARK=0        # the TV log's line count when it was interrupted: the checks in 5t. look after it
  LAST_BEAT=0   # and the last beat the TV had played by then
  OLD_TV_UP=0   # the kill didn't take (kill_tv)
  if [[ "$RECONNECT" == "1" || "$RESTART" == "1" ]]; then
    while kill -0 "$PHONES_PID" 2>/dev/null && (( $(beats_played) < AT_BEAT )); do
      sleep 0.2
    done
    MARK=$(wc -l < "$OUT/tv.log")
    LAST_BEAT=$(beats_played)
    if (( LAST_BEAT < AT_BEAT )); then
      log "the battle ended before beat $AT_BEAT"
    elif [[ "$RECONNECT" == "1" ]]; then
      log "reconnect: killing the TV at beat $LAST_BEAT (log line $MARK)"
      kill_tv
      log "reconnect: relaunching it, to resume through session.json"
      start_tv "$OUT/tv-relaunched" --beat-log
    else
      log "restart: stopping the server at beat $LAST_BEAT (log line $MARK)"
      stop_server_for_restart
      sleep 2
      log "restart: starting it again on $URL with $DB"
      start_server "$OUT/server-restarted.log"
      log "restart: the server is back"
    fi
  fi
  wait "$PHONES_PID"
  PHONES_EXIT=$?
  PHONES_PID=""
  set -e

  log "letting the TV finish its shots ($LINGER s)"
  sleep "$LINGER"
  cleanup
  TV_PID="" XVFB_PID="" SERVER_PID=""

  # ---- 5t. the checks
  TACTICS_FAIL=0
  tactics_fail() { log "tactics: MISSING $*"; TACTICS_FAIL=1; }
  # after <line> <awk condition>: the first TV log line after <line> that matches, or nothing.
  after() { awk -v from="$1" -v code="$CODE" "NR > from && ($2) { print NR; exit }" "$OUT/tv.log"; }
  ERRORS=$(awk '
    pending != "" { if (tolower($0) !~ /^ *at: .*(alsa|audio|pulse)/) print pending; pending = "" }
    /^ *\[[0-9]+\] / { next }   # the backtrace of a warning: one is a shown signal meeting the server gone (ARCBOX_RESTART)
    tolower($0) ~ /exception|error/ && tolower($0) !~ /alsa|audio|pulse/ { pending = $0 }
    END { if (pending != "") print pending }' "$OUT/tv.log")
  if [[ -n "$ERRORS" ]]; then
    log "errors in the TV log:"
    echo "$ERRORS"
  fi
  log "the TV played $(grep -c '^BEAT ' "$OUT/tv.log" || true) beats, up to beat $(beats_played)"
  { grep -E '^TACTICS catch-up ' "$OUT/tv.log" || true; } | while read -r line; do log "$line"; done   # a healthy plain run has none

  if (( MARK > 0 )); then
    (( LAST_BEAT >= AT_BEAT )) || tactics_fail "beat $AT_BEAT before the battle ended"
    (( OLD_TV_UP == 0 )) || tactics_fail "the old TV gone: it was still running when the new one started"
    OTHER=$(grep -oE '^ROOM [A-Z]{4}' "$OUT/tv.log" | cut -d' ' -f2 | grep -vx "$CODE" | sort -u | paste -sd' ' || true)
    [[ -z "$OTHER" ]] || tactics_fail "the same room: the TV also opened $OTHER"
    BACK=$(after "$MARK" '$0 == "ROOM " code')
    if [[ -n "$BACK" ]]; then
      log "TV back in room $CODE · line $BACK"
    else
      tactics_fail "ROOM $CODE from the TV after line $MARK"
      BACK=$MARK
    fi
  fi
  RESUMED_AT=${BACK:-0}
  if [[ "$RECONNECT" == "1" ]]; then
    CAUGHT=$(after "$BACK" '/^TACTICS catch-up to beat /')
    FIRST=$(after "$BACK" '/^BEAT [0-9]+ /')
    if [[ -z "$CAUGHT" || -z "$FIRST" ]]; then
      tactics_fail "a catch-up and a first beat after the TV came back"
    else
      WANT=$(sed -n "${CAUGHT}p" "$OUT/tv.log" | awk '{ sub(",", "", $5); print $5 }')
      GOT=$(sed -n "${FIRST}p" "$OUT/tv.log" | awk '{ print $2 }')
      log "reconnect: $(sed -n "${CAUGHT}p" "$OUT/tv.log") · then beat $GOT; beat $LAST_BEAT was the last before it died"
      [[ "$GOT" == "$WANT" ]] || tactics_fail "the catch-up's beat first: it said $WANT and played $GOT"
      (( GOT > LAST_BEAT )) || tactics_fail "a beat newer than $LAST_BEAT first: an old beat came back ($GOT)"
    fi
    RESUMED_AT=$FIRST
  elif [[ "$RESTART" == "1" ]]; then
    RESUMING=$(after "$MARK" '$0 == "TACTICS screen tactics-resuming"')   # it comes in ahead of the ROOM line
    if [[ -z "$RESUMING" ]]; then
      # The phones can all be back before the TV is: then Resuming ends before the TV hears of it, and a slow TV mustn't hold the
      # game up. So the TV, once back, has to play on from a beat newer than any it played before it was back.
      # The TV can rejoin the server while it shuts down (a ROOM line), and again once it's back: the last before the Results counts.
      BACK=$(awk -v from="$MARK" -v code="$CODE" 'NR > from && $0 == "TACTICS screen tactics-results" { exit }
        NR > from && $0 == "ROOM " code { n = NR } END { print n + 0 }' "$OUT/tv.log")
      BEFORE_BACK=$(awk -v to="$BACK" 'NR < to && /^BEAT [0-9]+ / && $2 > n { n = $2 } END { print n + 0 }' "$OUT/tv.log")
      n=$(after "$BACK" '/^BEAT [0-9]+ /')
      GOT=$([[ -n "$n" ]] && sed -n "${n}p" "$OUT/tv.log" | awk '{ print $2 }' || true)
      if [[ -n "$GOT" ]] && (( GOT > BEFORE_BACK )); then
        log "restart: the phones were back before the TV, so it never saw Resuming; it played on from beat $GOT, after $BEFORE_BACK"
        RESUMED_AT=$n
      else
        tactics_fail "TACTICS screen tactics-resuming after the restart, or a newer beat than $BEFORE_BACK once the TV was back"
      fi
    else
      n=$(after "$RESUMING" '/^BEAT [0-9]+ /')
      if [[ -n "$n" ]]; then
        log "restart: resuming at line $RESUMING, then $(sed -n "${n}p" "$OUT/tv.log" | cut -d' ' -f1-2)"
        RESUMED_AT=$n
      else
        tactics_fail "a beat after Resuming"
      fi
    fi
  fi
  RESULTS=$(after "${RESUMED_AT:-0}" '$0 == "TACTICS screen tactics-results"')
  [[ -n "$RESULTS" ]] && log "the TV showed the results · line $RESULTS" || tactics_fail "TACTICS screen tactics-results on the TV"
  if [[ "$SHOTS" != "0" ]]; then
    SHOT=$(find "$OUT" -name '*tactics-results*.png' | tail -1)
    [[ -n "$SHOT" ]] && log "results screenshot: $SHOT" || tactics_fail "a screenshot of the results"
  fi

  # The sound, from the AUDIO lines: music from the start, effects with the beats, and the music changing at the Results.
  FIRST_MUSIC=$(after 0 '/^AUDIO Music /')
  [[ -n "$FIRST_MUSIC" ]] && log "music: $(sed -n "${FIRST_MUSIC}p" "$OUT/tv.log") · line $FIRST_MUSIC" || tactics_fail "an AUDIO Music line"
  EFFECTS=$(grep -c '^AUDIO Sfx ' "$OUT/tv.log" || true)
  (( EFFECTS > 0 )) && log "effects: $EFFECTS AUDIO Sfx lines" || tactics_fail "AUDIO Sfx lines"
  if [[ -n "$RESULTS" ]]; then
    ENDING=$(awk -v to="$RESULTS" 'NR < to && /^AUDIO Music / { line = $3 } END { print line }' "$OUT/tv.log")
    FIGHTING=$(awk -v to="$RESULTS" 'NR < to && /^AUDIO Music / { print $3 }' "$OUT/tv.log" | tail -2 | head -1)
    log "music at the results: $FIGHTING, then $ENDING"
    [[ "$ENDING" =~ ^(victory|defeat|camp)$ ]] || tactics_fail "the music changing at the Results (it was $ENDING)"
  fi
  if grep -q '"say"' "$ROOT/content/tactics/base/rules/encounters/$ENCOUNTER.json"; then
    CLIP=$(grep -m1 -E "^AUDIO Voice clip say_${ENCOUNTER}_" "$OUT/tv.log" || true)
    [[ -n "$CLIP" ]] && log "voice: $CLIP" || tactics_fail "a voice clip for a say line of $ENCOUNTER"
  fi

  if (( PHONES_EXIT != 0 )) || [[ -n "$ERRORS" ]] || (( TACTICS_FAIL )); then
    log "FAILED (phones exit $PHONES_EXIT)"
    exit 1
  fi
  log "OK"
  exit 0
fi

# ---- 4. phones
log "phones: $PHONES bots$([[ "$FULL" == "1" ]] && echo ", full race")$([[ "$RESTART" == "1" ]] && echo ", the server restarts mid-race")$([[ "$SWAP" == "1" ]] && echo ", bot 2 swaps phones mid-race")"
set +e
run_phones() {
  ARCBOX_URL="$URL" ARCBOX_ROOM="$CODE" ARCBOX_PHONES="$PHONES" ARCBOX_START=host ARCBOX_SHOTS="$OUT/phones" \
    ARCBOX_PACE="${ARCBOX_PACE:-4}" ARCBOX_FULL="$FULL" ARCBOX_RESTART="$RESTART" ARCBOX_SWAP="$SWAP" \
    node "$ROOT/web/play/e2e/redline.e2e.mjs" 2>&1 | tee "$OUT/phones.log"
  return "${PIPESTATUS[0]}"
}
# The TV log's line count when the server went down (ARCBOX_RESTART): the checks in 9. look after it.
RESTART_AT=0
if [[ "$RESTART" == "1" ]]; then
  : > "$OUT/phones.log"
  run_phones &
  PHONES_PID=$!
  while kill -0 "$PHONES_PID" 2>/dev/null && ! grep -q 'RESTART now' "$OUT/phones.log"; do
    sleep 0.5
  done
  if grep -q 'RESTART now' "$OUT/phones.log"; then
    RESTART_AT=$(wc -l < "$OUT/tv.log")
    log "restart: stopping the server mid-race (TV log at line $RESTART_AT)"
    stop_pid "$SERVER_PID" "server"
    sleep 2   # long enough for the TV and the phones to see it go
    log "restart: starting it again on $URL with $DB"
    start_server "$OUT/server-restarted.log"
    log "restart: the server is back"
  else
    log "restart: the phone script ended before asking for the restart"
  fi
  wait "$PHONES_PID"
  PHONES_EXIT=$?
  PHONES_PID=""
  # The TV holds the results for 10 s before its lobby: wait for that lobby (or give up after 60 s).
  for _ in $(seq 1 120); do
    awk -v code="$CODE" '/^RESULTS Game interrupted: the server restarted\.$/ { note = 1 } note && $0 == "LOBBY " code { found = 1; exit }
      END { exit !found }' "$OUT/tv.log" && break
    sleep 0.5
  done
else
  run_phones
  PHONES_EXIT=$?
fi
if [[ "${TV_REMATCH:-}" == "1" && "$FULL" == "1" && $PHONES_EXIT -eq 0 ]]; then
  # A second race in the same room, started by the VIP phone: the TV reuses the scene it kept from the first.
  log "rematch: the VIP phone starts a second race (to its first Reveal)"
  ARCBOX_URL="$URL" ARCBOX_ROOM="$CODE" ARCBOX_PHONES="$PHONES" ARCBOX_SHOTS="$OUT/phones-rematch" ARCBOX_REJOIN=0 ARCBOX_NAME=Again \
    ARCBOX_PACE="${ARCBOX_PACE:-4}" ARCBOX_FULL=0 ARCBOX_SWAP=0 ARCBOX_RESTART=0 \
    node "$ROOT/web/play/e2e/redline.e2e.mjs" 2>&1 | tee "$OUT/phones-rematch.log"
  PHONES_EXIT=${PIPESTATUS[0]}
fi
set -e

# ---- 5. wrap up
log "letting the TV finish its shots (${LINGER} s)"
sleep "$LINGER"
cleanup
TV_PID="" XVFB_PID="" SERVER_PID=""

# Godot prints "ERROR: …" then "   at: <source>": skip errors raised by the audio driver (there is no sound card).
ERRORS=$(awk '
  pending != "" { if (tolower($0) !~ /^ *at: .*(alsa|audio|pulse)/) print pending; pending = "" }
  tolower($0) ~ /exception|error/ && tolower($0) !~ /alsa|audio|pulse/ { pending = $0 }
  END { if (pending != "") print pending }' "$OUT/tv.log")
SHOTS_TV=$(find "$OUT/tv" -name '*.png' | wc -l)
SHOTS_PHONES=$(find "$OUT/phones" -name '*.png' | wc -l)
log "screenshots: $SHOTS_TV from the TV in $OUT/tv, $SHOTS_PHONES from phone 1 in $OUT/phones"
# Qualifying phases are all round 0: show their lap instead (L1…), counted from each lap's first phase.
PHASES=$(grep -oE '^REDLINE [A-Za-z]+ round [0-9]+' "$OUT/tv.log" | awk '{print $2, $4}' | uniq)
log "TV phases: $(awk '
  $1 == "DriverSelect" { lap = 0 }
  $1 == "QualifyingReading" || ($1 == "QualifyingQuestion" && prev != "QualifyingReading") { lap++ }
  { print $1 "(" ($1 ~ /^Qualifying/ ? "L" lap : $2) ")"; prev = $1 }' <<< "$PHASES" | tr '\n' ' ')"
if [[ -n "$ERRORS" ]]; then
  log "errors in the TV log:"
  echo "$ERRORS"
fi

# ---- 6. audio: each expected AUDIO line after the one before it ("label|regex").
AUDIO_FAIL=0
# expect_in_order <what> <label|regex>…: each line after the one before it; sets AUDIO_FAIL on the first one missing.
expect_in_order() {
  local what=$1 last=0 item label re n
  shift
  for item in "$@"; do
    label=${item%%|*}
    re=${item#*|}
    n=$(RE="$re" awk -v from="$last" 'NR > from && $0 ~ ENVIRON["RE"] { print NR; exit }' "$OUT/tv.log")
    if [[ -z "$n" ]]; then
      log "$what: MISSING $label (/$re/ after line $last)"
      AUDIO_FAIL=1
      return
    fi
    log "$what: $label · $(sed -n "${n}p" "$OUT/tv.log")"
    last=$n
  done
}
# Qualifying runs before the start lights; its closing line may overlap them, so it is checked on its own.
expect_in_order "audio" \
  "qualifying start|^AUDIO Voice redline\.announcer: Qualifying! Three questions, fastest correct answers take pole\.$" \
  "pole position|^AUDIO Voice redline\.announcer: Pole position for "
EXPECT=(
  "start beep 1|^AUDIO Sfx start-beep #1$"
  "start beep 2|^AUDIO Sfx start-beep #2$"
  "start beep 3|^AUDIO Sfx start-beep #3$"
  "start beep 4|^AUDIO Sfx start-beep #4$"
  "start beep 5|^AUDIO Sfx start-beep #5$"
  "lights out|^AUDIO Sfx lights-out$"
  "round 1 commentary|^AUDIO Voice redline\.announcer: (And it's lights out!|Round 1\.)"
  "question read out|^AUDIO Voice redline\.questions: "
  "Reveal sting or buzz|^AUDIO Sfx (correct|wrong)$"
  "the answer|^AUDIO Voice redline\.announcer: The answer is "
)
if [[ "$FULL" == "1" ]]; then
  EXPECT+=("fanfare|^AUDIO Sfx fanfare$" "the winner|^AUDIO Voice redline\.announcer: (Chequered flag!|.* takes the win\.)")
fi
(( AUDIO_FAIL )) || expect_in_order "audio" "${EXPECT[@]}"

# ---- 7. the podium (ARCBOX_FULL=1): the TV reaches the Champagne; the anthem (or, for a country without a melody,
# the victory theme) plays in the Anthem and the theme follows it; every podium phone's cork pops on the TV, the
# first with the victory sting.
if [[ "$FULL" == "1" && $AUDIO_FAIL -eq 0 ]]; then
  podium_fail() { log "podium: MISSING $*"; AUDIO_FAIL=1; }
  ANTHEM_AT=$(grep -n -m1 -E '^REDLINE Anthem round ' "$OUT/tv.log" | cut -d: -f1 || true)
  CHAMPAGNE_AT=$(grep -n -m1 -E '^REDLINE Champagne round ' "$OUT/tv.log" | cut -d: -f1 || true)
  if [[ -z "$ANTHEM_AT" || -z "$CHAMPAGNE_AT" ]]; then
    podium_fail "the TV never reached the Anthem and the Champagne"
  else
    log "podium: $(sed -n "${ANTHEM_AT}p" "$OUT/tv.log") → $(sed -n "${CHAMPAGNE_AT}p" "$OUT/tv.log")"
    FIRST=$(awk -v from="$ANTHEM_AT" 'NR > from && /^AUDIO Music (anthem [a-z][a-z]|victory-theme)$/ { print NR": "$0; exit }' "$OUT/tv.log")
    if [[ -z "$FIRST" ]]; then
      podium_fail "the anthem or the victory theme"
    else
      log "podium: music · ${FIRST#*: }"
      if [[ "$FIRST" == *anthem* ]]; then
        THEME=$(awk -v from="${FIRST%%:*}" 'NR > from && /^AUDIO Music victory-theme$/ { print NR; exit }' "$OUT/tv.log")
        [[ -n "$THEME" ]] && log "podium: music · victory-theme after the anthem" || podium_fail "the victory theme after the anthem"
      fi
    fi
    grep -q -E '^AUDIO Sfx victory-sting$' "$OUT/tv.log" && log "podium: victory sting" || podium_fail "the victory sting on the first spray"
    for step in $(seq 1 $(( PHONES < 3 ? PHONES : 3 ))); do
      n=$(grep -c -E "^REDLINE spray #[0-9]+ step $step " "$OUT/tv.log" || true)
      pop=$(grep -c -E "^AUDIO Sfx cork-pop step $step$" "$OUT/tv.log" || true)
      if (( n > 0 && pop > 0 )); then
        log "podium: step $step sprayed $n time(s) · $(grep -m1 -E "^REDLINE spray #[0-9]+ step $step " "$OUT/tv.log")"
      else
        podium_fail "a spray from podium step $step ($n sprays, $pop cork pops)"
      fi
    done
  fi
fi
# ---- 8. qualifying: the phases before the first Grid, and the bots' grid order (each race: the rematch has its own).
QUALI_FAIL=0
QSEQ=$(awk '$1 == "Grid" { exit } $1 ~ /^Qualifying/ { printf "%s ", $1 }' <<< "$PHASES")
QWANT=$(for _ in 1 2 3; do printf 'QualifyingReading QualifyingQuestion QualifyingResult '; done)
if [[ "$QSEQ" == "$QWANT" ]]; then
  log "qualifying: QualifyingReading → QualifyingQuestion → QualifyingResult for laps 1–3, then Grid"
else
  log "qualifying: WRONG phases before the Grid: '${QSEQ% }' (want '${QWANT% }')"
  QUALI_FAIL=1
fi
race=0
for plog in "$OUT/phones.log" "$OUT/phones-rematch.log"; do
  [[ -f "$plog" ]] || continue
  race=$((race + 1))
  want=$(grep -oE 'QUALI order .*' "$plog" | tail -1 | cut -d' ' -f3-)
  grid=$(grep -E '^REDLINE grid ' "$OUT/tv.log" | sed -n "${race}p" | cut -d' ' -f3-)
  if [[ -z "$want" || "$want" == *"?"* || -z "$grid" ]]; then
    log "qualifying: race $race: no bot order ('$want') or no grid line ('$grid')"
    QUALI_FAIL=1
    continue
  fi
  got=$(for c in $grid; do if [[ " $want " == *" $c "* ]]; then printf '%s ' "$c"; fi; done)
  got=${got% }
  if [[ "$got" == "$want" ]]; then
    log "qualifying: race $race: bots on the grid as they qualified: $got · REDLINE grid $grid"
  else
    log "qualifying: race $race: bots on the grid in order '$got', qualifying says '$want' · REDLINE grid $grid"
    QUALI_FAIL=1
  fi
done

# ---- 9. the restart (ARCBOX_RESTART=1): back in the same room, the race parked, the note, then the lobby, and silence.
RESTART_FAIL=0
if [[ "$RESTART" == "1" ]]; then
  restart_fail() { log "restart: MISSING $*"; RESTART_FAIL=1; }
  # after <line> <awk condition>: the first TV log line after <line> that matches, or nothing.
  after() { awk -v from="$1" -v code="$CODE" "NR > from && ($2) { print NR; exit }" "$OUT/tv.log"; }
  if (( RESTART_AT == 0 )); then
    restart_fail "the restart (the phone script never asked for it)"
  else
    OTHER=$(grep -oE '^ROOM [A-Z]{4}' "$OUT/tv.log" | cut -d' ' -f2 | grep -vx "$CODE" | sort -u | paste -sd' ' || true)
    [[ -z "$OTHER" ]] || restart_fail "the same room: the TV also opened $OTHER"
    n=$(after "$RESTART_AT" '$0 == "ROOM " code')
    [[ -n "$n" ]] && log "restart: TV back in room $CODE · line $n" || restart_fail "ROOM $CODE from the TV after the restart"
    PARKED=$(after "$RESTART_AT" '$0 == "REDLINE parked for the next race (interrupted)"')
    if [[ -n "$PARKED" ]]; then
      log "restart: the race left the air mid-phase · line $PARKED"
      # Nothing of the cut-off race may start once it is off the air (narration, anthem, effects): only the lobby loop.
      LEFT=$(awk -v from="$PARKED" 'NR > from && /^AUDIO / && $0 != "AUDIO Music lobby-loop"' "$OUT/tv.log")
      if [[ -n "$LEFT" ]]; then
        log "restart: sound from the race after it was parked:"
        echo "$LEFT"
        RESTART_FAIL=1
      else
        log "restart: no sound after the park but the lobby loop"
      fi
    else
      restart_fail "REDLINE parked for the next race (interrupted)"
    fi
    NOTE=$(after "$RESTART_AT" '$0 == "RESULTS Game interrupted: the server restarted."')
    if [[ -z "$NOTE" ]]; then
      restart_fail "RESULTS Game interrupted: the server restarted."
    else
      n=$(after "$NOTE" '$0 == "LOBBY " code')
      [[ -n "$n" ]] && log "restart: results with the note · line $NOTE, then the lobby of $CODE · line $n" || restart_fail "LOBBY $CODE after the results"
    fi
  fi
fi

# ---- 10. the swap (ARCBOX_SWAP=1): the TV's results list bot 2 once, among one row per phone.
# The line is `RESULTS <message> | <name> <score>, …`; REDLINE names carry the driver code ("Bot2 (PIA) 12").
SWAP_FAIL=0
if [[ "$SWAP" == "1" ]]; then
  RESULTS_LINE=$(grep -E '^RESULTS ' "$OUT/tv.log" | head -1 | sed 's/^RESULTS //')
  SCORES=$(sed 's/.* | //' <<< "$RESULTS_LINE")
  ROWS=$(sed 's/, /\n/g' <<< "$SCORES" | grep -c . || true)
  BOT2=$(sed 's/, /\n/g' <<< "$SCORES" | grep -cE '^Bot2( \([A-Z]{3}\))? -?[0-9]+$' || true)
  if [[ "$ROWS" == "$PHONES" && "$BOT2" == "1" ]]; then
    log "swap: TV results $RESULTS_LINE"
  else
    log "swap: TV results '$RESULTS_LINE': want $PHONES rows with Bot2 once"
    SWAP_FAIL=1
  fi
fi

log "narrator: $(grep -E '^NARRATOR ' "$OUT/tv.log" | sed 's/^NARRATOR //' | awk '!seen[$0]++' | paste -sd '|' | sed 's/|/ | /g')"
grep -E '^AUDIO ' "$OUT/tv.log" > "$OUT/audio.log" || true
log "audio: $(wc -l < "$OUT/audio.log") AUDIO lines in $OUT/audio.log"

if (( PHONES_EXIT != 0 )) || [[ -n "$ERRORS" ]] || (( AUDIO_FAIL )) || (( QUALI_FAIL )) || (( RESTART_FAIL )) || (( SWAP_FAIL )); then
  log "FAILED (phones exit $PHONES_EXIT)"
  exit 1
fi
log "OK"
