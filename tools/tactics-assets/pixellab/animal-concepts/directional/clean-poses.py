"""Remove tiny disconnected generation debris; preserve whole limbs and original source files."""
import json
from pathlib import Path
import numpy as np
from scipy.ndimage import label,distance_transform_edt
from PIL import Image
R=Path(__file__).resolve().parent
def clean(im):
 a=np.array(im.convert('RGBA'));labs,n=label(a[:,:,3]>0,np.ones((3,3)))
 if n<2:return im,0
 counts=np.bincount(labs.ravel());counts[0]=0;main=counts.argmax();near=distance_transform_edt(labs!=main);remove=np.zeros(labs.shape,dtype=bool)
 for i in range(1,n+1):
  if i==main:continue
  m=labs==i;size=counts[i];gap=float(near[m].min())
  if size<4 or (size<25 and gap>2) or (size<counts[main]*.03 and gap>7):remove|=m
 n=int(remove.sum());a[remove]=0;return Image.fromarray(a),n

def main():
 rows=[]
 for p in sorted((R/'canonical').glob('*/poses/*.png')):
  if p.parts[-3]=='wisp':continue
  backup=R/'pre-cleanup'/p.relative_to(R/'canonical');backup.parent.mkdir(parents=True,exist_ok=True)
  if not backup.exists():backup.write_bytes(p.read_bytes())
  im,n=clean(Image.open(backup).convert('RGBA'));im.save(p)
  if n:rows.append({'file':str(p.relative_to(R)),'removed_disconnected_pixels':n})
 (R/'pose-cleanup.json').write_text(json.dumps({'policy':'Small disconnected specks only; no rescaling, recentering or body edits. Raw originals preserved.','changes':rows},indent=2)+'\n');print('Cleaned',len(rows),'poses;',sum(c['removed_disconnected_pixels'] for c in rows),'stray pixels removed.')
if __name__=='__main__':main()
