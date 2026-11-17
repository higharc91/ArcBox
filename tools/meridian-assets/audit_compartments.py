"""Fail on content/manifest drift, missing deck variants or damaged immutable assets."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image
import compartments


def audit(pack, reference):
    expected={r['id']:r for r in compartments.rules(reference)}
    manifest=json.loads((pack/'tiles/manifest.json').read_text())
    actual=manifest['tiles']
    if set(actual)!=set(expected):
        raise ValueError(f'tile IDs differ: missing={sorted(set(expected)-set(actual))}, extra={sorted(set(actual)-set(expected))}')
    for key,rule in expected.items():
        entry=actual[key]
        if set(entry['variants'])!=set(rule['decks']): raise ValueError('deck variants differ: '+key)
        if entry['name']!=rule['name'] or entry['hatches']!=rule['hatches'] or entry['symbol']!=rule.get('symbol') or entry['rule']!=rule.get('rule'):
            raise ValueError('rules metadata differs: '+key)
    count=0
    def visit(value,folder):
        nonlocal count
        if isinstance(value,dict):
            if 'file' in value:
                path=folder/value['file']
                data=path.read_bytes(); digest=hashlib.sha256(data).hexdigest()
                if digest!=value['sha256'] or digest[:12] not in path.name: raise ValueError('asset hash differs: '+str(path))
                with Image.open(path) as image:
                    expected_size=(64*value.get('frames',1),64)
                    if image.size!=expected_size: raise ValueError('frame bounds differ: '+str(path))
                count+=1
            for child in value.values(): visit(child,folder)
        elif isinstance(value,list):
            for child in value: visit(child,folder)
    visit(manifest,pack/'tiles')
    hatches=json.loads((pack/'hatches/manifest.json').read_text())
    if set(hatches['states'])!={'open','closed','sealed','none','one_way'}: raise ValueError('missing hatch state')
    visit(hatches,pack/'hatches')
    return {'tileIds':len(expected),'physicalTiles':sum(r.get('copies',1) for r in expected.values()),
            'variants':sum(len(v['variants']) for v in actual.values()),'referencedAssets':count}


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--pack',type=Path,default=compartments.ROOT/'content/meridian/base')
    live=compartments.ROOT/'content/meridian/base/rules/tiles.json'
    parser.add_argument('--rules',type=Path,default=live if live.exists() else compartments.REFERENCE)
    args=parser.parse_args();print(json.dumps(audit(args.pack,args.rules),indent=2))
