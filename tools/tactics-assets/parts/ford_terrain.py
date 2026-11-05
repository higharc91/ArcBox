"""Approved Ford ground direction, authored through the native indexed terrain canvas.

128 source pixels cover four tiles, matching the town's terrain density. Every
feature wraps, uses discrete palette slots and is drawn without resampling.
"""
import numpy as np
from PIL import Image, ImageDraw
from parts import terrain as TR

N = TR.T
RAMPS = {
    'ash': ['#2a2835', '#373740', '#484953', '#5e616a', '#7c8088', '#a4aab1'],
    'char': ['#140f1a', '#2a2835', '#373443', '#484554', '#5d5b69', '#8a8798'],
    'soil': ['#461814', '#5b433c', '#6e584d', '#857162', '#a08f7c', '#bdaf99'],
    'grass': ['#5b433c', '#6e584d', '#745c28', '#8c7d36', '#a08f7c', '#bdaf99'],
    'stone': ['#2a2835', '#454152', '#585464', '#706d7c', '#8e8c98', '#afacb9'],
    'water': ['#2a2835', '#484953', '#5e616a', '#7c8088', '#a4aab1', '#cdd4db'],
}


def top(kind, seed):
    rng = TR.tex.rng(seed)
    # Material regions carry the large shapes. Keep local texture quiet instead
    # of replacing the old soft camouflage with hard-edged camouflage blobs.
    base = 4 if kind == 'path' else 2 if kind in ('ash','water','wet','rock') else 3
    shades = np.full((N,N),base,dtype='uint8')
    shade = Image.fromarray(shades)
    slot = Image.new('L', (N, N), TR.SLOTS.id('main'))
    ds, dt = ImageDraw.Draw(shade), ImageDraw.Draw(slot)

    def polygon(points, value, material='main'):
        for dx in (-N, 0, N):
            for dy in (-N, 0, N):
                pts = [(x+dx, y+dy) for x,y in points]
                ds.polygon(pts, fill=value); dt.polygon(pts, fill=TR.SLOTS.id(material))

    def line(points, value, material='main'):
        for dx in (-N, 0, N):
            for dy in (-N, 0, N):
                pts = [(x+dx, y+dy) for x,y in points]
                ds.line(pts, fill=value); dt.line(pts, fill=TR.SLOTS.id(material))

    if kind == 'ash':
        for _ in range(22):
            x,y=map(int,rng.integers(N,size=2));w=int(rng.integers(5,12))
            polygon([(x,y),(x+w-2,y-1),(x+w,y+2),(x+2,y+3)],3)
        for _ in range(38):
            x,y = map(int,rng.integers(N,size=2)); w=int(rng.integers(3,10)); h=int(rng.integers(1,4))
            polygon([(x,y),(x+w-2,y-1),(x+w,y+h),(x+2,y+h+1)], 2, 'alt')
            line([(x+1,y),(x+w-2,y-1)], 3, 'alt')
            if w>6: line([(x+w-3,y),(x+w-2,y-3)],2,'alt')
        for _ in range(24):
            x,y=map(int,rng.integers(N,size=2))
            polygon([(x,y),(x+3,y-1),(x+5,y+1),(x+1,y+2)],4)
    elif kind in ('grass','dry_grass'):
        for _ in range(110 if kind=='grass' else 70):
            x,y=map(int,rng.integers(N,size=2)); h=int(rng.integers(2,5))
            line([(x-2,y+1),(x+3,y+1)],2)
            for off,lean in [(-2,-1),(0,0),(2,1)]:
                line([(x+off,y),(x+off+lean,y-h+abs(off)//2)],3 if off else 4,'alt')
        for _ in range(18):
            x,y=map(int,rng.integers(N,size=2));line([(x,y),(x+3,y-1)],2)
    elif kind == 'rock':
        for _ in range(56):
            x,y=map(int,rng.integers(N,size=2));w=int(rng.integers(4,11));h=int(rng.integers(3,7))
            polygon([(x,y+1),(x+w-2,y),(x+w,y+h),(x+2,y+h+1)],2)
            polygon([(x,y),(x+w-3,y-1),(x+w-1,y+2),(x+2,y+h-1)],4)
            line([(x+1,y),(x+w-3,y-1)],5)
    elif kind == 'water':
        # Short low-contrast current bands; shore foam stays with the shoreline art.
        for _ in range(36):
            x,y=map(int,rng.integers(N,size=2));w=int(rng.integers(3,12))
            line([(x,y),(x+w//2,y),(x+w,y-1)],3 if _%3 else 4)
            if _%4==0:line([(x+2,y+2),(x+w-1,y+2)],2)
    else:
        for _ in range(20 if kind=='path' else 30):
            x,y=map(int,rng.integers(N,size=2));w=int(rng.integers(2,5))
            polygon([(x,y),(x+w-1,y-1),(x+w,y+1),(x+1,y+2)],2)
            line([(x,y),(x+w-1,y-1)],4)
        for _ in range(12):
            x,y=map(int,rng.integers(N,size=2))
            line([(x,y),(x+3,y+1),(x+6,y)],2)
    canvas=TR._canvas(N,N,np.asarray(shade),np.asarray(slot))
    main = RAMPS['ash' if kind=='ash' else 'stone' if kind=='rock' else 'water' if kind=='water' else 'soil']
    alt = RAMPS['char' if kind=='ash' else 'grass']
    if kind=='dry_grass':alt=RAMPS['soil']
    return canvas, {'main':main,'alt':alt}


def cliff():
    """Quiet, crisp bank strata, at the same 32-pixel height scale as native terrain."""
    c=TR.cliff_side(seed=822)
    return c, {name: RAMPS['stone'] for name in TR.SLOTS.names[1:]}


TOPS = {
    'ash_ford': [('charred','ash',820),('roots','ash',828)],
    'grass_ford': [('autumn','grass',821),('dry','dry_grass',822)],
    'dirt_ford': [('wet','wet',823),('dry','dirt',824)],
    'path_ford': [('packed','path',825)],
    'rock_ford': [('bank','rock',826)],
    'ford_water': [('clear','water',827)],
}
