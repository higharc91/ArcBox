"""Complete original compartment set; explicit room compositions, no placeholder fallback."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import build as style

ROOT = style.ROOT
REFERENCE = Path(__file__).parent/'reference/tiles.json'
RULES_HEAD = 'ff3175af859733bf8b02c0ff2afa43d0113e1c66'
DECKS = ['command', 'habitat', 'engineering']
DIRECTIONS = ['n', 'w', 's', 'e']  # PIL counterclockwise rotations


def rules(path=REFERENCE):
    data = json.loads(path.read_text())
    return data['startTiles']+data['tiles']


def base(deck):
    c = style.canvas(64, 64)
    for inset, material, shade in [(0,'hull',1),(2,'hull',4),(4,'hull',0),(7,'floor',2)]:
        c.rect(inset,inset,63-inset,63-inset,material,shade)
    for x in [8,32]:
        for y in [8,32]:
            c.line(x+2,y+2,x+20,y+2,'floor',3)
            c.line(x+22,y,x+22,y+22,'floor',1)
    for x,y in [(5,5),(53,5),(5,53),(53,53)]:
        c.rect(x,y,x+5,y+5,'hull',1)
        c.rect(x+1,y+1,x+4,y+4,'hull',3)
        c.rect(x+2,y+2,x+3,y+3,'hull',4)
    return c


def box(c,x,y,w,h,material='hull'):
    c.rect(x,y,x+w-1,y+h-1,material,0)
    c.rect(x+1,y+1,x+w-2,y+h-2,material,3)
    if w>5 and h>5:
        c.line(x+2,y+2,x+w-3,y+2,material,4)
        c.line(x+2,y+h-3,x+w-3,y+h-3,material,2)


def screen(c,x,y,w=12,h=8,material='command'):
    box(c,x,y,w,h)
    c.rect(x+2,y+2,x+w-3,y+h-3,material,0)
    c.rect(x+3,y+3,x+w-4,y+4,material,4)
    if h>=9: c.line(x+3,y+5,x+w-6,y+5,material,3)
    c.px(x+w-2,y+h-2,'warning',4)


def bed(c,x,y,cryo=False):
    # Broad pillow/blanket/window clusters remain readable at 128px per room.
    box(c,x,y,16,32)
    c.rect(x+2,y+3,x+13,y+28,'linen',3)
    c.rect(x+3,y+3,x+12,y+8,'linen',5)
    c.line(x+3,y+9,x+12,y+9,'linen',2)
    c.rect(x+2,y+14,x+13,y+25,'coolant',3)
    c.rect(x+3,y+14,x+12,y+16,'coolant',4)
    for dy in [12,26]:
        c.rect(x,y+dy,x+1,y+dy+3,'hull',5)
        c.rect(x+14,y+dy,x+15,y+dy+3,'hull',5)
    if cryo:
        c.rect(x+2,y+2,x+13,y+28,'coolant',1)
        c.rect(x+4,y+5,x+11,y+8,'coolant',4)
        c.rect(x+4,y+12,x+6,y+24,'coolant',3)
        c.rect(x+6,y+29,x+10,y+30,'warning',4)


def table(c,x,y,w=22,h=12):
    box(c,x,y,w,h)
    c.rect(x+2,y+2,x+w-3,y+h-3,'linen',2)
    for dx in [3,w-7]:
        box(c,x+dx,y-5,4,4,'suit')
        box(c,x+dx,y+h+1,4,4,'suit')


def rack(c,x,y,w=13,h=28,material='hull'):
    box(c,x,y,w,h)
    for dy in range(4,h-3,6):
        c.line(x+2,y+dy,x+w-3,y+dy,'hull',0)
        c.rect(x+3,y+dy-2,x+w-4,y+dy-1,material,3)
        c.px(x+w-3,y+dy-1,'warning',4)


def tank(c,x,y,rx=7,ry=12,material='coolant'):
    c.ellipse(x,y,rx,ry,'hull',0)
    c.ellipse(x,y,rx-1,ry-1,'hull',4)
    c.ellipse(x,y,rx-3,ry-3,material,2)
    c.rect(x-2,y-ry+5,x+1,y+ry-5,material,4)
    c.rect(x-2,y-ry-2,x+2,y-ry,'hull',2)
    c.rect(x-2,y+ry,x+2,y+ry+2,'hull',2)


def duct(c,x,y,w,h):
    box(c,x,y,w,h)
    for dx in range(3,w-2,3): c.line(x+dx,y+2,x+dx,y+h-3,'hull',0)


def crate(c,x,y,w=12,h=12):
    box(c,x,y,w,h,'engineering')
    c.line(x+2,y+2,x+w-3,y+h-3,'engineering',1)
    c.line(x+w-3,y+2,x+2,y+h-3,'engineering',1)
    c.rect(x+w//2-1,y+2,x+w//2+1,y+3,'linen',4)


def chair(c,x,y):
    box(c,x,y,8,9,'suit')
    c.line(x+1,y+1,x+6,y+1,'suit',4)
    c.line(x-1,y+3,x-1,y+7,'hull',3)
    c.line(x+8,y+3,x+8,y+7,'hull',3)


def draw_room(c,key,deck):
    # Related spaces share manufactured equipment, but each has a deliberately authored layout.
    if key=='airlock':
        c.polygon([(16,10),(47,10),(54,17),(54,40),(47,47),(16,47),(9,40),(9,17)],'hull',0)
        box(c,13,14,38,30)
        c.rect(17,17,46,40,'hull',4)
        c.rect(30,17,33,40,'hull',0)
        for x in [20,37]:
            box(c,x,20,8,12); c.rect(x+2,22,x+5,29,'coolant',3)
            c.rect(x,35,x+7,37,'hull',1)
        for x in [18,27,36,45]: c.thick_line(x,11,x+3,14,2,'warning',4)
    elif key=='bridge':
        c.polygon([(9,11),(54,11),(54,34),(43,34),(43,25),(20,25),(20,34),(9,34)],'hull',0)
        screen(c,11,12,42,13)
        screen(c,10,25,10,10); screen(c,44,25,10,10)
        c.ellipse(32,38,8,4,'hull',0)
        box(c,26,29,13,13,'suit')
        c.rect(28,30,36,33,'suit',4)
        c.rect(24,34,26,40,'hull',4); c.rect(38,34,40,40,'hull',4)
        c.line(17,17,24,20,'linen',4); c.line(24,20,30,16,'linen',4)
    elif key=='med_bay':
        bed(c,10,13); bed(c,39,13)
        box(c,28,9,9,13,'linen')
        c.rect(31,11,33,19,'red',3); c.rect(29,14,35,16,'red',3)
        screen(c,28,27,9,13,'coolant')
    elif key=='reactor_core':
        for radius,mat,shade in [(22,'hull',0),(20,'hull',4),(17,'coolant',1),(15,'coolant',3),
                                 (12,'hull',0),(9,'anomaly',2),(5,'anomaly',5)]:
            c.ellipse(32,29,radius,radius,mat,shade)
        for x,y,w,h in [(28,8,8,8),(28,42,8,5),(10,25,8,8),(47,25,8,8)]:
            box(c,x,y,w,h); c.rect(x+2,y+2,x+w-3,y+h-3,'warning',4)
    elif key in ['central_lift','command_landing','engineering_landing']:
        box(c,13,11,38,35)
        c.rect(16,14,47,42,'floor',1)
        c.rect(19,17,44,39,'hull',2)
        for y in [21,27,33,39]: c.line(20,y,43,y,'hull',3)
        screen(c,48,18,7,18,deck)
        if key=='central_lift':
            c.polygon([(21,27),(26,20),(31,27)],'linen',4); c.polygon([(33,28),(43,28),(38,35)],'linen',4)
        elif key=='command_landing':
            c.polygon([(22,32),(32,19),(42,32)],deck,4)
        else: c.polygon([(22,22),(42,22),(32,35)],deck,4)
    elif key=='docking_ring':
        for radius,mat,shade in [(20,'hull',0),(18,'hull',3),(14,'warning',2),(12,'floor',1)]:
            c.ellipse(32,30,radius,radius,mat,shade)
        for x,y in [(11,26),(45,26),(28,10),(28,44)]: box(c,x,y,8,6)
        c.line(26,29,37,29,'hull',3)
    elif key=='captains_quarters':
        bed(c,10,12); screen(c,34,12,21,15); chair(c,40,32)
        box(c,27,40,17,7,'engineering'); c.rect(29,41,32,44,'linen',4)
    elif key in ['navigation','chart_room']:
        box(c,14,18,36,27)
        c.rect(17,21,46,41,'command',0)
        for x,y in [(22,26),(32,35),(42,25)]:
            c.rect(x-2,y-2,x+2,y+2,'command',4)
        c.line(22,26,32,35,'command',2); c.line(32,35,42,25,'command',2)
        if key=='navigation': screen(c,10,9,44,10)
        else: rack(c,10,13,6,30,'linen'); rack(c,48,13,6,30,'linen')
    elif key in ['comms_array','sensor_suite']:
        for x in [11,39]: screen(c,x,10,15,12)
        c.ellipse(32,32,19,13,'hull',0); c.ellipse(32,32,17,11,'command',1)
        c.ellipse(32,32,12,8,'command',2)
        if key=='comms_array':
            c.thick_line(21,40,44,24,3,'command',4); c.ellipse(32,32,4,4,'linen',4)
        else:
            c.line(15,32,49,32,'command',4); c.line(32,21,32,43,'command',4)
            c.rect(37,25,41,28,'anomaly',5)
    elif key=='observation_dome':
        c.ellipse(32,24,24,15,'hull',0); c.ellipse(32,24,22,13,'hull',4)
        c.ellipse(32,24,19,10,'command',0)
        for x,y in [(23,20),(35,17),(41,24),(29,26)]: c.px(x,y,'linen',4)
        c.line(16,22,48,22,'hull',2); c.line(32,14,32,30,'hull',2)
        chair(c,20,37); chair(c,37,37)
    elif key in ['officers_mess','mess_hall','briefing_room']:
        if key=='briefing_room': screen(c,15,11,34,10); table(c,13,27,38,16)
        elif key=='officers_mess':
            table(c,10,18,21,18); table(c,34,25,21,18)
            c.rect(18,23,21,25,'linen',5); c.rect(41,35,44,37,'linen',5)
        else:
            table(c,15,17,34,10); table(c,15,37,34,10)
            c.rect(22,20,25,22,'coolant',3); c.rect(39,40,42,42,'linen',4)
    elif key=='armory':
        for x in [10,38]:
            rack(c,x,10,16,35)
            for y in [17,29]:
                c.rect(x+4,y,x+6,y+10,'hull',5); c.rect(x+7,y+3,x+11,y+6,'hull',1)
        crate(c,28,29,8,14)
    elif key=='escape_pod_bay':
        for x in [18,44]:
            tank(c,x,28,10,17,'coolant'); c.rect(x-5,20,x+5,32,'hull',0)
            c.rect(x-3,22,x+3,30,'command',4)
        screen(c,27,13,10,9)
    elif key=='galley':
        box(c,11,12,42,12); box(c,10,24,16,20)
        for x in [16,30]:
            c.ellipse(x,18,5,5,'hull',0); c.ellipse(x,18,2,2,'hull',4)
        c.rect(44,15,49,20,'coolant',1); table(c,29,34,21,9)
    elif key=='crew_bunks':
        for x in [10,39]: bed(c,x,12)
        rack(c,27,12,10,12,'linen'); box(c,28,37,9,8,'engineering')
    elif key=='quiet_room':
        box(c,10,13,14,14,'suit'); box(c,40,13,14,14,'suit')
        c.ellipse(32,34,19,11,'habitat',1); c.ellipse(32,34,15,8,'habitat',2)
        box(c,29,31,6,6,'engineering'); c.ellipse(31,33,1,1,'linen',4)
    elif key=='hydroponics':
        for x in [10,37]:
            box(c,x,10,18,35)
            for y in [19,30,40]:
                c.line(x+3,y,x+10,y,'coolant',3)
                c.ellipse(x+8,y-2,6,4,'anomaly',3)
                c.line(x+8,y-6,x+8,y,'anomaly',5)
    elif key=='laundry':
        for x in [11,35]:
            box(c,x,11,19,24,'linen'); c.ellipse(x+9,24,7,8,'hull',0)
            c.ellipse(x+9,24,5,6,'coolant',3)
        box(c,18,37,26,9); c.rect(22,39,29,42,'linen',4); c.rect(33,39,39,42,'habitat',2)
    elif key=='gymnasium':
        for x in [10,37]:
            box(c,x,11,18,33); c.rect(x+3,19,x+14,38,'suit',1)
            c.rect(x+2,13,x+15,16,'hull',5)
        c.line(27,31,36,31,'hull',4)
        c.rect(25,27,27,35,'hull',0); c.rect(36,27,38,35,'hull',0)
    elif key=='chapel':
        c.ellipse(32,21,12,12,'hull',0); c.ellipse(32,21,10,10,'anomaly',2)
        c.rect(26,19,38,21,'linen',4); c.rect(31,15,33,27,'linen',4)
        for x in [13,39]:
            for y in [31,41]: box(c,x,y,12,5,'engineering')
    elif key=='cryo_bay':
        for x in [11,38]: bed(c,x,11,True)
        c.line(13,45,51,45,'coolant',2)
    elif key=='suit_lockers':
        for x in [10,34]:
            box(c,x,10,20,35)
            c.ellipse(x+10,18,5,4,'coolant',3)
            c.rect(x+5,24,x+14,34,'suit',4)
            c.rect(x+5,35,x+8,41,'suit',2); c.rect(x+11,35,x+14,41,'suit',2)
    elif key=='recreation_deck':
        table(c,10,20,25,20); c.line(16,24,27,31,'habitat',3)
        screen(c,39,11,15,21); chair(c,42,35)
    elif key=='isolation_ward':
        bed(c,25,12)
        for x in [20,45]: c.line(x,12,x,45,'coolant',2)
        c.line(20,12,42,12,'coolant',4)
        screen(c,9,14,10,18,'coolant'); c.line(22,44,40,44,'warning',4)
    elif key=='archive':
        for x in [10,36]: rack(c,x,10,18,35,'linen')
    elif key=='engine_room':
        for y in [21,39]:
            box(c,12,y-7,40,13)
            for x in [22,43]:
                c.ellipse(x,y,6,6,'hull',0); c.ellipse(x,y,4,4,'engineering',3)
                c.rect(x-1,y-3,x+1,y+3,'engineering',4)
        c.line(12,30,52,30,'coolant',2)
    elif key=='coolant_pumps':
        for x in [19,45]: tank(c,x,28,10,16)
        c.line(18,14,45,14,'coolant',3); c.line(18,46,45,46,'coolant',3)
        box(c,28,25,9,13); c.ellipse(32,31,2,2,'warning',4)
    elif key=='machine_shop':
        box(c,12,13,40,11)
        c.line(18,18,34,18,'hull',0); c.rect(20,16,23,20,'hull',5)
        box(c,11,29,25,15); c.line(22,28,22,40,'hull',4)
        rack(c,40,26,14,18,'engineering')
    elif key in ['cargo_hold','storage_closet']:
        placements=[(10,10),(35,10),(10,31),(35,31)] if key=='cargo_hold' else [(11,11),(34,11),(23,30)]
        for x,y in placements: crate(c,x,y,19,15)
        if key=='cargo_hold': c.line(27,38,35,46,'warning',3)
    elif key=='waste_reclamation':
        tank(c,18,28,10,16,'anomaly'); tank(c,46,28,10,16,'anomaly')
        duct(c,28,14,8,28); c.line(14,45,49,45,'warning',3)
    elif key=='power_relay':
        for x in [9,39]: rack(c,x,10,16,35,'coolant')
        c.line(25,17,37,17,'warning',3); c.line(25,41,37,41,'warning',3)
        c.polygon([(33,23),(29,31),(33,31),(30,38),(37,28),(33,28)],'warning',4)
    elif key=='drone_bay':
        for x,y in [(22,22),(43,35)]:
            c.ellipse(x,y,11,9,'hull',0); c.ellipse(x,y,9,7,'hull',4)
            c.rect(x-3,y-1,x+3,y+1,'coolant',4)
            for dx in [-10,8]: c.rect(x+dx,y-2,x+dx+2,y+2,'hull',1)
        screen(c,38,12,13,10)
    elif key=='crawlspace':
        c.rect(17,10,46,46,'hull',0); c.rect(21,10,42,46,'floor',1)
        for y in [14,23,32,41]:
            c.rect(18,y,45,y+2,'hull',2); c.rect(22,y+1,41,y+2,'hull',4)
        c.line(24,12,24,44,'coolant',2)
    elif key=='fuel_tanks':
        for x in [19,45]: tank(c,x,28,10,17,'warning')
        c.line(15,12,49,12,'hull',3); c.line(15,46,49,46,'hull',3)
    elif key=='chiefs_office':
        screen(c,10,10,32,18,'engineering'); chair(c,22,32)
        rack(c,45,10,10,34,'warning'); c.rect(13,40,29,45,'linen',2)
        c.line(16,42,24,42,'command',3)
    elif key=='containment_lab':
        box(c,10,11,39,34)
        c.rect(13,14,46,42,'coolant',1)
        for x in [21,42]: c.line(x,18,x,40,'coolant',3)
        c.ellipse(30,28,10,12,'hull',0); c.ellipse(30,28,6,8,'anomaly',4)
        screen(c,47,25,7,12,'anomaly')
    elif key in ['service_corridor','junction']:
        c.rect(24,7,39,56,'hull',1); c.rect(27,7,36,56,'floor',3)
        if key=='junction':
            c.rect(7,24,56,39,'hull',1); c.rect(7,27,56,36,'floor',3)
            c.ellipse(32,32,7,7,'hull',2); c.ellipse(32,32,4,4,deck,2)
        else:
            duct(c,11,12,10,32); c.line(44,12,44,44,'coolant',2)
        for y in [15,43]: c.rect(29,y,34,y+1,'linen',3)
    elif key=='maintenance_shaft':
        box(c,17,12,30,34)
        c.rect(20,15,43,43,'floor',0)
        c.rect(24,19,39,39,'hull',0)
        for x in [28,36]: c.line(x,17,x,42,'hull',3)
        for y in range(20,42,5): c.rect(28,y,36,y+1,'hull',4)
        for x in [18,42]:
            for y in [16,25,34]: c.line(x,y,x+3,y+3,'warning',4)
    elif key=='vent_hub':
        duct(c,9,9,46,12); duct(c,9,35,46,10)
        c.ellipse(32,28,14,14,'hull',0); c.ellipse(32,28,12,12,'hull',4)
        for points in [[(30,27),(28,17),(36,18),(34,27)],[(33,26),(44,24),(42,32),(33,30)],[(34,29),(37,39),(29,38),(30,29)],[(31,30),(20,32),(22,24),(31,26)]]:
            c.polygon(points,'hull',1)
        c.ellipse(32,28,3,3,'hull',5)
    elif key=='sealed_alcove':
        box(c,16,13,32,31)
        c.rect(20,17,43,40,'hull',1)
        c.thick_line(21,18,42,39,3,'warning',3); c.thick_line(42,18,21,39,3,'warning',3)
        c.rect(29,25,34,32,'anomaly',2)
    else:
        raise ValueError('No authored composition for '+key)
    return c.render(style.PALETTE)


def symbol(kind):
    c=style.canvas(11,11)
    if kind=='incident':
        c.polygon([(5,0),(10,10),(0,10)],'warning',4)
        c.rect(4,3,6,6,'hull',0); c.rect(4,8,6,9,'hull',0)
    elif kind=='salvage':
        c.rect(0,3,10,9,'linen',4); c.rect(3,0,7,1,'linen',4)
        c.line(2,1,2,3,'linen',4); c.line(8,1,8,3,'linen',4)
        c.line(0,6,10,6,'hull',0); c.rect(4,5,6,7,'linen',5)
    elif kind=='anomaly':
        c.polygon([(5,0),(10,5),(5,10),(0,5)],'anomaly',4)
        c.polygon([(5,2),(8,5),(5,8),(2,5)],'hull',0)
        c.rect(4,4,6,6,'anomaly',5)
    else: raise ValueError(kind)
    return c.render(style.PALETTE)


def tile(rule,deck):
    image=draw_room(base(deck),rule['id'],deck)
    # Consistent low-interference legend zone, separate from equipment and central crew space.
    c=style.canvas(64,64)
    c.rect(8,46,55,56,'floor',2)
    for i in range(DECKS.index(deck)+1): c.rect(11+i*4,51,12+i*4,54,deck,4)
    image.alpha_composite(c.render(style.PALETTE))
    if rule.get('symbol'): image.alpha_composite(symbol(rule['symbol']),(27,46))
    # All potential passages remain transparent; rules choose wall/door overlays at composition time.
    for i in range(24,40):
        for j in range(7):
            for x,y in [(i,j),(i,63-j),(j,i),(63-j,i)]: image.putpixel((x,y),(0,0,0,0))
    return image


def compose(image,rule,open_state='open',turn=0):
    result=image.copy()
    for direction in DIRECTIONS:
        state=open_state if direction in rule['hatches'] else 'none'
        result.alpha_composite(style.hatch(state).rotate(90*DIRECTIONS.index(direction)))
    return result.rotate(90*turn)


def one_way():
    im=style.hatch('open'); c=style.canvas(64,64)
    c.polygon([(29,1),(34,1),(34,3),(37,3),(32,7),(27,3),(29,3)],'warning',4)
    im.alpha_composite(c.render(style.PALETTE)); return im


def hatch_frames(kind,reduced=False):
    frames=[]
    total=6 if kind=='opening' else 8
    for frame in range(total):
        c=style.canvas(64,64)
        if kind=='opening':
            distance=round(frame*6/(total-1))
            for x0,x1 in [(26,31-distance),(32+distance,37)]:
                if x0<=x1:
                    c.rect(x0,0,x1,6,'hull',2)
                    c.line(x0,2,x1,2,'hull',4)
            image=style.hatch('open'); image.alpha_composite(c.render(style.PALETTE))
            if frame==0: image=style.hatch('closed')
            if frame==total-1: image=style.hatch('open')
        else:
            image=style.hatch('closed')
            # Continuous weld progression, no flashing background or screen shake.
            path=[(26+i,round(i*6/11)) for i in range(12)]+[(37-i,round(i*6/11)) for i in range(12)]
            for x,y in path[:round(frame*len(path)/(total-1))]:
                c.px(x,y,'warning',4)
            if not reduced and frame<total-1:
                x,y=path[min(len(path)-1,round((frame+1)*len(path)/total)-1)]
                c.px(x+1,min(7,y+1),'warning',5)
                c.px(x-1,min(7,y+2),'warning',3)
            image.alpha_composite(c.render(style.PALETTE))
            if frame==0: image=style.hatch('closed')
            if frame==total-1: image=style.hatch('sealed')
        frames.append(image)
    return frames


def strip(frames):
    result=Image.new('RGBA',(frames[0].width*len(frames),frames[0].height))
    for i,frame in enumerate(frames): result.alpha_composite(frame,(i*frame.width,0))
    return result


def background(deck,unexplored=False):
    c=style.canvas(64,64)
    c.rect(0,0,63,63,'floor',0)
    if not unexplored:
        for inset in [1,33]:
            c.line(inset,1,inset,62,'hull',0); c.line(1,inset,62,inset,'hull',0)
        c.rect(4,4,5,5,deck,0); c.rect(58,58,59,59,deck,0)
    else:
        for x,y in [(28,30),(32,30),(36,30)]: c.rect(x,y,x+1,y+1,'hull',1)
    return c.render(style.PALETTE)


def build(root=ROOT,reference=REFERENCE):
    all_rules=rules(reference)
    pack=root/'content/meridian/base'; review=root/'docs/superpowers/reports/meridian-art/tiles'
    review.mkdir(parents=True,exist_ok=True)
    common={'version':2,'status':'awaiting-set-review','issue':332,
            'rulesReference':{'pr':316,'head':RULES_HEAD,'sha256':hashlib.sha256(reference.read_bytes()).hexdigest()},
            'styleApproval':'Owner approved refined #331 samples in conversation on 2026-11-17; continued quality refinement and appropriate animation requested.'}
    entries={}
    for rule in all_rules:
        variants={deck:style.immutable(pack,'tiles',rule['id']+'-'+deck,tile(rule,deck)) for deck in rule['decks']}
        entries[rule['id']]={'name':rule['name'],'frameSize':[64,64],'variants':variants,
                              'hatches':rule['hatches'],'symbol':rule.get('symbol'),'rule':rule.get('rule'),
                              'rotationDegrees':[0,90,180,270],'doorway':[24,39]}
    backgrounds={deck:style.immutable(pack,'tiles','background-'+deck,background(deck)) for deck in DECKS}
    unknown=style.immutable(pack,'tiles','unexplored',background('habitat',True))
    states={state:{**style.immutable(pack,'hatches',state,style.hatch(state)), 'frameSize':[64,64], 'edge':'north','edgeBounds':[23,0,41,8]} for state in style.STATES}
    states['one_way']={**style.immutable(pack,'hatches','one-way',one_way()),'frameSize':[64,64],'edge':'north','edgeBounds':[23,0,41,8]}
    animations={}
    for kind in ['opening','sealing']:
        animations[kind]={}
        for mode in ['full','reduced']:
            frames=hatch_frames(kind,mode=='reduced')
            animations[kind][mode]={**style.immutable(pack,'hatches',kind+'-'+mode,strip(frames)),
                'frameSize':[64,64],'frames':len(frames),'fps':10,'seconds':len(frames)/10,'loop':False,'blend':'alpha',
                'restState':'open' if kind=='opening' else 'sealed','reducedMotion':'show restState without animation'}
    style.write_json(pack/'tiles/manifest.json',{**common,'tiles':entries,'backgrounds':backgrounds,'unexplored':unknown})
    style.write_json(pack/'hatches/manifest.json',{**common,'states':states,'animations':animations})
    review_page(review,pack,all_rules,entries,animations)
    return pack,review


def review_page(review,pack,all_rules,entries,animations):
    base_url='../../../../../content/meridian/base/'
    cards=[]; contact=Image.new('RGBA',(960, max(1,(sum(len(x['decks']) for x in all_rules)+5)//6)*104+40),style.INK)
    draw=ImageDraw.Draw(contact); draw.fontmode='1'; font=ImageFont.load_default()
    draw.text((12,10),'MERIDIAN / ALL COMPARTMENTS / EXACT TV 2x AFTER EXPORT',font=font,fill=style.PAPER)
    index=0
    for rule in all_rules:
        for deck,entry in entries[rule['id']]['variants'].items():
            image=compose(Image.open(pack/'tiles'/entry['file']),rule)
            key=rule['id']+'-'+deck
            views=[]
            # All quarter turns at source and TV scale, with legal wall/door composition.
            for turn in range(4):
                rotated=image.rotate(90*turn)
                name=f'{key}-{turn*90}.png'; rotated.save(review/name)
                views.append(f'<div><span>{turn*90}°</span><img alt="{rule["name"]}, {deck}, {turn*90} degrees, source" src="{name}" width="64" height="64"><img alt="{rule["name"]} at TV scale" src="{name}" width="128" height="128"></div>')
            cards.append(f'<article data-deck="{deck}"><h3>{rule["name"]}</h3><p>{deck} · {rule.get("symbol","no card draw")} · hatches {", ".join(rule["hatches"])}</p><div class="rotations">'+''.join(views)+'</div></article>')
            x=12+(index%6)*158; y=40+(index//6)*104
            contact.alpha_composite(image,(x,y))
            draw.text((x,y+67),rule['name'][:24],font=font,fill=style.PAPER)
            draw.text((x,y+80),deck,font=font,fill=style.PAPER)
            index+=1
    readability_sheets(review,all_rules)
    contact.resize((contact.width*2,contact.height*2),Image.Resampling.NEAREST).convert('RGB').save(review/'all-compartments-tv.png')
    # Half-explored review boards: chosen samples, no claim of a legal random deal.
    for deck in DECKS:
        mock=Image.new('RGBA',(960,540),style.INK); d=ImageDraw.Draw(mock); d.fontmode='1'
        d.text((24,20),'DERELICT: ISV MERIDIAN / '+deck.upper(),fill=style.PAPER,font=font)
        d.text((24,42),'COMPARTMENT ART REVIEW / HALF EXPLORED / NOT GAMEPLAY',fill=style.PAPER,font=font)
        choices=[r for r in all_rules if deck in r['decks']]
        for gy in range(5):
            for gx in range(8):
                i=gy*8+gx; x=48+gx*64; y=100+gy*64
                if i<20:
                    rule=choices[i%len(choices)]; im=tile(rule,deck)
                    mock.alpha_composite(compose(im,rule),(x,y))
                else: mock.alpha_composite(background(deck,True),(x,y))
        for i,(x,y) in enumerate([(124,184),(252,248)]):
            d.ellipse((x-2,y+12,x+22,y+30),outline=style.PLAYERS[i],width=2)
            mock.alpha_composite(style.crew(),(x,y))
            d.text((x+6,y+33),str(i+1),font=font,fill=style.PAPER)
        d.text((620,130),'DECK '+str(DECKS.index(deck)+1),font=font,fill=style.PAPER)
        d.text((620,158),'TRIANGLE: INCIDENT',font=font,fill=style.PAPER)
        d.text((620,182),'CASE: SALVAGE',font=font,fill=style.PAPER)
        d.text((620,206),'DIAMOND: ANOMALY',font=font,fill=style.PAPER)
        focus=next(r for r in choices if r['id']=={'command':'navigation','habitat':'med_bay','engineering':'reactor_core'}[deck])
        mock.alpha_composite(compose(tile(focus,deck),focus).resize((128,128),Image.Resampling.NEAREST),(620,250))
        d.text((620,390),focus['name'].upper(),font=font,fill=style.PAPER)
        d.text((620,410),'FOCUS DETAIL / 4x',font=font,fill=style.PAPER)
        mock.resize((1920,1080),Image.Resampling.NEAREST).convert('RGB').save(review/f'{deck}-mock.png')
    static_hatches=[]
    for state in style.STATES+['one_way']:
        im=one_way() if state=='one_way' else style.hatch(state)
        name='hatch-'+state+'.png'; im.save(review/name)
        static_hatches.append(f'<figure><figcaption>{state.replace("_"," ")}</figcaption><div class="demo" style="background-image:url({base_url}tiles/{entries["airlock"]["variants"]["habitat"]["file"]})"><img src="{name}" alt="{state} north edge overlay" width="128" height="128"></div></figure>')
    demos=[]
    tile_url=base_url+'tiles/'+entries['airlock']['variants']['habitat']['file']
    for kind,modes in animations.items():
        for mode,entry in modes.items():
            n=entry['frames']; image_url=base_url+'hatches/'+entry['file']
            demos.append(f'<figure><figcaption>{kind} · {mode} · {entry["seconds"]}s</figcaption><div class="demo" style="background-image:url({tile_url})"><div class="clip" data-frames="{n}" style="--last:{-128*(n-1)}px;--duration:{(n-1)/10}s;--steps:{n-1};background-image:url({image_url});background-size:{n*128}px 128px"></div></div></figure>')
    # Animated user-facing preview, integer scale, no interpolated pixels.
    for kind in ['opening','sealing']:
        bg=tile(next(r for r in all_rules if r['id']=='airlock'),'habitat')
        frames=[]
        for overlay in hatch_frames(kind):
            im=Image.new('RGBA',(64,64),style.INK); im.alpha_composite(bg); im.alpha_composite(overlay)
            frames.append(im.resize((256,256),Image.Resampling.NEAREST).convert('RGB'))
        frames[0].save(review/f'{kind}-preview.gif',save_all=True,append_images=frames[1:]+[frames[-1]]*10,duration=100,loop=0,disposal=2)
    html='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian compartments</title><style>
:root{color-scheme:dark;font:17px/1.5 system-ui;background:#111b2a;color:#d8e1da}*{box-sizing:border-box}body{margin:auto;padding:16px;max-width:1280px}a{color:#9ee0ed}h1{line-height:1.1}button,select{font:inherit;min-height:44px;padding:8px;background:#26384b;color:#d8e1da;border:1px solid #657482}article,figure{border:1px solid #657482;padding:12px;margin:16px 0}img,.demo,.clip{image-rendering:pixelated}article img{display:inline-block;margin:6px;vertical-align:bottom}.rotations{display:flex;gap:16px;flex-wrap:wrap}.rotations>div{width:224px}.rotations span{display:block}.demos{display:flex;gap:16px;flex-wrap:wrap}.demo{width:128px;height:128px;background-size:128px 128px}.clip{width:128px;height:128px;animation:play var(--duration) steps(var(--steps)) infinite alternate}@keyframes play{from{background-position:0 0}to{background-position:var(--last) 0}}body.paused .clip{animation-play-state:paused}body.still .clip{animation:none;background-position:var(--last) 0}.pan{max-height:75vh;overflow:auto}.pan img{display:block;max-width:none}@media(prefers-reduced-motion:reduce){.clip{animation:none;background-position:var(--last) 0}}[hidden]{display:none!important}
</style><body><h1>Meridian / compartment set</h1><p>48 unique room IDs · 50 physical tiles · 57 deck variants. Original deterministic pixel art, approved style continued after owner feedback. Rules from unmerged PR #316 at ff3175af; integration remains pending.</p><p>All four rotations at 1× and exact 2× TV scale. Hatches compose from each room's legal edges; bases retain transparent door gaps. Symbols use distinct shapes as well as colour. Decks use I / II / III bars.</p><p><a href="all-compartments-tv.png">Complete TV-scale sheet</a></p><h2>Hatch states</h2><div class="demos">'''+''.join(static_hatches)+'''</div><h2>Hatch motion</h2><p>Runtime clips are one-shot, 0.6s opening / 0.8s sealing. Preview repeats forward/back to expose every frame. Reduced-effects welds omit sparks; reduced-motion shows the final state. No screen shake or area flashing.</p><button id="pause" type="button">Pause previews</button> <label><input id="still" type="checkbox"> Still previews</label><div class="demos">'''+''.join(demos)+'''</div><h2>Every compartment</h2><label>Deck <select id="deck"><option value="all">All decks</option><option>command</option><option>habitat</option><option>engineering</option></select></label>'''+''.join(cards)+'''<h2>Half-explored deck mocks / native 1920×1080</h2><p>Concept boards for art review, not a legal tile draw or an implemented TV scene. Pan within each panel on phones.</p>'''+''.join(f'<h3>{deck.title()}</h3><div class="pan" tabindex="0" aria-label="{deck} full resolution mock"><img src="{deck}-mock.png" width="1920" height="1080" alt="Half-explored {deck} deck with room samples and two numbered crew"></div>' for deck in DECKS)+'''<script>
const pause=document.querySelector('#pause'),still=document.querySelector('#still');
pause.onclick=()=>{document.body.classList.toggle('paused');pause.textContent=document.body.classList.contains('paused')?'Play previews':'Pause previews'};
still.checked=matchMedia('(prefers-reduced-motion:reduce)').matches;document.body.classList.toggle('still',still.checked);
still.onchange=()=>document.body.classList.toggle('still',still.checked);
document.querySelector('#deck').onchange=e=>document.querySelectorAll('article').forEach(a=>a.hidden=e.target.value!=='all'&&a.dataset.deck!==e.target.value);
</script></body></html>'''
    (review/'index.html').write_text(html)


def readability_sheets(review,all_rules):
    from PIL import ImageOps
    # All 48 room IDs, twelve per native 1080p page, at exact 128px grid scale.
    for page in range(4):
        board=Image.new('RGBA',(960,540),style.INK)
        d=ImageDraw.Draw(board);d.fontmode='1';font=ImageFont.load_default()
        d.text((16,12),'TV READABILITY / 128px TILE / '+str(page+1)+' OF 4',font=font,fill=style.PAPER)
        for i,rule in enumerate(all_rules[page*12:(page+1)*12]):
            deck=rule['decks'][0]; im=compose(tile(rule,deck),rule)
            x=16+(i%4)*236; y=50+(i//4)*160
            board.alpha_composite(im,(x,y))
            covered=im.copy();covered.alpha_composite(style.crew(),(20,20))
            grey=ImageOps.grayscale(covered).convert('RGBA')
            board.alpha_composite(grey,(x+88,y))
            d.text((x,y+74),rule['name'][:27],font=font,fill=style.PAPER)
            d.text((x,y+91),'COLOUR / GREY + CREW',font=font,fill=style.PAPER)
        native=board.resize((1920,1080),Image.Resampling.NEAREST).convert('RGB')
        native.save(review/f'tv-readability-{page+1}.png')
    # Compare prior approved room samples to this TV-focused composition pass.
    comparison=Image.new('RGBA',(400,330),style.INK)
    d=ImageDraw.Draw(comparison);d.fontmode='1';font=ImageFont.load_default()
    d.text((10,8),'TV ROOM PASS / BEFORE -> LARGER FEATURES',font=font,fill=style.PAPER)
    for i,key in enumerate(['bridge','med_bay','reactor_core','airlock']):
        x=10+(i%2)*195;y=40+(i//2)*140;rule=next(r for r in all_rules if r['id']==key)
        comparison.alpha_composite(style.room(key),(x,y))
        comparison.alpha_composite(tile(rule,rule['decks'][0]),(x+90,y))
        d.text((x,y+72),rule['name'],font=font,fill=style.PAPER)
        d.text((x,y+89),'BEFORE        TV PASS',font=font,fill=style.PAPER)
    comparison.resize((800,660),Image.Resampling.NEAREST).convert('RGB').save(review/'room-tv-comparison.png')
    # A compact six-room sheet for conversation, retaining true TV 2x size.
    select=['bridge','med_bay','hydroponics','reactor_core','maintenance_shaft','escape_pod_bay']
    sheet=Image.new('RGBA',(400,300),style.INK);d=ImageDraw.Draw(sheet);d.fontmode='1'
    d.text((10,8),'MERIDIAN / LARGER TV FEATURES',font=ImageFont.load_default(),fill=style.PAPER)
    for i,key in enumerate(select):
        rule=next(r for r in all_rules if r['id']==key);x=10+(i%3)*130;y=40+(i//3)*125
        sheet.alpha_composite(compose(tile(rule,rule['decks'][0]),rule),(x,y))
        d.text((x,y+70),rule['name'][:21],font=ImageFont.load_default(),fill=style.PAPER)
    sheet.resize((800,600),Image.Resampling.NEAREST).convert('RGB').save(review/'featured-tv.png')


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=ROOT);parser.add_argument('--rules',type=Path,default=REFERENCE)
    args=parser.parse_args();build(args.root,args.rules)
