"""Copy complete approved primary figures into technical reference sheets."""
from pathlib import Path
import json
from PIL import Image

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parents[3] / 'content/tactics/base'
sprites = json.loads((BASE / 'creatures/manifest.json').read_text())['sprites']
(ROOT / 'references').mkdir(exist_ok=True)
for role in json.loads((ROOT / 'roles.json').read_text()):
    sprite = sprites[role['primaryId']]
    image = Image.open(BASE / 'creatures' / sprite['file']).convert('RGBA')
    width, height = sprite['frame']['width'], sprite['frame']['height']
    columns, rows = sprite['columns'], sprite['rowsPerDirection']
    for action in ['idle', 'walk', 'attack', 'damage']:
        if action == 'damage':
            frames = [sprite['anims']['hurt']['frames'][0], sprite['anims']['down']['frames'][0]]
        elif action == 'walk' and len(sprite['anims']['walk']['frames']) >= 8:
            frames = [sprite['anims']['walk']['frames'][i] for i in [0, 2, 4, 6]]
        else:
            frames = list(dict.fromkeys(sprite['anims'][action]['frames']))[:4]
        figures = []
        for row in range(8):
            for frame in frames:
                x, y = frame % columns * width, (row * rows + frame // columns) * height
                figure = image.crop((x, y, x + width, y + height))
                figure = figure.crop(figure.getbbox())
                scale = min(190 / figure.width, 190 / figure.height)
                figure = figure.resize((round(figure.width * scale), round(figure.height * scale)), Image.Resampling.NEAREST)
                figures.append(figure)
        sheet = Image.new('RGBA', (1024, 256 * ((len(figures) + 3) // 4)))
        for i, figure in enumerate(figures):
            sheet.alpha_composite(figure, (i % 4 * 256 + (256 - figure.width) // 2, i // 4 * 256 + 224 - figure.height))
        sheet.save(ROOT / 'references' / (role['id'] + '-' + action + '.png'))
print('Copied complete primary figures; anatomy and source artwork are unchanged.')
