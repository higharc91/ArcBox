"""Runtime tactical art contracts. Run: python3 tools/tactics-assets/test_tactical.py."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
import numpy as np
from PIL import Image
from arcpix.out import PACK
from parts.creator import RACES,CLASSES
from parts.rig import ANIMS,Pose,frame_list
from parts.tactical_art.animation import FRAME,COLUMNS,frame,transform,dagger_grip,dagger_transform
from parts.tactical_art.build import bake


class TacticalTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root=PACK/'tactical'
        cls.manifest=json.loads((cls.root/'manifest.json').read_text())

    def test_every_race_class_and_animation_is_exported(self):
        expected={f'{race}_{role}' for race in RACES for role in CLASSES}
        self.assertEqual(expected,set(self.manifest['presets']))
        self.assertEqual(len(frame_list()),self.manifest['framesPerDirection'])
        self.assertEqual(set(ANIMS),set(self.manifest['anims']))
        for key in expected:
            with Image.open(self.root/f'{key}.png') as im:
                self.assertEqual((3840,2560),im.size,key)
        for anim in self.manifest['anims'].values():
            self.assertTrue(anim['frames'])
            self.assertTrue(all(0<=i<len(frame_list()) for i in anim['frames']))

    def test_baked_front_and_back_frames_match_current_renderer(self):
        poses=frame_list();rows=(len(poses)+COLUMNS-1)//COLUMNS
        for role,race in [('fighter','human'),('wizard','dragonborn'),('cleric','dwarf'),('warlock','tiefling')]:
            with Image.open(self.root/f'{race}_{role}.png') as sheet:
                for back in (False,True):
                    for index in (0,12,22,39,97,137):
                        x=index%COLUMNS*FRAME[0];y=(int(back)*rows+index//COLUMNS)*FRAME[1]
                        actual=np.array(sheet.crop((x,y,x+FRAME[0],y+FRAME[1])))
                        expected=np.array(frame(role,race,back,poses[index][2]))
                        self.assertTrue(np.array_equal(actual,expected),(role,race,back,index))

    def test_extreme_poses_fit_and_actions_do_not_repeat_idle(self):
        for role,race in [('fighter','half_orc'),('wizard','dragonborn'),('rogue','halfling'),('cleric','dwarf'),('warlock','tiefling')]:
            idle=np.array(frame(role,race,False,Pose()))
            for anim in ('walk','attack','cast','down','death','summon','jump'):
                images=[]
                for back in (False,True):
                    for pose in ANIMS[anim][2]:
                        im=np.array(frame(role,race,back,pose));a=im[:,:,3]
                        self.assertFalse(a[0].any() or a[-1].any() or a[:,0].any() or a[:,-1].any(),(role,race,anim,pose))
                        images.append(im)
                self.assertTrue(any(not np.array_equal(idle,im) for im in images))

    def test_bake_is_deterministic(self):
        with tempfile.TemporaryDirectory() as root:
            key=bake(('fighter','human',root))
            expected=hashlib.sha256((self.root/f'{key}.png').read_bytes()).digest()
            self.assertEqual(expected,hashlib.sha256((Path(root)/f'{key}.png').read_bytes()).digest())

    def test_dwarf_hair_stays_attached_to_the_head_in_every_pose(self):
        for anim,_,pose in frame_list():
            self.assertTrue(np.array_equal(transform('hair',pose,'dwarf'),transform('head',pose,'dwarf')),anim)
        # Long-haired characters still retain their existing secondary motion.
        pose=Pose(drag=(2,1))
        self.assertFalse(np.array_equal(transform('hair',pose,'elf'),transform('head',pose,'elf')))

    def test_each_rogue_dagger_tracks_its_own_hand_in_every_race_and_pose(self):
        for race in RACES:
            for anim,_,pose in frame_list():
                for blade,arm in (('weapon','near-hand'),('offhand','far-arm')):
                    grip=dagger_grip(blade,race)
                    self.assertTrue(np.allclose(dagger_transform(blade,pose,race)@grip,
                        transform(arm,pose,race)@grip),(race,anim,blade))

if __name__=='__main__':unittest.main()
