"""Issue #82 bridge candidates, authored as native town-kit geometry.

No generated pixels or concept crops. Export only into a disposable review pack
until the owner approves the pixel/TV stage. Geometry uses town's 32-unit tiles,
2:1 projection, material textures, selective outlines and top-left lighting.
"""
from __future__ import annotations
import copy
import json
import math
from pathlib import Path
import numpy as np
from PIL import Image
from . import town as T
from .lore_places import _rod

STATES = ('fortified', 'force', 'parley')
STONE = ('fieldstone', 'grey')
WOOD = T.solid('wood/oak', 3, grain=True)
CAP = T.solid('terrain/stone', 4)


def timber(a, b, p):
    """Continuous cross-laid deck boards, with paired nails and sparse wood knots."""
    x,y = p[:,0],p[:,1]
    ramp = np.array([T._hex_rgb(c) for c in T._ref('wood/oak')])
    seam = np.floor(x).astype(int) % 5 == 0
    shade = np.where(seam, 1, np.where(np.floor(x).astype(int)%5==1,4,3))
    grain = (np.floor(y*.4)+np.floor(x/5)*7).astype(int)%23==0
    shade = np.where(grain & ~seam, 2, shade)
    nail = ((y<8)|(y>56)) & (np.floor(x).astype(int)%5==3)
    shade = np.where(nail,1,shade)
    return np.c_[ramp[shade],np.full(len(x),255)].astype(np.float32)


def _skin(pale=False):
    def f(a,b,p):
        r=np.array([T._hex_rgb(c) for c in T._ref('town/canvas' if pale else 'leather/brown')])
        fold=(np.floor(a/7)+np.floor(b/9)).astype(int)
        shade=np.where(fold%7==0,2,3)
        seam=(np.floor(a).astype(int)%19==0)
        shade=np.where(seam,1,shade)
        stitch=seam & (np.floor(b).astype(int)%4==0)
        shade=np.where(stitch,4,shade)
        return np.c_[r[shade],np.full(len(a),255)].astype(np.float32)
    return f


def support():
    """Three actual open arches. Piers are geometry, not black painted holes."""
    m=T.Model(3,2,ground_shadow=False)
    stone=T.V_WALL(STONE)
    for y,n in ((0,(0,-1,0)),(64,(0,1,0))):
        holes=[]
        for cx in (16,48,80):
            pts=[(cx-11,y,-32.01)]
            pts += [(cx+11*math.cos(t),y,-25+13*math.sin(t)) for t in np.linspace(math.pi,0,13)]
            pts += [(cx+11,y,-32.01)]
            holes.append(pts)
        def masonry(a,b,p):
            out=stone(a,b,p)
            xx,zz=p[:,0],p[:,2]
            cx=np.floor(xx/32)*32+16
            radius=np.hypot((xx-cx), (zz+25)*11/13)
            ring=(radius>=11)&(radius<15)&(zz>=-25)
            wedge=(np.floor(np.arctan2((zz+25)*11/13,xx-cx)/math.pi*11+.5)).astype(int)
            out[ring,:3]*=np.where(wedge[ring]%2==0,1.12,.93)[:,None]
            return out
        m.poly([(0,y,-32),(96,y,-32),(96,y,-3),(0,y,-3)],masonry,n,holes=holes)
    # Intrados and solid piers across the channel, under the walking surface.
    for cx in (16,48,80):
        for a,b in zip(np.linspace(math.pi,0,13)[:-1],np.linspace(math.pi,0,13)[1:]):
            xa,za=cx+11*math.cos(a),-25+13*math.sin(a)
            xb,zb=cx+11*math.cos(b),-25+13*math.sin(b)
            m.poly([(xa,0,za),(xb,0,zb),(xb,64,zb),(xa,64,za)],T.solid('terrain/stone_dark',2),(-math.cos((a+b)/2),0,-math.sin((a+b)/2)),shade=.7)
    for x in (0,29,61,93):
        m.box(x,x+3,0,64,-32,-3,stone,None,side_u=T.U_WALL(STONE))
    return m


def deck():
    m=T.Model(3,2,ground_shadow=False)
    m.box(0,96,0,64,-3,0,WOOD,timber)
    for x in (0,31,63,94):m.box(x,x+2,-2,66,-7,-3,WOOD,WOOD)
    return m


def deck_tile():
    m=T.Model(1,1,ground_shadow=False);m.box(0,32,0,32,-2,0,WOOD,timber);return m


def parapet():
    m=T.Model(1,1,ground_shadow=False)
    m.box(0,32,13,19,0,10,T.V_WALL(STONE),CAP,side_u=T.U_WALL(STONE))
    m.box(0,32,12,20,10,12,T.V_WALL(STONE),CAP,side_u=T.U_WALL(STONE))
    return m


def hide_screen():
    m=T.Model(1,1,ground_shadow=False)
    for x in (2,30):_rod(m,(x,16,0),(x,16,39),radius=.8)
    m.poly([(3,16,13),(11,16,11),(18,16,14),(29,16,12),(29,16,35),(20,16,33),(11,16,34),(3,16,36)],_skin(),(0,1,0),cast=False)
    for x in (3,29):
        for z in (16,25,34):_rod(m,(x-1,15,z),(x+1,17,z+1),'prop/hay',.22)
    return m


def _stake(m,x,y,h=30):
    m.box(x-1.3,x+1.3,y-1.3,y+1.3,0,h-5,WOOD,T.solid('wood/oak',4))
    m.poly([(x-1.3,y+1.3,h-5),(x+1.3,y+1.3,h-5),(x,y,h)],T.solid('wood/oak',4),(0,1,0))
    m.poly([(x+1.3,y+1.3,h-5),(x+1.3,y-1.3,h-5),(x,y,h)],WOOD,(1,0,0))


def barricade():
    m=T.Model(1,2)
    for y in range(5,64,7):_stake(m,16,y,27+(y%3))
    for z in (7,19):m.box(17,20,3,61,z,z+4,WOOD,WOOD)
    for y in (8,56):
        _rod(m,(3,y,0),(18,y,22),radius=1.3)
        _rod(m,(28,y,0),(18,y,22),radius=1.3)
    return m


def gate_open():
    m=T.Model(1,2)
    for y in (1,61):
        for x in (3,25):_rod(m,(x,y,0),(x,y,22),radius=1)
        for z in (4,16):_rod(m,(3,y,z),(26,y,z),radius=1.5)
        for x in range(4,26,5):_rod(m,(x,y,3),(x,y,19),radius=.8)
    return m


def wreckage():
    m=T.Model(1,2)
    for i in range(9):
        y=7+i*5
        _rod(m,(2+(i%3)*3,y,1+(i%2)*2),(28-(i%3)*2,y+(-1 if i%2 else 1)*9,3+(i%3)*2),radius=1.7)
    for x,y in ((3,5),(24,54),(11,39)):_rod(m,(x,y,1),(x+5,y+4,11),radius=1.2)
    m.poly([(4,22,5),(20,18,7),(27,33,3),(17,37,2)],_skin(),(0,0,1),cast=False)
    return m


def rolled_hide():
    m=T.Model(1,1,ground_shadow=False)
    # A horizontal polygonal roll, with ropes attached around its girth.
    for k in range(12):
        a,b=k*math.tau/12,(k+1)*math.tau/12
        m.poly([(3,16+4*math.cos(a),5+4*math.sin(a)),(28,16+4*math.cos(a),5+4*math.sin(a)),(28,16+4*math.cos(b),5+4*math.sin(b)),(3,16+4*math.cos(b),5+4*math.sin(b))],_skin(True),(0,math.cos((a+b)/2),math.sin((a+b)/2)))
    for x in (8,23):
        for k in range(12):
            a,b=k*math.tau/12,(k+1)*math.tau/12
            _rod(m,(x,16+4.3*math.cos(a),5+4.3*math.sin(a)),(x,16+4.3*math.cos(b),5+4.3*math.sin(b)),'wood/dark',.25)
    return m


def stake_bundle():
    m=T.Model(1,1)
    for i in range(6):
        y=8+(i%3)*5;z=2+(i//3)*3
        _rod(m,(3,y,z),(29,y+2,z),radius=1.3)
    for x in (9,23):_rod(m,(x,6,1),(x,21,7),'prop/hay',.4)
    return m


def shelter(state='fortified'):
    m=T.Model(1,1)
    if state=='force':
        for i in range(3):_rod(m,(3+i*5,4,0),(24-i*4,29,5+i),radius=.7)
        m.poly([(2,2,1),(29,6,2),(21,20,4),(28,29,1),(7,28,1)],_skin(),(0,0,1),cast=False)
    else:
        for x in (2,30):_rod(m,(x,29,0),(x,29,25),radius=.7)
        m.poly([(0,0,34),(32,0,34),(30,29,25),(2,29,25)],_skin(state=='parley'),(0,.3,1),cast=True)
        m.poly([(2,29,25),(30,29,25),(27,30,20),(19,29,22),(12,30,20),(4,29,22)],_skin(state=='parley'),(0,1,0))
    return m


def gatehouse(state='fortified'):
    m=T.Model(2,2)
    def wall(base):
        def f(a,b,p):
            out=base(a,b,p)
            if state=='force':
                # Crisp uneven boundary, not a blurred gradient over the building.
                char=(p[:,0]>30)&(p[:,2]>22+((np.floor(p[:,1]/5).astype(int)%4)*3))
                rgb=np.array([T._hex_rgb(c) for c in T._ref('prop/soot')])
                out[char,:3]=rgb[1+(np.floor(p[char,2]/7).astype(int)%2)]
            return out
        return f
    m.box(6,58,6,56,0,28,wall(T.V_WALL(STONE)),None,side_u=wall(T.U_WALL(STONE)))
    m.box(6,58,6,56,28,55,wall(T.V_WALL(('timber_frame','ochre'),z0=28)),None,side_u=wall(T.U_WALL(('timber_frame','ochre'),z0=28)))
    T.gable(m,6,58,6,56,55,22,axis='v',roof=('shakes','grey'),gable_fn=T._gable_wall(('timber_frame','ochre'),z0=28),oh=4,ohg=4)
    # Roof soot is a localized material edit, with exact common geometry across states.
    if state=='force':
        for face in m.faces:
            if face.fn is None:continue
            fn=face.fn
            def scorch(a,b,p,fn=fn):
                out=fn(a,b,p); mask=(p[:,1]>35)&(p[:,2]>58)
                out[mask,:3]*=.32;return out
            face.fn=scorch
    door=T.solid('wood/dark',2,grain=True)
    m.box(17,36,56,57,1,43,door,T.solid('wood/oak',4))
    for x in (15,36):m.box(x,x+2,57,59,0,46,WOOD,WOOD)
    m.box(15,38,57,59,44,47,WOOD,WOOD)
    for z in (9,34):m.box(18,35,57.2,58.3,z,z+2,T.solid('metal/iron',2),None)
    m.blobs.append(T.Blob((32,59,23),.9,T._ref('metal/iron'),part=m.part()))
    # Chimney and a small side slit window preserve the quiet town architecture.
    m.box(12,21,17,27,62,87,T.V_WALL(STONE),T.solid('prop/soot',1),side_u=T.U_WALL(STONE))
    m.poly([(58.2,18,30),(58.2,32,30),(58.2,32,45),(58.2,18,45)],T.solid('wood/dark',0),(1,0,0))
    for y in (21,26,31):_rod(m,(58.8,y,30),(58.8,y,45),'wood/oak',.45)
    m.attach['door']=[(26,59,0)]
    return m


def approach(*, rise=32, descending=False):
    """Two-tile supported stone ramp, continuous from bank to deck."""
    m=T.Model(2,2,ground_shadow=False)
    z0,z1=(rise,0) if descending else (0,rise)
    # Masonry wedge on both faces; no unsupported floating tread.
    for y,n in ((0,(0,-1,0)),(64,(0,1,0))):
        pts=[(0,y,0),(64,y,0)]
        if z1:pts.append((64,y,z1))
        if z0:pts.append((0,y,z0))
        if len(pts)>=3:m.poly(pts,T.V_WALL(STONE),n)
    m.poly([(0,0,z0),(64,0,z1),(64,64,z1),(0,64,z0)],T.flat_top(('flagstone','grey')),(-z1+z0,0,64))
    # Curbs follow the grade down to the road instead of ending at a sheer wall.
    for y in (0,60):
        m.poly([(0,y+4,z0),(64,y+4,z1),(64,y+4,z1+7),(0,y+4,z0+7)],T.V_WALL(STONE),(0,1,0))
        m.poly([(0,y,z0+7),(64,y,z1+7),(64,y+4,z1+7),(0,y+4,z0+7)],CAP,(z0-z1,0,64))
    return m


def road_apron():
    m=T.Model(1,2,ground_shadow=False)
    m.poly([(0,0,0),(32,0,0),(32,64,0),(0,64,0)],T.flat_top(('flagstone','grey')),(0,0,1))
    return m


def join(target, source, offset=(0,0,0)):
    source=copy.deepcopy(source);delta=np.array(offset,float)
    for f in source.faces:f.o+=delta;f.part+=target._part
    for b in source.blobs:b.p=tuple(np.array(b.p)+delta);b.part+=target._part
    target.faces.extend(source.faces);target.blobs.extend(source.blobs);target._part+=source._part+1
    return target


def assembly(state):
    m=T.Model(5,4,ground_shadow=False)
    join(m,support());join(m,deck())
    join(m,approach(),(-64,0,-32));join(m,approach(rise=16,descending=True),(96,0,-16))
    join(m,road_apron(),(-96,0,-32));join(m,road_apron(),(160,0,-16))
    for x in (0,32,64):
        for y in (-16,48):join(m,parapet(),(x,y,0))
    join(m,gatehouse(state),(99,-64,-16));join(m,shelter(state),(115,-9,-16))
    if state=='fortified':
        for x in (32,64):
            for y in (-16,48):join(m,hide_screen(),(x,y,0))
        join(m,barricade(),(0,0,0))
    elif state=='force':
        join(m,wreckage(),(-30,70,-32))
        for x in (34,66):
            for y in (0,64):_rod(m,(x,y,0),(x,y,16),radius=.8)
    else:
        join(m,gate_open(),(-25,0,-12));join(m,stake_bundle(),(-30,82,-32))
        for x in (32,64):
            for y in (-16,48):join(m,rolled_hide(),(x,y,12))
        # A modest grain share, beside rather than across the road.
        for x,y in ((107,-5),(115,-2),(110,5)):
            m.blobs.append(T.Blob((x,y,-10),5,T._ref('prop/hay'),part=m.part(),squash=1.35))
    return m


BUILDERS={'warden_bridge_support':support,'warden_bridge_deck':deck,
 'warden_bridge_deck_tile':deck_tile,'warden_bridge_parapet':parapet,
 'warden_hide_screen':hide_screen,'warden_barricade':barricade,
 'warden_open_gate':gate_open,'warden_wreckage':wreckage,
 'warden_hide_roll':rolled_hide,'warden_stake_bundle':stake_bundle,
 'warden_approach_west':approach,'warden_approach_east':lambda:approach(rise=16,descending=True),
 'warden_road_apron':road_apron}
