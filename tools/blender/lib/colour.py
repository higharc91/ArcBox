"""Colours. Hex strings in the data (grid2026.json) are sRGB; glTF baseColorFactor and Blender's Principled BSDF
Base Color are linear, so every hex goes through srgb_to_linear() before it reaches a material."""


def _decode(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _encode(c):
    c = max(0.0, min(1.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def srgb_to_linear(hex_colour, alpha=1.0):
    """'#rrggbb' (sRGB) -> (r, g, b, a) linear floats. '#ff8000' (McLaren papaya) -> (1.0, 0.216, 0.0, 1.0)."""
    h = hex_colour.lstrip("#")
    if len(h) != 6:
        raise ValueError(f"'{hex_colour}' is not #rrggbb")
    r, g, b = (int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    return (_decode(r), _decode(g), _decode(b), alpha)


def linear_to_srgb_hex(rgb):
    """Inverse of srgb_to_linear (for checks): linear (r, g, b[, a]) -> '#rrggbb'."""
    return "#" + "".join(f"{round(_encode(c) * 255):02x}" for c in rgb[:3])
