"""Native art-review pixel identity, exact browser sprite scaling and actual CVD transforms."""
import json
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
REPORT=ROOT/'docs/superpowers/reports/meridian-art/tokens'
MATRICES={
'deuteranopia':[[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
'protanopia':[[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]]}
rows=[]
for scene in ['monsters','tokens','states']:
    source=Image.open(REPORT/f'{scene}-tv.png').convert('RGB');native=Image.open(REPORT/f'native-{scene}.png').convert('RGB')
    if source.size!=(1920,1080) or not np.array_equal(np.asarray(source),np.asarray(native)):raise ValueError('Native capture differs: '+scene)
    native.save(REPORT/f'stream-{scene}.jpg',quality=55,subsampling=2)
    rgb=np.asarray(native,dtype=float)/255;linear=np.where(rgb<=.04045,rgb/12.92,((rgb+.055)/1.055)**2.4)
    for name,matrix in MATRICES.items():
        transformed=np.clip(linear@np.asarray(matrix).T,0,1);encoded=np.where(transformed<=.0031308,transformed*12.92,1.055*transformed**(1/2.4)-.055)
        Image.fromarray(np.round(encoded*255).astype('uint8')).save(REPORT/f'{name}-{scene}.png')
    rows.append({'scene':scene,'nativePixelsMatch':True,'size':[1920,1080],'actualCvd':list(MATRICES),'streamProxy':'JPEG 55 / 4:2:0, not a physical-TV or Discord test'})
for e in json.loads((REPORT/'browser.json').read_text())['pixelSamples']:
    source=Image.open(ROOT/'content/meridian/base/tokens'/e['source']).convert('RGBA').crop((0,0,24,24)).resize((48,48),Image.Resampling.NEAREST)
    expected=Image.new('RGB',(48,48),'#21243c');expected.paste(source,mask=source.getchannel('A'))
    actual=Image.open(REPORT/e['capture']).convert('RGB')
    if not np.array_equal(np.asarray(expected),np.asarray(actual)):raise ValueError('Browser scaling differs: '+str(e))
(REPORT/'capture-check.json').write_text(json.dumps({'nativeReviews':rows,'exactNearestBrowserCases':12,'newNativeGameSession':False,'physicalTvOrHumanRecognitionTest':False},indent=2)+'\n')
print('Three native 1080p art reviews match; twelve browser sprites match exact 2x; actual CVD/stream proxies saved.')
