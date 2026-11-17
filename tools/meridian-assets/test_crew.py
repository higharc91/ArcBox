"""Coverage and actual image-contract regressions for the eight original crew designs."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
import numpy as np
from PIL import Image,ImageOps
import crew
import audit_crew


class CrewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary=tempfile.TemporaryDirectory();cls.root=Path(cls.temporary.name)
        cls.pack,cls.review=crew.build(cls.root)
        cls.frames={(key,facing,clip,i):crew.board(key,facing,clip,i) for key in crew.DESIGNS for facing in crew.FACINGS for clip,(n,_,_) in crew.CLIPS.items() for i in range(n)}

    @classmethod
    def tearDownClass(cls): cls.temporary.cleanup()

    def test_every_content_id_has_every_facing_pose_echo_and_portrait(self):
        self.assertEqual({'crewIds':8,'facings':4,'livingAndEchoBoardFrames':768,'portraitsAndCards':32,'uniqueAssets':296},audit_crew.audit(self.pack,crew.REFERENCE))

    def test_all_board_frames_preserve_the_feet_anchor_and_transparent_margin(self):
        for key,image in self.frames.items():
            pixels=np.asarray(image)
            self.assertEqual((24,24,4),pixels.shape)
            self.assertTrue(pixels[:,:,3].any(),key)
            self.assertFalse(pixels[23,:,3].any(),key)
            self.assertFalse(pixels[:,0,3].any(),key)
            self.assertFalse(pixels[:,23,3].any(),key)

    def test_all_four_facings_are_distinct_for_each_pose(self):
        for key in crew.DESIGNS:
            for clip in crew.CLIPS:
                frames=[self.frames[(key,facing,clip,0)].tobytes() for facing in crew.FACINGS]
                self.assertEqual(4,len(set(frames)),(key,clip))

    def test_every_crew_has_a_distinct_silhouette_in_every_standing_facing(self):
        for facing in crew.FACINGS:
            masks=[np.asarray(self.frames[(key,facing,'idle',0)])[:,:,3]>0 for key in crew.DESIGNS]
            self.assertEqual(8,len({m.tobytes() for m in masks}),facing)

    def test_six_walk_frames_are_distinct_and_both_legs_move(self):
        for key in crew.DESIGNS:
            for facing in crew.FACINGS:
                frames=[self.frames[(key,facing,'walk',i)] for i in range(6)]
                self.assertEqual(6,len({im.tobytes() for im in frames}),(key,facing))
                legs=[np.asarray(im)[17:23] for im in frames]
                self.assertGreater(len({p.tobytes() for p in legs}),3)

    def test_idle_and_hurt_actually_change_while_down_is_a_single_pose(self):
        for key in crew.DESIGNS:
            for facing in crew.FACINGS:
                for clip in ['idle','hurt']:
                    self.assertGreater(len({self.frames[(key,facing,clip,i)].tobytes() for i in range(crew.CLIPS[clip][0])}),1)
                self.assertEqual(1,crew.CLIPS['down'][0])

    def test_echo_keeps_coverage_and_character_without_temporal_flashing(self):
        for key,image in self.frames.items():
            pixels=np.asarray(image);echo=np.asarray(crew.echo_image(image))
            self.assertTrue(np.array_equal(pixels[:,:,3]>0,echo[:,:,3]>0),key)
            self.assertGreater(echo[:,:,3].max(),0);self.assertLess(echo[:,:,3].max(),255)
            self.assertFalse(np.array_equal(pixels,echo))

    def test_portraits_and_crew_cards_are_independent_drawings_with_distinct_expressions(self):
        for key in crew.DESIGNS:
            expressions=[crew.portrait(key,state) for state in ['neutral','hurt','echo']]
            self.assertTrue(all(im.size==(96,112) for im in expressions))
            self.assertEqual(3,len({im.tobytes() for im in expressions}))
            self.assertEqual((96,160),crew.portrait(key,card=True).size)
            self.assertNotEqual(expressions[0].crop((0,0,96,96)).tobytes(),self.frames[(key,'south','idle',0)].resize((96,96),Image.Resampling.NEAREST).tobytes())

    def test_all_visible_source_pixels_avoid_pure_black_and_white(self):
        images=list(self.frames.values())+[crew.portrait(key) for key in crew.DESIGNS]
        for image in images:
            pixels=np.asarray(image);rgb=pixels[:,:,:3][pixels[:,:,3]>0]
            self.assertFalse((rgb==0).all(axis=1).any());self.assertFalse((rgb==255).all(axis=1).any())

    def test_name_role_facing_pose_and_portrait_drift_are_rejected(self):
        path=self.pack/'crew/manifest.json';original=path.read_bytes()
        for mutation in ['name','role','facing','pose','alias']:
            value=json.loads(original);entry=value['crew']['ines_okafor']
            if mutation in ['name','role']:entry[mutation]='Wrong content'
            elif mutation=='facing':del entry['echo']['north']
            elif mutation=='pose':del entry['facings']['south']['hurt']
            else:entry['walk']=entry['idle']
            try:
                path.write_text(json.dumps(value))
                with self.assertRaises(ValueError):audit_crew.audit(self.pack,crew.REFERENCE)
            finally:path.write_bytes(original)
        path=self.pack/'portraits/manifest.json';original=path.read_bytes();value=json.loads(original)
        del value['portraits']['kit_sato']
        try:
            path.write_text(json.dumps(value))
            with self.assertRaises(ValueError):audit_crew.audit(self.pack,crew.REFERENCE)
        finally:path.write_bytes(original)

    def test_corrupted_hash_named_art_is_rejected(self):
        entry=json.loads((self.pack/'crew/manifest.json').read_text())['crew']['ines_okafor']['idle']
        path=self.pack/'crew'/entry['file'];original=path.read_bytes()
        try:
            path.write_bytes(original+b'corrupt')
            with self.assertRaises(ValueError):audit_crew.audit(self.pack,crew.REFERENCE)
        finally:path.write_bytes(original)

    def test_unknown_crew_cannot_silently_get_placeholder_art(self):
        with self.assertRaises(KeyError):crew.board('unknown_crew')
        with tempfile.TemporaryDirectory() as folder:
            source=Path(folder)/'crew.json';source.write_text('{"crew":[]}')
            with self.assertRaises(ValueError):crew.build(Path(folder),source)

    def test_full_rebuild_is_byte_identical(self):
        before={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}
        crew.build(self.root)
        after={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}
        self.assertEqual(before,after)

    def test_native_lineup_is_exact_1080p_and_source_files_stay_immutable(self):
        with Image.open(self.review/'lineup-tv.png') as image:self.assertEqual((1920,1080),image.size)
        with self.assertRaises(ValueError):crew.style.build(self.root)
        original=crew.board('ines_okafor');entry=crew.style.immutable(self.pack,'crew','immutability-check',original)
        path=self.pack/'crew'/entry['file'];data=path.read_bytes()
        try:
            path.write_bytes(b'changed')
            with self.assertRaises(ValueError):crew.style.immutable(self.pack,'crew','immutability-check',original)
        finally:path.write_bytes(data);path.unlink()


if __name__=='__main__':unittest.main()
