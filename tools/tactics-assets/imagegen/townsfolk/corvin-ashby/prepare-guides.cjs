// Offline import of the approved standing sheet, not new art generation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {PNG}=require('pngjs');
const root=__dirname,src=PNG.sync.read(fs.readFileSync(path.join(root,'candidate-1.png')));
const approval=JSON.parse(fs.readFileSync(path.join(root,'sprite-approval.json')));
assert(approval.ownerAppearanceApproval?.quote,'Standing appearance must be approved');
const dirs=['south','south-west','west','north-west','north','north-east','east','south-east'];
const cw=src.width/4,ch=src.height/2;
fs.mkdirSync(path.join(root,'directional/guides'),{recursive:true});
for(let i=0;i<8;i++){
 const col=i%4,row=Math.floor(i/4),out=new PNG({width:96,height:96});
 let left=Infinity,top=Infinity,right=-1,bottom=-1;
 for(let y=Math.ceil(row*ch);y<Math.floor((row+1)*ch);y++)for(let x=Math.ceil(col*cw);x<Math.floor((col+1)*cw);x++)if(src.data[(y*src.width+x)*4+3]>=128){left=Math.min(left,x-col*cw);right=Math.max(right,x-col*cw);top=Math.min(top,y-row*ch);bottom=Math.max(bottom,y-row*ch);}
 assert(right>=left);const scale=72/(bottom-top+1),ox=48-(left+right)/2*scale,oy=84-bottom*scale;
 for(let y=0;y<96;y++)for(let x=0;x<96;x++){
  const lx=(x-ox)/scale,ly=(y-oy)/scale;
  if(lx<0||lx>=cw||ly<0||ly>=ch)continue;
  const p=(Math.floor(row*ch+ly)*src.width+Math.floor(col*cw+lx))*4,q=(y*96+x)*4;
  if(src.data[p+3]<128)continue;src.data.copy(out.data,q,p,p+3);out.data[q+3]=255;
 }
 fs.writeFileSync(path.join(root,'directional/guides',dirs[i]+'.png'),PNG.sync.write(out));
}
console.log('Imported all eight approved appearance guides; no mirroring.');
