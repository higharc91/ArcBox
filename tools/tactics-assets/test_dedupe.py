import tempfile
import unittest
from pathlib import Path
from arcpix.dedupe import dedupe_pngs


class LayerDedupeTests(unittest.TestCase):
    def test_identical_files_share_the_original_and_distinct_feminine_art_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for file, data in {"short_f/hair/bun.png": b"shared", "short/hair/bun.png": b"shared", "short_f/body/human.png": b"distinct"}.items():
                path = root / file
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            self.assertEqual({"short_f/hair/bun.png": "short/hair/bun.png"}, dedupe_pngs(root))
            self.assertEqual(b"distinct", (root / "short_f/body/human.png").read_bytes())
            self.assertEqual(b"shared", (root / "short/hair/bun.png").read_bytes())
            self.assertFalse((root / "short_f/hair/bun.png").exists())
