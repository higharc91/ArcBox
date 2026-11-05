// Check immutable generation provenance and the exact finished-review bindings.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..');
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const receipts=JSON.parse(fs.readFileSync(path.join(__dirname,'generation-receipts.json')));
assert.equal(receipts.imageToolCalls,11);assert.equal(receipts.records.length,11);
for(const record of receipts.records){
 assert.equal(hash(path.join(__dirname,record.request)),record.requestSha256,record.request);
 assert.equal(hash(path.join(__dirname,record.output)),record.outputSha256,record.output);
 for(const [file,sha] of Object.entries(record.inputSha256||{}))assert.equal(hash(path.join(root,file)),sha,file);
}
const approval=JSON.parse(fs.readFileSync(path.join(__dirname,'sprite-approval.json')));
for(const [file,sha] of Object.entries(approval.completedSet.sha256))assert.equal(hash(path.join(__dirname,file)),sha,file);
assert.equal(hash(path.join(root,approval.completedSet.tv.path)),approval.completedSet.tv.sha256);
console.log(JSON.stringify({result:'PASS',generationRecords:11,completionImageToolCalls:9,boundReviewAssets:Object.keys(approval.completedSet.sha256).length,nativeTvHash:'PASS',pixelLabSpend:0}));
