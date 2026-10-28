"""Export contracts: what every .glb must satisfy. Pure Python (uses lib.glb), so check.py can run anywhere.

An asset module returns a list of Contract from contracts(); build.py checks them after building and check.py checks
the committed files.
"""
import os
from dataclasses import dataclass, field

from . import glb


@dataclass
class Contract:
    path: str                      # relative to the models dir, e.g. "car_2026.glb"
    max_triangles: int
    nodes: tuple = ()              # node names that must exist
    materials: tuple = ()          # material names that must be used by some primitive
    size: dict = field(default_factory=dict)   # axis "x"/"y"/"z" -> (min extent, max extent) in metres
    min_y: tuple = None            # (lo, hi) for the lowest point (e.g. a car sits on y = 0)
    max_bytes: int = 8 * 1024 * 1024
    extra: object = None           # callable(summary) -> list of error strings

    def check(self, models_dir):
        path = os.path.join(models_dir, self.path)
        if not os.path.exists(path):
            return [f"{self.path}: missing"], None
        s = glb.summary(path)
        errors = []
        if s["triangles"] > self.max_triangles:
            errors.append(f"{self.path}: {s['triangles']} triangles > budget {self.max_triangles}")
        missing = [n for n in self.nodes if n not in s["nodes"]]
        if missing:
            errors.append(f"{self.path}: missing nodes {missing}")
        missing = [m for m in self.materials if m not in s["used_materials"]]
        if missing:
            errors.append(f"{self.path}: missing materials {missing}")
        (lx, ly, lz), (hx, hy, hz) = s["bounds"]
        for axis, (lo, hi) in self.size.items():
            ext = {"x": hx - lx, "y": hy - ly, "z": hz - lz}[axis]
            if not lo <= ext <= hi:
                errors.append(f"{self.path}: {axis} extent {ext:.3f} m outside [{lo}, {hi}]")
        if self.min_y is not None and not self.min_y[0] <= ly <= self.min_y[1]:
            errors.append(f"{self.path}: lowest point y={ly:.4f} outside {self.min_y}")
        if s["bytes"] > self.max_bytes:
            errors.append(f"{self.path}: {s['bytes']} bytes > {self.max_bytes}")
        if s["extensions_required"]:
            errors.append(f"{self.path}: requires extensions {s['extensions_required']}")
        if self.extra:
            errors += [f"{self.path}: {e}" for e in self.extra(s)]
        return errors, s


def check_all(contracts, models_dir, log=print):
    failures = []
    for c in contracts:
        errors, s = c.check(models_dir)
        if s is not None:
            log(f"  {c.path:<34} {s['triangles']:>7} tris  {s['bytes'] / 1024:>7.0f} KiB  "
                f"{len(s['used_materials'])} materials  {'OK' if not errors else 'FAIL'}")
        for e in errors:
            log(f"    ERROR {e}")
        failures += errors
    return failures
