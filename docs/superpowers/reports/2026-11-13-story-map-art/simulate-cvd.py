"""Review-only full-severity Machado et al. (2009) linear-sRGB simulations."""
from pathlib import Path
import numpy as np
from PIL import Image
root = Path(__file__).resolve().parent
matrices = {
    'deuteranopia': [[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
    'protanopia': [[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]],
}
(root/'cvd').mkdir(exist_ok=True)
sources = list((root/'native').glob('*.png'))
sources = [p for p in sources if p.stem in ['forest_ruins','warlord_bridge','crypt_hall','crystal_cave','rime_tower']]
sources += list(root.glob('phone-390-*.png'))
for source in sources:
    name = source.stem
    rgb = np.asarray(Image.open(source).convert('RGB'), dtype=float)/255
    linear = np.where(rgb <= .04045, rgb/12.92, ((rgb+.055)/1.055)**2.4)
    for kind, matrix in matrices.items():
        transformed = np.clip(linear @ np.array(matrix).T, 0, 1)
        encoded = np.where(transformed <= .0031308, transformed*12.92, 1.055*transformed**(1/2.4)-.055)
        Image.fromarray(np.round(encoded*255).astype('uint8')).save(root/'cvd'/f'{name}-{kind}.png')

# Review-only contact sheet of the eight phone simulations, without resizing.
from PIL import ImageDraw
files = sorted((root/'cvd').glob('phone-390-*.png'))
canvas = Image.new('RGB', (390*4, 980*2), '#181824')
draw = ImageDraw.Draw(canvas)
for i, file in enumerate(files):
    x, y = (i%4)*390, (i//4)*980
    draw.text((x+5, y+5), file.stem, fill='white')
    canvas.paste(Image.open(file).convert('RGB'), (x, y+30))
canvas.save(root/'cvd/phone-contact-sheet.png')
