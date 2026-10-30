"""Where the build writes, and the two writers every set uses (deterministic PNG and JSON output)."""
from __future__ import annotations

import json
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parents[1]           # tools/tactics-assets
PACK = HERE.parents[1] / "content" / "tactics" / "base"
PREVIEWS = HERE / "previews"
MOCKUPS = HERE.parents[1] / "docs" / "superpowers" / "mockups" / "tactics" / "assets"


def save(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)


def write_json(obj, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2) + "\n")
