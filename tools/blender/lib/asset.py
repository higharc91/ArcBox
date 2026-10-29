"""The context build.py hands to each asset module.

An asset module is any tools/blender/<name>.py (not build.py / check.py / _*.py) that defines:
  GROUP = "cars"                      # the --only group it belongs to (cars, drivers, podium, landmarks, ...)
  def build(ctx: Context) -> None     # builds and exports its .glb files (blend.reset() first)
  def contracts() -> list[Contract]   # what its exports must satisfy (checked after build and by check.py)
and optionally
  def preview(ctx: Context) -> None   # Cycles preview renders into ctx.preview_dir (run with --preview)
"""
import json
import os
from dataclasses import dataclass, field

TOOLS_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO_ROOT = os.path.dirname(os.path.dirname(TOOLS_DIR))
MODELS_DIR = os.path.join(REPO_ROOT, "src", "ArcBox.Host", "games", "redline", "models")
GRID_JSON = os.path.join(REPO_ROOT, "games", "Redline", "Redline.Logic", "Data", "grid2026.json")


@dataclass
class Context:
    models_dir: str = MODELS_DIR
    preview_dir: str = ""
    preview: bool = False
    args: dict = field(default_factory=dict)

    def model(self, rel):
        """Absolute path for an export, e.g. ctx.model("car_2026.glb")."""
        return os.path.join(self.models_dir, rel)


def grid():
    """grid2026.json (the single source of liveries, numbers and driver codes)."""
    with open(GRID_JSON, encoding="utf-8") as f:
        return json.load(f)


def team(team_id):
    for t in grid()["teams"]:
        if t["id"] == team_id:
            return t
    raise KeyError(team_id)
