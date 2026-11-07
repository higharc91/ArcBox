"""Druid-only forms built on the approved eight-direction creature anatomy and motion.
Natural coats, attached fur/bark mantles and small living-green details retain the game's
clustered pixels and lighting. Source atlases and all non-Druid creatures stay untouched.
"""
from pathlib import Path
import copy,json
import numpy as np
from PIL import Image,ImageDraw
from arcpix.out import PACK,save,write_json
from .druid_form_poses import POSE_NECK
FORMS={
 'wolf':('wolf_grey','beasts.wolf.druid',.4),
 'dire_wolf':('wolf_dire','beasts.wolf.druid_dire',.5),
 'bear':('bear_brown','beasts.bear.druid',.4),
 'elder_bear':('bear_brown','beasts.bear.druid_elder',.5)}
LEAF=['#263c2d','#39563c','#567548','#88a361','#c5d19a']
BARK=['#302c2b','#4a3e31','#71583b','#a58758','#d0b681']

# Small, continuous neck ties follow each facing. The original fur is the ruff;
# no brightness threshold/window repaints a moving piece of the coat.
VINES=(
 ((-7,-1),(0,3),(7,-1)), ((-5,-5),(0,0),(3,6)),
 ((-3,-7),(0,0),(2,6)), ((-4,-4),(0,0),(5,3)),
 ((-7,0),(0,3),(7,0)), ((-5,3),(0,0),(4,-4)),
 ((-2,6),(0,0),(3,-7)), ((-3,6),(0,0),(5,-5)))

def leaf_sprig(pen,x,y,side=1):
 # One attached sprig: broad leaf planes rather than a ring of competing spikes.
 pen.line([(x,y-2),(x,y+5)],fill=LEAF[0],width=2)
 pen.polygon([(x,y),(x-4*side,y-4),(x-5*side,y-1),(x-1*side,y+3)],fill=LEAF[0])
 pen.polygon([(x,y),(x-3*side,y-2),(x-3*side,y),(x,y+2)],fill=LEAF[2])
 pen.line([(x,y+1),(x-3*side,y-2)],fill=LEAF[3])
 pen.polygon([(x,y+2),(x+3*side,y-2),(x+5*side,y),(x+1*side,y+5)],fill=LEAF[0])
 pen.polygon([(x+1*side,y+2),(x+3*side,y),(x+3*side,y+2),(x+1*side,y+4)],fill=LEAF[2])

def bark_plate(pen,x,y):
 pen.polygon([(x-4,y+3),(x-4,y-3),(x+1,y-6),(x+4,y-3),(x+4,y+3),(x,y+6)],fill=BARK[0])
 pen.polygon([(x-2,y+2),(x-2,y-2),(x+1,y-4),(x+2,y-2),(x+2,y+2),(x,y+4)],fill=BARK[2])
 pen.line([(x-2,y-2),(x+1,y-4),(x+2,y-2)],fill=BARK[3])
 pen.line([(x,y-1),(x,y+2)],fill=BARK[1])

def frame_image(source,form,direction,index):
 im=source.copy().convert('RGBA');offset=0
 if index<12:
  ys=np.where(np.array(im.getchannel('A'))>0)[0]
  if len(ys):
   offset=im.height-5-int(ys.max())
   if offset:
    aligned=Image.new('RGBA',im.size);aligned.paste(im,(0,offset));im=aligned
 if not im.getbbox():return im
 bear='bear' in form;cx,cy=POSE_NECK['bear' if bear else 'wolf'][direction][index];cy+=offset
 if not bear:
  # Retain the authored coat/ruff and change only the source's red/amber eyes.
  a=np.array(im);eyes=(a[:,:,3]>0)&(a[:,:,0]>a[:,:,1]*1.15)&(a[:,:,0]>a[:,:,2]*1.15)&(a[:,:,0]>90)
  for y,x in zip(*np.where(eyes)):
   value=a[y,x,:3].max()/255;a[y,x,:3]=np.array([125,185,114])*max(.45,value)
  im=Image.fromarray(a)
 overlay=Image.new('RGBA',im.size);pen=ImageDraw.Draw(overlay)
 if bear:
  # Front-facing shoulder details sit beside the head, never across the face.
  if direction in (0,4):sites=[(cx-15,cy-2),(cx+15,cy-2)]
  else:sites=[(cx,cy)]
  if form=='elder_bear':
   if direction not in (0,4):
    back=1 if direction in (1,2,3) else -1
    sites.append((cx+back*9,cy+(-3 if direction in (1,2,6,7) else 5)))
   for x,y in sites:bark_plate(pen,x,y)
  # A little living green belongs to the shoulder rather than obscuring its fur.
  for x,y in sites:leaf_sprig(pen,x,y+5,1 if direction<4 else -1)
 else:
  points=[(cx+x,cy+y) for x,y in VINES[direction]]
  pen.line(points,fill=LEAF[0],width=3);pen.line(points,fill=LEAF[2],width=1)
  leaf_sprig(pen,*points[1],1 if direction<4 else -1)
 # No silhouette clipping: the same whole sprig/plate survives every gait frame.
 # Pose keys move it with the neck; moving legs/tails cannot reshape the material.
 return Image.alpha_composite(im,overlay)

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
  provenance='Pose-keyed Druid leaf ties and shoulder bark on the approved '+source_name+' anatomy and eight-direction motion. No source creature atlas is modified.')
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
