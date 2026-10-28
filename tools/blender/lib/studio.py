"""Studio lighting for portrait previews and contact sheets (Task 2): a soft key, a fill, a rim and a neutral
backdrop, with Cycles settings (no denoiser in this Blender build). Positions are in the Godot frame."""
import math

import bpy

from . import frame
from .colour import srgb_to_linear


def _area(name, pos, target, size, energy, colour=(1, 1, 1)):
    light = bpy.data.lights.new(name, "AREA")
    light.size = size
    light.energy = energy
    light.color = colour
    obj = bpy.data.objects.new(name, light)
    bpy.context.scene.collection.objects.link(obj)
    p, t = frame.to_blender(pos), frame.to_blender(target)
    obj.location = p
    obj.rotation_euler = (t - p).to_track_quat("-Z", "Y").to_euler()
    return obj


def stage(target=(0, 1.6, 0), scale=1.0, backdrop="#5b6068", key=1.0):
    """Key from the front-left above, fill from the right, rim from behind, a grey world. Returns the objects."""
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("studio")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = srgb_to_linear(backdrop)
    bg.inputs["Strength"].default_value = 0.35
    tx, ty, tz = target
    s = scale
    objs = [
        _area("key", (tx + 1.1 * s, ty + 0.9 * s, tz + 1.6 * s), target, 1.4 * s, 75 * s * s * key, (1.0, 0.97, 0.92)),
        _area("fill", (tx - 1.6 * s, ty + 0.2 * s, tz + 1.2 * s), target, 2.0 * s, 28 * s * s * key, (0.92, 0.95, 1.0)),
        _area("rim", (tx - 0.6 * s, ty + 1.0 * s, tz - 1.5 * s), target, 1.0 * s, 60 * s * s * key),
    ]
    return objs


def render(path, eye, target, lens=85.0, res=(640, 640), samples=48, transparent=False):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("studio_cam")
    cam_data.lens = lens
    cam_data.clip_start = 0.01
    cam = bpy.data.objects.new("studio_cam", cam_data)
    scene.collection.objects.link(cam)
    e, t = frame.to_blender(eye), frame.to_blender(target)
    cam.location = e
    cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.03
    scene.cycles.max_bounces = 4
    scene.cycles.seed = 1
    scene.cycles.filter_width = 1.2
    scene.render.film_transparent = transparent
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    names = [i.identifier for i in scene.view_settings.bl_rna.properties["view_transform"].enum_items]
    scene.view_settings.view_transform = "AgX" if "AgX" in names else "Filmic"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    return path
