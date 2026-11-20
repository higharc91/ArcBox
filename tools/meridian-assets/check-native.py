"""Pixel-exact native capture check and actual CVD / compressed-stream review artifacts."""
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
REVIEW=ROOT/'docs/superpowers/reports/meridian-art/tiles'
MATRICES={
'deuteranopia':[[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
'protanopia':[[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]]}
results=[]
for deck in ['command','habitat','engineering']:
    original=np.asarray(Image.open(REVIEW/f'{deck}-mock.png').convert('RGB'))
    native=Image.open(REVIEW/f'native-{deck}.png').convert('RGB')
    if not np.array_equal(original,np.asarray(native)): raise ValueError('native capture differs: '+deck)
    native.save(REVIEW/f'stream-{deck}.jpg',quality=55,subsampling=2)
    rgb=np.asarray(native,dtype=float)/255
    linear=np.where(rgb<=.04045,rgb/12.92,((rgb+.055)/1.055)**2.4)
    for name,matrix in MATRICES.items():
        transformed=np.clip(linear@np.asarray(matrix).T,0,1)
        encoded=np.where(transformed<=.0031308,transformed*12.92,1.055*transformed**(1/2.4)-.055)
        Image.fromarray(np.round(encoded*255).astype('uint8')).save(REVIEW/f'{name}-{deck}.png')
    results.append({'deck':deck,'nativePixelsMatch':True,'size':[1920,1080],'actualCvd':list(MATRICES),'streamProxy':'JPEG quality 55, 4:2:0; not a Discord/human playtest'})
(REVIEW/'native-check.json').write_text(json.dumps(results,indent=2)+'\n')
