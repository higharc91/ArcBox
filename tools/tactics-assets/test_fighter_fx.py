"""The shipped Fighter art must match a second deterministic native export."""
import io,json,unittest,hashlib
from pathlib import Path
from PIL import Image
from arcpix.out import PACK
from parts import fx
from parts.fighter_fx import DESIGNS,frames
from parts.fighter_casts import CASTS
from parts.fx_packing import pack
import numpy as np

class FighterFxTests(unittest.TestCase):
    def test_physical_techniques_have_distinct_geometry_and_motion_before_colouring(self):
        silhouettes={hashlib.sha256(b''.join(c.slot.tobytes() for c in frames(name))).digest()
            for name in tuple(DESIGNS)}
        self.assertEqual(len(DESIGNS),len(silhouettes))

    def test_all_sheets_are_byte_identical_to_their_native_generator(self):
        manifest=json.loads((PACK/'fx/manifest.json').read_text())
        entries=[e for e in fx.CATALOGUE if e.id.startswith('fighter_')]
        self.assertEqual(28,len(entries))
        self.assertEqual(28,len(DESIGNS))
        for effect in entries:
            frames=effect.make();w,h=frames[0].w,frames[0].h
            entry=manifest['effects'][effect.id]
            self.assertEqual([w,h],entry['sourceFrame'])
            self.assertEqual(len(frames),entry['frames'])
            self.assertEqual(effect.fps,entry['fps'])
            for variant,palette in effect.variants.items():
                original=[c.render(fx.ramps(palette)) for c in frames]
                images,anchor,trim=pack(original,effect.anchor,effect.attach)
                self.assertEqual(list(anchor),entry['anchor'])
                self.assertEqual(list(trim),entry['trim'])
                cw,ch=images[0].size
                self.assertEqual([cw,ch],entry['frame'])
                for old,new in zip(original,images):
                    restored=Image.new('RGBA',(w,h));restored.paste(new,trim)
                    self.assertTrue(np.array_equal(np.array(old),np.array(restored)),effect.id)
                if effect.attach=='projectile':
                    self.assertEqual((w/2-trim[0],h/2-trim[1]),(cw/2,ch/2))
                if effect.anchor[0]*6<=w:self.assertEqual(w-effect.anchor[0],cw-anchor[0])
                sheet=Image.new('RGBA',(cw*len(frames),ch))
                for i,im in enumerate(images):sheet.paste(im,(i*cw,0))
                out=io.BytesIO();sheet.save(out,format='PNG',optimize=True)
                self.assertEqual((PACK/'fx'/entry['variants'][variant]).read_bytes(),out.getvalue(),effect.id)
            self.assertEqual("normal",effect.blend)
            self.assertTrue(any(c.slot.any() for c in frames),effect.id)
            # Techniques retain open space, with readable open space around the caster.
            for frame in frames:
                self.assertLess(np.count_nonzero(frame.slot)/(frame.w*frame.h),.25,effect.id)

    def test_all_abilities_use_their_own_effect_and_preserve_original_cinematic_tiers(self):
        root=Path(__file__).parent
        original=json.loads((root/'baseline/fighter-vfx.json').read_text())
        abilities=json.loads((PACK/'rules/abilities/fighter.json').read_text())['abilities']
        self.assertEqual(set(original),{a['id'] for a in abilities})
        effects=set()
        for ability in abilities:
            p=ability['presentation'];old=original[ability['id']];cost=ability['cost']
            tier=old.get('cutIn') or ('none' if cost.get('reaction') else 'full' if any(str(v).startswith('signature.') for v in old.values()) else 'short' if ability['tier']>0 or cost.get('uses') or any(cost.get('resource',{}).values()) else 'none')
            actual=p.get('cutIn') or ('none' if cost.get('reaction') else 'short' if ability['tier']>0 or cost.get('uses') or any(cost.get('resource',{}).values()) else 'none')
            self.assertEqual(tier,actual,ability['id'])
            self.assertIn(p['anim'].split('.')[-1],CASTS)
            self.assertNotIn(p['fx'],effects);effects.add(p['fx'])
        self.assertEqual(20,len(effects))
