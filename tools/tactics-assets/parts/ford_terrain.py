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
    'green': ['#5b433c', '#745c28', '#8c7d36', '#42852f', '#a9a548', '#c4b464'],
    'stone': ['#2a2835', '#454152', '#585464', '#706d7c', '#8e8c98', '#afacb9'],
    'water': ['#153b48', '#1c5262', '#286a78', '#3b8491', '#6fa9ad', '#acd3d0'],
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
    elif kind in ('grass','pasture','shore','dry_grass'):
        # Coherent low-contrast turf clusters, with exposed earth between them.
        # Shore patches share the earth base; reeds live inside these textures,
        # so this visual pass adds no props, collisions or simulation behaviour.
        if kind != 'dry_grass':
            count = 30 if kind == 'shore' else 58
            for _ in range(count):
                x,y=map(int,rng.integers(N,size=2));w=int(rng.integers(8,19));h=int(rng.integers(4,9))
                polygon([(x,y),(x+w-5,y-2),(x+w,y+2),(x+w-3,y+h),(x+3,y+h+1)], 2 if _%3 else 3, 'alt')
        for _ in range(110 if kind=='grass' else 70):
            x,y=map(int,rng.integers(N,size=2)); h=int(rng.integers(2,5))
            line([(x-2,y+1),(x+3,y+1)],2)
            for off,lean in [(-2,-1),(0,0),(2,1)]:
                line([(x+off,y),(x+off+lean,y-h+abs(off)//2)],3 if off else 4,'alt')
            if kind=='shore' and _%4==0:
                line([(x,y),(x-1,y-8)],3,'alt')
                line([(x-1,y-8),(x,y-10)],4,'alt')
        for _ in range(18):
            x,y=map(int,rng.integers(N,size=2));line([(x,y),(x+3,y-1)],2)
    elif kind == 'rock':
        for _ in range(56):
            x,y=map(int,rng.integers(N,size=2));w=int(rng.integers(4,11));h=int(rng.integers(3,7))
            polygon([(x,y+1),(x+w-2,y),(x+w,y+h),(x+2,y+h+1)],2)
            polygon([(x,y),(x+w-3,y-1),(x+w-1,y+2),(x+2,y+h-1)],4)
            line([(x+1,y),(x+w-3,y-1)],5)
    elif kind == 'water':
        # Broad, broken current bands make the river read as water at TV scale.
        # All strokes wrap, so the four-tile sheet has no visible joins.
        for i in range(54):
            x,y=map(int,rng.integers(N,size=2)); w=int(rng.integers(10,27))
            line([(x,y),(x+w//3,y-1),(x+2*w//3,y),(x+w,y-2)], 3 if i%3 else 4)
            if i%4==0:
                line([(x+3,y+2),(x+w//2,y+1)], 1)
            if i%5==0:
                line([(x+w//2+1,y-3),(x+w-3,y-4)], 5)
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
    if kind in ('grass','pasture','shore'):alt=RAMPS['green']
    if kind in ('grass','pasture'):main=RAMPS['grass']
    if kind=='dry_grass':alt=RAMPS['soil']
    return canvas, {'main':main,'alt':alt}


def cliff():
    """Quiet, crisp bank strata, at the same 32-pixel height scale as native terrain."""
    c=TR.cliff_side(seed=822)
    return c, {name: RAMPS['stone'] for name in TR.SLOTS.names[1:]}


TOPS = {
    'ash_ford': [('charred','ash',820),('roots','ash',828)],
    'grass_ford': [('autumn','grass',821),('dry','dry_grass',822),('pasture','pasture',829),('moist','shore',830),('moist_earth','shore',830)],
    'dirt_ford': [('wet','wet',823),('dry','dirt',824)],
    'path_ford': [('packed','path',825)],
    'rock_ford': [('bank','rock',826)],
    'ford_water': [('clear','water',827)],
}
