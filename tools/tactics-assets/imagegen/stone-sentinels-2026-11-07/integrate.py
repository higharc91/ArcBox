"""Register complete golem figures and visual clips; preserve all rules/FX assets."""
from pathlib import Path
import json,shutil
ROOT=Path(__file__).resolve().parent
BASE=ROOT.parents[3]/'content/tactics/base'
path=BASE/'creatures/manifest.json';manifest=json.loads(path.read_text())
roles={v['id']:v for v in json.loads((ROOT/'roles.json').read_text())}
for mob in json.loads((ROOT/'packing.json').read_text()):
    role=roles[mob['id']];name='mob_refresh_'+mob['id']
    shutil.copyfile(ROOT/'export'/mob['id']/'atlas.png',BASE/'creatures'/(name+'.png'))
    idle=[f for f in mob['frames'] if f['action']=='idle']
    head_top=round(min(mob['anchor'][1]+(f['head'][1]-f['foot'][1])*f['scale'] for f in idle))
    front=idle[0];head_x=round(mob['anchor'][0]+(front['head'][0]-front['foot'][0])*front['scale'])
    head_y=round(mob['anchor'][1]+(front['head'][1]-front['foot'][1])*front['scale'])
    checks=json.loads((ROOT/'export'/mob['id']/'checks.json').read_text());bounds=[f['bounds'] for f in checks if f['action']=='idle']
    clearance={}
    for action in ['walk','attack','special','cast']:
        top=min(f['bounds'][1] for f in checks if f['action']==action)
        if top<head_top:clearance[action]=round((head_top-top)*mob['pixelScale']+2,2)
    special=[14,15,15,15,15,16,16,17,0,0]
    secondary=[18,19,19,19,20,20,21,0]
    anims={'idle':{'frames':[0,1,2,3],'fps':role['idleFps'],'loop':True},'walk':{'frames':[4,5,6,7],'fps':role['walkFps'],'loop':True},
        'attack':{'frames':[8,9,9,9,10,10,11,0],'fps':10,'loop':False},'hurt':{'frames':[12],'fps':8,'loop':False},'down':{'frames':[13],'fps':1,'loop':False},
        'special':{'frames':special,'fps':10,'loop':False},'cast':{'frames':secondary,'fps':10,'loop':False}}
    anims['buff']=dict(anims['cast']);clearance['buff']=clearance.get('cast',0)
    manifest['sprites'][name]={'file':name+'.png','family':mob['id'],'variant':'stone-sentinels',
        'frame':{'width':mob['size'],'height':mob['size'],'anchor':mob['anchor']},'portrait':[head_x-32,max(0,head_y-3),64,64],'headTop':head_top,
        'overheadClearance':clearance,'pixelScale':mob['pixelScale'],'columns':8,'rowsPerDirection':3,
        'directions':['south','south-west','west','north-west','north','north-east','east','south-east'],
        'facingOffsets':{v:[0,0] for v in ['south','south-west','west','north-west','north','north-east','east','south-east']},
        'footprintWidth':round(max(b[2]-b[0]+1 for b in bounds)*mob['pixelScale'],2),'footprintHeight':round(max(b[3]-b[1]+1 for b in bounds)*mob['pixelScale'],2),
        'keys':[role['key']],'anims':anims,'provenance':'Scenario/OpenAI variant concepts; OpenAI complete pixel poses. Source/packing receipts: tools/tactics-assets/imagegen/stone-sentinels-2026-11-07/.'}
path.write_text(json.dumps(manifest,indent=2)+'\n')
print('Registered three golem library-variant atlases; all rule JSON and existing assets unchanged.')
