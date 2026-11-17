"""Compare actual Chromium nine-slice corners to exact 2x source pixels.

Locator screenshots ceil fractional text-driven card bounds, potentially adding
one background row/column. Match only at the edge or one pixel inside it; never
resample the captured pixels.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
REPORT=ROOT/'docs/superpowers/reports/meridian-art/cards'
manifest=json.loads((ROOT/'content/meridian/base/cards/manifest.json').read_text())
rows=[]
for width in [320,390,1920]:
    for kind,entry in manifest['frames'].items():
        source=Image.open(ROOT/'content/meridian/base/cards'/entry['file']).convert('RGBA')
        image=Image.open(REPORT/f'phone-{kind}-{width}.png').convert('RGB'); w,h=image.size
        matches={}
        for name,src,right,bottom in [('tl',(0,0,12,12),False,False),('tr',(52,0,64,12),True,False),('bl',(0,52,12,64),False,True),('br',(52,52,64,64),True,True)]:
            rgba=source.crop(src).resize((24,24),Image.Resampling.NEAREST)
            expected=Image.new('RGB',(24,24),'#111b2a'); expected.paste(rgba,mask=rgba.getchannel('A'))
            offsets=[]
            for dx in ([0,-1] if right else [0,1]):
                for dy in ([0,-1] if bottom else [0,1]):
                    x=(w-24 if right else 0)+dx; y=(h-24 if bottom else 0)+dy
                    if np.array_equal(np.asarray(expected),np.asarray(image.crop((x,y,x+24,y+24)))):offsets.append([dx,dy])
            if not offsets:raise ValueError((width,kind,name,'corner differs from exact source'))
            matches[name]=offsets[0]
        rows.append({'viewport':width,'kind':kind,'exactNearestScale':2,'cornerOffsets':matches})
(REPORT/'nine-slice-browser.json').write_text(json.dumps(rows,indent=2)+'\n')
print('36 actual Chromium corners match exact nearest 2x source')
