"""Race turnarounds on a consistent ranger outfit, using indexed material layers."""
from .anatomy import draw
from .common import composite
from .common import *
from parts.creator import RACES
import numpy as np
import json




def reshape(canvas,race):
    """Warp indexed geometry, preserving material ids and outline flags.

    Heads retain their pixel density; short builds shorten the torso/legs rather
    than uniformly shrinking the finished sprite. The foot anchor stays at y=75.
    """
    width={'dwarf':1.10,'halfling':.88,'gnome':.88,'half_orc':1.12,'goliath':1.12}.get(race,1)
    ys={'dwarf':[0,24,44,57,75,80],'halfling':[0,25,45,58,75,80],
        'gnome':[0,25,45,58,75,80],'half_orc':[0,9,30,49,75,80],
        'goliath':[0,9,30,49,75,80]}.get(race,[0,12,33,50,75,80])
    x=np.clip(np.rint((np.arange(64)-32)/width+32).astype(int),0,63)
    y=np.clip(np.rint(np.interp(np.arange(80),ys,[0,12,33,50,75,80])).astype(int),0,79)
    out=Canvas(64,80,SLOTS)
    for attr in ('slot','shade','flags'):
        setattr(out,attr,getattr(canvas,attr)[y[:,None],x[None,:]].copy())
    return out


def race_character(race,back=False,phase=0,fitted=True):
    _,_,source=draw('ranger',back,phase,hair='long' if race in ('elf','tiefling','aasimar') else 'short')
    rec=make_character('Race study',race,'ranger')
    palette=rec.ramps()
    palette['trim']=ramp('#c39343')
    # The same ranger clothes on all builds. Uncover heads to show racial traits.
    clothing=source['clothing-and-equipment']
    clothing.slot[:33+phase%2,18:45]=0
    head=source['head']
    tail=Canvas(64,80,SLOTS)
    traits=Canvas(64,80,SLOTS)
    bob=phase%2
    def P(c,pts,s,sh=3): c.polygon([(x,y+bob) for x,y in pts],s,sh)
    def L(c,x,y,r,b,s,sh=3): c.line(x,y+bob,r,b+bob,s,sh)
    def R(c,x,y,r,b,s,sh=3): c.rect(x,y+bob,r,b+bob,s,sh)
    # Ears occupy their own layer so long hair can hide the roots without
    # erasing the visible tips. Back ears use quieter shading.
    if race in ('elf','half_elf','gnome','half_orc'):
        length={'elf':6,'half_elf':3,'gnome':4,'half_orc':3}[race]
        for x,sgn in ((25,-1),(40,1)):
            P(traits,[(x,22),(x+sgn*length,19),(x+sgn*(length-1),24),(x,26)],'skin',2 if back else 3)
            L(traits,x,23,x+sgn*(length-2),22,'skin',4 if not back else 3)
    if race=='half_elf':
        # Tied auburn hair is visible from behind, without masking the ears.
        if back:
            P(traits,[(35,26),(38,28),(39,36),(35,40),(34,34)],'hair',2)
            L(traits,36,29,37,35,'hair',4); R(traits,35,27,38,28,'leather',2)
    if race=='dwarf':
        if not back:
            P(traits,[(25,26),(28,28),(32,27),(36,28),(40,25),(40,32),(37,37),(33,40),(28,36),(25,32)],'hair',2)
            P(traits,[(27,29),(30,30),(31,35),(29,34)],'hair',4)
            P(traits,[(34,30),(37,28),(37,33),(34,37)],'hair',3)
            L(traits,29,28,35,28,'hair',1)
            R(traits,30,35,34,36,'trim',3)
        else:
            P(traits,[(29,27),(35,27),(36,34),(32,37),(29,33)],'hair',2)
            L(traits,31,28,33,31,'hair',4)
            R(traits,30,33,34,34,'trim',3)
    if race in ('halfling','gnome'):
        # A compact broken fringe keeps the small builds distinct.
        P(traits,[(24,17),(23,14),(26,11),(29,12),(31,10),(34,12),(37,11),(40,14),(40,18),(37,17),(35,19),(32,17),(29,19),(27,17)],'hair',2)
        L(traits,26,14,28,13,'hair',4); L(traits,32,13,34,14,'hair',4)
        if race=='gnome' and not back:
            P(traits,[(34,24),(37,25),(38,27),(34,28),(33,26)],'skin',4)
        if race=='halfling':
            # Bare feet with leather sandal straps.
            for x in (26,38):
                R(traits,x-4,70,x+5,74,'skin',3)
                L(traits,x-4,72,x+5,72,'leather',2)
    if race in ('half_orc','goliath'):
        # A broad skull and heavier jaw, drawn independently from the hair.
        head=Canvas(64,80,SLOTS)
        P(head,[(25,15),(29,11),(36,11),(41,15),(42,25),(39,31),(29,32),(24,28),(23,21)],'skin',3)
        P(head,[(25,16),(29,13),(32,13),(30,23),(26,26)],'skin',4)
        P(head,[(38,15),(41,18),(42,25),(37,30),(34,29)],'skin',2)
        R(head,29,30,36,34,'skin',2)
        if not back:
            R(head,28,23,29,24,'eyes',1); R(head,36,23,37,24,'eyes',1)
            L(head,27,21,31,21,'skin',1); L(head,35,21,39,21,'skin',1)
            L(head,31,29,36,29,'skin',1)
            R(head,34,25,35,26,'skin',4)
        if race=='half_orc':
            P(traits,[(29,13),(29,8),(32,5),(35,8),(36,15),(33,13)],'hair',2)
            L(traits,31,9,32,7,'hair',4)
            if not back:
                P(traits,[(29,29),(28,26),(30,27),(31,30)],'horn',4)
                P(traits,[(37,29),(38,26),(36,27),(35,30)],'horn',4)
        else:
            # Stone markings follow the face in front and the scalp behind.
            if back:
                L(traits,32,14,30,19,'paint',2); L(traits,30,19,33,24,'paint',2)
                L(traits,33,24,31,29,'paint',2)
            else:
                L(traits,26,17,28,20,'paint',2); L(traits,28,20,26,25,'paint',2)
                L(traits,38,26,36,29,'paint',2)
    if race=='tiefling':
        for x,sg in ((25,-1),(39,1)):
            P(traits,[(x,16),(x+sg*3,13),(x+sg*4,9),(x+sg*2,6),(x,7),(x+sg,10),(x,12),(x-sg,14)],'horn',3)
            L(traits,x+sg*2,8,x+sg*3,10,'horn',4)
        if not back:
            R(traits,28,23,29,23,'eyes',4); R(traits,36,23,36,23,'eyes',4)
        P(tail,[(39,54),(43,59),(49,61),(53,58),(54,52),(56,53),(56,60),(51,64),(44,63),(39,59)],'skin',3)
        P(tail,[(53,53),(52,48),(56,50),(58,48),(56,55)],'skin',2)
    if race=='aasimar':
        # A restrained halo and warm eye accents distinguish celestial skin.
        L(traits,27,7,37,7,'trim',4); L(traits,25,8,27,7,'trim',3)
        L(traits,37,7,40,9,'trim',3)
        if not back:
            R(traits,28,23,29,23,'eyes',4); R(traits,36,23,36,23,'eyes',4)
    if race=='dragonborn':
        head=Canvas(64,80,SLOTS)
        P(head,[(24,17),(28,12),(35,13),(40,18),(42,22),(47,24),(48,28),(43,31),(30,32),(24,28),(22,23)],'skin',3)
        P(head,[(25,18),(29,14),(33,15),(29,24),(25,26)],'skin',4)
        R(head,29,31,36,35,'skin',2)
        if not back:
            P(head,[(36,24),(46,25),(46,28),(38,29)],'skin',4)
            L(head,38,30,45,29,'skin',1); R(head,44,25,45,25,'skin',1)
            L(head,28,21,32,22,'skin',2)
            R(head,29,23,31,23,'eyes',3); R(head,30,23,30,24,'eyes',0)
            R(head,40,30,40,31,'horn',4)
        else:
            for y in (17,21,25,29):
                P(traits,[(30,y),(33,y-2),(35,y),(32,y+2)],'horn',3)
        for x in (25,37):
            P(traits,[(x,18),(x-3,14),(x-4,8),(x-2,10),(x,13),(x+2,16)],'horn',3)
            L(traits,x-3,11,x-1,15,'horn',5)
        P(tail,[(39,53),(44,58),(50,59),(55,56),(57,51),(58,56),(56,62),(50,65),(43,62),(39,58)],'skin',3)
        L(tail,44,59,50,62,'skin',4)
    # Restore quiver arrows as a background layer, free of the head's face mask.
    arrows=Canvas(64,80,SLOTS)
    for x in (18,21,24):
        L(arrows,x,25,x+5,40,'wood',4)
        P(arrows,[(x,25),(x-2,22),(x,20),(x+2,24)],'horn',4)
    # With the back towards us the tail root is in front of the rear garment.
    layers={'tail-behind':tail if not back else Canvas(64,80,SLOTS),
            'quiver-arrows':arrows,'head':head,'outfit':clothing,
            'rear-garment':source['rear-garment'],'race-details':traits}
    if back: layers['tail-back']=tail
    shaped={name:reshape(c,race) for name,c in layers.items()} if fitted else layers
    return composite(shaped),palette,shaped
