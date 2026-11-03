"""Overworld art contracts: shipped frames, family anchors and lore-specific art."""
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from PIL import Image
from arcpix.out import PACK
from audit_overworld import inventory,review


class OverworldArtTests(unittest.TestCase):
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
