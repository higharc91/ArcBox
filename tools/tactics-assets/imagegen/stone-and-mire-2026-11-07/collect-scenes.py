"""Collect unmodified Godot captures; use the beat-clock release moment, not a wall-clock shot."""
from pathlib import Path
import argparse, shutil
p=argparse.ArgumentParser()
p.add_argument('--battle',type=Path,required=True)
p.add_argument('--world',type=Path,required=True)
p.add_argument('--specials',type=Path,required=True)
a=p.parse_args()
out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/stone-and-mire'
out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
    files=list(folder.glob(pattern));assert len(files)==1,(pattern,files)
    shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png')
take(a.world,'world-lineup.png','overworld-lineup.png')
take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for name in ['troll-boulder-held','troll-frenzy-roar','golem-slam-windup','golem-slam-impact','golem-stone-aegis']:
    take(a.specials,'*-'+name+'.png',name+'.png')
# Software rendering can hitch while loading the first projectile: the beat's own
# attack moment follows its slowed animation clock and reliably shows empty hands.
take(a.specials,'*-tactics-beat-attack-1.png','troll-boulder-release.png')
print('Collected corrected HUD views and the event-timed boulder release.')
