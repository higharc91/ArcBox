"""Strict complete UI-kit coverage, immutable files, dice layout and panel checks."""
import argparse,hashlib,json
from pathlib import Path
from PIL import Image
import ui

def audit(pack):
    m=json.loads((pack/'ui/manifest.json').read_text())
    if set(m['ui'])!=set(ui.IDS):raise ValueError('UI piece IDs differ')
    if m['displayScale']!=2:raise ValueError('UI art must display at 2x')
    dice=m['ui']['die']
    if dice['values']!=[0,0,1,1,1,2] or dice['faces']!=['face0','face0','face1','face1','face1','face2']:raise ValueError('Signal die faces differ')
    if not {'roll','face0','face1','face2'}<=dice.keys():raise ValueError('Missing die state')
    if dice['roll'].get('frames')!=4 or dice['roll'].get('fps')!=12:raise ValueError('Rolling layout differs')
    if set(m['lobby']['badges'])!={'seat'+str(i) for i in range(1,7)}:raise ValueError('Missing lobby seat badge')
    if set(m['extras']['miniMarkers'])!={'seat'+str(i) for i in range(1,7)}:raise ValueError('Missing mini-map marker')
    if m['extras']['auto'].get('roleNeutral') is not True:raise ValueError('Auto tag must be role-neutral')
    if not {'backdrop','banner','badges'}<=m['lobby'].keys() or 'breachBackdrop' not in m['extras']:raise ValueError('Missing phase/lobby art')
    files=set()
    def visit(v):
        if isinstance(v,dict):
            if 'text' in v:raise ValueError('Text must be live')
            if 'file' in v:
                p=pack/'ui'/v['file'];data=p.read_bytes();digest=hashlib.sha256(data).hexdigest()
                if digest!=v['sha256'] or digest[:12] not in p.name:raise ValueError('Hash mismatch')
                w,h=v['frameSize'];frames=v.get('frames',1)
                with Image.open(p) as image:
                    if image.size!=(w*frames,h):raise ValueError('Frame bounds differ')
                if 'insets' in v and (v['insets']!=[12]*4 or [w,h]!=[64,64]):raise ValueError('Panel insets differ')
                files.add(p.name)
            for child in v.values():visit(child)
        elif isinstance(v,list):
            for child in v:visit(child)
    visit(m)
    for key in ui.PANELS:
        if m['ui'][key].get('insets')!=[12]*4:raise ValueError('Missing panel insets')
    return {'uiPieces':13,'panels':10,'diceValues':[0,0,1,1,1,2],'lobbyBadges':6,'miniMarkers':6,'uniqueAssets':len(files)}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pack',type=Path,default=ui.ROOT/'content/meridian/base');a=p.parse_args();print(json.dumps(audit(a.pack),indent=2))
