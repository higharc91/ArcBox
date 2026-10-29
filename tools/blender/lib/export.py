"""glTF binary export with the project's settings: Y-up, modifiers applied, normals + UVs, no Draco (Godot can't
import it), no vertex colours, animations, cameras, lights or extras. Only the given objects (and their children)
are written, so one scene can hold several exports (LODs)."""
import os

import bpy


def _children(objs):
    out = []
    for o in objs:
        out.append(o)
        out += _children(list(o.children))
    return out


def glb(path, objects):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    objs = _children(objects)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    wanted = dict(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_texcoords=True,
        export_normals=True,
        export_tangents=False,
        export_materials="EXPORT",
        export_colors=False,
        export_attributes=False,
        export_cameras=False,
        export_lights=False,
        export_extras=False,
        export_animations=False,
        export_skins=False,
        export_morph=False,
        export_draco_mesh_compression_enable=False,
        export_image_format="AUTO",
        will_save_settings=False,
    )
    props = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    bpy.ops.export_scene.gltf(**{k: v for k, v in wanted.items() if k in props})
    for o in objs:
        o.select_set(False)
    return path
