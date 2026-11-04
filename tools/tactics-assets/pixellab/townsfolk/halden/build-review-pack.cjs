// Review-only overlay. Never writes under production content or edits linked files.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../../../../..'),source=path.join(root,'content/tactics/base');
assert(process.argv[2],'Pass an explicit external review-pack destination');
const destination=path.resolve(process.argv[2]),marker=path.join(destination,'halden-review-owner.json');
assert(!destination.startsWith(path.join(root,'content')+path.sep),'Review pack must be outside content');
if(fs.existsSync(destination)&&fs.readdirSync(destination).length)assert(fs.existsSync(marker)&&JSON.parse(fs.readFileSync(marker)).source===source,'Refusing to overwrite an unrelated directory');
fs.mkdirSync(destination,{recursive:true});
fs.writeFileSync(marker,JSON.stringify({source,reviewOnly:true})+'\n');
function copyLink(from,to){if(fs.existsSync(to))return;try{fs.linkSync(from,to);}catch(e){if(['EXDEV','EPERM','ENOTSUP'].includes(e.code))fs.copyFileSync(from,to);else throw e;}}
for(const f of fs.readdirSync(source,{withFileTypes:true})) {
 const from=path.join(source,f.name),to=path.join(destination,f.name);
 if(f.name==='creatures'){
  fs.mkdirSync(to,{recursive:true});
  for(const name of fs.readdirSync(from))if(name!=='manifest.json')copyLink(path.join(from,name),path.join(to,name));
 }else if(!fs.existsSync(to)){if(f.isDirectory())fs.symlinkSync(from,to,'junction');else copyLink(from,to);}
}
const manifest=JSON.parse(fs.readFileSync(path.join(source,'creatures/manifest.json')));
manifest.sprites.review_halden=JSON.parse(fs.readFileSync(path.join(__dirname,'review-sprite.json')));
fs.copyFileSync(path.join(__dirname,'review_halden.png'),path.join(destination,'creatures/review_halden.png'));
fs.writeFileSync(path.join(destination,'creatures/manifest.json'),JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({destination,productionInstalled:false}));
