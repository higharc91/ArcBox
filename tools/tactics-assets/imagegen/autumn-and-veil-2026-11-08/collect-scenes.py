"""Collect real event-timed natural hits, stone releases and ability contacts."""
from pathlib import Path
import argparse, shutil
p=argparse.ArgumentParser()
for name in ['battle','world','specials']:p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args();out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/autumn-and-veil';out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
    files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png')
take(a.world,'world-lineup.png','overworld-lineup.png')
take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for seq,name in enumerate(['autumn','blossom','shade'],4):
    take(a.specials,'*-'+name+'-attack-windup.png',name+'-attack-windup.png')
    take(a.specials,f'*-tactics-beat-hit-{seq}.png',name+'-attack-contact.png')
take(a.specials,'*-shade-drain-windup.png','shade-drain-windup.png')
take(a.specials,'*-tactics-beat-special-contact-7.png','shade-drain-impact.png')
print('Collected four engine-timed vine, touch and Life Drain contacts.')
