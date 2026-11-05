"""Run the existing TV renderer in an isolated software OpenGL display."""
import argparse, json, os, select, signal, subprocess, time
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--pack', required=True)
parser.add_argument('--region', default='ashen_ford')
parser.add_argument('--mode', choices=['views', 'perf'], default='views')
parser.add_argument('--name', required=True)
parser.add_argument('--report', type=Path)
parser.add_argument('--flags', default='')
parser.add_argument('--walk')
parser.add_argument('--looks', default='bridge-west@106,61@110,64;bridge-east@109,62@110,64')
parser.add_argument('--seconds', type=int, default=40)
parser.add_argument('--party-walk', action='store_true')
parser.add_argument('--record', action='store_true')
parser.add_argument('--battle', help='Capture the standalone battle demo instead of the world')
args = parser.parse_args()
root = Path(__file__).resolve().parents[5]
report = args.report or (Path(args.pack).resolve().parents[3] / 'tv' / args.name)
report.mkdir(parents=True, exist_ok=True)
env = os.environ.copy()
env.update(DOTNET_ROOT='/workspace/.dotnet', DOTNET_CLI_HOME='/workspace/.arcbox/dotnet-home', NUGET_PACKAGES='/workspace/.arcbox/nuget', LIBGL_ALWAYS_SOFTWARE='1', ARCBOX_TACTICS_ART_ROOT=str(Path(args.pack).resolve()), LD_LIBRARY_PATH='/workspace/.arcbox/system/root/usr/lib/x86_64-linux-gnu:' + env.get('LD_LIBRARY_PATH', ''))
read_fd, write_fd = os.pipe()
display = subprocess.Popen(['/workspace/.arcbox/bin/Xvfb', '-displayfd', str(write_fd), '-screen', '0', '1280x720x24', '-nolisten', 'tcp'], env=env, pass_fds=(write_fd,), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
os.close(write_fd)
try:
    if not select.select([read_fd], [], [], 10)[0]:
        raise RuntimeError('Xvfb startup failed')
    env['DISPLAY'] = ':' + os.read(read_fd, 100).decode().strip()
    cmd = ['/workspace/.arcbox/bin/godot', '--path', str(root / 'src/ArcBox.Host'), '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--max-fps', '30', '--', '--world-demo=' + args.region, '--preset=low', '--res=1280x720']
    if args.flags: cmd += ['--world-story-flags=' + args.flags]
    if args.party_walk: cmd += ['--world-party-walk']
    if args.walk: cmd += ['--world-route=' + args.walk]
    if args.mode == 'views':
        cmd += ['--world-looks=' + args.looks, '--shots=' + str(report / 'shots'), '--quit-after=' + str(args.seconds)]
    else:
        cmd += ['--perf', '--quit-after=150']
    if args.battle:
        cmd = [c for c in cmd if not c.startswith(('--world-demo=', '--world-looks='))]
        cmd += ['--tactics-demo=' + args.battle]
    with (report / 'tv.txt').open('w') as log:
        started=time.monotonic();movie=None;battle_shot=False
        process=subprocess.Popen(cmd,env=env,stdout=log,stderr=subprocess.STDOUT)
        try:
            while process.poll() is None:
                if time.monotonic()-started>190:raise TimeoutError('TV exceeded 190 seconds')
                if args.battle and not battle_shot and time.monotonic()-started>15 and 'TACTICS screen' in (report/'tv.txt').read_text():
                    subprocess.run(['ffmpeg','-y','-loglevel','error','-f','x11grab','-video_size','1280x720','-i',env['DISPLAY'],
                        '-frames:v','1',str(report/'battle.png')],env=env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                    battle_shot=True
                if args.record and movie is None and 'textures ' in (report/'tv.txt').read_text():
                    movie=subprocess.Popen(['ffmpeg','-y','-loglevel','error','-f','x11grab','-video_size','1280x720','-framerate','30','-i',env['DISPLAY'],
                        '-c:v','libx264','-threads','2','-preset','ultrafast','-crf','24','-pix_fmt','yuv420p',str(report/'walk.mp4')],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                time.sleep(.1)
            result=process
        finally:
            if process.poll() is None:process.terminate();process.wait(timeout=10)
            if movie is not None:movie.send_signal(signal.SIGINT);movie.wait(timeout=10)
    log_text = (report / 'tv.txt').read_text()
    if result.returncode or 'ERROR:' in log_text:
        raise RuntimeError(f'TV exit {result.returncode}; inspect {report}')
    if args.battle:
        if not (report/'battle.png').is_file():raise RuntimeError('Missing battle capture')
    elif args.mode == 'views':
        for shot in ('world-bridge-west.png', 'world-bridge-east.png'):
            if not (report / 'shots' / shot).is_file():
                raise RuntimeError('Missing TV view: ' + shot)
    if args.mode == 'perf':
        check = subprocess.run(['node', str(root / 'tools/tactics-playtest/world-memory.cjs'), str(report / 'tv.txt')], text=True, capture_output=True)
        if check.returncode:
            raise RuntimeError(check.stderr)
        (report / 'memory.json').write_text(check.stdout)
    print(json.dumps({'name': args.name, 'mode': args.mode, 'exit': result.returncode, 'report': str(report)}), flush=True)
finally:
    os.close(read_fd)
    display.terminate()
    display.wait(timeout=5)
