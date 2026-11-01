// What tools/tv-e2e.sh's ARCBOX_CHAPTER=1 needs beyond the bots: a check of the TV's log against the chapter's scenes, and shots of
// the TV's display at each moment of the chapter worth seeing.
//
//   node tools/tactics-playtest/chapter.cjs check <tv.log> <force|parley> [--relaunched-at=<line>] [--restarted-at=<line>]
//   node tools/tactics-playtest/chapter.cjs shots <tv.log> <X display> <dir>
//
// check reads the TV's --story-log lines (STORY <seq> <line> said|shown <state>, STORY card <chapter>) and the scenes in
// content/tactics/base/lore/scenes/ch1.json, and fails (exit 1, a line for each thing wrong) unless:
//   - the scenes come in the branch's order, from the mayor to the Stranger's four more, and every one of them is there;
//   - each scene's lines walk its graph: every line of a node in order, then a node its `next`, an option's `to` or a check's pass or
//     fail leads to, from the scene's start to a node that ends it;
//   - every line is said exactly once on the TV: never twice, and never not at all, but for the line a relaunched TV came back to,
//     which it only shows (--relaunched-at: the TV log line where the new TV started), and for the line a restart came back to
//     (--restarted-at: the TV log line where the server stopped), which a TV the phones beat back to the server only shows;
//   - the battle beat's fight is on the TV between its lead-in and its aftermath;
//   - the chapter's card comes up after the last line, and the party takes a step after it.
// It prints the scenes and the count of lines as it goes.
//
// shots watches the TV's log and saves the X display (ImageMagick's `import`) a moment after each scene's first line, each question,
// each roll, each fight, its results, the card and the first step after it, as <dir>/<nn>-<scene or moment>.png; it runs until killed.
const fs = require('fs');
const path = require('path');
const { execFile } = require('child_process');

const SCENES = JSON.parse(fs.readFileSync(path.join(__dirname, '../../content/tactics/base/lore/scenes/ch1.json'), 'utf8')).scenes;
const ORDER = {
  force: ['ch1_mayor', 'ch1_ford', 'ch1_dawn', 'ch1_bridge_cost', 'ch1_maud', 'ch1_docks', 'ch1_kindler', 'ch1_four_more'],
  parley: ['ch1_mayor', 'ch1_ford', 'ch1_parley', 'ch1_young_ones', 'ch1_road_east', 'ch1_maud', 'ch1_granary', 'ch1_song', 'ch1_docks',
    'ch1_kindler', 'ch1_four_more'],
};
/** The fight of each branch, between its lead-in and its aftermath. */
const FIGHT = { force: ['ch1_dawn', 'ch1_bridge_cost'], parley: ['ch1_young_ones', 'ch1_road_east'] };

const sceneOf = new Map(SCENES.flatMap((sc) => sc.nodes.flatMap((n) => (n.lines ?? []).map((l) => [l.id, sc.id]))));

/** The TV log's lines, numbered from 1. */
const read = (file) => fs.readFileSync(file, 'utf8').split('\n');
const STORY = /^STORY (\d+) (\S+) (said|shown) (\S+)/;

function check(file, branch, args) {
  const opt = (name) => Number(args.find((a) => a.startsWith(`--${name}=`))?.split('=')[1] ?? 0);
  const relaunched = opt('relaunched-at');
  const restarted = opt('restarted-at');
  const fail = [];
  const lines = read(file);
  // Each line's showings: its log line numbers, and whether it was said.
  const shown = [];
  for (const [i, text] of lines.entries()) {
    const m = STORY.exec(text);
    if (m && m[2] !== '-') shown.push({ at: i + 1, line: m[2], said: m[3] === 'said', state: m[4] });
  }
  const order = shown.filter((s, i) => i === 0 || shown[i - 1].line !== s.line).map((s) => s.line);
  const unknown = order.filter((l) => !sceneOf.has(l));
  if (unknown.length) fail.push(`lines that aren't Chapter 1's: ${unknown.join(', ')}`);

  // The scenes, in order: each scene's lines together, and the branch's scenes.
  const runs = [];
  for (const line of order.filter((l) => sceneOf.has(l))) {
    const scene = sceneOf.get(line);
    if (runs.at(-1)?.scene === scene) runs.at(-1).lines.push(line);
    else runs.push({ scene, lines: [line] });
  }
  const scenes = runs.map((r) => r.scene);
  console.log(`scenes: ${runs.map((r) => `${r.scene} (${r.lines.length})`).join(', ')}`);
  if (JSON.stringify(scenes) !== JSON.stringify(ORDER[branch])) fail.push(`the scenes ${ORDER[branch].join(', ')} in order (they were ${scenes.join(', ')})`);

  // Each scene's lines walk its graph.
  for (const run of runs) {
    const scene = SCENES.find((sc) => sc.id === run.scene);
    const nodes = new Map(scene.nodes.map((n) => [n.id, n]));
    let node = nodes.get(scene.start);
    let k = 0;
    const wrong = (why) => fail.push(`${run.scene}: ${why}`);
    for (;;) {
      const want = (node.lines ?? []).map((l) => l.id);
      const got = run.lines.slice(k, k + want.length);
      if (JSON.stringify(got) !== JSON.stringify(want)) {
        wrong(`node ${node.id}'s lines ${want.join(' ')} (got ${got.join(' ') || 'none'})`);
        break;
      }
      k += want.length;
      const ends = node.end || (!node.next && !(node.options ?? []).length);
      if (k === run.lines.length) {
        if (!ends) wrong(`it stopped in node ${node.id}, which doesn't end the scene`);
        break;
      }
      const leads = [node.next, ...(node.options ?? []).flatMap((o) => (o.check ? [o.check.pass, o.check.fail] : [o.to]))].filter(Boolean);
      const after = [...nodes.values()].find((n) => leads.includes(n.id) && (n.lines ?? [])[0]?.id === run.lines[k]);
      if (!after) {
        wrong(`after node ${node.id}, ${run.lines[k]} isn't the first line of ${leads.join(' or ')}`);
        break;
      }
      node = after;
    }
  }

  // Every line said once: never twice, and not at all only for the line a relaunched TV came back to.
  const backTo = relaunched ? shown.find((s) => s.at > relaunched)?.line : null;
  // A restart: the line in flight as the server stopped comes back first. When the phones beat the TV back, the game carries on before
  // the TV sees Resuming, and the TV, back on a line already under way, only shows it. That one line may go unsaid.
  const inFlight = restarted ? shown.find((s) => s.at > restarted)?.line : null;
  let said = 0;
  for (const line of new Set(order)) {
    const times = shown.filter((s) => s.line === line && s.said).length;
    said += times;
    if (times > 1) fail.push(`${line} said once (it was said ${times} times)`);
    if (times === 0 && line !== backTo && line !== inFlight) fail.push(`${line} said (it was only shown)`);
  }
  console.log(`lines: ${new Set(order).size}, said ${said}${backTo ? `; the relaunched TV came back to ${backTo}` : ''}${inFlight ? `; the restart came back to ${inFlight}` : ''}`);
  if (relaunched && backTo) {
    const first = shown.find((s) => s.at > relaunched);
    if (first.said) fail.push(`the relaunched TV's first line, ${first.line}, only shown (it said it)`);
  }

  // The fight between its lead-in and its aftermath.
  const lastOf = (scene) => shown.filter((s) => sceneOf.get(s.line) === scene).at(-1)?.at ?? 0;
  const firstOf = (scene) => shown.find((s) => sceneOf.get(s.line) === scene)?.at ?? 0;
  const [lead, after] = FIGHT[branch];
  const fight = lines.findIndex((l, i) => i + 1 > lastOf(lead) && /^TACTICS screen tactics-battle-/.test(l)) + 1;
  if (!fight || !(fight < firstOf(after))) fail.push(`a fight on the TV between ${lead} and ${after}`);
  else console.log(`the fight: ${lines[fight - 1]} (line ${fight})`);

  // The card after the last line, and a step after it.
  const card = lines.findIndex((l) => /^STORY card /.test(l)) + 1;
  const last = shown.at(-1)?.at ?? 0;
  if (!card || card < last) fail.push(`STORY card after the last line (line ${last})`);
  else {
    console.log(`the card: ${lines[card - 1].slice(11)} (line ${card})`);
    if (!lines.some((l, i) => i + 1 > card && /^WALK \d+ /.test(l))) fail.push('a step on the TV after the card');
  }

  for (const f of fail) console.log(`MISSING ${f}`);
  return fail.length ? 1 : 0;
}

function shots(file, display, dir) {
  fs.mkdirSync(dir, { recursive: true });
  let pos = 0;
  let rest = '';
  let n = 0;
  let fights = 0;
  let scene = 'story';
  const seen = new Set();
  const grab = (name, ms) => setTimeout(() => {
    const out = path.join(dir, `${String(++n).padStart(2, '0')}-${name}.png`);
    execFile('import', ['-display', display, '-window', 'root', out], (e) => console.log(e ? `SHOT ${name} failed: ${e.message}` : `SHOT ${out}`));
  }, ms);
  const once = (key, name, ms) => {
    if (seen.has(key)) return;
    seen.add(key);
    grab(name, ms);
  };
  setInterval(() => {
    if (!fs.existsSync(file)) return;
    const size = fs.statSync(file).size;
    if (size < pos) [pos, rest] = [0, ''];
    if (size === pos) return;
    const fd = fs.openSync(file, 'r');
    const chunk = Buffer.alloc(size - pos);
    fs.readSync(fd, chunk, 0, chunk.length, pos);
    fs.closeSync(fd);
    pos = size;
    const got = (rest + chunk.toString('utf8')).split('\n');
    rest = got.pop();
    for (const line of got) {
      const m = STORY.exec(line);
      if (m && sceneOf.has(m[2])) {
        scene = sceneOf.get(m[2]);
        once(`scene ${scene}`, scene, 1800);
        if (m[4] === 'asking') once(`ask ${scene} ${m[2]}`, `${scene}-question`, 2500);
      }
      // the roll's verdict, as its die lands
      if (line === 'AUDIO Sfx ui_confirm' || line === 'AUDIO Sfx ui_error') once(`roll ${scene} ${n}`, `${scene}-roll`, 600);
      if (/^TACTICS screen tactics-battle-/.test(line)) once(`fight ${fights}`, `fight-${fights + 1}`, 9000);
      if (line === 'TACTICS screen tactics-results' && !seen.has(`results ${fights}`)) {
        once(`results ${fights}`, `results-${fights + 1}`, 2500);
        fights++;
      }
      if (/^STORY card /.test(line)) once('card', 'chapter-card', 2000);
      if (seen.has('card') && /^WALK \d+ /.test(line)) once('after', 'walking-after', 1500);
    }
  }, 200);
}

const [mode, ...args] = process.argv.slice(2);
if (mode === 'check' && args.length >= 2 && ORDER[args[1]]) process.exit(check(args[0], args[1], args.slice(2)));
else if (mode === 'shots' && args.length === 3) shots(...args);
else {
  console.error('usage: chapter.cjs check <tv.log> <force|parley> [--relaunched-at=<line>] | chapter.cjs shots <tv.log> <display> <dir>');
  process.exit(2);
}
