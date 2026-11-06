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
function sourceBounds(img,col,row,grid){
 const {cw,ch,originX}=grid;let left=Infinity,top=Infinity,right=-1,bottom=-1;
 for(let y=Math.ceil(row*ch);y<Math.floor((row+1)*ch);y++)for(let x=Math.max(0,Math.ceil(originX+col*cw));x<Math.min(img.width,Math.floor(originX+(col+1)*cw));x++)if(img.data[(y*img.width+x)*4+3]>=threshold){left=Math.min(left,x-originX-col*cw);right=Math.max(right,x-originX-col*cw);top=Math.min(top,y-row*ch);bottom=Math.max(bottom,y-row*ch);}
 assert(right>=left,`Empty ${col},${row}`);return {left,top,right,bottom};
}
for(const [index,dir] of dirs.entries()){
 const corrected=`directional/${dir}-corrected-source.png`;
 const relative=fs.existsSync(path.join(root,corrected))?corrected:`directional/${dir}-source.png`,bytes=fs.readFileSync(path.join(root,relative)),img=PNG.sync.read(bytes);
 audit.sources.push({path:relative,sha256:hash(bytes)});
 // The generator spaces columns inside outer margins, rather than at canvasWidth/6.
 // Fit ONE regular grid from all six planted-idle foot centres. No per-frame shifts.
 const coarse={cw:img.width/6,ch:img.height/3,originX:0},centres=[];
 for(let f=0;f<6;f++){
  const b=sourceBounds(img,f,0,coarse);let l=Infinity,r=-1;
  for(let y=Math.ceil(b.bottom-(b.bottom-b.top)*.08);y<=Math.floor(b.bottom);y++)
   for(let x=Math.ceil(f*coarse.cw);x<Math.floor((f+1)*coarse.cw);x++)if(img.data[(y*img.width+x)*4+3]>=threshold){l=Math.min(l,x);r=Math.max(r,x);}
  centres.push((l+r)/2);
 }
 const mean=centres.reduce((a,b)=>a+b,0)/6;
 const pitch=centres.reduce((sum,c,f)=>sum+(f-2.5)*(c-mean),0)/17.5;
 const intercept=mean-2.5*pitch,grid={cw:pitch,ch:img.height/3,originX:intercept-pitch/2};
 assert(pitch>img.width/8&&pitch<img.width/5,'Invalid shared grid fit');
 const {cw,ch,originX}=grid,idle=Array.from({length:6},(_,i)=>sourceBounds(img,i,0,grid));
 const walkPath=path.join(root,'directional',dir+'-walk-corrected-source.png');
 let correctedWalk=null;
 if(fs.existsSync(walkPath)){
  const correctedBytes=fs.readFileSync(walkPath);correctedWalk=PNG.sync.read(correctedBytes);
  assert.equal(correctedWalk.width,img.width);assert.equal(correctedWalk.height,img.height);
  audit.sources.push({path:`directional/${dir}-walk-corrected-source.png`,sha256:hash(correctedBytes)});
 }
 const scale=72/median(idle.map(b=>b.bottom-b.top+1));
 const offset=[anchor[0]-cw/2*scale,anchor[1]-median(idle.map(b=>b.bottom))*scale];
 const settings={nativeSource:[img.width,img.height],sourceGrid:[6,3],sourceCell:[cw,ch],sourceGridOrigin:[originX,0],gridRegistration:{method:"one least-squares column pitch from six idle foot centres",perFrameOffsets:false,idleFootCentres:centres,intercept,pitch},scale,fixedOffset:offset,actions:{}};
 for(const [action,spec] of Object.entries(specs)){
  const measurements=[];
  for(let frame=0;frame<6;frame++){
   const source=action==='walk'&&correctedWalk?correctedWalk:img;
   const sourceFrame=frame;
   const bound=sourceBounds(source,sourceFrame,spec.row,grid);
   assert(bound.left*scale+offset[0]>=0&&bound.right*scale+offset[0]<W&&bound.top*scale+offset[1]>=0&&bound.bottom*scale+offset[1]<H,`Clipped ${dir}/${action}/${frame}`);
   let left=W,top=H,right=-1,bottom=-1,opaque=0;
   for(let y=0;y<H;y++)for(let x=0;x<W;x++){
    const localX=(x-offset[0])/scale,localY=(y-offset[1])/scale;
    if(localX<0||localX>=cw||localY<0||localY>=ch)continue;
    const sx=Math.floor(originX+sourceFrame*cw+localX),sy=Math.floor(spec.row*ch+localY),p=(sy*img.width+sx)*4;
    if(source.data[p+3]<threshold)continue;
    const q=((index*H+y)*atlas.width+(spec.start+frame)*W+x)*4;
    for(let c=0;c<3;c++)atlas.data[q+c]=source.data[p+c];atlas.data[q+3]=255;
    left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);opaque++;
   }
   assert(opaque>200,`Empty/sparse frame ${dir}/${action}/${frame}`);
   assert(left>0&&right<W-1&&top>0&&bottom<H-1,`Output edge contact ${dir}/${action}/${frame}`);
   const heightWorld=(bottom-top+1)*.5,footDeltaWorld=(bottom-anchor[1])*.5;
   assert(heightWorld>=30&&heightWorld<=42,`Wrong adult body size ${dir}/${action}/${frame}: ${heightWorld}`);
   assert(Math.abs(footDeltaWorld)<=(action==='walk'?4:2),`Foot drift ${dir}/${action}/${frame}: ${footDeltaWorld}`);
   measurements.push({frame,sourceFrame,sourceBounds:bound,outputBounds:[left,top,right,bottom],opaquePixels:opaque,heightWorld,footDeltaWorld});
  }
  settings.actions[action]={frames:measurements,maxFootDriftWorld:Math.max(...measurements.map(m=>Math.abs(m.footDeltaWorld)))};
 }
 audit.directions[dir]=settings;
}
// One shared palette prevents independently quantized frames changing the clothing colours.
const opaque=[];for(let p=0;p<atlas.data.length;p+=4)if(atlas.data[p+3])opaque.push(atlas.data[p],atlas.data[p+1],atlas.data[p+2],255);
const palettePath=path.join(root,'palette.json');
if(!fs.existsSync(palettePath))fs.writeFileSync(palettePath,JSON.stringify(quantize(Uint8Array.from(opaque),96),null,2)+'\n');
const palette=JSON.parse(fs.readFileSync(palettePath));
assert.equal(palette.length,96,'Shared palette changed');
// Exact RGB cache, unlike a reduced-bit bucket whose first colour can change ties.
const rgbCache=new Map();
function nearest(r,g,b){
 const key=(r<<16)|(g<<8)|b;if(rgbCache.has(key))return rgbCache.get(key);
 let best=0,distance=Infinity;
 for(let i=0;i<palette.length;i++){const c=palette[i],d=(r-c[0])**2+(g-c[1])**2+(b-c[2])**2;if(d<distance){distance=d;best=i;}}
 rgbCache.set(key,best);return best;
}
for(let p=0;p<atlas.data.length;p+=4)if(atlas.data[p+3]){
 const color=palette[nearest(atlas.data[p],atlas.data[p+1],atlas.data[p+2])];
 for(let c=0;c<3;c++)atlas.data[p+c]=color[c];
}
const gifPalette=[...palette,[35,32,40]],exactIndices=new Map(gifPalette.map((c,i)=>[(c[0]<<16)|(c[1]<<8)|c[2],i]));
function exactGifIndices(rgba){const out=new Uint8Array(rgba.length/4);for(let i=0,p=0;p<rgba.length;p+=4,i++){const index=exactIndices.get((rgba[p]<<16)|(rgba[p+1]<<8)|rgba[p+2]);assert(index!==undefined);out[i]=index;}return out;}
audit.paletteSha256=hash(fs.readFileSync(path.join(root,'palette.json')));
const anims={};
for(const [action,spec] of Object.entries(specs)){
 anims[action]={frames:spec.frames.map(f=>spec.start+f),fps:spec.fps,loop:true};
 const contact=new PNG({width:6*W,height:8*H});
 for(let d=0;d<8;d++)for(let f=0;f<6;f++)PNG.bitblt(atlas,contact,(spec.start+f)*W,d*H,W,H,f*W,d*H);
 fs.writeFileSync(path.join(root,`dagna-${action}-contact.png`),PNG.sync.write(contact));
 const gif=GIFEncoder();
 for(const f of spec.frames){
  const output=new PNG({width:W*8,height:H*4});
  for(let d=0;d<8;d++)for(let y=0;y<H*2;y++)for(let x=0;x<W*2;x++){
   const p=((d*H+Math.floor(y/2))*atlas.width+(spec.start+f)*W+Math.floor(x/2))*4,q=((Math.floor(d/4)*H*2+y)*output.width+(d%4)*W*2+x)*4;
   for(let c=0;c<3;c++)output.data[q+c]=atlas.data[p+3]?atlas.data[p+c]:[35,32,40][c];output.data[q+3]=255;
  }
  gif.writeFrame(exactGifIndices(output.data),output.width,output.height,{palette:gifPalette,delay:1000/spec.fps,repeat:0});
 }
 gif.finish();fs.writeFileSync(path.join(root,`dagna-eight-direction-${action}.gif`),gif.bytes());
}
anims.down={frames:[0],fps:1,loop:false};
// Stable, native facing guides and an enlarged eight-view review from final idle0.
fs.mkdirSync(path.join(root,'rotations'),{recursive:true});
const facingReview=new PNG({width:768,height:384});
for(let d=0;d<8;d++){
 const frame=new PNG({width:W,height:H});PNG.bitblt(atlas,frame,0,d*H,W,H,0,0);
 fs.writeFileSync(path.join(root,'rotations',dirs[d]+'.png'),PNG.sync.write(frame));
 for(let y=0;y<H*2;y++)for(let x=0;x<W*2;x++){
  const p=(Math.floor(y/2)*W+Math.floor(x/2))*4,q=((Math.floor(d/4)*H*2+y)*768+d%4*W*2+x)*4;
  for(let c=0;c<3;c++)facingReview.data[q+c]=frame.data[p+3]?frame.data[p+c]:[35,32,40][c];facingReview.data[q+3]=255;
 }
}
fs.writeFileSync(path.join(root,'dagna-directions-review.png'),PNG.sync.write(facingReview));
const bytes=PNG.sync.write(atlas);fs.writeFileSync(path.join(root,'review_dagna.png'),bytes);
audit.atlas={width:atlas.width,height:atlas.height,uncompressedMiB:atlas.width*atlas.height*4/1048576,sha256:hash(bytes)};
fs.writeFileSync(path.join(root,'atlas-validation.json'),JSON.stringify(audit,null,2)+'\n');
fs.writeFileSync(path.join(root,'review-sprite.json'),JSON.stringify({file:'review_dagna.png',family:'townsfolk',variant:'dagna_review',frame:{width:W,height:H,anchor},headTop:12,pixelScale:.5,columns,rowsPerDirection:1,directions:dirs,footprintWidth:16,footprintHeight:36,keys:['characters.guard_captain','characters.dagna_holt'],anims,provenance:'REVIEW ONLY: image-generated Dagna Holt; appearance approved, full directional/motion/TV review pending.'},null,2)+'\n');
console.log(JSON.stringify({atlas:audit.atlas,footDrift:Object.fromEntries(dirs.map(d=>[d,Object.fromEntries(Object.entries(audit.directions[d].actions).map(([a,v])=>[a,v.maxFootDriftWorld]))])),productionInstalled:false}));
