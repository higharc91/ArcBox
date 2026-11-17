"""Original Meridian crew: authored silhouettes, directional pixel animation and independent portraits."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps
import build as style
import compartments
from arcpix.palette import ramp

ROOT=style.ROOT
REFERENCE=Path(__file__).parent/'reference/crew.json'
RULES_HEAD='1d770a7312e28d99c62fcfb7ebcd5bf398a758e9'
FACINGS=['south','east','north','west']
CLIPS={'idle':(3,3,True),'walk':(6,8,True),'hurt':(2,6,False),'down':(1,0,False)}
# Geometry and equipment establish identity; palette is supplementary, independent of seat rings.
DESIGNS={
'ines_okafor':dict(kind='chief',width=10,skin='#996649',hair='#343c49',cloth='#687f77',trim='warning',jaw=14,
 cue='Compact helmet, broad command collar and square survey tablet'),
 'tobe_raske':dict(kind='rigger',width=13,skin='#bf906e',hair='#715340',cloth='#a78251',trim='engineering',jaw=17,
 cue='Round hardhat, rope coil and projecting rigging hook'),
 'sable_quist':dict(kind='pilot',width=8,skin='#c79377',hair='#3e354c',cloth='#657a9d',trim='command',jaw=12,
 cue='Swept hair, flight collar, headset and asymmetric thigh holster'),
 'anselm_varga':dict(kind='medic',width=9,skin='#aa795e',hair='#aeb8ad',cloth='#b2b9a7',trim='red',jaw=15,
 cue='Long split medical coat, spectacles and large side medical case'),
 'pell_ostrander':dict(kind='tech',width=9,skin='#b9a078',hair='#705b51',cloth='#95856c',trim='coolant',jaw=13,
 cue='Square goggles, tall tool backpack and projecting probe'),
 'juno_achterberg':dict(kind='hauler',width=14,skin='#865841',hair='#303943',cloth='#8c7362',trim='warning',jaw=18,
 cue='Wide padded shoulders, load frame, cropped hair and heavy gauntlets'),
 'kit_sato':dict(kind='scout',width=7,skin='#c5a184',hair='#343b49',cloth='#717b87',trim='habitat',jaw=12,
 cue='Pointed hood, long asymmetric scarf and narrow stance'),
 'rhee_callender':dict(kind='signals',width=9,skin='#aa785a',hair='#4c4144',cloth='#74817a',trim='coolant',jaw=13,
 cue='Twin radio aerials, broad earcups and chest receiver')}


def rules(path=REFERENCE): return json.loads(path.read_text())['crew']


def palette(design):
    p=dict(style.PALETTE)
    for key in ['skin','hair','cloth']:
        p[key]=ramp(design[key],shift=.055,spread=.85)
    return p


def equipment(c,d,facing,bob=0):
    k=d['kind']; back=facing=='north'; side=facing=='east'; y=bob
    # Projecting equipment changes the external silhouette as well as the interior marks.
    if k=='chief':
        c.rect(4,11+y,7,16+y,'hull',0);c.rect(5,12+y,6,15+y,'coolant',3)
        c.rect(8,9+y,15,10+y,'warning',4)
    elif k=='rigger':
        c.ellipse(5,11+y,3,4,'engineering',0);c.ellipse(5,11+y,2,3,'engineering',4)
        c.ellipse(5,11+y,1,2,'hull',1)
        c.line(18,10+y,20,16+y,'hull',4);c.line(20,16+y,18,18+y,'hull',4)
        c.line(18,18+y,17,16+y,'hull',4)
    elif k=='pilot':
        c.rect(16,14+y,18,18+y,'hull',0);c.line(17,15+y,17,17+y,'command',4)
        c.rect(8,8+y,15,10+y,'cloth',4)
    elif k=='medic':
        c.rect(17,12+y,21,17+y,'hull',0);c.rect(18,13+y,20,16+y,'linen',4)
        c.line(19,13+y,19,16+y,'red',3);c.line(18,14+y,20,14+y,'red',3)
        if back: c.rect(10,11+y,13,14+y,'linen',4)
    elif k=='tech':
        c.rect(4,6+y,7,15+y,'hull',0);c.rect(5,7+y,6,13+y,'engineering',3)
        c.line(18,13+y,21,10+y,'hull',4);c.px(21,9+y,'coolant',5)
        if back: c.rect(9,9+y,15,15+y,'hull',2);c.line(11,10+y,11,14+y,'warning',4)
    elif k=='hauler':
        c.rect(5,7+y,18,11+y,'hull',0)
        c.rect(6,8+y,8,10+y,'warning',3);c.rect(15,8+y,17,10+y,'warning',3)
        c.rect(2,13+y,5,17+y,'hull',1);c.rect(18,13+y,21,17+y,'hull',1)
        if back:
            c.rect(8,9+y,15,15+y,'engineering',1)
            c.line(8,9+y,15,15+y,'engineering',4);c.line(15,9+y,8,15+y,'engineering',4)
    elif k=='scout':
        c.polygon([(7,8+y),(10,9+y),(7,17+y),(3,19+y)],'cloth',2)
        c.line(18,13+y,20,11+y,'hull',4);c.rect(19,10+y,21,11+y,'linen',4)
    elif k=='signals':
        for x,top in [(6,0),(17,2)]:
            c.line(x,top,x,9+y,'hull',1);c.px(x,top,'coolant',4)
        c.rect(8,10+y,15,13+y,'hull',0);c.rect(10,11+y,13,12+y,'coolant',4)
        if back: c.rect(8,8+y,15,15+y,'hull',2);c.line(10,10+y,13,10+y,'coolant',4)
    if side:
        # Equipment stays attached to the body; an east profile exposes the pack and leading tool.
        c.rect(5,9+y,7,14+y,'hull',1)


def board_head(c,d,facing,y=0):
    kind=d['kind'];back=facing=='north';side=facing=='east'
    cx=13 if side else 12
    if kind=='scout':
        c.polygon([(cx,1+y),(cx+5,5+y),(cx+4,9+y),(cx-4,9+y),(cx-5,5+y)],'cloth',1)
        if not back: c.rect(cx-2,5+y,cx+2,8+y,'skin',3)
        c.line(cx-2,3+y,cx+2,3+y,'cloth',4)
    elif kind in ['chief','rigger']:
        c.rect(cx-4,3+y,cx+3,8+y,'skin',3)
        c.ellipse(cx,4+y,5 if kind=='rigger' else 4,3,'hull' if kind=='chief' else 'engineering',3)
        c.line(cx-4,5+y,cx+4,5+y,'hull',1)
        if kind=='chief' and not back:
            c.rect(cx-2,5+y,cx+2,6+y,'coolant',3)
            c.line(cx-1,5+y,cx+2,5+y,'coolant',5)
        if kind=='rigger': c.line(cx-1,2+y,cx-1,3+y,'warning',5)
    else:
        c.rect(cx-3,3+y,cx+3,8+y,'skin',3)
        c.ellipse(cx,3+y,4,2,'hair',2)
        if kind=='pilot':
            c.polygon([(cx-4,1+y),(cx+3,1+y),(cx+4,3+y),(cx-2,5+y)],'hair',3)
            c.rect(cx-5,4+y,cx-4,7+y,'hull',1)
        elif kind=='medic':
            c.rect(cx-4,3+y,cx-3,6+y,'hair',4)
            if not back:
                c.line(cx-3,5+y,cx+3,5+y,'hull',0)
                c.px(cx,5+y,'linen',4)
        elif kind=='tech' and not back:
            c.rect(cx-4,4+y,cx+3,6+y,'hull',0)
            c.rect(cx-3,4+y,cx-1,5+y,'coolant',4)
            c.rect(cx+1,4+y,cx+2,5+y,'coolant',4)
        elif kind=='signals':
            c.rect(cx-5,4+y,cx-4,7+y,'hull',2)
            c.rect(cx+4,4+y,cx+5,7+y,'hull',2)
        elif kind=='hauler': c.rect(cx-3,2+y,cx+2,3+y,'hair',1)
    if back:
        c.rect(cx-3,4+y,cx+2,7+y,'hair' if kind not in ['chief','rigger','scout'] else 'hull',2)
        c.line(cx-2,7+y,cx+1,7+y,'hull',3)
    elif side:
        c.rect(cx+3,5+y,cx+4,6+y,'skin',4)
        c.px(cx+2,5+y,'hull',0)
    elif kind not in ['chief','tech','medic']:
        c.px(cx-2,6+y,'hull',0);c.px(cx+2,6+y,'hull',0)


def board(key,facing='south',clip='idle',frame=0,echo=False):
    d=DESIGNS[key]
    if facing=='west':
        return ImageOps.mirror(board(key,'east',clip,frame,echo))
    count=CLIPS[clip][0]
    if frame not in range(count): raise ValueError('frame outside clip')
    c=style.canvas(24,24);w=d['width'];side=facing=='east';back=facing=='north'
    if clip=='down':
        # Authored low pose keeps the figure above its stable feet anchor, without cropped rotations.
        c.rect(9,14,17,19,'cloth',2)
        c.rect(15,17,20,19,'cloth',1);c.rect(17,20,21,21,'hull',0)
        c.rect(11,19,15,20,'hull',2)
        head_y=13 if facing=='north' else 16 if facing=='south' else 15
        c.ellipse(7,head_y,4,4,'skin',3)
        c.ellipse(6,head_y-2,4,2,'hair' if d['kind'] not in ['chief','rigger','scout'] else 'hull',3)
        if d['kind']=='scout': c.polygon([(3,11),(10,12),(9,18),(3,17)],'cloth',1)
        if d['kind']=='rigger': c.ellipse(12,13,3,3,'engineering',4)
        if d['kind']=='medic': c.rect(9,17,14,20,'linen',4);c.line(11,17,11,20,'red',3)
        if d['kind']=='signals': c.line(4,11,4,7,'hull',2);c.line(8,10,8,6,'hull',2)
        if d['kind']=='hauler': c.rect(10,12,18,17,'hull',1)
        if d['kind']=='tech': c.rect(13,11,18,15,'hull',2)
        if d['kind']=='chief': c.rect(6,14,9,15,'coolant',4)
        if d['kind']=='pilot': c.rect(14,15,18,17,'command',3)
    else:
        left_step=([-1,0,1,1,0,-1][frame] if clip=='walk' else 0)
        right_step=([1,1,0,-1,-1,0][frame] if clip=='walk' else 0)
        breath=-1 if clip=='idle' and frame==1 else 0
        hurt=1 if clip=='hurt' else 0
        dy=breath+hurt
        left=9 if side else max(7,12-w//3-2)
        right=13 if side else min(14,12+w//3)
        for x,offset in [(left,left_step),(right,right_step)]:
            c.rect(x,15,x+2,19+offset,'cloth',1)
            c.line(x,16,x,18+offset,'cloth',3)
            c.rect(x-1,19+offset,x+3,20+offset,'hull',0)
            c.line(x,19+offset,x+2,19+offset,'hull',3)
        bw=7 if side else w
        x0=12-bw//2
        c.polygon([(x0,9+dy),(x0+bw,9+dy),(x0+bw-1,15+dy),(x0+1,16+dy)],'cloth',2)
        c.rect(x0+2,10+dy,x0+bw-2,13+dy,'cloth',4)
        c.rect(x0+1,15+dy,x0+bw-1,16+dy,'hull',0)
        c.px(12,15+dy,d['trim'],4)
        for x,offset in [(x0-2,-left_step),(x0+bw+1,-right_step)]:
            c.rect(x,10+dy,x+1,14+offset+dy,'cloth',2)
            c.rect(x,14+offset+dy,x+1,15+offset+dy,'hull',2)
        if d['kind']=='medic':
            c.rect(x0,13+dy,11,18+dy,'linen',3)
            c.rect(13,13+dy,x0+bw,18+dy,'linen',3)
        if back:
            c.rect(x0+2,10+dy,x0+bw-2,14+dy,'cloth',1)
            c.line(x0+2,11+dy,x0+bw-2,11+dy,d['trim'],3)
        equipment(c,d,facing,dy)
        board_head(c,d,facing,hurt)
        if clip=='hurt':
            c.line(8,10+frame,16,12+frame,'hull',0)
            c.rect(14,9,16,10,'linen',4)
    c.outline()
    image=c.render(palette(d))
    return echo_image(image) if echo else image


def echo_image(image):
    pixels=np.asarray(image).copy()
    rgb=pixels[:,:,:3].astype(float)
    luminance=rgb@np.array([.2126,.7152,.0722])
    # Persistent translucent scan bands, never flashing or strobing in time.
    for channel,(base,gain) in enumerate([(29,.32),(72,.48),(86,.48)]):
        pixels[:,:,channel]=np.clip(base+luminance*gain,0,254).astype('uint8')
    pixels[:,:,3]=np.where(pixels[:,:,3]>0,190,0).astype('uint8')
    pixels[3::6,:,3]=(pixels[3::6,:,3]*.62).astype('uint8')
    return Image.fromarray(pixels)


class FaceCanvas:
    """Integer-coordinate portrait brush; faces are authored independently of the 24px board sprite."""
    def __init__(self,c,cx,cy,scale=1): self.c,self.cx,self.cy,self.s=c,cx,cy,scale
    def rect(self,x0,y0,x1,y1,mat,shade=3): self.c.rect(self.cx+x0*self.s,self.cy+y0*self.s,self.cx+x1*self.s,self.cy+y1*self.s,mat,shade)
    def line(self,x0,y0,x1,y1,mat,shade=3,width=1): self.c.thick_line(round(self.cx+x0*self.s),round(self.cy+y0*self.s),round(self.cx+x1*self.s),round(self.cy+y1*self.s),max(1,round(width*self.s)),mat,shade)
    def ellipse(self,x,y,rx,ry,mat,shade=3): self.c.ellipse(self.cx+x*self.s,self.cy+y*self.s,rx*self.s,ry*self.s,mat,shade)
    def poly(self,points,mat,shade=3): self.c.polygon([(self.cx+x*self.s,self.cy+y*self.s) for x,y in points],mat,shade)


def face(c,d,cx,cy,expression='neutral',scale=1):
    p=FaceCanvas(c,cx,cy,scale);kind=d['kind'];jaw=d['jaw']
    p.ellipse(0,-2,18,24,'skin',1)
    p.poly([(-16,-12),(15,-12),(16,9),(jaw//2,22),(-jaw//2,22),(-16,7)],'skin',3)
    p.poly([(-11,-8),(7,-12),(12,-4),(9,12),(1,18),(-9,12)],'skin',4)
    p.rect(-18,-1,-15,7,'skin',2);p.rect(15,-1,18,7,'skin',2)
    # Discrete eye sockets, brows, nose planes and mouth/jaw rather than an upscaled sprite face.
    for x in [-8,8]:
        p.line(x-4,-4,x+3,-5 if expression=='hurt' else -4,'hair',1,2)
        p.rect(x-3,-1,x+2,2,'skin',1)
        if expression=='hurt' and x<0: p.line(x-3,1,x+2,1,'hull',0,2)
        else:
            p.rect(x-3,0,x+2,1,'linen',3);p.rect(x-1,0,x,2,'hull',0)
    p.poly([(-1,-2),(-4,8),(2,10),(4,7),(1,7)],'skin',2)
    p.line(0,2,0,7,'skin',5)
    p.line(-5,14,5,14 if expression!='hurt' else 12,'skin',0,2)
    p.line(-3,17,3,17,'skin',2)
    p.line(-11,8,-7,10,'skin',2);p.line(8,9,12,7,'skin',2)
    # Eight hairstyles / headgear with matching board silhouettes.
    if kind=='chief':
        p.poly([(-21,-20),(-14,-29),(13,-29),(22,-18),(22,-4),(16,-4),(15,-19),(-15,-19),(-16,-4),(-22,-4)],'hull',1)
        p.poly([(-16,-24),(15,-24),(18,-19),(-18,-19)],'hull',4)
        p.rect(-22,-12,-18,2,'hull',3);p.rect(18,-12,22,2,'hull',3)
        p.line(-16,-16,15,-16,'coolant',4,2)
        p.line(-16,3,16,3,'coolant',2,1)
        p.line(-21,4,-15,10,'hull',2,2)
    elif kind=='rigger':
        p.ellipse(0,-20,24,13,'engineering',1)
        p.ellipse(0,-21,22,11,'engineering',3)
        p.rect(-27,-16,27,-12,'hull',0)
        p.rect(-25,-16,25,-14,'warning',4)
        p.rect(-3,-30,2,-19,'warning',5)
        p.poly([(-13,7),(-9,14),(0,19),(10,14),(13,7),(12,19),(5,25),(-6,25),(-13,18)],'hair',2)
    elif kind=='pilot':
        p.poly([(-18,-6),(-23,-18),(-17,-29),(8,-30),(20,-24),(21,-14),(11,-12),(2,-22),(-13,-13)],'hair',1)
        p.poly([(-19,-19),(-13,-26),(6,-27),(12,-23),(-3,-14)],'hair',3)
        p.rect(-23,-8,-19,7,'hull',1)
        p.line(-22,-9,-22,7,'hull',4,2);p.line(-19,8,-12,11,'hull',3,2)
        p.rect(-13,10,-10,12,'coolant',4)
    elif kind=='medic':
        p.poly([(-17,-3),(-22,-10),(-19,-25),(-10,-30),(5,-29),(18,-20),(20,-7),(16,-1),(14,-19),(0,-22),(-13,-17)],'hair',3)
        p.line(-14,-25,-4,-27,'hair',5,3);p.line(5,-25,14,-21,'hair',4,3)
        for x in [-9,8]:
            p.rect(x-6,-3,x+5,5,'hull',0);p.rect(x-4,-1,x+3,3,'coolant',1)
            p.line(x-3,-1,x+2,-1,'linen',4)
        p.line(-3,-1,2,-1,'hull',2,2)
        p.line(-12,9,-8,11,'skin',1);p.line(9,11,13,8,'skin',1)
        p.line(-7,20,5,21,'hair',2,2)
    elif kind=='tech':
        p.poly([(-20,-6),(-22,-22),(-11,-28),(15,-26),(20,-15),(16,-8),(12,-19),(-13,-17)],'hair',2)
        p.rect(-22,-20,20,-13,'hull',1)
        for x in [-10,9]:
            p.rect(x-7,-24,x+7,-13,'hull',0);p.rect(x-5,-22,x+5,-15,'coolant',2)
            p.line(x-3,-21,x+3,-21,'coolant',5,2)
        p.line(-5,-10,-1,-12,'hair',2,2)
        p.rect(-11,6,-8,9,'skin',1)
    elif kind=='hauler':
        p.poly([(-18,-5),(-21,-16),(-14,-28),(9,-29),(20,-19),(17,-6),(14,-17),(-14,-17)],'hair',1)
        p.rect(-12,-25,10,-20,'hair',3)
        p.line(-13,10,-8,15,'skin',1,2);p.line(9,15,14,9,'skin',1,2)
        p.line(-6,19,7,19,'skin',2,2)
        p.rect(17,-3,20,2,'hull',4)
    elif kind=='scout':
        p.poly([(-25,13),(-26,-12),(-15,-29),(0,-35),(17,-26),(25,-9),(23,16),(15,12),(16,-10),(7,-21),(-9,-22),(-17,-10),(-15,13)],'cloth',1)
        p.line(-21,-11,-13,-24,'cloth',4,3);p.line(4,-28,17,-18,'cloth',3,3)
        p.poly([(-15,13),(0,19),(17,12),(17,24),(2,29),(-16,23)],'cloth',3)
        p.line(-12,18,11,19,'cloth',4,2)
    elif kind=='signals':
        p.poly([(-18,-6),(-20,-22),(-11,-29),(13,-26),(19,-15),(16,-7),(12,-18),(-13,-18)],'hair',2)
        p.rect(-24,-11,-18,9,'hull',1);p.rect(18,-11,24,9,'hull',1)
        p.rect(-23,-7,-20,5,'hull',4);p.rect(20,-7,23,5,'hull',4)
        p.line(-22,-9,-22,-35,'hull',1,2);p.rect(-24,-37,-20,-34,'coolant',4)
        p.line(21,-8,21,-29,'hull',2,2);p.rect(19,-31,23,-28,'coolant',4)
        p.line(21,11,11,14,'hull',2,2);p.rect(8,12,12,15,'coolant',4)
    if expression=='hurt':
        p.rect(6,6,13,9,'linen',4);p.line(7,7,11,8,'linen',2)
        p.line(-12,5,-9,8,'red',2,2)


def portrait(key,expression='neutral',card=False):
    d=DESIGNS[key];kind=d['kind'];c=style.canvas(96,160 if card else 112)
    if card:
        # Independent full-body drawing, with human proportions and role equipment.
        for x in [34,53]:
            c.polygon([(x,101),(x+12,101),(x+10,143),(x+4,147),(x-1,143)],'cloth',2)
            c.rect(x+2,106,x+7,129,'cloth',3)
            c.rect(x-3,140,x+12,149,'hull',0);c.rect(x-2,140,x+10,143,'hull',3)
        c.polygon([(26,58),(67,58),(70,83),(63,111),(30,111),(23,83)],'cloth',2)
        c.polygon([(35,59),(57,59),(62,87),(55,103),(37,103),(31,85)],'cloth',4)
        for x in [19,68]:
            c.polygon([(x,61),(x+10,61),(x+12,87),(x+9,102),(x+2,100),(x-2,81)],'cloth',2)
            c.rect(x+1,93,x+10,103,'hull',1)
        c.rect(29,98,65,104,'hull',0);c.rect(44,98,51,104,d['trim'],4)
        if kind=='medic':
            c.rect(29,104,44,124,'linen',3);c.rect(52,104,66,124,'linen',3)
        body_gear(c,d,card=True)
        c.rect(41,53,54,63,'skin',2)
        face(c,d,48,43,expression,.66)
    else:
        # Broad bust gives face/gear enough pixels for phone and TV portraits.
        width=40 if kind=='hauler' else 33 if kind in ['rigger','chief'] else 29
        c.polygon([(48-width,80),(28,69),(37,64),(59,64),(69,69),(48+width,80),(94,111),(2,111)],'cloth',1)
        c.polygon([(22,80),(38,70),(59,70),(76,81),(84,111),(13,111)],'cloth',3)
        c.rect(37,61,58,78,'skin',2)
        c.polygon([(34,72),(48,83),(62,72),(65,89),(48,97),(31,89)],'cloth',4)
        c.line(48,87,48,111,'hull',1)
        body_gear(c,d)
        face(c,d,48,46,expression)
    image=c.render(palette(d))
    return echo_image(image) if expression=='echo' else image


def body_gear(c,d,card=False):
    k=d['kind'];y=72 if card else 82
    if k=='chief':
        c.polygon([(26,y-8),(44,y+5),(39,y+11),(23,y-1)],'warning',3)
        c.polygon([(70,y-8),(53,y+5),(57,y+11),(73,y-1)],'warning',3)
        c.rect(14,y+14,32,y+33,'hull',0);c.rect(17,y+17,29,y+29,'coolant',2)
        c.line(19,y+20,27,y+20,'coolant',4)
    elif k=='rigger':
        for radius,shade in [(15,0),(13,4),(10,1),(8,4),(5,1)]: c.ellipse(26,y+6,radius,radius,'engineering',shade)
        c.line(67,y-7,73,y+24,'hull',4)
        c.line(73,y+24,65,y+30,'hull',4,);c.line(65,y+30,62,y+21,'hull',4)
    elif k=='pilot':
        c.polygon([(26,y-7),(42,y+7),(35,y+14),(22,y+2)],'cloth',4)
        c.polygon([(69,y-7),(53,y+7),(60,y+14),(75,y+2)],'cloth',4)
        c.rect(55,y+12,64,y+20,'command',1);c.line(57,y+15,62,y+15,'linen',4)
    elif k=='medic':
        c.polygon([(27,y-7),(45,y+4),(40,y+30),(17,y+30)],'linen',4)
        c.polygon([(68,y-7),(51,y+4),(56,y+30),(78,y+30)],'linen',3)
        c.rect(59,y+6,64,y+22,'red',3);c.rect(54,y+11,69,y+16,'red',3)
        if card:
            c.rect(66,96,83,117,'hull',0);c.rect(68,98,81,115,'linen',4)
            c.rect(73,100,76,112,'red',3);c.rect(69,104,80,108,'red',3)
    elif k=='tech':
        c.rect(17,y-12,29,y+17,'hull',1);c.rect(20,y-9,26,y+10,'engineering',3)
        c.line(32,y-2,52,y+21,'warning',3);c.line(59,y-2,42,y+21,'warning',3)
        c.rect(37,y+8,59,y+23,'hull',0);c.rect(40,y+11,56,y+18,'coolant',2)
        c.line(73,y+12,82,y-4,'hull',4);c.rect(80,y-8,84,y-4,'coolant',4)
    elif k=='hauler':
        for x in [13,63]:
            c.polygon([(x,y-9),(x+17,y-7),(x+21,y+8),(x+16,y+17),(x-2,y+13)],'hull',1)
            c.rect(x+3,y-3,x+13,y+1,'warning',4)
        c.rect(31,y+3,37,y+28,'hull',2);c.rect(57,y+3,63,y+28,'hull',2)
        c.rect(35,y+18,59,y+25,'engineering',2)
    elif k=='scout':
        c.polygon([(22,y-13),(49,y-2),(72,y-10),(71,y+3),(48,y+12),(24,y+1)],'cloth',2)
        c.polygon([(28,y-1),(40,y+6),(28,y+35),(14,y+32)],'cloth',3)
        c.line(25,y+6,20,y+27,'cloth',4)
        if card: c.rect(71,93,80,98,'linen',4)
    elif k=='signals':
        c.rect(30,y+4,65,y+25,'hull',0);c.rect(34,y+8,61,y+16,'coolant',2)
        for x in [37,43,49,55]: c.rect(x,y+11,x+2,y+13,'coolant',4)
        c.line(21,y-6,31,y+5,'hull',4);c.line(74,y-7,64,y+5,'hull',4)


def strip(images):
    output=Image.new('RGBA',(images[0].width*len(images),images[0].height))
    for i,im in enumerate(images): output.alpha_composite(im,(i*im.width,0))
    return output


def build(root=ROOT,reference=REFERENCE):
    pack=root/'content/meridian/base';review=root/'docs/superpowers/reports/meridian-art/crew';review.mkdir(parents=True,exist_ok=True)
    source=rules(reference)
    if {x['id'] for x in source}!=set(DESIGNS): raise ValueError('Crew IDs differ from the authored designs')
    common={'version':2,'issue':333,'status':'awaiting-set-review','rulesReference':{'pr':316,'head':RULES_HEAD,'sha256':hashlib.sha256(reference.read_bytes()).hexdigest()}}
    members={};portraits={}
    for person in source:
        key=person['id'];facings={};echo={}
        for facing in FACINGS:
            facings[facing]={};echo[facing]={}
            for clip,(frames,fps,loop) in CLIPS.items():
                for is_echo,target in [(False,facings),(True,echo)]:
                    images=[board(key,facing,clip,i,is_echo) for i in range(frames)]
                    target[facing][clip]={**style.immutable(pack,'crew',key+'-'+facing+'-'+clip+('-echo' if is_echo else ''),strip(images)),
                        'frameSize':[24,24],'frames':frames,'fps':fps,'loop':loop,'blend':'alpha',
                        'reducedMotionFrame':0,'seconds':frames/fps if fps else None}
        members[key]={'name':person['name'],'role':person['role'],'frameSize':[24,24],'anchor':[12,22],
            'silhouetteCue':DESIGNS[key]['cue'],'facings':facings,'echo':echo,
            # M4 draws the entire idle image into 24x24; give it a true one-frame image.
            'idle':{**style.immutable(pack,'crew',key+'-compat-idle',board(key)), 'frameSize':[24,24], 'frames':1,'fps':0,'loop':False,'reducedMotionFrame':0},
            'walk':facings['south']['walk'],
            'compatibility':'M4 flat idle is a single 24px pose; full directional animation is in facings/echo.',
            'echoTreatment':'static translucent scan bands; no temporal flicker; same idle/walk cadence as living crew'}
        portraits[key]={'name':person['name'],'role':person['role'],'bustSize':[96,112],'crewCardSize':[96,160]}
        for expression in ['neutral','hurt','echo']:
            portraits[key][expression]={**style.immutable(pack,'portraits',key+'-'+expression,portrait(key,expression)),'frameSize':[96,112]}
        portraits[key]['crewCard']={**style.immutable(pack,'portraits',key+'-crew-card',portrait(key,card=True)),'frameSize':[96,160]}
    style.write_json(pack/'crew/manifest.json',{**common,'crew':members})
    style.write_json(pack/'portraits/manifest.json',{**common,'portraits':portraits})
    style.write_json(review/'palette.json',{key:palette(d) for key,d in DESIGNS.items()})
    review_page(review,pack,source,members,portraits)
    return pack,review


def label(image,xy,text):
    d=ImageDraw.Draw(image);d.fontmode='1';d.text(xy,text,font=ImageFont.load_default(),fill=style.PAPER)


def review_page(review,pack,source,members,portraits):
    base_url='../../../../../content/meridian/base/'
    # Native 1080p crew board, with all eight at required 48px TV size on 128px rooms.
    lineup=Image.new('RGBA',(960,540),style.INK)
    label(lineup,(24,16),'DERELICT: ISV MERIDIAN / EIGHT CREW / EXACT TV SCALE')
    label(lineup,(24,40),'COLOUR / GREYSCALE / ECHO - 24px SOURCE, 2x TV, FEET ANCHOR (12,22)')
    for i,person in enumerate(source):
        key=person['id'];x=24+(i%4)*230;y=90+(i//4)*210
        room=next(r for r in compartments.rules() if r['id']=='med_bay')
        tile=compartments.compose(compartments.tile(room,'habitat'),room)
        for offset,mode in [(0,'colour'),(76,'grey'),(152,'echo')]:
            panel=tile.copy();sprite=board(key,echo=mode=='echo');panel.alpha_composite(sprite,(20,24))
            if mode=='grey':
                alpha=panel.getchannel('A');panel=ImageOps.grayscale(panel).convert('RGBA');panel.putalpha(alpha)
            lineup.alpha_composite(panel,(x+offset,y))
        label(lineup,(x,y+78),person['name'])
        label(lineup,(x,y+95),person['role'])
        label(lineup,(x,y+115),'COLOUR    GREY     ECHO')
    lineup.resize((1920,1080),Image.Resampling.NEAREST).convert('RGB').save(review/'lineup-tv.png')
    # Large reference line-up for silhouette and pose inspection, clearly labelled as an enlargement.
    reference=Image.new('RGBA',(800,230),style.INK)
    label(reference,(12,8),'CREW REFERENCE / 3x SOURCE / COLOUR AND GREYSCALE')
    for i,person in enumerate(source):
        x=12+i*98;sprite=board(person['id'])
        reference.alpha_composite(sprite.resize((72,72),Image.Resampling.NEAREST),(x,36))
        grey=ImageOps.grayscale(sprite).convert('RGBA');grey.putalpha(sprite.getchannel('A'))
        reference.alpha_composite(grey.resize((72,72),Image.Resampling.NEAREST),(x,122))
        label(reference,(x,205),person['name'].removeprefix('Dr. ').split()[0])
    # This reference is 3x, not the actual TV presentation above.
    label(reference,(12,22),'3x DETAIL; ACTUAL TV SCALE IS SHOWN IN THE 1080p BOARD')
    reference.convert('RGB').save(review/'crew-reference.png')
    sheet=Image.new('RGBA',(800,560),style.INK)
    label(sheet,(12,8),'CREW PORTRAITS / 96x112 SOURCE / INDEPENDENT DRAWINGS')
    for i,person in enumerate(source):
        x=10+(i%4)*198;y=38+(i//4)*255
        sheet.alpha_composite(portrait(person['id']),(x,y))
        sheet.alpha_composite(portrait(person['id'],'echo'),(x+96,y))
        label(sheet,(x,y+124),person['name']);label(sheet,(x,y+142),person['role'])
        label(sheet,(x,y+161),'NEUTRAL      ECHO')
    sheet.convert('RGB').save(review/'portraits-contact.png')
    facings_sheet=Image.new('RGBA',(800,400),style.INK)
    label(facings_sheet,(12,8),'FOUR FACINGS AT TV 2x / SOUTH, EAST, NORTH, WEST')
    for i,person in enumerate(source):
        x=12+(i%4)*196;y=38+(i//4)*174
        for j,facing in enumerate(FACINGS):
            facings_sheet.alpha_composite(board(person['id'],facing).resize((48,48),Image.Resampling.NEAREST),(x+j*46,y))
        for frame in range(6):
            facings_sheet.alpha_composite(board(person['id'],'south','walk',frame),(x+frame*28,y+70))
        label(facings_sheet,(x,y+108),person['name'])
        label(facings_sheet,(x,y+126),'SIX WALK PHASES AT 1x')
    facings_sheet.convert('RGB').save(review/'facings-review.png')
    # Portrait/crew-pick reviews at their proposed exact 2x TV scale.
    for mode in ['portraits','cards']:
        screen=Image.new('RGBA',(960,540),style.INK)
        label(screen,(24,16),'MERIDIAN / '+mode.upper()+' / EXACT 2x TV REVIEW')
        for i,person in enumerate(source):
            key=person['id'];x=24+(i%4)*230;y=65+(i//4)*225
            if mode=='portraits':
                screen.alpha_composite(portrait(key),(x,y))
                screen.alpha_composite(portrait(key,'hurt'),(x+108,y))
                label(screen,(x,y+124),'NEUTRAL       HURT')
            else:
                screen.alpha_composite(portrait(key,card=True),(x,y))
                screen.alpha_composite(portrait(key,'echo'),(x+108,y))
                label(screen,(x,y+170),'CREW CARD     ECHO')
            label(screen,(x,y+190),person['name'])
            label(screen,(x,y+204),person['role'])
        screen.resize((1920,1080),Image.Resampling.NEAREST).convert('RGB').save(review/(mode+'-tv.png'))
    articles=[]
    for person in source:
        key=person['id'];m=members[key];p=portraits[key]
        clips=[]
        for facing in FACINGS:
            for state,frames in m['facings'][facing].items():
                url=base_url+'crew/'+frames['file'];n=frames['frames'];fps=frames['fps']
                echo_variant=base_url+'crew/'+m['echo'][facing][state]['file']
                end=-48*(n if frames['loop'] else n-1)
                duration=(n if frames['loop'] else n-1)/fps if fps else 0
                steps=n if frames['loop'] else max(1,n-1)
                # One-shot clips have explicit Replay controls; looping clips share Pause/Still controls.
                replay=f'<button type="button" class="replay-pose" aria-label="Replay {person["name"]} {facing} {state}">Replay {state}</button>' if n>1 and not frames['loop'] else ''
                classes='sprite '+('loop' if frames['loop'] else 'once') if n>1 else 'sprite'
                clips.append(f'<figure><figcaption>{facing} · {state}</figcaption><div class="{classes}" data-live="{url}" data-echo="{echo_variant}" role="img" aria-label="{person["name"]}, {facing}, {state}" style="--end:{end}px;--duration:{duration}s;--steps:{steps};background-image:url({url});background-size:{n*48}px 48px"></div><img class="strip" data-live="{url}" data-echo="{echo_variant}" alt="{facing} {state} source strip" src="{url}" width="{n*24}" height="24">{replay}</figure>')
        art=''.join(f'<figure><figcaption>{state}</figcaption><img alt="{person["name"]}, {state}" src="{base_url}portraits/{p[state]["file"]}" width="96" height="{160 if state=="crewCard" else 112}"></figure>' for state in ['neutral','hurt','echo','crewCard'])
        echo_url=base_url+'crew/'+m['echo']['south']['walk']['file']
        articles.append(f'<article><h2>{person["name"]} · {person["role"]}</h2><p>{m["silhouetteCue"]}.</p><div class="portraits">{art}</div><div class="clips">'+''.join(clips)+f'<figure><figcaption>echo · south walk</figcaption><div class="sprite loop echo" role="img" aria-label="{person["name"]} echo walking" style="--end:-288px;--duration:.75s;--steps:6;background-image:url({echo_url});background-size:288px 48px"></div><p>All four echo facings/poses are in the manifest.</p></figure></div></article>')
    # Motion samples are enlarged detail references, not a claim of runtime integration.
    for clip in ['idle','walk','echo']:
        frames=[]
        n,fps,_=CLIPS['walk' if clip=='echo' else clip]
        for frame in range(n):
            image=Image.new('RGBA',(640,140),style.INK)
            label(image,(12,8),'CREW '+clip.upper()+' / 3x DETAIL PREVIEW')
            for i,person in enumerate(source):
                image.alpha_composite(board(person['id'],'south','walk' if clip=='echo' else clip,frame,clip=='echo').resize((72,72),Image.Resampling.NEAREST),(8+i*79,32))
                label(image,(8+i*79,112),person['name'].removeprefix('Dr. ').split()[0])
            frames.append(image.convert('RGB'))
        frames[0].save(review/(clip+'-preview.gif'),save_all=True,append_images=frames[1:],duration=round(1000/fps),loop=0,disposal=2)
    html='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian crew review</title><style>
:root{color-scheme:dark;font:17px/1.5 system-ui;background:#111b2a;color:#d8e1da}*{box-sizing:border-box}body{margin:auto;max-width:1240px;padding:16px}h1{line-height:1.1}img,.sprite{image-rendering:pixelated}a{color:#9ee0ed}button,select{font:inherit;min-height:44px;padding:10px;background:#26384b;color:#d8e1da;border:1px solid #657482}.pan{overflow:auto;max-height:75vh;border:1px solid #657482}.pan img{display:block;max-width:none}article{border-top:2px solid #657482;padding-top:16px;margin-top:24px}.portraits,.clips{display:flex;gap:16px;flex-wrap:wrap}figure{padding:12px;border:1px solid #657482;margin:0 0 16px;max-width:100%}figcaption{margin-bottom:10px}.strip{display:block;margin-top:12px;max-width:none}.sprite{width:48px;height:48px;animation:frames var(--duration) steps(var(--steps)) infinite}.sprite.once{animation-iteration-count:1;animation-fill-mode:forwards}.sprite:not(.loop):not(.once){animation:none}@keyframes frames{from{background-position:0 0}to{background-position:var(--end) 0}}body.paused .sprite{animation-play-state:paused}body.still .sprite{animation:none;background-position:0 0}@media(prefers-reduced-motion:reduce){.sprite{animation:none;background-position:0 0}}.clips figure{width:180px}label{display:inline-block;margin-left:12px;min-height:44px}
</style><body><h1>Meridian / eight crew</h1><p>Original salvage team assets for #333. Each role has different headgear, proportions and projecting equipment, independent of player colour. Names/roles remain live UI text. This is an art review, not a physical-TV or human recognition test.</p><p>24×24 board frames, anchor (12,22), four authored facings. Idle 3 frames / 3fps; walk 6 / 8fps; hurt 2 / 6fps one-shot; down 1 static. Echoes use stable translucency and scan bands with no temporal flashing. Reduced motion shows a still frame.</p><p><label>Board variant <select id="variant"><option value="live">Living</option><option value="echo">Echo</option></select></label> <label>Portrait size <select id="portrait-size"><option value="1">Phone source 1×</option><option value="2">TV 2×</option></select></label></p><button id="pause">Pause motion</button> <button id="replay">Replay poses</button><label><input id="still" type="checkbox">Still previews</label><h2>Actual TV scale / native 1920×1080</h2><p>128px room tiles and 48px crew; colour, greyscale and echo. Pan within the panel on phones.</p><div class="pan" tabindex="0" aria-label="Full resolution crew line-up"><img src="lineup-tv.png" width="1920" height="1080" alt="All eight crew at exact TV scale on sample compartments, in colour, greyscale and echo"></div><p><a href="portraits-tv.png">Neutral and hurt portraits at TV scale</a> · <a href="cards-tv.png">Crew cards and echoes at TV scale</a> · <a href="facings-review.png">All facings and walk phases</a> · <a href="crew-reference.png">Enlarged silhouette reference</a> · <a href="portraits-contact.png">Portrait contact sheet</a></p>'''+''.join(articles)+'''<script>
const pause=document.querySelector('#pause'),still=document.querySelector('#still');
pause.onclick=()=>{document.body.classList.toggle('paused');pause.textContent=document.body.classList.contains('paused')?'Play motion':'Pause motion'};
const preference=matchMedia('(prefers-reduced-motion:reduce)');
function motion(){document.body.classList.toggle('still',still.checked);pause.disabled=still.checked||preference.matches;document.querySelectorAll('#replay,.replay-pose').forEach(b=>b.disabled=still.checked||preference.matches);}
still.checked=preference.matches;motion();still.onchange=motion;preference.addEventListener('change',e=>{if(e.matches)still.checked=true;motion();});
document.querySelector('#variant').onchange=e=>document.querySelectorAll('[data-live]').forEach(el=>{const url=el.dataset[e.target.value];if(el.tagName==='IMG')el.src=url;else el.style.backgroundImage='url('+url+')';});
document.querySelector('#portrait-size').onchange=e=>document.querySelectorAll('.portraits img').forEach(el=>{el.width=el.naturalWidth*Number(e.target.value);el.height=el.naturalHeight*Number(e.target.value);});
function replay(el){el.style.animation='none';void el.offsetWidth;el.style.animation='';}
document.querySelector('#replay').onclick=()=>document.querySelectorAll('.once').forEach(replay);
document.querySelectorAll('.replay-pose').forEach(b=>b.onclick=()=>replay(b.closest('figure').querySelector('.once')));
</script></body></html>'''
    (review/'index.html').write_text(html)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=ROOT);parser.add_argument('--rules',type=Path,default=REFERENCE)
    args=parser.parse_args();build(args.root,args.rules)
