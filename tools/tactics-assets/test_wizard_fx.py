"""The shipped Wizard art must match a second deterministic native export."""
import io,json,unittest
from pathlib import Path
from PIL import Image
from arcpix.out import PACK
from parts import fx
from parts.wizard_fx import DESIGNS
from parts.wizard_casts import CASTS

class WizardFxTests(unittest.TestCase):
    def test_all_sheets_are_byte_identical_to_their_native_generator(self):
        manifest=json.loads((PACK/'fx/manifest.json').read_text())
        entries=[e for e in fx.CATALOGUE if e.id.startswith('wizard_')]
        self.assertEqual(55,len(entries))
        self.assertEqual(48,len(DESIGNS))
        for effect in entries:
            frames=effect.make();w,h=frames[0].w,frames[0].h
            entry=manifest['effects'][effect.id]
            self.assertEqual([w,h],entry['frame'])
            self.assertEqual(len(frames),entry['frames'])
            self.assertEqual(effect.fps,entry['fps'])
            for variant,palette in effect.variants.items():
                sheet=Image.new('RGBA',(w*len(frames),h))
                for i,canvas in enumerate(frames):sheet.paste(canvas.render(fx.ramps(palette)),(i*w,0))
                out=io.BytesIO();sheet.save(out,format='PNG',optimize=True)
                self.assertEqual((PACK/'fx'/entry['variants'][variant]).read_bytes(),out.getvalue(),effect.id)
            self.assertTrue(any(c.slot.any() for c in frames),effect.id)

    def test_all_abilities_use_their_own_effect_and_preserve_original_cinematic_tiers(self):
        root=Path(__file__).parent
        original=json.loads((root/'baseline/wizard-vfx.json').read_text())
        abilities=json.loads((PACK/'rules/abilities/wizard.json').read_text())['abilities']
        self.assertEqual(set(original),{a['id'] for a in abilities})
        effects=set()
        for ability in abilities:
            p=ability['presentation'];old=original[ability['id']];cost=ability['cost']
            tier=old.get('cutIn') or ('none' if cost.get('reaction') else 'full' if any(str(v).startswith('signature.') for v in old.values()) else 'short' if ability['tier']>0 or cost.get('uses') or any(cost.get('resource',{}).values()) else 'none')
            actual=p.get('cutIn') or ('none' if cost.get('reaction') else 'short' if ability['tier']>0 or cost.get('uses') or any(cost.get('resource',{}).values()) else 'none')
            self.assertEqual(tier,actual,ability['id'])
            self.assertIn(p['anim'].split('.')[-1],CASTS)
            self.assertNotIn(p['fx'],effects);effects.add(p['fx'])
        self.assertEqual(47,len(effects))
