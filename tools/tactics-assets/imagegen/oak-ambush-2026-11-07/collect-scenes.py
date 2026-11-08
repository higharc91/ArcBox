"""Copy unmodified Godot review captures."""
from pathlib import Path
import argparse, shutil
p=argparse.ArgumentParser()
for name in ['battle','world','moves']: p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args(); out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/oak-ambush'
out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
 files=list(folder.glob(pattern)); assert len(files)==1,(pattern,files)
 shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png')
take(a.world,'world-lineup.png','overworld-lineup.png')
take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for name in ['mimic-disguise','mimic-skitter','mimic-bite-windup','mimic-bite-contact','mimic-adhesive-contact','mimic-restored']:
 take(a.moves,'*-'+name+'.png',name+'.png')
take(a.moves,'*-mimic-adhesive-contact.png','bite-in-game.png')
