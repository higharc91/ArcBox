// Offline import of generated source pixels: no mirrored or synthesized poses.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const {PNG}=require('pngjs'),{GIFEncoder,quantize,applyPalette}=require('gifenc');
const root=__dirname,dirs=['south','south-west','west','north-west','north','north-east','east','south-east'];
const presets=['barmaid','child','farmer','farmhand','fisher','guard','merchant'];
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const median=a=>{const b=[...a].sort((x,y)=>x-y);return(b[Math.floor((b.length-1)/2)]+b[Math.ceil((b.length-1)/2)])/2;};
function components(im){
 const seen=new Uint8Array(im.width*im.height),cs=[];
 for(let at=0;at<seen.length;at++){
  if(seen[at]||im.data[at*4+3]<128)continue;
  const pixels=[at];seen[at]=1;let l=im.width,t=im.height,r=-1,b=-1;
  for(let k=0;k<pixels.length;k++){
   const n=pixels[k],x=n%im.width,y=Math.floor(n/im.width);l=Math.min(l,x);r=Math.max(r,x);t=Math.min(t,y);b=Math.max(b,y);
   for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++){
    const nx=x+dx,ny=y+dy,v=ny*im.width+nx;
    if(nx<0||ny<0||nx>=im.width||ny>=im.height||seen[v]||im.data[v*4+3]<128)continue;
    seen[v]=1;pixels.push(v);
   }
  }
  cs.push({pixels,l,t,r,b,cx:(l+r)/2,cy:(t+b)/2});
 }
 return cs;
}
function paletteImage(im,count){
 const rgba=[];for(let p=0;p<im.data.length;p+=4)if(im.data[p+3])rgba.push(im.data[p],im.data[p+1],im.data[p+2],255);
 const palette=quantize(Uint8Array.from(rgba),count),indices=applyPalette(im.data,palette);
 for(let i=0,p=0;p<im.data.length;p+=4,i++)if(im.data[p+3])for(let c=0;c<3;c++)im.data[p+c]=palette[indices[i]][c];
 return palette;
}
function enlarge(im,scale){
 const out=new PNG({width:im.width*scale,height:im.height*scale});
 for(let y=0;y<out.height;y++)for(let x=0;x<out.width;x++){
  const p=(Math.floor(y/scale)*im.width+Math.floor(x/scale))*4,q=(y*out.width+x)*4;
  for(let c=0;c<3;c++)out.data[q+c]=im.data[p+3]?im.data[p+c]:[35,32,40][c];out.data[q+3]=255;
 }return out;
}
function sprite(key){
 const folder=path.join(root,key),bytes=fs.readFileSync(path.join(folder,'sprite-source.png')),im=PNG.sync.read(bytes);
 const all=components(im),figures=all.filter(c=>c.pixels.length>500);
 assert.equal(figures.length,48,`${key}: expected 48 complete source figures`);
 figures.sort((a,b)=>a.cy-b.cy);
 const cells=[];for(let row=0;row<8;row++)cells.push(figures.slice(row*6,row*6+6).sort((a,b)=>a.cx-b.cx));
 const owners=new Int16Array(im.width*im.height).fill(-1);
 for(let row=0;row<8;row++)for(let col=0;col<6;col++)for(const p of cells[row][col].pixels)owners[p]=row*6+col;
 // Tiny disconnected alpha debris is discarded; retain the connected full body.
 const height=key.startsWith('child_')?52:72,scale=height/median(cells.flatMap(row=>row.slice(0,2).map(c=>c.b-c.t+1)));
 const atlas=new PNG({width:576,height:768});
 const audit={sourceSha256:hash(bytes),sourceSize:[im.width,im.height],sourceFrames:48,frame:[96,96],anchor:[48,84],pixelScale:.5,bodyHeightWorld:height/2,uniformScale:scale,mirroring:false,perFrameRecentering:false,discardedDisconnectedPixels:all.filter(c=>c.pixels.length<=500).reduce((n,c)=>n+c.pixels.length,0),directions:{}};
 for(let row=0;row<8;row++){
  const cs=cells[row],mean=cs.reduce((n,c)=>n+c.cx,0)/6,pitch=cs.reduce((n,c,i)=>n+(i-2.5)*(c.cx-mean),0)/17.5,first=mean-2.5*pitch;
  const floor=median(cs.slice(0,2).map(c=>c.b)),frames=[];
  for(let col=0;col<6;col++){
   const center=first+pitch*col;let l=96,t=96,r=-1,b=-1,opaque=0;
   for(let y=0;y<96;y++)for(let x=0;x<96;x++){
    const sx=Math.floor(center+(x-48)/scale),sy=Math.floor(floor+(y-84)/scale);
    if(sx<0||sy<0||sx>=im.width||sy>=im.height||owners[sy*im.width+sx]!==row*6+col)continue;
    const p=(sy*im.width+sx)*4,q=((row*96+y)*atlas.width+col*96+x)*4;
    im.data.copy(atlas.data,q,p,p+3);atlas.data[q+3]=255;l=Math.min(l,x);t=Math.min(t,y);r=Math.max(r,x);b=Math.max(b,y);opaque++;
   }
   assert(opaque>300,`${key}/${row}/${col}: incomplete frame`);
   assert(l>0&&t>0&&r<95&&b<95,`${key}/${row}/${col}: clipped frame`);
   const drift=(b-84)*.5;assert(Math.abs(drift)<=3,`${key}/${row}/${col}: foot drift ${drift}`);
   frames.push({sourceBounds:[cs[col].l,cs[col].t,cs[col].r,cs[col].b],outputBounds:[l,t,r,b],opaquePixels:opaque,footDeltaWorld:drift});
  }
  audit.directions[dirs[row]]={gridPitch:pitch,gridFirstCenter:first,fixedFloor:floor,frames};
 }
 paletteImage(atlas,96);
 const output=PNG.sync.write(atlas);audit.atlasSha256=hash(output);audit.textureMiB=atlas.width*atlas.height*4/1048576;
 fs.writeFileSync(path.join(folder,'atlas.png'),output);
 const spec={file:`generic_${key}.png`,family:'townsfolk',variant:key,frame:{width:96,height:96,anchor:[48,84]},headTop:84-height,pixelScale:.5,columns:6,rowsPerDirection:1,directions:dirs,footprintWidth:16,footprintHeight:height/2,keys:[`characters.${key}`],anims:{idle:{frames:[0,0,0,0,0,0,0,1],fps:6,loop:true},walk:{frames:[2,3,4,5],fps:6,loop:true},down:{frames:[0],fps:1,loop:false}},provenance:'Generic townsfolk review: eight real facings, generated idle/walk source pixels; owner finished-set/TV approval pending.'};
 fs.writeFileSync(path.join(folder,'sprite.json'),JSON.stringify(spec,null,2)+'\n');
 fs.writeFileSync(path.join(folder,'validation.json'),JSON.stringify(audit,null,2)+'\n');
 fs.writeFileSync(path.join(folder,'directions-review.png'),PNG.sync.write(enlarge(atlas,2)));
 for(const action of ['idle','walk']){
  const indices=spec.anims[action].frames,enc=GIFEncoder(),w=384,h=192;
  for(const frame of indices){
   const contact=new PNG({width:w,height:h});
   for(let row=0;row<8;row++)PNG.bitblt(atlas,contact,frame*96,row*96,96,96,row%4*96,Math.floor(row/4)*96);
   const image=enlarge(contact,2),palette=quantize(image.data,128),indexed=applyPalette(image.data,palette);
   enc.writeFrame(indexed,image.width,image.height,{palette,delay:1000/spec.anims[action].fps});
  }enc.finish();fs.writeFileSync(path.join(folder,`${action}-review.gif`),Buffer.from(enc.bytes()));
 }
 console.log(JSON.stringify({key,frames:48,textureMiB:audit.textureMiB,maxFootDriftWorld:Math.max(...Object.values(audit.directions).flatMap(d=>d.frames.map(f=>Math.abs(f.footDeltaWorld))))}));
}
module.exports={components,paletteImage,enlarge,hash};
if(require.main===module)for(const key of process.argv.slice(2).length?process.argv.slice(2):presets.flatMap(p=>['a','b'].map(v=>`${p}_${v}`)))sprite(key);
