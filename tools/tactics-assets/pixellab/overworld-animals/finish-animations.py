"""Normalize and validate every small overworld animal loop against fixed poses and palettes."""
import json,runpy
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import label,distance_transform_edt
R=Path(__file__).resolve().parent;MAPPED=runpy.run_path(str(R.parent/'animal-concepts/directional/canonicalize.py'))['mapped']
def clean(im):
 a=np.array(im);labs,n=label(a[:,:,3]>0,np.ones((3,3)));counts=np.bincount(labs.ravel());counts[0]=0
 if n<2:return im,0
 body=counts.argmax();gap=distance_transform_edt(labs!=body);remove=np.zeros(labs.shape,dtype=bool)
 for i in range(1,n+1):
  if i==body:continue
  mask=labs==i
  if counts[i]<max(4,counts[body]*.025) and gap[mask].min()>3:remove|=mask
 a[remove]=0;return Image.fromarray(a),int(remove.sum())
def main():
 data=json.loads((R/'canonical-layout.json').read_text());loops=[];pending=[];errors=[]
 for c in data['families']:
  k=c['id'];palette={tuple(bytes.fromhex(h[1:])) for h in c['palette']}
  for d in data['directions']:
   pose=clean(Image.open(R/'canonical'/k/'poses'/(d+'.png')).convert('RGBA'))[0];pose.save(R/'canonical'/k/'poses'/(d+'.png'))
   for action,n in [('idle',4),('move',8)]+([('flap',4)] if k=='crow' else []):
    key=k+'/animations/'+d+'/'+action;raw=R/('authored-animations' if k=='duck' else 'raw-animations')/key;ps=sorted(raw.glob('frame-*.png'))
    if len(ps)!=n+1:pending.append(key);continue
    out=R/'canonical'/key;out.mkdir(parents=True,exist_ok=True);images=[];removed=0
    for i,p in enumerate(ps):
     im=Image.open(p).convert('RGBA')
     if list(im.size)!=c['canvas']:errors.append({'key':key,'frame':i,'error':'Unexpected size','size':list(im.size)});continue
     im=pose if i in [0,n] else MAPPED(im,c['palette']);im,count=clean(im);removed+=count;im.save(out/p.name);images.append(im)
     bb=im.getbbox()
     if bb is None or bb[0]==0 or bb[1]==0 or bb[2]==im.width or bb[3]==im.height:errors.append({'key':key,'frame':i,'error':'Empty or clipped','bounds':bb})
     a=np.array(im)
     if not set(np.unique(a[:,:,3]))<={0,255}:errors.append({'key':key,'error':'Nonbinary alpha'})
     if not {tuple(x) for x in a[a[:,:,3]>0,:3]}<=palette:errors.append({'key':key,'error':'Palette mismatch'})
    if len(images)!=n+1:continue
    if images[0].tobytes()!=images[-1].tobytes():errors.append({'key':key,'error':'Loop seam'})
    if images[0].tobytes()!=pose.tobytes():errors.append({'key':key,'error':'Opening pose mismatch'})
    if len({im.tobytes() for im in images[:-1]})<2:errors.append({'key':key,'error':'Static animation'})
    loops.append({'key':key,'frames':n,'downloaded_frames':n+1,'canvas':c['canvas'],'anchor':c['anchor'],'pixel_scale':.5,'method':'authored fixed-pose buoyancy' if k=='duck' else 'PixelLab motion with palette and endpoint cleanup','removed_stray_pixels':removed,'frame_duration_ms':180 if action=='idle' else 110})
 result={'status':'complete' if len(data['families'])==7 and not pending and not errors else 'in progress','expected_loops':120,'complete_loops':len(loops),'pending':pending,'errors':errors,'policy':'Exact rest endpoint, original pixels and fixed palette; one anchor for all facings and frames, no frame recentering; duplicate closing omitted from loop playback.','loops':loops}
 (R/'animation-validation.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:v for k,v in result.items() if k not in ['loops','pending']}));print('Pending loops:',len(pending))
if __name__=='__main__':main()
