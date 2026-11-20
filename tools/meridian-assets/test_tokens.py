import hashlib,json,shutil,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import numpy as np
from PIL import Image,ImageOps
import tokens,audit_tokens
from arcpix.canvas import Canvas,SlotTable

class TokenTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp=tempfile.TemporaryDirectory();cls.root=Path(cls.temp.name);cls.pack,cls.report=tokens.build(cls.root)
        cls.manifest=json.loads((cls.pack/'tokens/manifest.json').read_text())
    @classmethod
    def tearDownClass(cls):cls.temp.cleanup()
    def test_all_twelve_scenarios_and_canonical_ids_are_covered(self):
        self.assertEqual({'scenarioKinds':18,'monsters':3,'scenarios':12,'uniquePngs':81,'facingClips':60,'declaredStates':4},audit_tokens.audit(self.pack))
    def test_every_monster_has_four_actual_runtime_facings_and_attack(self):
        for key in tokens.MONSTERS:
            g=self.manifest['tokens'][key]
            for clip in ['idle','walk','hurt','stun','attack']:
                self.assertEqual({'n','e','s','w'},set(g[clip]))
            self.assertEqual(4,len({tokens.monster(key,s).tobytes() for s in tokens.SIDES}))
    def test_move_cycles_have_six_distinct_frames_other_clips_have_motion(self):
        for key in tokens.MONSTERS:
            for side in tokens.SIDES:
                for clip,(n,fps,loop) in tokens.CLIPS.items():
                    count=len({tokens.monster(key,side,clip,i).tobytes() for i in range(n)})
                    self.assertGreaterEqual(count,6 if clip=='walk' else 2,(key,side,clip))
                    if not loop:self.assertLessEqual(n/fps,1)
    def test_sprite_recipes_never_draw_outside_their_frame(self):
        class GuardCanvas(Canvas):
            def px(self,x,y,slot,shade=2):
                if not self._ok(int(round(x)),int(round(y))):raise ValueError((x,y,slot))
                return super().px(x,y,slot,shade)
        with patch.object(tokens.style,'canvas',side_effect=lambda w,h:GuardCanvas(w,h,SlotTable())):
            for key in tokens.MONSTERS:
                for side in tokens.SIDES:
                    for clip,(n,_,_) in tokens.CLIPS.items():
                        for f in range(n):tokens.monster(key,side,clip,f)
            for key in set(tokens.NAMES)-tokens.MONSTERS:
                for state in tokens.STATES.get(key,[None]):tokens.token(key,state)
            tokens.token('hidden')
    def test_objects_and_monsters_have_distinct_greyscale_and_silhouettes(self):
        images=[tokens.still(self.manifest,k) for k in tokens.NAMES]
        self.assertEqual(18,len({ImageOps.grayscale(i).tobytes() for i in images}))
        self.assertEqual(18,len({i.getchannel('A').tobytes() for i in images}))
    def test_repair_and_relay_states_change_geometry_not_just_colour(self):
        for key,states in tokens.STATES.items():
            a,b=[np.asarray(tokens.token(key,s)) for s in states]
            self.assertGreater(np.count_nonzero(a[:,:,3]!=b[:,:,3]),10,key)
    def test_palette_no_visible_pure_black_or_white(self):
        for key in tokens.NAMES:
            im=tokens.still(self.manifest,key);p=np.asarray(im);rgb=p[:,:,:3][p[:,:,3]>0]
            self.assertFalse((rgb==0).all(axis=1).any(),key);self.assertFalse((rgb==255).all(axis=1).any(),key)
    def test_private_objects_and_hidden_marker_cannot_be_public_defaults(self):
        self.assertEqual('private',self.manifest['tokens']['sabotage']['visibility'])
        self.assertEqual('hidden-until-found',self.manifest['tokens']['charge']['visibility'])
        self.assertNotIn('sabotage',[k for k in tokens.NAMES if k not in tokens.PRIVATE])
        self.assertNotIn('hidden',self.manifest['tokens'])
        self.assertEqual('private-reference-only',self.manifest['extras']['hiddenMarker']['visibility'])
    def test_actual_initial_states_are_backed_by_current_scenario_source(self):
        self.assertIn('State: "powered"',(tokens.REFERENCE/'ColdSleep.cs').read_text())
        self.assertIn('State: "broken"',(tokens.REFERENCE/'OneOfUsIsnt.cs').read_text())
        for key,state in tokens.BASE.items():self.assertEqual(self.manifest['tokens'][key][state],self.manifest['tokens'][key]['idle'])
    def test_missing_kind_facing_state_or_corrupt_hash_fails(self):
        path=self.pack/'tokens/manifest.json';original=path.read_bytes()
        for which in ['kind','facing','state','hash','private']:
            m=json.loads(original)
            if which=='kind':del m['tokens']['dose']
            elif which=='facing':del m['tokens']['drone']['walk']['w']
            elif which=='state':del m['tokens']['repair']['fixed']
            elif which=='hash':m['tokens']['item']['idle']['sha256']='0'*64
            else:m['tokens']['sabotage']['visibility']='public'
            try:
                path.write_text(json.dumps(m))
                with self.assertRaises(ValueError):audit_tokens.audit(self.pack)
            finally:path.write_bytes(original)
    def test_new_scenario_kinds_and_states_require_authored_recipes(self):
        with tempfile.TemporaryDirectory() as d:
            ref=Path(d)/'reference';shutil.copytree(tokens.REFERENCE,ref)
            p=ref/'scenarios/company_asset.json';m=json.loads(p.read_text());m['tokens']['scanner']={'name':'New scanner'};p.write_text(json.dumps(m))
            with self.assertRaises(ValueError):tokens.build(Path(d)/'output',ref)
        with self.assertRaises(ValueError):tokens.monster('dragon')
        with self.assertRaises(ValueError):tokens.token('relay','unknown')
    def test_rebuild_is_identical_and_review_describes_scale_and_reduced_motion(self):
        def hashes():return {str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}
        first=hashes();tokens.build(self.root);self.assertEqual(first,hashes())
        page=(self.report/'index.html').read_text();self.assertIn('prefers-reduced-motion',page);self.assertIn('48px',page);self.assertIn('Private reference',page)
        for name in ['tokens','monsters','states']:
            with Image.open(self.report/f'{name}-tv.png') as im:self.assertEqual((1920,1080),im.size)
