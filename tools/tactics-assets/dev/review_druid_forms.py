"""Inspect Druid forms at the same game scale as the player; no separate creature renderer."""
import sys,json,html,base64
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PIL import Image,ImageDraw,ImageFont
from arcpix.out import PACK
from parts.druid_forms import FORMS
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'docs/superpowers/reports/combat-vfx/druid/forms'
FONT=ImageFont.truetype('DejaVuSans.ttf',17)
def crop(sheet,e,f,row):
 w,h=e['frame']['width'],e['frame']['height'];x=f%e['columns']*w;y=(row*e['rowsPerDirection']+f//e['columns'])*h
 return sheet.crop((x,y,x+w,y+h))
def preview(im,e,label,hero,anchor):
 out=Image.new('RGB',(460,300),'#222732');draw=ImageDraw.Draw(out);draw.line((8,261,450,261),fill='#51545d')
 human=hero.resize((320,320),Image.Resampling.NEAREST);out.paste(human,(105-anchor[0]*2,260-anchor[1]*2),human)
 z=4*e['pixelScale'];animal=im.resize((round(im.width*z),round(im.height*z)),Image.Resampling.NEAREST);ax,ay=e['frame']['anchor'];out.paste(animal,(320-round(ax*z),260-round(ay*z)),animal)
 draw.text((10,8),label,font=FONT,fill='#eee4cd');return out

def main():
 OUT.mkdir(parents=True,exist_ok=True);manifest=json.loads((PACK/'creatures/manifest.json').read_text())['sprites'];tm=json.loads((PACK/'tactical/manifest.json').read_text());hero=Image.open(PACK/'tactical/human_druid.png').convert('RGBA').crop((0,0,160,160));anchor=tm['frame']['anchor'];cards=[];grid=Image.new('RGB',(1380,1200),'#222732')
 for row,form in enumerate(FORMS):
  e=manifest['druid_'+form];sheet=Image.open(PACK/'creatures'/e['file']).convert('RGBA');old=manifest['bear_wildshape' if 'bear' in form else 'wolf_wildshape'];oldsheet=Image.open(PACK/'creatures'/old['file']).convert('RGBA')
  for col,(label,entry,atlas,d) in enumerate([('Before',old,oldsheet,1),('After · front',e,sheet,0),('After · profile',e,sheet,2)]):
   grid.paste(preview(crop(atlas,entry,0,d),entry,form.replace('_',' ').title()+' · '+label,hero,anchor),(col*460,row*300))
  for action in ('walk','attack'):
   anim=e['anims'][action];frames=[]
   for d in range(8):
    for f in anim['frames']:
     frames.append(preview(crop(sheet,e,f,d),e,form.replace('_',' ').title()+' · '+e['directions'][d]+' · '+action,hero,anchor))
   name=form+'-'+action+'.gif';frames[0].save(OUT/name,save_all=True,append_images=frames[1:],duration=round(1000/anim['fps']),loop=0,optimize=False)
  cards.append('<article><h2>'+form.replace('_',' ').title()+'</h2><img src="'+form+'-walk.gif"><img src="'+form+'-attack.gif"></article>')
 grid.save(OUT/'style-grid.png',optimize=True)
 (OUT/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Druid form inspection</title><style>body{background:#222732;color:#eee4cd;font:17px system-ui;margin:24px}main{display:grid;grid-template-columns:repeat(2,minmax(320px,1fr));gap:20px}img{max-width:100%;image-rendering:pixelated}</style><h1>Druid forms · player scale reference</h1><p>Source-atlas inspection at the same 4× game magnification. Each animation cycles through all eight real compass views. Idle and walking feet stay at their declared ground anchor; attack poses retain their authored motion. The main gallery contains the actual native game captures.</p><p><a href="motion/index.html">Synchronized previous/revised motion</a> · <a href="../index.html">Native before-and-after gallery</a></p><img src="style-grid.png"><main>'+''.join(cards)+'</main>')
 print('Exported eight-direction form walk/attack GIFs, style grid and player-scale inspector.')
if __name__=='__main__':main()
