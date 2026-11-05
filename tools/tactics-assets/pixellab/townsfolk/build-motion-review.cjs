const fs=require('node:fs'),path=require('node:path');
const {PNG}=require('pngjs');
const {GIFEncoder,quantize,applyPalette}=require('gifenc');
const pack=process.argv[2]||path.join(require('node:os').homedir(),'.codex/tmp/bree-tv-review');
const manifest=JSON.parse(fs.readFileSync(path.join(pack,'creatures/manifest.json'),'utf8'));
const sprite=manifest.sprites.review_bree;
const atlas=PNG.sync.read(fs.readFileSync(path.join(pack,'creatures',sprite.file)));
const {width,height}=sprite.frame;
const cellWidth=width*2,cellHeight=height*2;
for(const action of ['idle','walk','talk']) {
  const clip=sprite.anims[action],frames=[];
  for(const index of clip.frames) {
    const output=new PNG({width:cellWidth*4,height:cellHeight*2});
    for(let direction=0;direction<8;direction++)for(let y=0;y<cellHeight;y++)for(let x=0;x<cellWidth;x++) {
      const sx=index%sprite.columns*width+Math.floor(x/2),sy=(direction*sprite.rowsPerDirection+Math.floor(index/sprite.columns))*height+Math.floor(y/2);
      const p=(sy*atlas.width+sx)*4,q=((Math.floor(direction/4)*cellHeight+y)*output.width+direction%4*cellWidth+x)*4,a=atlas.data[p+3]/255;
      [35,32,40].forEach((bg,c)=>output.data[q+c]=Math.round(atlas.data[p+c]*a+bg*(1-a)));output.data[q+3]=255;
    }
    frames.push(output);
  }
  const palette=quantize(Buffer.concat(frames.map(p=>p.data)),256),gif=GIFEncoder();
  for(const frame of frames)gif.writeFrame(applyPalette(frame.data,palette),frame.width,frame.height,{palette,delay:1000/clip.fps,repeat:0});
  gif.finish();fs.writeFileSync(path.join(__dirname,`bree-eight-direction-${action}.gif`),gif.bytes());
  fs.writeFileSync(path.join(__dirname,`bree-eight-direction-${action}.png`),PNG.sync.write(frames[0]));
  const contact=new PNG({width:clip.frames.length*width,height:8*height});
  clip.frames.forEach((index,column)=>{
    for(let direction=0;direction<8;direction++)for(let y=0;y<height;y++)for(let x=0;x<width;x++) {
      const p=(((direction*sprite.rowsPerDirection+Math.floor(index/sprite.columns))*height+y)*atlas.width+index%sprite.columns*width+x)*4;
      const q=((direction*height+y)*contact.width+column*width+x)*4,a=atlas.data[p+3]/255;
      [35,32,40].forEach((bg,c)=>contact.data[q+c]=Math.round(atlas.data[p+c]*a+bg*(1-a)));contact.data[q+3]=255;
    }
  });
  fs.writeFileSync(path.join(__dirname,`bree-${action}-contact.png`),PNG.sync.write(contact));
}
console.log('Built eight-direction idle, walk and talk review GIFs.');
