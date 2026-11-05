"""Locally authored Ashen Ford architecture, built from the production town materials.

These are original pixel geometry/material functions, not repainted concept sheets.
Export into a preview pack until the owner approves the TV art/placement review.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

from . import town as T
from arcpix.out import save, write_json


def _rod(m, a, b, material="wood/dark", radius=0.45):
    """A thin physical cord/twig with a square cross-section, attached in world space."""
    a, b = np.array(a, float), np.array(b, float)
    axis = b - a
    axis /= np.linalg.norm(axis)
    guide = np.array([0., 1., 0.]) if abs(axis[1]) < 0.9 else np.array([1., 0., 0.])
    u = np.cross(axis, guide); u = u / np.linalg.norm(u) * radius
    v = np.cross(axis, u); v = v / np.linalg.norm(v) * radius
    ring = [u + v, -u + v, -u - v, u - v]
    part = m.part()
    for i in range(4):
        p, q = ring[i], ring[(i + 1) % 4]
        normal = p + q
        m.poly([a + p, b + p, b + q, a + q], T.solid(material, 3), normal,
               part=part, cast=False)


def _granary_base(a, b, p):
    out = T.V_WALL(("fieldstone", "grey"))(a, b, p)
    x, z = p[:, 0], p[:, 2]
    door = (x >= 27) & (x <= 66) & (z >= 5) & (z <= 52)
    wood = np.array([T._hex_rgb(c) for c in T._ref("wood/oak")])
    planks = np.floor((x - 27) / 5).astype(int)
    shade = np.where((np.floor(x - 27) % 5 == 0) | (abs(x - 46.5) < .8), 1,
                     np.where((planks * 7 + np.floor(z / 9)) % 11 == 0, 2, 3))
    out[door, :3] = wood[shade[door]]
    frame = (((x >= 24) & (x < 27) | (x > 66) & (x <= 69)) & (z <= 55)) | (
        (x >= 24) & (x <= 69) & (z > 52) & (z <= 55))
    out[frame, :3] = wood[2]
    iron = np.array([T._hex_rgb(c) for c in T._ref("metal/iron")])
    for level in (15, 44):
        strap = door & (z >= level) & (z <= level + 2) & (((x >= 29) & (x <= 41)) | ((x >= 52) & (x <= 64)))
        out[strap, :3] = iron[2]
        rivet = strap & (abs(z - level - 1) < .6) & ((abs(x - 31) < .7) | (abs(x - 39) < .7) |
                                                                   (abs(x - 54) < .7) | (abs(x - 62) < .7))
        out[rivet, :3] = iron[4]
    return out


def _loft(base, front=False):
    def paint(a, b, p):
        out = base(a, b, p)
        across, z = (p[:, 0] if front else p[:, 1]), p[:, 2]
        for centre in ((20, 46, 73) if front else (19, 43)):
            vent = (abs(across - centre) <= 4) & (z >= 66) & (z <= 82)
            out[vent, :3] = T._hex_rgb(T._ref("wood/dark")[0])
            louvre = vent & (np.floor(z) % 4 == 1)
            out[louvre, :3] = T._hex_rgb(T._ref("wood/dark")[3])
        return out
    return paint


def granary():
    m = T.Model(3, 2)
    stone = ("fieldstone", "grey")
    oak = T.solid("town/timber", 3, grain=True)
    m.box(3, 91, 4, 59, 0, 55, _granary_base, None, side_u=T.U_WALL(stone))
    m.box(3, 91, 4, 59, 55, 92,
          _loft(T.V_WALL(("timber_frame", "ochre"), z0=55), True), None,
          side_u=_loft(T.U_WALL(("timber_frame", "ochre"), z0=55)))
    # Broad oak sill, corner posts, and roof gable keep this a grain barn, not a town house.
    m.box(2, 92, 3, 60, 54, 58, oak, T.solid("town/timber", 4))
    for x in (3, 89):
        m.box(x, x + 2, 58, 61, 56, 92, oak, None)
    T.gable(m, 3, 91, 4, 59, 92, 29, axis="v", roof=("shakes", "grey"),
            gable_fn=T._gable_wall(("timber_frame", "ochre"), z0=55), oh=4, ohg=4)
    # A real hasp bridges the two door leaves; the shackle rests on it.
    iron = T.solid("metal/iron", 3)
    m.box(39, 56, 59.3, 60.5, 31, 33, iron, T.solid("metal/iron", 4))
    for x in (46, 50):
        m.box(x, x + 1, 60.6, 61.6, 33, 36, iron, None)
    m.box(46, 51, 60.6, 61.6, 36, 37, iron, T.solid("metal/iron", 5))
    def lock(a, b, p):
        out = T.solid("metal/iron", 3)(a, b, p)
        key = (abs(p[:, 0] - 48) < .8) & (p[:, 2] > 27) & (p[:, 2] < 31)
        out[key, :3] = T._hex_rgb(T._ref("wood/dark")[0])
        return out
    m.box(45, 51.5, 61.8, 63.5, 27, 33, lock, iron, side_u=iron)
    # Small hoist, its brace, pulley and rope; no wealth of overflowing grain.
    m.box(45, 49, 57, 75, 101, 105, oak, T.solid("town/timber", 4))
    _rod(m, (47, 59, 92), (47, 71, 102), radius=.8)
    _rod(m, (47, 73, 101), (47, 73, 84), "prop/hay", .35)
    m.blobs.append(T.Blob((47, 73, 99), 2.4, T._ref("wood/dark"), part=m.part()))
    m.box(25, 69, 59, 64, 0, 3, T.V_WALL(stone), T.flat_top(("flagstone", "grey")))
    m.attach["door"] = [(47, 63, 0)]
    return m


def _herb_bundle(m, x, y, z, index=0):
    _rod(m, (x, y, z + 1), (x, y, z - 7), "prop/hay", .25)
    leaf = T._ref("prop/hay" if index == 1 else "foliage/leaf_light")
    for k in range(5):
        zz = z - 5 - k * 1.8
        width = 1.6 + k * .46
        for side in (-1, 1):
            end = (x + side * width, y + .7, zz - 2)
            _rod(m, (x, y, zz + 1), end, "wood/dark", .22)
            m.blobs.append(T.Blob(end, 1.6 if index == 1 else 1.9, leaf,
                                  part=m.part(), squash=.7))
    _rod(m, (x - 1.5, y + .3, z - 4), (x + 1.5, y + .3, z - 4), "prop/hay", .25)


def _charms(m, x, y, z):
    _rod(m, (x, y, z), (x, y, z - 16), "prop/hay", .25)
    for k in range(3):
        zz = z - 5 - k * 4
        _rod(m, (x - 2.5, y + .4, zz - 2), (x + 2, y + .4, zz + 1), radius=.4)
        m.blobs.append(T.Blob((x + (1 if k % 2 else -1), y + .6, zz - 3), .9,
                              T._ref("town/shake"), part=m.part()))


def maud_hut():
    m = T.cottage(dict(wall=("whitewash", "ochre"), roof=("thatch", "old"), trim="green"))
    # Preserve geometry and ground anchor. Surface soot belongs only to the ordinary chimney.
    for face in m.faces:
        if face.fn is None:
            continue
        original = face.fn
        def weather(a, b, p, original=original):
            out = original(a, b, p)
            near = (p[:, 0] >= 55) & (p[:, 1] >= 12) & (p[:, 1] <= 28) & (p[:, 2] > 72)
            taper = np.clip((p[:, 2] - 72) / 30, 0, 1)
            streak = .7 + .3 * (np.floor(p[:, 0] + p[:, 1] * 1.7) % 5 / 4)
            out[near, :3] *= (1 - .58 * taper[near] * streak[near])[:, None]
            return out
        face.fn = weather
    # Stone footing sits beside the doorway, not over its threshold.
    m.poly([(36, 59.2, 0), (57, 59.2, 0), (57, 59.2, 15), (36, 59.2, 15)],
           T.V_WALL(("fieldstone", "warm")), (0, 1, 0))
    m.poly([(57.2, 59, 0), (57.2, 5, 0), (57.2, 5, 15), (57.2, 59, 15)],
           T.U_WALL(("fieldstone", "warm")), (1, 0, 0))
    for i, x in enumerate((8, 40, 53)):
        _herb_bundle(m, x, 65.8, 61, i)
    _charms(m, 4.5, 63, 44)
    # The scene's existing smoke overlay stays separate from the pixel building.
    return m


def ash_garden():
    m = T.Model(1, 2)
    def soil(a, b, p):
        n = (np.floor(p[:, 0] * 3) + np.floor(p[:, 1] * 7)).astype(int)
        colors = np.array([T._hex_rgb(c) for c in T._ref("prop/soot")])
        shade = 2 + (n % 13 < 4) - (n % 17 == 0)
        return np.c_[colors[shade], np.full(len(a), 255.)].astype(np.float32)
    m.box(4, 28, 4, 60, 0, 1.2, soil, soil)
    # Hand-laid irregular stones follow the grid's own projection, with no luminous ash.
    rng = np.random.default_rng(810207)
    for x, y in [(3, y) for y in range(5, 62, 8)] + [(29, y) for y in range(5, 62, 8)] + [
            (x, y) for x in range(9, 29, 8) for y in (3, 62)]:
        m.blobs.append(T.Blob((x, y, 2), float(rng.uniform(2.6, 3.5)), T._ref("terrain/stone"),
                              part=m.part(), squash=.62))
    for i, (x, y) in enumerate(((11, 13), (21, 23), (12, 35), (21, 48), (11, 55))):
        for k in range(3):
            angle = k * 2.2 + i
            end = (x + math.cos(angle) * 3, y + math.sin(angle) * 2, 5 + (k % 2) * 2)
            _rod(m, (x, y, 1), end, "wood/dark", .28)
            m.blobs.append(T.Blob(end, 1.7, T._ref("foliage/leaf"), part=m.part(), squash=.65))
    return m


def stool_pots():
    m = T.Model(1, 1)
    oak = T.solid("wood/oak", 3, grain=True)
    for x, y in ((6, 7), (17, 7), (12, 16)):
        m.box(x, x + 1.7, y, y + 1.7, 0, 10, oak, None)
    T._cylinder(m, 12, 10, 10, [(7, 0), (7, 2)], oak, T.solid("wood/oak", 4), sides=12)
    for x, y, r, h in ((24, 14, 4, 11), (19, 25, 3, 6), (27, 26, 3.8, 8)):
        T._cylinder(m, x, y, 0, [(r * .65, 0), (r, h * .5), (r * .7, h - 2), (r * .85, h)],
                    T.solid("town/clay", 3), T.solid("wood/dark", 0), sides=12)
        T._cylinder(m, x, y, h - 1, [(r * .85, 0), (r * .85, 1)], T.solid("town/clay", 4),
                    T.solid("wood/dark", 0), sides=12)
    # A knee-high stool and kitchen pots, not furniture at the character's shoulder height.
    centre = np.array([16., 16., 0.])
    scale = .72
    for face in m.faces:
        face.o = centre + (face.o - centre) * scale
        face.rings = [[(a * scale, b * scale) for a, b in ring] for ring in face.rings]
    return m


BUILDERS = {"granary": granary, "maud_hut": maud_hut}
PROP_BUILDERS = {"maud_ash_garden": ash_garden, "maud_stool_pots": stool_pots}


def export(pack: Path):
    """Add just these assets to an existing pack. Call on a preview copy before approval."""
    town_path, props_path = pack / 'town/manifest.json', pack / 'props/manifest.json'
    town = json.loads(town_path.read_text())
    props = json.loads(props_path.read_text())
    outputs = {}
    for name, make in BUILDERS.items():
        model = make()
        rendered = T.render(model)
        image = f'buildings/{name}_ashen_ford.png'
        shadow = f'buildings/{name}_ashen_ford_shadow.png'
        save(rendered.image, pack / 'town' / image)
        save(rendered.shadow, pack / 'town' / shadow)
        entry = {"variants": {"ashen_ford": {"image": image, "shadow": shadow}},
                 "size": list(rendered.image.size), "frames": 1, "fps": 0.,
                 "footprint": [model.fw, model.fd], "origin": list(rendered.origin),
                 "anchor": list(rendered.anchor), "height": T._height_steps(model),
                 "blocks": [[1] * model.fw for _ in range(model.fd)], "walkable": False,
                 "cover": "full", "tags": ["lore", "home" if name == 'maud_hut' else "storage"],
                 "attach": {k: [list(p) for p in ps] for k, ps in rendered.attach.items()}}
        # The granary has no artificial glow; Maud's shutters stay dark, with external ordinary light.
        town['buildings'][name] = entry
        outputs[name] = {"image": str(pack / 'town' / image), **entry}
    for name, make in PROP_BUILDERS.items():
        model = make()
        rendered = T.render(model)
        filename = f'{name}_ashen_ford.png'
        save(rendered.image, pack / 'props' / filename)
        entry = {"variants": {"ashen_ford": filename}, "size": list(rendered.image.size),
                 "anchor": list(rendered.anchor), "frames": 1, "fps": 0., "blocks": False,
                 "cover": "none", "height": 1, "tags": ["lore", "dressing"]}
        props['props'][name] = entry
        outputs[name] = {"image": str(pack / 'props' / filename), **entry}
    write_json(town, town_path)
    write_json(props, props_path)
    return outputs
