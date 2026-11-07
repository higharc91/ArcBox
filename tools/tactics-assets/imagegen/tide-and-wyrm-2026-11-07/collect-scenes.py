"""Collect unmodified captures and event-timed natural contacts."""
from pathlib import Path
import argparse,shutil
p=argparse.ArgumentParser();p.add_argument('--battle',type=Path,required=True);p.add_argument('--world',type=Path,required=True);p.add_argument('--specials',type=Path,required=True);a=p.parse_args();out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/tide-and-wyrm';out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
 files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png');take(a.world,'world-lineup.png','overworld-lineup.png');take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for seq,name in enumerate(['water-tidal-crush','water-undertow','dragon-breath','dragon-roar','dragon-wingbeat'],5):
 take(a.specials,'*-'+name+'-windup.png',name+'-windup.png')
 take(a.specials,f'*-tactics-beat-special-contact-{seq}.png',name+'-impact.png')
for i,name in [(3,'water'),(4,'dragon')]:take(a.specials,f'*-tactics-beat-hit-{i}.png',name+'-attack-contact.png')
print('Collected five specials, two event-timed natural contacts and world views.')
