// Review reel of the actual sprite animations/portraits and a labelled native TV still.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),{spawnSync}=require('node:child_process');
const root=__dirname,tmp=fs.mkdtempSync(path.join(os.tmpdir(),'celandine-reel-')),font=process.env.CELANDINE_REVIEW_FONT||'/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf';
const tv=process.argv[2],ffmpeg=process.env.FFMPEG||'ffmpeg';
const labels={idle:'IDLE - a quiet breath',walk:'WALK - mantle and gown sway',talk:'TALK - a cultivated conversation'};
function run(args){const p=spawnSync(ffmpeg,['-hide_banner','-loglevel','error','-y',...args],{encoding:'utf8'});if(p.status!==0)throw new Error(p.stderr);}
const title=t=>`drawtext=fontfile=${font}:text='${t}':fontcolor=0xe8c18c:fontsize=28:x=(w-text_w)/2:y=30`;
const footer=t=>`drawtext=fontfile=${font}:text='${t}':fontcolor=0xc8c1cc:fontsize=16:x=(w-text_w)/2:y=604`;
const encode=['-an','-r','20','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-threads','2'];
try{
 const clips=[];
 for(const action of ['idle','walk','talk']){
  const file=path.join(tmp,action+'.mp4'),labelsDirections=['S','SW','W','NW','N','NE','E','SE'].map((s,i)=>`drawtext=fontfile=${font}:text='${s}':fontcolor=0xa7a0af:fontsize=14:x=${128+i%4*192+90}:y=${128+Math.floor(i/4)*192+4}`).join(',');
  run(['-ignore_loop','1','-stream_loop','-1','-i',path.join(root,`celandine-eight-direction-${action}.gif`),'-t',action==='walk'?'3.6':action==='talk'?'6':'3','-vf',`scale=768:384:flags=neighbor,pad=1024:640:128:128:color=0x231f28,${title('LADY CELANDINE - '+labels[action])},${labelsDirections},${footer('EMBERWICK - LADY CELANDINE')}`,...encode,file]);clips.push(file);
 }
 const portraits=path.join(tmp,'portraits.mp4');run(['-loop','1','-i',path.join(root,'celandine-portraits-review.png'),'-t','3','-vf',`scale=720:480:flags=neighbor,pad=1024:640:152:96:color=0x231f28,${title('LADY CELANDINE - dialogue portraits')},${footer('Neutral - Hurt - Happy  /  Angry - Sad - Surprised')}`,...encode,portraits]);clips.push(portraits);
 if(tv){const file=path.join(tmp,'tv.mp4');run(['-loop','1','-i',path.resolve(tv),'-t','4','-vf',`scale=1024:576:flags=neighbor,pad=1024:640:(ow-iw)/2:40:color=0x231f28,${title('TV STILL - Celandine with four party members').replace(':y=30',':y=8')}`,...encode,file]);clips.push(file);}
 const list=path.join(tmp,'clips.txt');fs.writeFileSync(list,clips.map(f=>`file '${f.replace(/'/g,"'\\''")}'`).join('\n')+'\n');
 const output=path.join(root,'celandine-finished-review.mp4');run(['-f','concat','-safe','0','-i',list,'-c','copy','-movflags','+faststart',output]);const voiced=path.join(tmp,'voiced.mp4');run(['-i',output,'-itsoffset','6.6','-i',path.join(root,'voice-sample.mp3'),'-map','0:v:0','-map','1:a:0','-c:v','copy','-af','apad','-t',tv?'19.6':'15.6','-c:a','aac','-movflags','+faststart',voiced]);fs.copyFileSync(voiced,output);console.log(JSON.stringify({video:output,containsNativeTvStill:Boolean(tv),audio:true,voice:'existing recorded Celandine town bark'}));
}finally{fs.rmSync(tmp,{recursive:true,force:true});}
