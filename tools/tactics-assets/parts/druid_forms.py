"""Druid-only forms built on the approved eight-direction creature anatomy and motion.
Natural coats, attached fur/bark mantles and small living-green details retain the game's
clustered pixels and lighting. Source atlases and all non-Druid creatures stay untouched.
"""
from pathlib import Path
import copy,json,math
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
from arcpix.out import PACK,save,write_json
FORMS={
 'wolf':('wolf_grey','beasts.wolf.druid',.4),
 'dire_wolf':('wolf_dire','beasts.wolf.druid_dire',.5),
 'bear':('bear_brown','beasts.bear.druid',.4),
 'elder_bear':('bear_brown','beasts.bear.druid_elder',.5)}
# Where the shoulder lies in each approved facing, as a fraction of the posed body bounds.
WOLF_NECK=[(.5,.50),(.40,.32),(.35,.31),(.42,.29),(.5,.27),(.58,.29),(.65,.31),(.60,.32)]
BEAR_NECK=[(.5,.32),(.40,.30),(.37,.28),(.42,.25),(.5,.25),(.58,.25),(.63,.28),(.60,.30)]
LEAF=['#263c2d','#39563c','#567548','#88a361','#c5d19a']
BARK=['#302c2b','#4a3e31','#71583b','#a58758','#d0b681']
FUR=['#34313b','#5b5862','#89858d','#b8b5ad','#e3ddc6']

def frame_image(source,form,direction,index):
 im=source.copy().convert('RGBA')
 # Imported idle/walk frames can drift by several pixels. Keep the grounded foot at the
 # runtime anchor, retaining the authored airborne attack frames unchanged.
 if index<12:
  alpha=np.array(im.getchannel('A'));ys=np.where(alpha>0)[0]
  if len(ys):
   offset=im.height-5-int(ys.max())
   if offset:
    aligned=Image.new('RGBA',im.size);aligned.paste(im,(0,offset));im=aligned
 a=np.array(im);mask=a[:,:,3]>0
 if not mask.any():return im
 ys,xs=np.where(mask);left,top,right,bottom=xs.min(),ys.min(),xs.max(),ys.max();bw,bh=right-left+1,bottom-top+1
 bear='bear' in form;elder=form in ('dire_wolf','elder_bear');neck=(BEAR_NECK if bear else WOLF_NECK)[direction]
 cx,cy=left+neck[0]*bw,top+neck[1]*bh
 # Amber/red eyes become a restrained living green, retaining each original shade.
 if not bear:
  eyes=mask & (a[:,:,0]>a[:,:,1]*1.15) & (a[:,:,0]>a[:,:,2]*1.15) & (a[:,:,0]>90)
  for y,x in zip(*np.where(eyes)):
   value=a[y,x,:3].max()/255
   a[y,x,:3]=np.array([125,185,114])*max(.45,value)
  im=Image.fromarray(a)
 # Elder forms have broad grizzled ruffs, retaining the original clustered shade planes.
 if elder:
  yy,xx=np.indices(mask.shape);rx=23 if bear else 14;ry=14 if bear else 10
  ruff=mask & (((xx-cx)/rx)**2+((yy-cy)/ry)**2<1)
  lum=np.mean(a[:,:,:3],axis=2)
  shade=np.clip(((lum-(28 if bear else 16))/(36 if bear else 22)).astype(int),0,4)
  colors=np.array([[49,44,43],[83,74,64],[124,111,91],[166,150,121],[208,194,157]] if bear else [[39,38,46],[74,72,83],[117,116,128],[163,164,172],[211,214,205]],dtype='uint8')
  a[ruff,:3]=colors[shade[ruff]]
  im=Image.fromarray(a)
 # Materials stay broad and directional: a fur ruff on the dire wolf; bark plates on bears.
 overlay=Image.new('RGBA',im.size);pen=ImageDraw.Draw(overlay)
 palette=BARK if bear else FUR if elder else LEAF
 radius=(18 if form=='elder_bear' else 14 if bear else 10 if elder else 6)
 # Mantle wraps the shoulders instead of a detached aura. Each leaf/fur tuft has a dark
 # underside, a broad middle plane and one top-left highlight, like the source creature.
 count=11 if elder else 7
 for j in range(count):
  angle=math.tau*j/count
  x=cx+math.cos(angle)*radius;y=cy+math.sin(angle)*radius*.42
  reach=(6 if elder else 4)+(j%3==0)
  side=1 if math.cos(angle)>=0 else -1
  poly=[(round(x-3),round(y-2)),(round(x+2),round(y-3)),(round(x+side*reach),round(y+reach*.65)),(round(x-2),round(y+2))]
  pen.polygon(poly,fill=palette[0]);pen.polygon([(px,py-1) for px,py in poly[:-1]],fill=palette[2])
  pen.line([(round(x-1),round(y-2)),(round(x+side*(reach-1)),round(y+reach*.45))],fill=palette[3],width=1)
  pen.point((round(x-1),round(y-2)),fill=palette[4])
 # Elder bear carries a broad living-bark ridge with inset moss, rather than a scaled-up recolour.
 if form=='elder_bear':
  for j in (-1,0,1):
   x=round(cx+j*9);y=round(cy-4+abs(j)*2)
   pen.polygon([(x-5,y+3),(x-4,y-6),(x+3,y-9),(x+6,y+1),(x,y+7)],fill=BARK[0])
   pen.polygon([(x-3,y+1),(x-2,y-5),(x+2,y-6),(x+4,y),(x,y+4)],fill=BARK[2])
   pen.line([(x-2,y-4),(x+1,y-5),(x+2,y+1)],fill=BARK[4],width=1)
   pen.line([(x-2,y+1),(x,y+3),(x+3,y+1)],fill=LEAF[2],width=2)
 # Living leaf ties distinguish the caster from a hostile animal without turning the whole coat green.
 for side in (-1,1):
  x=round(cx+side*(radius*.6));y=round(cy+4)
  pen.polygon([(x,y-3),(x+side*4,y),(x+side*2,y+6),(x-1,y+2)],fill=LEAF[0])
  pen.line([(x,y-1),(x+side*2,y+3)],fill=LEAF[3],width=2)
 # Limit extensions to a three-pixel connected fringe of the posed animal. Blank atlas cells stay blank.
 allowed=Image.fromarray((mask.astype('uint8')*255)).filter(ImageFilter.MaxFilter(7))
 alpha=np.minimum(np.array(overlay.getchannel('A')),np.array(allowed));overlay.putalpha(Image.fromarray(alpha))
 im=Image.alpha_composite(im,overlay)
 return im

def render(form,manifest=None):
 source_name,key,scale=FORMS[form]
 if manifest is None:manifest=json.loads((PACK/'creatures/manifest.json').read_text())['sprites']
 entry=copy.deepcopy(manifest[source_name]);source=Image.open(PACK/'creatures'/entry['file']).convert('RGBA')
 w,h=entry['frame']['width'],entry['frame']['height'];cols=entry['columns'];rows=entry['rowsPerDirection']
 used=sorted({f for anim in entry['anims'].values() for f in anim['frames']})
 targetcols=6;targetrows=(max(used)+targetcols)//targetcols;atlas=Image.new('RGBA',(w*targetcols,h*targetrows*8))
 for d in range(8):
  for frame in used:
   x=frame%cols*w;y=(d*rows+frame//cols)*h
   atlas.paste(frame_image(source.crop((x,y,x+w,y+h)),form,d,frame),((frame%targetcols)*w,(d*targetrows+frame//targetcols)*h))
 entry['anims']['attack']['fps']=100/7  # impact frame 2 lands at the 0.14 s bite/swipe cue
 oldscale=entry['pixelScale'];entry.update(file='druid_'+form+'.png',keys=[key],variant='druid_'+form,pixelScale=scale,columns=targetcols,rowsPerDirection=targetrows,
  footprintWidth=entry['footprintWidth']*scale/oldscale,footprintHeight=entry['footprintHeight']*scale/oldscale,
  provenance='Native Druid mantle, fur and bark work on the approved '+source_name+' anatomy and eight-direction motion. No source creature atlas is modified.')
 return atlas,entry

def build():
 path=PACK/'creatures/manifest.json';manifest=json.loads(path.read_text())
 for form in FORMS:
  atlas,entry=render(form,manifest['sprites']);save(atlas,PACK/'creatures'/entry['file']);manifest['sprites']['druid_'+form]=entry
 write_json(manifest,path)
 # Register variants under their existing species so legacy coat lookups retain their
 # wolf/bear sound family. The existing native creature override supplies each real atlas.
 aliases_path=PACK/'beasts/manifest.json';aliases=json.loads(aliases_path.read_text())
 for form,(_,key,_) in FORMS.items():
  _,species,variant=key.split('.')
  aliases['beasts'][species]['variants'][variant]=aliases['beasts'][species]['variants']['wildshape']
 write_json(aliases,aliases_path)
 # These four sprite keys are presentation data; no statistic, attack, HP or AI field is changed.
 path=PACK/'rules/monsters/wildshape.json';text=path.read_text()
 import re
 for form,(_,key,_) in FORMS.items():
  pattern=r'("id"\s*:\s*"'+form+r'"[\s\S]*?"sprite"\s*:\s*)"[^"]+"'
  text,n=re.subn(pattern,lambda m:m[1]+json.dumps(key),text,count=1);assert n==1
 path.write_text(text)
 print('Exported four distinct Druid forms, preserving all approved source creature atlases.')
if __name__=='__main__':build()
