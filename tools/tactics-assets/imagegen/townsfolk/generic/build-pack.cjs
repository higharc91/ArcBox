// Build a separate review pack. Never mutate hard-linked production content.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {hash}=require('./build.cjs');
const root=path.resolve(__dirname,'../../../../..'),source=path.join(root,'content/tactics/base'),destination=path.resolve(process.argv[2]||'/workspace/work/issue80-generic-review');
assert(destination!==source&&!destination.startsWith(source+path.sep));
const owner=path.join(destination,'generic-review-owner.json');
if(fs.existsSync(destination)&&fs.readdirSync(destination).length)assert.equal(JSON.parse(fs.readFileSync(owner)).source,source,'Unrelated output directory');
fs.mkdirSync(destination,{recursive:true});
function link(from,to){fs.mkdirSync(path.dirname(to),{recursive:true});if(!fs.existsSync(to)){try{fs.linkSync(from,to);}catch(e){if(['EXDEV','EPERM','ENOTSUP'].includes(e.code))fs.copyFileSync(from,to);else throw e;}}}
function walk(dir,rel=''){for(const f of fs.readdirSync(dir,{withFileTypes:true})){const name=path.join(rel,f.name),from=path.join(dir,f.name);if(f.isDirectory())walk(from,name);else link(from,path.join(destination,name));}}
walk(source);
function write(file,bytes){const target=path.join(destination,file);fs.mkdirSync(path.dirname(target),{recursive:true});if(fs.existsSync(target))fs.unlinkSync(target);fs.writeFileSync(target,bytes);}
function json(file,obj){write(file,JSON.stringify(obj,null,2)+'\n');}
const creatures=JSON.parse(fs.readFileSync(path.join(source,'creatures/manifest.json'))),portraits=JSON.parse(fs.readFileSync(path.join(source,'portraits/manifest.json'))),world=JSON.parse(fs.readFileSync(path.join(source,'rules/world/ashen_ford.json')));
const presets=['barmaid','child','farmer','farmhand','fisher','guard','merchant'],moods=['idle','hurt','happy','angry','sad','surprised'];
const assetHashes={},placements=[];
for(const preset of presets)for(const variant of ['a','b']){
 const key=`${preset}_${variant}`,folder=path.join(__dirname,key),spec=JSON.parse(fs.readFileSync(path.join(folder,'sprite.json'))),audit=JSON.parse(fs.readFileSync(path.join(folder,'validation.json'))),atlas=fs.readFileSync(path.join(folder,'atlas.png'));
 assert.equal(hash(atlas),audit.atlasSha256);assert.equal(hash(fs.readFileSync(path.join(folder,'sprite-source.png'))),audit.sourceSha256);
 creatures.sprites[`generic_${key}`]=spec;write(`creatures/${spec.file}`,atlas);assetHashes[`creatures/${spec.file}`]=hash(atlas);
 const faces={},portraitAudit=JSON.parse(fs.readFileSync(path.join(folder,'portraits-validation.json')));
 for(const mood of moods){const bytes=fs.readFileSync(path.join(folder,'portraits',`${mood}.png`)),file=`generic_${key}_${mood}.png`;assert.equal(hash(bytes),portraitAudit.moods[mood].sha256);write(`portraits/${file}`,bytes);faces[mood]=file;assetHashes[`portraits/${file}`]=hash(bytes);}
 portraits.presets[key]=faces;
 if(variant==='a')portraits.presets[preset]=faces;
}
for(const preset of presets){
 const people=world.people.filter(p=>p.preset===preset&&!p.character).sort((a,b)=>a.id.localeCompare(b.id));
 assert(people.length>=2,`${preset} needs two placed variants`);
 people.forEach((p,i)=>{p.variant='ab'[i%2];placements.push({id:p.id,preset,variant:p.variant});});
}
json('creatures/manifest.json',creatures);json('portraits/manifest.json',portraits);json('rules/world/ashen_ford.json',world);
const record={source,reviewOnly:true,productionInstalled:false,baseCommit:'facbaceb',variants:14,sourceFrames:672,portraits:84,cosmeticDocuments:['creatures/manifest.json','portraits/manifest.json','rules/world/ashen_ford.json'],placements,assetHashes};
json('generic-review-owner.json',record);fs.writeFileSync(path.join(__dirname,'review-pack.json'),JSON.stringify(record,null,2)+'\n');
console.log(JSON.stringify({destination,variants:14,portraits:84,placed:placements.length,reviewOnly:true}));
