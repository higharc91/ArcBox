// Phone-friendly enlargement of the actual atlas frames; no new animation.
const fs=require('node:fs'),path=require('node:path');
const {PNG}=require('pngjs'),{GIFEncoder,quantize,applyPalette}=require('gifenc');
const root=__dirname,atlas=PNG.sync.read(fs.readFileSync(path.join(root,'review_old_maud.png'))),sprite=JSON.parse(fs.readFileSync(path.join(root,'review-sprite.json')));
const row=7,scale=4,size=96*scale,gif=GIFEncoder();
const palette=[...JSON.parse(fs.readFileSync(path.join(root,'palette.json'))),[35,32,40]],indices=new Map(palette.map((c,i)=>[(c[0]<<16)|(c[1]<<8)|c[2],i]));
for(const action of ['idle','walk','walk','talk'])for(const frame of sprite.anims[action].frames){
 const out=new Uint8Array(size*size*4);
 for(let y=0;y<size;y++)for(let x=0;x<size;x++){
  const p=((row*96+Math.floor(y/scale))*atlas.width+frame*96+Math.floor(x/scale))*4,q=(y*size+x)*4;
  for(let c=0;c<3;c++)out[q+c]=atlas.data[p+3]?atlas.data[p+c]:[35,32,40][c];out[q+3]=255;
 }
 const indexed=new Uint8Array(size*size);for(let i=0,p=0;p<out.length;p+=4,i++)indexed[i]=indices.get((out[p]<<16)|(out[p+1]<<8)|out[p+2]);gif.writeFrame(indexed,size,size,{palette,delay:1000/sprite.anims[action].fps,repeat:0});
}
gif.finish();fs.writeFileSync(path.join(root,'old_maud-closeup-review.gif'),gif.bytes());
console.log(JSON.stringify({facing:'south-east',sequence:['idle','walk','walk','talk'],nearestNeighbourScale:4,seconds:5.4}));
