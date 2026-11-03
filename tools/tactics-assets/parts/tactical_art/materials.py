"""Authored material planes, clipped to the interchangeable garment surfaces.

Folds and bevels change shades only: they cannot move a grip, enlarge a part,
paint across a trim/aperture, or interfere with race fitting and articulation.
Coordinates are in the shared 64x80 authoring space, before race fitting.
"""
import numpy as np
from .common import Canvas, SLOTS

SHADOW_LAYERS={'near-hand','near-palm','far-palm','head','hair',
    'headgear','class-headwear','class-details','weapon','offhand','instrument-back'}


def contact_shadow(dst,mask):
    """One authoring pixel of occlusion beneath overlapping opaque parts.

    Operates on the current posed masks, so wrist/weapon shadows follow motion.
    Spells do not cast a painted dark halo and empty pixels stay transparent.
    """
    shifted=np.zeros_like(mask);shifted[1:,1:]=mask[:-1,:-1]
    receivers=np.isin(dst.slot,[SLOTS.id(s) for s in
        ('skin','cloth1','cloth2','metal','leather','wood','trim','blade')])
    hit=shifted&~mask&receivers&(dst.flags==0)
    dst.shade[hit]=np.maximum(dst.shade[hit].astype(np.int16)-1,0)


def shade(c, slot, shape, geometry, value):
    stencil=Canvas(c.w,c.h,SLOTS)
    getattr(stencil,shape)(*geometry,slot,value)
    mask=(stencil.slot>0)&(c.slot==SLOTS.id(slot))&(c.flags==0)
    c.shade[mask]=value


def polygon(c,slot,points,value):
    shade(c,slot,'polygon',(points,),value)


def line(c,slot,points,value):
    shade(c,slot,'line',points,value)


def finish_base(layers,outfit,back):
    g=layers['garment']
    if outfit=='robe':
        # Tension gathers at the belt; broad, tapered folds open toward the hem.
        polygon(g,'cloth1',[(28,51),(31,52),(30,62),(31,70),(28,72),(27,65)],4)
        polygon(g,'cloth1',[(32,52),(33,55),(34,65),(33,70),(31,71),(32,62)],2)
        polygon(g,'cloth1',[(35,52),(37,57),(39,67),(37,69),(35,63)],1)
        polygon(g,'cloth1',[(37,54),(39,58),(42,66),(40,68),(39,63)],3)
        polygon(g,'cloth1',[(24,54),(25,54),(23,64),(24,68),(22,68)],3)
        line(g,'cloth1',(29,54,28,59),3)
        line(g,'cloth1',(29,69,30,70),3)
        # Diagonal compression folds above the belt and a soft collar shadow.
        polygon(g,'cloth1',[(27,43),(31,45),(35,43),(33,46),(29,47)],2)
        line(g,'cloth1',(28,44,31,45),4)
        polygon(g,'cloth1',[(27,32),(32,33),(36,34),(34,37),(30,36)],2)
        if back:
            polygon(g,'cloth1',[(30,35),(32,36),(32,45),(30,43)],3)
        else:
            line(g,'cloth1',(29,38,29,41),4)
        # Actual hem thickness, retaining existing gold embroidery.
        line(g,'cloth1',(23,69,29,71),2)
        line(g,'cloth1',(35,69,40,70),1)
    elif outfit=='mail':
        # Separate chest planes and overlapping plate edges, not metal texture.
        polygon(g,'metal',[(31,34),(34,35),(34,42),(31,45),(30,43)],3)
        line(g,'metal',(26,35,29,34),5)
        polygon(g,'metal',[(26,37),(29,37),(31,38),(30,39),(27,39)],5)
        line(g,'metal',(25,37,25,40),5)
        line(g,'metal',(27,45,30,46),1)
        line(g,'metal',(31,46,35,43),1)
        line(g,'metal',(35,36,35,42),4)
        polygon(g,'metal',[(37,37),(40,38),(40,43),(37,46)],1)
        for x,y in ((21,34),(38,35)):
            line(g,'metal',(x-1,y-1,x+2,y),5)
            line(g,'metal',(x-2,y+2,x+3,y+3),2)
            line(g,'metal',(x-2,y+3,x+2,y+4),1)
        line(g,'cloth1',(29,53,30,58),4)
        polygon(g,'cloth1',[(32,53),(34,53),(33,59),(31,61)],2)
        if back:
            line(g,'metal',(27,35,29,34),4)
            line(g,'metal',(32,36,33,44),1)
    else:
        # Leather has broad soft planes, a turned edge and a few paired stitches.
        polygon(g,'leather',[(30,34),(33,35),(34,42),(31,45),(29,43)],3)
        polygon(g,'leather',[(36,36),(38,37),(39,43),(36,46),(35,44)],1)
        line(g,'leather',(24,38,24,42),4)
        line(g,'leather',(26,46,29,47),2)
        line(g,'leather',(28,53,27,57),3)
        line(g,'leather',(31,53,30,57),2)
        for x,y in ((26,35),(26,38),(27,55),(36,53)):
            line(g,'leather',(x,y,x+1,y),4)
    # The belt casts down onto the garment. Back view never receives a buckle.
    for slot in ('cloth1','metal','leather'):
        line(g,slot,(25,53,38,50),1 if slot=='metal' else 2)
    for name in ('cape-behind','cape-back'):
        c=layers[name];slot='cloth1' if name=='cape-back' else 'cloth2'
        polygon(c,slot,[(28,36),(31,37),(30,49),(28,59),(29,63),(26,64),(26,56)],3)
        polygon(c,slot,[(33,37),(35,39),(34,51),(36,62),(34,63),(31,56),(32,46)],1)
        polygon(c,slot,[(36,46),(38,50),(41,61),(38,64),(37,59)],2)
        line(c,slot,(24,62,27,63),4)
        line(c,slot,(33,33,37,35),1)
    for name in ('near-hand','far-arm'):
        c=layers[name]
        if name=='near-hand':
            line(c,'cloth1',(40,40,41,43),4)
            line(c,'cloth1',(42,44,44,45),1)
            line(c,'cloth1',(40,46,42,47),3)
            line(c,'metal',(41,48,44,48),5)
            line(c,'metal',(41,51,44,51),1)
        else:
            line(c,'cloth1',(20,38,19,41),3)
            line(c,'cloth1',(19,44,21,45),1)
    # Boot uppers/soles are readable at the half-density runtime size.
    c=layers['legs']
    line(c,'leather',(23,67,27,68),3)
    line(c,'leather',(33,71,38,71),4)
    line(c,'leather',(34,73,40,74),1)
    # These anatomical planes become visible on the unarmoured barbarian.
    c=layers['body']
    if back:
        polygon(c,'skin',[(25,34),(29,33),(31,36),(29,44),(26,46),(24,42)],4)
        polygon(c,'skin',[(33,35),(38,36),(40,42),(36,46),(33,43)],2)
        line(c,'skin',(31,36,32,45),2)
    else:
        polygon(c,'skin',[(25,35),(29,35),(31,38),(30,41),(26,41),(24,39)],4)
        polygon(c,'skin',[(34,36),(38,37),(40,41),(37,44),(34,42)],2)
        line(c,'skin',(27,43,30,44),2)
        line(c,'skin',(30,45,31,47),4)


def finish_equipment(layers):
    """Small directional bevels on rigid equipment; retain authored bright cores."""
    for name in ('weapon','offhand','instrument-back','class-headwear','headgear'):
        c=layers.get(name)
        if c is None:continue
        for slot in ('metal','blade','trim'):
            mask=c.slot==SLOTS.id(slot)
            above=np.zeros_like(mask);above[1:]=mask[:-1]
            left=np.zeros_like(mask);left[:,1:]=mask[:,:-1]
            below=np.zeros_like(mask);below[:-1]=mask[1:]
            right=np.zeros_like(mask);right[:,:-1]=mask[:,1:]
            # Rim light is short and upper-left; no outline or extra coverage.
            lit=mask&~above&~left&(c.shade>=3)
            dark=mask&~below&~right&(c.shade<=3)
            c.shade[lit]=5 if slot=='blade' else 4
            c.shade[dark&~lit]=1
