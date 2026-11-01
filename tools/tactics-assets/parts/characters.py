"""Builds the layered character sprites: every part x every build, as recolourable ramp-map sheets, plus recipes
(complete characters = a build + one variant per layer + a palette) rendered as ready-to-use previews.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from functools import lru_cache

import numpy as np
from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.palette import FAMILIES, RAMP_LEN
from arcpix.sheet import Sheet, Anim
from . import character_parts as P
from .rig import BUILDS, DIRECTIONS, FRAME_W, FRAME_H, ANCHOR, ANIMS, ANIM_ORDER, frame_list, points

SLOTS = SlotTable()
for _s in ("skin", "eyes", "hair", "cloth1", "cloth2", "trim", "metal", "leather", "wood", "magic", "horn", "paint", "blade"):
    SLOTS.id(_s)

# layer -> variant -> part factory
PARTS: dict[str, dict[str, P.Part]] = {
    "cape": {k: P.cape(k) for k in ("short_cape", "cloak", "pelt", "quiver", "banner_back")},
    "body": {k: P.body(k) for k in ("human", "elf", "half_elf", "gnome", "orc", "dragon", "goblin", "skeleton")},
    "tail": {k: P.tail(k) for k in ("devil", "dragon")},
    "legs": {k: P.legs(k) for k in ("pants", "greaves", "shorts", "skirt", "kilt")},
    "feet": {k: P.feet(k) for k in ("boots", "sabatons", "sandals")},
    "torso": {k: P.torso(k) for k in ("tunic", "vest", "chain", "plate", "robe", "vestments", "coat", "rags",
                                         "leather", "scale", "tabard", "gi",
                                         "apron", "dress", "doublet", "harness", "silk_robe", "drake_robe",
                                         "motley_robe")},
    "beard": {k: P.beard(k) for k in ("short", "full", "braided", "goatee", "moustache", "stubble")},
    "hair": {k: P.hair(k) for k in ("short", "messy", "long", "ponytail", "bun", "mohawk", "braids", "cropped",
                                    "curly", "pixie", "topknot", "twintails")},
    "marks": {k: P.marks(k) for k in ("freckles", "scar", "warpaint", "tattoo", "stripes", "scales", "runes")},
    "horns": {k: P.horns(k) for k in ("curled", "straight", "ram", "small", "swept")},
    "headgear": {k: P.headgear(k) for k in ("open_helm", "great_helm", "hood", "wizard_hat", "circlet", "bandana", "crown",
                                               "horned_helm", "feathered_cap", "mask_hood", "winged_helm", "halo",
                                               "beret", "antlers", "flower_crown", "headband", "straw_hat",
                                               "cap", "mitre", "wolf_pelt")},
    "weapon": {k: P.weapon(k) for k in ("sword", "greatsword", "axe", "mace", "dagger", "spear", "staff", "bow", "tome", "wand", "rapier", "lute", "warhammer",
                                           "halberd", "flail", "scimitar", "quarterstaff", "crossbow",
                                           "hourglass_staff", "crystal_staff", "holy_staff", "storm_staff",
                                           "living_staff", "lyre", "pitchfork", "smith_hammer", "broom",
                                           "tankard", "scroll", "greataxe", "maul", "spellfire", "wild_motes",
                                           "rune_staff")},
    "hands": {k: P.hands(k) for k in ("bare", "gloves", "gauntlets", "wraps", "rune_marked")},
    "shoulders": {k: P.shoulders(k) for k in ("drake_pauldrons", "rune_stones")},
    "float": {k: P.float_(k) for k in ("chaos_orb",)},
    "offhand": {k: P.offhand(k) for k in ("round_shield", "kite_shield", "buckler", "lantern", "orb",
                                              "tower_shield", "blade", "dagger", "main_gauche", "bomb",
                                              "war_horn", "hand_axe", "spirit_totem")},
}

# Draw order per facing (first = bottom). In the front view the cloak hangs behind the body and the off-hand
# (shield) is held in front; from behind it is the other way round.
ORDER: dict[str, list[str]] = {
    "front": ["float", "tail", "cape", "body", "marks", "legs", "feet", "torso", "beard", "hair", "shoulders", "headgear",
              "horns", "weapon", "hands", "offhand"],
    "back": ["float", "offhand", "body", "marks", "legs", "feet", "torso", "tail", "weapon", "hands", "cape", "beard", "hair",
             "shoulders", "headgear", "horns"],
}

# Slots that get automatic volume shading per layer (others are drawn with explicit shades).
VOLUME_SLOTS = ("skin", "hair", "cloth1", "cloth2", "metal", "leather", "wood", "trim", "blade")
# Horns are excluded on purpose: they're small, and drawn with explicit shades.


def render_layer(layer: str, variant: str, build: str, direction: str, pose) -> Canvas:
    """One layer, shaded, *without* an outline: outlines are applied once to the composed character, otherwise
    every upper layer's outline would paint over the layer beneath it (eyes under a fringe, hands on a torso).

    Cached: a layer looks the same wherever the same pose recurs (presets, previews, held frames), and fields the
    layer can't see are dropped from the key first. Treat the result as read-only."""
    return _render_layer(layer, variant, build, direction, P.pose_key(layer, variant, pose))


@lru_cache(maxsize=60000)
def _render_layer(layer: str, variant: str, build: str, direction: str, pose) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    pts = points(BUILDS[build], pose, direction)
    PARTS[layer][variant](c, pts)
    loose = P.loose_rows(layer, variant, pts)
    if loose and pose.drag != (0, 0):
        _trail(c, *loose, pose.drag[0] * pts.near_sign, pose.drag[1])
    if pose.tilt or pose.shift:
        _topple(c, pts)
    for s in VOLUME_SLOTS:
        _shade_form(c, s)
    return c


def _runs(m: np.ndarray, axis: int) -> tuple[np.ndarray, np.ndarray]:
    """For every pixel of mask m: its index within its run along `axis`, and that run's length."""
    a = m if axis == 1 else m.T
    h, w = a.shape
    left = np.zeros((h, w), np.int32)
    right = np.zeros((h, w), np.int32)
    acc = np.zeros(h, np.int32)
    for x in range(w):
        acc = np.where(a[:, x], acc + 1, 0)
        left[:, x] = acc
    acc = np.zeros(h, np.int32)
    for x in range(w - 1, -1, -1):
        acc = np.where(a[:, x], acc + 1, 0)
        right[:, x] = acc
    k, n = left - 1, left + right - 1
    return (k, n) if axis == 1 else (k.T, n.T)


def _shade_form(c: Canvas, slot: str, base: int = 3) -> None:
    """Canvas.shade_form, vectorised (the layer loop calls it ~2 million times a build). Same rule, same result:
    across each horizontal run the left 30% gets +1 and the right third -1; down vertical runs of 4+ the top 16%
    gets +1 and the bottom 18% -1; only flat, unflagged base-shade pixels change."""
    m = c.slot == SLOTS.id(slot)
    flat = m & (c.shade == base) & (c.flags == 0)
    if not flat.any():
        return
    k, n = _runs(m, 1)
    t = (k + 0.5) / np.maximum(n, 1)
    dx = np.where((n >= 2) & (t < 0.3), 1, np.where((n >= 2) & (t > 0.68), -1, 0))
    k, n = _runs(m, 0)
    t = (k + 0.5) / np.maximum(n, 1)
    dy = np.where(n >= 4, np.where(t < 0.16, 1, np.where(t > 0.82, -1, 0)), 0)
    delta = np.clip(dx + dy, -2, 1)
    c.shade = np.where(flat, np.clip(base + delta, 0, 5), c.shade).astype(np.uint8)


def _trail(c: Canvas, pivot: int, span: int, follows_y: bool, dx: int, dy: int) -> None:
    """Secondary motion: rows below `pivot` lag the body. Each row slides sideways in proportion to its distance
    below the pivot (full `dx` at `span` rows), so a lock of hair or a cloak bends rather than breaking; then, for
    parts that follow drag y, the free part lifts (dy < 0) or drops (dy > 0) as a whole, stretching at the pivot."""
    arrs = [c.slot, c.shade, c.flags]
    h, w = c.slot.shape
    if dx:
        for y in range(max(0, pivot + 1), h):
            k = int(math.floor(dx * min(1.0, (y - pivot) / span) + 0.5))
            if not k:
                continue
            for a in arrs:
                row = a[y].copy()
                a[y] = 0
                if k > 0:
                    a[y, k:] = row[:w - k]
                else:
                    a[y, :w + k] = row[-k:]
    if dy and follows_y:
        top = max(0, pivot + 1)
        src = [a[top:].copy() for a in arrs]
        n = h - top
        if dy < 0 and top >= -dy:                    # lift: the free part rises over the pivot rows
            k = -dy
            moved = src[0] > 0
            for a, part in zip(arrs, src):
                a[top:] = 0
                dst = a[top - k:h - k]
                dst[moved] = part[moved]
        elif dy > 0:                                 # drop: everything below moves down, the pivot row stretches
            for a, part in zip(arrs, src):
                a[top + dy:] = part[:n - dy]
                a[top:top + dy] = part[0]


def _topple(c: Canvas, p) -> None:
    """Whole-body moves the keypoints can't express: a slide along x (`shift`) and a fall backwards (`tilt`, about
    the hips, landing the body on the ground in the middle of the frame). Nearest-neighbour, so pixels stay crisp;
    the volume shading runs afterwards, so a body lying down is still lit from the top-left."""
    pose, b = p.pose, p.build
    s = p.near_sign
    ang = math.radians(pose.tilt) * s
    py = p.torso_bot
    px = p.mx(16)
    lift = math.sin(abs(ang))
    head_len = py - p.head_y0                      # how far the upper body reaches above the pivot
    legs = b.foot_y + 4 - py                       # and the folded legs below it, toes included
    tx = s * (pose.shift + lift * (head_len - legs) / 2)
    ty = lift * (b.foot_y - 4 - py)
    h, w = c.slot.shape
    ys, xs = np.mgrid[0:h, 0:w]
    qx = xs + 0.5 - (px + 0.5) - tx
    qy = ys + 0.5 - (py + 0.5) - ty
    ca, sa = math.cos(ang), math.sin(ang)
    # inverse of the forward map (x, y) -> (x cos + y sin, -x sin + y cos): rotate the other way
    sx = qx * ca - qy * sa + px + 0.5
    sy = qx * sa + qy * ca + py + 0.5
    ix, iy = np.floor(sx).astype(int), np.floor(sy).astype(int)
    ok = (ix >= 0) & (ix < w) & (iy >= 0) & (iy < h)
    ixc, iyc = np.clip(ix, 0, w - 1), np.clip(iy, 0, h - 1)
    for name in ("slot", "shade", "flags"):
        a = getattr(c, name)
        setattr(c, name, np.where(ok, a[iyc, ixc], 0).astype(np.uint8))


def layer_frames(layer: str, variant: str, build: str) -> list[Canvas]:
    """All frames for both facings: front row then back row, ANIM_ORDER within a row."""
    out: list[Canvas] = []
    for d in DIRECTIONS:
        for _anim, _i, pose in frame_list():
            out.append(render_layer(layer, variant, build, d, pose))
    return out


def frames_per_direction() -> int:
    return len(frame_list())


def anim_table() -> list[Anim]:
    """Animations in sheet terms (front row; the back row is the same indices + frames_per_direction())."""
    anims, i = [], 0
    for name in ANIM_ORDER:
        fps, loop, poses = ANIMS[name]
        anims.append(Anim(name, list(range(i, i + len(poses))), fps, loop))
        i += len(poses)
    return anims


@dataclass
class Recipe:
    id: str
    name: str
    build: str
    layers: dict[str, str]                         # layer -> variant (omit a layer to leave it empty)
    palette: dict[str, str]                        # slot -> "family/ramp"
    tags: list[str] = field(default_factory=list)
    race: str | None = None                        # creator picks (None for enemies authored directly)
    cls: str | None = None

    def __post_init__(self) -> None:
        # Weapons have their own metal; unless a palette says otherwise it matches the armour.
        self.palette.setdefault("blade", self.palette.get("metal", "metal/steel"))

    def ramps(self) -> dict[str, list[str]]:
        out = {}
        for slot, ref in self.palette.items():
            fam, name = ref.split("/")
            out[slot] = FAMILIES[fam][name]
        return out


def compose(recipe: Recipe, direction: str, pose) -> Canvas:
    """Stacks the recipe's layers in draw order, then outlines the silhouette once. This is exactly what the game
    does when a character is created or changes gear (see README, "Composing a character")."""
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for layer in ORDER[direction]:
        v = recipe.layers.get(layer)
        if v:
            _over(c, render_layer(layer, v, recipe.build, direction, pose))
    c.outline()
    return c


def _over(dst: Canvas, src: Canvas) -> None:
    """Canvas.blit at zero offset, vectorised: the same "non-empty pixels win" rule, for the hot compose loop."""
    m = src.slot > 0
    dst.slot[m] = src.slot[m]
    dst.shade[m] = src.shade[m]
    dst.flags[m] = src.flags[m]


_SHEETS: dict = {}


def _recipe_key(recipe: Recipe) -> tuple:
    return (recipe.id, recipe.build, tuple(sorted(recipe.layers.items())), tuple(sorted(recipe.palette.items())))


def _sheet_frames(recipe: Recipe) -> list[Image.Image]:
    ramps = recipe.ramps()
    return [compose(recipe, d, pose).render(ramps) for d in DIRECTIONS for _a, _i, pose in frame_list()]


def recipe_sheet(recipe: Recipe) -> Sheet:
    """A recipe's baked sheet. Memoised: the presets, rosters and animation previews all ask for the same ones."""
    key = _recipe_key(recipe)
    if key not in _SHEETS:
        _SHEETS[key] = _sheet_frames(recipe)
    s = Sheet(FRAME_W, FRAME_H, frames_per_direction(), ANCHOR, anims=anim_table())
    s.frames = list(_SHEETS[key])
    return s


def _pool():
    """A fork pool for the heavy loops (every layer sheet, every preset). Each job is independent and results come
    back in order, so the output is byte-identical to a serial build."""
    import multiprocessing as mp
    import os
    return mp.get_context("fork").Pool(max(1, min(4, os.cpu_count() or 1)))


def warm_sheets(recipes) -> None:
    """Bakes many recipe sheets in parallel into the memo that recipe_sheet reads."""
    todo = [r for r in recipes if _recipe_key(r) not in _SHEETS]
    if not todo:
        return
    with _pool() as pool:
        for r, frames in zip(todo, pool.imap(_sheet_frames, todo)):
            _SHEETS[_recipe_key(r)] = frames


def _layer_sheet(job) -> None:
    from arcpix.out import save
    layer, variant, build, path = job
    s = Sheet(FRAME_W, FRAME_H, frames_per_direction(), ANCHOR)
    s.frames = [cv.ramp_map() for cv in layer_frames(layer, variant, build)]
    save(s.image(), path)


def save_layer_sheets(jobs) -> None:
    """Writes the ramp-map sheet for every (layer, variant, build, path) job, in parallel (PNG optimisation is
    as slow as the drawing, so the workers save too)."""
    with _pool() as pool:
        for _ in pool.imap_unordered(_layer_sheet, jobs, chunksize=2):
            pass


def lut(palettes: dict[str, dict[str, str]]) -> tuple[Image.Image, list[str]]:
    """The palette lookup texture for the runtime recolour shader: one row per palette, 16 slots x 8 shades
    (128 px). Pixel (slot*8 + shade, row) is that slot's colour; unused cells stay transparent."""
    names = list(palettes)
    img = Image.new("RGBA", (16 * 8, len(names)), (0, 0, 0, 0))
    for row, name in enumerate(names):
        for slot, ref in palettes[name].items():
            fam, rname = ref.split("/")
            ramp = FAMILIES[fam][rname]
            sid = SLOTS.id(slot)
            for shade in range(RAMP_LEN):
                h = ramp[shade].lstrip("#")
                img.putpixel((sid * 8 + shade, row), (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255))
    return img, names
