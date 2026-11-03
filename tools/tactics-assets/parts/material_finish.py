"""Material planes for the classic 32x40 enemy/NPC layer library.

Author highlights against rig points before cloth trailing and body rotation.
Only existing pixels of the requested material may change, preserving the rig,
silhouette, facial features, weapon contacts and palette-swappable layer format.
"""
from arcpix.canvas import Canvas


def _line(c,slot,a,b,value):
    stencil=Canvas(c.w,c.h,c.slots)
    stencil.line(*a,*b,slot,value)
    mask=(stencil.slot>0)&(c.slot==c.slots.id(slot))&(c.flags==0)
    c.shade[mask]=value


def _poly(c,slot,points,value):
    stencil=Canvas(c.w,c.h,c.slots)
    stencil.polygon(points,slot,value)
    mask=(stencil.slot>0)&(c.slot==c.slots.id(slot))&(c.flags==0)
    c.shade[mask]=value


def finish_part(c,layer,variant,p):
    cx,top,bot=p.torso_cx,p.torso_top,p.torso_bot
    hip=p.mx(16)
    if layer=='torso':
        for slot in ('cloth1','cloth2'):
            # Collar occlusion and belt compression, separate from any trim.
            _line(c,slot,(cx-2,top+1),(cx+1,top+2),2)
            _line(c,slot,(cx-2,bot-2),(cx,bot-1),4)
            _line(c,slot,(cx+1,bot-2),(cx+3,bot-3),2)
        if variant in ('rags','robe','vestments','coat','tunic','vest'):
            for slot in ('cloth1','cloth2','leather'):
                _line(c,slot,(cx+2,top+3),(hip+2,bot-3),2)
                _line(c,slot,(cx-2,top+3),(hip-2,bot-3),4)
                if variant in ('rags','coat'):
                    _line(c,slot,(hip,bot),(hip-1,bot+2),2)
        if variant in ('plate','tabard'):
            # A broad turned chest plane, with a short bright upper bevel.
            _poly(c,'metal',[(cx+1,top+3),(cx+3,top+3),
                (hip+3,bot-2),(hip+1,bot-1)],2)
            _line(c,'metal',(cx-2,top+3),(cx,top+3),5)
            _line(c,'metal',(hip-2,bot-2),(hip,bot-1),1)
            for sx,sy in (p.shoulder_f,p.shoulder_n):
                _line(c,'metal',(sx-1,sy-1),(sx+1,sy-1),5)
                _line(c,'metal',(sx-1,sy+1),(sx+1,sy+1),2)
    elif layer=='legs' and variant in ('skirt','kilt'):
        hem=min(p.foot_f[1],p.foot_n[1])-1
        for slot in ('cloth1','cloth2'):
            _poly(c,slot,[(hip-1,bot+1),(hip,bot+2),(hip-1,hem),
                (hip-3,hem)],4)
            _poly(c,slot,[(hip+1,bot+1),(hip+3,hem),(hip+1,hem)],2)
    elif layer=='cape' and variant in ('cloak','short_cape'):
        end=bot+7 if variant=='cloak' else bot+2
        for slot in ('cloth1','cloth2'):
            _poly(c,slot,[(cx-1,top+2),(cx,top+3),(hip-1,end),
                (hip-3,end-1)],4)
            _poly(c,slot,[(cx+1,top+2),(cx+3,top+4),(hip+3,end),
                (hip+1,end-2)],2)
    elif layer=='body' and variant not in ('skeleton','dragon'):
        # A cheek plane and shadow under the jaw; never repaint the eyes.
        x,y=p.head_x0,p.head_y0
        if p.direction=='front':
            _line(c,'skin',(x+2,y+6),(x+3,y+7),4)
            _line(c,'skin',(x+7,y+9),(x+9,y+8),2)
        else:
            _line(c,'skin',(x+2,y+3),(x+2,y+6),4)
    elif layer in ('weapon','offhand','headgear','feet','hands'):
        for slot in ('metal','blade'):
            m=c.slot==c.slots.id(slot)
            above=m.copy();above[0]=False;above[1:]=m[:-1]
            below=m.copy();below[-1]=False;below[:-1]=m[1:]
            # A short highlight on upper-facing surfaces, a dark lower bevel.
            c.shade[m&~above&(c.shade>=3)]=5
            c.shade[m&~below&(c.shade<=3)]=1
