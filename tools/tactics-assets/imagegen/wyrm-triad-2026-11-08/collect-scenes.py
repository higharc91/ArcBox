"""Copy unmodified Godot shots; use engine-timed contacts for the twelve actions."""
from pathlib import Path
import argparse, shutil
p=argparse.ArgumentParser()
for name in ['battle','world','specials']: p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args();out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/wyrm-triad';out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
    files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png')
take(a.world,'world-lineup.png','overworld-lineup.png')
take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for seq,color in enumerate(['black','blue','green'],4):
    take(a.specials,'*-'+color+'-attack-windup.png',color+'-attack-windup.png')
    take(a.specials,f'*-tactics-beat-hit-{seq}.png',color+'-attack-contact.png')
for seq,name in enumerate([c+'-'+m for c in ['black','blue','green'] for m in ['breath','roar','wingbeat']],7):
    take(a.specials,'*-'+name+'-windup.png',name+'-windup.png')
    take(a.specials,f'*-tactics-beat-special-contact-{seq}.png',name+'-impact.png')
print('Collected three natural jaw hits and all nine engine-timed special contacts.')
