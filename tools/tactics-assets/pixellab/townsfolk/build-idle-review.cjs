// Preview dependencies: pngjs and gifenc (may be installed outside the repository
// and exposed with NODE_PATH). This script never modifies production assets.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { PNG } = require('pngjs');
const { GIFEncoder, quantize, applyPalette } = require('gifenc');
const read = file => PNG.sync.read(fs.readFileSync(path.join(__dirname,file)));
const candidate = Number(process.argv[2] || 1);
assert(Number.isInteger(candidate) && candidate >= 1 && candidate <= 4, 'Candidate must be 1–4');
const outputRoot = candidate === 1 ? __dirname : path.join(__dirname,`candidate-${candidate}-idle`);
const source = read(`normalized/candidate-${candidate}.png`);
const hero = read('current-hero-idle.png');
const frames = Array.from({length:5},(_,i)=>read(candidate === 1 ? `idle/frame-${i}.png` : `candidate-${candidate}-idle/frames/frame-${i}.png`));
assert.deepEqual(frames[0].data, source.data, 'Provider changed input frame');
const measurements = [];
const previews = [];
const contact = new PNG({width:160*frames.length,height:160});
for (const [i,frame] of frames.entries()) {
  assert.equal(frame.width,160); assert.equal(frame.height,160);
  let minX=160,minY=160,maxX=-1,maxY=-1,changedPixels=0,footSilhouetteChangedPixels=0;
  for(let y=0;y<160;y++)for(let x=0;x<160;x++) {
    const p=(y*160+x)*4;
    if(frame.data[p+3]) {minX=Math.min(minX,x);minY=Math.min(minY,y);maxX=Math.max(maxX,x);maxY=Math.max(maxY,y);}
    // RGB bytes under zero alpha can differ without changing the visible sprite.
    if((frame.data[p+3] || source.data[p+3]) && !frame.data.subarray(p,p+4).equals(source.data.subarray(p,p+4)))changedPixels++;
    if(y>=118 && Boolean(frame.data[p+3])!==Boolean(source.data[p+3]))footSilhouetteChangedPixels++;
    frame.data.copy(contact.data,(y*contact.width+i*160+x)*4,p,p+4);
  }
  assert(minX>0&&minY>0&&maxX<159&&maxY<159,'Frame clips canvas');
  measurements.push({frame:i,bounds:[minX,minY,maxX,maxY],changedPixels,footSilhouetteChangedPixels,sha256:crypto.createHash('sha256').update(frame.data).digest('hex')});
  const preview = new PNG({width:960,height:480});
  for(let y=0;y<480;y++)for(let x=0;x<960;x++) {
    const s=x<480?hero:frame, sx=Math.floor((x%480)/3),sy=Math.floor(y/3),p=(sy*160+sx)*4,q=(y*960+x)*4,a=s.data[p+3]/255;
    [35,32,40].forEach((bg,c)=>preview.data[q+c]=Math.round(s.data[p+c]*a+bg*(1-a)));preview.data[q+3]=255;
  }
  previews.push(preview);
}
const palette = quantize(Buffer.concat(previews.map(p=>p.data)),256);
const gif = GIFEncoder();
for(const p of previews)gif.writeFrame(applyPalette(p.data,palette),p.width,p.height,{palette,delay:300,repeat:0});
gif.finish();
fs.writeFileSync(path.join(outputRoot,'bree-idle-review.gif'),gif.bytes());
fs.writeFileSync(path.join(outputRoot,'bree-idle-frames.png'),PNG.sync.write(contact));
fs.writeFileSync(path.join(outputRoot,'idle-measurements.json'),JSON.stringify({candidate,displayFrameMs:300,sourcePlusGeneratedFrames:5,individualFrameTransforms:false,frames:measurements},null,2)+'\n');
console.log(JSON.stringify(measurements));
