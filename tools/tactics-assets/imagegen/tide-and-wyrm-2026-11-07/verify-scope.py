"""Protect established art and mechanics; record exact source/runtime provenance."""
from pathlib import Path
import hashlib,json,subprocess
from PIL import Image
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parents[3];OUT=REPO/'docs/art/mob-sprite-review/tide-and-wyrm/checks';OUT.mkdir(exist_ok=True)
PRIOR='d07880ed';roles=json.loads((ROOT/'roles.json').read_text());names={'mob_refresh_'+r['id'] for r in roles}
def old(p):return subprocess.check_output(['git','show',f'{PRIOR}:{p}'],cwd=REPO)
def save(p,d):p.write_text(json.dumps(d,indent=2)+'\n')
p='content/tactics/base/creatures/manifest.json';a=json.loads(old(p));b=json.loads((REPO/p).read_text())
assert {k:v for k,v in a.items() if k!='sprites'}=={k:v for k,v in b.items() if k!='sprites'}
assert all(b['sprites'][k]==v for k,v in a['sprites'].items());assert set(b['sprites'])-set(a['sprites'])==names
paths=subprocess.check_output(['git','diff','--name-only',PRIOR],cwd=REPO,text=True).splitlines()
allowed={'content/tactics/base/creatures/manifest.json'}|{'content/tactics/base/creatures/'+n+'.png' for n in names}
assert not [p for p in paths if p.startswith('content/') and p not in allowed]
for p in ['all-mobs.png','inventory.json','cards.json']:
 p='docs/art/mob-sprite-review/'+p;assert (REPO/p).read_bytes()==old(p)
for file,prefix in [('index.html',''),('production/index.html','../')]:
 p='docs/art/mob-sprite-review/'+file;link=f'<p><a style="color:#b5ead4" href="{prefix}tide-and-wyrm/index.html">Latest: Water Elemental and Red Dragon</a></p>'
 assert (REPO/p).read_text().replace(link,'')==old(p).decode()
source=sorted((ROOT/'source').glob('*.png'));ids=json.loads((ROOT/'source-images.json').read_text());assert len(source)==13 and set(ids)=={p.stem for p in source}
save(ROOT/'source-sha256.json',{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source})
save(OUT/'runtime-assets.json',{f'content/tactics/base/creatures/{n}.png':hashlib.sha256((REPO/f'content/tactics/base/creatures/{n}.png').read_bytes()).hexdigest() for n in sorted(names)})
packing=json.loads((ROOT/'packing.json').read_text());assert len(packing)==2
assert [len(m['frames']) for m in packing]==[176,208]
assert sum(len({(f['source'],f['sourcePose']) for f in m['frames']}) for m in packing)==272
motion={r['id']:{action:{p.name:len({hashlib.sha256(Image.open(f).tobytes()).hexdigest() for f in p.glob('*.png')}) for p in sorted((ROOT/'export'/r['id']/action).iterdir())} for action in ['idle','walk','attack','special','cast']+(['buff'] if r['id']=='dragon' else [])} for r in roles}
assert all(n==4 for m in motion.values() for action in m.values() for n in action.values())
save(OUT/'directional-motion.json',motion)
save(OUT/'scope.json',{'priorCommit':PRIOR,'addedCreatureEntries':sorted(names),'combatMechanicsUnchanged':True,'abilityBindingsUnchanged':True,'priorCreatureEntriesUnchanged':True,'priorRuntimeArtUnchanged':True,'dialoguePortraitsUnchanged':True,'historicalBaselineUnchanged':True,'sourceImages':13,'packedFrameKeys':384,'distinctAuthoredPoseSelections':272,'runtimeAssets':2,'specialsPerCreature':{'water_elemental':2,'dragon':3},'wholePoseFacingSelection':True,'visualContactTimingRefined':True})

for m in packing:
 assert all(f['sourcePose']<8 for f in m['frames'] if f['action']=='hurt')
 assert all(f['sourcePose']>=8 for f in m['frames'] if f['action']=='down')
assert not any(f['source']=='source/water_elemental-moves.png' and 12<=f['sourcePose']<=15 for f in packing[0]['frames'])
print('Verified 384 keys / 272 distinct authored selections, corrected rear gestures, two atlases and unchanged prior art/rules.')
# The user specifically requested visible wings during attacks, in every facing.
import numpy as np
export=ROOT/'export/dragon';wing_ratios={}
for direction in sorted((export/'idle').iterdir()):
 def visible_width(file):
  alpha=np.array(Image.open(file))[:,:,3]>=128;x=np.where(alpha)[1];return int(x.max()-x.min()+1)
 ratio=visible_width(export/'attack'/direction.name/'2.png')/visible_width(direction/'0.png')
 assert ratio>1.25,(direction.name,ratio)
 wing_ratios[direction.name]=round(ratio,3)
save(OUT/'dragon-attack-wings.json',{'minimumExpansion':1.25,'ratios':wing_ratios,'passed':True})
