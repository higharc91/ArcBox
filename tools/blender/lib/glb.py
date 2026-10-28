"""A small .glb reader in pure Python (no bpy, no numpy): enough to validate exports.

summary(path) -> dict with
  nodes      {name: {"translation", "rotation", "scale", "mesh", "children"}}
  materials  [names], base_colours {name: (r, g, b, a) linear}
  meshes     {name: triangles}, triangles (total over mesh *instances*)
  bounds     ((minx, miny, minz), (maxx, maxy, maxz)) in the glTF/Godot frame (node transforms applied to the
             POSITION accessors' min/max boxes, so it is exact for translated nodes and conservative when rotated)
  bytes      file size
"""
import json
import math
import os
import struct


def read(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF" or version != 2:
        raise ValueError(f"{path}: not a glTF 2.0 binary")
    off = 12
    doc, binary = None, b""
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == 0x4E4F534A:
            doc = json.loads(chunk.decode("utf-8"))
        elif ctype == 0x004E4942:
            binary = chunk
        off += 8 + clen
    if doc is None:
        raise ValueError(f"{path}: no JSON chunk")
    return doc, binary


def _quat_rotate(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v); v' = v + w * t + cross(q.xyz, t)
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))


def _compose(parent, node):
    """parent/node: (t, q, s) -> world (t, q, s) assuming uniform-ish scale (good enough for bounds)."""
    pt, pq, ps = parent
    t = node.get("translation", [0, 0, 0])
    q = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    st = _quat_rotate(pq, (t[0] * ps[0], t[1] * ps[1], t[2] * ps[2]))
    wt = (pt[0] + st[0], pt[1] + st[1], pt[2] + st[2])
    x1, y1, z1, w1 = pq
    x2, y2, z2, w2 = q
    wq = (w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2, w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
          w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2, w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2)
    return wt, wq, (ps[0] * s[0], ps[1] * s[1], ps[2] * s[2])


def summary(path):
    doc, _ = read(path)
    nodes = doc.get("nodes", [])
    meshes = doc.get("meshes", [])
    accessors = doc.get("accessors", [])
    mats = doc.get("materials", [])

    def mesh_tris(mi):
        n = 0
        for p in meshes[mi].get("primitives", []):
            if p.get("mode", 4) != 4:
                continue
            if "indices" in p:
                n += accessors[p["indices"]]["count"] // 3
            else:
                n += accessors[p["attributes"]["POSITION"]]["count"] // 3
        return n

    out_nodes = {}
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    total = 0
    scene = doc.get("scenes", [{}])[doc.get("scene", 0)] if doc.get("scenes") else {"nodes": list(range(len(nodes)))}

    def visit(i, parent):
        nonlocal total
        node = nodes[i]
        world = _compose(parent, node)
        name = node.get("name", f"node{i}")
        out_nodes[name] = dict(translation=world[0], rotation=world[1], scale=world[2], mesh=node.get("mesh"),
                               children=[nodes[c].get("name", f"node{c}") for c in node.get("children", [])])
        if "mesh" in node:
            total += mesh_tris(node["mesh"])
            for p in meshes[node["mesh"]].get("primitives", []):
                acc = accessors[p["attributes"]["POSITION"]]
                mn, mx = acc.get("min"), acc.get("max")
                if mn is None or mx is None:
                    continue
                for cx in (mn[0], mx[0]):
                    for cy in (mn[1], mx[1]):
                        for cz in (mn[2], mx[2]):
                            v = _quat_rotate(world[1], (cx * world[2][0], cy * world[2][1], cz * world[2][2]))
                            v = (v[0] + world[0][0], v[1] + world[0][1], v[2] + world[0][2])
                            for k in range(3):
                                lo[k] = min(lo[k], v[k])
                                hi[k] = max(hi[k], v[k])
        for c in node.get("children", []):
            visit(c, world)

    for root in scene.get("nodes", []):
        visit(root, ((0, 0, 0), (0, 0, 0, 1), (1, 1, 1)))

    used = set()
    for m in meshes:
        for p in m.get("primitives", []):
            if "material" in p:
                used.add(mats[p["material"]].get("name", ""))
    return dict(
        nodes=out_nodes,
        materials=[m.get("name", "") for m in mats],
        used_materials=sorted(used),
        base_colours={m.get("name", ""): tuple(m.get("pbrMetallicRoughness", {}).get("baseColorFactor", [1, 1, 1, 1]))
                      for m in mats},
        meshes={m.get("name", f"mesh{i}"): mesh_tris(i) for i, m in enumerate(meshes)},
        triangles=total,
        bounds=(tuple(lo), tuple(hi)),
        bytes=os.path.getsize(path),
        extensions_required=doc.get("extensionsRequired", []),
    )
