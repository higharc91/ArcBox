"""Translate Scenario concepts at the integrated tactical party's pixel density."""
import json,base64
from pathlib import Path
R=Path(__file__).resolve().parent
SIZES={'mule':[96,96],'goat':[64,64],'fox':[64,48],'duck':[48,40],'crow':[40,32],'dog':[64,48],'sheep':[64,56]}
def main():
 plan=json.loads((R/'plan.json').read_text());concepts=json.loads((R/'scenario-complete.json').read_text());data=[];public=[]
 for i,c in enumerate(plan['families']):
  k=c['id'];concept=next(v for v in concepts['records'] if v['id']==k);w,h=SIZES[k]
  desc='ONE complete '+k+' pixel sprite in true STRAIGHT-ON SOUTH view, facing the viewer, low top-down camera. Match the main animal from the Scenario concept; ignore its human, inset and secondary poses. '+c['notes']+' Crisp 1-pixel selective dark outlines and stepped warm top-left light/cool shadows, no blur or antialiasing. Exactly '+('two legs and two folded feathered wings' if k in ['crow','duck'] else 'four natural jointed legs')+'. Keep all ears, tail, beak, hooves and straps attached. No scenery, shadows, lettering, particles, extra animals or decorative effects. Preserve believable animal proportions. Human game reference height is 37 world pixels = 74 authoring pixels; this sprite draws at half scale. Animal visible heights (AUTHORING px) '+str({a:round(b*37/29*2) for a,b in c['target_visible_dimensions_px'].items()})+'. Frame '+str(w)+'x'+str(h)+' authoring pixels. Do not enlarge this small animal just to fill the canvas. Feet near y='+str(h-7)+', full silhouette and 5+ transparent pixels of margin.'
  args={'description':desc,'width':w,'height':h,'no_background':True,'reference_images':[{'url':concept['download']['url'],'usage':'Animal identity, coat markings and equipment from MAIN specimen only. Ignore all other panels and background.'}],'style_image_url':json.loads((R.parent/'animal-concepts/style-upload.json').read_text())['ref'],'style_copy':['outline','detail','shading'],'max_colors':32,'seed':180000+i}
  key=k+'/base';q={'key':key,'name':'create_image_pro','arguments':args};data.append(q);public.append({**q,'arguments':{a:b for a,b in args.items() if a not in ['reference_images']},'concept_asset_id':concept['result']['assetIds'][0],'canvas':[w,h],'pixel_scale':.5})
 Path('/workspace/requests-overworld-pixels.json').write_text(json.dumps(data));(R/'pixel-requests.json').write_text(json.dumps(public,indent=2)+'\n');print('Prepared',len(data),'concept-led native pixel sprites.')
if __name__=='__main__':main()
