"""Build runtime atlases with shared anchors, combat poses and faithful existing palette variants."""
import collections,json,runpy,sys
from pathlib import Path
import numpy as np
from PIL import Image
R=Path(__file__).resolve().parent;B=R.parent;SOURCE_REPO=next(p for p in R.parents if (p/'content/tactics/base').exists());TARGET=Path(sys.argv[1]) if len(sys.argv)>1 else SOURCE_REPO
sys.path.insert(0,str(SOURCE_REPO/'tools/tactics-assets'))
from parts import beasts,bestiary
from pixel_atlas import save_exact_png,portrait_rect
from creature_scale import SCALES,scale_for
MAPPED=runpy.run_path(str(R/'canonicalize.py'))['mapped'];CLEAN=runpy.run_path(str(R/'clean-poses.py'))['clean']
DIRS=['south','south-west','west','north-west','north','north-east','east','south-east']
ANIMS={'idle':{'frames':list(range(4)),'fps':1000/180,'loop':True},'walk':{'frames':list(range(4,12)),'fps':1000/110,'loop':True},'attack':{'frames':list(range(12,16))+[0],'fps':10,'loop':False},'special':{'frames':list(range(12,16))+[0],'fps':9,'loop':False},'hurt':{'frames':[16],'fps':10,'loop':False},'down':{'frames':[17],'fps':1,'loop':False}}

def recolor_map(old,variant):
 a=np.array(old.convert('RGBA'));b=np.array(variant.convert('RGBA'));m=(a[:,:,3]>=128)&(b[:,:,3]>=128)
 votes=collections.defaultdict(collections.Counter)
 for s,t in zip(a[m,:3],b[m,:3]):votes[tuple(s)][tuple(t)]+=1
 return {s:c.most_common(1)[0][0] for s,c in votes.items()}
_CACHE=None
def recolor(im,mapping):
 global _CACHE
 if _CACHE is None or _CACHE[0] is not im:
  a=np.array(im);colors,inv=np.unique(a[:,:,:3].reshape(-1,3),axis=0,return_inverse=True);_CACHE=(im,a,colors,inv)
 _,original,colors,inv=_CACHE;a=original.copy();src=np.array(list(mapping),dtype=np.int32);dst=np.array([mapping[tuple(c)] for c in src],dtype=np.uint8)
 idx=((colors[:,None,:].astype(np.int32)-src[None,:,:])**2).sum(2).argmin(1);a[:,:,:3]=dst[idx][inv].reshape(a.shape[0],a.shape[1],3);a[a[:,:,3]==0]=0;return Image.fromarray(a)
def main():
 layout=json.loads((R/'canonical-layout.json').read_text());inv={c['id']:c for c in json.loads((SOURCE_REPO/'tools/tactics-assets/concepts/animals/inventory.json').read_text())['families']};palettes={c['id']:c['palette'] for c in json.loads((B/'normalization.json').read_text())['creatures']};forms={c.id:set(c.forms) for c in beasts.CATALOGUE+bestiary.CATALOGUE};out=TARGET/'content/tactics/base/creatures';out.mkdir(exist_ok=True);stage=out/'.staging';stage.mkdir(exist_ok=True)
 for stale in stage.glob('*.png'):stale.unlink()
 existing=json.loads((out/'manifest.json').read_text()) if (out/'manifest.json').exists() else {'sprites':{}}
 sprites={k:v for k,v in existing['sprites'].items() if k.startswith('fauna_')};pending=[];issues=[];preserved=[];legacy_overrides={};report=[]
 for c in layout['families']:
  k=c['id'];w,h=c['canvas'];ax,ay=c['anchor'];ready=k=='wisp' or all(len(list((R/'polish'/k/'attack'/d).glob('frame-*.png')))==5 and (R/'polish'/k/'down'/(d+'.png')).exists() for d in DIRS)
  if not ready:pending.append(k);continue
  directions=[];issue_start=len(issues)
  for d in DIRS:
   pose=Image.open(R/'canonical'/k/'poses'/(d+'.png')).convert('RGBA');idle=[Image.open(p).convert('RGBA') for p in sorted((R/'canonical'/k/'animations'/d/'idle').glob('frame-*.png'))][:-1];move=[Image.open(p).convert('RGBA') for p in sorted((R/'canonical'/k/'animations'/d/'move').glob('frame-*.png'))][:-1]
   assert len(idle)==4 and len(move)==8
   if k=='wisp':
    attack=move[::2];a=np.array(pose);a[:,:,3]=np.where(a[:,:,3]>=192,64,0);a[a[:,:,3]==0]=0;down=Image.fromarray(a)
   else:
    attack=[]
    repair=R/'polish'/k/'attack-repair'/d
    attack_source=repair if len(list(repair.glob('frame-*.png')))==5 else R/'polish'/k/'attack'/d
    for i,p in enumerate(sorted(attack_source.glob('frame-*.png'))[:-1]):
     im=pose if i==0 else CLEAN(MAPPED(Image.open(p),palettes[k]))[0]
     bb=im.getbbox()
     if bb is not None and (bb[0]==0 or bb[1]==0 or bb[2]==im.width or bb[3]==im.height):issues.append({'family':k,'direction':d,'frame':'attack-'+str(i),'issue':'native source edge contact'})
     if attack_source==repair and i>0:
      metadata=repair.parent/(d+'-translation.json')
      offset=json.loads(metadata.read_text())['input_translation'] if metadata.exists() else [(im.width-w)//2,(im.height-h)//2]
      padded=Image.new('RGBA',(w+256,h+256));padded.alpha_composite(im,(128-offset[0],128-offset[1]));im=padded
     attack.append(im)
    down=CLEAN(MAPPED(Image.open(R/'polish'/k/'down'/(d+'.png')),palettes[k]))[0]
    bb=down.getbbox()
    if bb[0]==0 or bb[1]==0 or bb[2]==down.width or bb[3]==down.height:issues.append({'family':k,'direction':d,'frame':'down','issue':'source edge contact'})
    dy=ay-bb[3]+1
    # A single defeated pose is aligned once to the same ground point, never per-frame centered.
    canvas=Image.new('RGBA',(w,h));canvas.alpha_composite(down,(0,dy));down=canvas
   frames=idle+move+attack+[pose,down]
   for n,im in enumerate(frames):
    assert im.size in [(w,h),(w+32,h+32),(w+256,h+256)],(k,d,n,im.size,(w,h))
    bb=im.getbbox()
    if bb is not None and (bb[0]==0 or bb[1]==0 or bb[2]==im.width or bb[3]==im.height):issues.append({'family':k,'direction':d,'frame':n,'issue':'edge contact'})
   directions.append(frames)
  if len(issues)>issue_start:
   pending.append(k);continue
  facing_offsets={}
  if k not in ['bat','harpy','wisp']:
   registered=[]
   for d,fs in zip(DIRS,directions):
    shift=ay-(fs[0].getbbox()[3]-1);facing_offsets[d]=[0,shift];aligned=[]
    for n,im in enumerate(fs):
     canvas=Image.new('RGBA',(w+256,h+256))
     origin=(0,0) if im.size==(w+256,h+256) else (128,128)
     canvas.alpha_composite(im,(origin[0],origin[1]+(0 if n==17 else shift)));aligned.append(canvas)
    registered.append(aligned)
   directions=registered
  finalw=max(im.width for fs in directions for im in fs);finalh=max(im.height for fs in directions for im in fs)
  if (finalw,finalh)!=(w,h):
   padded=[]
   for fs in directions:
    target=[]
    for im in fs:
     canvas=Image.new('RGBA',(finalw,finalh));canvas.alpha_composite(im,((finalw-im.width)//2,(finalh-im.height)//2));target.append(canvas)
    padded.append(target)
   directions=padded;ax+=(finalw-w)//2;ay+=(finalh-h)//2;w,h=finalw,finalh
  boxes=[im.getbbox() for fs in directions for im in fs if im.getbbox()];left=max(0,min(b[0] for b in boxes)-2);top=max(0,min(b[1] for b in boxes)-2);right=min(w,max(max(b[2] for b in boxes),ax+1)+2);bottom=min(h,max(max(b[3] for b in boxes),ay+1)+2);crop=(left,top,right,bottom);cw,ch=right-left,bottom-top
  atlas=Image.new('RGBA',(cw*8,ch*24))
  for row,fs in enumerate(directions):
   for n,im in enumerate(fs):atlas.alpha_composite(im.crop(crop),((n%8)*cw,(row*3+n//8)*ch))
  source=inv[k];pack=source['source_pack'];ref=source['reference_variant'];oldpath=SOURCE_REPO/source['source_file'];old=Image.open(oldpath).convert('RGBA').crop((0,0,*[source['source_frame'][v] for v in ['width','height']]))
  for variant,file in source['source_variants'].items():
   if variant in forms.get(k,set()) and variant!=ref:
    artkey=pack+'.'+k+'.'+variant;preserved.append(artkey)
    original=Image.open(SOURCE_REPO/'content/tactics/base'/pack/file).convert('RGBA').crop((0,0,old.width,old.height));ob=original.getbbox();basebb=old.getbbox();refbb=directions[7][0].getbbox();legacy_scale=(refbb[3]-refbb[1])*SCALES[k]/(basebb[3]-basebb[1])
    legacy_overrides[artkey]={'pixelScale':round(legacy_scale,6),'headTop':ob[1],'footprintWidth':round((ob[2]-ob[0])*legacy_scale,3),'familyScale':SCALES[k],'note':'Original distinct-form art retained; physical scale follows the new primary specimen.'}
    continue
   basename=k+'_'+variant+'.png';scale=scale_for(k,variant)
   if variant==ref:variant_atlas=atlas
   else:
    other=Image.open(SOURCE_REPO/'content/tactics/base'/pack/file).convert('RGBA').crop((0,0,old.width,old.height));variant_atlas=recolor(atlas,recolor_map(old,other))
   save_exact_png(variant_atlas,stage/basename)
   keys=[pack+'.'+k+'.'+variant]+([pack+'.'+k] if variant==ref else [])
   sprites[k+'_'+variant]={'file':basename,'frame':{'width':cw,'height':ch,'anchor':[ax-left,ay-top]},'portrait':portrait_rect(directions[0][0].crop(crop)),'headTop':max(0,directions[0][0].getbbox()[1]-top),'pixelScale':scale,'unlit':k=='wisp','columns':8,'rowsPerDirection':3,'directions':DIRS,'facingOffsets':facing_offsets,'footprintWidth':max(b[2]-b[0] for b in boxes)*scale,'footprintHeight':max(1,ay-min(b[1] for b in boxes))*scale,'keys':keys,'anims':ANIMS,'family':k,'variant':variant,'provenance':'Scenario concepts, PixelLab authored rotations and motion; original game palette roles retained.'}
  report.append({'family':k,'crop':list(crop),'native_frame':[cw,ch],'anchor':[ax-left,ay-top],'facings':8,'frames_per_direction':18})
 if issues and '--partial' not in sys.argv:raise SystemExit('Repair edge contact before export: '+json.dumps(issues))
 if pending and '--partial' not in sys.argv:raise SystemExit('Combat poses still pending: '+','.join(pending))
 data={'version':1,'status':'partial runtime export' if pending else 'integrated art','directions':DIRS,'sprites':sprites,'legacyDistinctForms':preserved,'legacyOverrides':legacy_overrides,'pending':pending,'notes':'Distinct structural forms retain their original authored art instead of losing riders, ribs, crystals or other identifying features.'}
 for f in stage.glob('*.png'):f.replace(out/f.name)
 temp=out/'manifest.json.tmp';temp.write_text(json.dumps(data,indent=2)+'\n');temp.replace(out/'manifest.json');(R/'runtime-export.json').write_text(json.dumps({'families':report,'palette_variants_exported':len(sprites),'preserved_distinct_forms':preserved,'pending':pending,'issues':issues},indent=2)+'\n');print('Exported',len(report),'families,',len(sprites),'palette variants. Pending:',pending)
if __name__=='__main__':main()
