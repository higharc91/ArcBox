"""Quick look at signature effects while drawing them: python3 dev_signature.py OUT.png [id[:variant] ...]

Each effect's frames go side by side at Z x (default 4) on the panel colour, one row per effect. With GIF=path set,
also writes the preview GIF cells for just those effects.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from PIL import Image  # noqa: E402

from arcpix.preview import PANEL, scale  # noqa: E402
from parts import signature as S  # noqa: E402


def main() -> None:
    out = Path(sys.argv[1])
    wanted = sys.argv[2:] or [e.id for e in S.CATALOGUE]
    rows, rendered = [], {}
    for w in wanted:
        fid, _, var = w.partition(":")
        e = next(x for x in S.CATALOGUE if x.id == fid)
        frames = e.make()
        imgs = [f.render(S.ramps(e.variants[var or next(iter(e.variants))])) for f in frames]
        rendered[e.id] = {next(iter(e.variants)): imgs}
        fw, fh = imgs[0].size
        row = Image.new("RGBA", (len(imgs) * (fw + 2), fh), PANEL)
        for k, im in enumerate(imgs):
            row.alpha_composite(im, (k * (fw + 2), 0))
        rows.append(scale(row, int(os.environ.get("Z", "4"))))
    W = max(r.width for r in rows)
    sheet = Image.new("RGBA", (W, sum(r.height + 8 for r in rows)), (13, 15, 26, 255))
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y))
        y += r.height + 8
    sheet.save(out)
    if os.environ.get("GIF"):
        S._preview_gif(rendered, only=list(rendered), path=os.environ["GIF"], cols=min(len(rendered), 4))


if __name__ == "__main__":
    main()
