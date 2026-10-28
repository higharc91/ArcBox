#!/usr/bin/env bash
# TV end-to-end run: the ArcBox server, the Godot TV app and bot phones together, with screenshots from both.
#
#   GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tools/tv-e2e.sh            REDLINE to the first Reveal
#   ARCBOX_FULL=1 GODOT=… tools/tv-e2e.sh                                           a full race, results, back to the lobby
#
# What it does:
#   1. builds the phone app (web/play/dist), the server and the TV host (skip with --skip-build);
#   2. starts the server in Development (serves web/play/dist, OpenTDB off) on a free port on 127.0.0.1;
#   3. starts the TV on its own Xvfb display with software Vulkan:
#        --new-room --shots=<out>/tv --preset=<low> --res=<1280x720> --server=<url> --autostart=redline:<phones> --audio-log
#      and reads the room code from its `ROOM <CODE>` log line;
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
#
# Environment (all optional except GODOT when `godot` is not on PATH):
#   GODOT            Godot 4.7.2 .NET binary
#   ARCBOX_FULL      1 = race to the chequered flag and back to the lobby (passed to the phone script)
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
# Exit code 0 when the phones reached their goal, the TV log has no errors beyond the expected audio-driver ones, and
# the audio lines are all there in order.
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
GODOT=${GODOT:-$(command -v godot || true)}
PHONES=${ARCBOX_PHONES:-3}
RES=${TV_RES:-1280x720}
PRESET=${TV_PRESET:-low}
LINGER=${TV_LINGER:-12}
OUT=${ARCBOX_E2E_OUT:-/tmp/arcbox-tv-e2e-$(date +%Y%m%d-%H%M%S)}
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

mkdir -p "$OUT/tv" "$OUT/phones"
log() { printf '[tv-e2e %s] %s\n' "$(date +%H:%M:%S)" "$*"; }

SERVER_PID=""
XVFB_PID=""
TV_PID=""
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
if [[ ! -d "$ROOT/src/ArcBox.Host/.godot/imported" ]]; then
  log "first run: importing the Godot project"
  timeout -k 5 600 "$GODOT" --headless --editor --quit --path "$ROOT/src/ArcBox.Host" >> "$OUT/build.log" 2>&1 || true
fi

# ---- 2. server
PORT=$(node -e 'const s=require("net").createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close()})')
URL="http://127.0.0.1:$PORT"
SERVER_DLL="$ROOT/src/ArcBox.Server/bin/Debug/net10.0/ArcBox.Server.dll"
log "starting the server on $URL"
(cd "$ROOT/src/ArcBox.Server" && exec env ASPNETCORE_ENVIRONMENT=Development dotnet "$SERVER_DLL" --urls "$URL") > "$OUT/server.log" 2>&1 &
SERVER_PID=$!
for _ in $(seq 1 60); do
  curl -sf -o /dev/null "$URL/" && break
  kill -0 "$SERVER_PID" 2>/dev/null || { log "the server exited"; tail -20 "$OUT/server.log"; exit 1; }
  sleep 0.5
done
curl -sf -o /dev/null "$URL/" || { log "the server did not come up"; exit 1; }

# ---- 3. TV
DISPLAY_NUM=90
while [[ -e "/tmp/.X${DISPLAY_NUM}-lock" || -e "/tmp/.X11-unix/X${DISPLAY_NUM}" ]]; do DISPLAY_NUM=$((DISPLAY_NUM + 1)); done
Xvfb ":$DISPLAY_NUM" -screen 0 "${RES}x24" -nolisten tcp > "$OUT/xvfb.log" 2>&1 &
XVFB_PID=$!
sleep 1
QUIT_AFTER=$([[ "${ARCBOX_FULL:-}" == "1" ]] && echo 2700 || echo 900)
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
log "phones: $PHONES bots${ARCBOX_FULL:+, full race}"
set +e
ARCBOX_URL="$URL" ARCBOX_ROOM="$CODE" ARCBOX_PHONES="$PHONES" ARCBOX_START=host ARCBOX_SHOTS="$OUT/phones" \
  ARCBOX_PACE="${ARCBOX_PACE:-4}" \
  node "$ROOT/web/play/e2e/redline.e2e.mjs" 2>&1 | tee "$OUT/phones.log"
PHONES_EXIT=${PIPESTATUS[0]}
if [[ "${TV_REMATCH:-}" == "1" && "${ARCBOX_FULL:-}" == "1" && $PHONES_EXIT -eq 0 ]]; then
  # A second race in the same room, started by the VIP phone: the TV reuses the scene it kept from the first.
  log "rematch: the VIP phone starts a second race (to its first Reveal)"
  ARCBOX_URL="$URL" ARCBOX_ROOM="$CODE" ARCBOX_PHONES="$PHONES" ARCBOX_SHOTS="$OUT/phones-rematch" ARCBOX_REJOIN=0 ARCBOX_NAME=Again \
    ARCBOX_PACE="${ARCBOX_PACE:-4}" ARCBOX_FULL=0 \
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
log "TV phases: $(grep -oE '^REDLINE [A-Za-z]+ round [0-9]+' "$OUT/tv.log" | awk '{print $2"("$4")"}' | tr '\n' ' ')"
if [[ -n "$ERRORS" ]]; then
  log "errors in the TV log:"
  echo "$ERRORS"
fi

# ---- 6. audio: each expected AUDIO line after the one before it ("label|regex").
AUDIO_FAIL=0
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
if [[ "${ARCBOX_FULL:-}" == "1" ]]; then
  EXPECT+=("fanfare|^AUDIO Sfx fanfare$" "the winner|^AUDIO Voice redline\.announcer: (Chequered flag!|.* takes the win\.)")
fi
LAST=0
for item in "${EXPECT[@]}"; do
  label=${item%%|*}
  re=${item#*|}
  n=$(RE="$re" awk -v from="$LAST" 'NR > from && $0 ~ ENVIRON["RE"] { print NR; exit }' "$OUT/tv.log")
  if [[ -z "$n" ]]; then
    log "audio: MISSING $label (/$re/ after line $LAST)"
    AUDIO_FAIL=1
    break
  fi
  log "audio: $label · $(sed -n "${n}p" "$OUT/tv.log")"
  LAST=$n
done

# ---- 7. the podium (ARCBOX_FULL=1): the TV reaches the Champagne; the anthem (or, for a country without a melody,
# the victory theme) plays in the Anthem and the theme follows it; every podium phone's cork pops on the TV, the
# first with the victory sting.
if [[ "${ARCBOX_FULL:-}" == "1" && $AUDIO_FAIL -eq 0 ]]; then
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
log "narrator: $(grep -E '^NARRATOR ' "$OUT/tv.log" | sed 's/^NARRATOR //' | awk '!seen[$0]++' | paste -sd '|' | sed 's/|/ | /g')"
grep -E '^AUDIO ' "$OUT/tv.log" > "$OUT/audio.log" || true
log "audio: $(wc -l < "$OUT/audio.log") AUDIO lines in $OUT/audio.log"

if (( PHONES_EXIT != 0 )) || [[ -n "$ERRORS" ]] || (( AUDIO_FAIL )); then
  log "FAILED (phones exit $PHONES_EXIT)"
  exit 1
fi
log "OK"
