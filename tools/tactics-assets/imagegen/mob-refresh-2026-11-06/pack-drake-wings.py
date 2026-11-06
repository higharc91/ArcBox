"""Measure complete source poses; one fixed scale from folded idle, never from wing span."""
from pathlib import Path
import json
import numpy as np
from PIL import Image
from scipy.ndimage import label, find_objects
root=Path(__file__).resolve().parent
im=Image.open(root/'drake-winged-attack.png').convert('RGBA')
a=np.array(im.getchannel('A'))>=64
labels,n=label(a,np.ones((3,3)))
assert n==8,n
boxes=find_objects(labels)
# Rows are explicit quarter-height bands; component centres can move when wings unfold.
order=sorted(range(n),key=lambda i:(int((boxes[i][0].stop-1)/(im.height/4)),boxes[i][1].start))
frames=[]
for i in order:
 y,x=boxes[i]
 assert not np.any((labels[y,x]>0)&(labels[y,x]!=i+1))
 lower=a[max(y.start,y.stop-20):y.stop,x.start:x.stop]
 xs=np.where(lower)[1]
 foot=[x.start+(int(xs.min())+int(xs.max()))/2,y.stop]
 frames.append({'cell':[x.start-2,y.start-2,x.stop-x.start+4,y.stop-y.start+4], 'foot':foot,'bounds':[x.start,y.start,x.stop,y.stop]})
scale=88/(boxes[order[0]][0].stop-boxes[order[0]][0].start)
# Absolute sheet coordinates retained so every frame uses exactly the same scale.
(root/'pilots/drake-wings-packing.json').write_text(json.dumps({'source':'drake-winged-attack.png','scale':scale,'size':256,'floor':224,'pixelScale':.32,'frames':frames},indent=2)+'\n')
print('Eight whole actors; constant scale',scale)
