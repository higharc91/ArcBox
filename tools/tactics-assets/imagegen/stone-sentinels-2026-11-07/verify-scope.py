"""Protect established art and mechanics; record exact source/runtime provenance."""
from pathlib import Path
import hashlib,json,subprocess
from PIL import Image
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parents[3];OUT=REPO/'docs/art/mob-sprite-review/stone-sentinels/checks';OUT.mkdir(exist_ok=True)
PRIOR='459b0fd5';roles=json.loads((ROOT/'roles.json').read_text());names={'mob_refresh_'+r['id'] for r in roles}
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
 p='docs/art/mob-sprite-review/'+file;link=f'<p><a style="color:#b5ead4" href="{prefix}stone-sentinels/index.html">Latest: Basalt, Crystal and Temple Golems</a></p>'
 assert (REPO/p).read_text().replace(link,'')==old(p).decode()
source=sorted((ROOT/'source').glob('*.png'));ids=json.loads((ROOT/'source-images.json').read_text());assert len(source)==16 and set(ids)=={p.stem for p in source}
save(ROOT/'source-sha256.json',{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source})
save(OUT/'runtime-assets.json',{f'content/tactics/base/creatures/{n}.png':hashlib.sha256((REPO/f'content/tactics/base/creatures/{n}.png').read_bytes()).hexdigest() for n in sorted(names)})
packing=json.loads((ROOT/'packing.json').read_text());assert len(packing)==3
assert all(len(m['frames'])==176 for m in packing)
assert sum(len({(f['source'],f['sourcePose']) for f in m['frames']}) for m in packing)==384
motion={r['id']:{action:{p.name:len({hashlib.sha256(Image.open(f).tobytes()).hexdigest() for f in p.glob('*.png')}) for p in sorted((ROOT/'export'/r['id']/action).iterdir())} for action in ['idle','walk','attack','special','cast']} for r in roles}
assert all(n==4 for m in motion.values() for action in m.values() for n in action.values())
save(OUT/'directional-motion.json',motion)
save(OUT/'scope.json',{'priorCommit':PRIOR,'addedCreatureEntries':sorted(names),'combatMechanicsUnchanged':True,'combatTrackerUnchanged':True,'abilityBindingsUnchanged':True,'priorCreatureEntriesUnchanged':True,'priorRuntimeArtUnchanged':True,'dialoguePortraitsUnchanged':True,'historicalBaselineUnchanged':True,'sourceImages':16,'packedFrameKeys':528,'distinctAuthoredPoseSelections':384,'runtimeAssets':3,'specialsPerCreature':2,'wholePoseFacingSelection':True,'rejectedCrystalRearPunchPoses':[6,7],'visualContactTimingRefined':True})
assert (REPO/'docs/art/mob-sprite-review/production/refresh-status.json').read_bytes()==old('docs/art/mob-sprite-review/production/refresh-status.json')
crystal=next(m for m in packing if m['id']=='golem_crystal')
assert not any(f['source']=='source/golem_crystal-attack.png' and f['sourcePose'] in [6,7] for f in crystal['frames'])
assert {f['sourcePose'] for f in crystal['frames'] if f['source']=='source/golem_crystal-attack-rear.png'}=={0,1}
for m in packing:
 assert all(f['sourcePose']%2==0 for f in m['frames'] if f['action']=='hurt' and f['source'].endswith('-damage.png'))
 assert all(f['sourcePose']%2==1 for f in m['frames'] if f['action']=='down')
refs=sorted((ROOT/'references').glob('*.png'));assert len(refs)==16
save(ROOT/'references-sha256.json',{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in refs})
for v in json.loads((ROOT/'reference-inputs.json').read_text()).values():
 for ref in v['references']:assert (ROOT/ref).exists(),ref
print('Verified528 frame keys /384 selected figures,16 source sheets and unchanged prior art/rules/combat tracker.')
