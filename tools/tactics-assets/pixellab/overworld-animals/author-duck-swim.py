"""Author restrained buoyancy loops: the swimming duck keeps wings folded and feet hidden.

Provider swim attempts opened wings and invented feet. Travel is driven by WorldFauna;
only a half-world-pixel vertical buoyancy cycle belongs in these in-place sprites.
"""
import json
from pathlib import Path
from PIL import Image
R = Path(__file__).resolve().parent
OFFSETS = {'idle': [0, 0, -1, 0, 0], 'move': [0, 0, -1, -1, 0, 0, 1, 0, 0]}
def main():
    layout = json.loads((R / 'canonical-layout.json').read_text())
    for direction in layout['directions']:
        pose = Image.open(R / 'canonical/duck/poses' / (direction + '.png')).convert('RGBA')
        for action, offsets in OFFSETS.items():
            out = R / 'authored-animations/duck/animations' / direction / action
            out.mkdir(parents=True, exist_ok=True)
            for frame, dy in enumerate(offsets):
                image = Image.new('RGBA', pose.size)
                image.alpha_composite(pose, (0, dy))
                image.save(out / ('frame-%03d.png' % frame))
    (R / 'duck/swim-authoring.json').write_text(json.dumps({
        'method': 'Fixed native pose with an authored one-native-pixel buoyancy cycle; actual travel is provided by WorldFauna.',
        'reason': 'Rejected provider motion unfolded wings and invented visible feet.',
        'offsets': OFFSETS, 'world_pixel_scale': .5,
        'no_per_frame_recentering': True,
    }, indent=2) + '\n')
if __name__ == '__main__': main()
