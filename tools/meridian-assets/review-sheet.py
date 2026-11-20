"""Build a source-to-source detail comparison and exact 2x contact sheet."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[2]
PACK=ROOT/'content/meridian/base'
OUT=ROOT/'docs/superpowers/reports/meridian-art/style'
OLD={'bridge':'7ddba1745235','med_bay':'4d6d5a795887','reactor_core':'797f3e9c5738','airlock':'a4e1eecfe9bd'}
tiles=json.loads((PACK/'tiles/manifest.json').read_text())['tiles']
hatches=json.loads((PACK/'hatches/manifest.json').read_text())['states']
crew=json.loads((PACK/'crew/manifest.json').read_text())['crew']['ines_okafor']
def draw_text(im,xy,text):
    d=ImageDraw.Draw(im); d.fontmode='1'; d.text(xy,text,font=ImageFont.load_default(),fill='#d8e1da')
sheet=Image.new('RGBA',(400,480),'#111b2a')
draw_text(sheet,(10,4),'MERIDIAN / REFINED SAMPLES / TV 2x')
compare=Image.new('RGBA',(640,440),'#111b2a')
draw_text(compare,(20,12),'DETAIL REFINEMENT / SOURCE PIXELS AT 4x')
draw_text(compare,(20,34),'Each room: previous sample / revised sample')
for col,(key,tile) in enumerate(tiles.items()):
    x=50+col*85
    draw_text(sheet,(x-24,24),key.replace('_',' '))
    image=Image.open(PACK/'tiles'/tile['file'])
    for row,(state,hatch) in enumerate(hatches.items()):
        y=42+row*88
        if col==0: draw_text(sheet,(4,y+26),state)
        sheet.alpha_composite(image,(x,y))
        sheet.alpha_composite(Image.open(PACK/'hatches'/hatch['file']),(x,y))
    # Pair rooms in two rows, four columns: before/after for each room.
    x0=20+(col%2)*316; y0=64+(col//2)*184
    draw_text(compare,(x0,y0),key.replace('_',' '))
    old=Image.open(PACK/'tiles'/f'{key}.{OLD[key]}.png')
    # Each pair at integer 2x; final comparison is integer 2x again.
    compare.alpha_composite(old.resize((128,128),Image.Resampling.NEAREST),(x0,y0+22))
    compare.alpha_composite(image.resize((128,128),Image.Resampling.NEAREST),(x0+148,y0+22))
    draw_text(compare,(x0,y0+154),'BEFORE              REVISED')
draw_text(sheet,(10,405),'INES / STANDING + FOUR WALK FRAMES')
sheet.alpha_composite(Image.open(PACK/'crew'/crew['idle']['file']),(50,430))
sheet.alpha_composite(Image.open(PACK/'crew'/crew['walk']['file']),(100,430))
sheet.resize((800,960),Image.Resampling.NEAREST).convert('RGB').save(OUT/'sample-sheet-tv.png')
compare.resize((1280,880),Image.Resampling.NEAREST).convert('RGB').save(OUT/'detail-comparison.png')
