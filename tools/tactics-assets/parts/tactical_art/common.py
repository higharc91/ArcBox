from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from arcpix.canvas import Canvas
from arcpix.palette import ramp
from parts.characters import SLOTS, compose
from parts.creator import make_character
from parts.rig import ANIMS


def composite(layers):
    out=Canvas(64,80,SLOTS)
    for layer in layers.values():
        mask=layer.slot!=0
        for name in ('slot','shade','flags'):
            getattr(out,name)[mask]=getattr(layer,name)[mask]
    out.outline()
    return out
