# Audit overworld art coverage and render a searchable review.
"""Rebuild the per-asset overworld review and standalone searchable gallery.

python3 tools/tactics-assets/dev/audit_overworld.py --before /path/to/previous/sets
Unknown asset families, missing art, malformed strips and unreviewed PNGs fail.
The gallery reports library readiness separately from actual region placement.
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import argparse
import base64
from collections import Counter
import hashlib
import html
import io
import json
from pathlib import Path
from PIL import Image
from arcpix.out import PACK
from parts.overworld_review import assessment

REPORT=Path(__file__).resolve().parents[3]/'docs/superpowers/reports/overworld-art-review'


def inventory():
    records=[]
    by_path={}
    def add(section,name,variant,path,size,frames=1,anchor=None,role='',**extra):
        where,note=assessment(section,name,variant,extra.get('replaces'))
        record=dict(id=f'{section}/{name}/{variant}'.rstrip('/'),section=section,
            family=name,variant=variant,path=f'{section}/{path}',size=size,frames=frames,
            anchor=anchor,role=role,where=where,assessment=note,**extra)
        if record['path'] in by_path:
            original=by_path[record['path']]
            if original['size']!=size or original['frames']!=frames or original['anchor']!=anchor:
                raise ValueError(f'Conflicting image alias metadata: {record["path"]}')
            original.setdefault('aliases',[]).append(record['id'])
        else:
            by_path[record['path']]=record
            records.append(record)
    for section in ('terrain','props','town','ambient','worldmap'):
        m=json.loads((PACK/section/'manifest.json').read_text())
        if section in ('terrain','town'):
            for name,item in m['textures'].items():
                for var,path in item['variants'].items():
                    add(section,name,var,path,item['size'],item.get('frames',1),role=item['kind'])
        if section=='props':
            for name,item in m['props'].items():
                for var,path in item['variants'].items():
                    look=item.get('looks',{}).get(var,{})
                    add(section,name,var,path,look.get('size',item['size']),item['frames'],look.get('anchor',item['anchor']),
                        role='sprite',blocks=item['blocks'],cover=item['cover'])
                if item.get('light',{}).get('emissive'):
                    add(section,name,'emissive-mask',item['light']['emissive'],item['size'],item['frames'],role='emissive mask')
        if section=='town':
            for name,item in m['buildings'].items():
                for var,files in item['variants'].items():
                    for role,path in files.items():
                        add(section,name,var+('' if role=='image' else '-'+role),path,
                            item['size'],1 if role=='shadow' else item['frames'],item.get('anchor'),role=role,
                            footprint=item.get('footprint'),blocks=item.get('blocks'))
            for name,item in m['overlays'].items():
                add(section,name,'overlay',item['image'],item['size'],item['frames'],item['anchor'],role='attachment')
        if section=='ambient':
            for name,item in m['assets'].items():
                for var,path in item['variants'].items():
                    add(section,name,var,path,item['frame'],item['frames'],item.get('anchor'),
                        role=item['kind'],replaces=item.get('replaces'),states=item.get('states'))
        if section=='worldmap':
            for name,path in m['icon']['files'].items():
                add(section,name,'',path,m['icon']['size'],m['icon']['animated'].get(name,{}).get('frames',1),m['icon']['anchor'],role='map icon')
            for name,path in (('parchment',m['parchment']['file']),('sample_map',m['sample'])):
                with Image.open(PACK/section/path) as im:size=list(im.size)
                add(section,name,'',path,size,role='style specimen')
    # Earlier crow sheets remain shipped for provenance but are no longer manifest entries.
    for name,frames in (('wild_crow',1),('wild_crow_anim',4)):
        add('props',name,'retired',name+'.png',[34,32],frames,
            role='retired wildlife sheet',runtime=False)
    return records


def region_usage():
    counts=Counter()
    for path in (PACK/'rules/world').glob('*.json'):
        m=json.loads(path.read_text())
        for chunk in m['chunks']:
            raw=base64.b64decode(chunk['tiles'])
            for n in range(0,len(raw),3):counts[m['materials'][raw[n+1]]]+=1
    return counts


def review(before=None,output=REPORT):
    records=inventory();usage=region_usage();covered=set();cards=[]
    for record in records:
        path=PACK/record['path'];covered.add(record['path'])
        w,h=record['size'];frames=record['frames']
        with Image.open(path) as im:
            # Rendering uses logical sizes with full-frame UVs; imported textures may be denser.
            density=im.height/h
            if density<1 or density!=int(density) or im.width!=w*frames*density:
                raise ValueError(f'{path}: {im.size} is not a uniform integer-density strip of {(w*frames,h)}')
            record['textureDensity']=int(density)
            if not im.convert('RGBA').getchannel('A').getbbox():raise ValueError(f'Empty asset: {path}')
            if record['anchor']:
                ax,ay=record['anchor']
                if not (0<=ax<w and 0<=ay<h):raise ValueError(f'{path}: anchor outside frame')
            thumb=im.crop((0,0,int(w*density),int(h*density))).convert('RGBA')
            scale=min(3,280/w,150/h)
            if scale>=1:scale=int(scale)
            thumb=thumb.resize((max(1,round(w*scale)),max(1,round(h*scale))),Image.Resampling.NEAREST)
            buf=io.BytesIO();thumb.save(buf,format='PNG')
        record['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
        if before:
            old=before/record['path']
            record['change']='added' if not old.exists() else 'refined' if hashlib.sha256(old.read_bytes()).hexdigest()!=record['sha256'] else 'kept'
        else:record['change']='reviewed'
        section=record['section']
        material=f'{record["family"]}/{record["variant"]}'
        if section=='terrain' and record['role']=='top':
            record['placement']=f'{usage[material]} tiles in saved regions; battle maps may also use it.'
        elif section=='terrain':record['placement']='Selected by material renderer or explicitly placed decal; not a standalone region prop.'
        else:record['placement']='Library asset; placement must be checked in the saved region and live walking renderer.'
        if record.get('runtime') is False:record['placement']='Retained source sheet, unreferenced by live manifests; current wildlife uses field_crow.'
        if record.get('replaces'):
            record['assessment']+=' Animated replacement: geometry/anchor must agree with the named static prop.'
            static=json.loads((PACK/'props/manifest.json').read_text())['props'][record['replaces']]
            if static['size']!=record['size'] or static['anchor']!=record['anchor']:
                raise ValueError(f'Replacement misalignment: {record["id"]}')
        if record.get('states'):
            for name,state in record['states'].items():
                if isinstance(state,list) and not all(0<=n<frames for n in state):raise ValueError(f'Invalid state: {record["id"]}/{name}')
        e=lambda key:html.escape(str(record[key]))
        search=html.escape(' '.join(str(v) for v in record.values()).lower(),quote=True)
        cards.append(f'<article data-search="{search}" data-section="{section}" data-change="{record["change"]}"><div class="preview"><img alt="{e("id")}" src="data:image/png;base64,{base64.b64encode(buf.getvalue()).decode()}"></div><h2>{e("id")}</h2><b>{e("change")} · {e("role")}</b><p>{w}×{h} · {frames} frames · anchor {e("anchor")}</p><h3>Where it fits</h3><p>{e("where")}</p><p>{e("assessment")}</p><h3>Current placement</h3><p>{e("placement")}</p></article>')
    actual={str(p.relative_to(PACK)) for section in ('terrain','props','town','ambient','worldmap') for p in (PACK/section).rglob('*.png')}
    if actual-covered:raise ValueError(f'Unreviewed images: {sorted(actual-covered)}')
    output.mkdir(parents=True,exist_ok=True)
    (output/'assets.json').write_text(json.dumps(records,indent=2)+'\n')
    template='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Wardenflame · overworld asset review</title><style>
    body{background:#101827;color:#dce2ed;font:16px system-ui;margin:24px}header{max-width:1000px}h1{color:#f3dfae}h2{font-size:15px;overflow-wrap:anywhere}h3{font-size:13px;color:#e4bb68;margin-bottom:0}p{line-height:1.5;font-size:13px}nav{position:sticky;top:0;background:#101827;padding:15px 0;z-index:2;display:flex;gap:12px;flex-wrap:wrap}input,select{padding:10px;background:#243348;color:white;border:1px solid #62718a;border-radius:5px}#grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(290px,1fr));gap:16px}article{background:#1a2639;border:1px solid #384860;border-radius:8px;padding:16px}article[hidden]{display:none}.preview{height:155px;display:flex;align-items:center;justify-content:center;background:#26344a}.preview img{max-width:100%;max-height:150px;image-rendering:pixelated}b{color:#d8bf86;font-size:13px}</style>
    <header><h1>Wardenflame · overworld asset review</h1><p>One card per shipped PNG, including animation strips, shadow/light masks, town attachments and map icons. Decisions follow the asset-by-asset visual review against the characters and Embers of the Vale lore.</p><p><strong>Scope:</strong> This inventory validates library art and metadata. Ashen Ford has authored props, buildings, effects and a live walking renderer; recommendations below do not prove scene placement. Check the saved region and in-game views for actual use. Retired source sheets are identified explicitly.</p></header>
    <nav><input id="search" type="search" placeholder="Find asset, region or issue" aria-label="Find assets"><select id="section" aria-label="Asset set"><option value="">All sets</option><option>terrain</option><option>props</option><option>town</option><option>ambient</option><option>worldmap</option></select><select id="change" aria-label="Change"><option value="">All changes</option><option>refined</option><option>added</option><option>kept</option></select><span id="count"></span></nav><main id="grid">CARDS</main><script>
    const cards=[...document.querySelectorAll('article')],search=document.getElementById('search'),section=document.getElementById('section'),change=document.getElementById('change');function filter(){let n=0;for(const c of cards){c.hidden=!(c.dataset.search.includes(search.value.toLowerCase())&&(!section.value||c.dataset.section===section.value)&&(!change.value||c.dataset.change===change.value));if(!c.hidden)n++}document.querySelector('#count').textContent=n+' / '+cards.length+' assets'}for(const id of ['search','section','change'])document.getElementById(id).addEventListener('input',filter);filter();</script></html>'''
    (output/'index.html').write_text(template.replace('CARDS','\n'.join(cards)))
    print(f'Reviewed {len(records)} assets; all {len(actual)} shipped PNGs accounted for. Changes: {dict(Counter(r["change"] for r in records))}')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--before',type=Path);p.add_argument('--output',type=Path,default=REPORT)
    args=p.parse_args();review(args.before,args.output)
