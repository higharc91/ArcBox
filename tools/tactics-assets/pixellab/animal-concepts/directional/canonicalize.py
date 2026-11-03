"""Pad all native views consistently and reuse validated diagonal loops without resizing."""
import json,shutil
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parent;BASE=ROOT.parent
DIRS=['south','south-west','west','north-west','north','north-east','east','south-east']

def mapped(im,palette,fx=False):
 a=np.array(im.convert('RGBA'));h,w=a.shape[:2];target=np.array([tuple(bytes.fromhex(v[1:])) for v in palette],dtype=np.int32);rgb,inv=np.unique(a[:,:,:3].reshape(-1,3),axis=0,return_inverse=True);near=((rgb[:,None,:].astype(np.int32)-target[None,:,:])**2).sum(2).argmin(1);a[:,:,:3]=target[near][inv].reshape(h,w,3);a[:,:,3]=(np.round(a[:,:,3].astype(float)/64)*64).clip(0,255).astype(np.uint8) if fx else np.where(a[:,:,3]>=128,255,0);a[a[:,:,3]==0]=0;return Image.fromarray(a)

def place(im,size,offset):
 bb=im.getbbox();dx,dy=offset
 if bb[0]+dx<0 or bb[1]+dy<0 or bb[2]+dx>size[0] or bb[3]+dy>size[1]:raise ValueError(('placement would crop',im.size,bb,size,offset))
 result=Image.new('RGBA',tuple(size));result.alpha_composite(im,(dx,dy));return result

def main():
 rows=json.loads((ROOT/'layout.json').read_text())['families'];palettes={c['id']:c['palette'] for c in json.loads((BASE/'normalization.json').read_text())['creatures']};output=[]
 for c in rows:
  key=c['id'];raw=ROOT/key/'raw-rotations';ready=key=='wisp' or all((raw/(d+'.png')).exists() for d in DIRS)
  if not ready:continue
  inputs=ROOT/key/'input-poses';inputs.mkdir(exist_ok=True)
  for d in ['south','south-east']:
   p=ROOT/key/'poses'/(d+'.png')
   if not (inputs/(d+'.png')).exists():shutil.copyfile(p,inputs/(d+'.png'))
  dims=[Image.open(raw/(d+'.png')).size for d in DIRS] if key!='wisp' else [tuple(c['canvas'])]
  side=min(256,max(c['canvas'][0]+32,max(max(v) for v in dims)+16));side=(side+3)//4*4;pad=[(side-c['canvas'][0])//2,(side-c['canvas'][1])//2];anchor=[c['anchor'][0]+pad[0],c['anchor'][1]+pad[1]]
  row={**c,'input_canvas':c['canvas'],'input_anchor':c['anchor'],'canvas':[side,side],'anchor':anchor,'padding_from_input':pad,'pose_warnings':[]};dest=ROOT/'canonical'/key/'poses';dest.mkdir(parents=True,exist_ok=True)
  for d in DIRS:
   p=inputs/('south-east.png' if key=='wisp' else d+'.png') if key=='wisp' or d in ('south','south-east') else raw/(d+'.png')
   im=Image.open(p).convert('RGBA');bb=im.getbbox()
   if bb[0]==0 or bb[1]==0 or bb[2]==im.width or bb[3]==im.height:row['pose_warnings'].append({'direction':d,'type':'native edge contact','bounds':list(bb)})
   im=place(im,(side,side),((side-im.width)//2,(side-im.height)//2))
   repair=ROOT/key/'north-tip-repair'/'accepted.png'
   if key=='bat' and d=='north' and repair.exists():
    im=Image.open(repair).convert('RGBA')
    row['pose_warnings']=[w for w in row['pose_warnings'] if w['direction']!=d]
   mapped(im,palettes[key],key=='wisp').save(dest/(d+'.png'))
  # Existing diagonal loops retain their exact silhouettes and pixel scale.
  dx,dy=c['translation_from_pilot'];dx+=pad[0];dy+=pad[1]
  for d in DIRS if key=='wisp' else ['south-east']:
   for action in ['idle','move']:
    target=ROOT/'canonical'/key/'animations'/d/action;target.mkdir(parents=True,exist_ok=True)
    for p in sorted((BASE/key/action).glob('frame-*.png')):
     im=place(Image.open(p).convert('RGBA'),(side,side),(dx,dy));mapped(im,palettes[key],key=='wisp').save(target/p.name)
  output.append(row)
 (ROOT/'canonical-layout.json').write_text(json.dumps({'status':'Eight-view poses; animation extension in progress.','directions':DIRS,'pixel_scale':.5,'families':output},indent=2)+'\n');print('Canonicalized',len(output),'families; reused their validated south-east loops.');print('Pose warnings:',[(c['id'],c['pose_warnings']) for c in output if c['pose_warnings']])
if __name__=='__main__':main()
