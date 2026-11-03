"""Palette-lock eight poses, align each rest view once, then animate without frame recentering."""
import base64,json,runpy
from pathlib import Path
import numpy as np
from PIL import Image
R=Path(__file__).resolve().parent;HELP=R.parent/'animal-concepts/directional';MAPPED=runpy.run_path(str(HELP/'canonicalize.py'))['mapped']
DETAIL={'mule':'Four natural hooved legs in a slow four-beat walk. Both panniers, saddle, rolls and straps remain fixed to the torso; tail follows its root. No rider.','goat':'Four cloven-hoofed legs take small coordinated steps. Horns remain fixed to the head and short tail follows its root.','fox':'Four slim canid legs walk lightly. Low narrow torso, fox muzzle and white-tipped bushy tail remain consistent. No extra tail.','duck':'Paddle smoothly in place in a swimming pose. Both feet remain hidden beneath the body, folded wings do not flap, head stays steady. No water or ripples drawn.','crow':'Two bird legs make a short coordinated hop in place and settle. Wings stay mostly folded, using only a tiny balance flick. No flight or extra legs.','dog':'Four canid legs take a natural alternating walk. Friendly small farm dog with steady muzzle and attached following tail, not a giant wolf.','sheep':'Four short cloven-hoofed legs take small measured steps. Wool stays attached to a single solid barrel-shaped body; no shedding or growing fleece.'}
def main():
 layout=json.loads((R/'layout.json').read_text());rows=[];qs=[];public=[]
 for i,c in enumerate(layout['families']):
  k=c['id'];raw=R/k/'raw-rotations'
  if not all((raw/(d+'.png')).exists() for d in layout['directions']):continue
  if k=='goat' and (c['source_file']!='goat/full-body.png' or not (R/'goat/approved-rotations.json').exists()):continue
  source=Image.open(R/k/'input-poses/south.png').convert('RGBA');a=np.array(source);colors,counts=np.unique(a[a[:,:,3]>127,:3],axis=0,return_counts=True);inds=np.argsort(-counts)[:32];palette=['#%02x%02x%02x'%tuple(colors[n]) for n in inds]
  side=c['canvas'][0]+32;anchor=[c['anchor'][0]+16,c['anchor'][1]+16];row={**c,'input_canvas':c['canvas'],'canvas':[side,side],'anchor':anchor,'palette':palette,'pose_translations':{}};poses=R/'canonical'/k/'poses';poses.mkdir(parents=True,exist_ok=True)
  for direction in layout['directions']:
   p=R/k/'input-poses/south.png' if direction=='south' else raw/(direction+'.png')
   override=R/k/'pose-overrides'/(direction+'.png')
   if override.exists():p=override
   im=Image.open(p).convert('RGBA');bb=im.getbbox();dx=(side-im.width)//2;dy=anchor[1]-bb[3]+1
   out=Image.new('RGBA',(side,side));out.alpha_composite(im,(dx,dy));out=MAPPED(out,palette);out.save(poses/(direction+'.png'));row['pose_translations'][direction]=[dx,dy]
   src=base64.b64encode((poses/(direction+'.png')).read_bytes()).decode()
   actions=[('idle',4),('move',8)]+([('flap',4)] if k=='crow' else [])
   for action,n in actions:
    if action=='idle':motion='One tiny stationary weight adjustment, then return. Keep every support foot planted; silhouette and body size nearly unchanged. '+('A small peck toward the ground then look up again, two feet planted. ' if k=='crow' else 'A restrained one-pixel floating bob, feet hidden, no water drawn. ' if k=='duck' else '')
    elif action=='flap':motion='One short grounded wing stretch and flap then fold both wings again. Two legs remain planted, feathers attached. Do not fly away or change body size. '
    else:motion='One complete locomotion cycle IN PLACE, returning to the start. '+DETAIL[k]+' '
    desc=motion+'Facing '+direction+' throughout. '+('Back remains toward viewer. ' if direction.startswith('north') else '')+'Keep exact species anatomy, attached body parts, coat, material palette, pixel clusters and physical scale. No turning toward camera, drifting across canvas, extra limbs, steam, particles, rings, symbols, effects, background, shadows or new equipment. All tips stay within transparent margins.'
    args={'description':desc,'frame_count':n,'first_frame_base64':src,'last_frame_base64':src,'no_background':True,'direction':direction,'view':'low top-down','seed':200000+i*100+layout['directions'].index(direction)*3+{'idle':0,'move':1,'flap':2}[action], 'subject_description':{'mule':'A small grey pack mule with four hooved legs, long ears, saddle, bedroll and two woven grain panniers.','goat':'A small brown-and-white farm goat with four cloven-hoofed legs, short horns and beard.','fox':'A small red fox with four canid legs, white chest and white-tipped bushy tail.','duck':'A small swimming mallard duck with green head, yellow bill, brown chest, folded feathered wings and hidden feet.','crow':'A small charcoal-feathered crow standing on two bird legs, with a short beak, folded wings and one feathered tail.','dog':'A small brown farm dog with four canid legs, floppy ears and one attached tail.','sheep':'A small cream woolly sheep with a beige face, two ears and four short cloven-hoofed legs.'}[k], 'initial_pose':'Stationary, facing '+direction+', with the original anatomy and attached equipment.'}
    key=k+'/animations/'+direction+'/'+action;q={'key':key,'name':'animate_image_pixminimax','arguments':args};qs.append(q);public.append({**q,'arguments':{x:y for x,y in args.items() if 'base64' not in x}})
  rows.append(row)
 (R/'canonical-layout.json').write_text(json.dumps({'families':rows,'directions':layout['directions'],'pixel_scale':.5},indent=2)+'\n');(R/'animation-requests.json').write_text(json.dumps(public,indent=2)+'\n')
 Path('/workspace/requests-overworld-animations-all.json').write_text(json.dumps(qs))
 new=[q for q in qs if not (R/'raw-animations'/(q['key']+'-receipt.json')).exists()];Path('/workspace/requests-overworld-animations-new.json').write_text(json.dumps(new));print('Prepared',len(qs),'loops for',[c['id'] for c in rows],'; new jobs:',len(new))
if __name__=='__main__':main()
