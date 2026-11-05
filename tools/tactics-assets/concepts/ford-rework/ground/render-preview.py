"""Make an isolated native ground review pack; never write installed terrain."""
import argparse, base64, fnmatch, hashlib, json, shutil, sys
from pathlib import Path
from PIL import Image, ImageDraw

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[4]
sys.path.insert(0,str(ROOT/'tools/tactics-assets'))
from parts import ford_terrain as F

ap=argparse.ArgumentParser();ap.add_argument('--out',type=Path,required=True);a=ap.parse_args()
pack=a.out/'content/tactics/base';source=ROOT/'content/tactics/base'
shutil.copytree(source,pack,dirs_exist_ok=True)
manifest=json.loads((pack/'terrain/manifest.json').read_text()); records=[]; sheets=[]
allowed={tuple(int(c[i:i+2],16) for i in (1,3,5)) for c in json.load(open(ROOT/'tools/tactics-assets/pixellab/wardenflame-style.json'))['palettes']['ashen_ford']['palette']}
pal=Image.new('P',(1,1));rgb=sorted(allowed);pal.putpalette([v for c in rgb+[rgb[0]]*(256-len(rgb)) for v in c])
for family,entries in F.TOPS.items():
    variants={}
    for variant,kind,seed in entries:
        canvas,ramps=F.top(kind,seed);im=canvas.render(ramps)
        assert set(im.getchannel('A').get_flattened_data())=={255}
        assert {p[:3] for p in im.get_flattened_data()}<=allowed
        rel=f'top/native_{family}_{variant}.png';dest=pack/'terrain'/rel;im.save(dest);variants[variant]=rel
        records.append({'family':family,'variant':variant,'kind':'top','seed':seed,'path':rel,'size':list(im.size),'colours':len(im.convert('RGB').getcolors()),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
        sheets.append((family+'/'+variant,im))
    manifest['textures'][family]={'kind':'top','variants':variants,'frames':1,'fps':0.,'size':[128,128],'span':4,'edges':'clustered'}
canvas,ramps=F.cliff();im=canvas.render(ramps).convert('RGB').quantize(palette=pal,dither=Image.Dither.NONE).convert('RGBA');im.save(pack/'terrain/side/native_cliff_ford.png')
records.append({'family':'cliff_ford','variant':'grey','kind':'side','seed':822,'path':'side/native_cliff_ford.png','size':list(im.size),'colours':len(im.convert('RGB').getcolors()),'sha256':hashlib.sha256((pack/'terrain/side/native_cliff_ford.png').read_bytes()).hexdigest()})
manifest['textures']['cliff_ford']={'kind':'side','variants':{'grey':'side/native_cliff_ford.png'},'frames':1,'fps':0.,'size':[128,32],'span':4}
(pack/'terrain/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
# The battle stamp's west-bank grass rectangle must read as part of the burned
# bank. Its remaining dry roots keep exactly the original fuel classification.
mapfile=pack/'rules/maps/river_crossing.json';m=json.loads(mapfile.read_text());raw=bytearray(base64.b64decode(m['tiles']));old=bytes(raw)
m['materials'].append('ash_ford/roots');rootmat=len(m['materials'])-1;paint=[]
for y in range(m['size'][1]):
    for x in range(6):
        i=(y*m['size'][0]+x)*3
        if m['materials'][raw[i+1]]=='grass_ford/autumn':raw[i+1]=rootmat;paint.append((x,y))
m['tiles']=base64.b64encode(raw).decode();mapfile.write_text(json.dumps(m,indent=2)+'\n')
worldfile=pack/'rules/world/ashen_ford.json';world=json.loads(worldfile.read_text());world['materials'].append('ash_ford/roots');worldmat=len(world['materials'])-1
for chunk in world['chunks']:
    b=bytearray(base64.b64decode(chunk['tiles']))
    for x,y in paint:
        x+=100;y+=52
        if x//32==chunk['x'] and y//32==chunk['y']:b[((y%32)*32+x%32)*3+1]=worldmat
    chunk['tiles']=base64.b64encode(b).decode()
worldfile.write_text(json.dumps(world,indent=2)+'\n')
envfile=pack/'rules/environment.json';env=json.loads(envfile.read_text());before=list(env['fuel']);env['fuel'].append('ash_ford/roots');envfile.write_text(json.dumps(env,indent=2)+'\n')
for i in range(0,len(raw),3):
    assert raw[i]==old[i] and raw[i+2]==old[i+2]
    fuel=lambda mat,patterns:any(fnmatch.fnmatchcase(m['materials'][mat],p) for p in patterns)
    assert fuel(raw[i+1],env['fuel'])==fuel(old[i+1],before)
(a.out/'mechanics.json').write_text(json.dumps({'repainted_west_bank_cells':len(paint),'heights_flags_and_per_cell_fuel_unchanged':True,'water_conduction_flags_unchanged':True,'enemy_counts_stats_rewards_objectives_unchanged':True},indent=2)+'\n')
a.out.mkdir(exist_ok=True)
(a.out/'assets.json').write_text(json.dumps(records,indent=2)+'\n')
sheet=Image.new('RGB',(4*300,((len(sheets)+3)//4)*320),(26,24,30));d=ImageDraw.Draw(sheet)
for i,(name,im) in enumerate(sheets):
    x,y=(i%4)*300,(i//4)*320;d.text((x+8,y+8),name,fill=(225,218,204))
    sheet.paste(im.resize((256,256),Image.Resampling.NEAREST),(x+8,y+36))
sheet.save(a.out/'native-materials.png')
print(json.dumps({'pack':str(pack),'tops':len(sheets),'sides':1,'native_spend':0,'palette':'ashen_ford','installed_content_changed':False}))
