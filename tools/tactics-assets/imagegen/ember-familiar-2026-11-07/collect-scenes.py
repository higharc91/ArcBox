"""Copy unmodified Godot review captures."""
from pathlib import Path
import argparse, shutil
p=argparse.ArgumentParser()
for name in ['battle','world','moves']: p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args(); out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/ember-familiar'
out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
 files=list(folder.glob(pattern)); assert len(files)==1,(pattern,files)
 shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png')
take(a.world,'world-lineup.png','overworld-lineup.png')
take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for name in ['imp-idle','imp-walk','imp-bolt-windup','imp-bolt-flight','imp-recovery','imp-ready']:
 take(a.moves,'*-'+name+'.png',name+'.png')
take(a.moves,'*-imp-bolt-flight.png','bolt-in-game.png')
