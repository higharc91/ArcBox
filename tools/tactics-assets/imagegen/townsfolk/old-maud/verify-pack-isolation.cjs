// Prove that the review changes only named cosmetic references and art manifests.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..'),src=path.join(root,'content/tactics/base'),dst=path.resolve(process.argv[2]||'');assert(process.argv[2]&&dst!==src);
const json=(base,f)=>JSON.parse(fs.readFileSync(path.join(base,f))),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const exceptions=new Set(['characters/manifest.json','creatures/manifest.json','portraits/manifest.json','lore/characters.json','rules/world/ashen_ford.json']);
const inventory=[];function visit(dir,rel=''){for(const f of fs.readdirSync(dir,{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name))){const name=rel?rel+'/'+f.name:f.name,full=path.join(dir,f.name);if(f.isDirectory())visit(full,name);else{const bytes=fs.readFileSync(full);inventory.push({path:name,bytes:bytes.length,sha256:hash(bytes)});if(!exceptions.has(name))assert.equal(hash(fs.readFileSync(path.join(dst,name))),hash(bytes),name);}}}visit(src);inventory.sort((a,b)=>a.path<b.path?-1:a.path>b.path?1:0);
const digest=hash(JSON.stringify(inventory));assert.equal(digest,'728704ed27106c3fe7818874f4d3537882c8ba9bb9c1aead254cf0435a4f8147','Production content changed');
for(const rel of exceptions){const a=json(src,rel),b=json(dst,rel);
 if(rel==='characters/manifest.json')b.presets=b.presets.filter(p=>!['old_maud','hermit_old_maud'].includes(p));
 if(rel==='creatures/manifest.json')delete b.sprites.review_old_maud;
 if(rel==='portraits/manifest.json')delete b.presets.old_maud;
 if(rel==='lore/characters.json')b.characters.find(c=>c.id==='old_maud').preset=a.characters.find(c=>c.id==='old_maud').preset;
 if(rel==='rules/world/ashen_ford.json')delete b.people.find(c=>c.id==='old_maud').variant;
 assert.deepEqual(b,a,rel+' changed beyond named art');
}
const result={result:'PASS',productionFiles:inventory.length,productionBytes:inventory.reduce((n,f)=>n+f.bytes,0),productionInventorySha256:digest,gameplayChanges:0,phoneContractChanges:0,cosmeticDocuments:exceptions.size};
fs.writeFileSync(path.join(__dirname,'pack-isolation-validation.json'),JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result));
