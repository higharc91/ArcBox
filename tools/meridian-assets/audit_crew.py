"""Check complete crew/state/facing/portrait coverage against actual content, hashes and frames."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image
import crew


def audit(pack,reference):
    content={p['id']:p for p in crew.rules(reference)}
    members=json.loads((pack/'crew/manifest.json').read_text())['crew']
    portraits=json.loads((pack/'portraits/manifest.json').read_text())['portraits']
    if set(members)!=set(content) or set(portraits)!=set(content): raise ValueError('Crew or portrait IDs differ from content')
    unique=set();board_frames=0
    def check(entry,folder,size,frames=1):
        path=pack/folder/entry['file'];raw=path.read_bytes();digest=hashlib.sha256(raw).hexdigest()
        if digest!=entry['sha256'] or digest[:12] not in path.name: raise ValueError('asset hash differs: '+str(path))
        with Image.open(path) as image:
            if image.size!=(size[0]*frames,size[1]): raise ValueError('asset dimensions differ: '+str(path))
        unique.add(str(path))
    for key,person in content.items():
        member=members[key];art=portraits[key]
        if any(item['name']!=person['name'] or item['role']!=person['role'] for item in [member,art]): raise ValueError('Name/role drift: '+key)
        if member['frameSize']!=[24,24] or member['anchor']!=[12,22]: raise ValueError('Frame/anchor drift: '+key)
        for variant in ['facings','echo']:
            if set(member[variant])!=set(crew.FACINGS): raise ValueError('Missing facing: '+key)
            for facing,clips in member[variant].items():
                if set(clips)!=set(crew.CLIPS): raise ValueError('Missing pose: '+key)
                for kind,entry in clips.items():
                    frames,fps,loop=crew.CLIPS[kind]
                    if (entry['frames'],entry['fps'],entry['loop'])!=(frames,fps,loop): raise ValueError('Wrong clip contract: '+key)
                    if entry['frameSize']!=[24,24] or entry['reducedMotionFrame']!=0: raise ValueError('Wrong frame/reduced motion metadata')
                    check(entry,'crew',(24,24),frames);board_frames+=frames
        if member['walk']!=member['facings']['south']['walk']: raise ValueError('M4 walk alias drift: '+key)
        if (member['idle']['frames'],member['idle']['fps'],member['idle']['loop'])!=(1,0,False): raise ValueError('M4 idle must be one static frame: '+key)
        check(member['idle'],'crew',(24,24))
        with Image.open(pack/'crew'/member['idle']['file']) as idle, Image.open(pack/'crew'/member['facings']['south']['idle']['file']) as strip:
            if idle.convert('RGBA').tobytes()!=strip.crop((0,0,24,24)).convert('RGBA').tobytes(): raise ValueError('M4 idle pose differs from full idle: '+key)
        for expression in ['neutral','hurt','echo','crewCard']:
            size=(96,160 if expression=='crewCard' else 112)
            if art[expression]['frameSize']!=list(size): raise ValueError('Portrait frame metadata differs: '+key)
            check(art[expression],'portraits',size)
    return {'crewIds':len(content),'facings':4,'livingAndEchoBoardFrames':board_frames,'portraitsAndCards':len(content)*4,'uniqueAssets':len(unique)}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pack',type=Path,default=crew.ROOT/'content/meridian/base')
    live=crew.ROOT/'content/meridian/base/rules/crew.json'
    p.add_argument('--rules',type=Path,default=live if live.exists() else crew.REFERENCE)
    args=p.parse_args();print(json.dumps(audit(args.pack,args.rules),indent=2))
