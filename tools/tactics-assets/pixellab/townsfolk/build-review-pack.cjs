// NODE_PATH must supply pngjs. Builds a separate Windows review pack, never production.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { PNG } = require('pngjs');
const root = path.resolve(__dirname,'../../../..');
const source = path.join(root,'content/tactics/base');
const destination = path.resolve(process.argv[2] || path.join(require('node:os').homedir(),'.codex/tmp/bree-tv-review'));
assert(!destination.toLowerCase().startsWith(path.join(root,'content').toLowerCase()+path.sep),'Review pack must be outside production content');
assert(destination !== source);
const plan=JSON.parse(fs.readFileSync(path.join(__dirname,'directional/atlas-plan.json'),'utf8'));
const ownerFile=path.join(destination,'bree-review-owner.json');
if(fs.existsSync(destination) && fs.readdirSync(destination).length) {
  assert(fs.existsSync(ownerFile),'Refusing to reuse an unrelated directory');
  assert.equal(JSON.parse(fs.readFileSync(ownerFile,'utf8')).source,source,'Review pack belongs to another checkout');
}
fs.mkdirSync(destination,{recursive:true});
fs.writeFileSync(ownerFile,JSON.stringify({source,reviewOnly:true},null,2)+'\n');
function linkFile(from,to) {
  try { fs.linkSync(from,to); }
  catch(error) { if(['EXDEV','EPERM','ENOTSUP'].includes(error.code))fs.copyFileSync(from,to);else throw error; }
}
for(const entry of fs.readdirSync(source,{withFileTypes:true})) {
  const from=path.join(source,entry.name),to=path.join(destination,entry.name);
  if(entry.name==='creatures') {
    fs.mkdirSync(to,{recursive:true});
    for(const f of fs.readdirSync(from))if(f!=='manifest.json'&&!fs.existsSync(path.join(to,f)))linkFile(path.join(from,f),path.join(to,f));
  } else if(!fs.existsSync(to)) {
    if(entry.isDirectory())fs.symlinkSync(from,to,'junction');
    else linkFile(from,to);
  }
}
const inputs=JSON.parse(fs.readFileSync(path.join(__dirname,'pilot-inputs.json'),'utf8'));
const frame=plan.outputFrame ?? inputs.heroFrame;
const {width,height,anchor}=frame;
const frameCount=Object.values(plan.actions).reduce((sum,a)=>sum+a.frames,0),columns=Math.min(24,frameCount),rows=Math.ceil(frameCount/columns);
const atlas=new PNG({width:columns*width,height:rows*height*plan.directions.length});
const anims={},audit={reviewOnly:true,productionInstalled:false,sourceSelection:1,actions:{},sources:[]};
function image(relative){const bytes=fs.readFileSync(path.join(__dirname,relative));audit.sources.push({path:relative,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});return PNG.sync.read(bytes);}
function paste(frame,x,y) {
  for(let py=0;py<frame.height;py++)for(let px=0;px<frame.width;px++) {
    const p=(py*frame.width+px)*4;if(!frame.data[p+3])continue;
    assert(px+x>=0&&py+y>=0&&px+x<width&&py+y<height,'Clipped normalized frame');
  }
  return {frame,x,y};
}
let start=0;
for(const [action,settings]of Object.entries(plan.actions)) {
  anims[action]={frames:(settings.playback??Array.from({length:settings.frames},(_,i)=>i)).map(i=>{assert(i>=0&&i<settings.frames);return start+i;}),fps:settings.fps,loop:true};
  audit.actions[action]={};
  plan.directions.forEach((direction,directionIndex)=>{
    const spec=settings.directions[direction];assert(spec,`Missing ${action}/${direction}`);
    const files=spec.files;assert.equal(files.length,settings.frames,`Wrong frame count ${action}/${direction}`);
    const frames=files.map(image);
    const rotation=image(plan.rotations[direction]);
    let floor=-1;for(let y=0;y<rotation.height;y++)for(let x=0;x<rotation.width;x++)if(rotation.data[(y*rotation.width+x)*4+3])floor=y;
    assert(floor>=0,'Empty rotation');
    for(const[i,frame]of frames.entries()) {
      const x=spec.productionCanvas?anchor[0]-inputs.heroFrame.anchor[0]:Math.floor((width-rotation.width)/2)-Math.floor((frame.width-rotation.width)/2);
      const y=spec.productionCanvas?anchor[1]-inputs.heroFrame.anchor[1]:anchor[1]-floor-Math.floor((frame.height-rotation.height)/2);
      paste(frame,x,y);
      const dx=((start+i)%columns)*width+x,dy=(directionIndex*rows+Math.floor((start+i)/columns))*height+y;
      for(let py=0;py<frame.height;py++)for(let px=0;px<frame.width;px++) {
        const p=(py*frame.width+px)*4;if(!frame.data[p+3])continue;
        frame.data.copy(atlas.data,((dy+py)*atlas.width+dx+px)*4,p,p+4);
      }
    }
    audit.actions[action][direction]={frames:files.length,fixedPlacement:true,productionCanvas:spec.productionCanvas??false};
  });
  start+=settings.frames;
}
anims.down={frames:[0],fps:1,loop:false}; // review fallback only; this NPC has no authored down animation
fs.writeFileSync(path.join(destination,'creatures/review_bree.png'),PNG.sync.write(atlas));
const manifest=JSON.parse(fs.readFileSync(path.join(source,'creatures/manifest.json'),'utf8'));
manifest.sprites.review_bree={file:'review_bree.png',family:'townsfolk',variant:'bree_review',frame,headTop:anchor[1]-inputs.heroHeadToFootWorldPixels/inputs.heroPixelScale,pixelScale:inputs.heroPixelScale,columns,rowsPerDirection:rows,directions:plan.directions,footprintWidth:16,footprintHeight:36,keys:['characters.innkeeper'],anims,provenance:'REVIEW ONLY: eight real facings and motion; production and TV approval pending.'};
// The world demo's stand-in uses class_fighter; show the actual shipped hero for scale.
const tactical=JSON.parse(fs.readFileSync(path.join(source,'tactical/manifest.json'),'utf8'));
const heroFile=path.join(destination,'creatures/review_scale_hero.png');
if(!fs.existsSync(heroFile))linkFile(path.join(source,'tactical/human_fighter.png'),heroFile);
manifest.sprites.review_scale_hero={file:'review_scale_hero.png',family:'hero',variant:'scale_review',frame:tactical.frame,headTop:tactical.heads.human_fighter,pixelScale:tactical.pixelScale,columns:tactical.columns,rowsPerDirection:Math.ceil(tactical.framesPerDirection/tactical.columns),directions:plan.directions,footprintWidth:32,footprintHeight:36,keys:['characters.class_fighter'],anims:tactical.anims};
fs.writeFileSync(path.join(destination,'creatures/manifest.json'),JSON.stringify(manifest,null,2)+'\n');
fs.writeFileSync(path.join(destination,'bree-review-audit.json'),JSON.stringify(audit,null,2)+'\n');
fs.writeFileSync(path.join(__dirname,'directional/review-output.json'),JSON.stringify({atlas:[atlas.width,atlas.height],frame,uncompressedMiB:atlas.width*atlas.height*4/1024/1024,atlasSha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(destination,'creatures/review_bree.png'))).digest('hex')},null,2)+'\n');
console.log(JSON.stringify({destination,atlas:[atlas.width,atlas.height],actions:Object.keys(plan.actions),directions:plan.directions.length}));
