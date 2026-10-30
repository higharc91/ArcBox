"""A choreographed battle beat on the Ashen Ford, rendered from the shipped assets only: the pack's FX strips,
combat numbers and banners, and the composed characters. It's the closest thing to the game running, and the best
check that the sets agree on scale, anchors and timing.

Writes previews only (anim-battle.gif and a contact sheet of key frames); nothing goes into the pack.
"""
from __future__ import annotations

import json
import math

from PIL import Image

from arcpix import iso
from arcpix.out import PACK, PREVIEWS, save
from arcpix.preview import Tile, contact_sheet
from . import backgrounds as BG
from . import characters as C
from . import dioramas as D
from .recipes import ALL
from .rig import ANCHOR, ANIMS

FX = json.loads((PACK / "fx" / "manifest.json").read_text())["effects"]
NUM = json.loads((PACK / "ui" / "manifest.json").read_text())["numbers"]
BODY = 15                                  # sprite px above the feet where 'body' effects attach

_strips: dict[str, list[Image.Image]] = {}


def fx_frames(fid: str, variant: str | None = None) -> list[Image.Image]:
    e = FX[fid]
    v = variant or next(iter(e["variants"]))
    key = f"{fid}/{v}"
    if key not in _strips:
        sheet = Image.open(PACK / "fx" / e["variants"][v]).convert("RGBA")
        w, h = e["frame"]
        _strips[key] = [sheet.crop((k * w, 0, (k + 1) * w, h)) for k in range(e["frames"])]
    return _strips[key]


def number(text: str, style: str) -> Image.Image:
    atlas = Image.open(PACK / "ui" / NUM["file"]).convert("RGBA")
    glyphs = NUM["styles"][style]
    if text in glyphs:
        x, y, w, h = glyphs[text]["rect"]
        return atlas.crop((x, y, x + w, y + h))
    parts = [glyphs[ch] for ch in text]
    W = sum(p["advance"] for p in parts) + 2
    H = max(p["rect"][3] for p in parts)
    out = Image.new("RGBA", (W, H))
    x = 0
    for p in parts:
        gx, gy, gw, gh = p["rect"]
        out.alpha_composite(atlas.crop((gx, gy, gx + gw, gy + gh)), (x, 0))
        x += p["advance"]
    return out


def _unit_img(rid: str, anim: str, k: int, facing: str, mirror: bool) -> tuple[Image.Image, tuple[int, int]]:
    r = next(x for x in ALL if x.id == rid)
    poses = ANIMS[anim][2]
    img = C.compose(r, facing, poses[k % len(poses)]).render(r.ramps())
    if mirror:
        return img.transpose(Image.FLIP_LEFT_RIGHT), (img.width - 1 - ANCHOR[0], ANCHOR[1])
    return img, ANCHOR


CAST = {   # id: (tile i, tile j, facing, mirror)
    "kara": (4, 4, "front", False), "aldric": (3, 3, "front", False), "ilyra": (8, 2, "front", True),
    "tamsin": (10, 2, "front", True), "wren": (6, 3, "front", False), "sefa": (1, 3, "front", False),
    "goblin_cutthroat": (5, 4, "back", False), "goblin_spear": (7, 5, "back", True),
    "orc_brute": (5, 8, "back", False), "goblin_shaman": (9, 8, "back", True),
}
TICKS = 78
CAMERA = (170, 130, 650, 400)              # the fight, framed like the game's camera would (then shown at 2x)


def _anim_at(rid: str, t: int) -> tuple[str, int] | None:
    """The script: what each actor plays on tick t (None = removed)."""
    idle = ("idle", (t // 3) % 4)
    ready = ("ready", (t // 3) % 4)
    party = rid in ("kara", "aldric", "ilyra", "tamsin", "wren", "sefa")
    if t >= 62:
        return ("victory", (t // 3) % 3) if party else (None if rid == "goblin_cutthroat" else ("hurt", 1))
    if rid == "kara" and 10 <= t < 20:
        return "attack", (t - 10) // 2
    if rid == "goblin_cutthroat":
        if 15 <= t < 19:
            return "hurt", 0
        if t >= 52:
            return None if t >= 58 else ("down", 0)
    if rid == "ilyra" and 22 <= t < 30:
        return "cast", (t - 22) // 2
    if rid == "goblin_spear" and 34 <= t < 38:
        return "hurt", 0
    if rid == "tamsin" and 40 <= t < 48:
        return "shoot", (t - 40) // 2
    return ready if (party and t >= 8) else idle


def _tile_xyz(scene: iso.Scene, i: float, j: float) -> tuple[float, float, float]:
    return i, j, scene.top_z(int(round(i)), int(round(j)))


def frame(t: int) -> Image.Image:
    scene = D.ashen_ford(t % 24, units=False)
    for rid, (i, j, facing, mirror) in CAST.items():
        a = _anim_at(rid, t)
        if a is None:
            continue
        img, anchor = _unit_img(rid, a[0], a[1], facing, mirror)
        scene.sprites.append(iso.Sprite(img, anchor, i, j))

    def fx_at(fid, variant, k, i, j, attach="body", rotate=0.0, z=None):
        e = FX[fid]
        frames = fx_frames(fid, variant)
        img = frames[min(k, len(frames) - 1)]
        ax, ay = e["anchor"]
        if rotate:
            img = img.rotate(rotate, resample=Image.NEAREST, expand=True)
            ax, ay = img.width // 2, img.height // 2
        if attach == "body":
            ay += BODY
        scene.sprites.append(iso.Sprite(img, (ax, ay), i + 0.01, j + 0.01, shadow=False, z=z))

    def num_at(text, style, i, j, since):
        img = number(text, style)
        img = img.resize((img.width, img.height), Image.NEAREST)
        rise = min(since, 4) * 3
        scene.sprites.append(iso.Sprite(img, (img.width // 2, img.height + 36 + rise), i + 0.02, j + 0.02,
                                        scale=2, shadow=False))

    def projectile(fid, variant, src, dst, t0, dur, t_, arc=0.0):
        s = (t_ - t0) / dur
        si, sj, sz = _tile_xyz(scene, *src)
        di, dj, dz = _tile_xyz(scene, *dst)
        i, j = si + (di - si) * s, sj + (dj - sj) * s
        z = sz + (dz - sz) * s + BODY * 2 + math.sin(s * math.pi) * arc
        dx = ((di - dj) - (si - sj)) * 32
        dy = ((di + dj) - (si + sj)) * 16 - (dz - sz)
        ang = -math.degrees(math.atan2(dy, dx))
        frames = fx_frames(fid, variant)
        img = frames[(t_ - t0) % len(frames)].rotate(ang, resample=Image.NEAREST, expand=True)
        scene.sprites.append(iso.Sprite(img, (img.width // 2, img.height // 2), i, j, shadow=False, z=z))

    # Kara's swing lands on the cutthroat
    if 14 <= t < 21:
        fx_at("slash", "steel", t - 14, 5, 4)
    if 15 <= t < 26:
        num_at("-9", "damage", 5, 4, t - 15)
    # Ilyra's fireball
    if 28 <= t < 34:
        projectile("fireball", "fire", (8, 2), (7, 5), 28, 6, t, arc=10)
    if 34 <= t < 43:
        fx_at("explosion", "fire", t - 34, 7, 5, attach="feet")
    if 35 <= t < 46:
        num_at("-14", "damage", 7, 5, t - 35)
    # Tamsin's critical arrow
    if 46 <= t < 51:
        projectile("arrow", "oak", (10, 2), (5, 4), 46, 5, t, arc=6)
    if 51 <= t < 59:
        fx_at("crit", "gold", t - 51, 5, 4)
    if 51 <= t < 62:
        num_at("-27", "crit", 5, 4, t - 51)
    if 55 <= t < 62:
        fx_at("poof", "white", t - 55, 5, 4, attach="feet")

    img = iso.render(scene, backdrop=lambda w, h: BG.compose("day", "hills", w, h, horizon=int(h * 0.48)))
    full = iso.finish(img, zoom=1).convert("RGBA")
    out = full.crop(CAMERA).resize(((CAMERA[2] - CAMERA[0]) * 2, (CAMERA[3] - CAMERA[1]) * 2), Image.NEAREST)
    # banners
    banner = None
    if t < 9:
        banner, slide = "your_turn", max(0, 3 - t) * 12
    elif t >= 62:
        banner, slide = "victory", max(0, 65 - t) * 12
    if banner:
        b = Image.open(PACK / "title" / f"{banner}.png").convert("RGBA")
        b = b.resize((b.width * 3, b.height * 3), Image.NEAREST)
        out.alpha_composite(b, ((out.width - b.width) // 2, 24 - slide * 2))
    return out


def build() -> None:
    frames = [frame(t) for t in range(TICKS)]
    pal = [f.convert("RGB").quantize(colors=255, method=Image.Quantize.MEDIANCUT) for f in frames]
    pal[0].save(PREVIEWS / "anim-battle.gif", save_all=True, append_images=pal[1:], duration=95, loop=0,
                optimize=True)
    keys = [(4, "Your turn"), (15, "Kara's swing"), (30, "Fireball"), (36, "Explosion"), (52, "Critical arrow"),
            (66, "Victory")]
    save(contact_sheet("A battle beat, from the shipped assets", [Tile(frames[t], label) for t, label in keys],
                       columns=2, zoom=1), PREVIEWS / "30-showcase.png")
