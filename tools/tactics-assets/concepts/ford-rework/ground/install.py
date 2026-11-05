"""Install the approved green checkpoint and prepare explicit native scene paint.

Run the scene paint through the world editor, then export the scene. This tool
never replaces a saved scene with a reconstruction from exported JSON.
"""
import argparse, base64, hashlib, json, shutil
from pathlib import Path

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[4]
ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--preview',type=Path,required=True)
ap.add_argument('--out',type=Path,required=True)
a=ap.parse_args();a.out.mkdir(parents=True,exist_ok=True)
source=a.preview/'content/tactics/base';target=ROOT/'content/tactics/base'
approved=json.loads((HERE/'green-revision/native-art/assets.json').read_text())
actual=json.loads((a.preview/'assets.json').read_text())
assert actual==approved,'Preview differs from the owner-approved checkpoint'
for entry in approved:
    path=source/'terrain'/entry['path']
    assert hashlib.sha256(path.read_bytes()).hexdigest()==entry['sha256'],path
    (target/'terrain'/entry['path']).parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(path,target/'terrain'/entry['path'])

def cells(world):
    result={}
    for chunk in world['chunks']:
        raw=base64.b64decode(chunk['tiles'])
        for y in range(32):
            for x in range(32):
                pos=(chunk['x']*32+x,chunk['y']*32+y);i=(y*32+x)*3
                if pos[0]<world['size'][0] and pos[1]<world['size'][1]:
                    result[pos]=(raw[i],world['materials'][raw[i+1]],raw[i+2])
    return result

old=json.loads((target/'rules/world/ashen_ford.json').read_text())
new=json.loads((source/'rules/world/ashen_ford.json').read_text())
before,after=cells(old),cells(new);paint=[]
for pos,(height,material,flags) in after.items():
    assert (height,flags)==(before[pos][0],before[pos][2]),'Unexpected terrain geometry change'
    if before[pos][1]!=material:
        paint.append({'at':list(pos),'height':height,'material':material,'flags':flags})
(a.out/'scene-paint.json').write_text(json.dumps({'regions':[{'region':'ashen_ford','paint':paint,'stamps':[]}]},indent=2)+'\n')
# Preserve every unrelated material and all legacy source images.
shutil.copyfile(source/'terrain/manifest.json',target/'terrain/manifest.json')
changed=[]
for path in (source/'rules/maps').glob('*.json'):
    dest=target/'rules/maps'/path.name
    if path.read_bytes()!=dest.read_bytes():shutil.copyfile(path,dest);changed.append(path.stem)
shutil.copyfile(source/'rules/environment.json',target/'rules/environment.json')
(a.out/'installation.json').write_text(json.dumps({'approved_assets':len(approved),'scene_paint_cells':len(paint),'changed_maps':changed,'scene_rebuilt':False,'legacy_pngs_preserved':True},indent=2)+'\n')
print(json.dumps({'assets':len(approved),'scene_paint_cells':len(paint),'maps':changed}))
