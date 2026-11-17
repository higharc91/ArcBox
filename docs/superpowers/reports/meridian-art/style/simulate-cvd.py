"""Review-only Machado 2009 full-severity colour-vision simulations, in linear sRGB."""
from pathlib import Path
import numpy as np
from PIL import Image
root = Path(__file__).resolve().parent
matrices = {
 'deuteranopia': [[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
 'protanopia': [[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]],
}
rgb = np.asarray(Image.open(root/'native-mock.png').convert('RGB'), dtype=float)/255
linear = np.where(rgb <= .04045, rgb/12.92, ((rgb+.055)/1.055)**2.4)
for name, matrix in matrices.items():
 transformed = np.clip(linear @ np.array(matrix).T, 0, 1)
 encoded = np.where(transformed <= .0031308, transformed*12.92, 1.055*transformed**(1/2.4)-.055)
 Image.fromarray(np.round(encoded*255).astype('uint8')).save(root/f'native-{name}.png')
