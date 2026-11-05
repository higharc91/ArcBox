"""Install approved native bridge art as physical tile-owned pieces, never a combined sprite."""
from pathlib import Path
import argparse,base64,hashlib,json,math,sys
import numpy as np
from PIL import Image
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[4]
sys.path.insert(0,str(ROOT/'tools/tactics-assets'))
from parts import town as T,ford_bridge as B


def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--baseline-rules',required=True,type=Path);ap.add_argument('--out',required=True,type=Path);a=ap.parse_args()
 pack=ROOT/'content/tactics/base';a.out.mkdir(parents=True,exist_ok=True)
 props=json.loads((pack/'props/manifest.json').read_text());props['props']={k:v for k,v in props['props'].items() if not any(str(p).startswith('ford/') for p in v.get('variants',{}).values())};terrain=json.loads((pack/'terrain/manifest.json').read_text())
 mp=json.loads((a.baseline_rules/'maps/river_crossing.json').read_text());raw=bytearray(base64.b64decode(mp['tiles']));w,h=mp['size']
 def material(name):
  if name not in mp['materials']:mp['materials'].append(name)
  return mp['materials'].index(name)
 def cell(x,y,height,name,flags=0):raw[(y*w+x)*3:(y*w+x)*3+3]=bytes([height,material(name),flags])
 for y in (9,10):
  for x in (5,6,7):cell(x,y,2,'warden_deck/oak')
  for x,z in [(0,0),(1,0),(2,0),(10,1),(11,1),(12,1),(13,1)]:cell(x,y,z,'cobble/grey')
  cell(3,y,0,'warden_ramp/west');cell(4,y,1,'warden_ramp/west')
  cell(8,y,2,'warden_ramp/east_high');cell(9,y,1,'warden_ramp/east_low')
 for y in range(5,11):
  for x in (12,13):cell(x,y,1,'cobble/grey')
 for x in (5,6,7):
  for y in (8,11):
   i=(y*w+x)*3;raw[i+1]=material('warden_edge/stone');raw[i+2]|=4
 for y in (6,7,8):
  for x in (9,10):cell(x,y,1,'warden_edge/stone',4)
 mp['tiles']=base64.b64encode(raw).decode()
 mp['props']=[p for p in mp['props'] if not p['id'].startswith('bridge_span')]
 for p in mp['props']:
  if p['id']=='crate' and p['at']==[9,9]:p['at']=[11,8]
 mp['zones']['ford']=[[6,7],[7,7]]
 mp['zones']['goblin_camp']=[[9,5],[10,5],[11,5],[12,5],[11,6],[12,6]]
 # The gatehouse now leaves the shallows' east-bank landing at [8,7] clear.
 encounter=json.loads((a.baseline_rules/'encounters/ford_assault.json').read_text())
 for enemy in encounter['enemies']:
  if enemy.get('id')=='ford_shaman':enemy['at']=[8,5]
 (pack/'rules/encounters/ford_assault.json').write_text(json.dumps(encounter,indent=2)+'\n')
 # Art materials deliberately suppress solid terrain columns under actual native arches/ramps.
 terrain['textures']['warden_deck']={**terrain['textures']['planks_ford'],'variants':{'oak':'top/planks_ford_oak.png'}}
 terrain['textures']['warden_edge']={'kind':'top','variants':{'stone':'top/rock_ford.png'},'frames':1,'fps':0.,'size':[256,256]}
 terrain['textures']['warden_ramp']={'kind':'top','variants':{key:'top/rock_ford.png' for key in ['west','east_high','east_low','u1','u2','nu1','nu2','v1','v2','nv1','nv2']},'frames':1,'fps':0.,'size':[256,256]}
 (pack/'terrain/manifest.json').write_text(json.dumps(terrain,indent=2)+'\n')
 env=json.loads((a.baseline_rules/'environment.json').read_text())
 if 'warden_deck/*' not in env['fuel']:env['fuel'].append('warden_deck/*')
 (pack/'rules/environment.json').write_text(json.dumps(env,indent=2)+'\n')
 colours=json.loads((ROOT/'tools/tactics-assets/pixellab/wardenflame-style.json').read_text())['palettes']['ashen_ford']['palette']
 palette=Image.new('P',(1,1));rgb=[tuple(int(c[k:k+2],16) for k in (1,3,5)) for c in colours];palette.putpalette([v for c in rgb+[rgb[0]]*(256-len(rgb)) for v in c])
 def indexed(im):
  alpha=im.getchannel('A');out=im.convert('RGB').quantize(palette=palette,dither=Image.Dither.NONE).convert('RGBA');out.putalpha(alpha);return out
 folder=pack/'props/ford';folder.mkdir(exist_ok=True)
 states={'fortified':{'unless':['ch1_ford_settled']},'force':{'requires':['ch1_ford_settled','ch1_skarrow_slain']},'parley':{'requires':['ch1_ford_settled','ch1_skarrow_spared']}}
 records=[]
 for state,gates in states.items():
  owner={};r=T.render(B.assembly(state),ownership=owner);im=indexed(r.image);rgba=np.asarray(im);hit=rgba[:,:,3]>0
  world=owner['world'];xy=np.floor((world[:,:,:2]+1e-5)/32).astype(int)
  rebuilt=np.zeros_like(rgba)
  for i,j in sorted(set(map(tuple,xy[hit]))):
   x,y=5+int(i),9+int(j)
   assert 0<=x<w and 0<=y<h,(state,x,y)
   selected=hit&(xy[:,:,0]==i)&(xy[:,:,1]==j);yy,xx=np.nonzero(selected)
   z=int(raw[(y*w+x)*3])
   # Full render origin is the deck's logical height 2. Account for the physical
   # owner's floor, including lower water/approach tiles beneath supports.
   ax=r.origin[0]+32*(int(i)-int(j));ay=r.origin[1]+16*(int(i)+int(j)+1)-16*(z-2)
   x0=min(int(xx.min()),ax)-4;y0=min(int(yy.min()),ay)-4;x1=max(int(xx.max())+1,ax+1)+4;y1=max(int(yy.max())+1,ay+1)+4
   canvas=np.zeros((y1-y0,x1-x0,4),np.uint8);canvas[yy-y0,xx-x0]=rgba[yy,xx];rebuilt[selected]=rgba[selected]
   name=f'warden_{state}_{x}_{y}';filename=f'ford/{name}.png';Image.fromarray(canvas).save(pack/'props'/filename)
   props['props'][name]={'variants':{'default':filename},'size':[x1-x0,y1-y0],'anchor':[ax-x0,ay-y0],'frames':1,'fps':0.,'height':max(1,math.ceil(float(world[selected,2].max())/16)+2-z),'spread':False,'blocks':False,'cover':'none','tags':['warden','tile-owned']}
   mp['props'].append({'id':name,'at':[x,y],'blocks':False,'cover':'none',**gates})
   records.append({'id':name,'at':[x,y],'state':state,'image':filename,'sha256':hashlib.sha256((pack/'props'/filename).read_bytes()).hexdigest()})
  assert np.array_equal(rebuilt[hit],rgba[hit]) and np.array_equal(rebuilt[:,:,3],rgba[:,:,3]),'Tile splitting changed approved art pixels'
 # A closed checkpoint blocks the entry only until the battle is WON.
 Image.new('RGBA',(1,1)).save(folder/'gate_collision.png')
 props['props']['warden_gate_collision']={'variants':{'default':'ford/gate_collision.png'},'size':[1,1],'anchor':[0,0],'frames':1,'fps':0.,'height':0,'spread':False,'blocks':True,'cover':'half'}
 for y in (9,10):mp['props'].append({'id':'warden_gate_collision','at':[5,y],'blocks':True,'cover':'half','unless':['ch1_ford_settled']})
 for p in folder.glob('*.png'):
  if p.name!='gate_collision.png' and not any(x['image']=='ford/'+p.name for x in records):p.unlink()
 (pack/'props/manifest.json').write_text(json.dumps(props,indent=2)+'\n');(pack/'rules/maps/river_crossing.json').write_text(json.dumps(mp,indent=2)+'\n')
 region=json.loads((a.baseline_rules/'world/ashen_ford.json').read_text());paints=[]
 for name in mp['materials']:
  if name not in region['materials']:region['materials'].append(name)
 for chunk in region['chunks']:
  b=bytearray(base64.b64decode(chunk['tiles']))
  for y in range(h):
   for x in range(w):
    wx,wy=100+x,52+y
    if wx//32!=chunk['x'] or wy//32!=chunk['y']:continue
    at=(y*w+x)*3;z,mat,flags=raw[at:at+3];materialname=mp['materials'][mat];k=((wy%32)*32+wx%32)*3
    b[k:k+3]=bytes([z+1,region['materials'].index(materialname),flags]);paints.append({'at':[wx,wy],'material':materialname,'height':z+1,'flags':flags})
  if chunk['x']==99//32 and chunk['y']==61//32:
   for wy in (61,62):
    k=((wy%32)*32+99%32)*3;b[k+1]=region['materials'].index('cobble/grey');paints.append({'at':[99,wy],'material':'cobble/grey','height':b[k],'flags':b[k+2]})
  chunk['tiles']=base64.b64encode(b).decode()
 (pack/'rules/world/ashen_ford.json').write_text(json.dumps(region,indent=2)+'\n')
 (a.out/'scene-paint.json').write_text(json.dumps({'regions':[{'region':'ashen_ford','paint':paints,'stamps':[]}]},indent=2)+'\n')
 (a.out/'installed-assets.json').write_text(json.dumps({'pieces':records,'pixel_reconstruction_exact':True,'whole_assembly_installed':False,'gatehouse_shift_tiles':[1,-1]},indent=2)+'\n')
 print(f'Installed {len(records)} tile-owned state pieces; no combined sprite installed')

if __name__=='__main__':main()
