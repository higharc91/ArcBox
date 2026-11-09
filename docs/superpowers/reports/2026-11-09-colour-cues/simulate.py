"""Machado et al. (2009), severity 1.0, applied in linear RGB to actual browser/Godot captures."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image
matrices={
 'protanopia':[[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]],
 'deuteranopia':[[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
}
root=Path(sys.argv[1])
for path in root.glob('*.png'):
 if any(k in path.stem for k in matrices):continue
 if '320' in path.stem:continue
 rgb=np.asarray(Image.open(path).convert('RGB'),dtype=float)/255
 linear=np.where(rgb<=.04045,rgb/12.92,((rgb+.055)/1.055)**2.4)
 for name,m in matrices.items():
  transformed=np.clip(linear@np.array(m).T,0,1)
  encoded=np.where(transformed<=.0031308,12.92*transformed,1.055*transformed**(1/2.4)-.055)
  Image.fromarray(np.uint8(np.clip(encoded*255,0,255))).save(path.with_stem(path.stem+'-'+name))
  print(path.stem,name)
