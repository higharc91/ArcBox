"""Thumbnails for the phone creator's Look step: one small head per option (each hair style, facial hair, horn shape,
marking, skin and race), so choices show as pictures rather than words. They're drawn by the same layers as the
hero, on a neutral model of the option's race, so a thumbnail is exactly what picking it will do.
"""
from __future__ import annotations

import shutil

from PIL import Image

from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet, stack
from . import characters as C
from .creator import RACES, HAIR_STYLES, ALL_BEARDS, MARKS, Look, make_character
from .rig import ANIMS, BUILDS

THUMB = 24                 # crop around the head, shown 2x on the phone
POSE = ANIMS["idle"][2][0]


def head(rec) -> Image.Image:
    layers = {k: v for k, v in rec.layers.items() if k not in ("weapon", "offhand", "cape", "tail")}
    r = C.Recipe(rec.id, rec.name, rec.build, layers, rec.palette, rec.tags)
    img = C.compose(r, "front", POSE).render(r.ramps())
    top = BUILDS[rec.build].head_top
    return img.crop((4, top - 6, 4 + THUMB, top - 6 + THUMB))


def _model(race: str, **look) -> Image.Image:
    base = {"hair": "short", "hair_colour": "brown"} if RACES[race].hair else {}
    base.update(look)
    return head(make_character("x", race, "monk", Look(**base), id="thumb"))


def build() -> None:
    out = PACK / "creator"
    shutil.rmtree(out, ignore_errors=True)
    groups: dict[str, dict[str, Image.Image]] = {
        "race": {r: _model(r) for r in RACES},
        "body_type": {b: _model("human", body_type=b) for b in ("masculine", "feminine")},
        "hair": {h: _model("human", hair=h, hair_colour="auburn", body_type="feminine") for h in HAIR_STYLES},
        "beard": {b: _model("human", beard=b, hair_colour="chestnut") for b in ALL_BEARDS},
        "horns": {h: _model("tiefling", horns=h) for h in RACES["tiefling"].horns},
        "dragon_horns": {h: _model("dragonborn", horns=h) for h in RACES["dragonborn"].horns},
        "marks": {m: _model("human", marks=m, paint="woad") for m in MARKS},
    }
    groups["hair"]["none"] = _model("human", hair="none")
    rects, sections = {}, []
    cells = [(g, k, im) for g, d in groups.items() for k, im in d.items()]
    cols = 12
    atlas = Image.new("RGBA", (cols * THUMB, ((len(cells) + cols - 1) // cols) * THUMB))
    for i, (g, k, im) in enumerate(cells):
        x, y = (i % cols) * THUMB, (i // cols) * THUMB
        atlas.paste(im, (x, y))
        rects.setdefault(g, {})[k] = [x, y, THUMB, THUMB]
    save(atlas, out / "thumbs.png")
    for race, r in RACES.items():                              # every skin each race offers
        row = Image.new("RGBA", (THUMB * len(r.skins), THUMB))
        for k, sk in enumerate(r.skins):
            row.paste(_model(race, skin=sk), (k * THUMB, 0))
        save(row, out / "skins" / f"{race}.png")
    write_json({"thumb": [THUMB, THUMB], "file": "thumbs.png", "groups": rects,
                "skins": {race: {"file": f"skins/{race}.png", "order": r.skins} for race, r in RACES.items()},
                "note": "draw at 2x on the phone; each thumbnail is the option drawn by the real layers"},
               out / "manifest.json")
    for g, d in groups.items():
        sections.append(contact_sheet(g.replace("_", " ").capitalize(),
                                      [Tile(im, k) for k, im in d.items()], columns=13, zoom=3))
    save(stack("Creator thumbnails", sections, "Picture chips for the phone's Look step, drawn by the real layers."),
         PREVIEWS / "36-creator-thumbs.png")
