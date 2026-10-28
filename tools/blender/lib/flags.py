"""Simplified national flags for the suit patch and helmet bands (Task 2): plain geometric shapes only, no coats of
arms, emblems or text. Coordinates are in the unit square, x to the right and y down (a 3:2 patch is drawn into it).
Each flag: `colours` {slot: sRGB hex} and `layers` [(slot, [polygon, ...])], drawn bottom to top.
Keys are the ISO codes grid2026.json uses for nationalities.
"""
import math


def _rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def _band(a, b, w, aspect=1.5):
    """A straight band of width w (in y units) from point a to point b."""
    dx, dy = (b[0] - a[0]) * aspect, b[1] - a[1]
    L = math.hypot(dx, dy)
    nx, ny = -dy / L * w / 2 / aspect, dx / L * w / 2
    return [(a[0] + nx, a[1] + ny), (b[0] + nx, b[1] + ny), (b[0] - nx, b[1] - ny), (a[0] - nx, a[1] - ny)]


def _star(cx, cy, r, points=5, inner=0.4, aspect=1.5, rot=0.0):
    out = []
    for i in range(points * 2):
        a = rot - math.pi / 2 + math.pi * i / points
        rr = r if i % 2 == 0 else r * inner
        out.append((cx + math.cos(a) * rr / aspect, cy + math.sin(a) * rr))
    return out


def _disc(cx, cy, r, n=16, aspect=1.5):
    return [(cx + math.cos(2 * math.pi * i / n) * r / aspect, cy + math.sin(2 * math.pi * i / n) * r) for i in range(n)]


def _scaled(polys, x0, y0, sx, sy):
    return [[(x0 + x * sx, y0 + y * sy) for x, y in p] for p in polys]


def _union_jack(x0=0.0, y0=0.0, sx=1.0, sy=1.0, blue="flag_1", white="flag_2", red="flag_3"):
    diag = [_band((0, 0), (1, 1), 0.2), _band((0, 1), (1, 0), 0.2)]
    diag_r = [_band((0, 0), (1, 1), 0.07), _band((0, 1), (1, 0), 0.07)]
    cross_w = [_rect(0.4167, 0, 0.5833, 1), _rect(0, 0.3667, 1, 0.6333)]
    cross_r = [_rect(0.45, 0, 0.55, 1), _rect(0, 0.42, 1, 0.58)]
    return [
        (blue, _scaled([_rect(0, 0, 1, 1)], x0, y0, sx, sy)),
        (white, _scaled(diag, x0, y0, sx, sy)),
        (red, _scaled(diag_r, x0, y0, sx, sy)),
        (white, _scaled(cross_w, x0, y0, sx, sy)),
        (red, _scaled(cross_r, x0, y0, sx, sy)),
    ]


def _h3(a, b, c, ratio=(1, 1, 1)):
    t = sum(ratio)
    y1, y2 = ratio[0] / t, (ratio[0] + ratio[1]) / t
    return [("flag_1", [_rect(0, 0, 1, y1)]), ("flag_2", [_rect(0, y1, 1, y2)]), ("flag_3", [_rect(0, y2, 1, 1)])]


def _v3():
    return [("flag_1", [_rect(0, 0, 1 / 3, 1)]), ("flag_2", [_rect(1 / 3, 0, 2 / 3, 1)]),
            ("flag_3", [_rect(2 / 3, 0, 1, 1)])]


_MAPLE = [(0.5, 0.18), (0.535, 0.29), (0.585, 0.26), (0.57, 0.42), (0.64, 0.34), (0.66, 0.39), (0.72, 0.37),
          (0.69, 0.5), (0.73, 0.53), (0.6, 0.62), (0.615, 0.68), (0.51, 0.665), (0.515, 0.8), (0.485, 0.8),
          (0.49, 0.665), (0.385, 0.68), (0.4, 0.62), (0.27, 0.53), (0.31, 0.5), (0.28, 0.37), (0.34, 0.39),
          (0.36, 0.34), (0.43, 0.42), (0.415, 0.26), (0.465, 0.29)]

FLAGS = {
    "GB": {"colours": {"flag_1": "#012169", "flag_2": "#ffffff", "flag_3": "#c8102e"}, "layers": _union_jack()},
    "AU": {"colours": {"flag_1": "#012169", "flag_2": "#ffffff", "flag_3": "#e4002b"},
           "layers": [("flag_1", [_rect(0, 0, 1, 1)])] + _union_jack(0, 0, 0.5, 0.5)[1:]
           + [("flag_2", [_star(0.25, 0.75, 0.14, 7, 0.45), _star(0.75, 0.2, 0.07, 7, 0.45),
                          _star(0.62, 0.45, 0.07, 7, 0.45), _star(0.87, 0.38, 0.07, 7, 0.45),
                          _star(0.75, 0.82, 0.08, 7, 0.45), _star(0.81, 0.55, 0.04, 5, 0.45)])]},
    "NZ": {"colours": {"flag_1": "#00247d", "flag_2": "#ffffff", "flag_3": "#cc142b"},
           "layers": [("flag_1", [_rect(0, 0, 1, 1)])] + _union_jack(0, 0, 0.5, 0.5)[1:]
           + [("flag_2", [_star(0.75, 0.2, 0.085, 5, 0.42), _star(0.63, 0.45, 0.085, 5, 0.42),
                          _star(0.87, 0.4, 0.075, 5, 0.42), _star(0.75, 0.8, 0.095, 5, 0.42)]),
              ("flag_3", [_star(0.75, 0.2, 0.06, 5, 0.42), _star(0.63, 0.45, 0.06, 5, 0.42),
                          _star(0.87, 0.4, 0.052, 5, 0.42), _star(0.75, 0.8, 0.07, 5, 0.42)])]},
    "MC": {"colours": {"flag_1": "#ce1126", "flag_2": "#ffffff"},
           "layers": [("flag_1", [_rect(0, 0, 1, 0.5)]), ("flag_2", [_rect(0, 0.5, 1, 1)])]},
    "NL": {"colours": {"flag_1": "#ae1c28", "flag_2": "#ffffff", "flag_3": "#21468b"}, "layers": _h3(0, 0, 0)},
    "FR": {"colours": {"flag_1": "#0055a4", "flag_2": "#ffffff", "flag_3": "#ef4135"}, "layers": _v3()},
    "IT": {"colours": {"flag_1": "#009246", "flag_2": "#ffffff", "flag_3": "#ce2b37"}, "layers": _v3()},
    "ES": {"colours": {"flag_1": "#aa151b", "flag_2": "#f1bf00", "flag_3": "#aa151b"},
           "layers": _h3(0, 0, 0, (1, 2, 1))},
    "CA": {"colours": {"flag_1": "#d52b1e", "flag_2": "#ffffff", "flag_3": "#d52b1e"},
           "layers": [("flag_1", [_rect(0, 0, 0.25, 1), _rect(0.75, 0, 1, 1)]), ("flag_2", [_rect(0.25, 0, 0.75, 1)]),
                      ("flag_3", [_MAPLE])]},
    "AR": {"colours": {"flag_1": "#74acdf", "flag_2": "#ffffff", "flag_3": "#74acdf", "flag_4": "#f6b40e"},
           "layers": _h3(0, 0, 0) + [("flag_4", [_star(0.5, 0.5, 0.13, 16, 0.62), ])]},
    "TH": {"colours": {"flag_1": "#a51931", "flag_2": "#f4f5f8", "flag_3": "#2d2a4a"},
           "layers": [("flag_1", [_rect(0, 0, 1, 1 / 6), _rect(0, 5 / 6, 1, 1)]),
                      ("flag_2", [_rect(0, 1 / 6, 1, 1 / 3), _rect(0, 2 / 3, 1, 5 / 6)]),
                      ("flag_3", [_rect(0, 1 / 3, 1, 2 / 3)])]},
    "DE": {"colours": {"flag_1": "#000000", "flag_2": "#dd0000", "flag_3": "#ffce00"}, "layers": _h3(0, 0, 0)},
    "BR": {"colours": {"flag_1": "#009c3b", "flag_2": "#ffdf00", "flag_3": "#002776"},
           "layers": [("flag_1", [_rect(0, 0, 1, 1)]),
                      ("flag_2", [[(0.5, 0.09), (0.93, 0.5), (0.5, 0.91), (0.07, 0.5)]]),
                      ("flag_3", [_disc(0.5, 0.5, 0.25, 20)])]},
    "MX": {"colours": {"flag_1": "#006847", "flag_2": "#ffffff", "flag_3": "#ce1126"}, "layers": _v3()},
    "FI": {"colours": {"flag_1": "#ffffff", "flag_2": "#002f6c"},
           "layers": [("flag_1", [_rect(0, 0, 1, 1)]),
                      ("flag_2", [_rect(0.278, 0, 0.444, 1), _rect(0, 0.364, 1, 0.636)])]},
}
