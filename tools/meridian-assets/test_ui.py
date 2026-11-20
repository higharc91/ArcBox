import hashlib,json,tempfile,unittest
from pathlib import Path
from PIL import Image,ImageOps
import numpy as np
import ui,audit_ui

class UiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp=tempfile.TemporaryDirectory();cls.root=Path(cls.temp.name);cls.pack,cls.report=ui.build(cls.root)
    @classmethod
    def tearDownClass(cls):cls.temp.cleanup()
    def test_complete_contract_and_supplementary_coverage(self):
        self.assertEqual({'uiPieces':13,'panels':10,'diceValues':[0,0,1,1,1,2],'lobbyBadges':6,'miniMarkers':6,'uniqueAssets':31},audit_ui.audit(self.pack))
    def test_all_panel_stretch_regions_are_uniform(self):
        for key in ui.PANELS:
            p=np.asarray(ui.panel(key));self.assertTrue((p[12:52,12:52]==p[32,32]).all(),key)
            self.assertTrue((p[:12,12:52]==p[:12,12:13]).all(),key)
            self.assertTrue((p[52:,12:52]==p[52:,12:13]).all(),key)
            self.assertTrue((p[12:52,:12]==p[12:13,:12]).all(),key)
            self.assertTrue((p[12:52,52:]==p[12:13,52:]).all(),key)
    def test_dice_settled_faces_and_tumble_are_distinct(self):
        self.assertEqual(3,len({ui.die(v).tobytes() for v in [0,1,2]}))
        self.assertEqual(4,len({ui.die(1,i).tobytes() for i in range(4)}))
        # Count the authored separated dark pip clusters in the settled centre.
        for v in [0,1,2]:
            p=np.asarray(ui.die(v));dark=np.all(p[6:18,6:18,:3]==[33,36,60],axis=2)
            seen=set();components=0
            for y,x in zip(*np.where(dark)):
                if (y,x) in seen:continue
                components+=1;stack=[(y,x)]
                while stack:
                    yy,xx=stack.pop()
                    if (yy,xx) in seen or not(0<=yy<12 and 0<=xx<12) or not dark[yy,xx]:continue
                    seen.add((yy,xx));stack.extend([(yy+1,xx),(yy-1,xx),(yy,xx+1),(yy,xx-1)])
            self.assertEqual(v,components)
    def test_seat_badges_and_markers_are_distinct_without_colour(self):
        for size in [16,24]:self.assertEqual(6,len({ImageOps.grayscale(ui.badge(i,size)).tobytes() for i in range(6)}))
        self.assertEqual((16,12),ui.auto_badge().size)
    def test_palette_has_no_visible_pure_black_or_white(self):
        for image in [ui.logo(),ui.title(),ui.alarm(),ui.backdrop(),ui.auto_badge(),*(ui.panel(k) for k in ui.PANELS),*(ui.die(i) for i in [0,1,2]),*(ui.badge(i) for i in range(6))]:
            p=np.asarray(image);p=p[:,:,:3][p[:,:,3]>0]
            self.assertFalse((p==0).all(axis=1).any());self.assertFalse((p==255).all(axis=1).any())
    def test_missing_piece_badge_or_die_and_corrupt_file_fail(self):
        path=self.pack/'ui/manifest.json';original=path.read_bytes()
        for target in ['piece','badge','face','values','insets','role']:
            m=json.loads(original)
            if target=='piece':del m['ui']['logPanel']
            elif target=='badge':del m['lobby']['badges']['seat6']
            elif target=='face':del m['ui']['die']['face2']
            elif target=='values':m['ui']['die']['values']=[2]*6
            elif target=='insets':m['ui']['panel']['insets']=[2]*4
            else:m['extras']['auto']['roleNeutral']=False
            try:
                path.write_text(json.dumps(m))
                with self.assertRaises(ValueError):audit_ui.audit(self.pack)
            finally:path.write_bytes(original)
        p=self.pack/'ui'/json.loads(original)['ui']['logo']['file'];data=p.read_bytes()
        try:
            p.write_bytes(data+b'damaged')
            with self.assertRaises(ValueError):audit_ui.audit(self.pack)
        finally:p.write_bytes(data)
    def test_review_has_every_phase_all_crew_and_public_scenario_copy(self):
        page=(self.report/'index.html').read_text()
        for phase in ui.PHASES:self.assertIn('data-phase="'+phase+'"',page)
        import html
        for c in ui.crew.rules():self.assertIn(html.escape(c['name']),page)
        scenario=json.loads(ui.REFERENCE.read_text());self.assertIn(html.escape(scenario['announcement']),page)
        self.assertIn('aria-pressed="false"',page);self.assertIn('prefers-reduced-motion',page)
    def test_unknown_recipe_is_rejected_and_rebuild_is_identical(self):
        with self.assertRaises(ValueError):ui.panel('unknown')
        with self.assertRaises(ValueError):ui.die(3)
        with self.assertRaises(ValueError):ui.badge(6)
        before={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).digest() for p in self.root.rglob('*') if p.is_file()}
        ui.build(self.root)
        self.assertEqual(before,{str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).digest() for p in self.root.rglob('*') if p.is_file()})
