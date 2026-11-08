"""Collect unmodified Godot captures; source shots remain available for timing review."""
from pathlib import Path
import argparse,shutil
p=argparse.ArgumentParser();p.add_argument('--battle',type=Path,required=True);p.add_argument('--world',type=Path,required=True);p.add_argument('--specials',type=Path,required=True);a=p.parse_args()
out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/night-companions';out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
 files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png');take(a.world,'world-lineup.png','overworld-lineup.png');take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for i,name in [(4,'black'),(5,'grey')]:
    take(a.specials,'*-'+name+'-attack-windup.png',name+'-attack-windup.png')
    take(a.specials,f'*-tactics-beat-hit-{i}.png',name+'-attack-contact.png')
take(a.specials,'*-violet-attack-windup.png','violet-attack-windup.png')
take(a.specials,'*-violet-attack-release.png','violet-attack-release.png')
take(a.specials,'*-tactics-beat-hit-6.png','violet-attack-hit.png')
print('Collected two beat-timed bites, hand-release/impact and world views.')
