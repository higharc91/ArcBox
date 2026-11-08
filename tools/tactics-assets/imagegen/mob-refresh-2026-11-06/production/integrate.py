"""Register the reviewed first batch; leave other roles and color variants unchanged."""
from pathlib import Path
import json,shutil
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[4]
BASE=REPO/'content/tactics/base'
manifest=json.loads((BASE/'creatures/manifest.json').read_text())
packing=json.loads((ROOT/'packing.json').read_text())
for d in packing:
    mob=d['id'];checks=json.loads((ROOT/'export'/mob/'checks.json').read_text());idle=[c['bounds']for c in checks if c['action']=='idle'];name='mob_refresh_'+mob
    filename=name+'.png';shutil.copyfile(ROOT/'export'/mob/'atlas.png',BASE/'creatures'/filename)
    first=next(c['bounds']for c in checks if c['action']=='idle' and c['direction']=='south' and c['frame']==0)
    portrait_size={'goblin':48,'orc':48,'drake':64}[mob]
    entry={'file':filename,'family':{'goblin':'goblin','orc':'orc','drake':'drake'}[mob],'variant':{'goblin':'cutthroat','orc':'brute','drake':'red'}[mob], 'frame':{'width':d['size'],'height':d['size'],'anchor':d['anchor']}, 'portrait':[d['anchor'][0]-portrait_size//2,max(0,first[1]-2),portrait_size,portrait_size], 'headTop':min(b[1]for b in idle),'pixelScale':d['pixelScale'],'columns':8,'rowsPerDirection':2,'directions':['south','south-west','west','north-west','north','north-east','east','south-east'],'facingOffsets':{v:[0,0]for v in ['south','south-west','west','north-west','north','north-east','east','south-east']},'footprintWidth':round(max(b[2]-b[0]+1 for b in idle)*d['pixelScale'],2),'footprintHeight':round(max(b[3]-b[1]+1 for b in idle)*d['pixelScale'],2),'keys':{'goblin':['characters.goblin_cutthroat'],'orc':['characters.orc_brute'],'drake':['beasts.drake.red','beasts.drake']}[mob], 'anims':{'idle':{'frames':[0,1,2,3],'fps':3,'loop':True},'walk':{'frames':[4,5,6,7],'fps':6,'loop':True},'attack':{'frames':[8,9,10,10,9,0] if mob=='drake' else [8,9,9,10,11,0] if mob=='orc' else [8,9,10,11,0],'fps':8 if mob=='drake' else 9 if mob=='orc' else 10,'loop':False},'hurt':{'frames':[12],'fps':8,'loop':False},'down':{'frames':[13],'fps':1,'loop':False}},'provenance':'Owner-approved Scenario concept and OpenAI-authored pixel frames; source, extraction, facing corrections and review in tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production.'}
    if mob=='drake':entry['anims']['special']={**entry['anims']['attack'],'fps':6};manifest['sprites']['drake_red']=entry
    else:manifest['sprites'][name]=entry
(BASE/'creatures/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Registered goblin cutthroat, orc brute and red drake: eight facings, five actions, no rule/stat changes.')
