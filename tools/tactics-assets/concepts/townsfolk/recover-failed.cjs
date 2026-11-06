const fs = require('node:fs');
const path = require('node:path');
const read = name => JSON.parse(fs.readFileSync(path.join(__dirname,name),'utf8'));
const ids = ['townsfolk_farmhand','townsfolk_guard','townsfolk_merchant'];
const batch = read('batch-requests.json');
if (process.argv.includes('--promote')) {
  const recoveryDir = process.argv.includes('--resolution') ? 'resolution-recovery' : 'recovery';
  for (const id of ids) {
    const r = read(`${recoveryDir}/${id}-result.json`);
    if (r.status!=='success' || !fs.existsSync(path.join(__dirname,`generated/${id}.png`))) throw Error(`Incomplete recovery ${id}`);
    fs.writeFileSync(path.join(__dirname,`generated/${id}-result.json`),JSON.stringify(r,null,2)+'\n');
    fs.copyFileSync(path.join(__dirname,`${recoveryDir}/${id}-run.json`),path.join(__dirname,`generated/${id}-run.json`));
  }
  console.log('Promoted three completed recovery outputs; original failures retained.');
} else {
  const requests = ids.map(id=>{
    const req = structuredClone(batch.requests.find(r=>r.id===id));
    if (fs.existsSync(path.join(__dirname,`generated/${id}.png`))) throw Error(`Do not regenerate existing art: ${id}`);
    if (id!=='townsfolk_merchant') {
      const r = read(`generated/${id}-result.json`);
      if(r.status!=='failure' || r.assetIds.length) throw Error(`Reconcile output before retrying ${id}`);
      fs.mkdirSync(path.join(__dirname,'failures'),{recursive:true});
      fs.copyFileSync(path.join(__dirname,`generated/${id}-result.json`),path.join(__dirname,`failures/${id}-attempt-1-result.json`));
      fs.copyFileSync(path.join(__dirname,`generated/${id}-run.json`),path.join(__dirname,`failures/${id}-attempt-1-run.json`));
      req.parameters.aspectRatio='16:9';
      req.parameters.prompt += ' Use a wide two-row layout: variant A across the top with front, three-quarter, rear and head; variant B across the bottom with front, three-quarter, rear and head. Keep all six full-body figures complete, with matching scale and each variant\'s own consistent hair, skin and clothes. No text.';
    }
    return req;
  });
  fs.writeFileSync(path.join(__dirname,'recovery-requests.json'),JSON.stringify({requests,reason:'Two provider output-token failures with no assets; a wider ratio follows the provider recovery hint. Merchant was not submitted previously.',maximumAdditionalCU:39},null,2)+'\n');
  console.log('Prepared two reconciled retries and the unsubmitted merchant.');
}
