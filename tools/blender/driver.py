"""The 22 driver figures for the podium: drivers/<CODE>.glb (each <= 40k triangles).

Built from data/drivers.json (likeness traits, each with a confidence) and grid2026.json (team, number, nationality).
Figure frame (Godot convention): metres, +Y up, feet on y = 0, facing +Z, +X to the figure's left. The rest pose holds
the champagne bottle upright in front of the chest, right hand round its body, left hand under its base.

Nodes (Godot reads them; see Redline.Presentation/Models/DriverModel.cs):
  legs                      legs and boots (static)
  spine                     empty at the waist; parent of everything above it
    torso                   suit torso with collar, belt and the nationality flag patch
    number_suit, name_suit  empties for the Label3D number (right chest) and surname (left chest): +Z out, +Y up
    head                    empty at the top of the neck
      face                  skin, eyes, brows, ears, facial hair
      hair                  the hair that shows under the cap;  hair_top: the rest (hidden while the cap is on)
      locs                  (locs styles only)
      cap                   team cap;  number_cap: empty for the number on its front panel
    arm_r / arm_l           empties at the shoulders (Godot rotates them for the spray pose)
      forearm_r / forearm_l empties at the elbows
        hand_r / hand_l     empties at the wrists
          bottle            the champagne bottle (child of hand_r), origin at its base, +Y up its axis; the mouth
                            is at (0, 0.48, 0) like BottleMesh
Every mesh node is named <part> (the mesh) under its empty, e.g. arm_r/arm_r_mesh.
Material slots: skin lips mouth stubble eyes iris pupil brows hair beard (per driver, as exported);
suit_primary suit_secondary belt gloves boots cap cap_brim bottle_foil (Godot binds team colours);
bottle_glass bottle_label bottle_band flag_1..flag_4 (as exported).
"""
import json
import math
import os

import bpy
from mathutils import Matrix, Vector

from lib import asset, blend, export, materials, studio
from lib import body as bd
from lib import cap as cp
from lib import figure as fg
from lib import hair as hr
from lib import head as hd
from lib.colour import linear_to_srgb_hex, srgb_to_linear
from lib.flags import FLAGS
from lib.frame import facing
from lib.validate import Contract

GROUP = "drivers"
DATA = os.path.join(asset.TOOLS_DIR, "data", "drivers.json")
BUDGET = 40_000

NODES = ("legs", "spine", "torso", "head", "face", "hair", "cap", "arm_r", "forearm_r", "hand_r", "bottle",
         "arm_l", "forearm_l", "hand_l", "number_suit", "name_suit", "number_cap")
SLOTS = ("skin", "hair", "suit_primary", "suit_secondary", "cap", "eyes", "iris", "gloves", "boots", "belt",
         "cap_brim", "bottle_glass", "bottle_foil", "flag_1")


def data():
    with open(DATA, encoding="utf-8") as f:
        return json.load(f)["drivers"]


def seats():
    """[(code, driver, team)] in grid order."""
    out = []
    for t in asset.grid()["teams"]:
        for d in t["drivers"]:
            out.append((d["code"], d, t))
    return out


def _v(trait, default=None):
    return trait.get("value", default) if isinstance(trait, dict) else (trait if trait is not None else default)


def mix(a, b, t):
    ca, cb = srgb_to_linear(a), srgb_to_linear(b)
    return linear_to_srgb_hex(tuple(ca[i] + (cb[i] - ca[i]) * t for i in range(3)))


def define_materials(t, team, nationality):
    skin = _v(t["skin"])
    hair = _v(t["hair"]["colour"])
    fh = t.get("facial_hair", {})
    fh_style = _v(fh, "clean")
    fh_col = fh.get("colour", hair)
    materials.define("skin", skin, roughness=0.52)
    materials.define("lips", mix(skin, "#9a3f48", 0.42), roughness=0.38)
    materials.define("mouth", mix(skin, "#2a1212", 0.8), roughness=0.6)
    k = {"light_stubble": 0.16, "stubble": 0.3}.get(fh_style, 0.22)
    materials.define("stubble", mix(skin, fh_col, k), roughness=0.62)
    materials.define("stubble_edge", mix(skin, fh_col, k * 0.5), roughness=0.58)
    materials.define("eyes", "#e9e4dc", roughness=0.12)
    materials.define("iris", _v(t["eye_colour"]), roughness=0.12)
    materials.define("pupil", "#060606", roughness=0.08)
    materials.define("lashes", mix(skin, "#140d0a", 0.85), roughness=0.7)
    materials.define("jewellery", "#e8e8ec", metallic=1.0, roughness=0.12)
    materials.define("brows", t.get("brows", {}).get("colour", hair), roughness=0.7)
    materials.define("hair", hair, roughness=0.5)
    materials.define("beard", fh_col, roughness=0.68)
    lv = team["livery"]
    materials.define("suit_primary", lv["top"], roughness=0.62)
    materials.define("suit_secondary", lv["side"] if lv["side"] != lv["top"] else lv["endplate"], roughness=0.62)
    materials.define("belt", mix(lv["side"], "#000000", 0.25), roughness=0.55)
    materials.define("gloves", lv["side"], roughness=0.6)
    materials.define("boots", "#18181b", roughness=0.5)
    materials.define("cap", lv["top"], roughness=0.68)
    materials.define("cap_brim", lv["side"], roughness=0.6)
    materials.define("bottle_glass", "#163a24", metallic=0.1, roughness=0.06)
    materials.define("bottle_label", "#efe6cf", roughness=0.5)
    materials.define("bottle_band", "#b8141c", roughness=0.45)
    materials.define("bottle_foil", lv["stripe"], metallic=0.85, roughness=0.28)
    for slot, hexc in FLAGS[nationality]["colours"].items():
        materials.define(slot, hexc, roughness=0.6)


def _obj(part, name, pivot=None):
    o = fg.make_smooth(part, name)
    if pivot is not None:
        blend.set_origin(o, pivot)
    return o


def build_figure(code, t, seat, team):
    """Builds one driver in the current (empty) scene. Returns {name: object}."""
    nationality = seat["nationality"]
    define_materials(t, team, nationality)
    H = float(_v(t["height_m"], 1.78))
    rig = bd.Rig(H, _v(t["build"], "athletic"))
    head = hd.Head(t)
    style = hr.Style(t)
    fh_style = _v(t.get("facial_hair", {}), "clean")
    M = Matrix.Translation(rig.eye) @ Matrix.Scale(rig.head_scale, 4)
    O = {}

    # Head parts (head-local, then placed).
    face_parts = [hd.skin(head, fh_style), hd.eyes(head), hd.brows(head, t), hd.ears(head)]
    if _v(t.get("jewellery", {}), "none") == "studs":
        face_parts.append(hd.studs(head))
    b = hr.beard(head, fh_style)
    if b is not None:
        face_parts.append(b)
    head_pivot = Vector((0.0, rig.neck_base + 0.075 * rig.head_scale, 0.0))
    O["head"] = blend.empty("head", head_pivot)
    O["face"] = _obj(fg.transform(fg.concat(face_parts), M), "face", head_pivot)
    shells, T = hr.shell(head, style, split=True)
    locks = hr.clumps(head, style, T)
    for name, part in shells:
        if name in locks:
            part = fg.concat([part, locks[name]])
        if part[0].faces:
            O[name] = _obj(fg.transform(part, M), name, head_pivot)
    if style.style == "locs":
        O["locs"] = _obj(fg.transform(hr.locs(head, style, back_y=-0.27, back_z=-0.13), M), "locs", head_pivot)
    cap_part, (cpos, cr, cu, cn) = cp.cap(head, style)
    O["cap"] = _obj(fg.transform(cap_part, M), "cap", head_pivot)
    O["number_cap"] = blend.empty("number_cap", M @ cpos, *facing(cn, cu))

    # Body.
    O["legs"] = _obj(bd.legs(rig), "legs")
    torso_part, tpos = bd.torso(rig)
    O["spine"] = blend.empty("spine", rig.spine)
    p_num, n_num = bd.surface_point(tpos, -0.078 * rig.s, 0.715 * H)
    p_name, n_name = bd.surface_point(tpos, 0.08 * rig.s, 0.735 * H)
    p_flag, n_flag = bd.surface_point(tpos, 0.118 * rig.s, 0.772 * H)
    flag = bd.flag_patch(nationality, p_flag, n_flag, (0, 1, 0), 0.06 * rig.s)
    O["torso"] = _obj(fg.concat([torso_part, flag]), "torso", rig.spine)
    O["number_suit"] = blend.empty("number_suit", p_num + n_num * 0.003, *facing(n_num, (0, 1, 0)))
    O["name_suit"] = blend.empty("name_suit", p_name + n_name * 0.003, *facing(n_name, (0, 1, 0)))

    for side, sfx in ((-1, "r"), (1, "l")):
        sh = rig.shoulder_r if side < 0 else rig.shoulder_l
        el = rig.elbow_r if side < 0 else rig.elbow_l
        wr = rig.wrist_r if side < 0 else rig.wrist_l
        upper, fore = bd.arm(rig, side)
        O[f"arm_{sfx}"] = blend.empty(f"arm_{sfx}", sh)
        O[f"forearm_{sfx}"] = blend.empty(f"forearm_{sfx}", el)
        O[f"hand_{sfx}"] = blend.empty(f"hand_{sfx}", wr)
        O[f"arm_{sfx}_mesh"] = _obj(upper, f"arm_{sfx}_mesh", sh)
        O[f"forearm_{sfx}_mesh"] = _obj(fore, f"forearm_{sfx}_mesh", el)
        hand = bd.right_hand(rig) if side < 0 else bd.left_hand(rig)
        O[f"hand_{sfx}_mesh"] = _obj(hand, f"hand_{sfx}_mesh", wr)
    bot = bd.bottle()
    O["bottle"] = _obj(fg.transform(bot, Matrix.Translation(rig.bottle_base)), "bottle", rig.bottle_base)

    # Hierarchy.
    P = blend.parent
    P(O["torso"], O["spine"])
    P(O["number_suit"], O["spine"])
    P(O["name_suit"], O["spine"])
    P(O["head"], O["spine"])
    for k in ("face", "hair", "hair_top", "locs", "cap"):
        if k in O:
            P(O[k], O["head"])
    P(O["number_cap"], O["cap"])
    for sfx in ("r", "l"):
        P(O[f"arm_{sfx}"], O["spine"])
        P(O[f"arm_{sfx}_mesh"], O[f"arm_{sfx}"])
        P(O[f"forearm_{sfx}"], O[f"arm_{sfx}"])
        P(O[f"forearm_{sfx}_mesh"], O[f"forearm_{sfx}"])
        P(O[f"hand_{sfx}"], O[f"forearm_{sfx}"])
        P(O[f"hand_{sfx}_mesh"], O[f"hand_{sfx}"])
    P(O["bottle"], O["hand_r"])
    return O, rig


def triangles(objs):
    return sum(blend.triangles(o) for o in objs.values() if o.type == "MESH")


def build(ctx):
    d = data()
    only = set(ctx.args["drivers"].split(",")) if ctx.args.get("drivers") else None
    for code, seat, team in seats():
        if only and code not in only:
            continue
        blend.reset()
        objs, rig = build_figure(code, d[code], seat, team)
        print(f"[driver] {code}: {triangles(objs)} triangles, height {rig.H:.2f} m")
        export.glb(ctx.model(f"drivers/{code}.glb"), [objs["legs"], objs["spine"]])


def contracts():
    d = data()
    out = []
    for code, seat, team in seats():
        H = float(_v(d[code]["height_m"], 1.78))
        out.append(Contract(f"drivers/{code}.glb", BUDGET, nodes=NODES, materials=SLOTS,
                            size={"y": (H - 0.02, H + 0.06)}, min_y=(-0.003, 0.003), max_bytes=3 * 1024 * 1024))
    return out


# ---- previews and the contact sheet ------------------------------------------------------------------------

FONT = os.path.join(asset.REPO_ROOT, "src", "ArcBox.Host", "fonts", "TitilliumWeb-SemiBold.ttf")


def _show(obj, on):
    obj.hide_render = not on
    for c in obj.children:
        _show(c, on)


def _skin_look():
    """Preview only (the export keeps plain PBR): subsurface scattering on skin and lips, and a little roughness
    variation, so the Cycles portraits read as skin rather than plastic. Godot does the same in its own material."""
    for name in ("skin", "lips", "stubble", "stubble_edge"):
        mat = bpy.data.materials.get(name)
        if mat is None:
            continue
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        for key, val in (("Subsurface Weight", 0.25), ("Subsurface Scale", 0.006)):
            if key in bsdf.inputs:
                bsdf.inputs[key].default_value = val
        if "Subsurface Radius" in bsdf.inputs:
            bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
        tex = nt.nodes.new("ShaderNodeTexNoise")
        tex.inputs["Scale"].default_value = 900.0
        coord = nt.nodes.new("ShaderNodeTexCoord")
        nt.links.new(coord.outputs["Object"], tex.inputs["Vector"])
        ramp = nt.nodes.new("ShaderNodeMapRange")
        ramp.inputs["To Min"].default_value = 0.42
        ramp.inputs["To Max"].default_value = 0.62
        nt.links.new(tex.outputs["Fac"], ramp.inputs["Value"])
        nt.links.new(ramp.outputs["Result"], bsdf.inputs["Roughness"])


def preview(ctx):
    """Renders each driver (cap off: front and three-quarter face; cap on: full figure) and composes the contact
    sheet drivers_sheet.png with code and name labels. Args: drivers=NOR,PIA (subset), res=400, samples=24."""
    d = data()
    only = set(ctx.args["drivers"].split(",")) if ctx.args.get("drivers") else None
    res = int(ctx.args.get("res", "400"))
    samples = int(ctx.args.get("samples", "24"))
    shots = []
    for code, seat, team in seats():
        if only and code not in only:
            continue
        blend.reset()
        objs, rig = build_figure(code, d[code], seat, team)
        eye = rig.eye
        for o in objs.values():
            if o.type == "MESH" and hasattr(o, "shadow_terminator_geometry_offset"):
                o.shadow_terminator_geometry_offset = 0.8
                o.shadow_terminator_offset = 0.15
        _skin_look()
        studio.stage(tuple(eye), 0.6)
        _show(objs["cap"], False)
        if "hair_top" in objs:
            _show(objs["hair_top"], True)
        tgt = eye + Vector((0, -0.03, 0.02))
        f1 = f"{ctx.preview_dir}/driver_{code}_front.png"
        f2 = f"{ctx.preview_dir}/driver_{code}_34.png"
        studio.render(f1, tgt + Vector((0, 0.01, 0.95)), tgt, 85, (res, res), samples)
        studio.render(f2, tgt + Vector((0.62, 0.04, 0.72)), tgt, 85, (res, res), samples)
        _show(objs["cap"], True)
        if "hair_top" in objs:
            _show(objs["hair_top"], False)
        f3 = f"{ctx.preview_dir}/driver_{code}_figure.png"
        if ctx.args.get("figure", "1") == "1":
            mid = Vector((0, rig.H * 0.55, 0.05))
            studio.render(f3, mid + Vector((0.9, 0.25, 3.3)), mid, 50, (int(res * 0.75), res), samples)
        shots.append((code, seat["name"], f1, f2, f3))
        print(f"[driver] preview {code}")
    if shots:
        sheet(shots, f"{ctx.preview_dir}/drivers_sheet.png", res)
        if ctx.args.get("figure", "1") == "1":
            sheet(shots, f"{ctx.preview_dir}/drivers_figures.png", res, figures=True)


def sheet(shots, path, res, figures=False):
    from PIL import Image, ImageDraw, ImageFont
    cols = 4 if not figures else 8
    cell_w = res * 2 if not figures else int(res * 0.75)
    label_h = 44
    rows = math.ceil(len(shots) / cols)
    img = Image.new("RGB", (cols * cell_w + (cols + 1) * 8, rows * (res + label_h) + (rows + 1) * 8), (24, 25, 29))
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, 26) if os.path.exists(FONT) else ImageFont.load_default()
    for i, (code, name, f1, f2, f3) in enumerate(shots):
        r, c = divmod(i, cols)
        x = 8 + c * (cell_w + 8)
        y = 8 + r * (res + label_h + 8)
        if figures:
            img.paste(Image.open(f3).convert("RGB"), (x, y + label_h))
        else:
            img.paste(Image.open(f1).convert("RGB"), (x, y + label_h))
            img.paste(Image.open(f2).convert("RGB"), (x + res, y + label_h))
        draw.text((x + 6, y + 6), f"{code}  {name}", fill=(235, 235, 240), font=font)
    img.save(path)
    print(f"[driver] contact sheet {path}")
