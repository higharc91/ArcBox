"""Coordinate frames.

All REDLINE geometry is *authored in the Godot frame*: metres, +Y up, +Z forward, +X to the model's left (for a car:
origin on the ground midway between the axles). Blender is Z-up; the glTF exporter converts Blender (x, y, z) to glTF
(x, z, -y), and glTF is Godot's frame. So a Godot point (x, y, z) is created in Blender at (x, -z, y) and comes back
out of the .glb exactly where it was authored.
"""
from mathutils import Matrix, Vector


def to_blender(p):
    """Godot-frame point or direction -> Blender."""
    return Vector((p[0], -p[2], p[1]))


def from_blender(p):
    """Blender point or direction -> Godot frame."""
    return Vector((p[0], p[2], -p[1]))


def node_matrix(pos, right=(1, 0, 0), up=(0, 1, 0), normal=(0, 0, 1)):
    """Blender world matrix for an object whose glTF/Godot node ends up at `pos` with local axes
    X = `right`, Y = `up`, Z = `normal` (all Godot frame, orthonormal, right = up x normal).

    The exporter conjugates object transforms by the axis change C (M' = C M C^-1), so the Blender object's local
    X, Y, Z axes must be C^-1(right), -C^-1(normal), C^-1(up).
    """
    r, u, n = Vector(right).normalized(), Vector(up).normalized(), Vector(normal).normalized()
    bx, by, bz = to_blender(r), -to_blender(n), to_blender(u)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2] = bx[i], by[i], bz[i]
    t = to_blender(pos)
    m[0][3], m[1][3], m[2][3] = t
    return m


def facing(normal, up_hint=(0, 1, 0)):
    """(right, up, normal) for a decal facing `normal` with its 'up' as close to `up_hint` as possible."""
    n = Vector(normal).normalized()
    u = Vector(up_hint)
    u = (u - n * u.dot(n)).normalized()
    r = u.cross(n)
    return r, u, n
