const fs = require('node:fs');
const path = require('node:path');
const read = name => JSON.parse(fs.readFileSync(path.join(__dirname, name), 'utf8'));
const batch = read('batch-requests.json');
const approvals = read('approvals.json');
const reused = read('reuse-inventory.json').reused;
if (approvals.concept_batch_2026_11_03?.status !== 'approved') throw Error('Spend approval required.');
const remaining = [];
let spent = 0;
for (const req of batch.requests) {
  const resultPath = path.join(__dirname, 'generated', req.id + '-result.json');
  const runPath = path.join(__dirname, 'generated', req.id + '-run.json');
  const imagePath = path.join(__dirname, 'generated', req.id + '.png');
  if (fs.existsSync(resultPath)) {
    const result = JSON.parse(fs.readFileSync(resultPath, 'utf8'));
    if (result.status !== 'success') throw Error(`Resolve existing result before generation: ${req.id}`);
    if (!fs.existsSync(imagePath)) throw Error(`Download existing output instead of regenerating: ${req.id}`);
    spent += result.cuCost ?? 0;
    continue;
  }
  if (reused.some(r => r.id === req.id)) throw Error(`Recover existing concept instead: ${req.id}`);
  if (fs.existsSync(runPath) || fs.existsSync(imagePath)) throw Error(`Existing job or art requires reconciliation: ${req.id}`);
  remaining.push(req);
}
const prices = read('batch-prices.json').quotes;
if (fs.existsSync(path.join(__dirname,'supplements/child-b-result.json'))) {
  const supplement = read('supplements/child-b-result.json');
  if (supplement.status==='success') spent += supplement.cuCost;
}
const estimatedCU = remaining.reduce((n,r)=>n+prices.find(p=>p.id===r.id).creativeUnitsCost,0);
const authorizedCU = approvals.concept_batch_2026_11_03.maximumCU - spent;
if (estimatedCU > authorizedCU) throw Error('Remaining requests exceed the authorized budget.');
fs.writeFileSync(path.join(__dirname, 'remaining-requests.json'), JSON.stringify({requests:remaining,estimatedCU,authorizedCU,spentCU:spent},null,2)+'\n');
console.log(`${remaining.length} missing sheets, ${estimatedCU} CU quoted, ${authorizedCU} CU authorized; completed and recovered sheets excluded.`);
