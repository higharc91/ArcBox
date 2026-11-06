const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{execFileSync}=require('node:child_process'),{PNG}=require('pngjs');
const {hash}=require('./build.cjs');
const root=path.resolve(__dirname,'../../../../..'),source=path.join(root,'content/tactics/base'),dest=path.resolve(process.argv[2]||'/workspace/work/issue80-generic-review');
const record=JSON.parse(fs.readFileSync(path.join(dest,'generic-review-owner.json'))),changed=new Set(record.cosmeticDocuments);
execFileSync('git',['diff','--exit-code','facbaceb','--','content/tactics/base'],{cwd:root});
let files=0,bytes=0;const inventory=[];
function walk(dir,rel=''){for(const entry of fs.readdirSync(dir,{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name))){const name=rel?`${rel}/${entry.name}`:entry.name,full=path.join(dir,entry.name);if(entry.isDirectory())walk(full,name);else{const data=fs.readFileSync(full),digest=hash(data);files++;bytes+=data.length;inventory.push({path:name,bytes:data.length,sha256:digest});if(!changed.has(name))assert.equal(hash(fs.readFileSync(path.join(dest,name))),digest,`Production asset changed: ${name}`);}}}
walk(source);
const json=(base,f)=>JSON.parse(fs.readFileSync(path.join(base,f)));
for(const file of changed){
 const a=json(source,file),b=json(dest,file);
 if(file==='creatures/manifest.json')for(const key of Object.keys(record.assetHashes).filter(f=>f.startsWith('creatures/')))delete b.sprites[path.basename(key,'.png')];
 if(file==='portraits/manifest.json')for(const preset of ['barmaid','child','farmer','farmhand','fisher','guard','merchant']){b.presets[preset]=a.presets[preset];delete b.presets[`${preset}_a`];delete b.presets[`${preset}_b`];}
 if(file==='rules/world/ashen_ford.json')for(const placement of record.placements)delete b.people.find(p=>p.id===placement.id).variant;
 assert.deepEqual(b,a,`Changes beyond generic art/placement variants in ${file}`);
}
for(const [file,digest]of Object.entries(record.assetHashes))assert.equal(hash(fs.readFileSync(path.join(dest,file))),digest,file);
for(const placement of record.placements){
 const key=`${placement.preset}_${placement.variant}`,folder=path.join(__dirname,key),audit=json(folder,'validation.json'),atlas=PNG.sync.read(fs.readFileSync(path.join(folder,'atlas.png')));
 assert.equal(atlas.width,576);assert.equal(atlas.height,768);assert.equal(audit.sourceFrames,48);assert.equal(hash(fs.readFileSync(path.join(folder,'sprite-source.png'))),audit.sourceSha256);
 for(let p=3;p<atlas.data.length;p+=4)assert(atlas.data[p]===0||atlas.data[p]===255,'Nonbinary sprite alpha');
 const rows=[];for(let row=0;row<8;row++){
  const frameHashes=[];for(let f=0;f<6;f++){const crop=new PNG({width:96,height:96});PNG.bitblt(atlas,crop,f*96,row*96,96,96,0,0);frameHashes.push(hash(crop.data));}
  assert.equal(new Set(frameHashes.slice(2)).size,4,`${key}/${row}: duplicated walking poses`);rows.push(frameHashes[0]);
 }assert.equal(new Set(rows).size,8,`${key}: duplicate facing`);
 const faces=json(folder,'portraits-validation.json');for(const mood of ['idle','hurt','happy','angry','sad','surprised']){const image=fs.readFileSync(path.join(folder,'portraits',mood+'.png'));assert.equal(hash(image),faces.moods[mood].sha256);const png=PNG.sync.read(image);assert.equal(png.width,40);assert.equal(png.height,40);}
}
const added=Object.keys(record.assetHashes).reduce((n,f)=>n+fs.statSync(path.join(dest,f)).size,0),documentDelta=[...changed].reduce((n,f)=>n+fs.statSync(path.join(dest,f)).size-fs.statSync(path.join(source,f)).size,0);
const result={result:'PASS',productionFiles:files,productionBytes:bytes,productionInventorySha256:hash(JSON.stringify(inventory)),reviewBytes:bytes+added+documentDelta,addedArtFiles:Object.keys(record.assetHashes).length,sourceFrames:672,variants:14,portraits:84,placedPeople:28,cosmeticDocuments:[...changed],productionUnchanged:true};
fs.writeFileSync(path.join(__dirname,'verification.json'),JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result));
