// Exact reuse check: corrections must not recolour/rebuild unrelated native poses.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const {PNG}=require('pngjs');
const root=__dirname,oldBytes=fs.readFileSync(path.join(root,'directional/before-curation-atlas.png')),newBytes=fs.readFileSync(path.join(root,'review_lark.png'));
const before=PNG.sync.read(oldBytes),after=PNG.sync.read(newBytes),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
let preserved=0;
for(let row=0;row<8;row++)for(let f=0;f<18;f++){
 if(row===3||(row===1&&f===17)||(row===0&&f>=6&&f<=11))continue;
 for(let y=0;y<96;y++){
  const p=((row*96+y)*after.width+f*96)*4;
  assert(before.data.subarray(p,p+384).equals(after.data.subarray(p,p+384)),`Unrelated pose changed: ${row}/${f}`);
 }
 preserved++;
}
assert.equal(preserved,119);
const result={result:'PASS',preservedNativeFrames:preserved,correctedNorthWestSlots:18,correctedSouthWalkSlots:6,curatedHeldTalkSlots:1,priorAtlasSha256:hash(oldBytes),currentAtlasSha256:hash(newBytes)};
fs.writeFileSync(path.join(root,'directional/preserved-frames.json'),JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify(result));
