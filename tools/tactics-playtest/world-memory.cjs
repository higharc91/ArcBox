// Validate a complete --world-demo=<region> --perf run, including roster replacement/release.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const limit = 512 * 1024 * 1024;
function verify(log) {
  assert(!/^ERROR:/m.test(log), 'Godot errors in the memory run');
  const stages = [];
  let region;
  for (const stage of ['open', 'crossing', 'party-change', 'party-restored']) {
    const matches = [...log.matchAll(/^WORLD texture-budget region=(\S+) stage=(\S+) party=(\d+) textures-bytes=(\d+) peak-bytes=(\d+) limit-bytes=(\d+) result=(\S+)\r?$/gm)]
      .filter(m => m[2] === stage);
    assert.equal(matches.length, 1, `Expected one completed ${stage} gate`);
    const [, name, , party, current, peak, cap, result] = matches[0];
    region ??= name;
    assert.equal(name, region, 'Mixed region logs');
    assert.equal(+party, 4, 'The gate needs four actual members');
    assert.equal(+cap, limit, 'The agreed limit changed');
    assert(+current > 0 && +peak >= +current && +peak <= limit, `Invalid or excessive memory at ${stage}`);
    assert.equal(result, 'PASS', `Failed ${stage} gate`);
    stages.push({ stage, bytes: +current, peakBytes: +peak });
  }
  for (const race of ['human', 'elf']) for (const cls of ['fighter', 'cleric', 'rogue', 'wizard']) {
    const path = `tactical/${race}_${cls}.png`;
    assert(log.includes(`WORLD unit-texture ${path} `) && !log.includes(`WORLD unit-texture ${path} missing`), `Hero not uploaded: ${path}`);
    assert(log.includes(`WORLD unit-release ${path}`), `Unused hero not released: ${path}`);
  }
  return { region, limitBytes: limit, stages, rosterUploadsAndReleases: 'verified' };
}
module.exports = { verify };
if (require.main === module) {
  try { console.log(JSON.stringify(verify(fs.readFileSync(process.argv[2], 'utf8')), null, 2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
