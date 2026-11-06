// Deterministic colour-key preview, not a redraw. Retain all three image-tool outputs.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {PNG}=require('pngjs'),root=__dirname;
const raw=fs.readFileSync(path.join(root,'candidate-1-export.png')),img=PNG.sync.read(raw);
for(let p=0;p<img.data.length;p+=4){
 const magenta=img.data[p]>180&&img.data[p+1]<90&&img.data[p+2]>150;
 if(magenta)img.data.fill(0,p,p+4);
 else img.data[p+3]=255;
}
const cw=img.width/4,ch=img.height/2;assert(Number.isInteger(cw)&&Number.isInteger(ch));
const bounds=[];
for(let i=0;i<8;i++){
 let left=cw,top=ch,right=-1,bottom=-1;
 for(let y=0;y<ch;y++)for(let x=0;x<cw;x++)if(img.data[((Math.floor(i/4)*ch+y)*img.width+i%4*cw+x)*4+3]){left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);}
 assert(left>0&&right<cw-1&&top>0&&bottom<ch-1,`Empty or clipped view ${i}`);bounds.push({left,top,right,bottom});
}
const output=PNG.sync.write(img),sha=b=>crypto.createHash('sha256').update(b).digest('hex');
fs.writeFileSync(path.join(root,'candidate-1-preview.png'),output);
fs.writeFileSync(path.join(root,'appearance-validation.json'),JSON.stringify({source:'candidate-1-export.png',sourceSha256:sha(raw),previewSha256:sha(output),colourKey:'r>180 && g<90 && b>150',alpha:'binary; transparent RGB cleared; non-key RGB preserved',layout:[4,2],bounds,nativeAtlasValidated:false},null,2)+'\n');
console.log('PASS: eight complete standing cells; colour-key appearance preview only, not a native atlas.');
