const { test } = require('node:test');
const assert = require('node:assert/strict');
const { safestMoves, escapeBurning } = require('./hazards.cjs');

test('old phone moves keep their order when no risk fields are present', () => {
  const moves = [{ to: [1, 0], cost: 1 }, { to: [2, 0], cost: 2 }];
  assert.deepEqual(safestMoves(moves), moves);
  assert.deepEqual(safestMoves([]), []);
});

test('advancing prefers a safe route, or the least damaging necessary crossing', () => {
  const fire = { to: [1, 0], risk: 7 };
  const saferFire = { to: [0, 1], risk: 3.5 };
  const safe = { to: [1, 1] };
  assert.deepEqual(safestMoves([fire, safe, saferFire]), [safe]);
  assert.deepEqual(safestMoves([fire, saferFire]), [saferFire]);
});

test('a hero can leave fire after spending its action, using remaining movement', () => {
  const exit = { to: [1, 0], cost: 1, path: [[0, 0], [1, 0]] };
  const data = { surfaces: ['burning'], choices: { economy: { action: 0, moveLeft: 3 }, moves: [
    { to: [0, 1], cost: 1, risk: 3.5 },
    { to: [2, 0], cost: 2 }, exit,
  ] } };
  assert.equal(escapeBurning(data), exit);
});

test('safe water is a valid fire exit even though its warning mentions conduction', () => {
  const water = { cost: 1, warn: 'water · conducts lightning' };
  assert.equal(escapeBurning({ surfaces: ['burning'], choices: { moves: [water] } }), water);
});

test('ordinary ground, decorative oil and no reachable safe exit do not cause shuffling', () => {
  for (const surfaces of [undefined, [], ['scorched'], ['oil']])
    assert.equal(escapeBurning({ surfaces, choices: { moves: [{ cost: 1 }] } }), null);
  assert.equal(escapeBurning({ surfaces: ['burning'], choices: { moves: [] } }), null);
  assert.equal(escapeBurning({ surfaces: ['burning'], choices: { moves: [{ cost: 1, risk: 3.5 }] } }), null);
});
