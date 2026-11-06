// Offline import of the approved standing sheet, not new art generation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {PNG}=require('pngjs');
const root=__dirname,src=PNG.sync.read(fs.readFileSync(path.join(root,'candidate-1-preview.png')));
const approval=JSON.parse(fs.readFileSync(path.join(root,'sprite-approval.json')));
assert(approval.ownerAppearanceApproval?.quote,'Standing appearance must be approved');
const dirs=['south','south-west','west','north-west','north','north-east','east','south-east'];
for(let p=0;p<src.data.length;p+=4)if(src.data[p]>src.data[p+1]+35&&src.data[p+2]>src.data[p+1]+35&&src.data[p]+src.data[p+2]>150)src.data.fill(0,p,p+4);
function footCentre(img,ox,oy,cw,ch,bottom,top){
 const active=[];for(let x=0;x<cw;x++){let hit=false;for(let y=Math.floor(bottom-(bottom-top)*.06);y<=bottom;y++)if(img.data[((oy+y)*img.width+ox+x)*4+3]>=128){hit=true;break;}active.push(hit);}
 const runs=[];for(let x=0;x<cw;){if(!active[x]){x++;continue;}const l=x;while(x<cw&&active[x])x++;runs.push([l,x-1]);}
 const widest=Math.max(...runs.map(([l,r])=>r-l+1)),boots=runs.filter(([l,r])=>r-l+1>=widest*.6);return(Math.min(...boots.map(r=>r[0]))+Math.max(...boots.map(r=>r[1])))/2;
}
const cw=src.width/4,ch=src.height/2;
fs.mkdirSync(path.join(root,'directional/guides'),{recursive:true});
for(let i=0;i<8;i++){
 const col=i%4,row=Math.floor(i/4),out=new PNG({width:96,height:96});
 let left=Infinity,top=Infinity,right=-1,bottom=-1;
 for(let y=Math.ceil(row*ch);y<Math.floor((row+1)*ch);y++)for(let x=Math.ceil(col*cw);x<Math.floor((col+1)*cw);x++)if(src.data[(y*src.width+x)*4+3]>=128){left=Math.min(left,x-col*cw);right=Math.max(right,x-col*cw);top=Math.min(top,y-row*ch);bottom=Math.max(bottom,y-row*ch);}
 assert(right>=left);const scale=50/(bottom-top+1),ox=48-footCentre(src,col*cw,row*ch,cw,ch,bottom,top)*scale,oy=84-bottom*scale;
 for(let y=0;y<96;y++)for(let x=0;x<96;x++){
  const lx=(x-ox)/scale,ly=(y-oy)/scale;
  if(lx<0||lx>=cw||ly<0||ly>=ch)continue;
  const p=(Math.floor(row*ch+ly)*src.width+Math.floor(col*cw+lx))*4,q=(y*96+x)*4;
  if(src.data[p+3]<128)continue;src.data.copy(out.data,q,p,p+3);out.data[q+3]=255;
 }
 fs.writeFileSync(path.join(root,'directional/guides',dirs[i]+'.png'),PNG.sync.write(out));
}
console.log('Imported eight approved Mother Skarrow guides; no mirroring.');
