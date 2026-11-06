"""Run the existing TV renderer in an isolated software OpenGL display."""
import argparse, json, os, select, subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--pack', required=True)
parser.add_argument('--region', default='ashen_ford')
parser.add_argument('--mode', choices=['views', 'perf'], default='views')
parser.add_argument('--name', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[5]
report = Path(__file__).resolve().parent / 'native' / args.name
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
    cmd = ['/workspace/.arcbox/bin/godot', '--path', str(root / 'src/ArcBox.Host'), '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy', '--', '--world-demo=' + args.region, '--preset=low', '--res=1280x720']
    if args.mode == 'views':
        cmd += ['--world-looks=inn@21,62@22,63;market@28,64@30,65;children@22,56@23,57;fields@37,84@38,85;docks@103,93@102,94;guards@31,78@31,79', '--shots=' + str(report / 'shots'), '--perf', '--quit-after=120']
    else:
        cmd += ['--perf', '--quit-after=150']
    with (report / 'tv.txt').open('w') as log:
        result = subprocess.run(cmd, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=190)
    if result.returncode:
        raise RuntimeError(f'TV exit {result.returncode}; inspect {report}')
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
