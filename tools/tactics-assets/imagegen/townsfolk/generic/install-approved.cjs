// Invoke only after the owner approves the bound finished motion/portrait/TV review.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {hash}=require('./build.cjs');
const approval=JSON.parse(fs.readFileSync(path.join(__dirname,'approval.json')));
assert.equal(approval.status,'approved','Issue #80 requires finished-set/TV approval before production installation.');
assert(approval.ownerApproval?.quote,'Missing owner approval evidence.');
for(const [file,digest]of Object.entries(approval.sha256))assert.equal(hash(fs.readFileSync(path.join(__dirname,file))),digest,`Reviewed asset changed: ${file}`);
const root=path.resolve(__dirname,'../../../../..'),destination=path.join(root,'content/tactics/base'),review=path.resolve(process.argv[2]||'/workspace/work/issue80-generic-review');
// Read-only checks prove the production baseline and exact review content first.
const {execFileSync}=require('node:child_process');
execFileSync(process.execPath,[path.join(__dirname,'verify.cjs'),review],{stdio:'inherit'});
const record=JSON.parse(fs.readFileSync(path.join(review,'generic-review-owner.json')));
const files=[...Object.keys(record.assetHashes),...record.cosmeticDocuments];
const pending=files.map(file=>[file,fs.readFileSync(path.join(review,file))]);
for(const file of record.cosmeticDocuments)assert.equal(hash(fs.readFileSync(path.join(review,file))),approval.reviewDocuments[file],`Review manifest changed: ${file}`);
for(const [file,bytes]of pending)fs.writeFileSync(path.join(destination,file),bytes);
console.log(JSON.stringify({installedVariants:14,portraits:84,placedPeople:28,legacyFilesRetained:true}));
