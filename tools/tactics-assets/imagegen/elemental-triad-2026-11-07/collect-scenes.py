"""Collect unmodified Godot captures; source shots remain available for timing review."""
from pathlib import Path
import argparse,shutil
p=argparse.ArgumentParser();p.add_argument('--battle',type=Path,required=True);p.add_argument('--world',type=Path,required=True);p.add_argument('--specials',type=Path,required=True);a=p.parse_args()
out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/elemental-triad';out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
 files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png');take(a.world,'world-lineup.png','overworld-lineup.png');take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for name in ['air-whirlwind','air-gale-blast','earth-tremor','earth-aegis','fire-surge','fire-breath']:
 for phase in ['windup','impact']:take(a.specials,'*-'+name+'-'+phase+'.png',name+'-'+phase+'.png')
for i,name in [(4,'air'),(5,'earth'),(6,'fire')]:take(a.specials,f'*-tactics-beat-hit-{i}.png',name+'-attack-contact.png')
print('Collected game captures, six specials and three event-timed physical contacts.')
