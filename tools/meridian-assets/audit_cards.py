"""Strict live-data coverage and immutable nine-slice/icon checks for Meridian cards."""
import argparse,hashlib,json
from pathlib import Path
from PIL import Image
import cards


def audit(pack,reference):
    data=cards.catalogue(reference);ids=[c['id'] for records in data.values() for c in records]
    if len(ids)!=len(set(ids)):raise ValueError('Duplicate content IDs')
    expected={c['id']:kind for kind,records in data.items() for c in records}
    item_ids={key:kind for key,kind in expected.items() if kind!='incident'}
    frames=json.loads((pack/'cards/manifest.json').read_text());art=json.loads((pack/'icons/manifest.json').read_text())
    if set(frames['cards'])!=set(expected) or set(art['icons'])!=set(item_ids)|set(cards.KINDS):raise ValueError('Card or item icon ID coverage differs')
    if set(frames['frames'])!=set(cards.KINDS) or set(art['symbols'])!=set(cards.KINDS):raise ValueError('Missing card kind')
    count=0
    def check(entry,folder,size):
        nonlocal count
        path=pack/folder/entry['file'];digest=hashlib.sha256(path.read_bytes()).hexdigest()
        if digest!=entry['sha256'] or digest[:12] not in path.name:raise ValueError('Hash mismatch: '+str(path))
        if entry['frameSize']!=list(size):raise ValueError('Metadata dimensions differ')
        with Image.open(path) as image:
            if image.size!=size:raise ValueError('Image dimensions differ')
        count+=1
    for key,kind in expected.items():
        if frames['cards'][key]!={'kind':kind,'frameKind':kind,'icon':key if kind!='incident' else None}:raise ValueError('Card kind/frame mapping differs: '+key)
        if kind!='incident':
            if art['icons'][key]['kind']!=kind:raise ValueError('Item kind differs')
            check(art['icons'][key],'icons',(24,24))
    for kind in cards.KINDS:
        plate=frames['frames'][kind]
        if plate['insets']!=[12,12,12,12] or plate['contentInset']!=[16,16,16,16]:raise ValueError('Nine-slice metadata differs')
        check(plate,'cards',(64,64))
        if art['icons'][kind]!=art['symbols'][kind]:raise ValueError('Canonical kind symbols differ')
        if set(art['symbols'][kind])!={'16','24'}:raise ValueError('Missing symbol size')
        for size in [16,24]:check(art['symbols'][kind][str(size)],'icons',(size,size))
    return {'cards':len(expected),'salvageIcons':sum(k=='salvage' for k in item_ids.values()),'anomalyIcons':sum(k=='anomaly' for k in item_ids.values()),'frames':3,'symbols':6,'referencedAssets':count}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pack',type=Path,default=cards.ROOT/'content/meridian/base')
    live=cards.ROOT/'content/meridian/base/rules/cards';p.add_argument('--rules',type=Path,default=live if live.exists() else cards.REFERENCE)
    a=p.parse_args();print(json.dumps(audit(a.pack,a.rules),indent=2))
