const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const shell = fs.readFileSync(path.join(__dirname, '../tv-e2e.sh'), 'utf8');
const program = shell.match(/MOVED=\$\(awk -v to="\$RESTART_AT" '([\s\S]*?)' "\$OUT\/tv.log"\)/)[1];
function displacement(log) {
  const result = spawnSync('awk', ['-v', 'to=1000', program], { input: log, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  return Number(result.stdout.trim());
}
test('a delayed first motion sample does not move the initial origin', () => {
  assert.equal(displacement('TACTICS walk snapped to 26.5 58.5\nWALKAT 1 1 1 26.77 60.77\nWALKAT 2 19 0 27.5 63.5\n'), 5);
});
test('a snap without any rendered motion does not prove movement', () => {
  assert.equal(displacement('TACTICS walk snapped to 26.5 58.5\n'), 0);
});
test('legacy logs without a snap retain their first motion sample as origin', () => {
  assert.equal(displacement('WALKAT 1 1 1 1 2\nWALKAT 2 2 0 4 6\n'), 4);
});
