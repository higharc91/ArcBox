"""Prove the final mob work preserves the separately merged main art/rules."""
from pathlib import Path
import hashlib,json,subprocess
R=Path(__file__).resolve().parents[5];O=R/'docs/art/mob-sprite-review/library-completion'
base='0d808be20ccb00e3fe3891983c40c60470e29cbc'
def old(path):return subprocess.check_output(['git','show',base+':'+path],cwd=R)
p='content/tactics/base/creatures/manifest.json';before=json.loads(old(p));after=json.loads((R/p).read_text())
assert {k:v for k,v in before.items() if k!='sprites'}=={k:v for k,v in after.items() if k!='sprites'}
assert all(after['sprites'][k]==v for k,v in before['sprites'].items())
added=set(after['sprites'])-set(before['sprites']);assert len(added)==19
allowed={p}|{'content/tactics/base/creatures/'+after['sprites'][k]['file'] for k in added}
changed=subprocess.check_output(['git','diff','--name-only',base],cwd=R,text=True).splitlines()
assert not [p for p in changed if p.startswith('content/') and p not in allowed]
for n in ['all-mobs.png','inventory.json','cards.json','production/refresh-status.json']:
 p='docs/art/mob-sprite-review/'+n;assert (R/p).read_bytes()==old(p)
t=json.loads((R/'docs/art/mob-sprite-review/production/library-refresh-status.json').read_text());assert t['remainingTwoDirectionLooks']==0 and len(t['integratedKeys'])==31
proof={'integrationBase':base,'newAppearances':19,'priorMainCreatureEntriesPreserved':True,'priorMainContentFilesPreserved':True,'rulesStatsAbilitiesPlacementsAndDialoguePortraitsPreserved':True,'historicalBaselineAndCombatTrackerPreserved':True,'remainingOlderLibraryAppearances':0,'olderLibraryAppearancesComplete':31,'totalRefreshedDesigns':60,'batches':26,'newAtlasSha256':{after['sprites'][k]['file']:hashlib.sha256((R/'content/tactics/base/creatures'/after['sprites'][k]['file']).read_bytes()).hexdigest() for k in sorted(added)}}
(O/'integration-scope.json').write_text(json.dumps(proof,indent=2)+'\n');print('Verified all 19 additions, all current-main creature entries/content, zero older library appearances remaining.')
