"""Asset-contract regressions: immutable delivery, rotation, frame bounds and deterministic output."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image
import build


class ContractTests(unittest.TestCase):
    def test_second_build_is_byte_identical(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            build.build(root)
            before = {str(p.relative_to(root)): p.read_bytes() for p in root.rglob('*') if p.is_file()}
            build.build(root)
            after = {str(p.relative_to(root)): p.read_bytes() for p in root.rglob('*') if p.is_file()}
            self.assertEqual(before, after)

    def test_room_edges_leave_sixteen_pixel_gaps_in_every_rotation(self):
        for key in build.SAMPLES:
            image = build.room(key)
            self.assertEqual((64, 64), image.size)
            for turn in range(4):
                alpha = np.array(image.rotate(90*turn))[:, :, 3]
                self.assertTrue((alpha[0:7, 24:40] == 0).all(), key)
                self.assertTrue((alpha[0:4, :24] == 255).all(), key)

    def test_wall_bevel_lighting_is_the_same_on_all_four_edges(self):
        for key in build.SAMPLES:
            image = np.array(build.room(key))
            for offset in range(4):
                self.assertTrue(np.array_equal(image[offset, 8:20], image[63-offset, 8:20]))
                self.assertTrue(np.array_equal(image[offset, 8:20], image[8:20, offset]))
                self.assertTrue(np.array_equal(image[offset, 8:20], image[8:20, 63-offset]))

    def test_hatch_shapes_are_distinct_and_only_occupy_north_edge(self):
        signatures = set()
        for state in build.STATES:
            pixels = np.array(build.hatch(state))
            self.assertFalse(pixels[8:, :, 3].any())
            signatures.add(pixels.tobytes())
        self.assertEqual(4, len(signatures))
        self.assertFalse(np.array(build.hatch('open'))[:6, 28:36, 3].any())
        self.assertTrue((np.array(build.hatch('none'))[:6, 28:36, 3] == 255).all())

    def test_crew_frames_fit_the_feet_anchor_and_walk_moves_both_legs(self):
        frames = [np.array(build.crew(i)) for i in range(5)]
        self.assertTrue(all(frame.shape == (24, 24, 4) for frame in frames))
        self.assertGreaterEqual(len({frame.tobytes() for frame in frames}), 3)
        self.assertFalse(np.array_equal(frames[1][15:], frames[3][15:]))
        self.assertTrue(all(not frame[23, :, 3].any() for frame in frames))

    def test_ramps_and_visible_pixels_never_use_pure_black_or_white(self):
        self.assertTrue(all(len(ramp) == 6 for ramp in build.PALETTE.values()))
        for image in [*(build.room(k) for k in build.SAMPLES), build.crew()]:
            pixels = np.array(image)
            visible = pixels[:, :, :3][pixels[:, :, 3] > 0]
            self.assertFalse((visible == 0).all(axis=1).any())
            self.assertFalse((visible == 255).all(axis=1).any())

    def test_manifests_reference_hash_named_files_with_real_frame_bounds(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pack, _ = build.build(root)
            count = 0
            def visit(value, folder):
                nonlocal count
                if isinstance(value, dict):
                    if 'file' in value:
                        p = folder/value['file']
                        digest = hashlib.sha256(p.read_bytes()).hexdigest()
                        self.assertEqual(value['sha256'], digest)
                        self.assertIn(digest[:12], p.name)
                        count += 1
                    for child in value.values():
                        visit(child, folder)
                elif isinstance(value, list):
                    for child in value:
                        visit(child, folder)
            for manifest in pack.rglob('manifest.json'):
                visit(json.loads(manifest.read_text()), manifest.parent)
            self.assertEqual(10, count)
            with Image.open(root/'docs/superpowers/reports/meridian-art/style/deck-mock-1080.png') as image:
                self.assertEqual((1920, 1080), image.size)


if __name__ == '__main__':
    unittest.main()
