"""Register three complete small-companion figures; preserve rules and prior art."""
from pathlib import Path
import json,shutil
ROOT=Path(__file__).resolve().parent;BASE=ROOT.parents[3]/'content/tactics/base';path=BASE/'creatures/manifest.json';manifest=json.loads(path.read_text())
roles={r['id']:r for r in json.loads((ROOT/'roles.json').read_text())}
for mob in json.loads((ROOT/'packing.json').read_text()):
 r=roles[mob['id']];name='mob_refresh_'+r['id'];shutil.copyfile(ROOT/'export'/r['id']/'atlas.png',BASE/'creatures'/(name+'.png'))
 idle=[f for f in mob['frames'] if f['action']=='idle'];front=idle[0];head_top=round(min(mob['anchor'][1]+(f['head'][1]-f['foot'][1])*f['scale'] for f in idle))
 hx=round(mob['anchor'][0]+(front['head'][0]-front['foot'][0])*front['scale']);hy=round(mob['anchor'][1]+(front['head'][1]-front['foot'][1])*front['scale']);size=r['portraitSize']
 checks=json.loads((ROOT/'export'/r['id']/'checks.json').read_text());bounds=[f['bounds'] for f in checks if f['action']=='idle'];clearance={}
 for action in ['walk','attack']:
  top=min(f['bounds'][1] for f in checks if f['action']==action)
  if top<head_top:clearance[action]=round((head_top-top)*mob['pixelScale']+2,2)
 manifest['sprites'][name]={'file':name+'.png','family':r['primaryId'],'variant':'night-companions','frame':{'width':mob['size'],'height':mob['size'],'anchor':mob['anchor']},'portrait':[hx-size//2,max(0,hy-3),size,size],'headTop':head_top,'overheadClearance':clearance,'pixelScale':mob['pixelScale'],'columns':8,'rowsPerDirection':2,'directions':['south','south-west','west','north-west','north','north-east','east','south-east'],'facingOffsets':{d:[0,0] for d in ['south','south-west','west','north-west','north','north-east','east','south-east']},'footprintWidth':round(max(b[2]-b[0]+1 for b in bounds)*mob['pixelScale'],2),'footprintHeight':round(max(b[3]-b[1]+1 for b in bounds)*mob['pixelScale'],2),'keys':[r['key']],'anims':{'idle':{'frames':[0,1,2,3],'fps':3,'loop':True},'walk':{'frames':[4,5,6,7],'fps':6,'loop':True},'attack':{'frames':[8,9,9,9,10,10,11,0],'fps':10,'loop':False},'hurt':{'frames':[12],'fps':8,'loop':False},'down':{'frames':[13],'fps':1,'loop':False}},'provenance':'Scenario three-companion concepts; OpenAI complete pixel figures. Exact sources/packing receipts: tools/tactics-assets/imagegen/night-companions-2026-11-07/.'}
path.write_text(json.dumps(manifest,indent=2)+'\n');print('Registered three small library companions; rules and prior entries unchanged.')
