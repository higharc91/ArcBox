"""Verify every rendered UI panel corner against unresampled source pixels."""
import json,math
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
REPORT=ROOT/'docs/superpowers/reports/meridian-art/ui'
manifest=json.loads((ROOT/'content/meridian/base/ui/manifest.json').read_text())
rows=[]
for phase,panels in json.loads((REPORT/'geometry.json').read_text()).items():
    screen=Image.open(REPORT/f'{phase}-tv.png').convert('RGB')
    for panel in panels:
        source=Image.open(ROOT/'content/meridian/base/ui'/manifest['ui'][panel['piece']]['file']).convert('RGBA')
        matches={}
        for name,box,right,bottom in [('tl',(0,0,12,12),False,False),('tr',(52,0,64,12),True,False),('bl',(0,52,12,64),False,True),('br',(52,52,64,64),True,True)]:
            expected=source.crop(box).resize((24,24),Image.Resampling.NEAREST).convert('RGB')
            x=panel['x']+(panel['width']-24 if right else 0)
            y=panel['y']+(panel['height']-24 if bottom else 0)
            found=[]
            for px in set([math.floor(x),math.ceil(x)]):
                for py in set([math.floor(y),math.ceil(y)]):
                    if np.array_equal(np.asarray(expected),np.asarray(screen.crop((px,py,px+24,py+24)))):found.append([px,py])
            if not found:raise ValueError((phase,panel,name,'corner differs'))
            matches[name]=found[0]
        rows.append({'phase':phase,'piece':panel['piece'],'exactNearestScale':2,'corners':matches})
(REPORT/'nine-slice-browser.json').write_text(json.dumps(rows,indent=2)+'\n')
print(f'{len(rows)*4} actual Chromium corners match exact nearest 2x source')
