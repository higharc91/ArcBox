// Install only the owner-approved existing Bree atlas; never copy the review hero or linked rules.
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../../..');
const review = path.resolve(process.argv[2] || path.join(require('node:os').homedir(), '.codex/tmp/bree-tv-review'));
const approval = JSON.parse(fs.readFileSync(path.join(__dirname, 'sprite-approval.json'), 'utf8'));
assert.equal(approval.candidate, 1);
assert.equal(approval.fullSetApproval?.status, 'approved', 'Full directional/TV owner approval is required');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
for (const [file, expected] of Object.entries(approval.fullSetApproval.sha256)) {
  const location = file.startsWith('docs/') ? path.join(root, file) : path.join(__dirname, file);
  assert.equal(hash(fs.readFileSync(location)), expected, `Approved source changed: ${file}`);
}
const output = JSON.parse(fs.readFileSync(path.join(__dirname, 'directional/review-output.json'), 'utf8'));
const atlas = fs.readFileSync(path.join(review, 'creatures/review_bree.png'));
assert.equal(hash(atlas), output.atlasSha256, 'Review atlas differs from the approved output');
const source = JSON.parse(fs.readFileSync(path.join(review, 'creatures/manifest.json'), 'utf8')).sprites.review_bree;
assert.deepEqual(source.frame, { width: 96, height: 96, anchor: [48, 84] });
assert.equal(source.pixelScale, .5);
assert.deepEqual(Object.keys(source.anims), ['idle', 'walk', 'talk', 'down']);
const manifestFile = path.join(root, 'content/tactics/base/creatures/manifest.json');
const manifest = JSON.parse(fs.readFileSync(manifestFile, 'utf8'));
manifest.sprites.bree_tallow = { ...source, file: 'bree_tallow.png', variant: 'bree_tallow',
  keys: ['characters.innkeeper', 'characters.bree_tallow'],
  provenance: 'PixelLab candidate 1; complete directional/motion set and TV appearance approved by owner in chat. Source checkpoint 0eacebec; exact atlas SHA256 ' + hash(atlas) };
fs.writeFileSync(path.join(root, 'content/tactics/base/creatures/bree_tallow.png'), atlas);
fs.writeFileSync(manifestFile, JSON.stringify(manifest, null, 2) + '\n');
console.log(JSON.stringify({ installed: 'bree_tallow', atlasSha256: hash(atlas), preservedLegacySheet: true, creditsSpent: 0 }));
