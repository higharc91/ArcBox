"""Where the build writes, and the two writers every set uses (deterministic PNG and JSON output)."""
from __future__ import annotations

import json
import os
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parents[1]           # tools/tactics-assets
PACK = Path(os.environ.get("TACTICS_PACK", HERE.parents[1] / "content" / "tactics" / "base"))
PREVIEWS = HERE / "previews"
MOCKUPS = HERE.parents[1] / "docs" / "superpowers" / "mockups" / "tactics" / "assets"

# build.py disables review output for an ordinary content build.
_PREVIEWS_ENABLED = True


def set_previews(enabled: bool) -> None:
    global _PREVIEWS_ENABLED
    _PREVIEWS_ENABLED = enabled


def previews_enabled() -> bool:
    return _PREVIEWS_ENABLED


def is_preview_path(path: Path) -> bool:
    return any(Path(path).resolve().is_relative_to(root.resolve()) for root in (PREVIEWS, MOCKUPS))


def save(img: Image.Image, path: Path, **options) -> None:
    if is_preview_path(path) and not previews_enabled():
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.suffix.lower() == ".png":
        options.setdefault("optimize", True)
    img.save(path, **options)


def write_json(obj, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2) + "\n")
