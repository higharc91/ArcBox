"""Whole-pose extraction and calibration, reusing the proven first-batch reader."""
from pathlib import Path
import importlib.util,json
import numpy as np
from scipy.ndimage import label,find_objects,binary_closing
from PIL import Image
ROOT=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('mob_measure',ROOT.parent/'mob-refresh-2026-11-06/production/measure.py')
reader=importlib.util.module_from_spec(spec);spec.loader.exec_module(reader);reader.ROOT=ROOT
DIRECTIONS=reader.DIRECTIONS

def source(name,count):
    frames=reader.poses(name,count,1)
    im=np.array(Image.open(ROOT/'source'/f'{name}.png').convert('RGBA')).astype(float)
    for frame in frames:
        x,y,w,h=frame['cell'];part=im[y:y+h,x:x+w]
        r,g,b,a=[part[:,:,i]for i in range(4)]
        skin=(a>=128)&(g>=r*.75)&(r>=g*.8)&(g>=b*1.2)&(r>=b*1.15)
        skin[int(h*.65):]=False
        labs,n=label(binary_closing(skin,np.ones((3,3))),np.ones((3,3)))
        sizes=np.bincount(labs.ravel());indices=[i for i in range(1,n+1)if sizes[i]>20]
        if indices:
            biggest=max(sizes[i]for i in indices)
            selected=[i for i in indices if sizes[i]>=biggest*.3]
            yy,xx=np.where(np.isin(labs,selected))
            frame['head']=[x+(int(xx.min())+int(xx.max())+1)/2,y+int(yy.min())]
            frame['headWidth']=int(xx.max()-xx.min()+1)
        else:frame['headWidth']=max(10,int(w*.6))
    return frames

def calibrated(name,count,height):
    frames=source(name,count)
    scale=height/(frames[0]['bounds'][3]-frames[0]['bounds'][1])
    for f in frames:f['scale']=scale
    return frames

items=[]
for role,size,floor,height in [('archer',128,111,80),('shaman',160,142,80),('boss',160,142,88)]:
    idle=[dict(f) for f in source(role+'-idle',32)]
    if role=='archer':idle[:8]=source('archer-idle-front',8)
    for name in {Path(f['source']).stem for f in idle}:
        reference=reader.SOURCES[name][0]
        scale=height/(reference['bounds'][3]-reference['bounds'][1])
        for f in idle:
            if Path(f['source']).stem==name:f['scale']=scale
    head_width=idle[0]['headWidth']*idle[0]['scale']
    walk=calibrated(role+'-walk',32,height)
    attack=calibrated(role+'-attack',32,height*(1 if role=='archer' else .9))
    damage=calibrated(role+'-damage',16,height)
    cast=None
    if role=='archer':
        fixed=calibrated('archer-continuity',8,height)
        idle[4:8]=fixed[:4];walk[4:8]=fixed[4:]
        fixed=calibrated('archer-aim',8,height)
        attack[4:8]=fixed[:4];attack[28:32]=fixed[4:]
    elif role=='shaman':
        fixed=calibrated('shaman-continuity',8,height)
        idle[28:32]=fixed[:4];walk[28:32]=fixed[4:]
        fixed=calibrated('shaman-aim',12,height*.9)
        for row,offset in [(5,0),(6,4),(7,8)]:attack[row*4:row*4+4]=fixed[offset:offset+4]
        cast=calibrated('shaman-cast',32,height)
        fixed=calibrated('shaman-cast-clean',8,height)
        cast[12:16]=fixed[:4];cast[28:32]=fixed[4:]
    else:cast=calibrated('boss-cast',32,height)
    frames=[]
    for row,direction in enumerate(DIRECTIONS):
        di=(row//2)*4+(row%2)*2
        groups=[('idle',idle[row*4:row*4+4]),('walk',walk[row*4:row*4+4]),('attack',attack[row*4:row*4+4]),('hurt',[damage[di]]),('down',[damage[di+1]])]
        if cast:groups.append(('cast',[cast[row*4+i]for i in [0,2,3]]))
        for action,poses in groups:
            for index,pose in enumerate(poses):frames.append({**pose,'direction':direction,'row':row,'action':action,'frame':index})
    item={'id':'goblin_'+role,'role':role,'size':size,'anchor':[size//2,floor],'pixelScale':.32,'rowsPerDirection':3 if cast else 2,'actionOffsets':{'idle':0,'walk':4,'attack':8,'hurt':12,'down':13,**({'cast':14}if cast else{})},'frames':frames}
    items.append(item)
    print(role,'target body',height,'head',round(head_width,1),'source scales',sorted(set(round(f['scale'],3)for f in frames)))
(ROOT/'packing.json').write_text(json.dumps(items,indent=2)+'\n')
print('Selected',sum(len(i['frames'])for i in items),'complete authored poses.')
