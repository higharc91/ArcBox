const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require.resolve('./bots.cjs'), 'utf8');
const Watch = vm.runInNewContext(source.slice(source.indexOf('class Watch {'), source.indexOf('const walkTo =')) + '\nWatch', {
  fs: {
    existsSync: () => true,
    statSync: () => ({ size: Buffer.byteLength(global.logText) }),
    openSync: () => 1,
    readSync: (_, buffer) => buffer.write(global.logText),
    closeSync: () => {},
  }, Buffer, tvLog: 'test.log', answerShown: false,
});
for (const newline of ['\n', '\r\n']) {
  test(`fresh party tile and exact screen markers work with ${JSON.stringify(newline)}`, () => {
    global.logText = ['TACTICS walk snapped to 26.5 58.5', 'TACTICS screen tactics-results', 'STORY marker ch1_mayors_request 33 59 !', ''].join(newline);
    const watch = new Watch();
    watch.read();
    assert.equal(String(watch.tile), '26,58');
    assert.ok(watch.resultsShown);
    assert.equal(watch.markers[0].beat, 'ch1_mayors_request');
  });
}

test('default route tokens use navigation without opening a shop and retain restart pauses', async () => {
  const calls = [];
  const start = source.indexOf('async function runScript(');
  const end = source.indexOf('\n}', start) + 2;
  const run = vm.runInNewContext(source.slice(start, end) + '\nrunScript', {
    walkScript: [['route', 'af_market_herbalist'], ['pause', 'restart']],
    walkToBeat: async (...args) => calls.push(['route', args[3].beat, args[4]]),
    leaderOf: async () => ({}),
    pause: async (_, name) => { calls.push(['pause', name]); return {}; },
    release: async () => {}, sleep: async () => {}, log: () => {},
    hold: async () => { throw new Error('route token was treated as a held direction'); },
  });
  await run([], {}, {});
  assert.deepEqual(calls, [['route', 'shop:af_market_herbalist', false], ['pause', 'restart']]);
});
