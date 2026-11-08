"""Audit all seven follow-up batches without modifying historical receipts."""
from pathlib import Path
import json,hashlib
R=Path(__file__).resolve().parents[5];root=R/'tools/tactics-assets/imagegen';out=R/'docs/art/mob-sprite-review/library-completion';batches=['crypt-kin','web-and-hunt','iron-and-fangs','wyrm-triad','gilded-and-rime','autumn-and-veil','spectral-remnant'];results=[]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
for slug in batches:
 r=root/(slug+'-2026-11-08');ids=json.loads((r/'source-images.json').read_text());sources=list((r/'source').glob('*.png'));assert set(ids)=={p.stem for p in sources}
 for folder,name in [('source','source-sha256.json'),('references','references-sha256.json')]:
  assert json.loads((r/name).read_text())=={p.name:sha(p) for p in (r/folder).glob('*.png')},slug
 originals=True
 for p in sources:
  original=Path('/workspace/generated_images')/('exec-'+ids[p.stem]+'.png')
  if original.exists():assert sha(p)==sha(original),(slug,p.name)
  else:originals=False
 packing=json.loads((r/'packing.json').read_text());keys=sum(len(m['frames']) for m in packing)
 selected=sum(len({(f['source'],f['sourcePose']) for f in m['frames']}) for m in packing);assert selected==keys
 assert not any(f['source'].endswith('-draft.png') for m in packing for f in m['frames'])
 results.append({'batch':slug,'appearances':len(packing),'sourceImages':len(sources),'distinctSelectedWholeFigures':keys,'sourceAndReferenceReceiptsUnchanged':True,'availableOriginalGenerationFilesIdentical':originals,'rejectedDraftsExcluded':True})
proof={'appearances':sum(r['appearances'] for r in results),'sourceImages':sum(r['sourceImages'] for r in results),'distinctSelectedWholeFigures':sum(r['distinctSelectedWholeFigures'] for r in results),'batches':results};assert proof['appearances']==19
(out/'provenance-audit.json').write_text(json.dumps(proof,indent=2)+'\n');print(f"Verified {proof['sourceImages']} original source images and {proof['distinctSelectedWholeFigures']} distinct selected whole figures across all nineteen appearances.")
