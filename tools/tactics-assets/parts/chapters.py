"""Chapter cards: the loading screen between scenes. Each one is a biome's diorama in its sky, colour-graded to the
chapter's mood, with the chapter ribbon and title set in the pixel font. Nothing here is new art; it's the shipped
sets composed the way the game would, which also makes it a good integration check.
"""
from __future__ import annotations

import shutil

from PIL import Image

from arcpix import iso
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet
from . import backgrounds as BG
from . import dioramas as D
from . import grades as G
from . import title as TT

CHAPTERS = [   # number, title, scene, sky, ranges, grade
    ("I", "THE ASHEN FORD", "ashen-ford", "day", "hills", "golden_hour"),
    ("II", "MIREWOOD", "mirewood", "dusk", "hills", "fey"),
    ("III", "THE SUNSCAR", "sunscar", "day", "mesas", "day"),
    ("IV", "FROSTPEAK PASS", "frostpeak", "overcast", "peaks", "dawn"),
    ("V", "THE EMBER KEEP", "ember-keep", "ashfall", "volcano", "infernal"),
]


def card(num: str, name: str, scene: str, sky: str, ranges: str, grade: str) -> Image.Image:
    img = iso.render(D.SCENES[scene](), backdrop=lambda w, h: BG.compose(sky, ranges, w, h, horizon=int(h * 0.48)))
    base = iso.finish(img, zoom=1)
    g = next(x for x in G.GRADES if x.name == grade)
    out = G.graded(base, g).convert("RGBA")
    label = TT.title_text(f"CHAPTER {num}", "silver", scale=2, spacing=1, rim=1)
    head = TT.title_text(name, "gold", scale=3, spacing=1, rim=2)
    rb = TT.ribbon(max(head.width, label.width) + 16, head.height + label.height + 6, "#1a1e3a")
    y = out.height - rb.height - 18
    out.alpha_composite(rb, ((out.width - rb.width) // 2, y))
    out.alpha_composite(label, ((out.width - label.width) // 2, y + 2))
    out.alpha_composite(head, ((out.width - head.width) // 2, y + label.height + 1))
    return out


def build() -> None:
    out = PACK / "chapters"
    shutil.rmtree(out, ignore_errors=True)
    tiles, files = [], {}
    for num, name, scene, sky, ranges, grade in CHAPTERS:
        img = card(num, name, scene, sky, ranges, grade)
        key = scene.replace("-", "_")
        save(img, out / f"{key}.png")
        files[key] = {"file": f"{key}.png", "chapter": num, "title": name, "grade": grade}
        tiles.append(Tile(img, f"Chapter {num}", name.title()))
    write_json({"cards": files, "note": "loading screens between scenes; each is a biome diorama, graded"},
               out / "manifest.json")
    save(contact_sheet("Chapter cards", tiles, columns=2, zoom=1,
                       subtitle="Loading screens composed from the shipped sets: diorama, sky, grade, ribbon, font."),
         PREVIEWS / "40-chapters.png")
