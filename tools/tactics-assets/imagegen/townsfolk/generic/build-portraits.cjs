const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {PNG}=require('pngjs'),{components,paletteImage,enlarge,hash}=require('./build.cjs');
const root=__dirname,moods=['idle','hurt','happy','angry','sad','surprised'];
for(const preset of process.argv.slice(2)){
 const bytes=fs.readFileSync(path.join(root,`${preset}-portraits-source.png`)),im=PNG.sync.read(bytes),cw=im.width/3,ch=im.height/4;
 const cs=components(im).filter(c=>c.pixels.length>500).sort((a,b)=>a.cy-b.cy);assert.equal(cs.length,12,`${preset}: expected twelve portrait busts`);
 const cells=[];for(let row=0;row<4;row++)cells.push(...cs.slice(row*3,row*3+3).sort((a,b)=>a.cx-b.cx));
 const owners=new Int16Array(im.width*im.height).fill(-1);cells.forEach((c,i)=>c.pixels.forEach(p=>owners[p]=i));
 for(let variant=0;variant<2;variant++){
  const key=`${preset}_${'ab'[variant]}`,set=cells.slice(variant*6,variant*6+6);
  const left=Math.min(...set.map((c,i)=>c.l-i%3*cw)),right=Math.max(...set.map((c,i)=>c.r-i%3*cw));
  const top=Math.min(...set.map((c,i)=>c.t-(variant*2+Math.floor(i/3))*ch)),floor=Math.max(...set.map((c,i)=>c.b-(variant*2+Math.floor(i/3))*ch));
  const scale=35/Math.max(right-left+1,floor-top+1),center=(left+right)/2;
  const offset=[20-center*scale,38-floor*scale],contact=new PNG({width:120,height:80}),audit={sourceSha256:hash(bytes),outputSize:40,paletteColors:64,uniformScale:scale,fixedOffset:offset,perMoodRecentering:false,moods:{}};
  for(let i=0;i<6;i++){
   for(let y=0;y<40;y++)for(let x=0;x<40;x++){
    const sx=Math.floor(i%3*cw+(x-offset[0])/scale),sy=Math.floor((variant*2+Math.floor(i/3))*ch+(y-offset[1])/scale);
    if(sx<0||sy<0||sx>=im.width||sy>=im.height||owners[sy*im.width+sx]!==variant*6+i)continue;
    const p=(sy*im.width+sx)*4,q=((Math.floor(i/3)*40+y)*contact.width+i%3*40+x)*4;
    im.data.copy(contact.data,q,p,p+3);contact.data[q+3]=255;
   }
  }
  paletteImage(contact,64);const folder=path.join(root,key,'portraits');fs.mkdirSync(folder,{recursive:true});
  for(let i=0;i<6;i++){
   const out=new PNG({width:40,height:40});PNG.bitblt(contact,out,i%3*40,Math.floor(i/3)*40,40,40,0,0);
   let visible=0;for(let y=0;y<40;y++)for(let x=0;x<40;x++){const a=out.data[(y*40+x)*4+3];if(a)visible++;if(x===0||x===39||y===0||y===39)assert.equal(a,0,`${key}/${moods[i]} clipped`);}
   assert(visible>300,`${key}/${moods[i]} sparse`);const outBytes=PNG.sync.write(out);fs.writeFileSync(path.join(folder,`${moods[i]}.png`),outBytes);audit.moods[moods[i]]={sha256:hash(outBytes),opaquePixels:visible};
  }
  fs.writeFileSync(path.join(root,key,'portraits-validation.json'),JSON.stringify(audit,null,2)+'\n');fs.writeFileSync(path.join(root,key,'portraits-review.png'),PNG.sync.write(enlarge(contact,4)));
  console.log(JSON.stringify({key,portraits:6,size:40}));
 }
}
