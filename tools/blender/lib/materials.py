"""Named material slots. The slot *name* is the contract with Godot (which rebinds every slot to its own material,
team colours included); the Blender material only sets sensible glTF defaults and drives the Cycles previews.

define() registers a slot; get() creates (once per scene) the Principled BSDF material for it. Presets:
  paint(name, hex, matte=False)   metallic-flake gloss with clearcoat, or matte (Coat off, rough)
  carbon(name)                    dark lacquered carbon (weave is a Godot shader detail)
  rubber(name, hex)               tyre rubber
  metal(name, hex, roughness)     brushed metal (rims, exhaust)
  emissive(name, hex, strength)   lights
  glass(name, hex)                mirror glass / visors
Colours are sRGB hex in, linear out (colour.srgb_to_linear).
"""
import bpy

from .colour import srgb_to_linear

_SPECS = {}
_CACHE = {}


def clear_cache():
    _CACHE.clear()


def define(name, colour="#808080", metallic=0.0, roughness=0.5, coat=0.0, coat_roughness=0.03, emission=None,
           emission_strength=0.0, alpha=1.0, anisotropic=0.0):
    _SPECS[name] = dict(colour=colour, metallic=metallic, roughness=roughness, coat=coat, coat_roughness=coat_roughness,
                        emission=emission, emission_strength=emission_strength, alpha=alpha, anisotropic=anisotropic)
    _CACHE.pop(name, None)
    return name


def paint(name, hex_colour, matte=False):
    if matte:
        return define(name, hex_colour, metallic=0.15, roughness=0.55)
    return define(name, hex_colour, metallic=0.3, roughness=0.32, coat=1.0, coat_roughness=0.04)


def carbon(name="carbon"):
    return define(name, "#16161a", metallic=0.2, roughness=0.35, coat=0.6, coat_roughness=0.08)


def rubber(name="tyre", hex_colour="#141416"):
    return define(name, hex_colour, roughness=0.88)


def metal(name="rim", hex_colour="#3b3d42", roughness=0.32, anisotropic=0.6):
    return define(name, hex_colour, metallic=1.0, roughness=roughness, anisotropic=anisotropic)


def emissive(name="light", hex_colour="#ff1a12", strength=6.0):
    return define(name, hex_colour, roughness=0.3, emission=hex_colour, emission_strength=strength)


def glass(name="glass", hex_colour="#1b2a40"):
    return define(name, hex_colour, metallic=0.9, roughness=0.05)


def _set(bsdf, names, value):
    for n in names if isinstance(names, (list, tuple)) else [names]:
        if n in bsdf.inputs:
            bsdf.inputs[n].default_value = value
            return


def get(name):
    """The bpy material for slot `name` (defined first with define() or a preset)."""
    mat = _CACHE.get(name)
    if mat is not None and mat.name in bpy.data.materials:
        return mat
    spec = _SPECS.get(name)
    if spec is None:
        raise KeyError(f"material slot '{name}' is not defined")
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    _set(bsdf, "Base Color", srgb_to_linear(spec["colour"]))
    _set(bsdf, "Metallic", spec["metallic"])
    _set(bsdf, "Roughness", spec["roughness"])
    _set(bsdf, ["Coat Weight", "Clearcoat"], spec["coat"])
    _set(bsdf, ["Coat Roughness", "Clearcoat Roughness"], spec["coat_roughness"])
    _set(bsdf, ["Anisotropic"], spec["anisotropic"])
    if spec["emission"]:
        _set(bsdf, ["Emission Color", "Emission"], srgb_to_linear(spec["emission"]))
        _set(bsdf, "Emission Strength", spec["emission_strength"])
    mat.diffuse_color = srgb_to_linear(spec["colour"])
    _CACHE[name] = mat
    return mat
