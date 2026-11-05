"""Build locally authored lore-place candidates into a disposable pack, never the live pack."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
sys.path.insert(0, str(REPO / 'tools/tactics-assets'))
from arcpix.out import PACK
from parts import lore_places


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    options = parser.parse_args()
    stage = options.out.resolve()
    pack = stage / 'content/tactics/base'
    if pack.resolve() == PACK.resolve() or stage == REPO:
        raise ValueError('Use a separate disposable preview directory.')
    protected = [PACK / 'town/manifest.json', PACK / 'props/manifest.json', PACK / 'rules/world/ashen_ford.json']
    before = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    if not pack.exists():
        shutil.copytree(PACK, pack)
    outputs = lore_places.export(pack)
    world_path = pack / 'rules/world/ashen_ford.json'
    world = json.loads((PACK / 'rules/world/ashen_ford.json').read_text())
    for building in world['buildings']:
        if building['id'] == 'cottage' and building['at'] == [40, 72]:
            building.update(id='granary', variant='ashen_ford', at=[39, 71])
        elif building['id'] == 'cottage' and building['at'] == [130, 88]:
            building.update(id='maud_hut', variant='ashen_ford')
    world['buildings'] = [b for b in world['buildings']
                          if not ((b['id'] == 'clutter_barrels' and b['at'] == [45, 73]) or
                                  (b['id'] == 'clutter_crates' and b['at'] == [42, 73]))]
    for group in world['props']:
        if group['id'] == 'skull_pike':
            group['at'] = [p for p in group['at'] if not (126 <= p[0] <= 135 and 86 <= p[1] <= 95)]
    world['props'].extend([
        {'id': 'sack', 'variant': 'burlap', 'at': [[38, 72], [42, 72], [42, 73]]},
        {'id': 'cart', 'variant': 'oak', 'at': [[38, 73]]},
        {'id': 'maud_ash_garden', 'variant': 'ashen_ford', 'at': [[132, 88]]},
        {'id': 'maud_stool_pots', 'variant': 'ashen_ford', 'at': [[129, 90]]},
    ])
    world_path.write_text(json.dumps(world, indent=2) + '\n')
    # The dev TV resolves its repository pack relative to res://. Clone its already-built host beside this pack.
    host = stage / 'src/ArcBox.Host'
    if not host.exists():
        shutil.copytree(REPO / 'src/ArcBox.Host', host)
    review = stage / 'review'
    review.mkdir(exist_ok=True)
    try:
        font = ImageFont.truetype('DejaVuSans.ttf', 20)
    except OSError:
        font = ImageFont.load_default(size=20)
    sheet = Image.new('RGB', (1500, 1060), '#292736')
    draw = ImageDraw.Draw(sheet)
    draw.text((30, 16), 'Ashen Ford · locally authored pixel art · review candidates', font=font, fill='#ead5a2')
    for i, (name, entry) in enumerate(outputs.items()):
        im = Image.open(entry['image']).convert('RGBA')
        bounds = im.getbbox()
        native = im.crop(bounds)
        zoom = 3 if i < 2 else 4
        native = native.resize((native.width * zoom, native.height * zoom), Image.Resampling.NEAREST)
        x, y = (35 + i * 740, 64) if i < 2 else (100 + (i - 2) * 700, 730)
        sheet.paste(native, (x, y), native)
        draw.text((x, y + native.height + 10), name.replace('_', ' '), font=font, fill='#eee4d5')
        entry['image'] = str(Path(entry['image']).relative_to(pack))
        entry['sha256'] = hashlib.sha256((pack / entry['image']).read_bytes()).hexdigest()
    sheet.save(review / 'local-pixel-art.png', optimize=True)
    (review / 'assets.json').write_text(json.dumps(outputs, indent=2) + '\n')
    validation = {}
    for name, make in (lore_places.BUILDERS | lore_places.PROP_BUILDERS).items():
        a, b = lore_places.T.render(make()), lore_places.T.render(make())
        assert a.image.tobytes() == b.image.tobytes() and a.shadow.tobytes() == b.shadow.tobytes()
        rgba = np.asarray(a.image)
        assert not any(edge.any() for edge in (rgba[0, :, 3], rgba[-1, :, 3], rgba[:, 0, 3], rgba[:, -1, 3]))
        assert a.image.size == a.shadow.size
        validation[name] = {'size': list(a.image.size), 'origin': list(a.origin), 'anchor': list(a.anchor),
                            'deterministic_image_and_shadow': True, 'transparent_frame_border': True}
    old_hut = lore_places.T.render_building('cottage', 'thatch_green')
    new_hut = lore_places.T.render(lore_places.maud_hut())
    assert old_hut.image.size == new_hut.image.size and old_hut.origin == new_hut.origin and old_hut.anchor == new_hut.anchor
    validation['maud_preserves_cottage_frame_and_anchor'] = True
    (review / 'art-validation.json').write_text(json.dumps(validation, indent=2) + '\n')
    for path, digest in before.items():
        assert hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest, 'Live pack changed!'
    print('Authored', len(outputs), 'native assets into', pack)
    print('Live content untouched; TV preview pack ready.')


if __name__ == '__main__':
    main()
