"""Collect real event-timed natural hits, stone releases and ability contacts."""
from pathlib import Path
import argparse, shutil
p=argparse.ArgumentParser()
for name in ['battle','world','specials']:p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args();out=Path(__file__).resolve().parents[4]/'docs/art/mob-sprite-review/gilded-and-rime';out.mkdir(parents=True,exist_ok=True)
def take(folder,pattern,name):
    files=list(folder.glob(pattern));assert len(files)==1,(pattern,files);shutil.copyfile(files[0],out/name)
take(a.battle,'*-tactics-battle-river-crossing.png','battle.png')
take(a.world,'world-lineup.png','overworld-lineup.png')
take(a.world,'world-road.png','overworld-lighting-occlusion.png')
for seq,name in enumerate(['gold','frost','ogre'],4):
    take(a.specials,'*-'+name+'-attack-windup.png',name+'-attack-windup.png')
    take(a.specials,f'*-tactics-beat-hit-{seq}.png',name+'-attack-contact.png')
for seq,name in [(7,'gold-breath'),(8,'gold-roar'),(9,'gold-wingbeat'),(11,'frost-roar'),(13,'ogre-roar')]:
    take(a.specials,'*-'+name+'-windup.png',name+'-windup.png')
    take(a.specials,f'*-tactics-beat-special-contact-{seq}.png',name+'-impact.png')
for seq,name in [(10,'frost'),(12,'ogre')]:
    take(a.specials,'*-'+name+'-boulder-windup.png',name+'-boulder-windup.png')
    take(a.specials,f'*-tactics-beat-boulder-release-{seq}.png',name+'-boulder-release.png')
print('Collected ten engine-timed strikes, stone releases and special contacts.')
