"""Build native bridge modules and isolated TV packs; never mutate live content."""
from __future__ import annotations
import argparse, copy, hashlib, json, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[4]
sys.path.insert(0,str(ROOT/'tools/tactics-assets'))
from parts import ford_bridge as B, town as T


def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--out',required=True,type=Path);a=ap.parse_args();out=a.out.resolve()
    live=ROOT/'content/tactics/base'
    if out==ROOT or out==live or live in out.parents:raise ValueError('Use external scratch stage')
    out.mkdir(parents=True,exist_ok=True)
    protected={str(p.relative_to(live)):digest(p) for p in live.rglob('*') if p.is_file() and p.suffix in ('.png','.json')}
    style=json.loads((ROOT/'tools/tactics-assets/pixellab/wardenflame-style.json').read_text());palette=style['palettes']['ashen_ford']['palette']
    rgb=[tuple(int(c[k:k+2],16) for k in (1,3,5)) for c in palette]
    pal=Image.new('P',(1,1));pal.putpalette([v for c in (rgb+[rgb[0]]*(256-len(rgb))) for v in c])
    def indexed(im):
        alpha=im.getchannel('A');im=im.convert('RGB').quantize(palette=pal,dither=Image.Dither.NONE).convert('RGBA');im.putalpha(alpha);return im
    def render(model,bounds=None):
        r=T.render(model,bounds=bounds);r.image=indexed(r.image);return r
    models=[B.assembly(s) for s in B.STATES];bounds=T.union(T.model_bounds(m) for m in models)
    review=out/'review';review.mkdir(exist_ok=True)
    font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',21)
    sheet=Image.new('RGB',(1100,1380),'#34313f');draw=ImageDraw.Draw(sheet)
    for i,(state,model) in enumerate(zip(B.STATES,models)):
        r=render(model,bounds);r.image.save(review/(state+'.png'));im=r.image.resize((r.image.width*2,r.image.height*2),Image.Resampling.NEAREST)
        draw.text((22,12+i*460),{'fortified':'Skarrow fortified','force':'After force','parley':'After parley'}[state],font=font,fill='#eddfbe');sheet.paste(im,(32,44+i*460),im)
    sheet.save(review/'native-states.png')
    # Native frame and tile-sized pieces accompany the assembly view.
    makers=dict(B.BUILDERS)
    span=B.join(T.Model(3,2,ground_shadow=False),B.support());B.join(span,B.deck());makers['warden_bridge_span']=lambda:span
    for name,fn in [('warden_bridge_parapet',B.parapet),('warden_hide_screen',B.hide_screen),('warden_hide_roll',B.rolled_hide)]:
        for edge,delta in [('rear',-16),('front',16)]:
            def shifted(fn=fn,delta=delta):
                md=fn();return B.join(T.Model(1,1,ground_shadow=False),md,(0,delta,0))
            makers[name+'_'+edge]=shifted
    rendered={k:render(fn()) for k,fn in makers.items()}
    gh=[B.gatehouse(s) for s in B.STATES];ghbounds=T.union(T.model_bounds(m) for m in gh)
    shelter=[B.shelter(s) for s in B.STATES];shbounds=T.union(T.model_bounds(m) for m in shelter)
    sources=out/'pixels';sources.mkdir(exist_ok=True)
    assets={}
    for name,r in rendered.items():
        p=sources/(name+'.png');r.image.save(p);assets[name]={'file':p.name,'size':list(r.image.size),'origin':list(r.origin),'anchor':[r.origin[0],r.origin[1]+16],'sha256':digest(p)}
    for prefix,ms,bs in [('warden_gatehouse',gh,ghbounds),('warden_shelter',shelter,shbounds)]:
        for state,m in zip(B.STATES,ms):
            r=render(m,bs);p=sources/(prefix+'_'+state+'.png');r.image.save(p);r.shadow.save(sources/(prefix+'_'+state+'_shadow.png'));assets[p.stem]={'file':p.name,'size':list(r.image.size),'origin':list(r.origin),'anchor':[r.origin[0],r.origin[1]+16],'sha256':digest(p)}
    # Source pieces use the same palette, have transparent margins, and stay unblurred.
    validations={}
    for key,item in assets.items():
        im=Image.open(sources/item['file']).convert('RGBA');ar=np.asarray(im);opaque=ar[:,:,3]>0
        assert set(np.unique(ar[:,:,3])).issubset({0,255}),key
        assert not any(edge.any() for edge in [ar[0,:,3],ar[-1,:,3],ar[:,0,3],ar[:,-1,3]]),key
        colours=set(map(tuple,ar[:,:,:3][opaque]));assert colours.issubset(set(rgb)),key
        validations[key]={'binary_alpha':True,'transparent_margins':True,'palette_colours':len(colours),'anchor':item['anchor']}
    for state in B.STATES:
        pack=out/state/'content/tactics/base'
        if not pack.exists():shutil.copytree(live,pack)
        props=json.loads((live/'props/manifest.json').read_text());town=json.loads((live/'town/manifest.json').read_text())
        for name,entry in assets.items():
            if name.startswith('warden_gatehouse_'):continue
            filename=entry['file'];shutil.copy2(sources/filename,pack/'props'/filename)
            # Tall screens/gates must keep their upright ordering, separate from low decks.
            height=3 if 'hide_screen' in name else 2 if any(v in name for v in ['barricade','open_gate','shelter']) else 1
            props['props'][name]={'variants':{'ashen_ford':filename},'size':entry['size'],'anchor':entry['anchor'],'frames':1,'fps':0.,'height':height,'blocks':False,'cover':'none','tags':['review-only','warden-bridge']}
        # Native town building, using calibrated origin and shared bounds across all states.
        r=render(gh[B.STATES.index(state)],ghbounds);name='warden_gatehouse_'+state;item=assets[name]
        for suffix in ('.png','_shadow.png'):shutil.copy2(sources/(name+suffix),pack/'town/buildings'/(name+suffix))
        town['buildings']['warden_gatehouse']={'variants':{'ashen_ford':{'image':'buildings/'+name+'.png','shadow':'buildings/'+name+'_shadow.png'}},'size':list(r.image.size),'origin':list(r.origin),'anchor':list(r.anchor),'footprint':[2,2],'height':6,'frames':1,'fps':0.,'blocks':[[1,1],[1,1]],'cover':'full','walkable':False,'tags':['review-only','warden-bridge'],'attach':{'door':[list(r.attach['door'][0])]}}
        (pack/'props/manifest.json').write_text(json.dumps(props,indent=2)+'\n');(pack/'town/manifest.json').write_text(json.dumps(town,indent=2)+'\n')
        # Review-only sprites draw the native town geometry without placing a building
        # inside the stamp, which the production content format currently forbids.
        name='warden_gatehouse_'+state;item=assets[name]
        shutil.copy2(sources/item['file'],pack/'props'/item['file'])
        props['props']['warden_gatehouse_review']={'variants':{'ashen_ford':item['file']},'size':item['size'],'anchor':[r.origin[0],r.origin[1]+48],'height':6,'frames':1,'fps':0.,'blocks':False,'cover':'none','tags':['review-only']}
        rr=render(span)
        props['props']['warden_bridge_span']['anchor']=[rr.origin[0]+32,rr.origin[1]+64]
        props['props']['warden_bridge_span']['height']=3
        (pack/'props/manifest.json').write_text(json.dumps(props,indent=2)+'\n')
        # One identical staged topology for all views. Changes are NOT production map changes.
        import base64
        mp=json.loads((live/'rules/maps/river_crossing.json').read_text());b=bytearray(base64.b64decode(mp['tiles']));w,h=mp['size'];mp['materials'].extend(['planks_ford/oak','cobble/grey']);mat=len(mp['materials'])-2;road=len(mp['materials'])-1
        for y in (9,10):
            for x in (5,6,7):b[(y*w+x)*3:(y*w+x)*3+3]=bytes([2,mat,0])
        # Continuous road -> west ramp -> deck -> east ramp -> east road.
        for y in (9,10):
            for x,height in [(0,0),(1,0),(2,0),(3,0),(4,1),(8,2),(9,1),(10,1),(11,1),(12,1),(13,1)]:
                b[(y*w+x)*3:(y*w+x)*3+3]=bytes([height,road,0])
        # Turn the east approach into the existing east-bank road at the exit.
        for y in range(5,11):
            for x in (12,13):b[(y*w+x)*3:(y*w+x)*3+3]=bytes([1,road,0])
        mp['tiles']=base64.b64encode(b).decode();mp['props']=[p for p in mp['props'] if not p['id'].startswith('bridge_span') and not (p['id']=='crate' and p['at']==[9,9])]
        # The review assembly is a single native render of the separate pieces.
        # This is ONLY an art/scale preview: unit occlusion and per-tile installation
        # remain implementation work after pixel approval, never silently accepted.
        image=review/(state+'.png');filename='warden_review_assembly_'+state+'.png'
        shutil.copy2(image,pack/'props'/filename)
        props['props']['warden_review_assembly']={'variants':{'ashen_ford':filename},'size':list(Image.open(image).size),'anchor':[-bounds[0]+96,-bounds[1]+96],'height':6,'frames':1,'fps':0.,'blocks':False,'cover':'none','tags':['review-only','not-for-installation']}
        (pack/'props/manifest.json').write_text(json.dumps(props,indent=2)+'\n')
        mp['props'].append({'id':'warden_review_assembly','variant':'ashen_ford','at':[9,10],'blocks':False,'cover':'none'})
        (pack/'rules/maps/river_crossing.json').write_text(json.dumps(mp,indent=2)+'\n')
        # Native town strips sort the structural span across both footprint axes.
        rr=render(span);fn='warden_bridge_structure.png';rr.image.save(pack/'town/buildings'/fn);rr.shadow.save(pack/'town/buildings'/'warden_bridge_structure_shadow.png')
        town['buildings']['warden_bridge_structure']={'variants':{'ashen_ford':{'image':'buildings/'+fn,'shadow':'buildings/warden_bridge_structure_shadow.png'}},'size':list(rr.image.size),'origin':list(rr.origin),'anchor':list(rr.anchor),'footprint':[3,2],'height':1,'drop':32,'attach':{},'frames':1,'fps':0.,'blocks':[[0,0,0],[0,0,0]],'cover':'none','walkable':True,'tags':['review-only','bridge']}
        (pack/'town/manifest.json').write_text(json.dumps(town,indent=2)+'\n')
        region=json.loads((live/'rules/world/ashen_ford.json').read_text());region['buildings']=list(region['buildings'])
        # WorldGround intentionally reads the baked region tiles, not map tiles.
        # Keep ALL stamp bytes equivalent, including the +1 world height offset.
        for material in mp['materials']:
            if material not in region['materials']:region['materials'].append(material)
        for chunk in region['chunks']:
            data=bytearray(base64.b64decode(chunk['tiles']));changed=False
            for yy in range(h):
                for xx in range(w):
                    wx,wy=100+xx,52+yy
                    if wx//32!=chunk['x'] or wy//32!=chunk['y']:continue
                    i=(yy*w+xx)*3;j=((wy%32)*32+wx%32)*3
                    data[j:j+3]=bytes([b[i]+1,region['materials'].index(mp['materials'][b[i+1]]),b[i+2]]);changed=True
            if changed:chunk['tiles']=base64.b64encode(data).decode()

        # Join the existing town cobbles at x=98 through the west stamp edge.
        roadmat=region['materials'].index('cobble/grey')
        for chunk in region['chunks']:
            if chunk['x']!=99//32 or chunk['y']!=61//32:continue
            data=bytearray(base64.b64decode(chunk['tiles']))
            for wy in (61,62):data[((wy%32)*32+99%32)*3+1]=roadmat
            chunk['tiles']=base64.b64encode(data).decode()
        # The fortification scene is only for visual review; story gating is a later step.
        (pack/'rules/world/ashen_ford.json').write_text(json.dumps(region,indent=2)+'\n')
    assert all(digest(live/p)==v for p,v in protected.items()),'Live pack changed'
    (review/'assets.json').write_text(json.dumps(assets,indent=2)+'\n');(review/'validation.json').write_text(json.dumps({'assets':validations,'live_png_json_unchanged':len(protected),'palette_source':'wardenflame-style.json#palettes.ashen_ford','production_ready':False},indent=2)+'\n')
    print(json.dumps({'stage':str(out),'pieces':len(assets),'states':list(B.STATES),'live_files_unchanged':len(protected)}))

if __name__=='__main__':main()
