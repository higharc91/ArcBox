"""Shared three-quarter anatomy and material-finished garment templates.

Original geometry, authored as independent indexed layers for the runtime rig.
"""
from .common import *
from .common import composite
from arcpix.palette import ramp
import json




def actor(role='fighter',back=False,outfit=None,helmet=True,cloak=True):
    outfit=outfit or {'fighter':'mail','cleric':'robe','ranger':'leather'}[role]
    layers={name:Canvas(64,80,SLOTS) for name in
            ('cape-behind','legs','body','far-arm','garment','head','hair','headgear','cape-back','weapon','offhand','near-hand')}
    pal=make_character('Study','elf' if role=='ranger' else 'human',role).ramps()
    pal.update(cloth1=ramp({'fighter':'#8b4148','cleric':'#d5cbb2','ranger':'#52674a'}[role],spread=.85),
               cloth2=ramp('#3d4654'),metal=ramp('#7e929e',spread=1.2),
               trim=ramp('#b79556'),leather=ramp('#655044'),blade=ramp('#a9bbc2',spread=1.2))
    def P(layer,pts,s,sh=3): layers[layer].polygon(pts,s,sh)
    def R(layer,x,y,r,b,s,sh=3): layers[layer].rect(x,y,r,b,s,sh)
    def L(layer,x,y,r,b,s,sh=3): layers[layer].line(x,y,r,b,s,sh)
    # The far foot is higher and narrower than the near foot. Both have a top plane.
    P('legs',[(24,49),(31,49),(31,63),(28,69),(22,68),(24,59)],'cloth2',2)
    P('legs',[(33,49),(40,48),(41,61),(39,73),(32,73),(33,62)],'cloth2',3)
    P('legs',[(23,65),(29,66),(29,69),(32,70),(31,72),(21,71),(20,69)],'leather',2)
    P('legs',[(33,68),(40,68),(40,71),(42,73),(41,75),(32,75),(31,73)],'leather',2)
    L('legs',23,66,27,67,'leather',4); L('legs',33,70,38,70,'leather',4)
    L('legs',22,70,29,71,'leather',1);L('legs',33,74,42,74,'leather',1)
    # Turned torso: short far shoulder, long near plane, diagonal waist.
    P('body',[(24,32),(32,30),(40,34),(42,43),(39,51),(29,53),(23,47),(21,39)],'skin',3)
    if cloak:
        P('cape-behind',[(24,32),(35,31),(40,38),(44,63),(39,68),(33,64),(24,67),(19,63)],'cloth2',2)
        P('cape-behind',[(24,36),(28,36),(26,57),(22,63),(21,61)],'cloth2',3)
    # Sleeve geometry also follows a turned pose; arms do not hang parallel.
    P('far-arm',[(23,34),(26,36),(24,43),(21,48),(17,47),(17,43),(20,37)],'cloth1',2)
    P('near-hand',[(39,37),(43,38),(46,46),(44,51),(40,50),(38,44)],'cloth1',2)
    R('near-hand',41,47,45,50,'leather',2)
    R('near-hand',42,50,45,53,'skin',3)
    L('near-hand',40,39,42,44,'cloth1',3)
    L('near-hand',42,43,43,46,'cloth1',1)
    # Garments share fitting anchors, but have distinct silhouettes and construction.
    if outfit=='mail':
        P('garment',[(24,32),(31,31),(39,34),(41,43),(38,50),(29,52),(23,47)],'metal',2)
        P('garment',[(25,34),(30,33),(35,35),(35,44),(29,47),(24,44)],'metal',4)
        P('garment',[(35,35),(39,36),(40,44),(36,48),(33,46)],'metal',2)
        L('garment',25,35,25,41,'metal',5)
        L('garment',26,45,30,47,'metal',1);L('garment',30,47,36,44,'metal',1)
        for x,y in ((21,34),(38,35)):
            P('garment',[(x-3,y),(x,y-3),(x+5,y-1),(x+6,y+3),(x+2,y+5),(x-3,y+3)],'metal',3)
            L('garment',x-1,y-1,x+3,y,'metal',5)
            L('garment',x-2,y+3,x+3,y+4,'metal',1)
        P('garment',[(26,50),(37,48),(40,61),(34,60),(31,63),(25,60)],'cloth1',3)
        L('garment',28,53,27,59,'cloth1',4);L('garment',35,52,36,58,'cloth1',1)
        for x,y in ((25,51),(35,50)):
            P('garment',[(x,y),(x+4,y-1),(x+5,y+6),(x+1,y+7)],'metal',3)
            L('garment',x+1,y+1,x+3,y,'metal',4)
        if back:
            P('garment',[(24,33),(31,31),(39,34),(40,44),(37,49),(29,51),(24,46)],'metal',2)
            P('garment',[(25,34),(30,33),(32,35),(30,44),(26,46)],'metal',3)
            L('garment',32,35,33,45,'metal',1)
            L('garment',25,45,31,48,'metal',1)
            L('garment',31,48,37,45,'metal',1)
            for y in (38,42): L('garment',35,y,38,y-1,'metal',3)
        else:
            L('garment',28,36,32,37,'metal',5)
            L('garment',36,39,37,42,'metal',1)
        R('near-hand',41,48,45,51,'metal',2)
        L('near-hand',42,48,44,49,'metal',4)
    elif outfit=='robe':
        P('garment',[(24,32),(31,31),(39,34),(40,49),(46,68),(40,72),(34,70),(30,73),(21,70),(20,67),(25,49)],'cloth1',3)
        P('garment',[(25,34),(29,34),(29,49),(25,66),(23,68),(22,66)],'cloth1',4)
        P('garment',[(35,35),(39,36),(38,50),(43,66),(40,69),(36,64),(32,51)],'cloth1',2)
        L('garment',30,54,28,65,'cloth1',2);L('garment',35,58,37,67,'cloth1',1)
        L('garment',22,69,29,71,'trim',3);L('garment',35,69,41,70,'trim',3)
        if not back:
            P('garment',[(25,33),(28,34),(27,49),(24,63),(22,64)],'trim',3)
            P('garment',[(35,33),(38,34),(38,48),(41,64),(38,65),(35,48)],'trim',2)
            L('garment',31,36,32,43,'trim',3)
            R('garment',31,41,33,43,'magic',3)
        else:
            L('garment',28,33,35,34,'trim',3)
            L('garment',32,37,32,46,'cloth1',2)
    else:
        P('garment',[(24,33),(31,31),(39,34),(40,43),(37,49),(40,58),(35,61),(30,58),(25,61),(22,56),(25,48),(22,43)],'leather',3)
        P('garment',[(25,34),(29,33),(31,35),(29,45),(25,47),(23,42)],'leather',4)
        P('garment',[(35,34),(38,36),(39,43),(36,48),(32,46)],'leather',2)
        L('garment',27,51,25,57,'leather',4)
        L('garment',35,50,37,56,'leather',1)
        if not back:
            L('garment',26,35,36,47,'cloth2',1)
            R('garment',29,39,31,41,'trim',3)
        else: L('garment',25,35,36,46,'leather',1)
    # Belt follows the hip plane; rear view has no buckle or hanging front pouch.
    P('garment',[(24,48),(38,46),(40,49),(25,52)],'leather',1)
    if not back:
        P('garment',[(30,48),(34,47),(35,50),(31,51)],'trim',3)
        L('garment',31,49,33,49,'leather',1)
        R('garment',36,50,40,55,'leather',2)
        L('garment',37,51,39,51,'leather',4)
    else:
        L('garment',26,49,27,50,'leather',3)
        L('garment',36,47,37,49,'leather',3)
    # Smaller face, angled jaw, restrained eyes and minimal mouth.
    R('head',29,28,35,33,'skin',2)
    P('head',[(24,16),(29,12),(35,12),(39,16),(40,22),(42,24),(40,26),(38,29),(32,31),(26,28),(23,23)],'skin',3)
    P('head',[(25,17),(28,14),(32,14),(31,23),(27,25),(25,23)],'skin',4)
    P('head',[(36,15),(39,18),(39,23),(42,24),(39,26),(37,29),(33,29),(34,26)],'skin',2)
    if not back:
        R('head',28,22,29,23,'eyes',1);R('head',36,22,36,23,'eyes',1)
        L('head',28,20,30,20,'skin',2)
        R('head',38,24,40,24,'skin',4)
        L('head',34,28,36,28,'skin',2)
    # Hair is an independently selectable surface, drawn differently from behind.
    if back:
        P('hair',[(23,18),(24,14),(28,10),(35,10),(40,14),(41,22),(38,28),(34,30),(27,28),(23,24)],'hair',2)
        P('hair',[(25,16),(29,12),(33,12),(30,19),(26,24),(24,22)],'hair',4)
        L('hair',35,15,37,23,'hair',3)
    else:
        P('hair',[(23,23),(22,18),(25,12),(29,10),(36,10),(40,14),(40,21),(37,17),(34,16),(31,19),(30,16),(26,19),(25,25)],'hair',2)
        P('hair',[(25,15),(29,12),(33,12),(31,15),(27,17)],'hair',4)
    if role=='ranger':
        # Long pale hair and ears sit above the coat but behind the bow.
        if back:
            P('hair',[(25,20),(39,20),(40,34),(35,39),(32,35),(27,38),(24,33)],'hair',2)
            L('hair',28,23,27,33,'hair',4);L('hair',35,24,37,33,'hair',3)
        else:
            P('hair',[(23,22),(25,25),(27,32),(25,36),(23,32)],'hair',2)
            P('hair',[(39,22),(41,25),(41,34),(37,37),(38,29)],'hair',2)
        P('headgear',[(24,22),(18,19),(20,24),(25,26)],'skin',3)
        P('headgear',[(39,22),(44,20),(43,24),(39,26)],'skin',3)
    if role=='fighter' and helmet:
        P('headgear',[(22,18),(23,13),(27,9),(35,9),(40,13),(42,20),(37,19),(25,19)],'metal',3)
        P('headgear',[(24,14),(28,11),(31,11),(29,17),(24,17)],'metal',4)
        L('headgear',31,10,33,19,'metal',5)
        P('headgear',[(22,19),(25,20),(26,28),(23,26)],'metal',3)
        P('headgear',[(39,20),(41,20),(40,27),(37,29)],'metal',2)
        if back:
            P('headgear',[(23,19),(41,19),(39,27),(33,30),(25,27)],'metal',2)
            L('headgear',26,21,36,22,'metal',4)
        P('headgear',[(29,9),(29,5),(32,3),(39,5),(42,9),(36,7),(33,8)],'cloth1',3)
        L('headgear',32,4,37,5,'cloth1',4)
    if role=='cleric':
        # Hood rests on the shoulders, leaving the face free.
        P('headgear',[(25,31),(28,33),(34,32),(37,30),(41,34),(37,38),(30,36),(24,35)],'cloth2',2)
        L('headgear',26,32,30,34,'cloth2',4)
        L('headgear',25,18,38,17,'trim',3)
        R('headgear',31,17,33,18,'magic',3)
    if back and cloak:
        P('cape-back',[(24,32),(30,31),(38,34),(39,48),(44,63),(37,68),(31,64),(23,66),(20,62)],'cloth1',2)
        P('cape-back',[(25,34),(28,35),(27,48),(23,61),(22,62)],'cloth1',3)
        P('cape-back',[(34,36),(37,36),(36,49),(40,62),(36,64),(32,57)],'cloth1',1)
        L('cape-back',25,33,33,34,'trim',3)
        L('cape-back',23,64,27,65,'trim',2);L('cape-back',37,66,41,63,'trim',2)
    if role=='fighter':
        # Tilted shield has a lit top edge, narrow side plane and dark lower bevel.
        if not back:
            P('offhand',[(9,43),(18,39),(26,43),(25,57),(20,65),(13,61),(9,55)],'trim',2)
            P('offhand',[(10,44),(18,41),(23,44),(22,56),(19,61),(14,58),(11,53)],'cloth1',3)
            L('offhand',10,43,18,40,'trim',5)
            P('offhand',[(23,44),(25,43),(24,56),(20,63),(19,61),(22,55)],'trim',1)
            L('offhand',17,45,18,56,'trim',4);L('offhand',14,48,21,47,'trim',4)
            L('offhand',12,46,13,52,'cloth1',4)
        else:
            P('offhand',[(10,44),(18,41),(22,44),(21,56),(17,62),(12,58)],'leather',2)
            L('offhand',13,46,15,55,'wood',4)
        P('weapon',[(45,48),(50,36),(57,22),(59,20),(58,27),(49,48)],'blade',3)
        L('weapon',50,38,57,24,'blade',5)
        L('weapon',43,45,53,50,'trim',3)
        L('weapon',45,49,44,54,'leather',2)
    elif role=='cleric':
        R('weapon',48,32,50,55,'wood',2)
        P('weapon',[(43,29),(46,25),(51,25),(55,29),(53,35),(46,36),(43,33)],'metal',2)
        P('weapon',[(45,29),(48,27),(50,28),(49,33),(46,33)],'metal',4)
        L('weapon',50,27,52,30,'trim',3)
        layers['offhand'].ellipse(16,49,7,9,'trim',2)
        layers['offhand'].ellipse(16,49,5,7,'metal',2)
        if not back:
            L('offhand',16,44,16,54,'trim',4);L('offhand',12,48,20,48,'trim',4)
    else:
        # Bow is offset from the body; quiver fletching uses a rear attachment.
        for a,b in zip([(49,28),(54,34),(56,44),(54,54)],[(54,34),(56,44),(54,54),(49,61)]):
            L('weapon',*a,*b,'wood',3);L('weapon',a[0]+1,a[1],b[0]+1,b[1],'wood',1)
        L('weapon',49,28,49,61,'horn',3)
        for x in (18,21,24):
            L('cape-behind',x,23,x+6,42,'wood',3)
            P('cape-behind',[(x,23),(x-2,20),(x,18),(x+2,22)],'horn',4)
    from .materials import finish_base
    finish_base(layers,outfit,back)
    return composite(layers),pal,layers
