"""Register goblin role art without changing combat rules or named Mother Skarrow art."""
from pathlib import Path
import json,shutil
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[3];BASE=REPO/'content/tactics/base'
manifest=json.loads((BASE/'creatures/manifest.json').read_text())
for d in json.loads((ROOT/'packing.json').read_text()):
    role=d['role'];name='mob_refresh_'+d['id'];filename=name+'.png'
    shutil.copyfile(ROOT/'export'/d['id']/'atlas.png',BASE/'creatures'/filename)
    idle=[f for f in d['frames']if f['action']=='idle']
    head_positions=[(d['anchor'][0]+(f['head'][0]-f['foot'][0])*f['scale'],d['anchor'][1]+(f['head'][1]-f['foot'][1])*f['scale'])for f in idle]
    hx,hy=head_positions[0];head_top=max(0,round(min(p[1]for p in head_positions)-(7 if role=='shaman' else 2)))
    checks=json.loads((ROOT/'export'/d['id']/'checks.json').read_text());bounds=[f['bounds']for f in checks if f['action']=='idle']
    anims={'idle':{'frames':[0,1,2,3],'fps':3,'loop':True},'walk':{'frames':[4,5,6,7],'fps':6,'loop':True},'attack':{'frames':[8,9,10,11,0],'fps':9 if role=='archer'else 10,'loop':False},'hurt':{'frames':[12],'fps':8,'loop':False},'down':{'frames':[13],'fps':1,'loop':False}}
    if role=='archer':anims['shoot']=anims['attack']
    else:
        cast={'frames':[14,14,15,15,16,0],'fps':8,'loop':False}
        for key in ['cast','point','summon','special','buff','pray']:anims[key]=cast
    entry={'file':filename,'family':'goblin','variant':role,'frame':{'width':d['size'],'height':d['size'],'anchor':d['anchor']},'portrait':[max(0,min(d['size']-56,round(hx)-28)),max(0,min(d['size']-56,head_top-2)),56,56],'headTop':head_top,'pixelScale':d['pixelScale'],'columns':8,'rowsPerDirection':d['rowsPerDirection'],'directions':['south','south-west','west','north-west','north','north-east','east','south-east'],'facingOffsets':{v:[0,0]for v in ['south','south-west','west','north-west','north','north-east','east','south-east']},'footprintWidth':round(max(b[2]-b[0]+1 for b in bounds)*d['pixelScale'],2),'footprintHeight':round(max(b[3]-b[1]+1 for b in bounds)*d['pixelScale'],2),'keys':['characters.'+{'archer':'goblin_archer','shaman':'goblin_shaman','boss':'goblin_spear'}[role]],'anims':anims,'provenance':'Scenario lore concepts; OpenAI-authored pixel poses. Source, corrections, selected gestures and export checks: tools/tactics-assets/imagegen/goblin-roles-2026-11-07/.'}
    manifest['sprites'][name]=entry
(BASE/'creatures/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Registered goblin archer, shaman/Stormcaller and Vorga/boss; stable keys, no stat/lore/spawn changes.')
