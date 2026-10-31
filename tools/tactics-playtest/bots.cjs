// Bot phones for ArcBox Tactics: joins a room as N players over the real hub and plays a whole session, so the TV can be
// run against a real game with no browser or Playwright. The TV draws it; the bots stand in for everyone holding a phone.
//
//   node tools/tactics-playtest/bots.cjs --room=<CODE> [options]
//
//   --room=<CODE>        the room to join (the TV prints `ROOM <CODE>`), or
//   --room-log=<file>    a file to watch for that line: the TV's stdout, redirected
//   --server=<url>       default http://localhost:5080
//   --players=<n>        default 3
//   --encounter=<id>     default river_crossing
//   --hold=<seconds>     how long each screen stays up: between class picks, before the encounter pick, on the results
//                        (default 3; a run taking screenshots wants the phases on air for a moment)
//   --fight              attack anything in reach and walk toward the foes; without it the bots only end their turns
//   --leave              once the battle is on, every bot disconnects, so the AI plays each hero: a whole battle with nobody
//                        at a phone. It exits at that point; the TV runs on to Results, so give --quit-after room
//   --sit-out=turn,react sit out what's named, so the game's timers run out as they do for a real player who's gone quiet: the
//                        first bot to get a turn leaves it for the 60 s timer, and then the AI finishes it (the TV badges
//                        that), and no reaction prompt is ever answered, so each one stays up for its 10 s
//   --area               prefer an ability that catches two or more foes and no friend, for watching area spells on the TV
//   --people=<n>         wait for n real phones to join and claim a seat before starting, so you can play alongside the bots:
//                        tell the TV's room code to your phone, then choose a class on it (see "With your own phone" below)
//   --then=end|next      after the results: end the session (default), or play the next battle, forever
//   --timeout=<seconds>  give up after this long (default 900)
//
// Run it (on Windows, from Git Bash; the database goes in a temp file so the run leaves no campaign behind):
//
//   export PATH="/c/Users/fatma/AppData/Local/Microsoft/dotnet:$PATH"
//   dotnet build src/ArcBox.Host
//   dotnet build src/ArcBox.Server
//   ASPNETCORE_ENVIRONMENT=Development Storage__Path="$TEMP/tactics-bots.db" \
//     dotnet run --no-build --no-launch-profile --project src/ArcBox.Server --urls http://localhost:5080 &
//   "$GODOT" --path src/ArcBox.Host -- --new-room --server=http://localhost:5080 --res=1280x720 \
//     --shots="$TEMP/tactics-shots" --quit-after=300 > "$TEMP/tv.log" 2>&1 &
//   node tools/tactics-playtest/bots.cjs --room-log="$TEMP/tv.log" --fight
//
// GODOT is the Godot 4.7.2 .NET console exe (the console one, or there is no stdout to read ROOM from). Keep the --no-build:
// a TV started while `dotnet run` was still building hung before its first line. A battle takes about 4 minutes, since
// nothing sends `shown` yet, so give --quit-after room. Stop the server afterwards: it holds the port and locks its files.
//
// With your own phone: start the server on the LAN and the TV as above, with the PC's own address in both, so the QR works:
//
//   ... dotnet run --no-build --no-launch-profile --project src/ArcBox.Server --urls http://0.0.0.0:5080 &
//   "$GODOT" --path src/ArcBox.Host -- --new-room --server=http://<pc-ip>:5080 --res=1920x1080 --quit-after=1200 > "$TEMP/tv.log" 2>&1 &
//   node tools/tactics-playtest/bots.cjs --room-log="$TEMP/tv.log" --server=http://localhost:5080 --players=3 --people=1 --fight --hold=1
//
// Then open http://<pc-ip>:5080 on the phone, type the room code and your name, and choose "I'm new here" if it asks who you
// are. The bots start the game once you're in, and pick the encounter once you've picked a class.
//
// The bots do the lobby themselves: the first to join is the VIP, who selects Tactics and makes a campaign, the others
// claim seats in it, and the VIP starts. So the TV needs no --autostart. --autostart=tactics:<n> works too: it selects
// the game and starts once CanStart is true, which needs the same campaign step, so both go to start at that moment. The
// one that loses is refused, which is harmless: here it's a log line, on the TV an "Autostart failed" warning.
//
// The bots reconnect and rejoin their seats as a phone does, so a server restart mid-battle (Resuming) carries on without them
// being told. It needs web/play/node_modules (`npm ci` in web/play). Prints each step, and exits non-zero on a timeout or a
// closed room.
const fs = require('fs');
const path = require('path');
const { HubConnectionBuilder, LogLevel } = require(path.join(__dirname, '../../web/play/node_modules/@microsoft/signalr'));

const opt = (name, fallback) => {
  const arg = process.argv.slice(2).find((a) => a === `--${name}` || a.startsWith(`--${name}=`));
  return arg === undefined ? fallback : arg.includes('=') ? arg.slice(arg.indexOf('=') + 1) : true;
};

const server = String(opt('server', 'http://localhost:5080')).replace(/\/$/, '');
const players = Number(opt('players', 3));
const encounter = String(opt('encounter', 'river_crossing'));
const holdMs = Number(opt('hold', 3)) * 1000;
const fight = opt('fight', false) === true;
const area = opt('area', false) === true;
const leave = opt('leave', false) === true;
const then = String(opt('then', 'end'));
const sitOut = String(opt('sit-out', '')).split(',');
const people = Number(opt('people', 0));
const timeoutMs = Number(opt('timeout', 900)) * 1000;

const NAMES = ['Ana', 'Bo', 'Cy', 'Dee', 'Eli', 'Fen'];
const CLASSES = ['fighter', 'cleric', 'rogue', 'wizard', 'paladin', 'ranger'];
const TICK_MS = 250;
/** A turn view that hasn't changed after this long is one the game refused: end the turn instead of waiting for its timer. */
const STUCK_MS = 4000;
/** Long enough for the game's 60 s turn timer to go off first. */
const SIT_OUT_MS = 65000;
/** Retries for a lost connection: a server restart takes some seconds, and a phone doesn't give up. */
const RECONNECT_MS = Array.from({ length: 120 }, () => 1000);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

async function until(what, test) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    const value = test();
    if (value) return value;
    await sleep(TICK_MS);
  }
  throw new Error(`Timed out waiting for ${what}`);
}

async function roomCode() {
  if (opt('room') && opt('room') !== true) return String(opt('room')).toUpperCase();
  const file = opt('room-log');
  if (!file || file === true) throw new Error('Pass --room=<CODE> or --room-log=<file>');
  return until(`ROOM in ${file}`, () => {
    if (!fs.existsSync(file)) return null;
    return [...fs.readFileSync(file, 'utf8').matchAll(/^ROOM (\w+)/gm)].at(-1)?.[1] ?? null;
  });
}

/** One phone: a connection, the newest lobby and view, and what it has acted on. */
class Bot {
  constructor(index) {
    this.index = index;
    this.name = NAMES[index];
    this.lobby = null;
    this.view = null;
    this.ended = null;
    this.closed = null;
    this.acted = null;       // the viewId last answered
    this.seenAt = 0;         // when that view arrived
    this.moved = false;      // walked this turn already
    this.conn = new HubConnectionBuilder().withUrl(`${server}/hub`).withAutomaticReconnect(RECONNECT_MS).configureLogging(LogLevel.Critical).build();
    this.conn.on('LobbyChanged', (l) => { this.lobby = l; });
    this.conn.on('ControllerView', (v) => { this.view = v; this.seenAt = Date.now(); });
    this.conn.on('GameEnded', (r) => { this.ended = r; });
    this.conn.on('RoomClosed', (why) => { this.closed = why || 'closed'; });
    this.conn.on('Kicked', () => { this.closed = 'kicked'; });
    this.conn.onreconnected(() => this.rejoin());
  }

  async join(code) {
    await this.conn.start();
    this.seat = await this.conn.invoke('TakeSeat', { code, newName: this.name, profileKeys: [] });
    await until(`${this.name}'s lobby`, () => this.lobby);
    log(`${this.name} joined ${code}`);
  }

  /** Back in this seat after a lost connection, as a phone does it. */
  async rejoin() {
    try {
      await this.conn.invoke('Rejoin', this.seat.code, this.seat.playerId, this.seat.secret);
      log(`${this.name} rejoined ${this.seat.code}`);
    } catch (e) {
      log(`${this.name}: rejoin failed: ${e.message}`);
    }
  }

  input(action, data) {
    this.acted = this.view.viewId;
    return this.conn.invoke('SendInput', this.view.viewId, data === undefined ? { action } : { action, data })
      .catch((e) => log(`${this.name}: ${action} failed: ${e.message}`));
  }

  /** The player's move on a `turn` screen: one command per view. */
  turn() {
    const { me, units, choices } = this.view.data;
    const foes = units.filter((u) => u.side === 'Enemy');
    const dist = (a, b) => Math.max(Math.abs(a[0] - b[0]), Math.abs(a[1] - b[1]));
    if (fight) {
      if (area) {
        const friends = units.filter((u) => u.side !== 'Enemy').map((u) => u.id);
        const isFoe = (a) => foes.some((f) => f.id === a.unit);
        for (const ability of choices.abilities.filter((a) => !a.item && a.cost?.action)) {
          const target = ability.targets.find((t) => t.affected.filter(isFoe).length >= 2 && !t.affected.some((a) => friends.includes(a.unit)));
          if (target) {
            log(`${this.name} uses ${ability.name} on ${target.affected.length}`);
            return this.input('use', { ability: ability.ability, unit: target.target.unit ?? undefined, tile: target.target.tile ?? undefined });
          }
        }
      }
      for (const ability of choices.abilities.filter((a) => !a.item && a.cost?.action)) {
        const target = ability.targets.find((t) => foes.some((f) => f.id === t.target.unit));
        if (target) {
          log(`${this.name} uses ${ability.name}`);
          return this.input('use', { ability: ability.ability, unit: target.target.unit });
        }
      }
      if (!this.moved && foes.length && choices.moves.length) {
        const near = (tile) => Math.min(...foes.map((f) => dist(tile, f.at)));
        const best = choices.moves.reduce((a, b) => (near(b.to) < near(a.to) ? b : a));
        if (near(best.to) < near(me.at)) {
          this.moved = true;
          return this.input('move', { path: best.path });
        }
      }
    }
    this.moved = false;
    return this.input('endTurn');
  }

  /** Answers the newest view, once. */
  async act(state) {
    const view = this.view;
    if (view?.kind !== 'custom') return;
    const stuck = this.acted === view.viewId && Date.now() - this.seenAt > STUCK_MS;
    if (this.acted === view.viewId && !stuck) return;
    if (stuck) this.seenAt = Date.now();
    switch (view.screen) {
      case 'pickClass':
        if (Date.now() < state.nextPick) return;
        state.nextPick = Date.now() + holdMs;
        log(`${this.name} picks ${CLASSES[this.index]}`);
        return this.input('pickClass', { class: CLASSES[this.index] });
      case 'pickEncounter':
        // The list needs a moment on screen.
        state.pickAt ??= Date.now() + holdMs;
        if (Date.now() < state.pickAt) return;
        state.pickAt = undefined;
        log(`${this.name} picks ${encounter}`);
        return this.input('pickEncounter', { encounter });
      case 'turn':
        if (sitOut.includes('turn') && !state.satOutUntil) {
          state.satOutUntil = Date.now() + SIT_OUT_MS;
          log(`${this.name} sits out the turn: the timer will run out`);
        }
        if (Date.now() < (state.satOutUntil ?? 0)) return;
        return stuck ? this.input('endTurn') : this.turn();
      case 'react':
        if (sitOut.includes('react')) {
          if (state.reactionSeen !== view.viewId) log(`${this.name} sits out a reaction: ${view.data.trigger}`);
          state.reactionSeen = view.viewId;
          return;
        }
        return this.input('react', { choice: 'pass' });
      case 'results':
        if (!view.data.vip) return;
        state.resultsAt ??= Date.now() + holdMs;
        if (Date.now() < state.resultsAt) return;
        state.resultsAt = undefined;
        log(`${view.data.outcome} at ${view.data.encounter}: ${view.data.lines.map((l) => `${l.name} +${l.xp} XP`).join(', ')}`);
        return this.input(then === 'next' ? 'next' : 'end');
    }
  }
}

(async () => {
  const code = await roomCode();
  const bots = Array.from({ length: players }, (_, i) => new Bot(i));
  for (const bot of bots) await bot.join(code);
  const [vip, ...rest] = bots;

  // The lobby's campaign step, as the phones do it.
  await vip.conn.invoke('SelectGame', 'tactics');
  await until('Tactics selected', () => vip.lobby.selectedGameId === 'tactics');
  await vip.conn.invoke('CreateCampaign', `Bots ${Date.now() % 100000}`);
  for (const bot of rest) await bot.conn.invoke('ClaimMember', { member: null, profileKeys: [] });
  if (people > 0) log(`Waiting for ${people} more to join room ${code}`);
  await until(`${people} more to join`, () => vip.lobby.players.filter((p) => p.connected && p.profile).length >= players + people);
  await until('the room to be able to start', () => vip.lobby.canStart || vip.lobby.inGame);
  if (!vip.lobby.inGame) await vip.conn.invoke('StartGame').catch((e) => log(`Start: ${e.message}`));   // --autostart may have got there first
  await until('the game to start', () => vip.lobby.inGame);
  log('Game on');

  const state = { nextPick: Date.now() + holdMs };
  await until('the session to end', () => {
    if (bots.some((b) => b.closed)) throw new Error(`Room closed: ${bots.find((b) => b.closed).closed}`);
    if (leave && bots.some((b) => ['turn', 'status', 'react'].includes(b.view?.screen))) return 'left';
    for (const bot of bots) void bot.act(state);
    return vip.ended;
  });
  if (leave && !vip.ended) log('Battle on: leaving it to the AI');
  else log(`Session over: ${vip.ended.message ?? ''}`);
  await Promise.all(bots.map((b) => b.conn.stop()));
  process.exit(0);
})().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
