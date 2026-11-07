"""Runtime tactical art contracts. Run: python3 tools/tactics-assets/test_tactical.py."""
import hashlib
import json
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
import numpy as np
from PIL import Image
from arcpix.out import PACK
from parts.creator import RACES,CLASSES
from parts.rig import ANIMS,Pose,frame_list
from parts.tactical_art.animation import (FRAME,COLUMNS,frame,transform,rest,warp,
    layer_transform,item_transform,arm_transform,hand_target,validate_contacts)
from parts.tactical_art.attachments import ATTACHMENTS,HAND_POINTS,validate_attachments,Attachment
from parts.tactical_art.races import fit_point
from arcpix.canvas import Canvas
from parts.characters import SLOTS
from parts.tactical_art.build import bake
from parts.wizard_casts import CASTS
from parts.class_casts import poses_for
from parts.sorcerer_casts import CASTS as SORCERER_CASTS
from parts.warlock_casts import CASTS as WARLOCK_CASTS
from parts.cleric_casts import CASTS as CLERIC_CASTS
from parts.druid_casts import CASTS as DRUID_CASTS
from parts.paladin_casts import CASTS as PALADIN_CASTS
from parts.bard_casts import CASTS as BARD_CASTS
from parts.ranger_casts import CASTS as RANGER_CASTS


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
                self.assertEqual((3840,3200 if key.endswith('_wizard') else 2880 if key.endswith(('_sorcerer','_warlock','_cleric','_druid','_paladin','_bard','_ranger')) else 2560),im.size,key)
        custom=self.manifest['classes']['wizard']
        self.assertEqual(223,custom['framesPerDirection'])
        self.assertEqual(set(CASTS),set(custom['anims']))
        for anim in custom['anims'].values():
            self.assertTrue(all(189<=i<223 for i in anim['frames']))
        custom=self.manifest['classes']['sorcerer']
        self.assertEqual(209,custom['framesPerDirection'])
        self.assertEqual(set(SORCERER_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<209 for i in anim['frames']))
        custom=self.manifest['classes']['warlock']
        self.assertEqual(210,custom['framesPerDirection'])
        self.assertEqual(set(WARLOCK_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<210 for i in anim['frames']))
        custom=self.manifest['classes']['cleric']
        self.assertEqual(209,custom['framesPerDirection'])
        self.assertEqual(set(CLERIC_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<209 for i in anim['frames']))
        custom=self.manifest['classes']['druid']
        self.assertEqual(214,custom['framesPerDirection'])
        self.assertEqual(set(DRUID_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<214 for i in anim['frames']))
        custom=self.manifest['classes']['paladin']
        self.assertEqual(214,custom['framesPerDirection'])
        self.assertEqual(set(PALADIN_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<214 for i in anim['frames']))
        custom=self.manifest['classes']['bard']
        self.assertEqual(214,custom['framesPerDirection'])
        self.assertEqual(set(BARD_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<214 for i in anim['frames']))
        custom=self.manifest['classes']['ranger']
        self.assertEqual(215,custom['framesPerDirection'])
        self.assertEqual(set(RANGER_CASTS),set(custom['anims']))
        for anim in custom['anims'].values():self.assertTrue(all(189<=i<215 for i in anim['frames']))
        for anim in self.manifest['anims'].values():
            self.assertTrue(anim['frames'])
            self.assertTrue(all(0<=i<len(frame_list()) for i in anim['frames']))

    def test_baked_front_and_back_frames_match_current_renderer(self):
        for role,race in [('fighter','human'),('wizard','dragonborn'),('sorcerer','human'),('cleric','dwarf'),('warlock','tiefling'),('druid','elf'),('paladin','half_orc'),('bard','gnome'),('ranger','elf')]:
            poses=poses_for(role);rows=(len(poses)+COLUMNS-1)//COLUMNS
            with Image.open(self.root/f'{race}_{role}.png') as sheet:
                for back in (False,True):
                    for index in (0,12,22,39,97,137)+(tuple(range(189,215 if role=='ranger' else 223 if role=='wizard' else 210 if role=='warlock' else 214 if role in ('druid','paladin','bard','ranger') else 209)) if role in ('wizard','sorcerer','warlock','cleric','druid','paladin','bard','ranger') else ()):
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
            for role in ('fighter','wizard','sorcerer','warlock','cleric','druid','paladin','bard','ranger'):
                key=bake((role,'human',root))
                expected=hashlib.sha256((self.root/f'{key}.png').read_bytes()).digest()
                self.assertEqual(expected,hashlib.sha256((Path(root)/f'{key}.png').read_bytes()).digest())

    def test_dwarf_hair_stays_attached_to_the_head_in_every_pose(self):
        for anim,_,pose in frame_list():
            self.assertTrue(np.array_equal(transform('hair',pose,'dwarf'),transform('head',pose,'dwarf')),anim)
        # Long-haired characters still retain their existing secondary motion.
        pose=Pose(drag=(2,1))
        self.assertFalse(np.array_equal(transform('hair',pose,'elf'),transform('head',pose,'elf')))

    def test_material_finishing_preserves_part_coverage_and_materials(self):
        from parts.tactical_art.base import actor
        from parts.tactical_art.materials import finish_base,finish_equipment
        from parts.characters import render_layer
        from parts.material_finish import finish_part
        from parts.rig import points,BUILDS
        for back in (False,True):
            for outfit in ('robe','mail','leather'):
                _,_,layers=actor('fighter',back,outfit)
                original={name:c.slot.copy() for name,c in layers.items()}
                finish_base(layers,outfit,back);finish_equipment(layers)
                for name,c in layers.items():
                    self.assertTrue(np.array_equal(original[name],c.slot),(outfit,back,name))
                    self.assertTrue((c.shade<=5).all())
        for direction in ('front','back'):
            for build in BUILDS:
                for anim,_,pose in frame_list()[::19]:
                    for layer,variant in [('torso','rags'),('torso','plate'),('legs','skirt'),('weapon','sword')]:
                        c=render_layer(layer,variant,build,direction,pose).copy()
                        original=c.slot.copy()
                        finish_part(c,layer,variant,points(BUILDS[build],pose,direction))
                        self.assertTrue(np.array_equal(original,c.slot),(build,direction,anim,variant))
                        self.assertTrue((c.shade<=5).all())
        # A one-pixel blade tip is both an upper and lower boundary. Its bright
        # rim must win rather than being overwritten by the lower-edge shadow.
        tip=Canvas(8,8,SLOTS);tip.px(3,3,'blade',3)
        finish_equipment({'weapon':tip})
        self.assertEqual(5,tip.shade[3,3])

    def test_contact_shadow_preserves_transparency_and_emissive_materials(self):
        from parts.tactical_art.materials import contact_shadow
        c=Canvas(8,8,SLOTS)
        c.rect(2,2,4,4,'cloth1',3);c.px(5,4,'magic',5)
        slots=c.slot.copy();mask=np.zeros((8,8),bool);mask[2:4,2:5]=True
        contact_shadow(c,mask)
        self.assertTrue(np.array_equal(slots,c.slot))
        self.assertEqual(2,c.shade[4,4])
        self.assertEqual(3,c.shade[2,2])
        self.assertEqual(5,c.shade[4,5])
        self.assertFalse(c.flags.any())

    def test_baked_enemy_materials_match_recolourable_layers_in_action(self):
        from parts.characters import compose
        from parts.recipes import ENEMIES
        # Check every humanoid enemy in both facings, not only a front idle.
        poses=frame_list()
        samples=[0]+[i for i,(name,_,_) in enumerate(poses)
            if name in ('walk','attack','cast','down')]
        for recipe in ENEMIES:
            with Image.open(PACK/f'characters/presets/{recipe.id}.png') as sheet:
                for back in (False,True):
                    for index in samples:
                        actual=np.array(sheet.crop((index*32,int(back)*40,(index+1)*32,(int(back)+1)*40)))
                        expected=np.array(compose(recipe,'back' if back else 'front',poses[index][2]).render(recipe.ramps()))
                        self.assertTrue(np.array_equal(actual,expected),(recipe.id,back,poses[index][:2]))

    def test_every_held_item_and_hand_agree_for_every_race_facing_and_pose(self):
        self.assertEqual(set(CLASSES),set(ATTACHMENTS))
        for race in RACES:
            for role in CLASSES:
                for back in (False,True):
                    _,layers=rest(role,race,back)
                    for anim,_,pose in poses_for(role):
                        for name,definition in ATTACHMENTS[role].items():
                            if name not in layers:continue
                            source=np.r_[fit_point(definition.grip,race),1.]
                            expected=hand_target(role,definition.hand,pose,race,back)
                            actual=item_transform(role,name,pose,race,back)@source
                            self.assertTrue(np.allclose(actual,expected),(race,role,back,anim,name))
                            palm=np.r_[fit_point(HAND_POINTS[definition.hand],race),1.]
                            wrist=arm_transform(role,definition.hand,pose,race,back)@palm
                            self.assertTrue(np.allclose(wrist,expected),(race,role,back,anim,name,'wrist'))

    def test_rasterized_grips_stay_connected_across_all_characters(self):
        # Every walking frame plus the endpoints of every other animation.
        poses=list(ANIMS['walk'][2])
        for _,_,sequence in ANIMS.values():
            poses.extend((sequence[0],sequence[-1]))
        for race in RACES:
            for role in CLASSES:
                for back in (False,True):
                    _,layers=rest(role,race,back)
                    custom=[p for _,_,sequence in (CLERIC_CASTS if role=='cleric' else PALADIN_CASTS if role=='paladin' else BARD_CASTS if role=='bard' else RANGER_CASTS if role=='ranger' else DRUID_CASTS).values() for p in sequence] if role in ('cleric','druid','paladin','bard','ranger') else []
                    for pose in poses+custom:
                        posed={}
                        for name,image in layers.items():
                            if name in ATTACHMENTS[role] or name in ('near-hand','far-arm','near-palm','far-palm'):
                                posed[name]=np.array(warp(image,layer_transform(role,name,pose,race,back)))[:,:,3]>0
                        validate_contacts(role,race,back,pose,posed)

    def test_missing_metadata_and_detached_art_fail_validation(self):
        with self.assertRaises(ValueError):Attachment('near',(1,2),kind='held-typo')
        art=Canvas(64,80,SLOTS);art.rect(10,10,12,12,'metal')
        with self.assertRaisesRegex(ValueError,'no hand/grip attachment'):
            validate_attachments('monk',{'weapon':art})
        posed={'weapon':np.zeros((160,160),bool),'near-palm':np.zeros((160,160),bool)}
        with self.assertRaisesRegex(ValueError,'Detached'):
            validate_contacts('wizard','dragonborn',False,Pose(),posed)

    def test_renderer_refuses_art_that_no_longer_reaches_its_declared_grip(self):
        palette,layers=rest('wizard','dragonborn',False)
        broken=dict(layers)
        art=Canvas(64,80,SLOTS);art.rect(3,15,5,60,'wood')
        broken['weapon']=art.ramp_map()
        with patch('parts.tactical_art.animation.rest',return_value=(palette,broken)):
            with self.assertRaisesRegex(ValueError,'Detached dragonborn/wizard/weapon'):
                frame('wizard','dragonborn',False,Pose())

    def test_palms_do_not_vanish_when_a_cast_bends_the_forearm_sharply(self):
        pose=ANIMS['cast'][2][2]
        for race in RACES:
            for role in CLASSES:
                for back in (False,True):
                    _,layers=rest(role,race,back)
                    for name in ('near-palm','far-palm'):
                        self.assertIn(name,layers)
                        im=warp(layers[name],layer_transform(role,name,pose,race,back))
                        self.assertTrue(np.array(im)[:,:,3].any(),(race,role,back,name))
                    frame(role,race,back,pose)

if __name__=='__main__':unittest.main()
