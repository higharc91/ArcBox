// Private presentation-only aliases: no shared shaman or Stormcaller replacements.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..'),source=path.join(root,'content/tactics/base'),dest=path.resolve(process.argv[2]||'');
assert(process.argv[2]&&!dest.startsWith(path.join(root,'content')+path.sep));
assert.equal(JSON.parse(fs.readFileSync(path.join(dest,'mother_skarrow-review-owner.json'))).source,source);
function privateFile(rel,edit){
 const parts=rel.split('/');let current=dest;
 for(const part of parts.slice(0,-1)){
  current=path.join(current,part);if(fs.existsSync(current)&&fs.lstatSync(current).isSymbolicLink()){const from=fs.realpathSync(current);fs.unlinkSync(current);fs.mkdirSync(current);for(const name of fs.readdirSync(from))fs.symlinkSync(path.join(from,name),path.join(current,name));}
  else fs.mkdirSync(current,{recursive:true});
 }
 const to=path.join(dest,rel);if(fs.existsSync(to))fs.unlinkSync(to);
 const doc=JSON.parse(fs.readFileSync(path.join(source,rel)));edit(doc);fs.writeFileSync(to,JSON.stringify(doc,null,2)+'\n');
}
privateFile('characters/manifest.json',doc=>{doc.presets.push('mother_skarrow','goblin_shaman_mother_skarrow');});
privateFile('rules/world/ashen_ford.json',doc=>{const p=doc.people.find(p=>p.id==='mother_skarrow');assert(p);p.variant='mother_skarrow';});
privateFile('rules/monsters/villains.json',doc=>{const m=doc.find(m=>m.id==='mother_skarrow');assert(m);m.sprite='characters.mother_skarrow';});
// Story speaker uses a named cosmetic preset; generic shaman and Stormcaller stay unchanged.
privateFile('lore/characters.json',doc=>{const list=Array.isArray(doc)?doc:doc.characters;assert(Array.isArray(list));const c=list.find(c=>c.id==='mother_skarrow');assert(c);c.preset='mother_skarrow';});
console.log('Private named-only Mother Skarrow body/portrait aliases prepared; gameplay data unchanged.');
