// Offline provenance only: no API connection, credentials or credit purchases.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=__dirname,repo=path.resolve(root,'../../../../..'),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const records=[];
for(const o of JSON.parse(fs.readFileSync(path.join(root,'generation-output-index.json')))){
 const portrait=o.direction==='portraits';
 records.push({request:portrait?'portraits-request.json':`directional/${o.direction}-request.json`,output:portrait?'portraits-source.png':`directional/${o.direction}-source.png`,originalGeneratedPath:o.path});
}
const extra=path.join(root,'generation-corrections.json');
if(fs.existsSync(extra))records.push(...JSON.parse(fs.readFileSync(extra)));
for(const r of records){
 const request=fs.readFileSync(path.join(root,r.request));r.requestSha256=hash(request);r.outputSha256=hash(fs.readFileSync(path.join(root,r.output)));
 r.inputs=JSON.parse(request).referenced_image_paths.map(p=>{
  const relative=p.replace(/^\/workspace\/ArcBox\//,'');return {path:relative,sha256:hash(fs.readFileSync(path.join(repo,relative)))};
 });
 r.tool='image_gen.imagegen';r.pixelLabCalls=0;
}
fs.writeFileSync(path.join(root,'generation-receipts.json'),JSON.stringify({date:'2026-11-04',completionImageToolCalls:records.length,priorAppearanceImageToolCalls:1,pixelLabCalls:0,pixelLabCreditsSpent:0,monetaryImageToolCost:'not quoted by tool; no price inferred',records},null,2)+'\n');
console.log(JSON.stringify({completionImageToolCalls:records.length,pixelLabCalls:0}));
