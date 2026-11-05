// Install only the twelve normalized portraits and their named/preset aliases.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict'), crypto = require('node:crypto');
const {PNG} = require('pngjs');
const root = path.resolve(__dirname, '../../../../..');
const portraits = path.join(root, 'content/tactics/base/portraits');
const manifestPath = path.join(portraits, 'manifest.json');
const manifest = JSON.parse(fs.readFileSync(manifestPath));
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const pending = [];
for (const [short, name, preset] of [['bree', 'bree_tallow', 'innkeeper'], ['halden', 'halden_varrow', 'mayor']]) {
  const audit = JSON.parse(fs.readFileSync(path.join(__dirname, `${short}-validation.json`)));
  assert.equal(hash(fs.readFileSync(path.join(__dirname, `${short}-source.png`))), audit.sourceSha256);
  const moods = {};
  for (const mood of ['idle', 'hurt', 'happy', 'angry', 'sad', 'surprised']) {
    const bytes = fs.readFileSync(path.join(__dirname, 'portraits', `${short}_${mood}.png`));
    assert.equal(hash(bytes), audit.moods[mood].sha256);
    const img = PNG.sync.read(bytes);
    assert.equal(img.width, 40); assert.equal(img.height, 40);
    let visible = 0;
    for (let y = 0; y < 40; y++) for (let x = 0; x < 40; x++) {
      const alpha = img.data[(y * 40 + x) * 4 + 3];
      assert(alpha === 0 || alpha === 255, 'Portrait alpha must be binary');
      if (x === 0 || y === 0 || x === 39 || y === 39) assert.equal(alpha, 0, 'Clipped portrait');
      if (alpha) visible++;
    }
    assert(visible > 300, 'Empty or incomplete portrait');
    const file = `${name}_${mood}.png`;
    moods[mood] = file; pending.push([file, bytes]);
  }
  manifest.presets[preset] = moods;
  manifest.presets[name] = moods;
}
// Validate both complete sets before writing anything. Preserve old files.
for (const [file, bytes] of pending) fs.writeFileSync(path.join(portraits, file), bytes);
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
console.log(JSON.stringify({installed: pending.length, size: 40, legacyFilesRetained: true}));
