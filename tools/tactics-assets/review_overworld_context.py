"""Same-scale environmental art comparisons, composed from baked pack images.

These are proposed dressing studies, not screenshots of a walking overworld.
python3 tools/tactics-assets/review_overworld_context.py --before /path/to/sets
"""
import argparse
import json
from functools import lru_cache
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from arcpix import iso
from arcpix.out import PACK,PREVIEWS


@lru_cache(None)
def manifest(root,section):
    return json.loads((root/section/'manifest.json').read_text())


@lru_cache(None)
def texture(root,name,var):
    t=manifest(root,'terrain')['textures'][name]
    with Image.open(root/'terrain'/t['variants'][var]) as src:
        return np.array(src.crop((0,0,*t['size']))).astype(np.float32)


def prop(root,name,var,i,j):
    p=manifest(root,'props')['props'][name]
    with Image.open(root/'props'/p['variants'][var]) as src:im=src.crop((0,0,*p['size']))
    return iso.Sprite(im,tuple(p['anchor']),i,j,scale=1,shadow=False)


def hero(role,race,i,j,back=False):
    with Image.open(PACK/f'tactical/{race}_{role}.png') as src:
        y=1280*int(back);im=src.crop((0,y,160,y+160)).resize((80,80),Image.Resampling.NEAREST)
    return iso.Sprite(im,(40,61.5),i,j,scale=1)


def scene(root,kind,after):
    s=iso.Scene(8,6)
    for j in range(6):
        for i in range(8):
            name,var,h=('grass','green',2)
            if kind=='bank':
                if i>=6:name,var='ash','grey'
                if i in (4,5):name,var,h='water','clear',1
                if j in (3,4) and i<4:name,var='path','packed'
            else:
                name,var='grass','autumn'
                if 2<=i<=5 and 2<=j<=4:name,var='dirt','dark'
            s.cells[i,j]=iso.Cell(h,texture(root,name,var),texture(root,'dirt_side','dark' if name=='ash' else 'brown'))
    if kind=='bank':
        s.sprites.extend([prop(root,'tree','autumn',1,1),prop(root,'dead_tree','dark',7,2),
            prop(root,'reeds','swamp',6,5),prop(root,'rock_mossy','grey',3,1),
            hero('fighter','human',2,3),hero('wizard','dragonborn',3,4)])
        if after:
            a=manifest(root,'ambient')['assets']['river_steam']
            with Image.open(root/'ambient'/a['variants']['mist']) as src:im=src.crop((0,0,*a['frame']))
            s.sprites.append(iso.Sprite(im,tuple(a['anchor']),5,2,scale=1,shadow=False))
    else:
        s.sprites.extend([prop(root,'tent','hide' if after else 'canvas',2,2),
            prop(root,'tent','canvas',5,1),prop(root,'campfire','fire',4,3),
            prop(root,'sack','burlap',5,4),prop(root,'log','oak',3,4),
            prop(root,'rock_small','grey',6,4),hero('ranger','elf',2,4,True)])
    return iso.render(s,margin=32,backdrop=lambda w,h:Image.new('RGBA',(w,h),'#192335'))


def review(before):
    f=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',17)
    b=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',24)
    out=Image.new('RGB',(1064,990),'#101827');d=ImageDraw.Draw(out)
    d.text((18,16),'OVERWORLD ART · BEFORE / AFTER',font=b,fill='#f0dfbd')
    d.text((18,55),'Proposed dressing studies · native game density · software composition',font=f,fill='#b8c8df')
    for row,(kind,title) in enumerate((('bank','Warm fields / burned river bank'),('camp','Skarrow winter shelter'))):
        y=92+row*440
        for col,(root,after) in enumerate(((before,False),(PACK,True))):
            x=12+col*532
            d.text((x+5,y),('AFTER · ' if after else 'BEFORE · ')+title,font=f,fill='#e6c276')
            out.paste(scene(root,kind,after),(x,y+32))
    d.text((18,958),'Art and recommended dressing only; the saved region has no prop-placement layer yet.',font=f,fill='#b8c8df')
    out.save(PREVIEWS/'overworld-context-comparison.png')
    picks=[('well','stone'),('tent','canvas'),('chest','oak'),('altar','holy')]
    out=Image.new('RGB',(1050,320),'#192335');d=ImageDraw.Draw(out)
    d.text((18,14),'UTILITY PROPS · BEFORE / AFTER',font=b,fill='#f0dfbd')
    for n,(name,var) in enumerate(picks):
        x=12+n*260;d.text((x,60),name.replace('_',' ').title(),font=f,fill='#e6c276')
        for col,root in enumerate((before,PACK)):
            im=prop(root,name,var,0,0).image;im=im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST)
            out.paste(im,(x+col*125,110),im)
            d.text((x+col*125,270),'Before' if col==0 else 'After',font=f,fill='#b8c8df')
    out.save(PREVIEWS/'overworld-prop-refinements.png')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--before',type=Path,required=True)
    review(p.parse_args().before)
