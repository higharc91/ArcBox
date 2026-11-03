"""Shared shaded human head templates used to author racial hair and traits."""
from .common import *

def draw(kind,back=False,phase=0,hair="short"):
    y=phase%2
    head=Canvas(64,80,SLOTS)
    def P(c,pts,s,shade=3): c.polygon([(x,v+y) for x,v in pts],s,shade)
    def R(c,x,v,r,b,s,shade=3): c.rect(x,v+y,r,b+y,s,shade)
    def L(c,x,v,r,b,s,shade=3): c.line(x,v+y,r,b+y,s,shade)
    if back:
        P(head,[(26,15),(30,12),(36,12),(40,16),(40,26),(36,31),(29,31),(25,26)],'skin',2)
        R(head,30,29,35,34,'skin',2)
        P(head,[(24,18),(25,14),(29,11),(36,11),(41,15),(41,25),(38,30),(34,29),(31,30),(26,27)],'hair',2)
        P(head,[(26,16),(30,13),(34,13),(31,20),(27,24),(25,22)],'hair',4)
        P(head,[(34,14),(38,15),(40,19),(39,25),(36,27),(34,25)],'hair',3)
        L(head,30,21,29,26,'hair',1)
        if hair=='long':
            P(head,[(25,21),(40,21),(42,37),(36,40),(33,37),(28,40),(24,36)],'hair',2)
            L(head,28,24,27,35,'hair',4); L(head,36,24,38,35,'hair',3)
    else:
        R(head,30,29,35,34,'skin',2)
        # Asymmetric jaw and nose give the face a slight three-quarter turn.
        P(head,[(26,16),(30,13),(36,14),(40,18),(41,25),(38,29),(34,32),(29,30),(25,26),(24,21)],'skin',3)
        P(head,[(26,18),(29,15),(33,15),(32,23),(28,26),(26,24)],'skin',4)
        P(head,[(38,19),(40,21),(41,25),(37,29),(34,30),(33,28),(37,26)],'skin',2)
        R(head,24,23,25,25,'skin',3)
        # Two compact eye clusters; far eye is narrower, no outlined whites.
        R(head,28,23,29,24,'eyes',1)
        R(head,36,23,36,24,'eyes',1)
        L(head,28,21,30,21,'skin',2)
        R(head,35,21,37,21,'skin',2)
        R(head,34,25,35,26,'skin',4)
        L(head,32,29,34,29,'skin',2)
        # The fringe overlaps the upper forehead rather than framing an oval.
        P(head,[(24,23),(23,18),(25,13),(29,11),(36,11),(40,14),(41,19),(40,22),(37,18),(34,17),(30,20),(30,17),(26,20),(26,25)],'hair',2)
        P(head,[(25,16),(28,13),(33,12),(36,13),(31,15),(28,18),(26,18)],'hair',4)
        L(head,35,14,38,16,'hair',3)
        if hair=='long':
            P(head,[(24,22),(26,26),(28,30),(26,36),(23,34)],'hair',2)
            P(head,[(39,22),(41,24),(42,34),(38,37),(39,29)],'hair',2)
    return None,None,{"head":head,"clothing-and-equipment":Canvas(64,80,SLOTS),"rear-garment":Canvas(64,80,SLOTS)}
