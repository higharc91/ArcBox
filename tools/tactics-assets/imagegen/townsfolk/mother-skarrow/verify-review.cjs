const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const {PNG}=require('pngjs');
const root=__dirname,sprite=JSON.parse(fs.readFileSync(path.join(root,'review-sprite.json'))),audit=JSON.parse(fs.readFileSync(path.join(root,'atlas-validation.json'))),bytes=fs.readFileSync(path.join(root,sprite.file)),atlas=PNG.sync.read(bytes);
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
assert.equal(hash(bytes),audit.atlas.sha256);assert.equal(hash(fs.readFileSync(path.join(root,'palette.json'))),audit.paletteSha256,'Shared palette changed');assert.equal(atlas.width,1728);assert.equal(atlas.height,1536);
assert.deepEqual(sprite.frame,{width:96,height:96,anchor:[48,84]});assert.equal(sprite.pixelScale,.5);assert.equal(sprite.directions.length,8);assert.equal(new Set(sprite.directions).size,8);
const colors=new Set();let transparent=0;
for(let p=0;p<atlas.data.length;p+=4){assert(atlas.data[p+3]===0||atlas.data[p+3]===255,'Non-binary runtime alpha');if(atlas.data[p+3])colors.add(atlas.data.subarray(p,p+3).toString('hex'));else transparent++;}
assert(transparent>atlas.width*atlas.height/2);assert(colors.size<=96);
fs.mkdirSync(path.join(root,'normalized'),{recursive:true});
const summary={result:'PASS',nativeFrame:[96,96],alpha:'binary',colors:colors.size,directions:8,nativeFrameSlots:288,perFrameRecentering:false,mirroring:false,actions:{},portraits:6,productionInstalled:false};
for(const [action,start] of [['idle',0],['walk',6],['talk',12],['attack',18],['cast',24],['hurt',30],['down',33]]){
 const count=['hurt','down'].includes(action)?3:6;const expected=['idle','talk'].includes(action)?10:count;assert.equal(sprite.anims[action].frames.length,expected);assert.equal(new Set(sprite.anims[action].frames).size,count);
 summary.actions[action]=[];
 for(let dir=0;dir<8;dir++){
  const unique=new Set(),measurements=[];
  const dest=path.join(root,'normalized',sprite.directions[dir],action);fs.mkdirSync(dest,{recursive:true});
  for(let f=0;f<count;f++){
   const img=new PNG({width:96,height:96});PNG.bitblt(atlas,img,((start+f)%18)*96,(dir*2+Math.floor((start+f)/18))*96,96,96,0,0);
   const data=PNG.sync.write(img);unique.add(hash(data));fs.writeFileSync(path.join(dest,`frame-${f}.png`),data);
   let left=96,top=96,right=-1,bottom=-1;
   for(let y=0;y<96;y++)for(let x=0;x<96;x++)if(img.data[(y*96+x)*4+3]){left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);}
   assert(left>0&&right<95&&top>0&&bottom<95,`Clipped/empty ${action}/${dir}/${f}`);
   const height=(bottom-top+1)*.5,foot=Math.abs(bottom-84)*.5;
   assert(height>=(action==='down'?6:22)&&height<=(action==='down'?25:28));assert(foot<=(['walk','attack','down'].includes(action)?5:3));
   let feetLeft=96,feetRight=-1;
   for(let y=Math.max(top,bottom-5);y<=bottom;y++)for(let x=0;x<96;x++)if(img.data[(y*96+x)*4+3]){feetLeft=Math.min(feetLeft,x);feetRight=Math.max(feetRight,x);}
   const horizontalFootDriftWorld=Math.abs((feetLeft+feetRight)/2-48)*.5;
   assert(horizontalFootDriftWorld<=(['idle','talk','walk'].includes(action)?5:25),`Horizontal foot drift ${action}/${sprite.directions[dir]}/${f}: ${horizontalFootDriftWorld}`);
   measurements.push({frame:f,heightWorld:height,footDriftWorld:foot,horizontalFootDriftWorld,sha256:hash(data)});
  }
  assert(unique.size>=(action==='idle'?2:action==='walk'?4:3),`Insufficient distinct motion: ${action}/${sprite.directions[dir]}`);
  summary.actions[action].push({direction:sprite.directions[dir],uniqueFrames:unique.size,maxFootDriftWorld:Math.max(...measurements.map(m=>m.footDriftWorld)),maxHorizontalFootDriftWorld:Math.max(...measurements.map(m=>m.horizontalFootDriftWorld)),frames:measurements});
 }
}
const portraitHashes=new Set();for(const mood of ['idle','hurt','happy','angry','sad','surprised']){
 const b=fs.readFileSync(path.join(root,'portraits',`mother_skarrow_${mood}.png`)),img=PNG.sync.read(b);assert.equal(img.width,40);assert.equal(img.height,40);portraitHashes.add(hash(b));for(let p=3;p<img.data.length;p+=4)assert(img.data[p]===0||img.data[p]===255);
}assert.equal(portraitHashes.size,6);
for(const source of audit.sources)assert.equal(hash(fs.readFileSync(path.join(root,source.path))),source.sha256,'Source changed');
fs.writeFileSync(path.join(root,'review-validation.json'),JSON.stringify(summary,null,2)+'\n');
console.log(JSON.stringify({result:summary.result,frames:288,directions:8,colors:colors.size,portraits:6,maximumFootDriftWorld:Math.max(...Object.values(summary.actions).flat().map(m=>m.maxFootDriftWorld))}));
