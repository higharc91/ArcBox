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
         'habitat': '#a98cb8', 'engineering': '#c39759', 'skin': '#b67d59', 'suit': '#697b74'}
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
    for y in range(8, 56, 12):
        for x in range(8, 56, 12):
            c.rect(x, y, x+10, y+10, 'floor', 2)
            c.rect(x+2, y+2, x+8, y+8, 'floor', 3)
    deck = SAMPLES[key]
    for i in range(['command', 'habitat', 'engineering'].index(deck)+1):
        c.rect(9+i*4, 49, 10+i*4, 54, deck, 4)
    if key == 'bridge':
        c.rect(12, 11, 51, 23, 'hull', 0)
        c.rect(14, 13, 49, 21, 'hull', 3)
        for x in [16, 28, 40]:
            c.rect(x, 14, x+7, 19, 'command', 1)
            c.rect(x+1, 15, x+6, 17, 'command', 5)
        for x in [20, 40]:
            c.rect(x, 29, x+5, 35, 'hull', 0)
            c.rect(x+1, 30, x+4, 33, 'suit', 3)
        c.rect(28, 40, 35, 43, 'command', 4)
    elif key == 'med_bay':
        for x in [12, 42]:
            c.rect(x, 13, x+10, 44, 'hull', 0)
            c.rect(x+1, 15, x+9, 42, 'hull', 4)
            c.rect(x+2, 18, x+8, 23, 'coolant', 3)
            c.rect(x+2, 28, x+8, 38, 'suit', 2)
        c.rect(28, 12, 35, 21, 'hull', 1)
        c.rect(31, 13, 32, 20, 'red', 4)
        c.rect(28, 16, 35, 17, 'red', 4)
    elif key == 'reactor_core':
        for radius, slot, shade in [(19, 'hull', 0), (17, 'hull', 4), (14, 'coolant', 1),
                                    (11, 'coolant', 4), (8, 'hull', 0), (5, 'anomaly', 4)]:
            c.ellipse(32, 31, radius, radius, slot, shade)
        for x, y in [(12, 12), (45, 12), (12, 45), (45, 45)]:
            c.rect(x, y, x+6, y+5, 'warning', 3)
            c.rect(x+2, y+1, x+4, y+4, 'hull', 0)
    else:
        for radius, slot, shade in [(20, 'hull', 0), (18, 'warning', 3), (15, 'hull', 1),
                                    (12, 'hull', 4), (9, 'floor', 1)]:
            c.ellipse(32, 31, radius, radius, slot, shade)
        c.rect(29, 23, 34, 39, 'hull', 3)
        c.rect(30, 25, 33, 37, 'coolant', 4)
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
            else:
                c.thick_line(26, 0, 37, 6, 2, 'warning', 4)
                c.thick_line(37, 0, 26, 6, 2, 'warning', 4)
    return c.render(PALETTE)


def crew(frame=0):
    c = canvas(24, 24)
    left, right = [(0, 0), (-1, 1), (0, 0), (1, -1), (0, 0)][frame]
    c.rect(7, 15+left, 10, 19+left, 'suit', 1)
    c.rect(13, 15+right, 16, 19+right, 'suit', 1)
    c.rect(6, 19+left, 10, 20+left, 'hull', 0)
    c.rect(13, 19+right, 17, 20+right, 'hull', 0)
    c.rect(6, 9, 17, 16, 'suit', 2)
    c.rect(8, 9, 15, 15, 'suit', 4)
    c.rect(4, 10, 6, 17-left, 'hull', 2)
    c.rect(17, 10, 19, 17-right, 'hull', 2)
    c.rect(9, 3, 14, 9, 'skin', 3)
    c.rect(7, 2, 16, 7, 'hull', 1)
    c.rect(8, 1, 15, 4, 'hull', 4)
    c.rect(9, 5, 14, 6, 'coolant', 4)
    c.rect(9, 11, 14, 12, 'warning', 4)
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
<p>Each card shows a 64px source at 1×, then 2× TV scale. Hatch overlays are independent and rotate in 90° steps. Decks carry I / II / III marks as well as distinct accents.</p><h2>Compartment and hatch samples</h2><div class="grid">'''+''.join(cards)+'''</div><h2>Crew source frames · 1×</h2><div class="crew">'''+f'<img alt="Compartment at 1x" src="{base}tiles/{tiles["med_bay"]["file"]}" width="64" height="64"><img alt="Ines standing at 1x" src="{base}crew/{person["idle"]["file"]}" width="24" height="24"><img alt="Four walking frames at 1x" src="{base}crew/{person["walk"]["file"]}" width="96" height="24">'+'''</div><h2>Standing and walking at TV scale</h2><div class="crew">'''+f'<img alt="Habitat compartment at 2x" src="{base}tiles/{tiles["med_bay"]["file"]}" width="128" height="128"><img alt="Ines standing at 2x" src="{base}crew/{person["idle"]["file"]}" width="48" height="48"><div role="img" aria-label="Ines walking at 2x" class="walker" style="background-image:url({base}crew/{person["walk"]["file"]})"></div>'+'''<button id="motion">Pause walk</button></div><p>24×24 frames, feet anchor (12,22), 6fps. Reduced-motion preference shows a still frame.</p><h2>Partly explored deck · 1920×1080</h2><p>1:1 native pixels. Scroll inside this panel on smaller screens. This is a concept mock, not the implemented game or a physical-TV test.</p><div class="pan" tabindex="0" aria-label="Full resolution TV mock; scroll to inspect"><img src="deck-mock-1080.png" width="1920" height="1080" alt="Habitat exploration mock with two crew, separate Command and Engineering samples, hatch legend and deck labels"></div>
<p><a href="https://github.com/higharc91/ArcBox/issues/331">Review issue #331</a> · <a href="../../../../../tools/meridian-assets/STYLE.md">Style and file contract</a> · <a href="palette.json">Exact palette ramps</a></p><script>const b=document.querySelector('#motion');b.onclick=()=>{document.body.classList.toggle('paused');b.textContent=document.body.classList.contains('paused')?'Play walk':'Pause walk'};</script></body></html>'''
    (review/'index.html').write_text(html)
    return pack, review


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=ROOT)
    args = parser.parse_args()
    build(args.root)
