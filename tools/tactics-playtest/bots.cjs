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
// Phone views have no position and bots answer each view once, so walking is a scripted loop of its own. Without these modes the
// VIP leaves the walk screen with `battles`, and the run is the battle it always was. With any of them the party walks, and then the
// VIP ends the session. They run in this order:
//
//   --walk="e:4,ne:2,stop:1"  hold each direction for that many seconds, pinging as the pad does; stop rests
//   --walk-latency[=<n>]      n presses (default 20), each timed against the TV's WALK line in --tv-log, or with --host the host view
//   --walk-flood[=<n>]        n seconds (default 10) of the leader alternating a press and a stop at 60 messages a second, far over
//                             the 20 a second the server's limiter lets through, so that stops are lost
//   --walk-drop[=kill|stall]  hold, then cut the connection with no stop, and time the party stopping and the disconnect event from
//                             another phone. kill (the default) ends the sockets with no SignalR or WebSocket goodbye. stall leaves them
//                             open and silent: a phone that just stops. Phones connect through a small proxy here, with stateful
//                             reconnect on, as web/play's transport has it
//   --tv-log=<file>           the TV's stdout, redirected: wait for `TACTICS world open` and read WALK lines from it
//   --host                    this process opens the room and watches the host view, with no TV: enough for --walk-drop
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
const net = require('net');
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
const walkScript = opt('walk') && opt('walk') !== true ? String(opt('walk')).split(',').map((s) => s.split(':')) : null;
const count = (name, fallback) => (opt(name) === undefined ? 0 : opt(name) === true ? fallback : Number(opt(name)));
const latencyPresses = count('walk-latency', 20);
const floodSeconds = count('walk-flood', 10);
const dropMode = opt('walk-drop') === undefined ? null : opt('walk-drop') === true ? 'kill' : String(opt('walk-drop'));
const asHost = opt('host', false) === true;
const tvLog = opt('tv-log') ?? opt('room-log');
const walking = Boolean(walkScript || latencyPresses || floodSeconds || dropMode);

/** The phone's cadence while a direction is held, and its retry after a stop. */
const PING_MS = 250;
const STOP_RETRY_MS = 150;
const FLOOD_MS = 1000 / 60;
/** Long enough for any disconnect event a phone can raise. */
const DROP_WATCH_MS = 120000;

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

/** A socket pair between one phone and the server, which can go away in the two ways a phone does. */
class Line {
  constructor() {
    this.mode = null;
    this.pairs = [];
    const target = new URL(server);
    this.listener = net.createServer((phone) => {
      if (this.mode === 'kill') return phone.destroy();
      if (this.mode === 'stall') return phone.on('error', () => {});   // it answers nothing, as a phone that's gone doesn't
      const up = net.connect(Number(target.port), target.hostname);
      phone.on('error', () => up.destroy());
      up.on('error', () => phone.destroy());
      phone.pipe(up);
      up.pipe(phone);
      this.pairs.push([phone, up]);
    });
  }

  async open() {
    await new Promise((r) => this.listener.listen(0, '127.0.0.1', r));
    return `http://127.0.0.1:${this.listener.address().port}`;
  }

  cut(mode) {
    this.mode = mode;
    for (const [phone, up] of this.pairs) {
      phone.unpipe(up);
      up.unpipe(phone);
      if (mode === 'kill') {
        up.resetAndDestroy();
        phone.destroy();
      } else {
        up.pause();
        phone.pause();
      }
    }
  }
}

/** One phone: a connection, the newest lobby and view, and what it has acted on. */
class Bot {
  constructor(index, url = server) {
    this.index = index;
    this.name = NAMES[index];
    this.lobby = null;
    this.view = null;
    this.ended = null;
    this.closed = null;
    this.acted = null;       // the viewId last answered
    this.seenAt = 0;         // when that view arrived
    this.moved = false;      // walked this turn already
    const builder = new HubConnectionBuilder().withUrl(`${url}/hub`).withAutomaticReconnect(RECONNECT_MS).configureLogging(LogLevel.Critical);
    this.conn = (dropMode ? builder.withStatefulReconnect() : builder).build();
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
      case 'walk':
        if (walking || !view.data.vip) return;
        state.walkAt ??= Date.now() + holdMs;
        if (Date.now() < state.walkAt) return;
        state.walkAt = undefined;
        log(`${this.name} leaves the walk for the battle list`);
        return this.input('battles');
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

/** The TV, or with --host the host view: the newest step shown, and when. */
class Watch {
  constructor(host) {
    this.host = host;
    this.seq = 0;
    this.at = 0;
    this.held = false;
    host?.on('HostView', (_, v) => {
      const e = v?.explore;
      if (!e) return;
      this.held = e.held;
      if (e.seq !== this.seq) [this.seq, this.at] = [e.seq, Date.now()];
    });
  }

  read() {
    if (this.host || !tvLog || !fs.existsSync(tvLog)) return;
    const last = [...fs.readFileSync(tvLog, 'utf8').matchAll(/^WALK (\d+) (-?\d+) (-?\d+) (\d+)/gm)].at(-1);
    if (last) [this.seq, this.at] = [Number(last[1]), Number(last[4])];
  }
}

const walkTo = (bot, dir) => bot.conn.invoke('SendInput', bot.view.viewId, { action: 'walk', data: { dir } })
  .catch((e) => log(`${bot.name}: walk failed: ${e.message}`));

/** Holds `dir` for `ms`, pinging as the pad does. */
async function hold(leader, dir, ms) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    await walkTo(leader, dir);
    await sleep(Math.min(PING_MS, Math.max(0, end - Date.now())));
  }
}

async function release(leader) {
  await walkTo(leader, null);
  await sleep(STOP_RETRY_MS);
  await walkTo(leader, null);
}

async function runScript(leader) {
  for (const [dir, seconds] of walkScript) {
    log(`WALK ${dir} for ${seconds} s`);
    if (dir === 'stop') {
      await release(leader);
      await sleep(Number(seconds) * 1000);
    } else await hold(leader, dir, Number(seconds) * 1000);
  }
  await release(leader);
}

async function runLatency(leader, watch) {
  const times = [];
  const dirs = ['e', 'w'];
  for (let i = 0; i < latencyPresses; i++) {
    watch.read();
    const was = watch.seq;
    const pressed = Date.now();
    await walkTo(leader, dirs[i % 2]);
    while (watch.seq === was && Date.now() - pressed < 1500) {
      await sleep(4);
      watch.read();
    }
    if (watch.seq !== was) times.push(watch.at - pressed);
    else log(`LATENCY press ${i}: no step`);
    await release(leader);
    await sleep(700);    // a step's end, so that the next press steps at once
  }
  times.sort((a, b) => a - b);
  log(`LATENCY ${times.length} of ${latencyPresses} presses, ms: median ${times[times.length >> 1]} p90 ${times[Math.floor(times.length * 0.9)]} worst ${times.at(-1)}`);
}

async function runFlood(leader) {
  log(`FLOOD ${floodSeconds} s`);
  const end = Date.now() + floodSeconds * 1000;
  let press = true;
  while (Date.now() < end) {
    void walkTo(leader, press ? 'e' : null);
    press = !press;
    await sleep(FLOOD_MS);
  }
  await sleep(1100);    // the limiter's bucket back, so the stop can't be lost
  await release(leader);
  log('FLOOD over');
}

/** Holds, cuts the leader's line, and times what the others see. */
async function runDrop(leader, line, others, watch) {
  await hold(leader, 'e', 1000);
  void walkTo(leader, 'e');
  const cut = Date.now();
  line.cut(dropMode);
  log(`DROP ${dropMode} at ${cut}`);
  const seen = {};
  const note = (what, test) => {
    if (seen[what] === undefined && test()) {
      seen[what] = Date.now() - cut;
      log(`DROP ${what} after ${seen[what]} ms`);
    }
  };
  const eye = others[0];
  while (Date.now() - cut < DROP_WATCH_MS && seen.disconnect === undefined) {
    note('party stopped', () => watch.host && !watch.held);
    note('lead passed', () => eye.view?.screen === 'walk' && eye.view.data.leaderName !== leader.name);
    note('disconnect', () => eye.lobby?.players.find((p) => p.name === leader.name)?.connected === false);
    await sleep(10);
  }
  if (watch.host) log(`DROP last step ${watch.at - cut} ms after the cut`);
  if (seen.disconnect === undefined) throw new Error(`No disconnect event in ${DROP_WATCH_MS} ms`);
}

async function walkAbout(bots, lines, watch) {
  const leader = await until('a leader', () => bots.find((b) => b.view?.screen === 'walk' && b.view.data.leader));
  log(`${leader.name} leads`);
  if (tvLog && !asHost) await until('the world on the TV', () => fs.existsSync(tvLog) && fs.readFileSync(tvLog, 'utf8').includes('TACTICS world open'));
  if (walkScript) await runScript(leader);
  if (latencyPresses) await runLatency(leader, watch);
  if (floodSeconds) await runFlood(leader);
  if (dropMode) await runDrop(leader, lines[bots.indexOf(leader)], bots.filter((b) => b !== leader), watch);
  log('WALK done');
  const ender = bots.find((b) => b.view?.data?.vip && (!dropMode || b !== leader));
  if (!ender) return log('The VIP was the one cut: the session stays open');
  await ender.input('endSession');
  await until('the session to end', () => ender.ended);
}

(async () => {
  const host = asHost ? new HubConnectionBuilder().withUrl(`${server}/hub`).configureLogging(LogLevel.Critical).build() : null;
  const watch = new Watch(host);
  let code;
  if (host) {
    await host.start();
    code = (await host.invoke('CreateRoom')).code;
  } else code = await roomCode();
  const lines = dropMode ? Array.from({ length: players }, () => new Line()) : [];
  const urls = await Promise.all(lines.map((l) => l.open()));
  const bots = Array.from({ length: players }, (_, i) => new Bot(i, urls[i]));
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
  if (walking) {
    await until('the walk screen', () => {
      for (const bot of bots) void bot.act(state);
      return bots.some((b) => b.view?.screen === 'walk' && b.view.data.leader);
    });
    await walkAbout(bots, lines, watch);
    process.exit(0);
  }
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
