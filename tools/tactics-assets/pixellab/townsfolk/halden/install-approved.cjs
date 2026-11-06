// Offline install of the exact approved Halden pixels and metadata; no generation or linked-content edits.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..');
const approval=JSON.parse(fs.readFileSync(path.join(__dirname,'sprite-approval.json')));
assert.equal(approval.status,'approved','Owner full-set/TV approval is required');
assert(approval.ownerApproval?.quote,'Missing owner approval evidence');
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
for(const [file,expected] of Object.entries(approval.sha256))assert.equal(hash(fs.readFileSync(path.join(__dirname,file))),expected,`Approved source changed: ${file}`);
assert.equal(hash(fs.readFileSync(path.join(root,approval.tv.path))),approval.tv.sha256,'Approved TV preview changed');
const source=JSON.parse(fs.readFileSync(path.join(__dirname,'review-sprite.json')));
assert.deepEqual(source.frame,{width:96,height:96,anchor:[48,84]});
assert.equal(source.pixelScale,.5);assert.equal(source.columns,15);assert.equal(source.rowsPerDirection,1);
assert.deepEqual(source.directions,['south','south-west','west','north-west','north','north-east','east','south-east']);
assert.deepEqual(source.anims,{
 idle:{frames:[0,1,2,3],fps:10/3,loop:true},walk:{frames:[4,5,6,7,8,9],fps:20/3,loop:true},
 talk:{frames:[10,11,12,13,14,13,12,11],fps:16/3,loop:true},down:{frames:[0],fps:1,loop:false}
});
const atlas=fs.readFileSync(path.join(__dirname,'review_halden.png'));
const manifestPath=path.join(root,'content/tactics/base/creatures/manifest.json');
const manifest=JSON.parse(fs.readFileSync(manifestPath));
manifest.sprites.halden_varrow={...source,file:'halden_varrow.png',variant:'halden_varrow',keys:['characters.mayor','characters.halden_varrow'],provenance:'PixelLab; complete directional idle/walk/talk and native TV appearance approved by owner in chat. Exact atlas SHA256 '+hash(atlas)};
fs.writeFileSync(path.join(root,'content/tactics/base/creatures/halden_varrow.png'),atlas);
fs.writeFileSync(manifestPath,JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({installed:'halden_varrow',atlasSha256:hash(atlas),creditsSpent:0,preservedLegacySheet:true}));
