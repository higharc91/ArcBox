"""Pad reviewed specimens once, then obtain real rear, side and diagonal views."""
import base64,json
from pathlib import Path
from PIL import Image
R=Path(__file__).resolve().parent
CHOICES={'mule':'native.png','goat':'full-body.png','fox':'base/frame-000.png','duck':'swim-front.png','crow':'base/frame-000.png','dog':'base/frame-000.png','sheep':'native.png'}
DIRS=['south','south-west','west','north-west','north','north-east','east','south-east']
def main():
 plan=json.loads((R/'plan.json').read_text());qs=[];rows=[]
 for i,c in enumerate(plan['families']):
  k=c['id'];source=R/k/CHOICES[k]
  if not source.exists():continue
  im=Image.open(source).convert('RGBA');bb=im.getbbox();side=(max(im.size)+24+3)//4*4;ax=side//2;ay=side-12;dx=(side-im.width)//2;dy=ay-bb[3]+1
  canvas=Image.new('RGBA',(side,side));canvas.alpha_composite(im,(dx,dy));folder=R/k/'input-poses';folder.mkdir(exist_ok=True);canvas.save(folder/'south.png');src=base64.b64encode((folder/'south.png').read_bytes()).decode()
  note=c['notes']+' '+('Swimming pose with feet hidden, no actual water or ripples drawn.' if k=='duck' else '')
  desc='The SAME small '+k+' in eight low-top-down directions. '+note+' Preserve exact physical size, material colors, pixel density and anatomy. North shows the back and rump, never a front face. Real left/right and diagonals. No scenery, particles, equipment changes or extra limbs.'
  if k in ['crow','duck']:
   kind='objects';tool='create_object_pro_flash';args={'description':desc,'first_frame_base64':src,'first_frame_direction':'south','n_directions':8,'view':'low top-down','name':'Wardenflame overworld '+k,'seed':197000+i}
  else:
   kind='characters';tool='create_character';args={'description':desc,'reference_image_base64':src,'mode':'v3','n_directions':8,'view':'low top-down','body_type':'quadruped','template':'horse' if k in ['mule','goat','sheep'] else 'dog','name':'Wardenflame overworld '+k}
  qs.append({'key':k,'kind':kind,'name':tool,'arguments':args});rows.append({**c,'canvas':[side,side],'anchor':[ax,ay],'source_file':str(source.relative_to(R)),'translation':[dx,dy],'pixel_scale':.5,'directions':DIRS})
 (R/'layout.json').write_text(json.dumps({'families':rows,'directions':DIRS,'pixel_scale':.5,'reference_human_world_height':37},indent=2)+'\n');Path('/workspace/requests-overworld-rotations.json').write_text(json.dumps(qs));print('Ready for rotation:',[c['id'] for c in rows])
if __name__=='__main__':main()
