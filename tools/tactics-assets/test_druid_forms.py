"""Druid forms retain approved anatomy, real compass views and native ground anchors."""
import io,json,unittest,hashlib
import numpy as np
from PIL import Image
from arcpix.out import PACK
from parts.druid_forms import FORMS,render,frame_image
class DruidFormsTests(unittest.TestCase):
 def test_four_sheets_and_manifest_entries_reproduce_byte_for_byte(self):
  manifest=json.loads((PACK/'creatures/manifest.json').read_text())['sprites']
  for form in FORMS:
   atlas,entry=render(form);saved=manifest['druid_'+form];self.assertEqual(entry,saved)
   out=io.BytesIO();atlas.save(out,format='PNG',optimize=True)
   self.assertEqual((PACK/'creatures'/entry['file']).read_bytes(),out.getvalue(),form)
 def test_every_facing_and_action_is_real_and_idle_feet_keep_the_approved_ground_anchor(self):
  manifest=json.loads((PACK/'creatures/manifest.json').read_text())['sprites']
  for form,(source,_,_) in FORMS.items():
   e=manifest['druid_'+form];s=manifest[source];w,h=e['frame']['width'],e['frame']['height'];atlas=Image.open(PACK/'creatures'/e['file']).convert('RGBA');reference=Image.open(PACK/'creatures'/s['file']).convert('RGBA')
   self.assertEqual(8,len(e['directions']));self.assertEqual(s['frame']['anchor'],e['frame']['anchor']);self.assertEqual(6,e['columns'])
   silhouettes=[]
   for row in range(8):
    for name,anim in e['anims'].items():
     for f in anim['frames']:
      x=f%e['columns']*w;y=(row*e['rowsPerDirection']+f//e['columns'])*h
      a=np.array(atlas.crop((x,y,x+w,y+h)))[:,:,3]
      self.assertTrue(a.any(),(form,row,name,f))
      if name in ('idle','walk'):self.assertEqual(e['frame']['anchor'][1],np.where(a>0)[0].max(),(form,row,f))
    f=e['anims']['idle']['frames'][0];x=f%e['columns']*w;y=(row*e['rowsPerDirection']+f//e['columns'])*h
    silhouettes.append(hashlib.sha256(atlas.crop((x,y,x+w,y+h)).tobytes()).digest())
   self.assertEqual(8,len(set(silhouettes)),form)
 def test_elder_forms_are_larger_and_all_four_keep_distinct_visible_materials(self):
  m=json.loads((PACK/'creatures/manifest.json').read_text())['sprites']
  self.assertGreater(m['druid_dire_wolf']['pixelScale'],m['druid_wolf']['pixelScale'])
  self.assertGreater(m['druid_elder_bear']['pixelScale'],m['druid_bear']['pixelScale'])
  self.assertEqual(4,len({hashlib.sha256((PACK/'creatures'/m['druid_'+f]['file']).read_bytes()).digest() for f in FORMS}))
  for f in FORMS:self.assertFalse(m['druid_'+f]['unlit'])

 def test_moving_extremities_do_not_slide_or_reshape_the_neck_decoration(self):
  # A tail/leg outside the neck must not reposition the fur or plant material.
  # Attack poses skip ground correction, isolating attachment from foot placement.
  manifest=json.loads((PACK/'creatures/manifest.json').read_text())['sprites']
  for form,(source,_,_) in FORMS.items():
   e=manifest[source];sheet=Image.open(PACK/'creatures'/e['file']).convert('RGBA');w,h=e['frame']['width'],e['frame']['height'];frame=12;direction=2
   x=frame%e['columns']*w;y=(direction*e['rowsPerDirection']+frame//e['columns'])*h
   body=sheet.crop((x,y,x+w,y+h));extended=body.copy();extended.putpixel((w-1,0),(70,60,50,255))
   ordinary=frame_image(body,form,direction,frame);moved=frame_image(extended,form,direction,frame)
   self.assertEqual(ordinary.crop((0,1,w-1,h)).tobytes(),moved.crop((0,1,w-1,h)).tobytes(),form)
