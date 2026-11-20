"""Complete current-scenario token/monster audit, including runtime animation paths."""
import argparse,hashlib,json
from pathlib import Path
from PIL import Image
import tokens


def audit(pack,reference=tokens.REFERENCE):
    m=json.loads((pack/'tokens/manifest.json').read_text());required=tokens.requirements(reference)
    if set(m['tokens'])!=set(required):raise ValueError('Missing or unknown scenario token kind')
    if m.get('displayScale')!=2:raise ValueError('Display scale differs')
    files=set();entries=0
    def visit(v):
        nonlocal entries
        if isinstance(v,dict):
            if 'text' in v:raise ValueError('Painted text metadata is forbidden')
            if 'file' in v:
                name=v['file']
                if Path(name).name!=name or '..' in name:raise ValueError('Unsafe asset filename')
                p=pack/'tokens'/name;data=p.read_bytes();sha=hashlib.sha256(data).hexdigest()
                if sha!=v.get('sha256') or sha[:12] not in name:raise ValueError('Asset hash mismatch')
                if v.get('frameSize')!=[24,24]:raise ValueError('Token frame size differs')
                n=v.get('frames',1)
                if not isinstance(n,int) or n<1:raise ValueError('Invalid frame count')
                with Image.open(p) as im:
                    if im.size!=(24*n,24) or im.mode!='RGBA':raise ValueError('Strip dimensions/mode differ')
                files.add(name);entries+=1
            for c in v.values():visit(c)
        elif isinstance(v,list):
            for c in v:visit(c)
    visit(m)
    for key,spec in required.items():
        group=m['tokens'][key]
        if group.get('monster')!=spec['monster']:raise ValueError('Monster classification differs')
        if spec['monster']:
            if group.get('anchor')!=[12,22] or 'default' not in group:raise ValueError('Missing monster anchor/default')
            for clip,(n,fps,loop) in tokens.CLIPS.items():
                if set(group.get(clip,{}))!=set(tokens.SIDES):raise ValueError('Missing canonical monster facing')
                for side in tokens.SIDES:
                    e=group[clip][side]
                    if (e.get('frames'),e.get('fps'),e.get('loop'),e.get('reducedMotionFrame'))!=(n,fps,loop,0):raise ValueError('Clip timing/reduced frame differs')
        else:
            if 'idle' not in group:raise ValueError('Missing static fallback')
            for state in spec['states']:
                if state not in group:raise ValueError('Missing actual scenario state')
            if key in tokens.BASE and group['idle']!=group[tokens.BASE[key]]:raise ValueError('Default state alias drift')
    if m['tokens']['sabotage'].get('visibility')!='private' or m['tokens']['charge'].get('visibility')!='hidden-until-found':raise ValueError('Private visibility metadata differs')
    if m.get('extras',{}).get('hiddenMarker',{}).get('visibility')!='private-reference-only':raise ValueError('Hidden marker must remain private')
    return {'scenarioKinds':len(required),'monsters':sum(s['monster'] for s in required.values()),'scenarios':len(list((reference/'scenarios').glob('*.json'))),'uniquePngs':len(files),'facingClips':len(tokens.MONSTERS)*len(tokens.CLIPS)*len(tokens.SIDES),'declaredStates':sum(len(s['states']) for s in required.values())}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pack',type=Path,default=tokens.ROOT/'content/meridian/base');a=p.parse_args();print(json.dumps(audit(a.pack),indent=2))
