#!/usr/bin/env bash
# TV end-to-end run: the ArcBox server, the Godot TV app and bot phones together, with screenshots from both.
#
#   GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tools/tv-e2e.sh            REDLINE to the first Reveal
#   ARCBOX_FULL=1 GODOT=… tools/tv-e2e.sh                                           a full race, results, back to the lobby
#   ARCBOX_RESTART=1 GODOT=… tools/tv-e2e.sh                                        the server restarts mid-race (issue #24)
#   ARCBOX_SWAP=1 GODOT=… tools/tv-e2e.sh                                           a phone is swapped mid-race (issue #24)
#
# What it does:
#   1. builds the phone app (web/play/dist), the server and the TV host (skip with --skip-build);
#   2. starts the server in Development (serves web/play/dist, OpenTDB off) on a free port on 127.0.0.1;
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
#      sound after the results but the lobby loop: no narration, anthem, effects or engines from the cut-off race.
#   9. ARCBOX_SWAP=1 (a full race): on a question in round 2 or later, bot 2's storage is cleared and a fresh page takes
#      its seat back through the picker (Peek, then TakeSeat); its old secret must fail to rejoin, and it must finish the
#      race in its own seat (the phone script checks; the TV's `RESULTS` line must list it once).
#
# Environment (all optional except GODOT when `godot` is not on PATH):
#   GODOT            Godot 4.7.2 .NET binary
#   ARCBOX_FULL      1 = race to the chequered flag and back to the lobby (passed to the phone script)
#   ARCBOX_RESTART   1 = restart the server mid-race and check everyone lands in the same room's lobby (not with ARCBOX_FULL)
#   ARCBOX_SWAP      1 = swap bot 2's phone mid-race and check it finishes in its seat (implies ARCBOX_FULL=1; needs 2+ phones)
#   ARCBOX_PHONES    bot phones (default 3)
#   ARCBOX_PACE      bots' reading-time multiplier (default 4 here: 1–9 s per decision, so each phase stays on the
#                    TV long enough for its screenshot even when the TV draws a frame a second)
#   ARCBOX_E2E_OUT   output folder (default /tmp/arcbox-tv-e2e-<time>): tv/*.png, phones/*.png and the three logs
#   TV_RES           TV window size (default 1280x720)
#   TV_PRESET        render preset (default low; software rendering can't keep up with the phase timers otherwise)
#   TV_LINGER        seconds the TV keeps running after the phones finish, for its last shots (default 12)
#   TV_ARGS          extra TV app arguments, e.g. --perf (log fps every 5 s)
#   TV_REMATCH       1 (with ARCBOX_FULL=1) = after the race, the VIP phone starts a second one in the same room
#                    and plays it to its first Reveal (shots in <out>/phones-rematch)
#
# Exit code 0 when the phones reached their goal, the TV log has no errors beyond the expected audio-driver ones, the
# audio lines are all there in order, qualifying ran and set the bots' grid order, and (ARCBOX_RESTART, ARCBOX_SWAP)
# the TV saw the restart or the swap as described above.
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
GODOT=${GODOT:-$(command -v godot || true)}
PHONES=${ARCBOX_PHONES:-3}
RES=${TV_RES:-1280x720}
PRESET=${TV_PRESET:-low}
LINGER=${TV_LINGER:-12}
OUT=${ARCBOX_E2E_OUT:-/tmp/arcbox-tv-e2e-$(date +%Y%m%d-%H%M%S)}
FULL=${ARCBOX_FULL:-}
RESTART=${ARCBOX_RESTART:-}
SWAP=${ARCBOX_SWAP:-}
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
for tool in dotnet node Xvfb curl; do
  command -v "$tool" >/dev/null || { echo "$tool is required" >&2; exit 2; }
done
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
stop_pid() {   # stop_pid <pid> <name>: TERM, wait up to 10 s, then KILL
  local pid=$1 name=$2
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null || return 0
  kill -TERM "$pid" 2>/dev/null || true
  for _ in $(seq 1 20); do
    kill -0 "$pid" 2>/dev/null || { log "$name stopped"; return 0; }
    sleep 0.5
  done
  log "$name did not stop; killing it"
  kill -KILL "$pid" 2>/dev/null || true
}
cleanup() {
  if [[ -n "$PHONES_PID" ]] && kill -0 "$PHONES_PID" 2>/dev/null; then
    pkill -TERM -P "$PHONES_PID" 2>/dev/null || true   # the phone script and its tee (ARCBOX_RESTART runs them in the background)
    stop_pid "$PHONES_PID" "phones"
  fi
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
# This run's own database, so a restart finds its rooms again (ARCBOX_RESTART) and no run sees another's.
DB="$OUT/arcbox.db"
# start_server <log>: the server on $URL and $DB, in the background; returns once it answers.
start_server() {
  local out=$1
  (cd "$ROOT/src/ArcBox.Server" && exec env ASPNETCORE_ENVIRONMENT=Development Storage__Path="$DB" dotnet "$SERVER_DLL" --urls "$URL") > "$out" 2>&1 &
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
DISPLAY_NUM=90
while [[ -e "/tmp/.X${DISPLAY_NUM}-lock" || -e "/tmp/.X11-unix/X${DISPLAY_NUM}" ]]; do DISPLAY_NUM=$((DISPLAY_NUM + 1)); done
Xvfb ":$DISPLAY_NUM" -screen 0 "${RES}x24" -nolisten tcp > "$OUT/xvfb.log" 2>&1 &
XVFB_PID=$!
sleep 1
QUIT_AFTER=$([[ "$FULL" == "1" ]] && echo 2700 || echo 900)
log "starting the TV on :$DISPLAY_NUM ($RES, preset $PRESET)"
DISPLAY=":$DISPLAY_NUM" "$GODOT" --path "$ROOT/src/ArcBox.Host" --rendering-driver vulkan -- \
  --new-room --shots="$OUT/tv" --preset="$PRESET" --res="$RES" --server="$URL" \
  --autostart="redline:$PHONES" --quit-after="$QUIT_AFTER" --audio-log ${TV_ARGS:-} > "$OUT/tv.log" 2>&1 &
TV_PID=$!

CODE=""
for _ in $(seq 1 360); do
  CODE=$(grep -m1 -oE '^ROOM [A-Z]{4}' "$OUT/tv.log" | cut -d' ' -f2 || true)
  [[ -n "$CODE" ]] && break
  kill -0 "$TV_PID" 2>/dev/null || { log "the TV exited"; tail -30 "$OUT/tv.log"; exit 1; }
  sleep 0.5
done
[[ -n "$CODE" ]] || { log "no ROOM line from the TV after 180 s"; tail -30 "$OUT/tv.log"; exit 1; }
log "TV opened room $CODE"

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
    n=$(after "$RESTART_AT" '$0 == "REDLINE parked for the next race (interrupted)"')
    [[ -n "$n" ]] && log "restart: the race left the air mid-phase · line $n" || restart_fail "REDLINE parked for the next race (interrupted)"
    NOTE=$(after "$RESTART_AT" '$0 == "RESULTS Game interrupted: the server restarted."')
    if [[ -z "$NOTE" ]]; then
      restart_fail "RESULTS Game interrupted: the server restarted."
    else
      n=$(after "$NOTE" '$0 == "LOBBY " code')
      [[ -n "$n" ]] && log "restart: results with the note · line $NOTE, then the lobby of $CODE · line $n" || restart_fail "LOBBY $CODE after the results"
      # Nothing of the cut-off race may start once it is off the air (narration, anthem, effects): only the lobby loop.
      LEFT=$(awk -v from="$NOTE" 'NR > from && /^AUDIO / && $0 != "AUDIO Music lobby-loop"' "$OUT/tv.log")
      if [[ -n "$LEFT" ]]; then
        log "restart: sound from the race after its results:"
        echo "$LEFT"
        RESTART_FAIL=1
      else
        log "restart: no sound after the results but the lobby loop"
      fi
    fi
  fi
fi

# ---- 10. the swap (ARCBOX_SWAP=1): the TV's results list bot 2 once, among one row per phone.
# The line is `RESULTS <message> | <name> <score>, …`; REDLINE names carry the driver code ("Bot2 (PIA) 12").
SWAP_FAIL=0
if [[ "$SWAP" == "1" ]]; then
  RES=$(grep -E '^RESULTS ' "$OUT/tv.log" | head -1 | sed 's/^RESULTS //')
  SCORES=$(sed 's/.* | //' <<< "$RES")
  ROWS=$(sed 's/, /\n/g' <<< "$SCORES" | grep -c . || true)
  BOT2=$(sed 's/, /\n/g' <<< "$SCORES" | grep -cE '^Bot2( \([A-Z]{3}\))? -?[0-9]+$' || true)
  if [[ "$ROWS" == "$PHONES" && "$BOT2" == "1" ]]; then
    log "swap: TV results $RES"
  else
    log "swap: TV results '$RES': want $PHONES rows with Bot2 once"
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
