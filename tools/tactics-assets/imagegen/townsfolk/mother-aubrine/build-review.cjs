// Deterministic asset import: one uniform scale/translation per direction, never per-frame recentering.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {PNG}=require('pngjs');
const {GIFEncoder,quantize,applyPalette}=require('gifenc');
const root=__dirname,dirs=['south','south-west','west','north-west','north','north-east','east','south-east'];
const W=96,H=96,anchor=[48,84],columns=18,threshold=128;
const specs={idle:{row:0,start:0,frames:[0,1,2,3,4,5,4,3,2,1],fps:20/3},walk:{row:1,start:6,frames:[0,1,2,3,4,5],fps:20/3},talk:{row:2,start:12,frames:[0,1,2,3,4,5,4,3,2,1],fps:20/3}};
const atlas=new PNG({width:columns*W,height:8*H}),audit={reviewOnly:true,productionInstalled:false,pixelLabCreditsSpent:0,frame:{width:W,height:H,anchor},pixelScale:.5,paletteColors:96,alphaThreshold:threshold,perFrameRecentering:false,mirroring:false,directions:{},sources:[]};
function hash(bytes){return crypto.createHash('sha256').update(bytes).digest('hex');}
function median(values){const a=[...values].sort((a,b)=>a-b);return (a[Math.floor((a.length-1)/2)]+a[Math.ceil((a.length-1)/2)])/2;}
function sourceBounds(img,col,row){
 const cw=img.width/6,ch=img.height/3;let left=Infinity,top=Infinity,right=-1,bottom=-1;
 for(let y=Math.ceil(row*ch);y<Math.floor((row+1)*ch);y++)for(let x=Math.ceil(col*cw);x<Math.floor((col+1)*cw);x++)if(img.data[(y*img.width+x)*4+3]>=threshold){left=Math.min(left,x-col*cw);right=Math.max(right,x-col*cw);top=Math.min(top,y-row*ch);bottom=Math.max(bottom,y-row*ch);}
 assert(right>=left,`Empty ${col},${row}`);return {left,top,right,bottom};
}
for(const [index,dir] of dirs.entries()){
 const relative=`directional/${dir}${dir==='south-east'||dir==='west'?'-clean':''}-source.png`,bytes=fs.readFileSync(path.join(root,relative)),img=PNG.sync.read(bytes);
 audit.sources.push({path:relative,sha256:hash(bytes)});
 const cw=img.width/6,ch=img.height/3,idle=Array.from({length:6},(_,i)=>sourceBounds(img,i,0));
 const scale=72/median(idle.map(b=>b.bottom-b.top+1));
 const offset=[anchor[0]-median(idle.map(b=>(b.left+b.right)/2))*scale,anchor[1]-median(idle.map(b=>b.bottom))*scale];
 const settings={nativeSource:[img.width,img.height],sourceGrid:[6,3],sourceCell:[cw,ch],scale,fixedOffset:offset,actions:{}};
 for(const [action,spec] of Object.entries(specs)){
  const measurements=[];
  for(let frame=0;frame<6;frame++){
   const bound=sourceBounds(img,frame,spec.row);
   assert(bound.left*scale+offset[0]>=0&&bound.right*scale+offset[0]<W&&bound.top*scale+offset[1]>=0&&bound.bottom*scale+offset[1]<H,`Clipped ${dir}/${action}/${frame}`);
   let left=W,top=H,right=-1,bottom=-1,opaque=0;
   for(let y=0;y<H;y++)for(let x=0;x<W;x++){
    const localX=(x-offset[0])/scale,localY=(y-offset[1])/scale;
    if(localX<0||localX>=cw||localY<0||localY>=ch)continue;
    const sx=Math.floor(frame*cw+localX),sy=Math.floor(spec.row*ch+localY),p=(sy*img.width+sx)*4;
    if(img.data[p+3]<threshold)continue;
    const q=((index*H+y)*atlas.width+(spec.start+frame)*W+x)*4;
    for(let c=0;c<3;c++)atlas.data[q+c]=img.data[p+c];atlas.data[q+3]=255;
    left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);opaque++;
   }
   assert(opaque>200,`Empty/sparse frame ${dir}/${action}/${frame}`);
   assert(left>0&&right<W-1&&top>0&&bottom<H-1,`Output edge contact ${dir}/${action}/${frame}`);
   const heightWorld=(bottom-top+1)*.5,footDeltaWorld=(bottom-anchor[1])*.5;
   assert(heightWorld>=30&&heightWorld<=42,`Wrong adult body size ${dir}/${action}/${frame}: ${heightWorld}`);
   assert(Math.abs(footDeltaWorld)<=(action==='walk'?4:2),`Foot drift ${dir}/${action}/${frame}: ${footDeltaWorld}`);
   measurements.push({frame,sourceBounds:bound,outputBounds:[left,top,right,bottom],opaquePixels:opaque,heightWorld,footDeltaWorld});
  }
  settings.actions[action]={frames:measurements,maxFootDriftWorld:Math.max(...measurements.map(m=>Math.abs(m.footDeltaWorld)))};
 }
 audit.directions[dir]=settings;
}
// One shared palette prevents independently quantized frames changing the clothing colours.
const opaque=[];for(let p=0;p<atlas.data.length;p+=4)if(atlas.data[p+3])opaque.push(atlas.data[p],atlas.data[p+1],atlas.data[p+2],255);
const palette=JSON.parse(fs.readFileSync(path.join(root,'palette.json'))),indexed=applyPalette(atlas.data,palette);
assert.equal(palette.length,96,'Shared palette changed');
for(let p=0,i=0;p<atlas.data.length;p+=4,i++)if(atlas.data[p+3])for(let c=0;c<3;c++)atlas.data[p+c]=palette[indexed[i]][c];
// Reuse the seven completed native rows exactly; palette tie-breaking must not alter them while fixing west.
const lockedBytes=fs.readFileSync(path.join(root,'directional/locked-native-rows.png')),locked=PNG.sync.read(lockedBytes);
assert.equal(locked.width,atlas.width);assert.equal(locked.height,atlas.height);
for(let row=0;row<8;row++)if(dirs[row]!=='west')PNG.bitblt(locked,atlas,0,row*H,atlas.width,H,0,row*H);
audit.sources.push({path:'directional/locked-native-rows.png',sha256:hash(lockedBytes)});
audit.preservedNativeDirections=dirs.filter(d=>'west'!==d);
audit.paletteSha256=hash(fs.readFileSync(path.join(root,'palette.json')));
const anims={};
for(const [action,spec] of Object.entries(specs)){
 anims[action]={frames:spec.frames.map(f=>spec.start+f),fps:spec.fps,loop:true};
 const contact=new PNG({width:6*W,height:8*H});
 for(let d=0;d<8;d++)for(let f=0;f<6;f++)PNG.bitblt(atlas,contact,(spec.start+f)*W,d*H,W,H,f*W,d*H);
 fs.writeFileSync(path.join(root,`aubrine-${action}-contact.png`),PNG.sync.write(contact));
 const gif=GIFEncoder();
 for(const f of spec.frames){
  const output=new PNG({width:W*8,height:H*4});
  for(let d=0;d<8;d++)for(let y=0;y<H*2;y++)for(let x=0;x<W*2;x++){
   const p=((d*H+Math.floor(y/2))*atlas.width+(spec.start+f)*W+Math.floor(x/2))*4,q=((Math.floor(d/4)*H*2+y)*output.width+(d%4)*W*2+x)*4;
   for(let c=0;c<3;c++)output.data[q+c]=atlas.data[p+3]?atlas.data[p+c]:[35,32,40][c];output.data[q+3]=255;
  }
  const pal=quantize(output.data,128);gif.writeFrame(applyPalette(output.data,pal),output.width,output.height,{palette:pal,delay:1000/spec.fps,repeat:0});
 }
 gif.finish();fs.writeFileSync(path.join(root,`aubrine-eight-direction-${action}.gif`),gif.bytes());
}
anims.down={frames:[0],fps:1,loop:false};
const bytes=PNG.sync.write(atlas);fs.writeFileSync(path.join(root,'review_aubrine.png'),bytes);
audit.atlas={width:atlas.width,height:atlas.height,uncompressedMiB:atlas.width*atlas.height*4/1048576,sha256:hash(bytes)};
fs.writeFileSync(path.join(root,'atlas-validation.json'),JSON.stringify(audit,null,2)+'\n');
fs.writeFileSync(path.join(root,'review-sprite.json'),JSON.stringify({file:'review_aubrine.png',family:'townsfolk',variant:'mother_aubrine_review',frame:{width:W,height:H,anchor},headTop:12,pixelScale:.5,columns,rowsPerDirection:1,directions:dirs,footprintWidth:16,footprintHeight:36,keys:['characters.priest','characters.mother_aubrine'],anims,provenance:'REVIEW ONLY: image-generated Mother Aubrine; appearance approved, full directional/motion/TV review pending.'},null,2)+'\n');
console.log(JSON.stringify({atlas:audit.atlas,footDrift:Object.fromEntries(dirs.map(d=>[d,Object.fromEntries(Object.entries(audit.directions[d].actions).map(([a,v])=>[a,v.maxFootDriftWorld]))])),productionInstalled:false}));
