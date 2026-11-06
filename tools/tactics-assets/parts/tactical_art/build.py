"""Bake the runtime tactical character set with the production animation table."""
from pathlib import Path
import json
import multiprocessing as mp
import tempfile
from PIL import Image
from parts.creator import RACES,CLASSES
from parts.rig import frame_list,ANIMS
from arcpix.out import PACK,PREVIEWS,previews_enabled,save
from .animation import FRAME,ANCHOR,COLUMNS,frame,rest,OFFSET
from parts.rig import Pose
import numpy as np

def review():
    if not previews_enabled(): return
    from arcpix.preview import matrix
    cells=[]
    for race in RACES:
        cells.append([frame(role,race,False,Pose()).crop((48,48,112,128)) for role in CLASSES])
    matrix('Runtime tactical characters', [r.name for r in RACES.values()],
        [c.name for c in CLASSES.values()], cells, zoom=2).save(PREVIEWS/'tactical-roster.png')

def walking_review():
    if not previews_enabled(): return
    from arcpix.preview import contact_sheet,Tile,strip
    picks=[('human','fighter'),('tiefling','rogue'),('dragonborn','wizard'),
        ('dwarf','cleric'),('elf','ranger'),('half_elf','paladin'),('gnome','bard'),
        ('halfling','druid'),('aasimar','warlock'),('goliath','monk'),
        ('half_orc','barbarian'),('human','sorcerer')]
    images=[]
    for pose in ANIMS['walk'][2]:
        tiles=[]
        for race,role in picks:
            views=[frame(role,race,back,pose).crop((32,32,128,144)) for back in (False,True)]
            tiles.append(Tile(strip(views,1,gap=4),f'{RACES[race].name} {CLASSES[role].name}'))
        images.append(contact_sheet('Walking attachment review',tiles,columns=3,zoom=2,
            subtitle='Front / back · all classes · generated from runtime poses').convert('RGB'))
    images[0].save(PREVIEWS/'tactical-all-classes-walk.gif',save_all=True,
        append_images=images[1:],duration=round(1000/ANIMS['walk'][0]),loop=0)

def bake(job):
    role,race,root=job
    poses=frame_list();rows=(len(poses)+COLUMNS-1)//COLUMNS
    atlas=Image.new('RGBA',(FRAME[0]*COLUMNS,FRAME[1]*rows*2))
    for back in (False,True):
        for i,(_,_,pose) in enumerate(poses):
            im=frame(role,race,back,pose)
            atlas.paste(im,((i%COLUMNS)*FRAME[0],(int(back)*rows+i//COLUMNS)*FRAME[1]))
    atlas.save(Path(root)/f'{race}_{role}.png',optimize=True)
    return f'{race}_{role}'

def build():
    root=PACK/'tactical';root.mkdir(exist_ok=True)
    # Do not replace the last working asset set if any frame fails validation.
    with tempfile.TemporaryDirectory(prefix='.tactical-build-',dir=PACK) as staging:
        staged=Path(staging)
        jobs=[(role,race,str(staged)) for race in RACES for role in CLASSES]
        with mp.get_context('fork').Pool(4) as pool:
            keys=list(pool.imap(bake,jobs))
        anims={}
        for name,(fps,loop,poses) in ANIMS.items():
            # Actual production order is defined by frame_list, not dictionary ordering.
            indices=[i for i,(a,_,_) in enumerate(frame_list()) if a==name]
            anims[name]={'frames':indices,'fps':fps,'loop':loop}
        heads={}
        for role,race,_ in jobs:
            # Include headwear/horns when positioning nameplates, not the weapon tip.
            _,layers=rest(role,race,False)
            ys=[]
            for name in ('head','hair','headgear','class-headwear','race-details'):
                if name in layers:
                    y=np.nonzero(np.array(layers[name])[:,:,3])[0]
                    if len(y):ys.append(int(y.min()))
            heads[f'{race}_{role}']=OFFSET[1]+min(ys or [10])
        manifest={'frame':{'width':FRAME[0],'height':FRAME[1],'anchor':ANCHOR},
            'columns':COLUMNS,'framesPerDirection':len(frame_list()),'pixelScale':.5,
            'footprintWidth':32,'heads':heads,'anims':anims,'presets':keys}
        (staged/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
        for key in keys:
            (staged/f'{key}.png').replace(root/f'{key}.png')
        (staged/'manifest.json').replace(root/'manifest.json')
    # Short animated review strips from the exact runtime frames.
    if previews_enabled():
        for role,race in [('fighter','human'),('wizard','dragonborn'),('rogue','tiefling'),('cleric','dwarf')]:
            images=[frame(role,race,False,p).convert('RGB') for p in ANIMS['walk'][2]]
            save(images[0], PREVIEWS/f'tactical-{race}-{role}-walk.gif',save_all=True,append_images=images[1:],duration=round(1000/ANIMS['walk'][0]),loop=0)
        review()
        walking_review()
    print(f'Built {len(keys)} tactical sheets, {len(frame_list())} frames per facing.')

if __name__=='__main__':build()
