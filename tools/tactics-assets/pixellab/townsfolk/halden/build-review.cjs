// Offline only: pack saved provider pixels without resizing, mirroring or per-frame recentering.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict'), crypto = require('node:crypto');
const { PNG } = require('pngjs');
const { GIFEncoder, quantize, applyPalette } = require('gifenc');
const root = __dirname, dirs = ['south','south-west','west','north-west','north','north-east','east','south-east'];
const specs = { idle: { count:4, fps:10/3 }, walk: { count:6, fps:20/3 }, talk: { count:5, fps:16/3, playback:[0,1,2,3,4,3,2,1] } };
const meta = JSON.parse(fs.readFileSync(path.join(root,'native/metadata.json'))), state = meta.states[0];
const width=96, height=96, anchor=[48,84], columns=15;
const atlas = new PNG({width:columns*width,height:8*height});
const audit={reviewOnly:true,productionInstalled:false,characterId:state.character.id,directions:dirs,frame:{width,height,anchor},pixelScale:.5,actions:{},sources:[],warnings:[]}, anims={};
function read(file) {
  const bytes=fs.readFileSync(path.join(root,'native',file));
  audit.sources.push({file,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});
  return PNG.sync.read(bytes);
}
function bounds(image) {
  let left=image.width,top=image.height,right=-1,bottom=-1;
  for(let y=0;y<image.height;y++)for(let x=0;x<image.width;x++)if(image.data[(y*image.width+x)*4+3]){left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);}
  assert(right>=left,'Empty provider frame');return {left,top,right,bottom};
}
let start=0;
for(const [action,spec] of Object.entries(specs)) {
  const group=state.frames.animations[`halden_${action}`];assert(group,`Missing halden_${action}`);
  anims[action]={frames:(spec.playback??Array.from({length:spec.count},(_,i)=>i)).map(i=>start+i),fps:spec.fps,loop:true};
  audit.actions[action]={};
  for(const [row,dir] of dirs.entries()) {
    assert.equal(group[dir]?.length,spec.count,`Incomplete ${action}/${dir}`);
    const rotation=read(state.frames.rotations[dir]), rb=bounds(rotation), images=group[dir].map(read);
    if(rb.top===0) audit.warnings.push(`${dir} static rotation touches top edge; inspect silhouette during owner review`);
    const size=images[0];assert(images.every(i=>i.width===size.width&&i.height===size.height),'Provider changed canvas within a clip');
    // One translation per entire direction/clip, derived from its rotation, never from individual motion frames.
    const dx=anchor[0]-Math.floor(rotation.width/2)-Math.floor((size.width-rotation.width)/2);
    const dy=anchor[1]-rb.bottom-Math.floor((size.height-rotation.height)/2);
    const measurements=[];
    for(const [i,image] of images.entries()) {
      const b=bounds(image);
      assert(b.left+dx>=0&&b.right+dx<width&&b.top+dy>=0&&b.bottom+dy<height,`Compact canvas clips ${action}/${dir}/${i}`);
      measurements.push({frame:i,bounds:b,footWorld:(b.bottom+dy)*.5,heightWorld:(b.bottom-b.top+1)*.5});
      for(let y=0;y<image.height;y++)for(let x=0;x<image.width;x++) {
        const p=(y*image.width+x)*4;if(!image.data[p+3])continue;
        const q=((row*height+y+dy)*atlas.width+(start+i)*width+x+dx)*4;
        image.data.copy(atlas.data,q,p,p+4);
      }
    }
    const feet=measurements.map(m=>m.footWorld);
    audit.actions[action][dir]={fixedOffset:[dx,dy],nativeCanvas:[size.width,size.height],maxFootDriftWorld:Math.max(...feet)-Math.min(...feet),frames:measurements};
  }
  const contacts=new PNG({width:spec.count*width,height:8*height});
  for(let r=0;r<8;r++)for(let i=0;i<spec.count;i++)PNG.bitblt(atlas,contacts,(start+i)*width,r*height,width,height,i*width,r*height);
  fs.writeFileSync(path.join(root,`${action}-contact.png`),PNG.sync.write(contacts));
  const frames=anims[action].frames.map(index=>{
    const out=new PNG({width:width*4,height:height*2});
    for(let r=0;r<8;r++)PNG.bitblt(atlas,out,index*width,r*height,width,height,r%4*width,Math.floor(r/4)*height);
    for(let p=0;p<out.data.length;p+=4){const a=out.data[p+3]/255;[35,32,40].forEach((v,c)=>out.data[p+c]=Math.round(out.data[p+c]*a+v*(1-a)));out.data[p+3]=255;}return out;
  });
  const palette=quantize(Buffer.concat(frames.map(f=>f.data)),256),gif=GIFEncoder();
  for(const f of frames)gif.writeFrame(applyPalette(f.data,palette),f.width,f.height,{palette,delay:1000/spec.fps,repeat:0});
  gif.finish();fs.writeFileSync(path.join(root,`halden-eight-direction-${action}.gif`),gif.bytes());
  start+=spec.count;
}
anims.down={frames:[0],fps:1,loop:false};
const bytes=PNG.sync.write(atlas);fs.writeFileSync(path.join(root,'review_halden.png'),bytes);
audit.atlas={width:atlas.width,height:atlas.height,uncompressedMiB:atlas.width*atlas.height*4/1024/1024,sha256:crypto.createHash('sha256').update(bytes).digest('hex')};
audit.warnings=[...new Set(audit.warnings)];
fs.writeFileSync(path.join(root,'atlas-validation.json'),JSON.stringify(audit,null,2)+'\n');
fs.writeFileSync(path.join(root,'review-sprite.json'),JSON.stringify({file:'review_halden.png',family:'townsfolk',variant:'halden_review',frame:{width,height,anchor},headTop:10,pixelScale:.5,columns,rowsPerDirection:1,directions:dirs,footprintWidth:16,footprintHeight:36,keys:['characters.mayor'],anims,provenance:'REVIEW ONLY: Halden directional set; owner TV approval required before production installation.'},null,2)+'\n');
console.log(JSON.stringify({atlas:audit.atlas,warnings:audit.warnings,productionInstalled:false}));
