"""Integrate eight-view world actors and native-size editor/fallback strips."""
import json,sys
from pathlib import Path
from PIL import Image
R=Path(__file__).resolve().parent;TARGET=Path(sys.argv[1]) if len(sys.argv)>1 else next(p for p in R.parents if (p/'content/tactics/base').exists());PACK=TARGET/'content/tactics/base'
sys.path.insert(0,str(next(p for p in R.parents if (p/'content/tactics/base').exists())/'tools/tactics-assets'))
from pixel_atlas import save_exact_png,portrait_rect
IDS={'mule':('pack_mule','grey'),'goat':('farm_goat','rust'),'fox':('red_fox','russet'),'duck':('river_duck','mallard'),'crow':('field_crow','charcoal'),'dog':('farm_dog','brown'),'sheep':('farm_sheep','cream')}
def main():
 layout=json.loads((R/'canonical-layout.json').read_text());validation=json.loads((R/'animation-validation.json').read_text());complete={c['key'] for c in validation['loops']};bad={c['key'] for c in validation['errors']};output=PACK/'creatures';output.mkdir(exist_ok=True);p=output/'manifest.json';manifest=json.loads(p.read_text()) if p.exists() else {'version':1,'directions':layout['directions'],'sprites':{}}
 propspath=PACK/'props/manifest.json';props=json.loads(propspath.read_text());report=[];pending=[]
 for c in layout['families']:
  k=c['id'];scale=.4 if k=='crow' else .5;prop,variant=IDS[k];required=[k+'/animations/'+d+'/'+a for d in layout['directions'] for a in ['idle','move']+(['flap'] if k=='crow' else [])]
  if not all(key in complete and key not in bad for key in required):pending.append(k);continue
  grouped=[];images=[]
  for d in layout['directions']:
   actions={a:[Image.open(p).convert('RGBA') for p in sorted((R/'canonical'/k/'animations'/d/a).glob('frame-*.png'))][:-1] for a in ['idle','move']+(['flap'] if k=='crow' else [])}
   fs=actions['idle']+actions['move']+(actions['flap']+[actions['flap'][0]] if k=='crow' else []);grouped.append(fs);images+=fs
  w,h=c['canvas'];ax,ay=c['anchor'];registered=[];facing_offsets={};images=[]
  for d,fs in zip(layout['directions'],grouped):
   shift=ay-(fs[0].getbbox()[3]-1);facing_offsets[d]=[0,shift];aligned=[]
   for im in fs:
    canvas=Image.new('RGBA',(w,h));canvas.alpha_composite(im,(0,shift));aligned.append(canvas)
   registered.append(aligned);images+=aligned
  grouped=registered;bbs=[im.getbbox() for im in images];left=max(0,min(b[0] for b in bbs)-2)//2*2;top=max(0,min(b[1] for b in bbs)-2)//2*2;right=min(w,(max(max(b[2] for b in bbs),ax+1)+3)//2*2);bottom=min(h,(max(max(b[3] for b in bbs),ay+1)+3)//2*2);crop=(left,top,right,bottom);cw,ch=right-left,bottom-top;anchor=[ax-left,ay-top];rowcount=3 if k=='crow' else 2;atlas=Image.new('RGBA',(cw*8,ch*8*rowcount))
  for row,fs in enumerate(grouped):
   for n,im in enumerate(fs):atlas.alpha_composite(im.crop(crop),((n%8)*cw,(row*rowcount+n//8)*ch))
  filename='fauna_'+k+'.png';save_exact_png(atlas,output/filename);anims={'idle':{'frames':list(range(4)),'fps':1000/180,'loop':True},'walk':{'frames':list(range(4,12)),'fps':1000/110,'loop':True},'down':{'frames':[0],'fps':1,'loop':False}}
  if k=='crow':anims['flap']={'frames':list(range(12,17)),'fps':1000/110,'loop':False}
  manifest['sprites']['fauna_'+k]={'file':filename,'family':k,'variant':variant,'frame':{'width':cw,'height':ch,'anchor':anchor},'portrait':portrait_rect(grouped[0][0].crop(crop)),'headTop':grouped[0][0].getbbox()[1]-top,'pixelScale':scale,'columns':8,'rowsPerDirection':rowcount,'directions':layout['directions'],'facingOffsets':facing_offsets,'footprintWidth':max(b[2]-b[0] for b in bbs)*scale,'footprintHeight':(ay-min(b[1] for b in bbs))*scale,'keys':['fauna.'+prop,'fauna.'+prop+'.'+variant],'anims':anims,'provenance':'Scenario concept → PixelLab native sprite, true rotations and direction-specific animations.'}
  # Static/legacy readers consume native world pixels; live fauna uses the detailed eight-view atlas above.
  se=grouped[layout['directions'].index('south-east')];ew,eh=round(cw*scale),round(ch*scale);rest=se[0].crop(crop).resize((ew,eh),Image.Resampling.NEAREST);walk=Image.new('RGBA',(ew*8,eh))
  for n,im in enumerate(se[4:12]):walk.alpha_composite(im.crop(crop).resize((ew,eh),Image.Resampling.NEAREST),(n*ew,0))
  rest.save(PACK/'props'/(prop+'.png'));walk.save(PACK/'props'/(prop+'_anim.png'))
  for family,im,count in [(prop,rest,1),(prop+'_anim',walk,8)]:
   tags=['wildlife','bird'] if k in ['crow','duck'] else ['wildlife','woodland','mammal'] if k=='fox' else ['farm','animal']
   if count>1:tags+=['animation']
   props['props'][family]={'variants':{variant:family+'.png'},'size':[ew,eh],'anchor':[round(anchor[0]*scale),im.getbbox()[3]-1],'frames':count,'fps':1000/110 if count>1 else 0,'blocks':False,'cover':'none','height':1,'tags':tags}
  report.append({'id':k,'prop':prop,'frame':[cw,ch],'anchor':anchor,'pixel_scale':scale,'editor_frame':[ew,eh],'directions':8})
 if pending and '--partial' not in sys.argv:raise SystemExit('World animals still pending: '+','.join(pending))
 for path,data in [(p,manifest),(propspath,props)]:
  temp=path.with_suffix('.json.tmp');temp.write_text(json.dumps(data,indent=2)+'\n');temp.replace(path)
 (R/'runtime-export.json').write_text(json.dumps({'families':report,'pending':pending},indent=2)+'\n');print('Integrated overworld animals:',[c['id'] for c in report],'; pending:',pending)
if __name__=='__main__':main()
