from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from arcpix.canvas import Canvas
from arcpix.palette import ramp
from parts.characters import SLOTS, compose
from parts.creator import make_character
from parts.rig import ANIMS


def composite(layers):
    out=Canvas(64,80,SLOTS)
    from .materials import SHADOW_LAYERS,contact_shadow
    for name,layer in layers.items():
        mask=layer.slot!=0
        if name in SHADOW_LAYERS:
            contact_shadow(out,mask&(layer.slot!=SLOTS.id('magic')))
        for attribute in ('slot','shade','flags'):
            getattr(out,attribute)[mask]=getattr(layer,attribute)[mask]
    out.outline()
    return out
