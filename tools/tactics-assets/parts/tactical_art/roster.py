"""Approved tactical art direction applied to all 12 classes and 11 races.

Independent indexed parts in both facings, consumed by the runtime animation rig.
"""
from .base import actor
from .common import *
from .common import composite
from .races import race_character, reshape
from parts.creator import RACES, CLASSES
import numpy as np
import json


KITS={
 'fighter':('fighter','mail',True,'#8b4148'),
 'rogue':('ranger','leather',True,'#454a56'),
 'wizard':('cleric','robe',False,'#51466f'),
 'cleric':('cleric','robe',False,'#d5cbb2'),
 'ranger':('ranger','leather',True,'#52674a'),
 'paladin':('fighter','mail',True,'#c8c7b4'),
 'bard':('ranger','leather',False,'#487478'),
 'druid':('cleric','robe',False,'#647353'),
 'warlock':('cleric','robe',True,'#60495e'),
 'monk':('ranger','leather',False,'#b18a50'),
 'barbarian':('ranger','leather',False,'#8e694b'),
 'sorcerer':('cleric','robe',False,'#706080'),
}


def character(role,race='human',back=False,outfit=None):
    base,garment,cloak,color=KITS[role]
    _,pal,layers=actor(base,back,outfit or garment,helmet=role=='fighter',cloak=cloak)
    race_recipe=make_character('Tactical',race,role)
    for slot in ('skin','hair','eyes','horn','paint'): pal[slot]=race_recipe.ramps()[slot]
    pal['cloth1']=ramp(color,spread=.85)
    pal['magic']=race_recipe.ramps()['magic']
    # Optional class details are separate from the shared fitted garment.
    extra=Canvas(64,80,SLOTS)
    headwear=Canvas(64,80,SLOTS)
    weapon=Canvas(64,80,SLOTS)
    offhand=Canvas(64,80,SLOTS)
    hand=layers['near-hand']
    def P(c,pts,s,sh=3): c.polygon(pts,s,sh)
    def R(c,x,y,r,b,s,sh=3): c.rect(x,y,r,b,s,sh)
    def L(c,x,y,r,b,s,sh=3): c.line(x,y,r,b,s,sh)
    def E(c,x,y,rx,ry,s,sh=3): c.ellipse(x,y,rx,ry,s,sh)
    # Ranger's original base includes elf ears: race traits are now independent.
    if base=='ranger': layers['headgear']=Canvas(64,80,SLOTS)
    if role not in ('fighter','cleric','ranger'):
        layers['headgear']=Canvas(64,80,SLOTS)
        layers['weapon']=weapon; layers['offhand']=offhand
        # Quiver is only appropriate for archers, not every leather-clad class.
        layers['cape-behind'].slot[:31,14:29]=0
    # A real far palm is required even when a shield or book normally hides it.
    R(layers['far-arm'],17,47,20,50,'skin',3)
    if role=='rogue':
        P(headwear,[(21,25),(21,16),(25,11),(32,8),(39,12),(42,18),(42,28),(37,32),(26,31)],'cloth1',2)
        if not back:
            # Hood aperture reveals the shared face; cowl obscures the mouth.
            for y in range(18,27):
                for x in range(25,40): headwear.clear(x,y)
            P(headwear,[(25,26),(31,28),(40,25),(39,30),(33,33),(26,30)],'cloth2',1)
        L(headwear,23,17,27,12,'cloth1',4)
        # Each dagger belongs to its own hand. The old combined weapon layer
        # incorrectly moved the offhand blade with the main-hand swing.
        for target,x,y,sg in ((offhand,19,48,-1),(weapon,44,51,1)):
            P(target,[(x,y-2),(x+sg*2,y-4),(x+sg*8,y-12),
                      (x+sg*7,y-7),(x+sg*2,y)],'blade',3)
            L(target,x-2,y-3,x+3,y,'trim',3)
            R(target,x-1,y-1,x+1,y+2,'leather',2)
        R(layers['far-arm'],17,47,20,50,'skin',3)
        if not back:
            R(extra,24,52,28,56,'leather',2); L(extra,25,53,27,53,'leather',4)
    elif role in ('wizard','druid'):
        R(weapon,49,25,51,73,'wood',2); L(weapon,49,26,49,70,'wood',4)
        if role=='wizard':
            P(headwear,[(21,18),(25,12),(28,9),(32,3),(38,6),(41,10),(35,8),(33,13),(39,20)],'cloth1',3)
            P(headwear,[(25,15),(29,11),(32,6),(32,11),(28,17)],'cloth1',4)
            P(headwear,[(18,20),(24,18),(38,18),(43,21),(37,22),(22,22)],'cloth1',2)
            L(headwear,24,17,37,18,'trim',3)
            P(weapon,[(46,24),(43,19),(45,13),(49,10),(54,15),(55,20),(52,25)],'trim',2)
            P(weapon,[(46,17),(49,12),(52,16),(52,20),(48,23),(45,20)],'magic',3)
            P(weapon,[(46,17),(49,13),(48,20),(46,21)],'magic',5)
        else:
            L(weapon,49,25,46,18,'wood',3);L(weapon,46,18,49,11,'wood',3)
            L(weapon,47,20,42,15,'wood',3)
            for x,y in ((43,16),(49,12),(52,28)):
                P(weapon,[(x,y),(x-2,y-4),(x+2,y-6),(x+4,y-3)],'cloth1',4)
            for x,sg in ((25,-1),(38,1)):
                L(headwear,x,18,x+sg*4,9,'wood',3)
                L(headwear,x+sg*3,12,x+sg*6,8,'wood',3)
            P(extra,[(24,32),(29,33),(32,36),(36,33),(40,34),(37,40),(30,40),(25,38)],'cloth1',2)
            L(extra,26,34,30,37,'cloth1',4)
        for y in (31,34,55,58):R(weapon,49,y,51,y,'trim',3)
    elif role=='paladin':
        P(weapon,[(46,51),(48,26),(50,17),(52,26),(50,51)],'blade',3)
        L(weapon,50,22,49,46,'blade',5);L(weapon,43,48,55,48,'trim',3)
        R(weapon,47,51,49,56,'leather',2)
        for x,sg in ((24,-1),(39,1)):
            P(headwear,[(x,19),(x+sg*5,12),(x+sg*5,17),(x+sg*2,22)],'metal',4)
        L(headwear,25,18,38,17,'trim',3)
        if not back:
            P(extra,[(28,34),(34,33),(36,59),(31,62),(27,59)],'cloth1',4)
            L(extra,31,37,32,44,'trim',3);L(extra,29,40,34,39,'trim',3)
    elif role=='bard':
        P(headwear,[(23,16),(25,11),(33,8),(40,12),(38,17)],'cloth1',3)
        L(headwear,22,18,41,18,'trim',3)
        P(headwear,[(35,11),(38,5),(43,3),(41,8)],'horn',4)
        E(weapon,29,53,7,9,'wood',3);E(weapon,28,51,5,6,'wood',4);E(weapon,29,51,2,2,'wood',0)
        P(weapon,[(32,48),(45,34),(48,36),(35,51)],'wood',2)
        L(weapon,30,55,46,36,'horn',4);L(weapon,32,55,47,37,'horn',3)
        if back:
            # Keep the lute behind the torso when viewing from the rear.
            layers['instrument-back']=weapon
            layers['weapon']=Canvas(64,80,SLOTS)
        for y in (36,40,44):R(extra,30,y,31,y,'trim',3)
    elif role=='warlock':
        P(extra,[(24,34),(24,29),(29,32),(32,37),(37,31),(40,29),(41,35),(36,40),(28,38)],'cloth2',1)
        P(offhand,[(10,45),(22,41),(24,55),(12,59)],'leather',1)
        P(offhand,[(12,46),(20,43),(22,54),(14,56)],'cloth1',3)
        L(offhand,16,47,17,53,'trim',3);L(offhand,14,49,20,48,'trim',3)
        P(weapon,[(49,33),(53,39),(49,45),(45,39)],'magic',3)
        P(weapon,[(49,36),(51,39),(49,42),(47,39)],'magic',5)
    elif role=='monk':
        # Cloth wrap tunic instead of leather; open front is directional.
        garment_layer=layers['garment']
        garment_layer.slot[garment_layer.slot==SLOTS.id('leather')]=SLOTS.id('cloth1')
        if not back:
            P(extra,[(26,33),(30,35),(35,32),(33,43),(30,46)],'skin',3)
            L(extra,25,34,32,46,'cloth2',3)
        P(extra,[(24,47),(38,45),(40,49),(25,52)],'cloth2',3)
        if not back:P(extra,[(34,50),(37,50),(40,62),(36,60)],'cloth2',2)
        for target,x in ((layers['far-arm'],18),(hand,41)):
            R(target,x,47,x+4,50,'horn',3)
            L(target,x,48,x+4,48,'horn',1)
        E(headwear,31,9,3,2,'hair',2)
    elif role=='barbarian':
        # Bare torso and fur shoulder mantle, not a recoloured jerkin.
        g=layers['garment']
        g.slot[:48]=0
        for layer in ('far-arm','near-hand'):
            layers[layer].slot[layers[layer].slot==SLOTS.id('cloth1')]=SLOTS.id('skin')
        P(extra,[(21,32),(24,29),(28,32),(32,30),(36,32),(40,31),(43,35),(41,39),(37,37),(30,38),(25,36),(21,39),(19,35)],'leather',2)
        L(extra,24,33,28,34,'leather',4)
        if not back:
            L(extra,26,35,37,46,'leather',2)
            L(extra,28,42,30,43,'skin',2)
        L(weapon,47,29,49,71,'wood',3)
        P(weapon,[(46,22),(41,20),(38,23),(39,30),(44,34),(46,28)],'blade',3)
        P(weapon,[(49,22),(55,20),(58,24),(56,31),(51,34),(50,28)],'blade',3)
        L(weapon,39,24,40,28,'blade',5);L(weapon,56,24,55,29,'blade',5)
        R(weapon,46,23,50,28,'metal',2)
        L(headwear,25,18,38,17,'leather',2)
    elif role=='sorcerer':
        P(extra,[(24,35),(23,29),(28,32),(32,38),(37,31),(41,28),(41,35),(35,41),(28,39)],'cloth2',2)
        L(extra,26,34,31,39,'trim',3)
        if not back:
            P(extra,[(25,48),(38,44),(39,48),(25,52)],'trim',2)
            R(extra,31,40,33,43,'magic',3)
        for x,y,s in ((14,43,4),(49,39,6)):
            target=offhand if x==14 else weapon
            P(target,[(x,y-s),(x+2,y-1),(x+s,y),(x+3,y+4),(x-2,y+5),(x-s,y+1),(x-2,y-2)],'magic',3)
            P(target,[(x,y-2),(x+2,y+1),(x,y+3),(x-2,y+1)],'magic',5)
    from .materials import finish_equipment
    layers['class-headwear']=headwear
    finish_equipment(layers)
    # Race anatomy is separate from clothing, and shared across all class recipes.
    _,_,racial=race_character(race,back,fitted=False)
    traits=racial['race-details']
    if race in ('half_orc','goliath','dragonborn'):
        layers['head']=racial['head']
        layers['hair']=Canvas(64,80,SLOTS)
    elif race in ('elf','tiefling','aasimar'):
        # Keep tactical facial geometry and replace only the hairstyle surfaces.
        hair=Canvas(64,80,SLOTS)
        src=racial['head'];mask=src.slot==SLOTS.id('hair')
        for attr in ('slot','shade','flags'):getattr(hair,attr)[mask]=getattr(src,attr)[mask]
        layers['hair']=hair
    elif race=='human' and base=='ranger':
        # Shared human hair, not ranger's old elf-specific long hair.
        _,_,human=actor('cleric',back,cloak=False)
        layers['hair']=human['hair']
    # Hood must hide the rear hair, horns deliberately remain above headwear.
    if role=='rogue' and back: layers['hair']=Canvas(64,80,SLOTS)
    # Palms stay rigid while the sleeve/forearm can stretch toward a socket.
    # Scaling the palm with a very bent arm could shrink it out of a pixel frame.
    for arm,palm_name,rect,point in (
        ('near-hand','near-palm',(41,49,47,55),(44,51)),
        ('far-arm','far-palm',(16,46,22,52),(19,48))):
        source=layers[arm];palm=Canvas(64,80,SLOTS)
        x0,y0,x1,y1=rect
        region=np.zeros_like(source.slot,dtype=bool);region[y0:y1,x0:x1]=True
        region&=source.slot>0
        for attr in ('slot','shade','flags'):getattr(palm,attr)[region]=getattr(source,attr)[region]
        # Leave a cuff underneath the rigid palm, extending the limb to the wrist.
        cuff=int(source.slot[point[1]-3,point[0]]) or SLOTS.id('cloth1')
        source.slot[region]=cuff;source.shade[region]=2
        layers[palm_name]=palm
    layers['class-details']=extra
    layers['class-headwear']=headwear
    # Reinsert headwear directly above head/hair, before rear cloak and weapons.
    ordered={}
    ordered['tail-behind']=racial['tail-behind']
    for name,layer in layers.items():
        if name in ('class-details','class-headwear','instrument-back','near-palm','far-palm'): continue
        ordered[name]=layer
        if name=='far-arm':ordered['far-palm']=layers['far-palm']
        if name=='near-hand':ordered['near-palm']=layers['near-palm']
        if name=='cape-behind' and 'instrument-back' in layers:
            ordered['instrument-back']=layers['instrument-back']
        if name=='garment': ordered['class-details']=extra
        if name=='headgear':
            ordered['class-headwear']=headwear;ordered['race-details']=traits
    if back and 'tail-back' in racial: ordered['tail-back']=racial['tail-back']
    # Race build fitting happens once, after independently authoring the layers.
    fitted={name:reshape(layer,race) for name,layer in ordered.items()}
    from .attachments import validate_attachments
    validate_attachments(role,fitted)
    return composite(fitted),pal,fitted
