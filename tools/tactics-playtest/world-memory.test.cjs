const test = require('node:test');
const assert = require('node:assert/strict');
const { verify } = require('./world-memory.cjs');
const cap = 512 * 1024 * 1024;
function complete() {
  const stages = ['open', 'crossing', 'party-change', 'party-restored'].map(stage =>
    `WORLD texture-budget region=ashen_ford stage=${stage} party=4 textures-bytes=${cap} peak-bytes=${cap} limit-bytes=${cap} result=PASS`);
  const sheets = ['human', 'elf'].flatMap(race => ['fighter', 'cleric', 'rogue', 'wizard'].flatMap(cls =>
    [`WORLD unit-texture tactical/${race}_${cls}.png 3072x3200`, `WORLD unit-release tactical/${race}_${cls}.png`]));
  return [...stages, ...sheets].join('\r\n');
}
test('a complete four-member run passes at the exact binary memory limit with CRLF logs', () => {
  assert.equal(verify(complete()).stages.length, 4);
});
test('the claimed PASS cannot conceal excessive or missing memory measurements', () => {
  assert.throws(() => verify(complete().replace(`peak-bytes=${cap}`, `peak-bytes=${cap + 1}`)));
  assert.throws(() => verify(complete().replace(`textures-bytes=${cap}`, 'textures-bytes=0')));
});
test('a single stand-in or an incomplete timed-out run cannot pass', () => {
  assert.throws(() => verify(complete().replace('party=4', 'party=1')));
  assert.throws(() => verify(complete().split('\r\n').filter(l => !l.includes('stage=crossing')).join('\n')));
});
test('roster tests require actual uploads and releases, without Godot errors', () => {
  assert.throws(() => verify(complete().replace('WORLD unit-release tactical/elf_wizard.png', '')));
  assert.throws(() => verify(complete().replace('WORLD unit-texture tactical/human_rogue.png 3072x3200', 'WORLD unit-texture tactical/human_rogue.png missing')));
  assert.throws(() => verify(complete() + '\nERROR: texture failed'));
});
