"""Quick look at effects while drawing them: python3 dev_fx.py OUT.png [id[:variant] ...] (all when none given).

Each effect's frames go side by side at 4x on the navy panel colour, one row per effect.
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from PIL import Image  # noqa: E402

from arcpix.preview import PANEL, scale  # noqa: E402
from parts import fx  # noqa: E402


def main() -> None:
    out = Path(sys.argv[1])
    wanted = sys.argv[2:] or [e.id for e in fx.CATALOGUE]
    rows = []
    for w in wanted:
        fid, _, var = w.partition(":")
        e = next(x for x in fx.CATALOGUE if x.id == fid)
        frames = e.make()
        pal = fx.ramps(e.variants[var or next(iter(e.variants))])
        imgs = [f.render(pal) for f in frames]
        fw, fh = imgs[0].size
        row = Image.new("RGBA", (len(imgs) * (fw + 2), fh), PANEL)
        for k, im in enumerate(imgs):
            row.alpha_composite(im, (k * (fw + 2), 0))
        rows.append(scale(row, int(__import__("os").environ.get("Z", "4"))))
    W = max(r.width for r in rows)
    sheet = Image.new("RGBA", (W, sum(r.height + 8 for r in rows)), (13, 15, 26, 255))
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y))
        y += r.height + 8
    sheet.save(out)


if __name__ == "__main__":
    main()
