#!/usr/bin/env python3
"""Check the live documentation contracts without network access."""
from __future__ import annotations
import argparse
from pathlib import Path
import re
import sys


def check(root: Path) -> list[str]:
    errors: list[str] = []
    index = root / "docs/superpowers/README.md"
    if not index.is_file():
        errors.append("Missing docs/superpowers/README.md tracker index")
    for plan in sorted((root / "docs/superpowers/plans").glob("*.md")):
        if not plan.read_text(encoding="utf-8").startswith("> Status:"):
            errors.append(f"{plan.relative_to(root)} needs a first-line status")
    stale = {
        "tools/tactics-world/README.md": ["You can't walk it yet", "from 5b on"],
        "games/Tactics/README.md": ["9,000 battles and takes about 5 minutes"],
        "docs/superpowers/specs/2026-11-02-tactics-shops-and-gold-design.md": [
            "service prices (rooms, cures) don't haggle", "have nothing to haggle over"],
        "docs/superpowers/plans/overworld-issue-board.md": [
            "whether rooms and cures should haggle (they don't)",
            "No PR, merge or issue closure has been requested.",
            "Issue #76 is being implemented"],
        "content/tactics/base/rules/shops/ashen_ford.json": ["af_market_burned"],
    }
    for name, phrases in stale.items():
        body = (root / name).read_text(encoding="utf-8")
        for phrase in phrases:
            if phrase in body:
                errors.append(f"{name}: stale text {phrase!r}")
    board = (root / "docs/superpowers/plans/overworld-issue-board.md").read_text(encoding="utf-8")
    if re.search(r"^### OW-36[^\n]*`doing`", board, re.M):
        errors.append("Closed #74 / OW-36 is still marked doing")
    # The Results text in screens.tsx is explicitly protected by the current task.
    # Its remaining change stays in #146/#204, rather than silently passing a code-wide audit.
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    errors = check(args.root)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print("OK: documentation statuses and known drift checks (protected Results wording remains tracked in #146/#204)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
