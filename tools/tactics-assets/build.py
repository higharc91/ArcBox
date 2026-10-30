"""Builds the tactics base content pack and its review sheets.

    python3 tools/tactics-assets/build.py [set ...]     # sets: characters (default: all)

Writes content/tactics/base/... (what the game loads) and tools/tactics-assets/previews/*.png (what people review).
Everything is deterministic: the same code produces byte-identical PNGs, so the pack hash only moves when art does.
"""
from __future__ import annotations

import json
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from PIL import Image  # noqa: E402

from arcpix.palette import FAMILIES, RAMP_LEN  # noqa: E402
from arcpix.preview import contact_sheet, Tile, strip  # noqa: E402
from arcpix.sheet import Sheet  # noqa: E402
from parts import characters as C  # noqa: E402
from parts.recipes import ALL, PARTY, CLASSES, ENEMIES  # noqa: E402
from parts import creator  # noqa: E402
from arcpix.preview import matrix  # noqa: E402
from parts.rig import BUILDS, DIRECTIONS, FRAME_W, FRAME_H, ANCHOR, ANIM_ORDER, ANIMS  # noqa: E402

PACK = HERE.parents[1] / "content" / "tactics" / "base"
MOCKUPS = HERE.parents[1] / "docs" / "superpowers" / "mockups" / "tactics" / "assets"
PREVIEWS = HERE / "previews"


def save(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)


def write_json(obj, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2) + "\n")


# --------------------------------------------------------------------------------------------------- characters
# A neutral palette for previewing bare layers: every slot gets a readable, distinct ramp.
NEUTRAL = {"skin": "skin/tan", "eyes": "eyes/dark", "hair": "hair/brown", "cloth1": "cloth/steel_blue",
           "cloth2": "cloth/slate", "trim": "metal/gold", "metal": "metal/steel", "leather": "leather/brown",
           "wood": "wood/oak", "magic": "magic/arcane", "horn": "horn/bone"}


def build_characters() -> None:
    out = PACK / "characters"
    shutil.rmtree(out, ignore_errors=True)       # renamed or removed parts must not linger in the pack
    n = C.frames_per_direction()

    # 1. Every layer variant, for every build, as a ramp-map sheet (front row, back row).
    variants: dict[str, list[str]] = {}
    for layer, parts in C.PARTS.items():
        variants[layer] = sorted(parts)
        for variant in parts:
            for build in BUILDS:
                s = Sheet(FRAME_W, FRAME_H, n, ANCHOR)
                s.frames = [cv.ramp_map() for cv in C.layer_frames(layer, variant, build)]
                save(s.image(), out / build / layer / f"{variant}.png")

    # 2. Palettes: every named ramp, plus one palette per recipe, and the LUT texture the shader samples.
    ramps = {fam: {name: ramp for name, ramp in rs.items()} for fam, rs in FAMILIES.items()}
    palettes = {r.id: r.palette for r in ALL}
    palettes = {"neutral": NEUTRAL, **palettes}
    lut, rows = C.lut(palettes)
    save(lut, out / "palettes.png")
    write_json({"ramps": ramps, "palettes": palettes, "lutRows": rows, "rampLength": RAMP_LEN}, out / "palettes.json")

    # 3. Presets: recipe JSON plus a baked, ready-to-draw sheet (use these when recolouring isn't needed).
    for r in ALL:
        sheet = C.recipe_sheet(r)
        save(sheet.image(), out / "presets" / f"{r.id}.png")
        write_json({"id": r.id, "name": r.name, "race": r.race, "class": r.cls, "build": r.build, "layers": r.layers,
                    "palette": r.palette, "tags": r.tags, "sheet": f"presets/{r.id}.png"},
                   out / "presets" / f"{r.id}.json")

    # 3b. The creator's options: every race and class kit, and what each race may pick.
    write_json(creator.options(), out / "creator.json")

    # 4. The manifest: everything the game needs to cut sheets up and compose a character.
    anims = {a.name: {"frames": a.frames, "fps": a.fps, "loop": a.loop} for a in C.anim_table()}
    write_json({
        "frame": {"width": FRAME_W, "height": FRAME_H, "anchor": list(ANCHOR)},
        "framesPerDirection": n,
        "directions": {d: {"row": i, "mirrorFor": "front_left" if d == "front" else "back_right"}
                       for i, d in enumerate(DIRECTIONS)},
        "anims": anims,
        "builds": {k: {"headTop": b.head_top, "shoulderY": b.shoulder_y, "hipY": b.hip_y} for k, b in BUILDS.items()},
        "slots": C.SLOTS.names[1:],
        "slotIds": {name: i for i, name in enumerate(C.SLOTS.names) if i},
        "layers": variants,
        "order": C.ORDER,
        "path": "characters/{build}/{layer}/{variant}.png",
        "rampMap": {"r": "slot * 16", "g": "shade * 32", "b": "1 = outline pixel", "a": "coverage"},
        "outline": {"colour": "#140f1a", "mix": 0.6},
        "presets": [r.id for r in ALL],
    }, out / "manifest.json")

    # ----- previews
    def roster(title: str, recipes, sub: str) -> Image.Image:
        tiles = []
        for r in recipes:
            sh = C.recipe_sheet(r)
            if r.race:
                caption = f"{creator.RACES[r.race].name} {creator.CLASSES[r.cls].name}"
            else:
                caption = f"{r.build} build"
            tiles.append(Tile(strip([sh.frames[0], sh.frames[n]], 1, gap=6), r.name, caption))
        return contact_sheet(title, tiles, columns=min(6, len(tiles)), zoom=5, subtitle=sub)

    save(roster("Characters · the party", PARTY,
                "Six sample heroes: each is a name + race + class + look. Front and back; the game mirrors the other two."),
         PREVIEWS / "01-party.png")
    save(roster("Characters · class starters", CLASSES,
                "One preset per class, each on a different race. Every piece is swappable and recolourable."),
         PREVIEWS / "02-classes.png")
    save(roster("Characters · enemies", ENEMIES,
                "Same rig, same layers: goblins and orcs reuse the 'short' and 'large' builds."),
         PREVIEWS / "03-enemies.png")

    # Animation strips for a handful of characters.
    tiles = []
    for rid in ("kara", "class_paladin", "tamsin", "ilyra", "class_bard", "orc_brute"):
        r = next(x for x in ALL if x.id == rid)
        sh = C.recipe_sheet(r)
        tiles.append(Tile(strip(sh.frames[:n], 1, gap=3), r.name, "  ".join(
            f"{a}×{len(ANIMS[a][2])}" for a in ANIM_ORDER)))
    save(contact_sheet("Characters · animation set", tiles, columns=1, zoom=4,
                       subtitle="idle · walk · swing · thrust · shoot · cast · hurt · down (20 frames per facing)"),
         PREVIEWS / "04-animations.png")

    # Any race x any class: the creator's two axes, with the race's default look and the class's kit.
    races, classes = list(creator.RACES), list(creator.CLASSES)
    pose_idle = ANIMS["idle"][2][0]
    grid_front, grid_back = [], []
    for race in races:
        rf, rb = [], []
        for cls in classes:
            rec = creator.make_character("Preview", race, cls)
            rf.append(C.compose(rec, "front", pose_idle).render(rec.ramps()))
            rb.append(C.compose(rec, "back", pose_idle).render(rec.ramps()))
        grid_front.append(rf)
        grid_back.append(rb)
    rnames = [creator.RACES[r].name for r in races]
    cnames = [creator.CLASSES[c].name for c in classes]
    save(matrix("Characters · any race, any class", rnames, cnames, grid_front, zoom=3,
                subtitle="Players name their hero and pick a race and a class. All 90 combinations compose from the "
                         "same layers (race defaults shown; hair, skin, horns and colours are the player's)."),
         PREVIEWS / "06-race-class.png")
    save(matrix("Characters · any race, any class (back)", rnames, cnames, grid_back, zoom=3),
         PREVIEWS / "07-race-class-back.png")
    # The same matrix as a plain 32x40 atlas for the creator mockup (row = race, column = class).
    atlas = Image.new("RGBA", (FRAME_W * len(classes), FRAME_H * len(races)))
    for i, row in enumerate(grid_front):
        for j, im in enumerate(row):
            atlas.paste(im, (j * FRAME_W, i * FRAME_H))
    save(atlas, MOCKUPS / "race-class.png")

    # The layer library on the neutral palette.
    ramps_neutral = {s: FAMILIES[ref.split("/")[0]][ref.split("/")[1]] for s, ref in NEUTRAL.items()}
    pose = ANIMS["idle"][2][0]
    base_body = C.render_layer("body", "human", "standard", "front", pose)
    tiles = []
    for layer in C.ORDER["front"]:
        for variant in sorted(C.PARTS[layer]):
            cv = C.Canvas(FRAME_W, FRAME_H, C.SLOTS)
            if layer not in ("body",):
                ghost = base_body.copy()
                cv.blit(ghost)
            if layer in ("cape", "tail"):
                cv = C.Canvas(FRAME_W, FRAME_H, C.SLOTS)
                cv.blit(C.render_layer(layer, variant, "standard", "back", pose))
                cv.blit(C.render_layer("body", "human", "standard", "back", pose))
                cv.blit(C.render_layer(layer, variant, "standard", "back", pose))
            else:
                cv.blit(C.render_layer(layer, variant, "standard", "front", pose))
            cv.outline()
            tiles.append(Tile(cv.render(ramps_neutral), variant, layer))
    save(contact_sheet("Characters · layer library", tiles, columns=12, zoom=3,
                       subtitle="Every layer variant on a bare body (neutral palette). Mix any set per character."),
         PREVIEWS / "05-layers.png")


def build_palettes() -> None:
    tiles = []
    for fam, ramps in FAMILIES.items():
        for name, ramp in ramps.items():
            img = Image.new("RGBA", (6 * len(ramp), 10))
            for i, hexc in enumerate(ramp):
                img.paste(Image.new("RGBA", (6, 10), hexc), (i * 6, 0))
            tiles.append(Tile(img, name, fam))
    save(contact_sheet("Palette ramps", tiles, columns=10, zoom=4,
                       subtitle="6-step hue-shifted ramps (cool shadows, warm highlights). Any slot takes any ramp."),
         PREVIEWS / "00-palettes.png")


# Which attack animation a weapon plays; anything not listed (staff, wand, tome, lute, unarmed) casts.
ATTACK_FOR = {"sword": "attack", "greatsword": "attack", "axe": "attack", "mace": "attack", "dagger": "thrust",
              "spear": "thrust", "rapier": "thrust", "bow": "shoot"}


def build_anim_previews() -> None:
    """Animated GIFs of the character set: every actor plays the same beat (idle, walk, attack, hurt, down)."""
    from arcpix.anim import Actor, stage_gif
    n = C.frames_per_direction()
    table = {a.name: a.frames for a in C.anim_table()}

    def hold(frames: list[int], ticks: int) -> list[int]:
        return [f for f in frames for _ in range(ticks)]

    def actors_for(recipes) -> list[Actor]:
        out = []
        for r in recipes:
            sub = (f"{creator.RACES[r.race].name} {creator.CLASSES[r.cls].name}" if r.race else r.name)
            out.append(Actor(r.name if r.race else r.name, sub if r.race else f"{r.build} build",
                             C.recipe_sheet(r).frames, ANCHOR))
        return out

    def script(recipes) -> list:
        k = len(recipes)
        atk = [table[ATTACK_FOR.get(r.layers.get("weapon", ""), "cast")] for r in recipes]
        return [
            ("Idle", [table["idle"] * 3] * k, 330),
            ("Walk", [table["walk"] * 3] * k, 125),
            ("Walk · away", [[f + n for f in table["walk"]] * 3] * k, 125),
            ("Attack", [[table["idle"][0]] * 2 + hold(a, 2) + [a[-1]] * 3 for a in atk], 90),
            ("Attack", [[table["idle"][0]] * 2 + hold(a, 2) + [a[-1]] * 3 for a in atk], 90),
            ("Hurt", [hold(table["hurt"], 6) + [table["idle"][0]] * 3] * k, 90),
            ("Downed", [hold(table["down"], 10)] * k, 90),
        ]

    for name, title, recipes, cols in (
            ("anim-party", "The party", PARTY, 6),
            ("anim-classes", "Class starters", CLASSES, 5),
            ("anim-enemies", "Enemies", ENEMIES, 4)):
        stage_gif(PREVIEWS / f"{name}.gif", title, actors_for(recipes), script(recipes), zoom=4, columns=cols)

    # One character walking in all four isometric directions: two drawn facings, two mirrored.
    kara = next(r for r in PARTY if r.id == "kara")
    fr = C.recipe_sheet(kara).frames
    dirs = [Actor("South-east", "front", fr, ANCHOR), Actor("South-west", "front, mirrored", fr, ANCHOR, mirror=True),
            Actor("North-west", "back", fr, ANCHOR), Actor("North-east", "back, mirrored", fr, ANCHOR, mirror=True)]
    walk = table["walk"]
    stage_gif(PREVIEWS / "anim-directions.gif", "Four facings from two drawn ones", dirs,
              [("Walk", [walk * 4, walk * 4, [f + n for f in walk] * 4, [f + n for f in walk] * 4], 125)], zoom=5)


SETS = {"palettes": build_palettes, "characters": build_characters, "anims": build_anim_previews}

if __name__ == "__main__":
    for name in sys.argv[1:] or list(SETS):
        print("building", name)
        SETS[name]()
