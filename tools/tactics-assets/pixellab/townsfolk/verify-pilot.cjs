const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { PNG } = require('pngjs');
const read = name => JSON.parse(fs.readFileSync(path.join(__dirname,name),'utf8'));
const bytes = name => fs.readFileSync(path.join(__dirname,name));
const approval = read('sprite-approval.json');
assert.equal(approval.candidate,1);
assert.equal(crypto.createHash('sha256').update(bytes(approval.source)).digest('hex'),approval.sourceSha256);
assert.equal(approval.idleApproval.status,'approved');
assert.equal(crypto.createHash('sha256').update(bytes(approval.idleApproval.preview)).digest('hex'),approval.idleApproval.previewSha256);
const source = PNG.sync.read(bytes(approval.source));
const cropped = PNG.sync.read(bytes('directional/approved-south-east.png'));
const imported = PNG.sync.read(bytes('directional/curated-rotations/south-east.png'));
assert.deepEqual([cropped.width,cropped.height],[48,80]);
assert.deepEqual([imported.width,imported.height],[80,80]);
for(let y=0;y<80;y++)for(let x=0;x<48;x++) {
  const a=(y*48+x)*4,b=((y+48)*source.width+x+56)*4,c=(y*80+x+16)*4;
  assert.deepEqual(cropped.data.subarray(a,a+4),source.data.subarray(b,b+4),'Crop changed source pixels');
  assert.deepEqual(imported.data.subarray(c,c+4),cropped.data.subarray(a,a+4),'Import changed approved pixels');
}
function visibleHeight(p) {
  let top=p.height,bottom=-1;
  for(let y=0;y<p.height;y++)for(let x=0;x<p.width;x++)if(p.data[(y*p.width+x)*4+3]){top=Math.min(top,y);bottom=y;}
  assert(bottom>=top,'Empty sprite');return bottom-top+1;
}
const hero=PNG.sync.read(bytes('current-hero-idle.png'));
const ratio=visibleHeight(source)/visibleHeight(hero);
assert(ratio>=.9&&ratio<=1.1,`Bree/hero height ratio ${ratio}`);
const directions=read('bree-static-result.json').structuredContent.frames;
assert.equal(directions,4,'Static candidate count changed');
console.log(JSON.stringify({approval:'candidate 1 + idle',crop:'pixel exact',import:'pixel exact',heightRatio:ratio}));
