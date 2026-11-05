"""Capture the staged native art through the actual Godot world renderer."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--godot', default=os.environ.get('GODOT') or shutil.which('godot'), required=False)
    options = parser.parse_args()
    if not options.godot:
        raise ValueError('Provide --godot with the Godot .NET executable.')
    stage = options.stage.resolve()
    review = stage / 'review'
    review.mkdir(exist_ok=True)
    env = os.environ.copy()
    xvfb = None
    try:
        if os.name != 'nt' and not env.get('DISPLAY'):
            display = next(n for n in range(110, 150) if not Path(f'/tmp/.X{n}-lock').exists())
            env['DISPLAY'] = f':{display}'
            with (review / 'xvfb.log').open('w') as log:
                xvfb = subprocess.Popen(['Xvfb', env['DISPLAY'], '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                        env=env, stdout=log, stderr=subprocess.STDOUT)
            time.sleep(1)
        with (review / 'tv.log').open('w') as log:
            result = subprocess.run([options.godot, '--rendering-method', 'gl_compatibility', '--rendering-driver', 'opengl3',
                '--max-fps', '30', '--audio-driver', 'Dummy', '--windowed', '--resolution', '1280x720',
                '--path', str(stage / 'src/ArcBox.Host'), '--', '--world-demo=ashen_ford',
                '--world-looks=granary-after@41,71@38,74;granary-marker@42,70@42,70;maud-after@130,90@131,91',
                '--preset=low', '--shots=' + str(review / 'tv'), '--quit-after=120'], cwd=stage, env=env,
                stdout=log, stderr=subprocess.STDOUT, timeout=150)
        text = (review / 'tv.log').read_text()
        assert result.returncode == 0 and 'ERROR:' not in text, 'Inspect the TV log.'
        for name in ('granary-after', 'granary-marker', 'maud-after'):
            assert (review / 'tv' / ('world-' + name + '.png')).is_file(), name
        print('TV previews captured: granary, marker clearance, Maud; no TV errors.')
    finally:
        if xvfb:
            xvfb.terminate()
            xvfb.wait()


if __name__ == '__main__':
    main()
