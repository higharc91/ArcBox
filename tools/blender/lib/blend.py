"""bpy helpers: scene reset, Mesh -> object, modifiers and joins, empties. Positions are in the Godot frame."""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import frame, materials


def reset():
    """Empty factory scene (deterministic: no startup file, no add-ons beyond the defaults)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials.clear_cache()
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    return scene


def _link(obj, collection=None):
    (collection or bpy.context.scene.collection).objects.link(obj)
    return obj


def make(mesh, name, collection=None):
    """Creates a Blender object from a lib.mesh.Mesh (Godot frame), with one material slot per slot name used."""
    me = bpy.data.meshes.new(name)
    verts = [tuple(frame.to_blender(v)) for v in mesh.verts]
    me.from_pydata(verts, [], [list(f) for f in mesh.faces])
    slots = []
    for s in mesh.mats:
        if s not in slots:
            slots.append(s)
    for s in slots:
        me.materials.append(materials.get(s))
    index = {s: i for i, s in enumerate(slots)}
    me.polygons.foreach_set("material_index", [index[s] for s in mesh.mats])
    me.polygons.foreach_set("use_smooth", [bool(s) for s in mesh.smooth])
    uv = me.uv_layers.new(name="UVMap")
    data = []
    for poly, puv, f in zip(me.polygons, mesh.uvs, mesh.faces):
        if puv and len(puv) == poly.loop_total:
            for c in puv:
                data += [c[0], c[1]]
        else:
            # No UVs from the builder: project onto the face's own plane (metres), so tangents are never degenerate.
            n = mesh.face_normal(f)
            n = n.normalized() if n.length > 1e-12 else Vector((0, 1, 0))
            a = Vector((0, 1, 0)) if abs(n.y) < 0.9 else Vector((1, 0, 0))
            u = a.cross(n).normalized()
            v = n.cross(u)
            for i in f:
                p = mesh.verts[i]
                data += [p.dot(u), p.dot(v)]
    uv.data.foreach_set("uv", data)
    me.validate(clean_customdata=False)
    me.update()
    obj = bpy.data.objects.new(name, me)
    return _link(obj, collection)


def subsurf(obj, levels=1, quality=3):
    m = obj.modifiers.new("subsurf", "SUBSURF")
    m.levels = levels
    m.render_levels = levels
    m.quality = quality
    m.uv_smooth = "PRESERVE_BOUNDARIES"
    return m


def bevel(obj, width, segments=2, angle=35.0, profile=0.5, limit="ANGLE", clamp=True):
    """Rounded hard edges: every edge sharper than `angle` degrees gets a `width` bevel."""
    m = obj.modifiers.new("bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = limit
    m.angle_limit = math.radians(angle)
    m.profile = profile
    m.use_clamp_overlap = clamp
    m.miter_outer = "MITER_ARC"
    return m


def boolean(obj, cutter, operation="DIFFERENCE"):
    """Boolean with the exact solver; faces made by the cutter take the cutter's material."""
    m = obj.modifiers.new("bool_" + cutter.name, "BOOLEAN")
    m.operation = operation
    m.solver = "EXACT"
    m.object = cutter
    if hasattr(m, "material_mode"):
        m.material_mode = "TRANSFER"
    cutter.hide_render = True
    cutter.hide_viewport = True
    return m


def weld(obj, distance=1e-5):
    m = obj.modifiers.new("weld", "WELD")
    m.merge_threshold = distance
    return m


def apply(obj):
    """Applies every modifier (context-free, via the evaluated depsgraph)."""
    if not obj.modifiers:
        return obj
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = obj.data
    obj.modifiers.clear()
    obj.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)
    return obj


def delete(obj):
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if data is not None and getattr(data, "users", 1) == 0 and isinstance(data, bpy.types.Mesh):
        bpy.data.meshes.remove(data)


def mark_sharp(obj, angle=40.0):
    """Marks every edge sharper than `angle` degrees (and open boundaries) sharp, so smooth shading keeps crisp
    creases after joins (the joined mesh uses auto smooth at 180 degrees and these marks)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    lim = math.radians(angle)
    for e in bm.edges:
        if len(e.link_faces) != 2:
            e.smooth = False
            continue
        f1, f2 = e.link_faces
        if not (f1.smooth and f2.smooth) or f1.normal.angle(f2.normal, 0.0) > lim:
            e.smooth = False
    bm.to_mesh(me)
    bm.free()
    me.use_auto_smooth = True
    me.auto_smooth_angle = math.pi
    return obj


def finish(obj, sharp_angle=40.0):
    """Applies the modifiers and marks sharp edges: the last step for every part before join()."""
    apply(obj)
    return mark_sharp(obj, sharp_angle)


def join(objs, name):
    """Joins objects into one (materials merged by name); returns the result named `name`."""
    objs = [o for o in objs if o is not None]
    target = objs[0]
    if len(objs) > 1:
        with bpy.context.temp_override(active_object=target, object=target, selected_objects=objs,
                                       selected_editable_objects=objs):
            bpy.ops.object.join()
    target.name = name
    target.data.name = name
    me = target.data
    me.use_auto_smooth = True
    me.auto_smooth_angle = math.pi
    _merge_duplicate_slots(target)
    return target


def _merge_duplicate_slots(obj):
    me = obj.data
    first = {}
    remap = []
    for i, mat in enumerate(me.materials):
        key = mat.name if mat else ""
        first.setdefault(key, i)
        remap.append(first[key])
    if len(first) == len(remap):
        return
    idx = [0] * len(me.polygons)
    me.polygons.foreach_get("material_index", idx)
    keep = sorted(set(remap))
    newpos = {old: k for k, old in enumerate(keep)}
    idx = [newpos[remap[i]] for i in idx]
    mats = [me.materials[i] for i in keep]
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", idx)
    me.update()


def set_origin(obj, pos):
    """Moves the object's origin to `pos` (Godot frame) without moving its geometry."""
    t = frame.to_blender(pos)
    obj.data.transform(Matrix.Translation(-t))
    obj.matrix_world = Matrix.Translation(t) @ obj.matrix_world
    return obj


def empty(name, pos, right=(1, 0, 0), up=(0, 1, 0), normal=(0, 0, 1), size=0.1, collection=None):
    """An empty whose glTF/Godot node sits at `pos` with local axes (right, up, normal) in the Godot frame."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = size
    e.matrix_world = frame.node_matrix(pos, right, up, normal)
    return _link(e, collection)


def parent(child, parent_obj):
    mw = child.matrix_world.copy()
    child.parent = parent_obj
    child.matrix_world = mw
    return child


def triangles(obj):
    me = obj.data
    return sum(len(p.vertices) - 2 for p in me.polygons)


def slot_names(obj):
    return [m.name for m in obj.data.materials if m]
