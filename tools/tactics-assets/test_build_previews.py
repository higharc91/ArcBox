"""Review output is explicit, while the content writer stays available."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from PIL import Image

from arcpix import out
from arcpix.anim import stage_gif


class PreviewOutputTests(unittest.TestCase):
    def tearDown(self):
        out.set_previews(True)

    def test_normal_build_omits_review_png_and_gif(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            with patch.object(out, "PREVIEWS", root / "previews"), patch.object(out, "MOCKUPS", root / "mockups"):
                out.set_previews(False)
                image = Image.new("RGB", (1, 1))
                out.save(image, root / "pack.png")
                out.save(image, out.PREVIEWS / "sheet.png")
                out.save(image, out.MOCKUPS / "mockup.png")
                stage_gif(out.PREVIEWS / "motion.gif", "unused", [], [])
                self.assertTrue((root / "pack.png").exists())
                self.assertFalse(out.PREVIEWS.exists())
                self.assertFalse(out.MOCKUPS.exists())

                out.set_previews(True)
                out.save(image, out.PREVIEWS / "sheet.png")
                self.assertTrue((out.PREVIEWS / "sheet.png").exists())


if __name__ == "__main__":
    unittest.main()
