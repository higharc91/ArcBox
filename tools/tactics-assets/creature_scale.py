"""One physical scale policy shared by the exporter and scale-only art tuning."""
import json
from pathlib import Path
SCALES = {
    'wolf': .40, 'boar': .32, 'bear': .40, 'spider': .32,
    'slime': .34, 'bat': .26, 'drake': .36, 'warg': .30,
    'hookbeak': .28, 'scorpion': .30, 'harpy': .32,
    'wisp': .50, 'gargoyle': .32, 'crab': .26,
}
VARIANTS = {('wolf', 'dire'): 1.25, ('bear', 'black'): .85, ('bear', 'polar'): 1.18}
def scale_for(family, variant): return SCALES[family] * VARIANTS.get((family, variant), 1)
def tune_manifest(path):
    """Update world measurements together; never resize or shift individual frames."""
    path = Path(path)
    data = json.loads(path.read_text())
    for c in data['sprites'].values():
        if c['family'] not in SCALES or c['keys'][0].startswith('fauna.'): continue
        scale = scale_for(c['family'], c['variant']); ratio = scale / c['pixelScale']
        for field in ['footprintWidth', 'footprintHeight']: c[field] *= ratio
        c['pixelScale'] = scale
    for key, c in data.get('legacyOverrides', {}).items():
        family = key.split('.')[1]
        # Keep the legacy special forms proportional to their updated primary.
        previous = c.get('familyScale', .5); target = SCALES[family]
        c['pixelScale'] *= target / previous
        c['footprintWidth'] *= target / previous
        c['familyScale'] = target
    data['scalePolicy'] = 'Reviewed beside the 36-world-pixel human fighter. Species scales are shared by every facing and action. Giant species remain larger; ordinary wildlife does not use giant proportions.'
    path.write_text(json.dumps(data, indent=2) + '\n')
if __name__ == '__main__':
    import sys
    tune_manifest(sys.argv[1])
