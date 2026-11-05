const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),{execFileSync}=require('node:child_process');
const {PNG}=require('pngjs');
const root=path.resolve(__dirname,'../../../../..'),work=fs.mkdtempSync(path.join(os.tmpdir(),'generic-contact-'));
const hero=PNG.sync.read(fs.readFileSync(path.join(root,'content/tactics/base/tactical/human_fighter.png'))),crop=new PNG({width:96,height:96});
PNG.bitblt(hero,crop,32,39,96,96,0,0);fs.writeFileSync(path.join(work,'hero.png'),PNG.sync.write(crop));
const frames=[];
for(const frame of [0,2,3,4,5]){
 const tiles=[];
 for(const preset of ['barmaid','child','farmer','farmhand','fisher','guard','merchant'])for(const variant of ['a','b','hero']){
  const source=variant==='hero'?path.join(work,'hero.png'):path.join(__dirname,`${preset}_${variant}/atlas.png`),tile=path.join(work,`${preset}_${variant}_${frame}.png`);
  const label=variant==='hero'?'Shipped hero':`${preset[0].toUpperCase()+preset.slice(1)} ${variant.toUpperCase()}`;
  execFileSync('convert',[source,'-crop',`96x96+${variant==='hero'?0:frame*96}+0`,'+repage','-filter','point','-resize','200%','-background','#232028','-alpha','remove','-fill','#eee','-font','DejaVu-Sans','-pointsize','14','-gravity','south','-splice','0x24','-annotate','+0+4',label,tile]);tiles.push(tile);
 }
 const out=path.join(work,`frame-${frame}.png`);execFileSync('montage',[...tiles,'-tile','3x7','-geometry','+4+4','-background','#232028',out]);
 if(frame===0)fs.copyFileSync(out,path.join(__dirname,'all-variants-review.png'));else frames.push(out);
}
execFileSync('convert',['-delay','17',...frames,'-loop','0',path.join(__dirname,'all-variants-walk.gif')]);
fs.rmSync(work,{recursive:true});
console.log('Created labelled fourteen-variant comparison and real-frame walk GIF.');
