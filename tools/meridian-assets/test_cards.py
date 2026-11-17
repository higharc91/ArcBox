"""Real content coverage, shape/readability, nine-slice constraints and corruption regressions."""
import hashlib,json,tempfile,unittest
from pathlib import Path
import numpy as np
from PIL import Image,ImageOps
import cards,compartments,audit_cards


class CardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary=tempfile.TemporaryDirectory();cls.root=Path(cls.temporary.name)
        cls.pack,cls.review=cards.build(cls.root)
    @classmethod
    def tearDownClass(cls):cls.temporary.cleanup()

    def test_exact_live_card_and_icon_coverage(self):
        self.assertEqual({'cards':32,'salvageIcons':8,'anomalyIcons':12,'frames':3,'symbols':6,'referencedAssets':29},audit_cards.audit(self.pack,cards.REFERENCE))

    def test_all_twenty_objects_have_distinct_greyscale_art(self):
        images=[ImageOps.grayscale(cards.icon(key)).tobytes() for key in cards.RECIPES]
        self.assertEqual(20,len(set(images)))
        self.assertTrue(all(cards.icon(key).size==(24,24) for key in cards.RECIPES))

    def test_kind_glyphs_match_the_floor_family_and_remain_distinct_when_rotated(self):
        for kind in cards.KINDS:self.assertEqual(compartments.symbol(kind).tobytes(),cards.glyph(kind,11).tobytes())
        for size in [16,24]:
            signatures={kind:{ImageOps.grayscale(cards.glyph(kind,size).rotate(t*90)).tobytes() for t in range(4)} for kind in cards.KINDS}
            for first in cards.KINDS:
                for second in cards.KINDS:
                    if first!=second:self.assertFalse(signatures[first]&signatures[second])

    def test_stretch_regions_are_uniform_and_ornaments_stay_in_fixed_corners(self):
        images=[]
        for kind in cards.KINDS:
            p=np.asarray(cards.frame(kind));images.append(p.tobytes())
            self.assertTrue((p[:,:,3]==255).all())
            self.assertTrue((p[12:52,12:52]==p[32,32]).all())
            self.assertTrue((p[:12,12:52]==p[:12,12:13]).all())
            self.assertTrue((p[52:,12:52]==p[52:,12:13]).all())
            self.assertTrue((p[12:52,:12]==p[12:13,:12]).all())
            self.assertTrue((p[12:52,52:]==p[12:13,52:]).all())
        self.assertEqual(3,len(set(images)))

    def test_visible_asset_pixels_never_use_pure_black_or_white(self):
        images=[*(cards.frame(k) for k in cards.KINDS),*(cards.glyph(k,s) for k in cards.KINDS for s in [16,24]),*(cards.icon(k) for k in cards.RECIPES)]
        for im in images:
            p=np.asarray(im);rgb=p[:,:,:3][p[:,:,3]>0]
            self.assertFalse((rgb==0).all(axis=1).any());self.assertFalse((rgb==255).all(axis=1).any())

    def test_unknown_ids_kinds_and_effects_fail_instead_of_getting_placeholders(self):
        with self.assertRaises(ValueError):cards.icon('unknown_item')
        with self.assertRaises(ValueError):cards.frame('unknown_kind')
        with self.assertRaises(ValueError):cards.glyph('unknown_kind',16)
        with self.assertRaises(ValueError):cards.effect_text({'kind':'unknown_effect'})

    def test_review_contains_every_unchanged_real_title_and_flavour_text(self):
        from html import escape
        page=(self.review/'index.html').read_text()
        for records in cards.catalogue().values():
            for card in records:
                self.assertIn(escape(card['title']),page);self.assertIn(escape(card['text']),page)
        self.assertIn('Single use.',page);self.assertIn('Draw a Salvage card.',page)
        self.assertIn('Gain 2 physical steps.',page)
        self.assertIn('Move to Central Lift.',page)

    def test_missing_icon_card_symbol_or_kind_mapping_is_rejected(self):
        path=self.pack/'icons/manifest.json';original=path.read_bytes()
        for mutation in ['icon','symbol','canonical-symbol','kind']:
            v=json.loads(original)
            if mutation=='icon':del v['icons']['crowbar']
            elif mutation=='symbol':del v['symbols']['incident']['16']
            elif mutation=='canonical-symbol':del v['icons']['incident']
            else:v['icons']['crowbar']['kind']='anomaly'
            try:
                path.write_text(json.dumps(v))
                with self.assertRaises(ValueError):audit_cards.audit(self.pack,cards.REFERENCE)
            finally:path.write_bytes(original)
        path=self.pack/'cards/manifest.json';original=path.read_bytes();v=json.loads(original);del v['cards']['cold_spot']
        try:
            path.write_text(json.dumps(v))
            with self.assertRaises(ValueError):audit_cards.audit(self.pack,cards.REFERENCE)
        finally:path.write_bytes(original)

    def test_damaged_file_and_slice_metadata_are_rejected(self):
        path=self.pack/'cards/manifest.json';original=path.read_bytes();v=json.loads(original);entry=v['frames']['incident'];file=self.pack/'cards'/entry['file'];data=file.read_bytes()
        try:
            file.write_bytes(data+b'damage')
            with self.assertRaises(ValueError):audit_cards.audit(self.pack,cards.REFERENCE)
        finally:file.write_bytes(data)
        try:
            entry['insets']=[2,2,2,2];path.write_text(json.dumps(v))
            with self.assertRaises(ValueError):audit_cards.audit(self.pack,cards.REFERENCE)
        finally:path.write_bytes(original)

    def test_new_item_content_requires_authored_art_and_duplicate_ids_are_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            ref=Path(directory)/'rules';ref.mkdir()
            for p in cards.REFERENCE.glob('*.json'):(ref/p.name).write_bytes(p.read_bytes())
            target=ref/'salvage.json';v=json.loads(target.read_text());v['cards'].append({'id':'new_m7_item','title':'New','text':'New.'});target.write_text(json.dumps(v))
            with self.assertRaises(ValueError):cards.build(Path(directory)/'out',ref)
            v['cards'][-1]['id']='crowbar';target.write_text(json.dumps(v))
            with self.assertRaises(ValueError):cards.build(Path(directory)/'out',ref)

    def test_second_build_is_byte_identical(self):
        before={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}
        cards.build(self.root)
        after={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}
        self.assertEqual(before,after)


if __name__=='__main__':unittest.main()
