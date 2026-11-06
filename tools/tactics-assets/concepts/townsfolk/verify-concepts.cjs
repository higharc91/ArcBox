const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const read = name => JSON.parse(fs.readFileSync(path.join(__dirname, name), 'utf8'));
const queue = read('briefs.json');
const approvals = read('approvals.json');
const all = [...queue.named, ...queue.townsfolk];
const batch = read('batch-requests.json');
assert.equal(new Set(all.map(x=>x.id)).size, all.length);
const assets = new Set();
const inventory = [];
let batchCU = 0;
for (const item of all) {
  const anchor = item.id === queue.anchor;
  const imagePath = anchor ? 'bree-concept.png' : `generated/${item.id}.png`;
  const receipt = read(anchor ? 'bree-result.json' : `generated/${item.id}-result.json`);
  assert.equal(receipt.status, 'success', item.id);
  const assetId = anchor ? receipt.asset_id : receipt.assetIds[0];
  if (approvals[item.id]?.status === 'approved') {
    assert.equal(approvals[item.id].asset_id, assetId, `Approval refers to a different asset: ${item.id}`);
  }
  assert(!assets.has(assetId), `One output reused for different people: ${item.id}`);
  assets.add(assetId);
  const bytes = fs.readFileSync(path.join(__dirname, imagePath));
  assert.equal(bytes.subarray(0,8).toString('hex'), '89504e470d0a1a0a', imagePath);
  const width = bytes.readUInt32BE(16), height = bytes.readUInt32BE(20);
  const study1K = receipt.resolution === '1K';
  if (study1K) assert(['townsfolk_farmhand','townsfolk_guard'].includes(item.id), `Unexpected lower-resolution study: ${item.id}`);
  assert(Math.max(width,height)>=(study1K?1024:2048), `Concept below requested resolution: ${item.id}`);
  const cost = anchor ? receipt.actualCU : receipt.cuCost;
  assert(Number.isFinite(cost) && cost>=0, item.id);
  if (!anchor) batchCU += cost;
  if (!anchor && !receipt.reused) {
    const request = batch.requests.find(r=>r.id===item.id);
    assert.deepEqual(receipt.reference_asset_ids, request.parameters.referenceImages, item.id);
  }
  inventory.push({ id:item.id,asset_id:assetId,output:imagePath,width,height,
    sha256:crypto.createHash('sha256').update(bytes).digest('hex'),
    reused:receipt.reused??false,issue80CU:cost,resolution:study1K?'1K study':'2K',
    design_approval:approvals[item.id]?.status??'pending' });
}
const supplement = read('supplements/child-b-result.json');
assert.equal(supplement.status,'success');
if (approvals.townsfolk_child_b_body?.status === 'approved') {
  assert.equal(approvals.townsfolk_child_b_body.asset_id, supplement.assetIds[0], 'Child-B approval asset mismatch');
}
const supplementBytes = fs.readFileSync(path.join(__dirname,'supplements/child-b.png'));
assert.equal(supplementBytes.subarray(0,8).toString('hex'),'89504e470d0a1a0a');
assert(Math.max(supplementBytes.readUInt32BE(16),supplementBytes.readUInt32BE(20))>=1024);
batchCU += supplement.cuCost;
assert(batchCU<=approvals.concept_batch_2026_11_03.maximumCU);
const result = { concepts:inventory.length, named:queue.named.length, paired_townsfolk:queue.townsfolk.length,
  batchCU,totalWithAnchorCU:batchCU+read('bree-result.json').actualCU,
  unspentAuthorizedCU:approvals.concept_batch_2026_11_03.maximumCU-batchCU,
  supplemental_views:[{id:'townsfolk_child_b_body',asset_id:supplement.assetIds[0],output:'supplements/child-b.png',
    sha256:crypto.createHash('sha256').update(supplementBytes).digest('hex'),issue80CU:supplement.cuCost,
    design_approval:approvals.townsfolk_child_b_body?.status??'pending'}],inventory };
fs.writeFileSync(path.join(__dirname,'verified-inventory.json'),JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify({concepts:result.concepts,batchCU,totalWithAnchorCU:result.totalWithAnchorCU,unspentAuthorizedCU:result.unspentAuthorizedCU}));
