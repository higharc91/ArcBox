"""Targeted Ranger export. Preserve every non-Ranger sheet and every gameplay field."""
import json,re,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PIL import Image
from arcpix.out import PACK,save,write_json
from parts import fx
from parts.ranger_fx import DESIGNS,PASSIVES
from parts.ranger_casts import classic_aliases,manifest as cast_manifest
from parts.fx_packing import pack

def build():
    rules_path=PACK/'rules/abilities/ranger.json'
    rules=json.loads(rules_path.read_text())
    config_path=Path(__file__).resolve().parents[1]/'baseline/ranger-vfx.json'
    if config_path.exists():original=json.loads(config_path.read_text())
    else:
        original={a['id']:a.get('presentation',{}) for a in rules['abilities']}
        write_json(original,config_path)
    meta_path=PACK/'fx/manifest.json';meta=json.loads(meta_path.read_text())
    for effect in fx.CATALOGUE:
        if not effect.id.startswith('ranger_'):continue
        frames=effect.make();variants={}
        for variant,palette in effect.variants.items():
            images,anchor,trim=pack([c.render(fx.ramps(palette)) for c in frames],effect.anchor,effect.attach)
            w,h=images[0].size
            sheet=Image.new('RGBA',(w*len(frames),h))
            for i,im in enumerate(images):sheet.paste(im,(i*w,0))
            name=f'{effect.id}_{variant}.png';save(sheet,PACK/'fx'/name);variants[variant]=name
        meta['effects'][effect.id]={'name':effect.name,'group':effect.group,'frame':[w,h],
            'frames':len(frames),'fps':effect.fps,'loop':effect.loop,'anchor':list(anchor),
            'sourceFrame':[frames[0].w,frames[0].h],'trim':list(trim),
            'attach':effect.attach,'blend':effect.blend,'layer':effect.layer,'variants':variants,'default':next(iter(variants))}
    meta.setdefault('passives',{}).update(PASSIVES)
    write_json(meta,meta_path)
    # Only replace each presentation object, preserving the authored compact rules and all gameplay bytes.
    source=rules_path.read_text(); remap=json.loads((Path(__file__).resolve().parents[1]/'baseline/anim-remap.json').read_text())
    soundmeta=json.loads((PACK/'sfx/manifest.json').read_text())
    sounds={}
    for sid,entry in soundmeta['sounds'].items():
        if entry.get('group')=='spell':
            for key in re.findall(r'(?:fx|signature)\.[a-z0-9_]+(?:\.(?:[a-z0-9_]+|\*))?',entry.get('use','')):sounds.setdefault(key.removesuffix('.*'),sid)
    for ability in rules['abilities']:
        aid=ability['id'];name=aid.split('.')[-1];shape,material=DESIGNS[name];old=original[aid]
        anim='ranger_draw' if name in ('ensnaring_strike','hail_of_thorns','lightning_arrow','deadeye') else 'ranger_volley' if name in ('volley','piercing_shot','conjure_barrage') else 'ranger_vanish' if name=='vanish' else 'ranger_command' if aid.startswith('beastmaster.') or name=='hunters_mark' else 'ranger_ward'
        new={'anim':'characters.'+anim,'fx':f'fx.ranger_{name}.{material}'}
        shot=next((e for e in fx.CATALOGUE if e.id=='ranger_'+name+'_shot'),None)
        if shot:new['projectile']=f'fx.{shot.id}.{material}'
        cost=ability['cost'];tier=old.get('cutIn') or ('none' if cost.get('reaction') else 'full' if any(str(v).startswith('signature.') for v in old.values()) else 'short' if ability['tier']>0 or cost.get('uses') or any(cost.get('resource',{}).values()) else 'none')
        # Preserve signature promotion after replacing its signature.* key. Leave ordinary defaults dynamic,
        # so a cantrip that critically downs a foe can still receive the shared full cinematic.
        if 'cutIn' in old or tier=='full':new['cutIn']=tier
        remap[aid]=new['anim']
        pattern=r'("id"\s*:\s*"'+re.escape(aid)+r'"[\s\S]*?"presentation"\s*:\s*)\{[^{}]*\}'
        source,count=re.subn(pattern,lambda m:m[1]+json.dumps(new),source,count=1);assert count==1,aid
        oldfx=old.get('fx','');sid=sounds.get(oldfx) or sounds.get('.'.join(oldfx.split('.')[:2]))
        if sid:
            for variant in meta['effects']['ranger_'+name]['variants']:
                key=f'fx.ranger_{name}.{variant}'
                if key not in soundmeta['sounds'][sid]['use']:soundmeta['sounds'][sid]['use']+='; '+key
    rules_path.write_text(source)
    write_json(remap,Path(__file__).resolve().parents[1]/'baseline/anim-remap.json')
    write_json(soundmeta,PACK/'sfx/manifest.json')
    p=Path(__file__).resolve().parents[2]/'tactics-audio/sfx-list.json';audio_source=p.read_text();audio=json.loads(audio_source)
    for sid,entry in soundmeta['sounds'].items():
        if 'fx.ranger_' in entry['use'] and sid in audio:audio_source=audio_source.replace(json.dumps(audio[sid]['use']),json.dumps(entry['use']))
    p.write_text(audio_source)
    p=PACK/'characters/manifest.json';classic=json.loads(p.read_text());classic_aliases(classic['anims']);write_json(classic,p)
    p=PACK/'tactical/manifest.json';tactical=json.loads(p.read_text());tactical.setdefault('classes',{})['ranger']=cast_manifest();write_json(tactical,p)
    print('Exported Ranger effects, presentation keys, sound reuse and cast manifest.')

if __name__=='__main__':build()
