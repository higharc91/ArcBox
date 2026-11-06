"""Overworld art contracts: shipped frames, family anchors and lore-specific art."""
import json
from pathlib import Path
import tempfile
import unittest
from collections import Counter
from unittest.mock import patch
import numpy as np
from PIL import Image
from arcpix.out import PACK
from dev.audit_overworld import inventory,review
from dev import audit_overworld
from parts.overworld_review import assessment


class OverworldArtTests(unittest.TestCase):
    def test_unreviewed_bridge_families_still_require_a_decision(self):
        for section, family in [('terrain', 'warden_unreviewed'), ('props', 'warden_force_99_99')]:
            with self.subTest(section=section, family=family), self.assertRaises(KeyError):
                assessment(section, family)

    def test_only_the_declared_invisible_collision_proxy_may_be_empty(self):
        cases = [('sprite', True, (1, 1), False, False),
                 ('collision proxy', True, (1, 1), False, True),
                 ('collision proxy', False, (1, 1), False, False),
                 ('collision proxy', True, (2, 2), False, False),
                 ('collision proxy', True, (1, 1), True, False)]
        for role, blocks, size, visible, accepted in cases:
            with self.subTest(role=role, blocks=blocks, size=size, visible=visible), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                (root/'props').mkdir()
                Image.new('RGBA', size, (255, 255, 255, 255 if visible else 0)).save(root/'props/gate.png')
                record = dict(id='props/warden_gate_collision/default', section='props',
                    family='warden_gate_collision', variant='default', path='props/gate.png',
                    size=list(size), frames=1, anchor=[0, 0], role=role, blocks=blocks,
                    where='Ford gate', assessment='Invisible collision proxy.')
                with patch.object(audit_overworld, 'PACK', root), \
                     patch.object(audit_overworld, 'inventory', return_value=[record]), \
                     patch.object(audit_overworld, 'region_usage', return_value=Counter()):
                    if accepted:
                        review(output=root/'review')
                        self.assertTrue((root/'review/assets.json').exists())
                    else:
                        with self.assertRaises(ValueError):
                            review(output=root/'review')

    def test_every_environment_image_has_a_review_and_valid_strip_metadata(self):
        with tempfile.TemporaryDirectory() as out:
            review(output=Path(out))
            entries=json.loads((Path(out)/'assets.json').read_text())
            self.assertEqual(len(entries),len({e['path'] for e in entries}))

    def test_cottage_roof_variants_share_the_manifest_projection_frame(self):
        from parts.town import render_frames
        m=json.loads((PACK/'town/manifest.json').read_text())['buildings']['cottage']
        for variant in m['variants']:
            for night in (False,True):
                for r in render_frames('cottage',variant,night):
                    self.assertEqual(tuple(m['size']),r.image.size,(variant,night))
                    self.assertEqual(tuple(m['size']),r.shadow.size,(variant,night))
                    self.assertEqual(tuple(m['origin']),r.origin,(variant,night))
                    self.assertEqual(tuple(m['anchor']),r.anchor,(variant,night))
                    self.assertEqual(m['attach'],{k:[list(p) for p in ps] for k,ps in r.attach.items()},(variant,night))

    def test_old_ash_is_non_emissive_and_grass_has_no_white_sparkle(self):
        from parts import terrain as T
        ash=T.ash();self.assertFalse((ash.slot==T.SLOTS.id('glow')).any())
        m=json.loads((PACK/'terrain/manifest.json').read_text())['textures']['ash']
        self.assertNotIn('emissive',m)
        grass=T.grass();self.assertFalse((grass.shade==5).any())

    def test_ford_steam_stays_translucent_and_has_no_loop_pop(self):
        from parts.ambient import river_steam
        frames=[np.array(im) for im in river_steam()]
        self.assertTrue(all(0<f[:,:,3].max()<=60 for f in frames))
        self.assertTrue(all(not f[0,:,3].any() and not f[-1,:,3].any() for f in frames))
        differences=[np.abs(a[:,:,3].astype(float)-b[:,:,3]).sum()
            for a,b in zip(frames,frames[1:]+frames[:1])]
        self.assertLessEqual(differences[-1],max(differences[:-1])*1.2)

    def test_literal_map_landmarks_resolve_to_existing_lore_and_icons(self):
        m=json.loads((PACK/'worldmap/manifest.json').read_text())
        regions=json.loads((PACK/'lore/regions.json').read_text())['regions']
        places={p['id']:p for r in regions for p in r['places']}
        for key,icon in m['placeIcons'].items():
            self.assertIn(key,places);self.assertIn(icon,m['icon']['files'])
        self.assertEqual('stone_bridge',m['placeIcons']['ford_bridge'])
        self.assertEqual('rope_bridge',m['placeIcons']['canyon_bridge'])
        self.assertEqual('hut',m['placeIcons']['maud_hut'])
        self.assertEqual('heartpool',m['placeIcons']['the_heartpool'])
        self.assertEqual('ice_cave',m['placeIcons']['cold_ember_caves'])


if __name__=='__main__':unittest.main()
