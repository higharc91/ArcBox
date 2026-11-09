"""Record real Godot beats, preserving engine-timed screenshots for review."""
from pathlib import Path
import os,subprocess,time
R=Path(__file__).resolve().parents[5];OUT=R/'docs/art/mob-sprite-review/library-completion';LOGS=Path(os.environ.get('ARCBOX_REVIEW_LOGS','/workspace/mob-review'));LOGS.mkdir(exist_ok=True,parents=True)
godot=os.environ.get('GODOT','/workspace/toolchains/godot-opengl' if Path('/workspace/toolchains/godot-opengl').exists() else 'godot');display=os.environ.get('DISPLAY',':97');env={**os.environ,'DISPLAY':display}
for slug,prefix,seconds,filename in [('gilded-and-rime','library-completion',42,'native-gilded-and-rime.mp4'),('spectral-remnant','library-completion-spectre',24,'native-spectre.mp4')]:
 shots=LOGS/(prefix+'-moves');shots.mkdir(exist_ok=True)
 for file in shots.glob('*.png'):file.unlink()
 with (LOGS/(prefix+'-moves.log')).open('w') as log:
  p=subprocess.Popen([godot,'--audio-driver','Dummy','--path','src/ArcBox.Host','--resolution','1920x1080','--','--preset=low','--tactics-demo=library-'+slug+'-moves','--shots='+str(shots),'--quit-after='+str(seconds+2)],cwd=R,env=env,stdout=log,stderr=subprocess.STDOUT)
  try:
   time.sleep(2)
   with (LOGS/(prefix+'-recording.log')).open('w') as flog:
    subprocess.run(['ffmpeg','-v','warning','-y','-f','x11grab','-framerate','20','-video_size','1920x1080','-i',display,'-t',str(seconds),'-vf','scale=960:540:flags=neighbor','-an','-c:v','libx264','-threads','2','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/filename)],stdout=flog,stderr=subprocess.STDOUT,check=True)
   p.wait(timeout=20);assert p.returncode==0,p.returncode
  finally:
   if p.poll() is None:p.kill();p.wait()
 print('Recorded '+slug+' through the normal Godot beat path.',flush=True)
