"""Audit all shipped road tiles and author continuous native stone ramps at every grade change."""
from pathlib import Path
import argparse,base64,hashlib,json,sys
import numpy as np
from PIL import Image
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[4];sys.path.insert(0,str(ROOT/'tools/tactics-assets'))
from parts import town as T


def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--work',required=True,type=Path);a=ap.parse_args();a.work.mkdir(parents=True,exist_ok=True)
 pack=ROOT/'content/tactics/base';props=json.loads((pack/'props/manifest.json').read_text());props['props']={k:v for k,v in props['props'].items() if not any(str(p).startswith('road-ramps/') for p in v.get('variants',{}).values())};terrain=json.loads((pack/'terrain/manifest.json').read_text());town=json.loads((pack/'town/manifest.json').read_text());report=[];edits=[]
 cols=json.loads((ROOT/'tools/tactics-assets/pixellab/wardenflame-style.json').read_text())['palettes']['ashen_ford']['palette'];rgb=[tuple(int(c[k:k+2],16) for k in (1,3,5)) for c in cols];pal=Image.new('P',(1,1));pal.putpalette([v for c in rgb+[rgb[0]]*(256-len(rgb)) for v in c]);variants={};folder=pack/'props/road-ramps';folder.mkdir(exist_ok=True)
 def image(depth,q):
  m=T.Model(1,1,ground_shadow=False);z=[v*4 for v in q];points=[(0,0,z[0]),(32,0,z[1]),(32,32,z[2]),(0,32,z[3])]
  for ids in [(0,1,2),(0,2,3)]:
   pts=[points[k] for k in ids];normal=np.cross(np.array(pts[1])-pts[0],np.array(pts[2])-pts[0]);m.poly(pts,T.flat_top(('flagstone','grey')),normal)
  base=-depth*16
  for pts,n,fn in [([(0,32,base),(32,32,base),points[2],points[3]],(0,1,0),T.V_WALL(('fieldstone','grey'))), ([(32,32,base),(32,0,base),points[1],points[2]],(1,0,0),T.U_WALL(('fieldstone','grey')))]:m.poly(pts,fn,n)
  r=T.render(m);alpha=r.image.getchannel('A');r.image=r.image.convert('RGB').quantize(palette=pal,dither=Image.Dither.NONE).convert('RGBA');r.image.putalpha(alpha)
  # A cached tile item must stay in its exact 64px isometric column.
  # Drop renderer padding/outer outlines at the shared tile boundary.
  r.image=r.image.crop((r.origin[0]-32,0,r.origin[0]+32,r.image.height));r.origin=(32,r.origin[1]);return r
 for worldfile in sorted((pack/'rules/world').glob('*.json')):
  baseline=a.work/(worldfile.stem+'-before.json')
  if not baseline.exists():baseline.write_bytes(worldfile.read_bytes())
  d=json.loads(baseline.read_text());allcells={};roads={};blocked=set()
  for b in d.get('buildings',[]):
   definition=town['buildings'][b['id']]
   for yy,row in enumerate(definition['blocks']):
    for xx,value in enumerate(row):
     if value:blocked.add((b['at'][0]+xx,b['at'][1]+yy))
  for group in d.get('props',[]):
   if props['props'][group['id']].get('blocks'):blocked.update(map(tuple,group['at']))
  for c in d['chunks']:
   raw=base64.b64decode(c['tiles'])
   for k in range(len(raw)//3):
    x,y=c['x']*32+k%32,c['y']*32+k//32;h,mat,flags=raw[k*3:k*3+3];name=d['materials'][mat];allcells[x,y]=(h,name,flags)
    if name.split('/')[0] in ['path','path_ford','cobble'] and not flags&20 and (x,y) not in blocked:roads[x,y]=h
  edges=[]
  for (x,y),h in roads.items():
   for dx,dy in [(1,0),(0,1)]:
    if (x+dx,y+dy) in roads and roads[x+dx,y+dy]!=h:edges.append({'from':[x,y],'to':[x+dx,y+dy],'levels':roads[x+dx,y+dy]-h})
  def vertex(x,y):
   levels=[roads[k] for k in [(x-1,y-1),(x,y-1),(x,y),(x-1,y)] if k in roads]
   return int(round(sum(levels)/len(levels)*4))
  paint=[];placed=[]
  for (x,y),height in sorted(roads.items()):
   q=[vertex(x,y)-height*4,vertex(x+1,y)-height*4,vertex(x+1,y+1)-height*4,vertex(x,y+1)-height*4]
   if not any(q):continue
   # Existing native ford ramps own their adjoining geometry; no double rendering.
   if worldfile.stem=='ashen_ford' and 100<=x<114 and 52<=y<64:continue
   depth=max(1,height-min(allcells.get((x+dx,y+dy),(height,'',0))[0] for dx,dy in [(0,0),(1,0),(-1,0),(0,1),(0,-1)]))
   key='q_'+'_'.join(map(str,q))+'_d'+str(depth)
   # The palette image is only a material swatch; real geometry supplies the surface.
   variants[key]=terrain['textures']['path_ford']['variants']['packed']
   mat='road_ramp/'+key
   if mat not in d['materials']:d['materials'].append(mat)
   name='road_ramp_'+key;filename='road-ramps/'+name+'.png'
   if name not in props['props']:
    r=image(depth,q);r.image.save(pack/'props'/filename)
    props['props'][name]={'variants':{'default':filename},'size':list(r.image.size),'anchor':[r.origin[0],r.origin[1]+16],'height':1,'frames':1,'fps':0.,'spread':False,'blocks':False,'cover':'none','tags':['road','ramp']}
   old=allcells[x,y];allcells[x,y]=(height,mat,old[2]);paint.append({'at':[x,y],'height':height,'material':mat,'flags':old[2]});placed.append({'id':name,'variant':'default','at':[[x,y]]})
  for c in d['chunks']:
   b=bytearray(base64.b64decode(c['tiles']))
   for p in paint:
    x,y=p['at']
    if x//32==c['x'] and y//32==c['y']:
     i=((y%32)*32+x%32)*3;b[i:i+3]=bytes([p['height'],d['materials'].index(p['material']),p['flags']])
   c['tiles']=base64.b64encode(b).decode()
  if len(d['materials'])>256:raise ValueError('Too many material indices')
  worldfile.write_text(json.dumps(d,indent=2)+'\n')
  report.append({'region':worldfile.stem,'road_tiles':len(roads),'elevation_edges':edges,'ramp_tiles':len(paint),'placements':placed,'paint':paint})
  edits.append({'region':worldfile.stem,'paint':paint,'stamps':[]})
 terrain['textures']['road_ramp']={'kind':'top','variants':variants,'frames':1,'fps':0.,'size':[256,256]}
 for p in folder.glob('*.png'):
  if not any(str(v)==('road-ramps/'+p.name) for prop in props['props'].values() for v in prop.get('variants',{}).values()):p.unlink()
 (pack/'props/manifest.json').write_text(json.dumps(props,indent=2)+'\n');(pack/'terrain/manifest.json').write_text(json.dumps(terrain,indent=2)+'\n')
 (a.work/'audit.json').write_text(json.dumps(report,indent=2)+'\n');(a.work/'scene-paint.json').write_text(json.dumps({'regions':edits},indent=2)+'\n')
 print([(r['region'],len(r['elevation_edges']),r['ramp_tiles']) for r in report])

if __name__=='__main__':main()
