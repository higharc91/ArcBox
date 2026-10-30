"""Builds the tactics base content pack and its review sheets.

    python3 tools/tactics-assets/build.py [set ...]     # sets: characters (default: all)

Writes content/tactics/base/... (what the game loads) and tools/tactics-assets/previews/*.png (what people review).
Everything is deterministic: the same code produces byte-identical PNGs, so the pack hash only moves when art does.
"""
from __future__ import annotations

import importlib
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
from parts.recipes import ALL, PARTY, CLASSES, ENEMIES, NPCS  # noqa: E402
from parts import creator  # noqa: E402
from arcpix.preview import matrix  # noqa: E402
from parts.rig import BUILDS, DIRECTIONS, FRAME_W, FRAME_H, ANCHOR, ANIM_ORDER, ANIMS  # noqa: E402

from arcpix.out import PACK, PREVIEWS, MOCKUPS, save, write_json  # noqa: E402


# --------------------------------------------------------------------------------------------------- characters
# A neutral palette for previewing bare layers: every slot gets a readable, distinct ramp.
NEUTRAL = {"skin": "skin/tan", "eyes": "eyes/dark", "hair": "hair/brown", "cloth1": "cloth/steel_blue",
           "cloth2": "cloth/slate", "trim": "metal/gold", "metal": "metal/steel", "leather": "leather/brown",
           "wood": "wood/oak", "magic": "magic/arcane", "horn": "horn/bone", "paint": "paint/woad",
           "blade": "metal/steel"}


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
                "Six sample heroes: each is a name + race + class + look. Front and back; the game mirrors the "
                "other two."),
         PREVIEWS / "01-party.png")
    save(roster("Characters · class starters", CLASSES,
                "One preset per class, each on a different race. Every piece is swappable and recolourable."),
         PREVIEWS / "02-classes.png")
    save(roster("Characters · enemies", ENEMIES,
                "Same rig, same layers: goblins and orcs reuse the 'short' and 'large' builds."),
         PREVIEWS / "03-enemies.png")
    save(roster("Characters · townsfolk", NPCS,
                "Exploration and dialogue NPCs from the same layers; tools ride in the weapon slot."),
         PREVIEWS / "03b-townsfolk.png")

    # Every animation, one row each, on a character that would use it.
    showcase = {"idle": "kara", "ready": "kara", "walk": "tamsin", "attack": "kara", "thrust": "wren",
                "shoot": "tamsin", "cast": "ilyra", "slam": "class_paladin", "spin": "black_knight",
                "flurry": "class_monk", "bash": "kara", "block": "aldric", "dodge": "wren", "jump": "class_monk",
                "item": "sefa", "victory": "kara", "hurt": "aldric", "down": "aldric", "rise": "aldric"}
    sheets = {}
    tiles = []
    for a in C.anim_table():
        rid = showcase[a.name]
        if rid not in sheets:
            sheets[rid] = C.recipe_sheet(next(x for x in ALL if x.id == rid))
        sh = sheets[rid]
        tiles.append(Tile(strip([sh.frames[k] for k in a.frames], 1, gap=4), a.name,
                          f"{len(a.frames)} frames · {a.fps:g} fps{' · loop' if a.loop else ''} · {rid}"))
    save(contact_sheet("Characters · animation set", tiles, columns=2, zoom=3,
                       subtitle=f"{len(C.anim_table())} animations, {n} frames per facing. Attacks go anticipation, "
                                f"strike (with a motion smear), impact, recovery; idles breathe and blink."),
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
                subtitle="Players name their hero and pick a race and a class. All 110 combinations compose from the "
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
    party = Image.new("RGBA", (FRAME_W * len(PARTY), FRAME_H))     # the party's real looks, for the mockups
    for j, r in enumerate(PARTY):
        party.paste(C.compose(r, "front", pose_idle).render(r.ramps()), (j * FRAME_W, 0))
    save(party, MOCKUPS / "party.png")

    build_customisation()
    build_equipment()
    build_subclasses(out)

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


def build_terrain() -> None:
    """content/tactics/base/terrain: world-aligned tops, sides and lips, ground decals, and a manifest."""
    from parts import terrain as TR
    out = PACK / "terrain"
    shutil.rmtree(out, ignore_errors=True)
    entries = {}
    by_kind: dict[str, list[Tile]] = {"top": [], "side": [], "lip": [], "decal": []}
    for t in TR.CATALOGUE:
        frames = t.frames()
        files = {}
        for vname, v in t.variants.items():
            ramps = TR.ramps(v)
            imgs = [f.render(ramps) for f in frames]
            sheet = Image.new("RGBA", (imgs[0].width * len(imgs), imgs[0].height))
            for k, im in enumerate(imgs):
                sheet.paste(im, (k * im.width, 0))
            rel = f"{t.kind}/{t.id}_{vname}.png"
            save(sheet, out / rel)
            files[vname] = rel
            img = imgs[0]
            by_kind[t.kind].append(Tile(img, t.id, vname + (f" · {len(frames)} frames" if len(frames) > 1 else "")))
        entries[t.id] = {"kind": t.kind, "variants": files, "frames": len(frames), "fps": t.fps,
                         "size": [frames[0].w, frames[0].h]}
    write_json({
        "tile": TR.TILE, "span": TR.T // TR.TILE, "heightStep": TR.TILE // 2,
        "sampling": {
            "top": "UV = world XZ / (span * tile): one texture covers span x span tiles",
            "side": "U = distance along the edge / (span * tile); V = depth below the top / tile (repeats)",
            "lip": "drawn over the top band of a side under a top that has one; transparent below its edge",
            "decal": "32x32, alpha; placed on a tile's top in tile-local UV",
            "animated": "frames are laid out left to right; play at fps, looping",
        },
        "textures": entries,
    }, out / "manifest.json")
    save(contact_sheet("Terrain · tops", by_kind["top"], columns=6, zoom=2,
                       subtitle="Seamless, world-aligned: each 128 px texture spans 4x4 tiles. Water and lava are "
                                "8-frame loops."), PREVIEWS / "09-terrain-tops.png")
    save(contact_sheet("Terrain · sides and lips", by_kind["side"] + by_kind["lip"], columns=4, zoom=3,
                       subtitle="Cliff faces repeat every tile of depth; lips hang over the top band where a top "
                                "meets its side."), PREVIEWS / "10-terrain-sides.png")
    save(contact_sheet("Terrain · ground decals", by_kind["decal"], columns=10, zoom=4, checker=True,
                       subtitle="Scattered on tile tops for variety: flowers, stones, leaves, bones, cracks, "
                                "puddles, blood and scorch marks."), PREVIEWS / "11-decals.png")
    build_dioramas()


def build_dioramas() -> None:
    from arcpix import iso
    from parts import dioramas as D
    from parts import backgrounds as BG
    pairs = {"ashen-ford": ("day", "hills"), "frostpeak": ("overcast", "peaks"), "mirewood": ("dusk", "hills"),
             "sunscar": ("day", "mesas"), "ember-keep": ("ashfall", "volcano")}

    def backdrop(name):
        sk, biome = pairs[name]
        return lambda w, h: BG.compose(sk, biome, w, h, horizon=int(h * 0.48))

    for name, make in D.SCENES.items():
        save(iso.finish(iso.render(make(), backdrop=backdrop(name))), PREVIEWS / f"12-diorama-{name}.png")
    # the ford again, animated: the river flows
    # 24 frames: the least common multiple of the water (8), campfire (6) and banner (4) loops
    bd = backdrop("ashen-ford")
    frames = [iso.finish(iso.render(D.ashen_ford(k), backdrop=bd), zoom=1).convert("P", palette=Image.Palette.ADAPTIVE)
              for k in range(24)]
    frames[0].save(PREVIEWS / "anim-river.gif", save_all=True, append_images=frames[1:], duration=140, loop=0)


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


SUBCLASS_RACE = {"fighter": "human", "rogue": "halfling", "wizard": "elf", "cleric": "dwarf", "ranger": "half_elf",
                 "paladin": "dragonborn", "bard": "tiefling", "druid": "gnome", "warlock": "human", "monk": "half_orc"}


def build_subclasses(out: Path) -> None:
    """06b-subclasses.png (every subclass: front, back and mid-swing) and a pack atlas of front idles that the phone's
    subclass picker shows. In the game a hero's own race and look carry through; these use one race per class."""
    pose, swing = ANIMS["idle"][2][0], ANIMS["attack"][2][2]
    cells, names, atlas_index = [], [], {}
    atlas = Image.new("RGBA", (FRAME_W * 3, FRAME_H * len(creator.CLASSES)))
    for row, cls in enumerate(creator.CLASSES):
        subs = [x for x in creator.SUBCLASSES.values() if x.cls == cls]
        r_cells = []
        for col, sub in enumerate(subs):
            rec = creator.make_character(sub.name, SUBCLASS_RACE[cls], cls,
                                         creator.Look(body_type="feminine" if col == 1 else "masculine"),
                                         subclass=sub.id)
            frames = [C.compose(rec, "front", pose), C.compose(rec, "back", pose), C.compose(rec, "front", swing)]
            imgs = [f.render(rec.ramps()) for f in frames]
            r_cells.append(strip(imgs, 1, gap=2))
            atlas.paste(imgs[0], (col * FRAME_W, row * FRAME_H))
            atlas_index[sub.id] = [col * FRAME_W, row * FRAME_H, FRAME_W, FRAME_H]
        cells.append(r_cells)
        names.append(creator.CLASSES[cls].name)
    save(atlas, out / "subclasses.png")
    write_json({"frame": [FRAME_W, FRAME_H], "rects": atlas_index,
                "note": "front idle of each subclass on a sample race, for the subclass picker"},
               out / "subclasses.json")
    cols = ["I", "II", "III"]
    img = matrix("Subclasses · 30, picked at level 3", names, cols, cells, zoom=3,
                 subtitle="Each subclass reshapes its class kit (gear, colours, companions). Front · back · swing.")
    # label each cell with its subclass name under the matrix's cells
    from PIL import ImageDraw
    from arcpix.preview import font, MUTED
    d = ImageDraw.Draw(img)
    cw = (img.width - 20 - 150 - 16) // 3
    for row, cls in enumerate(creator.CLASSES):
        subs = [x for x in creator.SUBCLASSES.values() if x.cls == cls]
        ch = (img.height - 86 - 30 - 20) // len(creator.CLASSES)
        for col, sub in enumerate(subs):
            d.text((20 + 150 + col * cw + 8, 86 + 30 + row * ch + 4), sub.name, font=font(12, True), fill=MUTED)
    save(img, PREVIEWS / "06b-subclasses.png")


def build_equipment() -> None:
    """19-equipment.png: what loot looks like. Every weapon, armour and helm on one hero, then metal tiers."""
    from arcpix.preview import stack
    pose = ANIMS["idle"][2][0]
    swing = ANIMS["attack"][2][2]
    mk, L = creator.make_character, creator.Look

    def img(rec, ps=pose):
        return C.compose(rec, "front", ps).render(rec.ramps())

    sections = []
    tiles = []
    for w in sorted(C.PARTS["weapon"]):
        rec = mk("x", "human", "fighter", L(gear={"weapon": w, "offhand": None}))
        tiles.append(Tile(strip([img(rec), img(rec, ANIMS[ATTACK_FOR.get(w, "cast")][2][1])], 1, gap=3), w,
                          f"idle · {ATTACK_FOR.get(w, 'cast')}"))
    sections.append(contact_sheet("Weapons", tiles, columns=8, zoom=3))
    tiles = []
    for t in sorted(C.PARTS["torso"]):
        tiles.append(Tile(img(mk("x", "human", "fighter", L(gear={"torso": t, "offhand": None, "weapon": None}))),
                          t, "torso"))
    for h in sorted(C.PARTS["headgear"]):
        tiles.append(Tile(img(mk("x", "dwarf", "fighter", L(gear={"headgear": h, "offhand": None}))), h,
                          "headgear"))
    sections.append(contact_sheet("Armour and headgear", tiles, columns=10, zoom=3))
    tiles = []
    for tier in ("iron", "steel", "silver", "gold", "bronze", "mithril", "adamantine", "obsidian", "elven", "dark"):
        rec = mk("x", "human", "paladin", L(colours={"metal": f"metal/{tier}", "blade": f"metal/{tier}"},
                                            gear={"headgear": "open_helm"}))
        tiles.append(Tile(img(rec, swing), tier, "metal tier"))
    sections.append(contact_sheet("Loot tiers · one plate set, ten metals", tiles, columns=10, zoom=3,
                                  subtitle="Armour (metal) and weapon (blade) metals are separate slots, so each "
                                           "item upgrades on its own."))
    save(stack("Equipment", sections, "Loot swaps a layer's variant; better loot swaps its metal ramp too."),
         PREVIEWS / "19-equipment.png")


def build_customisation() -> None:
    """08-customisation.png: everything a player can change about their hero, section by section."""
    from arcpix.preview import stack
    pose = ANIMS["idle"][2][0]

    def front(rec, d="front"):
        return C.compose(rec, d, pose).render(rec.ramps())

    def head(rec):
        top = BUILDS[rec.build].head_top                     # crop around the head, wherever the build puts it
        return front(rec).crop((4, max(0, top - 5), 28, top + 15))

    mk = creator.make_character
    L = creator.Look
    sections = []

    tiles = []
    for race in creator.RACES:
        a, b = mk("x", race, "monk"), mk("x", race, "monk", L(body_type="feminine"))
        tiles.append(Tile(strip([front(a), front(b)], 1, gap=4), creator.RACES[race].name, "masculine · feminine"))
    sections.append(contact_sheet("Body type", tiles, columns=9, zoom=3,
                                  subtitle="Every race in both body types. All gear follows the silhouette."))

    tiles = []
    for i, h in enumerate(creator.HAIR_STYLES + ["none"]):
        bt = "feminine" if i % 2 else "masculine"
        rec = mk("x", "human", "bard", L(body_type=bt, hair=h, hair_colour=creator.HAIR_COLOURS[(i * 5) % 12]))
        tiles.append(Tile(strip([front(rec), front(rec, "back")], 1, gap=3), h if h != "none" else "bald",
                          "front · back"))
    sections.append(contact_sheet("Hair · 12 styles and bald", tiles, columns=7, zoom=3))

    tiles = []
    for b in creator.ALL_BEARDS:
        tiles.append(Tile(head(mk("x", "human", "fighter", L(beard=b, hair_colour="chestnut"))), b, "facial hair"))
    for m, race, paint in zip(creator.MARKS, ["half_elf", "human", "half_orc", "elf", "tiefling"],
                              ["woad", "woad", "woad", "kohl", "kohl"]):
        tiles.append(Tile(head(mk("x", race, "monk", L(marks=m, paint=paint, body_type="feminine"))), m, "markings"))
    sections.append(contact_sheet("Face · facial hair and markings", tiles, columns=11, zoom=6,
                                  subtitle="Painted markings take any paint colour: woad, ochre, ash, kohl, gold, "
                                           "crimson, jade."))

    tiles = []
    for race, r in creator.RACES.items():
        heads = [head(mk("x", race, "monk", L(skin=sk))) for sk in r.skins]
        tiles.append(Tile(strip(heads, 1, gap=2), r.name, f"{len(r.skins)} skins"))
    sections.append(contact_sheet("Skin · each race's range", tiles, columns=3, zoom=5))

    eyes = [head(mk("x", "human", "monk", L(eyes=e, body_type="feminine", hair="pixie"))) for e in creator.EYES]
    hairs = [head(mk("x", "human", "monk", L(hair="short", hair_colour=hc))) for hc in creator.HAIR_COLOURS]
    horns = [head(mk("x", "tiefling", "monk", L(horns=h, horn=hc)))
             for h, hc in zip(creator.RACES["tiefling"].horns, ["obsidian", "ram", "ivory", "crimson"])]
    sections.append(contact_sheet("Colours and horns", [
        Tile(strip(eyes, 1, gap=2), "Eyes", "7 colours"), Tile(strip(hairs, 1, gap=2), "Hair colour", "12 colours"),
        Tile(strip(horns, 1, gap=2), "Horns", "4 shapes x 5 colours (tiefling, dragonborn)")], columns=1, zoom=6))
    save(stack("Character customisation", sections,
               "Name, race and class come first; then the hero's look. Every option is a layer or a palette ramp."),
         PREVIEWS / "08-customisation.png")


# Which attack animation a weapon plays; anything not listed (staff, wand, tome, lute, unarmed) casts.
ATTACK_FOR = {"sword": "attack", "greatsword": "attack", "axe": "attack", "mace": "attack", "dagger": "thrust",
              "spear": "thrust", "rapier": "thrust", "bow": "shoot", "warhammer": "attack", "flail": "attack",
              "scimitar": "attack", "quarterstaff": "attack", "halberd": "thrust", "crossbow": "shoot"}


def build_anim_previews() -> None:
    """Animated GIFs of the character set: every actor plays the same beats (idle, ready, walk, their weapon's
    attack, a class special, guard, hurt, downed, revived, victory), plus an idle close-up and the four facings."""
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

    def special(r) -> str:
        w, off = r.layers.get("weapon", ""), r.layers.get("offhand", "")
        if off in ("round_shield", "kite_shield", "tower_shield", "buckler"):
            return "bash"
        if not w:
            return "flurry"
        if w in ("greatsword", "warhammer", "axe"):
            return "slam"
        if w in ("halberd", "flail", "scimitar"):
            return "spin"
        if w in ("bow", "crossbow"):
            return "jump"
        if ATTACK_FOR.get(w) == "thrust":
            return "flurry" if off in ("dagger", "blade", "main_gauche") else "dodge"
        return "item"

    def script(recipes) -> list:
        k = len(recipes)
        atk = [table[ATTACK_FOR.get(r.layers.get("weapon", ""), "cast")] for r in recipes]
        spc = [table[special(r)] for r in recipes]
        guard = [table["block" if r.layers.get("offhand", "") in ("round_shield", "kite_shield", "tower_shield",
                                                                     "buckler") else "dodge"] for r in recipes]
        idle0 = table["idle"][0]
        return [
            ("Idle", [table["idle"] * 3] * k, 250),
            ("Battle ready", [table["ready"] * 3] * k, 200),
            ("Walk", [table["walk"] * 3] * k, 125),
            ("Walk · away", [[f + n for f in table["walk"]] * 3] * k, 125),
            ("Attack", [[table["ready"][0]] * 2 + hold(a, 2) + [a[-1]] * 3 for a in atk], 80),
            ("Special", [[table["ready"][0]] * 2 + hold(a, 2) + [a[-1]] * 3 for a in spc], 80),
            ("Guard", [hold(g, 4) + [table["ready"][0]] * 2 for g in guard], 90),
            ("Hurt", [hold(table["hurt"], 3) + [idle0] * 3] * k, 90),
            ("Downed", [hold(table["down"], 8)] * k, 90),
            ("Revived", [hold(table["rise"], 4) + [idle0] * 2] * k, 90),
            ("Victory", [table["victory"] * 4] * k, 160),
        ]

    for name, title, recipes, cols in (
            ("anim-party", "The party", PARTY, 6),
            ("anim-classes", "Class starters", CLASSES, 5),
            ("anim-enemies", "Enemies", ENEMIES, 4)):
        stage_gif(PREVIEWS / f"{name}.gif", title, actors_for(recipes), script(recipes), zoom=4, columns=cols)

    # Idles up close: the exploration loop and the battle stance, everyone breathing and blinking.
    roster = PARTY + CLASSES
    stage_gif(PREVIEWS / "anim-idle.gif", "Idle and battle-ready loops", actors_for(roster),
              [("Idle", [table["idle"] * 4] * len(roster), 250),
               ("Battle ready", [table["ready"] * 4] * len(roster), 200)],
              zoom=4, columns=8)

    # One character walking in all four isometric directions: two drawn facings, two mirrored.
    kara = next(r for r in PARTY if r.id == "kara")
    fr = C.recipe_sheet(kara).frames
    dirs = [Actor("South-east", "front", fr, ANCHOR), Actor("South-west", "front, mirrored", fr, ANCHOR, mirror=True),
            Actor("North-west", "back", fr, ANCHOR), Actor("North-east", "back, mirrored", fr, ANCHOR, mirror=True)]
    walk = table["walk"]
    stage_gif(PREVIEWS / "anim-directions.gif", "Four facings from two drawn ones", dirs,
              [("Walk", [walk * 4, walk * 4, [f + n for f in walk] * 4, [f + n for f in walk] * 4], 125)], zoom=5)


SETS = {"palettes": build_palettes, "characters": build_characters, "anims": build_anim_previews,
        "terrain": build_terrain}

# Sets that live in their own module: parts/<name>.py with a build() function. Each writes under
# content/tactics/base/<name>/ and previews/, using arcpix.out.
PLUGIN_SETS = ("props", "portraits", "beasts", "backgrounds", "ui", "icons", "fx",
               "ambient", "bosses", "emotes", "town", "status", "pickups", "worldmap",
               "signature", "title", "crests", "dungeon", "projectiles", "gear_icons", "grades", "creator_thumbs", "hazards", "transitions", "sheet_icons", "chapters", "showcase", "gallery")   # these read the others: keep them last
for _name in PLUGIN_SETS:
    try:
        _mod = importlib.import_module(f"parts.{_name}")
    except ModuleNotFoundError as e:
        if e.name != f"parts.{_name}":
            raise
        continue
    if hasattr(_mod, "build"):          # a set still being written has no build() yet
        SETS[_name] = _mod.build

if __name__ == "__main__":
    for name in sys.argv[1:] or list(SETS):
        print("building", name)
        SETS[name]()
