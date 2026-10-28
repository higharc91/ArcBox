"""Cycles preview renders (CPU; this Blender build has no OpenImageDenoise, so denoising stays off and the sample
count does the work). Camera positions are in the Godot frame. The real look is Godot's; these are for checking
shapes and slot colours without running the TV app."""
import math

import bpy
from mathutils import Vector

from . import frame


def stage(ground_hex="#3a3b3e", sun_elevation=38.0, sun_rotation=-35.0, strength=0.35):
    """Sky (Nishita), a sun lamp and a large asphalt-grey ground plane at y = 0."""
    scene = bpy.context.scene
    world = bpy.data.worlds.new("preview") if not scene.world else scene.world
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes.get("Background")
    sky = nt.nodes.new("ShaderNodeTexSky")
    sky.sky_type = "NISHITA"
    sky.sun_elevation = math.radians(sun_elevation)
    sky.sun_rotation = math.radians(sun_rotation)
    sky.sun_disc = False
    nt.links.new(sky.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = strength

    sun = bpy.data.objects.new("preview_sun", bpy.data.lights.new("preview_sun", "SUN"))
    sun.data.energy = 3.5
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(90 - sun_elevation), 0, math.radians(sun_rotation + 180))
    scene.collection.objects.link(sun)

    from .colour import srgb_to_linear
    me = bpy.data.meshes.new("preview_ground")
    s = 60
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [[0, 1, 2, 3]])
    ground = bpy.data.objects.new("preview_ground", me)
    mat = bpy.data.materials.new("preview_ground")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = srgb_to_linear(ground_hex)
    b.inputs["Roughness"].default_value = 0.85
    me.materials.append(mat)
    scene.collection.objects.link(ground)
    return [sun, ground]


def render(path, eye, target, lens=50.0, res=(1280, 720), samples=96):
    """Renders the scene from `eye` looking at `target` (Godot frame) to a PNG."""
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("preview_cam")
    cam_data.lens = lens
    cam = bpy.data.objects.new("preview_cam", cam_data)
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
    scene.cycles.adaptive_threshold = 0.02
    scene.cycles.max_bounces = 6
    scene.cycles.seed = 1
    scene.render.film_transparent = False
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in
                                                           scene.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    return path
