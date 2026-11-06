"""Technical extraction only: locate complete authored actors, keep each action's fixed scale."""
from pathlib import Path
import json
import numpy as np
from PIL import Image
from scipy.ndimage import label, find_objects
ROOT=Path(__file__).resolve().parent
DIRECTIONS=['south','south-west','west','north-west','north','north-east','east','south-east']
SOURCES={}
def poses(name,count,scale):
    if name in SOURCES:return SOURCES[name]
    im=Image.open(ROOT/'source'/f'{name}.png').convert('RGBA')
    alpha=np.array(im.getchannel('A'))
    labels,n=label(alpha>=64,np.ones((3,3)))
    sizes=np.bincount(labels.ravel())
    boxes=[(i,b) for i,b in enumerate(find_objects(labels),1) if sizes[i]>100]
    assert len(boxes)==count,(name,len(boxes))
    boxes.sort(key=lambda v:v[1][0].stop)
    ordered=[]
    for start in range(0,count,4):ordered+=sorted(boxes[start:start+4],key=lambda v:v[1][1].start)
    frames=[]
    for i,(lab,(yy,xx)) in enumerate(ordered):
        assert not np.any((labels[yy,xx]!=0)&(labels[yy,xx]!=lab)&(sizes[labels[yy,xx]]>100)),(name,i,'neighbor')
        assert xx.start>0 and yy.start>0 and xx.stop<im.width and yy.stop<im.height,(name,i,'source edge')
        # Feet, not the raised blade/wing tips, determine the ground anchor.
        rgba=np.array(im)[yy,xx]
        lower=rgba[int(rgba.shape[0]*.72):]
        if not name.startswith('drake'):
            r,g,b,a=[lower[:,:,j].astype(float) for j in range(4)]
            boots=(a>=128)&(r>g*1.08)&(g>b*1.13)&(r<180)
            rowcounts=boots.sum(axis=1)
            occupied=np.where(rowcounts>=max(3,int(rowcounts.max()*.2)))[0]
            floor=yy.start+int(rgba.shape[0]*.72)+int(occupied[-1])+1 if len(occupied) else yy.stop
        else:floor=yy.stop
        # Ground contact's midpoint is independent of an overhead weapon or spread wings.
        strip=alpha[max(yy.start,floor-12):floor,xx]
        contact=np.where(strip>=128)[1]
        if not name.startswith('drake'):
            pixels=np.array(im)[max(yy.start,floor-12):floor,xx].astype(float)
            brown=(pixels[:,:,3]>=128)&(pixels[:,:,0]>pixels[:,:,1]*1.08)&(pixels[:,:,1]>pixels[:,:,2]*1.13)&(pixels[:,:,0]<180)
            if brown.any():contact=np.where(brown)[1]
        foot=[xx.start+(int(contact.min())+int(contact.max())+1)/2,floor]
        x,y=max(0,xx.start-2),max(0,yy.start-2)
        x2,y2=min(im.width,xx.stop+2),min(im.height,yy.stop+2)
        head_mask=alpha[yy.start:yy.start+max(1,int((yy.stop-yy.start)*.22)),xx]>=128
        head_x=xx.start+(int(np.where(head_mask)[1].min())+int(np.where(head_mask)[1].max())+1)/2
        frames.append({'head':[head_x,yy.start],'source':f'source/{name}.png','sourcePose':i,'cell':[x,y,x2-x,y2-y], 'bounds':[xx.start,yy.start,xx.stop,yy.stop], 'foot':foot,'scale':scale})
    SOURCES[name]=frames
    return frames

items=[]
for mob,size,floor in [('goblin',128,111),('orc',192,171),('drake',192,171)]:
    scales={'goblin':{'idle':.408,'walk':.432,'attack':.44,'damage':.332},'orc':{'idle':.65,'walk':.677,'attack':.70,'damage':.50},'drake':{'idle':.521,'walk':.536,'attack':.70,'damage':.449}}[mob]
    idle=poses(mob+('-idle-breath' if mob=='drake' else '-idle'),32,scales['idle'])
    walk=poses(mob+'-walk',32,scales['walk'])
    attack=poses(mob+'-attack',32,scales['attack'])
    damage=poses(mob+'-damage',16,scales['damage'])
    if mob=='goblin':
        correction=poses('goblin-walk-corrected',8,.213)
        for row,new in [(3,0),(7,4)]:walk[row*4:row*4+4]=correction[new:new+4]
        correction=poses('goblin-attack-corrected',8,.35)
        for row,new in [(0,0),(7,4)]:attack[row*4:row*4+4]=correction[new:new+4]
    if mob=='orc':
        correction=poses('orc-walk-corrected',8,.37)
        for row,new in [(2,0),(6,4)]:walk[row*4:row*4+4]=correction[new:new+4]
        correction=poses('orc-attack-corrected',8,.37)
        attack[28:32]=correction[4:8]
    if mob=='drake':
        correction=poses('drake-attack-rear',8,.34)
        for row,new in [(3,0),(5,4)]:attack[row*4:row*4+4]=correction[new:new+4]
    frames=[]
    for row,d in enumerate(DIRECTIONS):
        damage_start=(row//2)*4+(row%2)*2
        for action,selected in [('idle',idle[row*4:row*4+4]),('walk',walk[row*4:row*4+4]),('attack',attack[row*4:row*4+4]),('hurt',[damage[damage_start]]),('down',[damage[damage_start+1]])]:
            for frame,f in enumerate(selected):frames.append({**f,'direction':d,'row':row,'action':action,'frame':frame})
    items.append({'id':mob,'size':size,'anchor':[size//2,floor],'pixelScale':.32,'frames':frames})
(ROOT/'packing.json').write_text(json.dumps(items,indent=2)+'\n')
print('Measured',sum(len(i['frames'])for i in items),'selected poses across all 8 facings; complete source actors, fixed scale per action.')
