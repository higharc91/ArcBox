// Read-only comparison to the approved-review baseline, including every retained asset.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict'),{execFileSync}=require('node:child_process');
const root=path.resolve(__dirname,'../../../../..'),prefix='content/tactics/base/',base=path.join(root,prefix),baseline='9a208878';
const git=args=>execFileSync('git',args,{cwd:root,maxBuffer:8*1024*1024}),json=rel=>JSON.parse(fs.readFileSync(path.join(base,rel))),old=rel=>JSON.parse(git(['show',baseline+':'+prefix+rel])),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const cosmetics=['characters/manifest.json','creatures/manifest.json','portraits/manifest.json','lore/characters.json','rules/world/ashen_ford.json','rules/monsters/villains.json'],moods=['idle','hurt','happy','angry','sad','surprised'],newFiles=['creatures/mother_skarrow.png',...moods.map(m=>'portraits/mother_skarrow_'+m+'.png')];
const entries=new Map(git(['ls-tree','-r',baseline,'--',prefix]).toString().trim().split('\n').map(line=>{const [meta,file]=line.split('\t');return[file.slice(prefix.length),meta.split(' ')[2]];}));
let verified=0;const inventory=[],oldInventory=[];
function walk(dir,rel=''){for(const f of fs.readdirSync(dir,{withFileTypes:true})){const name=rel?rel+'/'+f.name:f.name,full=path.join(dir,f.name);if(f.isDirectory()){walk(full,name);continue;}const bytes=fs.readFileSync(full),sha256=hash(bytes);inventory.push({path:name,bytes:bytes.length,sha256});
 if(!entries.has(name)){assert(newFiles.includes(name),'Unexpected production addition '+name);continue;}
 const original=cosmetics.includes(name)?git(['show',baseline+':'+prefix+name]):bytes;
 if(!cosmetics.includes(name)){const sha1=crypto.createHash('sha1').update(Buffer.from('blob '+bytes.length+'\0')).update(bytes).digest('hex');assert.equal(sha1,entries.get(name),'Existing production asset changed '+name);verified++;}
 oldInventory.push({path:name,bytes:original.length,sha256:hash(original)});
}}walk(base);
for(const name of entries.keys())assert(fs.existsSync(path.join(base,name)),'Deleted production file '+name);
const order=(a,b)=>a.path<b.path?-1:a.path>b.path?1:0;inventory.sort(order);oldInventory.sort(order);assert.equal(hash(JSON.stringify(oldInventory)),'1373f33f5c9b56f62bd26015f3a87fe6bc23c8793fc587e5246d7d0f8fb99a3d');
const a=JSON.parse(fs.readFileSync(path.join(__dirname,'sprite-approval.json')));assert.equal(a.completedSet.status,'approved');assert(a.ownerFullSetApproval.quote);assert.equal(a.ownerFullSetApproval.quote,'looks good, I approvee');
assert.equal(hash(fs.readFileSync(path.join(base,newFiles[0]))),a.completedSet.sha256['review_mother_skarrow.png']);
for(const m of moods)assert.equal(hash(fs.readFileSync(path.join(base,`portraits/mother_skarrow_${m}.png`))),a.completedSet.sha256[`portraits/mother_skarrow_${m}.png`]);
for(const rel of cosmetics){const before=old(rel),after=json(rel);
 if(rel==='characters/manifest.json')after.presets=after.presets.filter(p=>!['mother_skarrow','goblin_shaman_mother_skarrow'].includes(p));
 if(rel==='creatures/manifest.json')delete after.sprites.mother_skarrow;
 if(rel==='portraits/manifest.json')delete after.presets.mother_skarrow;
 if(rel==='lore/characters.json')after.characters.find(p=>p.id==='mother_skarrow').preset=before.characters.find(p=>p.id==='mother_skarrow').preset;
 if(rel==='rules/world/ashen_ford.json')delete after.people.find(p=>p.id==='mother_skarrow').variant;
 if(rel==='rules/monsters/villains.json')after.find(p=>p.id==='mother_skarrow').sprite=before.find(p=>p.id==='mother_skarrow').sprite;
 assert.deepEqual(after,before,'Changed beyond approved named cosmetics '+rel);
}
const result={result:'PASS',baseline,approvedAssetHashes:'PASS',unchangedExistingAssets:verified,cosmeticDocuments:cosmetics.length,addedArtFiles:newFiles.length,gameplayChanges:0,phoneContractChanges:0,productionFiles:inventory.length,productionBytes:inventory.reduce((sum,f)=>sum+f.bytes,0),productionInventorySha256:hash(JSON.stringify(inventory))};
fs.writeFileSync(path.join(__dirname,'installation-validation.json'),JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result));
