"""Compare the prior/revised Druid panels from actual game capture videos/posters."""
import argparse,io,subprocess,tempfile,html
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[3]
REPORT=ROOT/'docs/superpowers/reports/combat-vfx/druid'
OUT=REPORT/'forms/native-motion'
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--before-ref',default='973287973e1b43ea0d82db5c02e84c4c927adef7');args=parser.parse_args();OUT.mkdir(parents=True,exist_ok=True);cards=[];font=ImageFont.truetype('DejaVuSans.ttf',28)
 with tempfile.TemporaryDirectory(prefix='druid-native-motion-') as scratch:
  for form in ('dire_wolf','wolf','elder_bear','bear'):
   for action in ('move','attack'):
    key='druid-form-'+form+'-'+action;old=Path(scratch)/(key+'.webm');relative=str((REPORT/(key+'-full.webm')).relative_to(ROOT));old.write_bytes(subprocess.check_output(['git','show',args.before_ref+':'+relative],cwd=ROOT))
    # Both source videos are real game before/after comparisons. Their right-hand
    # panels are the two Druid revisions being reviewed; the pre-Druid halves are omitted.
    cmd=['ffmpeg','-y','-v','error','-i',str(old),'-i',str(REPORT/(key+'-full.webm')),'-filter_complex','[0:v]crop=960:540:960:0[old];[1:v]crop=960:540:960:0[new];[old][new]hstack=inputs=2:shortest=0[v]','-map','[v]','-an','-c:v','libvpx-vp9','-threads','4','-row-mt','1','-cpu-used','4','-crf','32','-b:v','0',str(OUT/(key+'.webm'))]
    subprocess.run(cmd,check=True)
    relative=str((REPORT/(key+'-full.png')).relative_to(ROOT));before=Image.open(io.BytesIO(subprocess.check_output(['git','show',args.before_ref+':'+relative],cwd=ROOT))).convert('RGB')
    poster=Image.new('RGB',(3840,1130),'#101222');pen=ImageDraw.Draw(poster)
    for side,picture in enumerate((before,Image.open(REPORT/(key+'-full.png')).convert('RGB'))):
     poster.paste(picture.crop((1920,50,3840,1130)),(side*1920,50));pen.text((side*1920+25,9),('PREVIOUS' if side==0 else 'REVISED')+' · '+form.replace('_',' ').title()+' · '+action,font=font,fill='#f5e6bf')
    poster.save(OUT/(key+'.png'),optimize=True)
    title=form.replace('_',' ').title()+' · '+('walk' if action=='move' else 'attack')
    cards.append('<article><h2>'+html.escape(title)+'</h2><div class="labels"><span>PREVIOUS</span><span>REVISED</span></div><video controls loop muted preload="none" poster="'+key+'.png" src="'+key+'.webm"></video><p><a href="'+key+'.png">Full-resolution native poster</a></p></article>')
 (OUT/'index.html').write_text('<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Druid animal motion · native comparison</title><style>body{background:#101222;color:#eee6d0;font:18px system-ui;max-width:1600px;margin:2rem auto;padding:0 1rem}a{color:#f0d27a}video{width:100%;image-rendering:pixelated}.labels{display:flex}.labels span{width:50%;padding:.5rem}article{margin:3rem 0}p{max-width:85ch}</style><h1>Druid animal motion · actual game comparison</h1><p>Previous decorations on the left, corrected art on the right. These extract the Druid panels from the previous and revised real-engine captures, retaining their actual timing. Both use the same deterministic fixtures and unchanged playback. Videos are crops of the existing native galleries; the linked posters retain the source capture resolution. Capture uses dummy audio.</p><p><a href="../motion/index.html">Synchronized source-atlas inspector and frame stepping</a> · <a href="../../index.html">Complete native Druid gallery</a></p>'+''.join(cards)+'</html>')
 print('Exported eight previous/revised native walk/attack comparisons for all four Druid forms.')
if __name__=='__main__':main()
