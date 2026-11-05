// Offline production integration. The owner must approve the COMPLETE motion/portrait/TV set first.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..'),approval=JSON.parse(fs.readFileSync(path.join(__dirname,'sprite-approval.json')));
assert.equal(approval.completedSet?.status,'approved','Complete directional/motion/portrait/TV owner approval is required; appearance approval alone is insufficient');
assert(approval.ownerFullSetApproval?.quote,'Missing full-set approval evidence');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
for(const [file,sha]of Object.entries(approval.completedSet.sha256))assert.equal(hash(fs.readFileSync(path.join(__dirname,file))),sha,`Approved asset changed: ${file}`);
assert.equal(hash(fs.readFileSync(path.join(root,approval.completedSet.tv.path))),approval.completedSet.tv.sha256,'Approved TV preview changed');
const sprite=JSON.parse(fs.readFileSync(path.join(__dirname,'review-sprite.json'))),atlas=fs.readFileSync(path.join(__dirname,'review_mother_skarrow.png'));
assert.deepEqual(sprite.frame,{width:96,height:96,anchor:[48,84]});assert.equal(sprite.pixelScale,.5);assert.equal(sprite.columns,18);assert.equal(sprite.rowsPerDirection,2);
assert.deepEqual(sprite.directions,['south','south-west','west','north-west','north','north-east','east','south-east']);
assert.deepEqual(sprite.anims.idle.frames,[0,1,2,3,4,5,4,3,2,1]);assert.deepEqual(sprite.anims.walk.frames,[6,7,8,9,10,11]);assert.deepEqual(sprite.anims.talk.frames,[12,13,14,15,16,17,16,15,14,13]);
assert.equal(atlas.readUInt32BE(16),1728);assert.equal(atlas.readUInt32BE(20),1536);
const sourcePortraits=JSON.parse(fs.readFileSync(path.join(__dirname,'portraits-validation.json'))),portraits={};
for(const mood of ['idle','hurt','happy','angry','sad','surprised']){
 const bytes=fs.readFileSync(path.join(__dirname,'portraits',`mother_skarrow_${mood}.png`));assert.equal(hash(bytes),sourcePortraits.moods[mood].sha256);assert.equal(bytes.readUInt32BE(16),40);assert.equal(bytes.readUInt32BE(20),40);portraits[mood]=bytes;
}
const content=path.join(root,'content/tactics/base'),creaturesPath=path.join(content,'creatures/manifest.json'),portraitsPath=path.join(content,'portraits/manifest.json');
const creatures=JSON.parse(fs.readFileSync(creaturesPath)),faces=JSON.parse(fs.readFileSync(portraitsPath)),moods={};
creatures.sprites.mother_skarrow={...sprite,file:'mother_skarrow.png',variant:'mother_skarrow',provenance:'Image tool; complete directional/motion/portrait/TV set approved by owner. Exact atlas SHA256 '+hash(atlas)};
// Validate and stage named-only cosmetic documents before the first write.
const read=rel=>JSON.parse(fs.readFileSync(path.join(content,rel))),documents={};
const chars=read('characters/manifest.json');for(const key of ['mother_skarrow','goblin_shaman_mother_skarrow'])if(!chars.presets.includes(key))chars.presets.push(key);documents['characters/manifest.json']=chars;
const world=read('rules/world/ashen_ford.json'),person=world.people.find(p=>p.id==='mother_skarrow');assert(person);person.variant='mother_skarrow';documents['rules/world/ashen_ford.json']=world;
const monsters=read('rules/monsters/villains.json'),mother=monsters.find(m=>m.id==='mother_skarrow');assert(mother);mother.sprite='characters.mother_skarrow';documents['rules/monsters/villains.json']=monsters;
const lore=read('lore/characters.json'),character=lore.characters.find(c=>c.id==='mother_skarrow');assert(character);character.preset='mother_skarrow';documents['lore/characters.json']=lore;
for(const [mood,bytes]of Object.entries(portraits)){const file=`mother_skarrow_${mood}.png`;moods[mood]=file;fs.writeFileSync(path.join(content,'portraits',file),bytes);}
faces.presets.mother_skarrow=moods;
fs.writeFileSync(path.join(content,'creatures/mother_skarrow.png'),atlas);fs.writeFileSync(creaturesPath,JSON.stringify(creatures,null,2)+'\n');fs.writeFileSync(portraitsPath,JSON.stringify(faces,null,2)+'\n');
for(const [rel,doc] of Object.entries(documents)){
 const file=path.join(content,rel),original=fs.readFileSync(file,'utf8');let text;
 if(rel==='lore/characters.json')text=original.replace(/("id":\s*"mother_skarrow",\s*"preset":\s*)"[^"]+"/,'$1"mother_skarrow"');
 else if(rel==='rules/monsters/villains.json')text=original.replace(/("id":\s*"mother_skarrow"[\s\S]*?"sprite":\s*)"[^"]+"/,'$1"characters.mother_skarrow"');
 else if(rel==='characters/manifest.json'){
  const missing=doc.presets.filter(key=>!JSON.parse(original).presets.includes(key));
  const at=original.lastIndexOf('\n  ]');assert(at>=0);text=original.slice(0,at)+missing.map(key=>',\n    '+JSON.stringify(key)).join('')+original.slice(at);
 }else text=JSON.stringify(doc,null,2)+'\n';
 assert.deepEqual(JSON.parse(text),doc,'Formatting-preserving document differs');fs.writeFileSync(file,text);
}
console.log(JSON.stringify({installed:'mother_skarrow',creditsSpent:0,legacyFilesRetained:true,atlasSha256:hash(atlas)}));
