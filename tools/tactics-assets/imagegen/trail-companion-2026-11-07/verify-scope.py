"""Protect prior art/mechanics and record exact source/runtime hashes."""
from pathlib import Path
import hashlib,json,subprocess
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[3]
OUT=REPO/'docs/art/mob-sprite-review/trail-companion/checks';OUT.mkdir(exist_ok=True)
PRIOR='05c4fb65'
def old(p):return subprocess.check_output(['git','show',f'{PRIOR}:{p}'],cwd=REPO)
def save(p,d):p.write_text(json.dumps(d,indent=2)+'\n')
p='content/tactics/base/creatures/manifest.json';a=json.loads(old(p));b=json.loads((REPO/p).read_text())
assert {k:v for k,v in a.items() if k!='sprites'}=={k:v for k,v in b.items() if k!='sprites'}
assert all(b['sprites'][k]==v for k,v in a['sprites'].items())
assert set(b['sprites'])-set(a['sprites'])=={'mob_refresh_hound'}
paths=subprocess.check_output(['git','diff','--name-only',PRIOR],cwd=REPO,text=True).splitlines()
assert not any(p.startswith(('content/tactics/base/rules/','content/tactics/base/lore/','content/tactics/base/bosses/','content/tactics/base/portraits/')) for p in paths)
allowed={'content/tactics/base/creatures/manifest.json','content/tactics/base/creatures/mob_refresh_hound.png'}
assert not [p for p in paths if p.startswith('content/') and p not in allowed]
for p in ['all-mobs.png','inventory.json','cards.json']:
 p='docs/art/mob-sprite-review/'+p;assert (REPO/p).read_bytes()==old(p)
for file,prefix in [('index.html',''),('production/index.html','../')]:
 p='docs/art/mob-sprite-review/'+file
 link=f'<p><a style="color:#b5ead4" href="{prefix}trail-companion/index.html">Latest: Hunting Hound</a></p>'
 assert (REPO/p).read_text().replace(link,'')==old(p).decode()
source=sorted((ROOT/'source').glob('*.png'));ids=json.loads((ROOT/'source-images.json').read_text());assert len(source)==4 and set(ids)=={p.stem for p in source}
save(ROOT/'source-sha256.json',{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source})
p=REPO/'content/tactics/base/creatures/mob_refresh_hound.png';save(OUT/'runtime-assets.json',{str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest()})
packing=json.loads((ROOT/'packing.json').read_text());frames=packing[0]['frames'];assert len(packing)==1 and len(frames)==112
unique={(f['source'],f['sourcePose']) for f in frames};assert len(unique)==112
save(OUT/'scope.json',{'priorCommit':PRIOR,'addedCreatureEntries':['mob_refresh_hound'],'combatMechanicsUnchanged':True,'abilityBindingsUnchanged':True,'priorCreatureEntriesUnchanged':True,'priorRuntimeArtUnchanged':True,'dialoguePortraitsUnchanged':True,'historicalBaselineUnchanged':True,'sourceImages':4,'packedFrameKeys':112,'distinctAuthoredPoseSelections':112,'runtimeAssets':1,'wholePoseFacingSelection':True})
from PIL import Image
export=ROOT/'export/hound'
motion={action:{p.name:len({hashlib.sha256(Image.open(f).tobytes()).hexdigest() for f in p.glob('*.png')}) for p in sorted((export/action).iterdir())} for action in ['idle','walk','attack']}
assert all(n==4 for action in motion.values() for n in action.values())
save(OUT/'directional-motion.json',motion)
print('Verified 112 keys / 112 distinct source poses, one new atlas, unchanged rules, prior art and baseline.')
