"""Content coverage, geometry, one-shot motion and corruption regressions for the full set."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
import numpy as np
from PIL import Image,ImageOps
import audit_compartments
import compartments as art
import build as style


class CompartmentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary=tempfile.TemporaryDirectory()
        cls.root=Path(cls.temporary.name)
        cls.pack,cls.review=art.build(cls.root)

    @classmethod
    def tearDownClass(cls): cls.temporary.cleanup()

    def test_all_content_ids_deck_variants_and_immutable_bytes_are_covered(self):
        self.assertEqual({'tileIds':48,'physicalTiles':50,'variants':57,'referencedAssets':70},audit_compartments.audit(self.pack,art.REFERENCE))

    def test_every_room_has_a_distinct_grayscale_composition(self):
        signatures=[]
        for rule in art.rules():
            im=ImageOps.grayscale(art.tile(rule,rule['decks'][0])).crop((8,8,56,46))
            signatures.append(im.tobytes())
        self.assertEqual(len(signatures),len(set(signatures)))

    def test_no_unknown_room_gets_a_placeholder(self):
        with self.assertRaises(ValueError): art.draw_room(art.base('habitat'),'unknown_room','habitat')

    def test_missing_room_variant_and_rules_drift_fail_the_audit(self):
        path=self.pack/'tiles/manifest.json'; original=path.read_bytes()
        for mutation in ['room','variant','metadata','name']:
            value=json.loads(original)
            if mutation=='room': del value['tiles']['med_bay']
            elif mutation=='variant': del value['tiles']['service_corridor']['variants']['command']
            elif mutation=='name': value['tiles']['med_bay']['name']='Wrong name'
            else: value['tiles']['maintenance_shaft']['rule']=None
            try:
                path.write_text(json.dumps(value))
                with self.assertRaises(ValueError): audit_compartments.audit(self.pack,art.REFERENCE)
            finally: path.write_bytes(original)

    def test_corrupt_asset_is_rejected(self):
        value=json.loads((self.pack/'tiles/manifest.json').read_text())['tiles']['bridge']['variants']['command']
        path=self.pack/'tiles'/value['file']; original=path.read_bytes()
        try:
            path.write_bytes(original+b'damaged')
            with self.assertRaises(ValueError): audit_compartments.audit(self.pack,art.REFERENCE)
        finally: path.write_bytes(original)

    def test_all_variants_rotate_with_clear_doorways_and_symmetric_wall_light(self):
        for rule in art.rules():
            for deck in rule['decks']:
                image=art.tile(rule,deck)
                self.assertEqual((64,64),image.size)
                pixels=np.array(image)
                for turn in range(4):
                    alpha=np.array(image.rotate(turn*90))[:,:,3]
                    self.assertFalse(alpha[:7,24:40].any(),(rule['id'],deck,turn))
                for offset in range(4):
                    for edge in [pixels[63-offset,8:20],pixels[8:20,offset],pixels[8:20,63-offset]]:
                        self.assertTrue(np.array_equal(pixels[offset,8:20],edge),rule['id'])

    def test_wall_composition_follows_rules_and_rotates(self):
        for rule in art.rules():
            im=art.tile(rule,rule['decks'][0]); composed=art.compose(im,rule)
            for i,direction in enumerate(art.DIRECTIONS):
                north=composed.rotate(-90*i)
                alpha=np.array(north)[:6,28:36,3]
                self.assertEqual(direction not in rule['hatches'],bool(alpha.all()),(rule['id'],direction))
                if direction in rule['hatches']: self.assertFalse(alpha.any())
            for turn in range(4):
                self.assertEqual(composed.rotate(90*turn).tobytes(),art.compose(im,rule,turn=turn).tobytes())

    def test_symbols_have_distinct_grayscale_shapes_in_every_rotation(self):
        shapes={kind:[ImageOps.grayscale(art.symbol(kind).rotate(turn*90)).tobytes() for turn in range(4)] for kind in ['incident','salvage','anomaly']}
        for first in shapes:
            for second in shapes:
                if first!=second: self.assertFalse(set(shapes[first])&set(shapes[second]))

    def test_hatch_animation_endpoints_and_safe_bounds(self):
        for reduced in [False,True]:
            for kind,first,last in [('opening','closed','open'),('sealing',None,'sealed')]:
                frames=art.hatch_frames(kind,reduced)
                self.assertLessEqual(len(frames)/10,1)
                if first: self.assertEqual(style.hatch(first).tobytes(),frames[0].tobytes())
                self.assertEqual(style.hatch(last).tobytes(),frames[-1].tobytes())
                self.assertGreater(len({im.tobytes() for im in frames}),3)
                for frame in frames:
                    pixels=np.array(frame)
                    self.assertFalse(pixels[8:,:,3].any())
                    self.assertFalse(pixels[:,:23,3].any())
                    self.assertFalse(pixels[:,41:,3].any())
        self.assertNotEqual(art.one_way().tobytes(),style.hatch('open').tobytes())

    def test_manifest_motion_is_one_shot_with_reduced_motion_rest_state(self):
        manifest=json.loads((self.pack/'hatches/manifest.json').read_text())
        for kind,modes in manifest['animations'].items():
            self.assertEqual({'full','reduced'},set(modes))
            for clip in modes.values():
                self.assertFalse(clip['loop']); self.assertEqual(10,clip['fps'])
                self.assertEqual(clip['frames']/clip['fps'],clip['seconds'])
                self.assertEqual('open' if kind=='opening' else 'sealed',clip['restState'])
                self.assertIn('without animation',clip['reducedMotion'])

    def test_second_full_build_preserves_every_asset_and_review_byte(self):
        before={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).digest() for p in self.root.rglob('*') if p.is_file()}
        art.build(self.root)
        after={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).digest() for p in self.root.rglob('*') if p.is_file()}
        self.assertEqual(before,after)

    def test_style_sample_builder_cannot_replace_the_complete_set(self):
        path=self.pack/'tiles/manifest.json'; original=path.read_bytes()
        with self.assertRaises(ValueError): style.build(self.root)
        self.assertEqual(original,path.read_bytes())

    def test_native_mock_sizes_and_no_visible_black_or_white(self):
        for deck in art.DECKS:
            with Image.open(self.review/(deck+'-mock.png')) as im: self.assertEqual((1920,1080),im.size)
        for rule in art.rules():
            pixels=np.array(art.tile(rule,rule['decks'][0]))
            visible=pixels[:,:,:3][pixels[:,:,3]>0]
            self.assertFalse((visible==0).all(axis=1).any())
            self.assertFalse((visible==255).all(axis=1).any())


if __name__=='__main__': unittest.main()
