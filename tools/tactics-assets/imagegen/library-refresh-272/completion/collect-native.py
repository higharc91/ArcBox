"""Compose real, engine-timed review captures after current-main integration."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,shutil,os
R=Path(__file__).resolve().parents[5];OUT=R/'docs/art/mob-sprite-review/library-completion';logs=Path(os.environ.get('ARCBOX_REVIEW_LOGS','/workspace/mob-review'));g=logs/'library-completion-moves';s=logs/'library-completion-spectre-moves'
def pick(folder,pattern):
 files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);return files[0]
shots=[]
for seq,name in enumerate(['Gold Jaws','Frost Troll strike','Ogre strike'],4):shots.append((name,pick(g,f'*-tactics-beat-hit-{seq}.png')))
for name,pattern in [('Gold Breath','*-tactics-beat-special-contact-7.png'),('Frost boulder release','*-tactics-beat-boulder-release-10.png'),('Ogre boulder release','*-tactics-beat-boulder-release-12.png'),('Gold Presence','*-tactics-beat-special-contact-8.png'),('Frost Frenzy Roar','*-tactics-beat-special-contact-11.png'),('Ogre Frenzy Roar','*-tactics-beat-special-contact-13.png'),('Gold Wingbeat','*-tactics-beat-special-contact-9.png')]:shots.append((name,pick(g,pattern)))
shots.extend([('Spectral touch',pick(s,'*-tactics-beat-hit-2.png')),('Life Drain',pick(s,'*-tactics-beat-special-contact-3.png'))])
assert len(shots)==12
c=Image.new('RGB',(1440,1600),'#111923');d=ImageDraw.Draw(c);font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',18)
folder=OUT/'native-contacts';folder.mkdir(exist_ok=True)
for i,(name,path) in enumerate(shots):
 x=i%3*480;y=i//3*400;d.text((x+10,y+10),name,font=font,fill='#eee9da');im=Image.open(path).crop((540,300,1500,1000)).resize((480,350),Image.Resampling.NEAREST);c.paste(im,(x,y+40));shutil.copyfile(path,folder/(name.lower().replace(' ','-')+'.png'))
c.save(OUT/'native-contacts.png');(OUT/'native-contacts.json').write_text(json.dumps({'integrationCommit':'fd5bf0c8','contacts':len(shots),'allFromEngineMomentMarkers':True,'scenes':[{'action':n,'sourceCapture':p.name} for n,p in shots]},indent=2)+'\n');print('Collected all twelve engine-timed contacts after main integration.')
