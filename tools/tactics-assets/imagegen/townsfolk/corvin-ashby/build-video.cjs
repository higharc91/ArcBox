// Review reel of the actual sprite animations/portraits and a labelled native TV still.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),{spawnSync}=require('node:child_process');
const root=__dirname,tmp=fs.mkdtempSync(path.join(os.tmpdir(),'corvin-reel-')),font=process.env.CORVIN_REVIEW_FONT||'/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf';
const tv=process.argv[2],ffmpeg=process.env.FFMPEG||'ffmpeg';
const labels={idle:'IDLE - a quiet breath',walk:'WALK - cloak and instrument sway',talk:'TALK - a songkeepers tale'};
function run(args){const p=spawnSync(ffmpeg,['-hide_banner','-loglevel','error','-y',...args],{encoding:'utf8'});if(p.status!==0)throw new Error(p.stderr);}
const title=t=>`drawtext=fontfile=${font}:text='${t}':fontcolor=0xe8c18c:fontsize=28:x=(w-text_w)/2:y=30`;
const footer=t=>`drawtext=fontfile=${font}:text='${t}':fontcolor=0xc8c1cc:fontsize=16:x=(w-text_w)/2:y=604`;
const encode=['-an','-r','20','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-threads','2'];
try{
 const clips=[];
 for(const action of ['idle','walk','talk']){
  const file=path.join(tmp,action+'.mp4'),labelsDirections=['S','SW','W','NW','N','NE','E','SE'].map((s,i)=>`drawtext=fontfile=${font}:text='${s}':fontcolor=0xa7a0af:fontsize=14:x=${128+i%4*192+90}:y=${128+Math.floor(i/4)*192+4}`).join(',');
  run(['-ignore_loop','1','-stream_loop','-1','-i',path.join(root,`corvin-eight-direction-${action}.gif`),'-t',action==='walk'?'3.6':'3','-vf',`pad=1024:640:128:128:color=0x231f28,${title('CORVIN ASHBY - '+labels[action])},${labelsDirections},${footer('EMBERWICK - KEEPER OF THE ARCHIVE')}`,...encode,file]);clips.push(file);
 }
 const portraits=path.join(tmp,'portraits.mp4');run(['-loop','1','-i',path.join(root,'corvin-portraits-review.png'),'-t','3','-vf',`scale=720:480:flags=neighbor,pad=1024:640:152:96:color=0x231f28,${title('CORVIN ASHBY - dialogue portraits')},${footer('Neutral - Hurt - Happy  /  Angry - Sad - Surprised')}`,...encode,portraits]);clips.push(portraits);
 if(tv){const file=path.join(tmp,'tv.mp4');run(['-loop','1','-i',path.resolve(tv),'-t','4','-vf',`pad=1024:640:(ow-iw)/2:40:color=0x231f28,${title('TV STILL - archive with four party members').replace(':y=30',':y=8')}`,...encode,file]);clips.push(file);}
 const list=path.join(tmp,'clips.txt');fs.writeFileSync(list,clips.map(f=>`file '${f.replace(/'/g,"'\\''")}'`).join('\n')+'\n');
 const output=path.join(root,'corvin-finished-review.mp4');run(['-f','concat','-safe','0','-i',list,'-c','copy','-movflags','+faststart',output]);console.log(JSON.stringify({video:output,containsNativeTvStill:Boolean(tv),audio:false}));
}finally{fs.rmSync(tmp,{recursive:true,force:true});}
