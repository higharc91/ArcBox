"""Protect prior art/rules and record exact production/source assets for this batch."""
from pathlib import Path
import copy, hashlib, json, subprocess
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[3]
OUT=REPO/'docs/art/mob-sprite-review/stone-and-mire/checks'
OUT.mkdir(parents=True,exist_ok=True)
PRIOR='3d826133'
def old(path): return json.loads(subprocess.check_output(['git','show',f'{PRIOR}:{path}'],cwd=REPO))
def now(path): return json.loads((REPO/path).read_text())
def save(path,data): path.write_text(json.dumps(data,indent=2)+'\n')
creatures='content/tactics/base/creatures/manifest.json'
a,b=old(creatures),now(creatures)
assert {k:v for k,v in a.items() if k!='sprites'}=={k:v for k,v in b.items() if k!='sprites'}
assert all(b['sprites'][k]==v for k,v in a['sprites'].items())
added=sorted(set(b['sprites'])-set(a['sprites']))
assert added==['mob_refresh_golem','mob_refresh_troll']
abilities='content/tactics/base/rules/abilities/monsters.json'
a,b=old(abilities),now(abilities)
normalized=copy.deepcopy(b)
changed=[]
for item in normalized['abilities']:
 if item['id'] in ['troll.frenzy_roar','golem.stone_aegis']:
  assert item['presentation']['anim']=='bosses.buff'
  item['presentation']['anim']='bosses.special'
  changed.append(item['id'])
assert normalized==a, 'Combat data, FX, projectiles and other presentations must be unchanged'
assert len(changed)==2
bosses='content/tactics/base/bosses/manifest.json'
a,b=old(bosses),now(bosses)
assert {k:v for k,v in a.items() if k!='anims'}=={k:v for k,v in b.items() if k!='anims'}
assert all(b['anims'][k]==v for k,v in a['anims'].items())
assert set(b['anims'])-set(a['anims'])=={'buff'}
assert b['anims']['buff']==a['anims']['special']
assert old('content/tactics/base/portraits/manifest.json')==now('content/tactics/base/portraits/manifest.json')
paths=subprocess.check_output(['git','diff','--name-only',PRIOR],cwd=REPO,text=True).splitlines()
assert not any(p.startswith('content/tactics/base/lore/') or p.startswith('content/tactics/base/rules/') and p!=abilities for p in paths)
allowed={creatures, 'content/tactics/base/creatures/mob_refresh_troll.png','content/tactics/base/creatures/mob_refresh_golem.png'}
unexpected=[p for p in paths if p.startswith(('content/tactics/base/creatures/','content/tactics/base/portraits/')) and p not in allowed]
assert not unexpected,unexpected
for p in ['docs/art/mob-sprite-review/all-mobs.png','docs/art/mob-sprite-review/inventory.json','docs/art/mob-sprite-review/cards.json']:
 assert (REPO/p).read_bytes()==subprocess.check_output(['git','show',f'{PRIOR}:{p}'],cwd=REPO)
for file,prefix in [('index.html',''),('production/index.html','../')]:
    path='docs/art/mob-sprite-review/'+file
    link=f'<p><a style="color:#b5ead4" href="{prefix}stone-and-mire/index.html">Latest: Troll and granite Stone Golem</a></p>'
    before=subprocess.check_output(['git','show',f'{PRIOR}:{path}'],cwd=REPO,text=True)
    assert (REPO/path).read_text().replace(link,'')==before, 'Only the new navigation link may change historical galleries'
source=sorted((ROOT/'source').glob('*.png'))
ids=json.loads((ROOT/'source-images.json').read_text())
assert set(ids)=={p.stem for p in source}
save(ROOT/'source-sha256.json',{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source})
assets=[REPO/'content/tactics/base/creatures'/('mob_refresh_'+mob+'.png') for mob in ['troll','golem']]
save(OUT/'runtime-assets.json',{str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest() for p in assets})
packing=json.loads((ROOT/'packing.json').read_text())
assert sum(len(m['frames']) for m in packing)==352
assert all('rejected' not in f['source'] for m in packing for f in m['frames'])
assert all(f['source']=='source/troll-special-fullback.png' for m in packing if m['id']=='troll' for f in m['frames'] if f['row']==4 and f['action']=='special')
save(OUT/'scope.json',{'priorCommit':subprocess.check_output(['git','rev-parse',PRIOR],cwd=REPO,text=True).strip(),'addedCreatureEntries':added,'changedVisualBindings':changed,'combatMechanicsUnchanged':True,'existingFxAndProjectilesUnchanged':True,'priorCreatureEntriesUnchanged':True,'priorRuntimeArtUnchanged':True,'legacyBossAnimsUnchanged':True,'newBuffAliasMatchesLegacySpecial':True,'dialoguePortraitsUnchanged':True,'historicalBaselineUnchanged':True,'sourceImages':len(source),'runtimeAssets':len(assets),'selectedDistinctPoses':352,'rejectedSourcesExcluded':True,'fullBackReleaseCorrectionSelected':True})
print('Verified two giant atlases, 352 poses, two visual bindings, unchanged combat mechanics/FX/projectiles/prior art/baseline.')
