"""Original Meridian samples: indexed pixels, immutable filenames and reproducible review output."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/tactics-assets'))
from arcpix.canvas import Canvas, SlotTable
from arcpix.palette import ramp

BASES = {'hull': '#657482', 'floor': '#414e60', 'warning': '#cf9b48', 'red': '#b94e61',
         'coolant': '#4eaaa8', 'anomaly': '#91a660', 'command': '#68a5c8',
         'habitat': '#a98cb8', 'engineering': '#c39759', 'skin': '#b67d59', 'suit': '#697b74', 'linen': '#b8c3b6'}
PALETTE = {name: ramp(value, shift=.055, spread=.85) for name, value in BASES.items()}
PLAYERS = ['#ff8a3d', '#3ec7ff', '#c08cff', '#5fe08a', '#ffd447', '#ff7fb6']
SAMPLES = {'bridge': 'command', 'med_bay': 'habitat', 'reactor_core': 'engineering', 'airlock': 'habitat'}
STATES = ['open', 'closed', 'sealed', 'none']
INK, PAPER = '#111b2a', '#d8e1da'


def canvas(w, h):
    return Canvas(w, h, SlotTable())


def room(key):
    """Overhead bevels have identical shading on all four sides; no cast shadow is baked in."""
    c = canvas(64, 64)
    c.rect(0, 0, 63, 63, 'hull', 1)
    c.rect(2, 2, 61, 61, 'hull', 4)
    c.rect(4, 4, 59, 59, 'hull', 0)
    c.rect(7, 7, 56, 56, 'floor', 3)
    # Quiet large panels keep small equipment silhouettes legible at 2x.
    for y in [8, 32]:
        for x in [8, 32]:
            c.rect(x, y, x+22, y+22, 'floor', 2)
            c.line(x+2, y+2, x+20, y+2, 'floor', 3)
            c.px(x+2, y+20, 'hull', 2)
            c.px(x+20, y+20, 'hull', 2)
    # Matching corner couplers, illuminated from overhead rather than a compass direction.
    for x, y in [(5, 5), (53, 5), (5, 53), (53, 53)]:
        c.rect(x, y, x+5, y+5, 'hull', 1)
        c.rect(x+1, y+1, x+4, y+4, 'hull', 3)
        c.rect(x+2, y+2, x+3, y+3, 'hull', 4)
    deck = SAMPLES[key]
    for i in range(['command', 'habitat', 'engineering'].index(deck)+1):
        c.rect(11+i*4, 51, 12+i*4, 54, deck, 4)
    if key == 'bridge':
        # Wraparound station: inset glass, clustered telemetry and discrete controls.
        c.polygon([(11,12),(52,12),(52,29),(44,29),(44,23),(19,23),(19,29),(11,29)], 'hull', 0)
        c.rect(12,13,51,21,'hull',3)
        for x in [14,27,40]:
            c.rect(x,14,x+9,19,'command',0)
            c.line(x+1,15,x+7,15,'command',4)
            c.line(x+1,17,x+3,17,'command',3)
            c.line(x+5,18,x+8,18,'command',5)
            c.px(x+9,21,'warning',4)
        for x in [12,45]:
            c.rect(x,22,x+5,27,'hull',3)
            c.rect(x+1,23,x+4,24,'command',2)
            c.line(x+1,26,x+3,26,'linen',3)
        # Chair has a back, armrests, seat and a visible pedestal.
        c.ellipse(32,37,5,3,'hull',0)
        c.rect(31,32,32,39,'hull',3)
        c.rect(27,27,36,34,'hull',0)
        c.rect(28,28,35,32,'suit',3)
        c.rect(29,27,34,28,'suit',4)
        c.rect(26,30,27,34,'hull',4)
        c.rect(36,30,37,34,'hull',4)
        # Low navigation table with a deliberately sparse route diagram.
        c.rect(26,43,39,49,'hull',0)
        c.rect(27,44,38,48,'command',1)
        c.line(29,47,32,45,'command',4)
        c.line(32,45,36,46,'command',4)
        c.px(36,46,'linen',5)
    elif key == 'med_bay':
        for x in [12,42]:
            c.rect(x+1,15,x+9,45,'hull',0)
            c.rect(x,18,x+10,41,'hull',3)
            c.rect(x+2,16,x+8,42,'linen',3)
            c.rect(x+3,17,x+7,21,'linen',5)
            c.line(x+3,22,x+7,22,'linen',2)
            c.rect(x+2,26,x+8,37,'coolant',2)
            c.line(x+3,27,x+7,27,'coolant',4)
            c.line(x+3,36,x+7,36,'coolant',1)
            for y in [24,39]:
                c.line(x,y,x,y+2,'hull',5)
                c.line(x+10,y,x+10,y+2,'hull',5)
            # Bedside monitor, its support and a waveform rather than a bright slab.
            mx = x+11 if x == 12 else x-5
            c.rect(mx+1,21,mx+2,30,'hull',2)
            c.rect(mx,15,mx+4,21,'hull',0)
            c.rect(mx+1,16,mx+3,19,'coolant',1)
            c.line(mx+1,18,mx+2,17,'coolant',5)
        c.rect(28,10,35,19,'hull',0)
        c.rect(29,11,34,18,'linen',3)
        c.line(31,12,31,16,'red',3)
        c.line(29,14,33,14,'red',3)
        c.rect(29,42,35,48,'hull',1)
        c.rect(30,43,34,44,'linen',3)
        c.line(30,46,34,46,'hull',4)
    elif key == 'reactor_core':
        # Pipes terminate in paired valve housings around a segmented containment ring.
        for x in [13,48]:
            c.rect(x,13,x+2,48,'hull',0)
            c.line(x+1,14,x+1,47,'coolant',3)
            for y in [15,44]:
                c.rect(x-2,y,x+4,y+4,'hull',2)
                c.line(x-1,y+2,x+3,y+2,'warning',4)
        for radius,slot,shade in [(18,'hull',0),(16,'hull',4),(14,'hull',1),
                                  (12,'coolant',3),(10,'coolant',1),(8,'hull',0),
                                  (6,'anomaly',2),(3,'anomaly',5)]:
            c.ellipse(32,31,radius,radius,slot,shade)
        for x,y,w,h in [(29,12,6,7),(29,44,6,7),(13,28,7,6),(45,28,7,6)]:
            c.rect(x,y,x+w-1,y+h-1,'hull',0)
            c.rect(x+1,y+1,x+w-2,y+h-2,'hull',3)
            c.rect(x+2,y+2,x+w-3,y+h-3,'warning',4)
        c.line(30,29,33,29,'anomaly',4)
        c.line(34,30,34,33,'anomaly',4)
    else:
        # Rectangular pressure vestibule distinguishes the airlock from the reactor.
        c.polygon([(19,12),(44,12),(50,18),(50,44),(44,50),(19,50),(13,44),(13,18)],'hull',0)
        c.rect(17,17,46,45,'hull',3)
        c.rect(20,19,43,43,'hull',1)
        c.rect(22,20,41,42,'hull',4)
        c.line(31,20,31,42,'hull',0)
        c.line(32,20,32,42,'hull',2)
        for x in [24,35]:
            c.rect(x,24,x+4,31,'hull',0)
            c.rect(x+1,25,x+3,30,'coolant',2)
            c.line(x+1,25,x+3,25,'coolant',4)
            c.rect(x+1,35,x+3,37,'hull',1)
            c.px(x+2,36,'linen',4)
        for y in [13,47]:
            for x in [22,28,34,40]:
                c.line(x,y,x+2,y+2,'warning',4)
        for x in [14,46]:
            for y in [22,37]:
                c.rect(x,y,x+2,y+4,'hull',0)
                c.line(x+1,y+1,x+1,y+3,'hull',5)
    # Exactly sixteen open edge pixels, including every outside wall layer.
    for i in range(24, 40):
        for j in range(7):
            for x, y in [(i, j), (i, 63-j), (j, i), (63-j, i)]:
                c.clear(x, y)
    return c.render(PALETTE)


def hatch(state):
    c = canvas(64, 64)
    if state == 'none':
        c.rect(24, 0, 39, 6, 'hull', 1)
        c.rect(24, 2, 39, 3, 'hull', 4)
        c.line(24, 5, 39, 5, 'hull', 2)
    else:
        for x in [23, 38]:
            c.rect(x, 0, x+2, 7, 'hull', 0)
            c.rect(x+1, 1, x+1, 6, 'coolant' if state == 'open' else 'warning', 4)
        if state != 'open':
            c.rect(26, 0, 37, 6, 'hull', 2)
            if state == 'closed':
                c.rect(26, 1, 30, 5, 'hull', 4)
                c.rect(33, 1, 37, 5, 'hull', 4)
                c.rect(31, 0, 32, 6, 'hull', 0)
                c.line(27, 2, 29, 2, 'hull', 5)
                c.line(34, 2, 36, 2, 'hull', 5)
                c.px(29, 4, 'hull', 1)
                c.px(34, 4, 'hull', 1)
            else:
                c.rect(26, 1, 37, 5, 'red', 1)
                c.thick_line(26, 0, 37, 6, 2, 'warning', 4)
                c.thick_line(37, 0, 26, 6, 2, 'warning', 4)
    return c.render(PALETTE)


def crew(frame=0):
    c = canvas(24, 24)
    left, right = [(0, 0), (-1, 1), (0, 0), (1, -1), (0, 0)][frame]
    # Articulated boots, tapered torso, separate gloves and a compact helmet.
    for x, offset in [(8,left),(13,right)]:
        c.rect(x,15+offset,x+2,19+offset,'suit',1)
        c.line(x,16+offset,x+1,17+offset,'suit',3)
        c.rect(x-1,19+offset,x+2,20+offset,'hull',0)
        c.line(x-1,19+offset,x+1,19+offset,'hull',3)
    c.rect(7,8,16,13,'suit',1)
    c.rect(8,9,15,15,'suit',3)
    c.rect(9,10,14,13,'suit',4)
    c.rect(8,15,15,16,'hull',0)
    c.rect(10,15,11,16,'warning',4)
    for x, offset in [(5,-left),(17,-right)]:
        c.rect(x,10,x+1,14+offset,'suit',2)
        c.px(x,10,'suit',4)
        c.rect(x,15+offset,x+1,16+offset,'hull',2)
    c.rect(8,2,15,7,'hull',1)
    c.rect(9,1,14,2,'hull',4)
    c.rect(8,3,8,5,'hull',3)
    c.rect(10,3,14,6,'coolant',1)
    c.line(10,3,13,3,'coolant',4)
    c.rect(10,7,13,8,'skin',3)
    c.px(15,6,'warning',4)
    c.rect(10,10,13,11,'linen',4)
    c.rect(14,12,15,13,'warning',3)
    c.outline()
    return c.render(PALETTE)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2)+'\n')


def immutable(pack, folder, name, image):
    data = io.BytesIO()
    image.save(data, format='PNG', optimize=True)
    digest = hashlib.sha256(data.getvalue()).hexdigest()
    file = f'{name}.{digest[:12]}.png'
    path = pack/folder/file
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() != data.getvalue():
        raise ValueError('Refusing to replace immutable asset '+str(path))
    path.write_bytes(data.getvalue())
    return {'file': file, 'sha256': digest}


def build(root=ROOT):
    pack = root/'content/meridian/base'
    review = root/'docs/superpowers/reports/meridian-art/style'
    review.mkdir(parents=True, exist_ok=True)
    tiles, hatches = {}, {}
    for key, deck in SAMPLES.items():
        tiles[key] = {**immutable(pack, 'tiles', key, room(key)), 'deck': deck,
                      'frameSize': [64, 64], 'doorway': [24, 39], 'rotationDegrees': [0, 90, 180, 270]}
    for state in STATES:
        hatches[state] = {**immutable(pack, 'hatches', state, hatch(state)), 'frameSize': [64, 64],
                          'edge': 'north', 'edgeBounds': [23, 0, 41, 8]}
    strip = Image.new('RGBA', (96, 24))
    for i in range(4):
        strip.alpha_composite(crew(i+1), (i*24, 0))
    person = {'frameSize': [24, 24], 'anchor': [12, 22],
              'idle': {**immutable(pack, 'crew', 'ines-okafor-idle', crew()), 'frames': 1, 'fps': 0},
              'walk': {**immutable(pack, 'crew', 'ines-okafor-walk', strip), 'frames': 4, 'fps': 6}}
    common = {'version': 1, 'status': 'sample-awaiting-owner-review', 'issue': 331,
              'rulesReference': 'PR #316 at af068bb1d23b5e295457adc4bf2ce1108ad6686d; not yet on main'}
    write_json(pack/'tiles/manifest.json', {**common, 'tiles': tiles})
    write_json(pack/'hatches/manifest.json', {**common, 'states': hatches})
    write_json(pack/'crew/manifest.json', {**common, 'crew': {'ines_okafor': person}})
    write_json(review/'palette.json', {'ramps': PALETTE, 'players': PLAYERS})
    # Source geometry is 960×540, enlarged only by an integer nearest-neighbour factor.
    mock = Image.new('RGBA', (960, 540), INK)
    draw = ImageDraw.Draw(mock)
    font = ImageFont.load_default()
    draw.fontmode = "1"
    def text(x, y, words, colour=PAPER):
        draw.text((x, y), words, fill=colour, font=font, stroke_width=0)
    text(28, 20, 'DERELICT: ISV MERIDIAN')
    text(28, 42, 'HABITAT  /  II     EXPLORE     ANOMALY  3 / 9')
    text(28, 66, 'STYLE SAMPLE - public information only')
    locations = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (3, 1), (1, 2)]
    for i, (gx, gy) in enumerate(locations):
        x, y = 120+gx*64, 140+gy*64
        key = 'airlock' if i == 7 else 'med_bay'
        mock.alpha_composite(room(key), (x, y))
        for turns in range(4):
            mock.alpha_composite(hatch('closed' if (i+turns)%4 == 0 else 'open').rotate(90*turns), (x, y))
    for number, pos in [(1, (212, 220)), (2, (340, 220))]:
        draw.ellipse((pos[0]-8, pos[1]+4, pos[0]+12, pos[1]+20), outline=PLAYERS[number-1], width=2)
        mock.alpha_composite(crew(), (pos[0]-9, pos[1]-4))
        text(pos[0]-1, pos[1]+23, str(number))
    text(120, 355, '1  INES OKAFOR     2  CREW SAMPLE')
    text(120, 380, 'Cyan rail = open   |   Split leaves = closed')
    text(120, 400, 'Welded X = sealed  |   Solid plate = wall')
    for i, key in enumerate(['bridge', 'reactor_core']):
        x, y = 685, 145+i*150
        mock.alpha_composite(room(key), (x, y))
        text(x+80, y+12, SAMPLES[key].upper())
        text(x+80, y+30, 'INACTIVE DECK')
    text(28, 500, 'PROPOSAL FOR #331  /  NOT A GAMEPLAY SCREEN OR FINAL UI KIT')
    mock.resize((1920, 1080), Image.Resampling.NEAREST).convert('RGB').save(review/'deck-mock-1080.png')
    base = '../../../../../content/meridian/base/'
    cards = []
    for key, tile in tiles.items():
        for state in STATES:
            # Separate figures make 1× and 2× comparisons explicit; never fit images fractionally.
            views = ''.join(f'<div class="tile s{scale}" aria-label="{scale}x">'+''.join(
                f'<img alt="" src="{base}{folder}/{entry["file"]}" width="{64*scale}" height="{64*scale}">'
                for folder, entry in [('tiles', tile), ('hatches', hatches[state])])+'</div>' for scale in [1, 2])
            cards.append(f'<figure><figcaption>{key.replace("_", " ")} · {state}</figcaption>{views}</figure>')
    html = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Meridian · style samples</title><style>
:root{color-scheme:dark;font:17px/1.5 system-ui;background:#111b2a;color:#d8e1da}*{box-sizing:border-box}
body{margin:0;padding:20px;max-width:1240px;margin:auto}h1{line-height:1.1}a{color:#8fd4df}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:16px}figure{margin:0;border:1px solid #657482;padding:16px;background:#1a2636}
figcaption{margin-bottom:12px;text-transform:capitalize}.tile{position:relative;display:inline-block;vertical-align:bottom;margin-right:12px}
.tile img{position:absolute;inset:0}.s1{width:64px;height:64px}.s2{width:128px;height:128px}img,.walker{image-rendering:pixelated}
.pan{overflow:auto;border:1px solid #657482;max-height:75vh}.pan img{display:block;max-width:none}.crew{display:flex;gap:24px;align-items:end;flex-wrap:wrap}
.walker{width:48px;height:48px;background-size:192px 48px;animation:walk .667s steps(4) infinite}button{font:inherit;padding:12px;min-height:44px;background:#26384b;color:#d8e1da;border:1px solid #657482}
@keyframes walk{to{background-position:-192px 0}}@media(prefers-reduced-motion:reduce){.walker{animation:none}}body.paused .walker{animation-play-state:paused}
</style><body><h1>DERELICT: ISV MERIDIAN</h1><p>Industrial salvage. Cold metal, lived-in spaces, overhead light.</p>
<p><strong>Style proposal · #331 · awaiting owner approval.</strong> Original deterministic samples, 0 generation credits. Room keys match the unmerged M1 rules in PR #316. These are four samples, not the full 50-tile set.</p>
<p>Each card shows a 64px source at 1×, then 2× TV scale. Hatch overlays are independent and rotate in 90° steps. Decks carry I / II / III marks as well as distinct accents.</p><p>Refinement: quieter floor panels, fitted consoles, medical linens and monitors, containment braces and pressure-door hardware. <a href="detail-comparison.png">Before / after detail sheet (4×)</a>.</p><h2>Compartment and hatch samples</h2><div class="grid">'''+''.join(cards)+'''</div><h2>Crew source frames · 1×</h2><div class="crew">'''+f'<img alt="Compartment at 1x" src="{base}tiles/{tiles["med_bay"]["file"]}" width="64" height="64"><img alt="Ines standing at 1x" src="{base}crew/{person["idle"]["file"]}" width="24" height="24"><img alt="Four walking frames at 1x" src="{base}crew/{person["walk"]["file"]}" width="96" height="24">'+'''</div><h2>Standing and walking at TV scale</h2><div class="crew">'''+f'<img alt="Habitat compartment at 2x" src="{base}tiles/{tiles["med_bay"]["file"]}" width="128" height="128"><img alt="Ines standing at 2x" src="{base}crew/{person["idle"]["file"]}" width="48" height="48"><div role="img" aria-label="Ines walking at 2x" class="walker" style="background-image:url({base}crew/{person["walk"]["file"]})"></div>'+'''<button id="motion">Pause walk</button></div><p>24×24 frames, feet anchor (12,22), 6fps. Reduced-motion preference shows a still frame.</p><h2>Partly explored deck · 1920×1080</h2><p>1:1 native pixels. Scroll inside this panel on smaller screens. This is a concept mock, not the implemented game or a physical-TV test.</p><div class="pan" tabindex="0" aria-label="Full resolution TV mock; scroll to inspect"><img src="deck-mock-1080.png" width="1920" height="1080" alt="Habitat exploration mock with two crew, separate Command and Engineering samples, hatch legend and deck labels"></div>
<p><a href="https://github.com/higharc91/ArcBox/issues/331">Review issue #331</a> · <a href="../../../../../tools/meridian-assets/STYLE.md">Style and file contract</a> · <a href="palette.json">Exact palette ramps</a></p><script>const b=document.querySelector('#motion');b.onclick=()=>{document.body.classList.toggle('paused');b.textContent=document.body.classList.contains('paused')?'Play walk':'Pause walk'};</script></body></html>'''
    (review/'index.html').write_text(html)
    return pack, review


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=ROOT)
    args = parser.parse_args()
    build(args.root)
