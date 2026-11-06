// Offline provenance and exact reuse checks; no API or credential access.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {PNG}=require('pngjs');
const root=__dirname,repo=path.resolve(root,'../../../../..');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const beforeBytes=fs.readFileSync(path.join(root,'directional/before-walk-correction-atlas.png'));
const before=PNG.sync.read(beforeBytes),current=PNG.sync.read(fs.readFileSync(path.join(root,'review_tobin.png')));
let preserved=0,changed=0;
for(let row=0;row<8;row++)for(let f=0;f<18;f++){
 let equal=true;
 for(let y=0;y<96;y++){const p=((row*96+y)*current.width+f*96)*4;if(!before.data.subarray(p,p+384).equals(current.data.subarray(p,p+384)))equal=false;}
 if(row!==0||f<6||f>11){assert(equal,`Unrelated completed frame changed: ${row}/${f}`);preserved++;}
 else if(!equal)changed++;
}
assert.equal(preserved,138);assert.equal(changed,6);
const records=[];
for(const o of JSON.parse(fs.readFileSync(path.join(root,'generation-output-index.json')))){
 const portrait=o.direction==='portraits';
 records.push({request:portrait?'portraits-request.json':`directional/${o.direction}-request.json`,output:portrait?'portraits-source.png':`directional/${o.direction}-source.png`,originalGeneratedPath:o.path});
}
records.push({request:'directional/south-walk-correction-request.json',output:'directional/south-walk-corrected-source.png',originalGeneratedPath:JSON.parse(fs.readFileSync(path.join(root,'directional/south-walk-correction-receipt.json'))).path});
for(const r of records){
 const request=fs.readFileSync(path.join(root,r.request));r.requestSha256=hash(request);r.outputSha256=hash(fs.readFileSync(path.join(root,r.output)));
 r.inputs=JSON.parse(request).referenced_image_paths.map(p=>{
  const relative=p.replace(/^\/workspace\/ArcBox\//,'');return {path:relative,sha256:hash(fs.readFileSync(path.join(repo,relative)))};
 });
 r.tool='image_gen.imagegen';r.pixelLabCalls=0;
}
fs.writeFileSync(path.join(root,'generation-receipts.json'),JSON.stringify({date:'2026-11-04',completionImageToolCalls:records.length,priorAppearanceImageToolCalls:1,pixelLabCalls:0,pixelLabCreditsSpent:0,monetaryImageToolCost:'not quoted by tool; no price inferred',records},null,2)+'\n');
fs.writeFileSync(path.join(root,'directional/curation.json'),JSON.stringify({compassOrder:['south','south-west','west','north-west','north','north-east','east','south-east'],sourceGrid:'6 columns x 3 rows: idle/walk/talk',registration:'One regular column grid fit per direction from all six planted idle-foot centres; one scale/offset shared across all18 poses. No per-frame offsets.',southWalk:{source:'south-walk-corrected-source.png',sourceColumns:[0,5,4,2,1,3],reason:'Original row repeated one lead leg; corrected row supplies opposite-leg contacts. Curated contact/settle/passing sequence.',preservedOtherFrames:preserved,changedFrames:changed,previousAtlasSha256:hash(beforeBytes)},northEastAlpha:'Raw RGB contains colourful low-alpha background artifacts. Binary128 import discards these; runtime backgrounds are transparent.',mirroring:false,perFrameRecentering:false},null,2)+'\n');
console.log(JSON.stringify({completionImageToolCalls:records.length,preservedFrames:preserved,correctedFrames:changed,pixelLabCalls:0}));
