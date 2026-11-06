// Review-only overlay. Never writes under production content or edits linked files.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..'),source=path.join(root,'content/tactics/base');
assert(process.argv[2],'Pass an explicit external review-pack destination');
const destination=path.resolve(process.argv[2]),marker=path.join(destination,'pip-review-owner.json');
assert(!destination.startsWith(path.join(root,'content')+path.sep),'Review pack must be outside content');
if(fs.existsSync(destination)&&fs.readdirSync(destination).length)assert(fs.existsSync(marker)&&JSON.parse(fs.readFileSync(marker)).source===source,'Refusing to overwrite an unrelated directory');
fs.mkdirSync(destination,{recursive:true});
fs.writeFileSync(marker,JSON.stringify({source,reviewOnly:true})+'\n');
function copyLink(from,to){if(fs.existsSync(to))return;try{fs.linkSync(from,to);}catch(e){if(['EXDEV','EPERM','ENOTSUP'].includes(e.code))fs.copyFileSync(from,to);else throw e;}}
for(const f of fs.readdirSync(source,{withFileTypes:true})) {
 const from=path.join(source,f.name),to=path.join(destination,f.name);
 if(f.name==='rules'){
  if(fs.existsSync(to)&&fs.lstatSync(to).isSymbolicLink())fs.unlinkSync(to);
  fs.cpSync(from,to,{recursive:true});
  const regionPath=path.join(to,'world/ashen_ford.json'),region=JSON.parse(fs.readFileSync(regionPath));
  region.people.find(p=>p.id==='pip_ashdown').variant='pip';
  fs.writeFileSync(regionPath,JSON.stringify(region,null,2)+'\n');
 }else if(f.name==='portraits'&&fs.existsSync(path.join(__dirname,'portraits-validation.json'))){
  if(fs.existsSync(to)&&fs.lstatSync(to).isSymbolicLink())fs.unlinkSync(to);
  fs.mkdirSync(to,{recursive:true});
  for(const entry of fs.readdirSync(from,{withFileTypes:true})){
   if(entry.name==='manifest.json')continue;
   const target=path.join(to,entry.name);
   if(entry.isDirectory()){if(!fs.existsSync(target))fs.symlinkSync(path.join(from,entry.name),target,'junction');}
   else copyLink(path.join(from,entry.name),target);
  }
  const portraits=JSON.parse(fs.readFileSync(path.join(from,'manifest.json'))),moods={};
  for(const mood of ['idle','hurt','happy','angry','sad','surprised']){
   const file=`review_pip_${mood}.png`;moods[mood]=file;
   fs.copyFileSync(path.join(__dirname,'portraits',`pip_${mood}.png`),path.join(to,file));
  }
  portraits.presets.child_pip=moods;portraits.presets.pip_ashdown=moods;
  fs.writeFileSync(path.join(to,'manifest.json'),JSON.stringify(portraits,null,2)+'\n');
 }else if(f.name==='creatures'){
  fs.mkdirSync(to,{recursive:true});
  for(const name of fs.readdirSync(from))if(name!=='manifest.json')copyLink(path.join(from,name),path.join(to,name));
 }else if(!fs.existsSync(to)){if(f.isDirectory())fs.symlinkSync(from,to,'junction');else copyLink(from,to);}
}
const manifest=JSON.parse(fs.readFileSync(path.join(source,'creatures/manifest.json')));
manifest.sprites.review_pip=JSON.parse(fs.readFileSync(path.join(__dirname,'review-sprite.json')));
fs.copyFileSync(path.join(__dirname,'review_pip.png'),path.join(destination,'creatures/review_pip.png'));
fs.writeFileSync(path.join(destination,'creatures/manifest.json'),JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({destination,productionInstalled:false}));
