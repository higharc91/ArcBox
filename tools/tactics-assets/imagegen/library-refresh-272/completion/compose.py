"""Review composition only: unchanged exported figures at a shared world zoom."""
from pathlib import Path
import json,hashlib,subprocess,base64,html,sys
from PIL import Image,ImageDraw,ImageFont
REPO=Path(__file__).resolve().parents[5]
BASE=REPO/'content/tactics/base';ART=REPO/'docs/art/mob-sprite-review';ROOT=REPO/'tools/tactics-assets/imagegen'
OUT=ART/'library-completion';OUT.mkdir(exist_ok=True)
BATCHES=['crypt-kin','web-and-hunt','iron-and-fangs','wyrm-triad','gilded-and-rime','autumn-and-veil','spectral-remnant']
DIRECTIONS=['south','south-west','west','north-west','north','north-east','east','south-east']
NAMES=['Front','Front-left','Left','Back-left','Back','Back-right','Right','Front-right']
ZOOM=3
font=lambda size:ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',size)
F=font(22);SMALL=font(17);TITLE=font(30)
manifest=json.loads((BASE/'creatures/manifest.json').read_text())['sprites'];inventory=json.loads((ART/'inventory.json').read_text())['sprites']
items=[];proof=[]
for slug in BATCHES:
 root=ROOT/(slug+'-2026-11-08')
 for role in json.loads((root/'roles.json').read_text()):
  entry=manifest['mob_refresh_'+role['id']];anchor=entry['frame']['anchor'];scale=entry['pixelScale']*ZOOM
  frames={}
  for direction in DIRECTIONS:
   frames[direction]=[]
   for index in range(4):
    image=Image.open(root/'export'/role['id']/'walk'/direction/(str(index)+'.png')).convert('RGBA')
    bounds=image.getbbox();assert bounds
    shown=[192+(bounds[0]-anchor[0])*scale,300+(bounds[1]-anchor[1])*scale,192+(bounds[2]-anchor[0])*scale,300+(bounds[3]-anchor[1])*scale]
    assert shown[0]>=8 and shown[1]>=70 and shown[2]<=376 and shown[3]<=392,(role['id'],direction,index,shown)
    frames[direction].append(image)
  items.append({'role':role,'slug':slug,'root':root,'anchor':anchor,'scale':scale,'frames':frames,'fps':entry['anims']['walk']['fps']})
  proof.append({'key':role['key'],'batch':slug,'walkFacings':8,'distinctKeys':32,'commonWorldZoom':ZOOM,'runtimePixelScale':entry['pixelScale'],'allPanelsFit':True})
assert len(items)==19
# Fighter frame coordinates exactly follow UnitArt.Character and UnitSprite.Source.
t=json.loads((BASE/'tactical/manifest.json').read_text());a=t['frame']['anchor'];size=t['frame']['width'];cols=t['columns'];rows=(t['framesPerDirection']+cols-1)//cols
sheet=Image.open(BASE/'tactical/human_fighter.png').convert('RGBA');fighter={}
# The party atlas has the established two-row/mirrored renderer. Hold its real
# front idle as a standing size reference; never invent six extra player views.
index=t['anims']['idle']['frames'][0];x=index%cols*size;y=(index//cols)*size
reference=sheet.crop((x,y,x+size,y+size));assert reference.getbbox()
for direction in DIRECTIONS:fighter[direction]=[reference]
items.append({'role':{'label':'Fighter · standing reference'},'anchor':a,'scale':t['pixelScale']*ZOOM,'frames':fighter,'fps':t['anims']['walk']['fps']})
def put(canvas,image,anchor,scale,x,y):
 # Uniform nearest-neighbour scale and composition; no pose deformation or pixel painting.
 box=image.getbbox();crop=image.crop(box);crop=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
 canvas.alpha_composite(crop,(round(x+(box[0]-anchor[0])*scale),round(y+(box[1]-anchor[1])*scale)))
# Historical pixels are read from the original inventory and never overwritten.
c=Image.new('RGBA',(1920,2600),'#111923');d=ImageDraw.Draw(c);d.text((24,14),'WARDENFLAME · ALL 19 LIBRARY APPEARANCES',font=TITLE,fill='#eee9da');d.text((24,54),'Before → refreshed · identical world zoom · historical baseline preserved',font=F,fill='#b7c4cc')
for i,item in enumerate(items[:-1]):
 role=item['role'];x=i%3*640;y=100+i//3*355;d.rounded_rectangle((x+4,y+4,x+636,y+350),radius=6,fill='#1c2733');d.text((x+15,y+13),role['label'],font=F,fill='#eee9da');d.text((x+40,y+45),'BEFORE',font=SMALL,fill='#efbd74');d.text((x+355,y+45),'REFRESHED',font=SMALL,fill='#87d4b4')
 old=next(v for v in inventory if v['key']==role['key']);im=Image.open(BASE/old['path']).convert('RGBA');f=old['front'];im=im.crop((f['x'],f['y'],f['x']+f['width'],f['y']+f['height']))
 d.line((x+15,y+325,x+625,y+325),fill='#536878');put(c,im,[old['anchorX'],old['anchorY']],old['pixelScale']*ZOOM,x+160,y+325);put(c,Image.open(item['root']/'export'/role['id']/'idle/south/0.png').convert('RGBA'),item['anchor'],item['scale'],x+480,y+325)
c.convert('RGB').save(OUT/'all-19-before-after.png')
# Stream one frame at a time so the combined reel never decodes 19 full atlases.
if '--reuse-video' in sys.argv:
 assert (OUT/'all-19-walking.mp4').exists() and (OUT/'walking-poster.png').exists()
else:
 width,height=1920,1700;count=480
 cmd=['ffmpeg','-v','warning','-y','-f','rawvideo','-pixel_format','rgb24','-video_size',f'{width}x{height}','-framerate','20','-i','pipe:0','-an','-c:v','libx264','-threads','2','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/'all-19-walking.mp4')]
 p=subprocess.Popen(cmd,stdin=subprocess.PIPE)
 try:
  for frame in range(count):
   row=frame//60;direction=DIRECTIONS[row];seconds=(frame%60)/20
   canvas=Image.new('RGBA',(width,height),'#111923');draw=ImageDraw.Draw(canvas);draw.text((24,14),'WARDENFLAME · 19 REFRESHED MOBS + PARTY REFERENCE',font=TITLE,fill='#eee9da');draw.text((24,55),f'Walking / spectral drift · {NAMES[row]} · common world zoom {ZOOM}× · runtime cadence',font=F,fill='#b7c4cc')
   for i,item in enumerate(items):
    x=i%5*384;y=100+i//5*400;draw.rectangle((x+3,y+3,x+381,y+397),fill='#1c2733');draw.text((x+12,y+15),item['role']['label'],font=SMALL,fill='#eee9da');draw.line((x+10,y+300,x+374,y+300),fill='#536878')
    images=item['frames'][direction];image=images[int(seconds*item['fps'])%len(images)];put(canvas,image,item['anchor'],item['scale'],x+192,y+300)
   if frame==0:canvas.convert('RGB').save(OUT/'walking-poster.png')
   p.stdin.write(canvas.convert('RGB').tobytes())
 finally:p.stdin.close()
 assert p.wait()==0
(OUT/'composition-proof.json').write_text(json.dumps({'mobCount':19,'facings':8,'seconds':24,'fps':20,'commonWorldZoom':ZOOM,'partyReference':'characters.class_fighter','frameCoordinates':'UnitArt.Character / UnitSprite.Source; tactical manifest; fighter held in real front idle','figures':proof},indent=2)+'\n')
links=''.join('<li><a href="../'+slug+'/index.html">'+html.escape(' / '.join(r['label'] for r in json.loads((ROOT/(slug+'-2026-11-08')/'roles.json').read_text())))+'</a></li>' for slug in BATCHES)
native=''
for name,title in [('native-battle.png','Final integrated battle'),('native-overworld.png','Final integrated overworld scale'),('native-contacts.png','Twelve real engine-timed contacts after main integration')]:
 if (OUT/name).exists():native+='<h2>'+title+'</h2><img alt="'+title+'" src="data:image/png;base64,'+base64.b64encode((OUT/name).read_bytes()).decode()+'">'
image64=base64.b64encode((OUT/'all-19-before-after.png').read_bytes()).decode();video64=base64.b64encode((OUT/'all-19-walking.mp4').read_bytes()).decode();poster64=base64.b64encode((OUT/'walking-poster.png').read_bytes()).decode()
(OUT/'index.html').write_text(f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Wardenflame · 19 completed mob appearances</title><style>body{{background:#111923;color:#eee9da;margin:16px;font:16px system-ui;line-height:1.5}}img,video{{max-width:100%;height:auto;image-rendering:pixelated}}a{{color:#b5ead4}}</style><h1>All 19 refreshed library appearances</h1><p>All eight walking directions together, using their runtime cadence and a shared world zoom. The fighter in the last panel shows party scale. Small creatures retain their real size.</p><video controls playsinline preload="none" poster="data:image/png;base64,{poster64}" src="data:video/mp4;base64,{video64}"></video><p><a href="all-19-walking.mp4">Download walking reel</a> · <a href="all-19-before-after.png">Download before/after</a></p><h2>Original → refreshed</h2><img alt="All nineteen historical and refreshed sprites at the same world zoom" src="data:image/png;base64,{image64}">{native}<h2>Full action reviews</h2><ul>{links}</ul><p>The comparison and reel are embedded in this file. Full batch reviews have separate self-contained galleries with native battle/overworld captures and every authored action.</p></html>''')
print('Composed all 19 before/after and 24 s eight-facing walking reel at the same party-relative scale.')
