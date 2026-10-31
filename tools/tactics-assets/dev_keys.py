"""Guards the art-key contract while the art changes.

The rules pack refers to art by key (`fx.heal.holy`, `beasts.wolf.grey`, `characters.cast`), and the C# loader
(`Tactics.Logic.ArtManifests`) builds those keys from every set's manifest.json. Removing or renaming a key breaks the
rules pack, so this script computes the keys exactly as the loader does and compares them with a saved baseline.

    python3 tools/tactics-assets/dev_keys.py snapshot base.json   # save today's keys
    python3 tools/tactics-assets/dev_keys.py check base.json      # fail if any saved key is gone; list new ones
    python3 tools/tactics-assets/dev_keys.py refs                 # fail if the rules pack names a key no set defines
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

BASE = Path(__file__).resolve().parents[2] / "content" / "tactics" / "base"
NAME = re.compile(r"^[A-Za-z0-9_-]+$")


def keys() -> set[str]:
    out: set[str] = set()
    for d in sorted(p for p in BASE.iterdir() if p.is_dir() and p.name != "rules"):
        m = d / "manifest.json"
        if not m.exists():
            continue
        doc = json.loads(m.read_text())
        for group in doc.values():
            if isinstance(group, dict):
                for name, asset in group.items():
                    out.add(f"{d.name}.{name}")
                    if isinstance(asset, dict) and isinstance(asset.get("variants"), dict):
                        out.update(f"{d.name}.{name}.{v}" for v in asset["variants"])
            elif isinstance(group, list):
                out.update(f"{d.name}.{s}" for s in group if isinstance(s, str) and NAME.match(s))
    return out


def refs() -> set[str]:
    """Every presentation key and sprite the rules pack names (fields known to hold art keys)."""
    found: set[str] = set()

    def walk(node):
        if isinstance(node, dict):
            for k, v in node.items():
                if k in ("anim", "fx", "projectile", "sprite", "icon", "portrait") and isinstance(v, str) and "." in v:
                    found.add(v)
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    for f in (BASE / "rules").rglob("*.json"):
        if "schema" not in f.parts:
            walk(json.loads(f.read_text()))
    return found


def main(argv: list[str]) -> int:
    cmd = argv[1] if len(argv) > 1 else "check"
    if cmd == "snapshot":
        Path(argv[2]).write_text(json.dumps(sorted(keys()), indent=0))
        print(f"saved {len(keys())} keys")
        return 0
    if cmd == "check":
        before = set(json.loads(Path(argv[2]).read_text()))
        now = keys()
        gone, new = sorted(before - now), sorted(now - before)
        print(f"{len(now)} keys now, {len(before)} before: {len(new)} new, {len(gone)} gone")
        for k in gone:
            print("  GONE", k)
        return 1 if gone else 0
    if cmd == "refs":
        missing = sorted(refs() - keys())
        for k in missing:
            print("  MISSING", k)
        print(f"{len(refs())} keys referenced by rules, {len(missing)} missing")
        return 1 if missing else 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
