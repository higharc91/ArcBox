// An explicit external review-only presence fixture; never edit production or linked rules.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..'),source=path.join(root,'content/tactics/base'),destination=path.resolve(process.argv[2]||'');
assert(process.argv[2]&&destination!==source&&!destination.startsWith(path.join(root,'content')+path.sep),'Use an explicit external review-pack directory');
const marker=JSON.parse(fs.readFileSync(path.join(destination,'rook-review-owner.json')));assert.equal(marker.source,source);
const rules=path.join(destination,'rules');
if(fs.lstatSync(rules).isSymbolicLink()){fs.unlinkSync(rules);fs.mkdirSync(rules);}
for(const name of fs.readdirSync(path.join(source,'rules'))){const from=path.join(source,'rules',name),to=path.join(rules,name);if(name!=='world'&&!fs.existsSync(to))fs.symlinkSync(from,to,fs.statSync(from).isDirectory()?'junction':'file');}
const world=path.join(rules,'world');fs.mkdirSync(world,{recursive:true});
for(const name of fs.readdirSync(path.join(source,'rules/world'))){const from=path.join(source,'rules/world',name),to=path.join(world,name);if(name==='ashen_ford.json'){const doc=JSON.parse(fs.readFileSync(from));const person=doc.people.find(p=>p.id==='rook');assert(person);doc.people.push({...person,id:'rook_art_review',requires:[],unless:[]});assert(!fs.existsSync(to)||!fs.lstatSync(to).isSymbolicLink());fs.writeFileSync(to,JSON.stringify(doc,null,2)+'\n');}else if(!fs.existsSync(to))fs.symlinkSync(from,to);}
fs.writeFileSync(path.join(destination,'rook-presence-fixture.json'),JSON.stringify({reviewOnly:true,person:'rook_art_review',copiedFrom:'rook',reason:'Show quest-gated Rook art at the docks without changing production rules or the original Rook presence flags.'},null,2)+'\n');
console.log('Prepared isolated Rook art-presence fixture; original gated Rook preserved.');
