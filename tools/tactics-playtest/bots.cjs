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
//   --fight              play to win, from what the phone shows: heal a friend who's down or badly hurt first (a healer keeps its
//                        spells for that and attacks with cantrips), else the use that previews the most damage, a kill worth more,
//                        so the party finishes off the hurt foe; never one that would hurt a friend. The party holds the ground it
//                        started on and takes on whatever comes within 7 tiles of it, walking by the map's own ground (round the river
//                        to the ford, never standing in water when dry ground will do); once the foes keep away for two of the party's
//                        turns each, or two or fewer are left, it goes after them, the weakest first. Without --fight the bots only
//                        end their turns. River Crossing at level 1 with 4 bots: 40 won of 40 with no TV (--host --shown)
//   --lose[=<n>]         lose the first n fights (default every one) on purpose: walk up to the foes and do nothing there
//   --classes=a,b,…      the class each bot picks at Muster, in order (default fighter, cleric, rogue, wizard, paladin, ranger)
//   --leave              once the battle is on, every bot disconnects, so the AI plays each hero: a whole battle with nobody
//                        at a phone. It exits at that point; the TV runs on to Results, so give --quit-after room
//   --sit-out=turn,react sit out what's named, so the game's timers run out as they do for a real player who's gone quiet: the
//                        first bot to get a turn leaves it for the 60 s timer, and then the AI finishes it (the TV badges
//                        that), and no reaction prompt is ever answered, so each one stays up for its 10 s
//   --area               prefer an ability that catches two or more foes and no friend, for watching area spells on the TV
//   --people=<n>         wait for n real phones to join and claim a seat before starting, so you can play alongside the bots:
//                        tell the TV's room code to your phone, then choose a class on it (see "With your own phone" below)
//   --then=end|next      after the results: end the session (default), or play the next battle, forever
//   --walk-to-stamp[=<n>] fight n times (default 1) where the party walks into a fight, rather than from the battle list: from wherever
//                        it stands, the leader walks it into the stamp of --encounter's map, a tap a step, by a route that
//                        route.cs works out with the server's own walking rule (it needs dotnet); the step into the stamp starts the
//                        fight. After each results the VIP goes on, so the next walk starts where that fight left the party (after a
//                        loss, the recovery spawn). After the last, the party walks on a few steps and the VIP ends the session. It
//                        needs to know where the party is: --tv-log with the TV's --walk-log, or --host. A beat's scenes (its
//                        lead-in, its aftermath) play through: the leader skips each line after 1.5 s, and every bot that may answer a
//                        vote takes the first option, and a check the best bonus, so no run waits on a 45 s vote
//   --timeout=<seconds>  give up after this long (default 900)
//
// Phone views have no position and bots answer each view once, so walking is a scripted loop of its own. Without these modes the
// VIP leaves the walk screen with `battles`, and the run is the battle it always was. With any of them the party walks, and then the
// VIP ends the session. They run in this order:
//
//   --walk="e:4,ne:2,stop:1"  hold each direction for that many seconds, pinging as the pad does; stop rests. pause:<name> logs
//                             `PAUSE <name>` and waits for a file <name> in --go, so a script can restart the server or kill the TV
//                             there; it then goes on with whoever leads once the phones have a fresh walk screen
//   --go=<dir>                where pause:<name> looks for its file (tools/tv-e2e.sh's ARCBOX_WALK=1 makes them)
//   --walk-latency[=<n>]      n presses (default 20), each timed against the TV's WALK line in --tv-log, or with --host the host view
//   --walk-flood[=<n>]        n seconds (default 10) of the leader alternating a press and a stop at 60 messages a second, far over
//                             the 20 a second the server's limiter lets through, so that stops are lost
//   --walk-drop[=kill|stall]  hold, then cut the connection with no stop, and time the party stopping and the disconnect event from
//                             another phone. kill (the default) ends the sockets with no SignalR or WebSocket goodbye. stall leaves them
//                             open and silent: a phone that just stops. Phones connect through a small proxy here, with stateful
//                             reconnect on, as web/play's transport has it
//   --tv-log=<file>           the TV's stdout, redirected: wait for `TACTICS world open:` and read WALK lines from it, and with the
//                             TV's --walk-log, WALKAT lines, for when the TV heard the party had stopped
//   --host                    this process opens the room and watches the host view, with no TV: enough for --walk-drop
//   --shown                   with --host, answer every beat with `shown` at once, as a TV that draws in no time, so that a battle with
//                             no TV takes seconds rather than minutes: for trying the bots' play over many battles
//
// BOTS_DEBUG=1 in the environment logs the board as each bot sees it on each of its turn views.
//
//   --shop                    earn the council advance, walk to Wenna, buy from an own purse, retry its trade id, sell and leave.
//
// The story (overworld 5d), in place of the battle list:
//
//   --story[=<n>]             the leader taps Interact whenever the walk screen offers it, and the phones answer the story's screens: a
//                             choice or a pick goes to the first of --story-pick that's offered, or else the first option; in a party vote
//                             the bots split between the first two options, so four of them tie and the leader breaks it; who rolls goes
//                             the same way among the cards. Each answer waits a moment, so the tally shows. After n scenes (default 1)
//                             the VIP ends the session. Start the server with a Development seed by the beat (tools/tv-e2e.sh's
//                             ARCBOX_SERVER_ENV), as the bots don't walk to it
//   --story-pick=<ids>        option ids to prefer, comma-separated (e.g. why,insight to take the ford choice's check)
//   --chapter                 all of the chapter, from wherever the party is (a fresh campaign needs no seed): the beat on offer, from
//                             the TV's --story-log `STORY marker` lines (or --host's view), is walked to by route.cs, round every stamp,
//                             each step checked against the TV's WALK line (routed again if it went elsewhere), and Interact tapped once
//                             offered; then the next, a lost fight's "!" again, until the TV shows `STORY card`; then a few steps, and
//                             the VIP ends the session. Every bot votes for --branch and --story-pick's ids, else the first option
//   --branch=force|parley     the option to vote for at the ford (default force)
//   --line=<seconds>          how long a line is up before the leader skips it (default 1.5)
//   --silent                  in a scene nobody answers anything or skips a line: it all runs on the game's timers
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
const answerShown = asHost && opt('shown', false) === true;
const tvLog = opt('tv-log') ?? opt('room-log');
const goDir = opt('go') && opt('go') !== true ? String(opt('go')) : null;
const walking = Boolean(walkScript || latencyPresses || floodSeconds || dropMode);
/** --lose[=<n>]: the fights to lose on purpose, the first n of them, or every one. */
const loseFights = count('lose', Infinity);
/** --walk-to-stamp[=<n>]: fights to walk into, from wherever the party is, one after another. */
const stampFights = count('walk-to-stamp', 1);
/** Fights over so far: the VIP counts them as it leaves each results screen. */
let fightsDone = 0;
/** When the VIP last went on from a results screen. */
let resultsLeft = 0;
const losing = () => fightsDone < loseFights;
/** --story[=<n>] and --story-pick=<ids>: walk to the "!" and play n beats, choosing these options where offered. */
const storyBeats = opt('story') === undefined ? 0 : opt('story') === true ? 1 : Number(opt('story'));
/** --chapter: play all of the region's chapter from wherever the party is, walking to each "!" in turn, to its card and a walk after. */
const chapter = opt('chapter', false) === true;
const shopRun = opt('shop', false) === true;
const recoveryRun = opt('camp', false) === true;
/** --branch=force|parley: the option the party votes for at the ford. */
const branch = String(opt('branch', 'force'));
/** --silent: nobody answers anything or skips a line, so every line, vote, choice and check runs on the game's own timers. */
const silent = opt('silent', false) === true;
const storyPick = [...(chapter ? [branch] : []), ...String(opt('story-pick', '')).split(',').filter(Boolean)];
/** --line=<seconds>: how long a line stays up before the leader's bot skips it. */
const lineMs = Number(opt('line', 1.5)) * 1000;

/** The phone's cadence while a direction is held, and its retry after a stop. */
const PING_MS = 250;
const STOP_RETRY_MS = 150;
const IDLE_PING_MS = 3000;
const FLOOD_MS = 1000 / 60;
/** Long enough for any disconnect event a phone can raise. */
const DROP_WATCH_MS = 120000;

const NAMES = ['Ana', 'Bo', 'Cy', 'Dee', 'Eli', 'Fen'];
const CLASSES = opt('classes') && opt('classes') !== true ? String(opt('classes')).split(',') : ['fighter', 'cleric', 'rogue', 'wizard', 'paladin', 'ranger'];
const TICK_MS = 250;
/** A turn view that hasn't changed after this long is one the game refused: end the turn instead of waiting for its timer. */
const STUCK_MS = 4000;
/** Long enough for the game's 60 s turn timer to go off first. */
const SIT_OUT_MS = 65000;
/** Retries for a lost connection: a server restart takes some seconds, and a phone doesn't give up. */
const RECONNECT_MS = Array.from({ length: 120 }, () => 1000);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
/** Battle tiles are [x, y]; reach and range are Manhattan. */
const dist = (a, b) => Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]);

const RULES = path.join(__dirname, '../../content/tactics/base/rules');
const [WATER, DIFFICULT, IMPASSABLE, DEEP] = [1, 2, 4, 16];
/** Paths.StepCost's climb: a Jump of 2, the default. */
const JUMP = 2;
const maps = new Map();
/**
 * A battle map from its file, as the bots need it: each tile's height and flags, and the tiles a prop blocks. Null for a map they can't
 * read, when they walk by the phone's moves alone.
 */
function mapOf(id) {
  if (!maps.has(id)) {
    let map = null;
    try {
      const file = JSON.parse(fs.readFileSync(path.join(RULES, 'maps', `${id}.json`), 'utf8'));
      const bytes = Buffer.from(file.tiles, 'base64');
      const [w, h] = file.size;
      const blocked = new Set((file.props ?? []).filter((p) => p.blocks).map((p) => `${p.at[0]},${p.at[1]}`));
      map = {
        w, h,
        height: (x, y) => bytes[(y * w + x) * 3],
        flags: (x, y) => bytes[(y * w + x) * 3 + 2],
        walkable: (x, y) => x >= 0 && y >= 0 && x < w && y < h && !(bytes[(y * w + x) * 3 + 2] & (IMPASSABLE | DEEP)) && !blocked.has(`${x},${y}`),
        far: new Map(),
      };
    } catch {
      // walk by the phone's moves alone
    }
    maps.set(id, map);
  }
  return maps.get(id);
}

const inWater = (map, [x, y]) => Boolean(map && x >= 0 && y >= 0 && x < map.w && y < map.h && map.flags(x, y) & WATER);

/**
 * How far each tile is from <goal> on foot, in Move, by Paths.StepCost's rule on the map's own ground (no units, no changed heights):
 * so a hero goes round the river to the ford rather than standing on the bank nearest a foe across it. A flood back from the goal.
 */
function farFrom(map, goal) {
  const key = `${goal[0]},${goal[1]}`;
  if (map.far.has(key)) return map.far.get(key);
  const far = new Map([[key, 0]]);
  const open = [[0, goal[0], goal[1]]];
  while (open.length) {
    open.sort((a, b) => b[0] - a[0]);
    const [cost, x, y] = open.pop();
    if (cost > far.get(`${x},${y}`)) continue;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const [fx, fy] = [x + dx, y + dy];      // a step from (fx, fy) onto (x, y)
      if (!map.walkable(fx, fy)) continue;
      const onto = x === goal[0] && y === goal[1] || map.walkable(x, y);
      if (!onto) continue;
      const climb = map.height(x, y) - map.height(fx, fy);
      if (climb > JUMP) continue;
      const step = (map.flags(x, y) & DIFFICULT ? 2 : 1) + Math.max(0, climb - 1);
      const next = cost + step;
      if (next < (far.get(`${fx},${fy}`) ?? Infinity)) {
        far.set(`${fx},${fy}`, next);
        open.push([next, fx, fy]);
      }
    }
  }
  map.far.set(key, far);
  return far;
}

/**
 * The best use of this turn's action or bonus action, or null: what a player who reads the phone's previews would pick. A heal for a
 * friend who's down or badly hurt comes first; then the most expected damage, a kill worth more, so the party finishes off the hurt foe
 * rather than spreading its blows. Anything that would hurt a friend is out, and so is a use that previews nothing (a buff, say).
 */
function bestUse(units, choices) {
  const byId = new Map(units.map((u) => [u.id, u]));
  const economy = choices.economy ?? {};
  // A healer keeps its spells for healing: it attacks with what costs nothing (a cantrip, a weapon) while it has a heal to cast.
  const healer = choices.abilities.some((a) => !a.item && a.targets.some((t) => t.affected.some((x) => x.heal > 0)));
  let best = null;
  for (const ability of choices.abilities) {
    const cost = ability.cost ?? {};
    if (cost.reaction || (cost.action && !economy.action) || (cost.bonus && !economy.bonus) || (!cost.action && !cost.bonus)) continue;
    for (const target of ability.targets) {
      let score = 0;
      for (const a of target.affected) {
        const u = byId.get(a.unit);
        if (!u) continue;
        if (u.side === 'Enemy') {
          if (u.life !== 'Up') continue;
          const left = u.hp + (u.tempHp ?? 0);
          // a foe whose fall wins the fight (Mother Skarrow) is worth half as much again
          score += (Math.min(a.damage, left) + (a.damage >= left ? 6 : 0)) * (TARGETS.has(u.id) ? 1.5 : 1);
        } else {
          if (a.damage > 0) score -= 100;
          const missing = u.maxHp - u.hp;
          if (a.heal > 0 && (u.life === 'Downed' || u.hp * 2 <= u.maxHp)) score += Math.min(a.heal, missing) + (u.life === 'Downed' ? 30 : 4);
        }
      }
      const heals = target.affected.some((x) => x.heal > 0);
      if (healer && !heals && cost.resource && Object.keys(cost.resource).length) score -= 8;
      // An item is spent for good, so it has to be worth it; and an action beats a bonus action of the same worth, as the bonus can follow.
      if (ability.item) score -= 3;
      if (cost.bonus && !cost.action) score -= 0.5;
      if (score >= 1 && (!best || score > best.score)) best = { score, ability, target };
    }
  }
  return best;
}

/** The foes whose fall wins a fight, from every encounter's `defeat:<id>` objectives. */
const TARGETS = new Set((() => {
  try {
    return fs.readdirSync(path.join(RULES, 'encounters')).filter((f) => f.endsWith('.json')).flatMap((f) => {
      const win = JSON.parse(fs.readFileSync(path.join(RULES, 'encounters', f), 'utf8')).objectives?.win ?? {};
      return [...(win.any ?? []), ...(win.all ?? [])].filter((o) => typeof o === 'string' && o.startsWith('defeat:')).map((o) => o.slice(7));
    });
  } catch {
    return [];     // no encounters to read: every foe is worth the same
  }
})());

/** The weakest foe, the nearest of those equally weak: the one the whole party goes for; but a foe whose fall wins it before any. */
const weakest = (foes, from) => foes.find((f) => TARGETS.has(f.id))
  ?? foes.reduce((a, b) => (b.hp < a.hp || (b.hp === a.hp && dist(from, b.at) < dist(from, a.at)) ? b : a));

/**
 * Where to walk to get nearer <goal>, or null to stand: the reachable tile nearest it on foot, on dry ground if that gets nearer (a hero
 * in the ford fights from the river bed, and a goblin on the bank above doesn't), the ford only when nothing dry does, and the cheapest
 * of those equally near.
 */
function stepTowards(data, goal, { wade = true, within = 0 } = {}) {
  const { me, choices } = data;
  const map = mapOf(data.map);
  const far = map ? farFrom(map, goal) : null;
  const near = (tile) => far?.get(`${tile[0]},${tile[1]}`) ?? 1000 + dist(tile, goal);
  if (dist(me.at, goal) <= within) return null;
  const closest = (moves) => moves.reduce((a, b) => (near(b.to) < near(a.to) || (near(b.to) === near(a.to) && b.cost < a.cost) ? b : a), moves[0]);
  const dry = choices.moves.filter((m) => !inWater(map, m.to));
  for (const moves of wade ? [dry, choices.moves] : [dry]) {
    const best = moves.length ? closest(moves) : null;
    if (best && near(best.to) < near(me.at)) return best;
  }
  return null;
}

/**
 * How the party fights a battle, shared by every bot: each holds the ground it started on and lets the foes come to it, taking on any
 * that come within ENGAGE tiles of the party's start, so it fights together and out of reach of whatever shoots from afar. Once the foes
 * have kept away for a while (QUIET turns each of the party's in a row with nobody near), or two or fewer are left, it goes after them,
 * the weakest first. Without that, a party would stand at its start for ever while an archer across the river kept its distance.
 */
const ENGAGE = 7;
const QUIET = 2;
const tactic = { home: new Map(), quiet: 0, advance: false };
function newBattle() {
  tactic.home.clear();
  tactic.quiet = 0;
  tactic.advance = false;
}

const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

/** The TV's log or the host view (Watch), for the VIP to wait for the TV's results. */
let tv = null;
const RESULTS_WAIT_MS = 60000;

/** Every bot, for a timeout to say what each was looking at. */
const everyone = [];

async function until(what, test) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    const value = test();
    if (value) return value;
    await sleep(TICK_MS);
  }
  const screens = everyone.map((b) => `${b.name} on ${b.view?.screen ?? 'nothing'}`).join(', ');
  throw new Error(`Timed out waiting for ${what}${screens ? ` (${screens})` : ''}`);
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
      if (phone.destroyed || up.destroyed) continue;
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
    this.rejoining = 0;      // when it last set about rejoining its seat
    const builder = new HubConnectionBuilder().withUrl(`${url}/hub`).withAutomaticReconnect(RECONNECT_MS).configureLogging(LogLevel.Critical);
    this.conn = (dropMode ? builder.withStatefulReconnect() : builder).build();
    this.conn.on('LobbyChanged', (l) => { this.lobby = l; });
    this.conn.on('ControllerView', (v) => { this.view = v; this.seenAt = Date.now(); });
    this.conn.on('GameEnded', (r) => { this.ended = r; });
    this.conn.on('RoomClosed', (why) => { this.closed = why || 'closed'; });
    this.conn.on('Kicked', () => { this.closed = 'kicked'; });
    this.conn.onreconnected(() => this.rejoin());
    everyone.push(this);
  }

  async join(code) {
    await this.conn.start();
    this.seat = await this.conn.invoke('TakeSeat', { code, newName: this.name, profileKeys: [] });
    await until(`${this.name}'s lobby`, () => this.lobby);
    log(`${this.name} joined ${code}`);
  }

  /** Back in this seat after a lost connection, as a phone does it. */
  async rejoin() {
    this.rejoining = Date.now();
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
    const foes = units.filter((u) => u.side === 'Enemy' && u.life === 'Up');
    if (process.env.BOTS_DEBUG && this.debugSeen !== this.view.viewId) {
      this.debugSeen = this.view.viewId;
      log(`  ${this.name} at ${me.at} hp ${me.hp}/${me.maxHp} mv ${choices.economy?.moveLeft} | ${units.map((u) => `${u.name.slice(0, 6)}${u.side === 'Enemy' ? '!' : ''}@${u.at}:${u.hp}${u.life === 'Up' ? '' : u.life[0]}`).join(' ')}`);
    }
    if (losing()) return this.loseTurn(me, foes, choices);
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
      const best = bestUse(units, choices);
      if (best) {
        const whom = units.find((u) => u.id === best.target.target.unit)?.name;
        log(`${this.name} uses ${best.ability.name}${whom ? ` on ${whom}` : ''}`);
        return this.input('use', {
          ability: best.ability.ability, item: best.ability.item ?? undefined,
          unit: best.target.target.unit ?? undefined, tile: best.target.target.tile ?? undefined,
        });
      }
      if (!tactic.home.has(this.name)) tactic.home.set(this.name, me.at);
      const home = tactic.home.get(this.name);
      const starts = [...tactic.home.values()];
      const close = foes.filter((f) => starts.some((h) => dist(f.at, h) <= ENGAGE));
      if (close.length) tactic.quiet = 0;
      if (!tactic.advance && foes.length <= 2) {
        tactic.advance = true;
        log(`${this.name}: ${foes.length} foe${foes.length === 1 ? '' : 's'} left, so the party goes after them`);
      }
      // Walk only while there's still an action to take where the walk ends: to the weakest foe near the party, or once the party goes
      // after them, the weakest of all; otherwise back to where this hero started.
      if (!this.moved && choices.economy?.action && foes.length) {
        const after = tactic.advance ? foes : close;
        const to = after.length ? stepTowards(this.view.data, weakest(after, me.at).at) : stepTowards(this.view.data, home, { wade: false, within: 1 });
        if (to) {
          this.moved = true;
          log(`${this.name} moves${after.length ? '' : ' back'}`);
          return this.input('move', { path: to.path });
        }
      }
      if (!close.length && !tactic.advance && ++tactic.quiet >= QUIET * Math.max(1, tactic.home.size)) {
        tactic.advance = true;
        log(`${this.name}: the foes keep away, so the party goes after them`);
      }
    }
    this.moved = false;
    log(`${this.name} ends the turn`);
    return this.input('endTurn');
  }

  /** --lose: walk up to the foes and stand there, doing nothing, so the party falls. */
  loseTurn(me, foes, choices) {
    if (!this.moved && foes.length && choices.moves.length) {
      const near = (tile) => Math.min(...foes.map((f) => dist(tile, f.at)));
      const best = choices.moves.reduce((a, b) => (near(b.to) < near(a.to) ? b : a));
      if (near(best.to) < near(me.at)) {
        this.moved = true;
        log(`${this.name} walks into the foes`);
        return this.input('move', { path: best.path });
      }
    }
    this.moved = false;
    log(`${this.name} ends the turn`);
    return this.input('endTurn');
  }

  /** --story: the leader taps Interact when it's offered; once the scenes are done, the VIP ends the session. */
  storyWalk(state) {
    const { data, viewId } = this.view;
    if (data.interact && state.interacted !== viewId) {
      state.interactAt ??= Date.now() + holdMs * 5;   // the "!" has a moment on the TV first
      if (Date.now() < state.interactAt) return;
      state.interactAt = undefined;
      state.interacted = viewId;
      state.scenes = (state.scenes ?? 0) + 1;
      log(`${this.name} taps ${data.interact} (scene ${state.scenes})`);
      return this.input('interact');
    }
    if (data.vip && (state.scenes ?? 0) >= storyBeats && !data.interact) {
      state.endAt ??= Date.now() + holdMs * 2;
      if (Date.now() < state.endAt) return;
      log(`${this.name} ends the session after ${state.scenes} scene(s)`);
      return this.input('endSession');
    }
  }

  /** --story: a vote, a choice, a tie-break or who rolls, answered after a moment that grows with the bot's seat, so the tally builds. */
  storyAnswer(state) {
    const { screen, data, viewId } = this.view;
    if (!data.canChoose || data.mine) return;
    const key = `${this.index}:${viewId}`;
    state.answerAt ??= {};
    state.answerAt[key] ??= Date.now() + holdMs * (1 + this.index);
    if (Date.now() < state.answerAt[key]) return;
    const ids = screen === 'vote' ? data.options.map((o) => o.id) : data.cards.map((c) => c.id);
    const offered = data.tie && data.tied?.length ? ids.filter((id) => data.tied.includes(id)) : ids;
    const preferred = storyPick.find((id) => offered.includes(id));
    const pick = data.tie || (screen === 'vote' && !data.party) ? preferred ?? offered[0]
      : preferred && screen === 'check' ? preferred : offered[this.index % Math.min(2, offered.length)];
    log(`${this.name} ${screen === 'vote' ? 'votes' : 'picks'} ${pick}${data.tie ? ' (tie-break)' : ''}`);
    return this.input(screen === 'vote' ? 'vote' : 'pickRoller', screen === 'vote' ? { option: pick } : { member: pick });
  }

  /** Answers the newest view, once. */
  async act(state) {
    const view = this.view;
    if (view?.kind !== 'custom') return;
    if (this.index === 0 && view.screen === 'dialogue' && view.data.line && view.data.line !== state.lineSeen) {
      state.lineSeen = view.data.line;
      log(`LINE ${view.data.line} ${view.data.speaker ?? 'narrator'}`);
    }
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
        if (stuck) log(`${this.name}: the game refused that, so ends the turn`);
        return stuck ? this.input('endTurn') : this.turn();
      case 'react':
        if (sitOut.includes('react')) {
          if (state.reactionSeen !== view.viewId) log(`${this.name} sits out a reaction: ${view.data.trigger}`);
          state.reactionSeen = view.viewId;
          return;
        }
        return this.input('react', { choice: 'pass' });
      // A scene (overworld 5d): the leader skips each line once it has been up a moment, and votes, choices and checks get an answer
      // at once, so a run waits on no 45 s vote. The game's own timers would get there too.
      case 'dialogue':
        if (silent || !view.data.canSkip || !view.data.line) return;
        // With a TV, a line is skipped only once the TV has taken it (its --story-log STORY line), and the dwell counts from then, as
        // someone reading the TV would: a slow TV otherwise missed a line skipped the moment it came up. At most a minute's wait.
        if (tv && !tv.host && tvLog) {
          tv.read();
          state.lineSince = state.lineFor === view.data.line ? state.lineSince : Date.now();
          state.lineFor = view.data.line;
          if (!tv.storyShown?.has(view.data.line) && Date.now() - state.lineSince < RESULTS_WAIT_MS) return;
        }
        state.lineAt ??= Date.now() + lineMs;
        if (Date.now() < state.lineAt) return;
        state.lineAt = undefined;
        log(`${this.name} skips line ${view.data.line}`);
        return this.input('skip', { line: view.data.line });
      case 'vote': {
        if (storyBeats) return this.storyAnswer(state);
        if (silent || !view.data.canChoose || view.data.mine || !view.data.options?.length) return;
        const ids = view.data.options.map((o) => o.id);
        const pick = storyPick.find((id) => ids.includes(id)) ?? ids[0];
        log(`${this.name} votes ${pick}`);
        return this.input('vote', { option: pick });
      }
      case 'check': {
        if (storyBeats) return this.storyAnswer(state);
        if (silent) return;
        if (!view.data.canChoose || view.data.mine || !view.data.cards?.length) return;
        const best = view.data.cards.reduce((a, b) => (b.bonus > a.bonus ? b : a));
        log(`${this.name} picks ${best.name} to roll ${view.data.skill}`);
        return this.input('pickRoller', { member: best.id });
      }
      case 'camp-job':
        if (!view.data.canPick || view.data.picked) return;
        return this.input('campJob', { job: view.data.jobs.find(j => j.id === 'tend')?.can ? 'tend' : 'cook' });
      case 'camp-target': {
        if (!view.data.canPick || view.data.picked?.target) return;
        const target = view.data.targets.find(t => t.can);
        return target ? this.input('campJob', { job: view.data.job, target: target.member, injury: target.injury }) : this.input('campJob', { job: 'cook' });
      }
      case 'camp-roll': return; // The server's result and dawn timers run; nobody votes for a roller.
      case 'walk':
        newBattle();
        if (storyBeats) return this.storyWalk(state);
        if (walking || stampFights || chapter || shopRun || !view.data.vip) return;
        state.walkAt ??= Date.now() + holdMs;
        if (Date.now() < state.walkAt) return;
        state.walkAt = undefined;
        log(`${this.name} leaves the walk for the battle list`);
        return this.input('battles');
      case 'results':
        newBattle();
        if (!view.data.vip) return;
        // With a TV, the results stay up until it shows them too: the bots can play faster than it draws, so it can be a few beats
        // behind, and a VIP who went on at once would take the results off the air before the last blow had landed. At most a minute.
        state.resultsSince ??= Date.now();
        if (tv && !tv.host && tvLog) {
          tv.read();
          if (!(tv.resultsShown > (tv.battleShown ?? 0)) && Date.now() - state.resultsSince < RESULTS_WAIT_MS) return;
        }
        state.resultsAt ??= Date.now() + holdMs;
        if (Date.now() < state.resultsAt) return;
        [state.resultsAt, state.resultsSince] = [undefined, undefined];
        log(`${view.data.outcome} at ${view.data.encounter}: ${view.data.lines.map((l) => `${l.name} +${l.xp} XP`).join(', ')}`);
        resultsLeft = Date.now();
        fightsDone++;
        return this.input(then === 'next' || stampFights || chapter ? 'next' : 'end');
    }
  }
}

/** The TV, or with --host the host view: the newest step shown, and when. */
class Watch {
  constructor(host) {
    this.host = host;
    this.seq = 0;
    this.at = 0;
    this.shown = [];
    this.held = false;
    this.shownSeq = 0;
    host?.on('HostView', (_, v) => {
      // --shown: answer each new beat at once, as a TV that draws in no time, so a battle with no TV isn't paced by the fallbacks
      const beat = v?.battle?.beats?.at(-1)?.seq;
      if (answerShown && beat !== undefined && beat !== this.shownSeq) {     // a beat's seq only climbs, until the next battle
        this.shownSeq = beat;
        host.invoke('HostSignal', 'shown', { seq: beat }).catch(() => {});
      }
      if (v?.chapter && !this.card) this.card = Date.now();
      const e = v?.explore;
      if (!e) return;
      const marks = (e.markers ?? []).map((m) => ({ beat: m.beat, at: m.at, emote: m.emote ?? null }));
      if (JSON.stringify(marks) !== JSON.stringify(this.markers)) [this.markers, this.markersSeen] = [marks, Date.now()];
      this.held = e.held;
      if (String(e.at) !== String(this.tile) || e.epoch !== this.epoch) [this.tile, this.epoch, this.placed] = [e.at, e.epoch, Date.now()];
      if (e.seq !== this.seq) [this.seq, this.at] = [e.seq, Date.now()];
    });
  }

  /** Reads what the TV has added to its log since the last read. */
  read() {
    if (this.host || !tvLog || !fs.existsSync(tvLog)) return;
    const size = fs.statSync(tvLog).size;
    if (size < (this.pos ?? 0)) [this.pos, this.rest] = [0, ''];    // a new log
    if (size === (this.pos ?? 0)) return;
    const fd = fs.openSync(tvLog, 'r');
    const chunk = Buffer.alloc(size - (this.pos ?? 0));
    fs.readSync(fd, chunk, 0, chunk.length, this.pos ?? 0);
    fs.closeSync(fd);
    this.pos = size;
    const lines = ((this.rest ?? '') + chunk.toString('utf8')).split('\n');
    this.rest = lines.pop();
    for (const line of lines) {
      // STORY marker lines come one a beat, together: a run of them is the whole set
      this.lineNo = (this.lineNo ?? 0) + (/^STORY marker /.test(line) ? 0 : 1);
      let m = /^WALK (\d+) (-?\d+) (-?\d+) (\d+)/.exec(line);
      if (m) [this.seq, this.at, this.tile, this.placed] = [Number(m[1]), Number(m[4]), [Number(m[2]), Number(m[3])], Date.now()];
      // the TV's screens: a fight's, and its results, which the VIP waits for as someone watching the TV would
      if (/^TACTICS screen tactics-battle-/.test(line)) this.battleShown = Date.now();
      if (line === 'TACTICS screen tactics-results') this.resultsShown = Date.now();
      // --story-log: the beats on offer, each time they change, and a chapter's card
      m = /^STORY marker (\S+)(?: (-?\d+) (-?\d+) (\S+))?$/.exec(line);
      if (m) {
        if (this.markerLines !== this.lineNo) [this.markers, this.markerLines] = [[], this.lineNo];
        if (m[1] !== 'none') this.markers.push({ beat: m[1], at: [Number(m[2]), Number(m[3])], emote: m[4] === '-' ? null : m[4] });
        this.markersSeen = Date.now();
      }
      if (/^STORY card /.test(line)) this.card ??= Date.now();
      // --story-log: each line the TV has taken, said or shown
      m = /^STORY \d+ (\S+) (?:said|shown) /.exec(line);
      if (m) (this.storyShown ??= new Set()).add(m[1]);
      // a snap puts the party somewhere new (a first view, a new epoch): at a tile's middle
      m = /^TACTICS walk snapped to (-?[\d.]+) (-?[\d.]+)/.exec(line);
      if (m) [this.tile, this.placed] = [[Math.floor(Number(m[1])), Math.floor(Number(m[2]))], Date.now()];
      // --walk-log's WALKAT lines: whether a direction is held, as the TV heard
      m = /^WALKAT (\d+) \d+ ([01]) /.exec(line);
      if (m) {
        this.shown.push({ at: Number(m[1]), held: m[2] === '1' });
        this.held = m[2] === '1';
      }
    }
  }

  /** When the TV first drew the party with nothing held, after <since>, by its clock; or undefined. */
  stoppedAfter(since) {
    return this.shown?.find((s) => s.at > since && !s.held)?.at;
  }
}

const walkTo = (bot, dir) => {
  bot.walkSent = Date.now();
  return bot.conn.invoke('SendInput', bot.view.viewId, { action: 'walk', data: { dir } })
    .catch((e) => log(`${bot.name}: walk failed: ${e.message}`));
};

/**
 * The pad's idle ping: the leader's phone says it's stopped every IDLE_PING_MS while nothing is held, so a quiet leader keeps the lead
 * (the server passes it on after 8 s of silence). Every bot does it whenever it leads, until it's cut off.
 */
function idlePings(bots) {
  setInterval(() => {
    for (const bot of bots)
      if (!bot.cutOff && bot.conn.state === 'Connected' && bot.view?.screen === 'walk' && bot.view.data.leader
        && Date.now() - (bot.walkSent ?? 0) >= IDLE_PING_MS) void walkTo(bot, null);
  }, 100).unref();
}

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

/** Whoever leads, once every phone is connected and has a walk screen from since it last rejoined. */
const leaderOf = (bots) => until('a leader', () =>
  bots.every((b) => b.conn.state === 'Connected' && b.view?.screen === 'walk' && b.seenAt >= b.rejoining) && bots.find((b) => b.view.data.leader));

/** Waits for the file <name> in --go, and gives back who leads after it. */
async function pause(bots, name) {
  if (!goDir) throw new Error(`pause:${name} needs --go=<dir>`);
  log(`PAUSE ${name}`);
  const file = path.join(goDir, name);
  await until(`${file} to go on`, () => fs.existsSync(file));
  const leader = await leaderOf(bots);
  log(`GO ${name}: ${leader.name} leads`);
  return leader;
}

async function runScript(bots, leader) {
  for (const [dir, seconds] of walkScript) {
    if (dir === 'pause') {
      leader = await pause(bots, seconds);
      continue;
    }
    log(`WALK ${dir} for ${seconds} s`);
    if (dir === 'stop') {
      await release(leader);
      await sleep(Number(seconds) * 1000);
    } else await hold(leader, dir, Number(seconds) * 1000);
  }
  await release(leader);
  return leader;
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
  leader.cutOff = true;
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
    watch.read();
    note('party stopped', () => (watch.host ? !watch.held : watch.stoppedAfter(cut) !== undefined));
    note('lead passed', () => eye.view?.screen === 'walk' && eye.view.data.leaderName !== leader.name);
    note('disconnect', () => eye.lobby?.players.find((p) => p.name === leader.name)?.connected === false);
    await sleep(10);
  }
  watch.read();
  if (watch.host || tvLog) log(`DROP last step ${watch.at - cut} ms after the cut`);
  if (!watch.host && tvLog) log(`DROP the TV drew the party stopped ${(watch.stoppedAfter(cut) ?? NaN) - cut} ms after the cut, by its clock`);
  if (seen.disconnect === undefined) throw new Error(`No disconnect event in ${DROP_WATCH_MS} ms`);
}

async function walkAbout(bots, lines, watch) {
  let leader = await leaderOf(bots);
  log(`${leader.name} leads`);
  if (tvLog && !asHost) await until('the world on the TV', () => fs.existsSync(tvLog) && fs.readFileSync(tvLog, 'utf8').includes('TACTICS world open:'));
  if (walkScript) leader = await runScript(bots, leader);
  if (latencyPresses) await runLatency(leader, watch);
  if (floodSeconds) await runFlood(leader);
  if (dropMode) await runDrop(leader, lines[bots.indexOf(leader)], bots.filter((b) => b !== leader), watch);
  log('WALK done');
  const ender = bots.find((b) => b.view?.data?.vip && (!dropMode || b !== leader));
  if (!ender) return log('The VIP was the one cut: the session stays open');
  await ender.input('endSession');
  await until('the session to end', () => ender.ended);
}

const inBattle = (bots) => bots.some((b) => ['turn', 'status', 'react'].includes(b.view?.screen));
/** A scene is on: the beat's lead-in, before its fight (overworld 5d). */
const inStory = (bots) => bots.some((b) => ['dialogue', 'vote', 'check'].includes(b.view?.screen));

/** Waits up to <ms> for <test>, and gives back what it last said. */
async function within(ms, test) {
  const end = Date.now() + ms;
  let value = test();
  while (!value && Date.now() < end) {
    await sleep(20);
    value = test();
  }
  return value;
}

/** Where the encounter is fought in the world: the region whose stamp is its map, from the content. */
function stampOf(id) {
  const { map } = JSON.parse(fs.readFileSync(path.join(RULES, 'encounters', `${id}.json`), 'utf8'));
  for (const file of fs.readdirSync(path.join(RULES, 'world')).filter((f) => f.endsWith('.json'))) {
    const region = JSON.parse(fs.readFileSync(path.join(RULES, 'world', file), 'utf8'));
    if ((region.stamps ?? []).some((s) => s.map === map)) return { region: region.id, map };
  }
  throw new Error(`No region stamps ${map}, the map of ${id}`);
}

/**
 * The pad directions from <tile>, one a step, by the server's own walking rule (route.cs): into the stamp of <stamp>'s map, or with
 * <beat>, into reach of that beat's "!" (none if it's in reach already).
 */
async function routeFrom(stamp, tile, beat = null) {
  const { execFile } = require('child_process');
  const goal = beat ? beat.startsWith('shop:') ? ['shop', beat.slice(5)] : beat.startsWith('thing:') ? ['thing', beat.slice(6)] : ['beat', beat] : [stamp.map];
  const args = ['run', path.join(__dirname, 'route.cs'), '--', RULES, stamp.region, ...goal, String(tile[0]), String(tile[1])];
  const [out, err] = await new Promise((resolve) => execFile('dotnet', args, { encoding: 'utf8' },
    (e, stdout, stderr) => resolve([String(stdout ?? '').trim(), `${stderr ?? ''}${e && !stderr ? e.message : ''}`.trim()])));
  const line = out.split('\n').at(-1);
  if (line === 'here') return [];      // there already: in reach of the beat, or on the stamp
  if (!line || line === 'none' || !/^[nsew ]+$/.test(line))
    throw new Error(`No way from ${tile} ${beat ? `to ${beat}` : `into ${stamp.map}'s stamp`} (route.cs said: ${out || 'nothing'}${err ? `; ${err.slice(0, 400)}` : ''})`);
  return line.split(' ');
}

/** The tile change of a screen direction, as WorldWalk.Delta has it. */
const DELTA = { n: [-1, -1], ne: [0, -1], e: [1, -1], se: [1, 0], s: [1, 1], sw: [0, 1], w: [-1, 1], nw: [-1, 0] };

/**
 * --walk-to-stamp: the leader walks the party from where it stands into the stamp, a tap a step (a press and a release: the server takes
 * a tap as one step, latched if the party is still on its way), waiting for each step before the next, as the TV log's WALK lines or
 * the host view say. The step into the stamp starts the fight, so it shows as the phones' battle screens rather than as a step.
 * <after>: the party must have been put somewhere since then (a snap, a new epoch), so the walk starts from where the last fight left it.
 */
async function walkIntoStamp(bots, watch, stamp, after) {
  if (tvLog && !asHost) await until('the world on the TV', () => fs.existsSync(tvLog) && fs.readFileSync(tvLog, 'utf8').includes('TACTICS world open:'));
  const tile = await until('where the party stands', () => {
    watch.read();
    return watch.placed > after && watch.tile;
  });
  let leader = await leaderOf(bots);
  const dirs = await routeFrom(stamp, tile);
  log(`STAMP ${leader.name} walks ${dirs.length} steps from ${tile} into ${stamp.map}: ${dirs.join(' ')}`);
  for (const [i, dir] of dirs.entries()) {
    watch.read();
    const was = watch.seq;
    leader = bots.find((b) => b.view?.screen === 'walk' && b.view.data.leader) ?? leader;
    await walkTo(leader, dir);
    await walkTo(leader, null);
    const went = await within(4000, () => {
      watch.read();
      return watch.seq !== was || inBattle(bots) || inStory(bots);
    });
    if (inStory(bots) || inBattle(bots)) return startsFight(bots, `at step ${i + 1} of ${dirs.length}`);
    if (!went) throw new Error(`Step ${i + 1} of ${dirs.length} (${dir}, from ${watch.tile}) never went`);
  }
  if (!(await within(5000, () => inBattle(bots) || inStory(bots)))) throw new Error(`The step into ${stamp.map}'s stamp started no fight`);
  return startsFight(bots, 'after the last step');
}

/** The step into the stamp went in: through the beat's lead-in, if it has one (the bots answer it as they go), to the fight. */
async function startsFight(bots, when) {
  if (inStory(bots)) {
    log(`STAMP a scene starts ${when}`);
    await until('the fight after the scene', () => inBattle(bots));
  }
  log(`STAMP the fight starts ${when}`);
}

/** After the last fight (or the chapter): walk on a little, any way that's open, so the TV shows the party walking again. */
async function walkOn(bots, watch, label = 'STAMP') {
  const leader = await leaderOf(bots);
  for (const dir of ['w', 'sw', 'nw', 's', 'n', 'e', 'ne', 'se']) {
    watch.read();
    const was = watch.seq;
    await hold(leader, dir, 1500);
    await release(leader);
    await sleep(600);
    watch.read();
    if (watch.seq - was >= 3) return log(`${label} walking on: ${watch.seq - was} steps ${dir}`);
  }
  log(`${label} walking on: no way was open`);
}

/** --walk-to-stamp=<n>: n fights, each walked into from wherever the last left the party, then a few steps on, and the session ends. */
async function fightAtStamps(bots, watch, state) {
  const stamp = stampOf(encounter);
  setInterval(() => {
    if (bots.some((b) => b.closed)) {
      console.error(`Room closed: ${bots.find((b) => b.closed).closed}`);
      process.exit(1);
    }
    for (const bot of bots) void bot.act(state);
  }, TICK_MS).unref();
  await until('the walk screen', () => bots.some((b) => b.view?.screen === 'walk' && b.view.data.leader));
  let after = -1;
  for (let i = 0; i < stampFights; i++) {
    await walkIntoStamp(bots, watch, stamp, after);
    const started = Date.now();
    log(`FIGHT ${i + 1} of ${stampFights}, ${losing() ? 'to lose' : 'to win'}`);
    await until(`fight ${i + 1} to end`, () => fightsDone > i && bots.every((b) => b.view?.screen === 'walk'));
    // Where the fight left the party: the host view has it as the fight ends, the TV once the results are over. A TV that came back
    // mid-fight snaps the walk to where it stood before the fight, so for the TV only a snap after the results counts.
    after = watch.host ? started : resultsLeft;
  }
  await walkOn(bots, watch);
  const vip = bots[0];
  await vip.input('endSession');
  await until('the session to end', () => vip.ended);
  log(`Session over: ${vip.ended.message ?? ''}`);
}

const onWalk = (bots) => bots.every((b) => b.view?.screen === 'walk');

/**
 * --chapter: walks the party into reach of the beat on offer, a tap a step by route.cs, checking each step lands where the route said
 * (a party put somewhere the bots didn't know of is routed again from where the step went), and taps Interact once it's offered. A
 * step into an armed stamp on the way starts the beat as well. Gives back once the beat has begun.
 */
async function walkToBeat(bots, watch, region, marker) {
  for (let attempt = 1; attempt <= 6; attempt++) {
    const tile = await until('where the party stands', () => {
      watch.read();
      return watch.tile;
    });
    const dirs = await routeFrom({ region }, tile, marker.beat);
    let leader = await leaderOf(bots);
    log(`CHAPTER ${leader.name} walks ${dirs.length} steps from ${tile} to ${marker.beat}${attempt > 1 ? ` (route ${attempt})` : ''}`);
    let at = tile;
    let off = false;
    for (const [i, dir] of dirs.entries()) {
      watch.read();
      const was = watch.seq;
      leader = bots.find((b) => b.view?.screen === 'walk' && b.view.data.leader) ?? leader;
      await walkTo(leader, dir);
      await walkTo(leader, null);
      const went = await within(4000, () => {
        watch.read();
        return watch.seq !== was || !onWalk(bots);
      });
      if (!onWalk(bots)) return log(`CHAPTER ${marker.beat} begins at step ${i + 1} of ${dirs.length}: a stamp`);
      at = [at[0] + DELTA[dir][0], at[1] + DELTA[dir][1]];
      if (!went || String(watch.tile) !== String(at)) {
        log(`CHAPTER step ${i + 1} (${dir}) ${went ? `went to ${watch.tile}, not ${at}` : 'never went'}: routing again`);
        off = true;
        break;
      }
    }
    if (off) continue;
    const lead = await within(5000, () => bots.find((b) => b.view?.screen === 'walk' && b.view.data.leader && b.view.data.interact));
    if (!lead) {
      log(`CHAPTER no Interact at ${at}: routing again`);
      continue;
    }
    log(`CHAPTER ${lead.name} taps ${lead.view.data.interact}: ${marker.beat}`);
    await lead.input('interact', marker.beat.startsWith('shop:') ? { target: marker.beat } : marker.beat.startsWith('thing:') ? { target: marker.beat.slice(6) } : undefined);
    if (await within(5000, () => !onWalk(bots))) return log(`CHAPTER ${marker.beat} begins`);
    log('CHAPTER the tap started nothing: trying again');
  }
  throw new Error(`Couldn't get to ${marker.beat}`);
}

/**
 * --chapter: from wherever the party is, the beat on offer (the TV's STORY marker lines, or the host view), walked to and played, and
 * so on to the chapter's card; then a walk, and the session ends. A fight lost puts its "!" back, so it's walked to again.
 */
async function playChapter(bots, watch, state) {
  const region = JSON.parse(fs.readFileSync(path.join(RULES, 'world', 'ashen_ford.json'), 'utf8')).id;
  setInterval(() => {
    if (bots.some((b) => b.closed)) {
      console.error(`Room closed: ${bots.find((b) => b.closed).closed}`);
      process.exit(1);
    }
    for (const bot of bots) void bot.act(state);
  }, TICK_MS).unref();
  idlePings(bots);
  await until('the walk screen', () => bots.some((b) => b.view?.screen === 'walk' && b.view.data.leader));
  if (tvLog && !asHost) await until('the world on the TV', () => fs.existsSync(tvLog) && fs.readFileSync(tvLog, 'utf8').includes('TACTICS world open:'));
  let since = 0;
  let camped = false, inn = false;
  for (let n = 1; ; n++) {
    const marker = await until('a "!" on offer, or the chapter\'s card', () => {
      watch.read();
      if (watch.card) return 'card';
      return onWalk(bots) && watch.markersSeen > since && watch.markers?.find((m) => m.emote === 'alert');
    });
    if (marker === 'card') break;
    log(`CHAPTER ${n}: ${marker.beat} at ${marker.at}`);
    since = Date.now();
    await walkToBeat(bots, watch, region, marker);
    await until(`${marker.beat} to be over`, () => {
      watch.read();
      return watch.card || (onWalk(bots) && watch.markersSeen > since);
    });
    log(`CHAPTER ${marker.beat} is over`);
    // The TV puts the new "!" up in the same view as it wakes or moves the party, and can log it first: a moment for the snap.
    await sleep(2000);
    if (recoveryRun && !camped && fightsDone > 0 && onWalk(bots)) {
      await walkToBeat(bots, watch, region, { beat: 'thing:af_ford_campfire' });
      await until('camp jobs and dawn to finish', () => onWalk(bots));
      camped = true;
      log('CAMP OK: voted, chose own jobs, resolved saved rolls, reached dawn');
      await sleep(1000);
    }
    if (recoveryRun && camped && !inn && onWalk(bots) && bots.some(b => b.view.data.restReady)) {
      await walkToBeat(bots, watch, region, { beat: 'shop:af_inn' });
      await until('the inn on every phone', () => bots.every(b => b.view?.screen === 'shop'));
      const leader = bots.find(b => b.view.data.leader);
      await leader.input('restPropose', { shop: 'af_inn' });
      await until('agreed rooms on every phone', () => bots.every(b => b.view?.screen === 'shop' && b.view.data.roomsOpen));
      const payer = bots.find(b => b.view.data.gold >= 10);
      const guest = payer.view.data.guests.find(g => !g.room);
      const service = payer.view.data.services.find(s => s.kind === 'room');
      const before = payer.view.data.gold;
      await payer.input('shopService', { shop: 'af_inn', service: service.id, member: guest.member, trade: require('crypto').randomUUID() });
      await until('the paid room', () => payer.view.data.gold === before - service.price && payer.view.data.guests.some(g => g.member === guest.member && g.room));
      await until('dawn at the inn', () => onWalk(bots));
      inn = true;
      log('INN OK: agreed night, paid for a room from an own purse, reached dawn');
      await sleep(1000);
    }
  }
  if (recoveryRun && (!camped || !inn)) throw new Error('The chapter did not complete both camp and inn recovery');
  log(`CHAPTER the card: ${fs.existsSync(String(tvLog)) ? (fs.readFileSync(tvLog, 'utf8').match(/^STORY card (.*)$/m)?.[1] ?? '') : ''}`);
  await until('walking after the card', () => onWalk(bots) && bots.some((b) => b.view.data.leader));
  await sleep(holdMs);
  await walkOn(bots, watch, 'CHAPTER');
  const vip = bots[0];
  await vip.input('endSession');
  await until('the session to end', () => vip.ended);
  log(`Session over: ${vip.ended.message ?? ''}`);
}

/** Real phone trades funded by the shipped council reward; no injected gold or sold starting supplies. */
async function playShop(bots, watch, state) {
  const timer = setInterval(() => { for (const bot of bots) void bot.act(state); }, TICK_MS);
  timer.unref();
  idlePings(bots);
  await until('the party to walk', () => onWalk(bots));
  if (tvLog && !asHost) await until('the TV world', () => fs.existsSync(tvLog) && fs.readFileSync(tvLog, 'utf8').includes('TACTICS world open:'));
  await walkToBeat(bots, watch, 'ashen_ford', { beat: 'ch1_mayors_request' });
  await until('the council advance on every phone', () => onWalk(bots) && bots.reduce((sum, b) => sum + (b.view.data.gold ?? 0), 0) === 200);
  const share = Math.floor(200 / bots.length);
  if (!bots.every(b => b.view.data.gold === share || b.view.data.gold === share + 1)) throw new Error('The council advance was not split evenly');
  log(`SHOP council advance: ${bots.map(b => `${b.name} ${b.view.data.gold}`).join(', ')} gold`);
  await walkToBeat(bots, watch, 'ashen_ford', { beat: 'shop:af_market_herbalist' });
  await until('the shop on every phone', () => bots.every(b => b.view?.screen === 'shop'));
  clearInterval(timer);
  const buyer = bots.find(b => !b.view.data.leader && b.view.data.you.canBuy) ?? bots[0];
  log(`SHOP ${buyer.name} trades as ${buyer.view.data.leader ? 'leader' : 'follower'}`);
  const purse = () => buyer.view.data.gold;
  async function trade(bot, action, row, count) {
    const before = bot.view.viewId;
    const otherPurses = bots.filter(b => b !== bot).map(b => [b, b.view.data.gold, b.view.viewId, JSON.stringify(b.view.data.sell)]);
    const expected = bot.view.data.gold + (action === 'shopBuy' ? -1 : 1) * row.price * count;
    const id = require('crypto').randomUUID();
    await bot.input(action, { shop: bot.view.data.shop.id, item: row.item, member: row.member, count, trade: id });
    await until('trade confirmation', () => bot.view.viewId !== before && bot.view.data.gold === expected && otherPurses.every(([b, gold, viewId, bag]) => b.view?.viewId !== viewId && b.view?.data?.gold === gold && JSON.stringify(b.view.data.sell) === bag));
    log(`SHOP ${action} ${count} ${row.item}: ${purse()} gold`);
    return id;
  }
  const potion = buyer.view.data.items.find(x => x.item === 'potion_of_healing');
  const item = purse() >= potion.price ? potion.item : 'smoke_bomb';
  const row = buyer.view.data.items.find(x => x.item === item);
  const before = purse();
  const id = await trade(buyer, 'shopBuy', row, 1);
  if (purse() !== before - row.price) throw new Error("Purchase did not debit the buyer's own purse");
  await buyer.input('shopBuy', { shop: buyer.view.data.shop.id, item: row.item, count: 1, trade: id });
  await sleep(500);
  if (purse() !== before - row.price) throw new Error('Repeated trade charged twice');
  const sale = buyer.view.data.sell.find(x => x.item === row.item);
  await trade(buyer, 'shopSell', sale, 1);
  if (purse() !== before - row.price + sale.price) throw new Error('Sale did not credit the purse');
  await bots.find(b => b.view.data.leader).input('shopLeave', { shop: buyer.view.data.shop.id });
  await until('walking after trade', () => onWalk(bots));
  await bots[0].input('endSession');
  await until('the session to end', () => bots[0].ended);
  log('SHOP OK: council reward split evenly, own purse and bag, other purses unchanged, bought, replayed the trade id, sold, left');
}

(async () => {
  const host = asHost ? new HubConnectionBuilder().withUrl(`${server}/hub`).configureLogging(LogLevel.Critical).build() : null;
  const watch = new Watch(host);
  tv = watch;
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
  if (shopRun) {
    await playShop(bots, watch, state);
    await Promise.all(bots.map(b => b.conn.stop()));
    process.exit(0);
  }
  if (chapter) {
    await playChapter(bots, watch, state);
    await Promise.all(bots.map((b) => b.conn.stop()));
    process.exit(0);
  }
  if (stampFights) {
    await fightAtStamps(bots, watch, state);
    await Promise.all(bots.map((b) => b.conn.stop()));
    process.exit(0);
  }
  if (walking) {
    await until('the walk screen', () => {
      for (const bot of bots) void bot.act(state);
      return bots.some((b) => b.view?.screen === 'walk' && b.view.data.leader);
    });
    idlePings(bots);
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
