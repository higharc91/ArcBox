const { test } = require('node:test');
const assert = require('node:assert/strict');
const { policy, latestPhase, matches } = require('../../web/play/e2e/redline-restart.cjs');

test('the default keeps round-two interruption behavior', () => {
  assert.equal(matches(policy(), 'throttle', 1), false);
  assert.equal(matches(policy(), 'throttle', 2), true);
});
for (const [target, phone, tv, round] of [
  ['qualifying', 'quali-question', 'QualifyingQuestion', 0],
  ['question', 'question', 'Question', 2], ['reveal', 'reveal', 'Reveal', 2],
  ['anthem', 'anthem', 'Anthem', 15], ['champagne', 'champagne', 'Champagne', 15],
]) test(`${target} waits for both the phone and the actual TV`, () => {
  const p = policy(target);
  assert.equal(matches(p, phone, round, `REDLINE ${tv} round ${round}\n`), true);
  assert.equal(matches(p, phone, round, 'REDLINE DriverSelect round 0\n'), false);
  assert.equal(matches(p, 'seated', round, `REDLINE ${tv} round ${round}\n`), false);
});
test('latest TV phase handles CRLF and ignores old phase lines', () => {
  assert.equal(latestPhase('REDLINE Question round 2\r\nAUDIO Sfx correct\r\nREDLINE Reveal round 2\r\n'), 'Reveal');
});
test('unsupported phase names fail instead of silently testing another phase', () => {
  assert.throws(() => policy('finish'), /Unknown restart phase/);
});
