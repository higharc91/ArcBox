// Bot phones for Derelict: ISV Meridian. Joins a room as N players over the real hub and plays from crew pick through the
// Breach, the briefings and the endgame to the Debrief, deciding only from what each phone's screen shows. Use it to watch the dev board (/dev.html) or a TV with no
// phones, or to play alongside bots.
//
//   node tools/meridian-playtest/bots.cjs --room=<CODE> [--server=http://localhost:5080] [--players=3] [--start]
//   node tools/meridian-playtest/bots.cjs --host [--players=3]
//
//   --room=<CODE>   join this room (the dev page's host panel or the TV shows the code)
//   --host          create the room and play the TV's part too: signal `presented` for each event the game shows
//   --start         the first bot selects Meridian and starts the game (needs it to be the VIP: the first to join)
//   --players=<n>   how many bot phones, default 3
//   --pace=<ms>     pause between a bot's actions, default 400, so a person watching can follow
//
// To play one scenario, force it on a Development server (the setting is ignored elsewhere); every Breach then picks it:
//
//   dotnet run --project src/ArcBox.Server -- --Meridian:Scenario=the_passenger
//   (or Meridian__Scenario=the_passenger in the environment; ids are in content/meridian/base/rules/breach.json)
//
// It exits 0 when the game ends (printing the results message), 1 on an error or after --timeout seconds (default 900).
'use strict';
const path = require('path');
const { HubConnectionBuilder, LogLevel } = require(path.join(__dirname, '../../web/play/node_modules/@microsoft/signalr'));

const arg = (name, fallback) => {
  const hit = process.argv.slice(2).find((a) => a === `--${name}` || a.startsWith(`--${name}=`));
  if (!hit) return fallback;
  return hit.includes('=') ? hit.slice(hit.indexOf('=') + 1) : true;
};
const server = String(arg('server', 'http://localhost:5080')).replace(/\/$/, '');
const players = Number(arg('players', 3));
const pace = Number(arg('pace', 400));
const timeout = Number(arg('timeout', 900));
const asHost = arg('host', false) === true;
const start = asHost || arg('start', false) === true;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const connect = () => new HubConnectionBuilder().withUrl(`${server}/hub`).withAutomaticReconnect().configureLogging(LogLevel.Critical).build();

/** What a phone does with the screen in front of it (the same policy as MeridianHubTests). */
function decide(view, picked) {
  if (!view || view.kind !== 'custom') return null;
  const d = view.data;
  const pick = (xs) => xs[Math.floor(Math.random() * xs.length)];
  switch (view.screen) {
    case 'crewPick': {
      if (d.crew.some((c) => c.mine)) return null;
      const free = d.crew.find((c) => !c.taken && !picked.has(c.id));
      if (!free) return null;
      picked.add(free.id);
      return ['pick', { crew: free.id }];
    }
    case 'turn':
      // The endgame first: a scenario action, an attack, or a monster to command, each about half the time.
      if (d.actions && d.actions.length && Math.random() < 0.5) {
        const a = pick(d.actions);
        return ['act', a.data ? { action: a.id, data: a.data } : { action: a.id }];
      }
      if (d.attacks && d.attacks.length && Math.random() < 0.5) return ['attack', { target: pick(d.attacks).id }];
      if (d.monsters && d.monsters.length && Math.random() < 0.5) {
        const m = pick(d.monsters);
        if (m.targets.length) return ['monsterAttack', { monster: m.id, target: pick(m.targets).id }];
        if (m.moves.length) return ['monsterMove', { monster: m.id, to: pick(m.moves) }];
      }
      if (d.opens.length) { const o = pick(d.opens); return ['open', { at: o.at, side: o.side }]; }
      if (d.reach.length && Math.random() < 0.75) return ['moveTo', { to: pick(d.reach).id }];
      return ['endTurn'];
    case 'orient': return ['orient', { rotation: d.options[0].rotation }];
    case 'roll': return ['roll'];
    case 'card': return ['choose', { option: d.options[0].id }];
    case 'trade': return ['answerTrade', { accept: false }];
    case 'briefing': return d.ready ? null : ['ready'];
    case 'allocate': return ['allocate', { first: Math.floor(Math.random() * (d.steps + 1)) }];
    case 'vote': return d.cast ? null : ['vote', { choice: pick(d.options).id }];
    case 'echo': return !d.lent && d.targets.length ? ['lend', { to: pick(d.targets).id }] : ['endTurn'];
    case 'debrief': return d.vip ? ['continue'] : null;
    default: return null;   // sheet, watch, over: nothing to send
  }
}

async function main() {
  let room = arg('room', null);
  let host = null;
  let presented = 0;
  if (asHost) {
    host = connect();
    host.on('HostView', (gameId, view) => {
      const seq = view && view.now && view.now.seq;
      if (gameId === 'meridian' && seq > presented) {
        presented = seq;
        setTimeout(() => host.invoke('HostSignal', 'presented', { seq }).catch(() => {}), Math.max(300, view.now.seconds * 1000 - 200));
      }
    });
    await host.start();
    room = (await host.invoke('CreateRoom')).code;
  }
  if (!room) throw new Error('Pass --room=<CODE> or --host.');
  console.log(`ROOM ${room}`);

  const picked = new Set();
  const bots = [];
  for (let i = 0; i < players; i++) {
    const bot = { name: `Bot${i + 1}`, conn: connect(), view: null, acted: null, ended: null };
    bot.conn.on('ControllerView', (v) => { bot.view = v; });
    bot.conn.on('GameEnded', (r) => { bot.ended = r; });
    await bot.conn.start();
    await bot.conn.invoke('Join', room, bot.name);
    bots.push(bot);
  }
  if (start) {
    await bots[0].conn.invoke('SelectGame', 'meridian');
    await bots[0].conn.invoke('StartGame');
  }

  const deadline = Date.now() + timeout * 1000;
  while (Date.now() < deadline) {
    const ended = bots.find((b) => b.ended);
    if (ended) {
      console.log(`ENDED ${ended.ended.message ?? ''}`);
      break;
    }
    for (const bot of bots) {
      if (!bot.view || bot.view.viewId === bot.acted) continue;
      const act = decide(bot.view, picked);
      if (!act) continue;
      bot.acted = bot.view.viewId;
      const [action, data] = act;
      console.log(`${bot.name} ${bot.view.screen} -> ${action}${data ? ' ' + JSON.stringify(data) : ''}`);
      await bot.conn.invoke('SendInput', bot.view.viewId, data === undefined ? { action } : { action, data }).catch((e) => console.log(`  (${e.message})`));
      await sleep(pace);
    }
    await sleep(50);
  }
  const ok = bots.some((b) => b.ended);
  for (const bot of bots) await bot.conn.stop();
  if (host) await host.stop();
  if (!ok) throw new Error(`No ending within ${timeout} s.`);
}

main().then(() => process.exit(0), (e) => { console.error(e.message); process.exit(1); });
