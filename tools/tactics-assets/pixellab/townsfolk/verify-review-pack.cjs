const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {PNG}=require('pngjs');
const pack=process.argv[2]||path.join(require('node:os').homedir(),'.codex/tmp/bree-tv-review');
const manifest=JSON.parse(fs.readFileSync(path.join(pack,'creatures/manifest.json'),'utf8'));
const sprite=manifest.sprites.review_bree;
assert.deepEqual(sprite.directions,manifest.directions,'Direction labels/order differ');
const atlas=PNG.sync.read(fs.readFileSync(path.join(pack,'creatures',sprite.file)));
assert.equal(atlas.width,sprite.columns*sprite.frame.width);
assert.equal(atlas.height,sprite.rowsPerDirection*sprite.frame.height*8);
const result={atlas:[atlas.width,atlas.height],uncompressedMiB:atlas.width*atlas.height*4/1024/1024,actions:{}};
for(const action of ['idle','walk','talk']) {
  const clip=sprite.anims[action],stats=[];
  for(const[direction,name]of sprite.directions.entries()) {
    let maxFootDriftWorld=0,minHeightWorld=Infinity,maxHeightWorld=0;
    for(const index of clip.frames) {
      assert(index>=0&&index<sprite.columns*sprite.rowsPerDirection);
      const {width,height}=sprite.frame;
      const ox=index%sprite.columns*width,oy=(direction*sprite.rowsPerDirection+Math.floor(index/sprite.columns))*height;
      let left=width,right=-1,top=height,bottom=-1;
      for(let y=0;y<height;y++)for(let x=0;x<width;x++)if(atlas.data[((oy+y)*atlas.width+ox+x)*4+3]) {left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);}
      assert(left>0&&right<width-1&&top>0&&bottom<height-1,`Empty/clipped frame ${action}/${name}/${index}`);
      maxFootDriftWorld=Math.max(maxFootDriftWorld,Math.abs(bottom-sprite.frame.anchor[1])*sprite.pixelScale);
      minHeightWorld=Math.min(minHeightWorld,(bottom-top+1)*sprite.pixelScale);maxHeightWorld=Math.max(maxHeightWorld,(bottom-top+1)*sprite.pixelScale);
    }
    if(action!=='walk')assert(maxFootDriftWorld<=2,`Foot drift ${action}/${name}: ${maxFootDriftWorld}`);
    assert(minHeightWorld>=30&&maxHeightWorld<=43,`Wrong adult height ${action}/${name}`);
    stats.push({direction:name,frames:clip.frames.length,maxFootDriftWorld,minHeightWorld,maxHeightWorld});
  }
  result.actions[action]=stats;
}
// The compact canvas must preserve the approved southeast idle's visible pixels.
for(let i=0;i<5;i++) {
  const original=PNG.sync.read(fs.readFileSync(path.join(__dirname,`idle/frame-${i}.png`)));
  const ox=i%sprite.columns*sprite.frame.width,oy=(7*sprite.rowsPerDirection+Math.floor(i/sprite.columns))*sprite.frame.height;
  for(let y=0;y<sprite.frame.height;y++)for(let x=0;x<sprite.frame.width;x++) {
    const a=((y+39)*original.width+x+32)*4,b=((oy+y)*atlas.width+ox+x)*4;
    if(original.data[a+3]||atlas.data[b+3])assert.deepEqual(atlas.data.subarray(b,b+4),original.data.subarray(a,a+4),'Compact canvas changed approved pixels');
  }
}
result.approvedSoutheastIdle='pixel exact after transparent padding crop';
fs.writeFileSync(path.join(__dirname,'directional/atlas-validation.json'),JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify(result));
