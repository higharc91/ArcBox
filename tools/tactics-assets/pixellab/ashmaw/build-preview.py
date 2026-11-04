"""Build a separate TV review pack; never installs incomplete directional states into the game pack."""
import json, math, os, shutil, sys
from pathlib import Path
from PIL import Image
ROOT = Path(__file__).resolve().parents[4]
ART = Path(__file__).resolve().parent
SOURCE = ROOT / 'content/tactics/base'
DIRECTIONS = ['south','south-west','west','north-west','north','north-east','east','south-east']

def build(destination):
    destination.mkdir(parents=True, exist_ok=True)
    approval = json.loads((ROOT/'tools/tactics-assets/concepts/ashmaw/approvals.json').read_text())
    motion_approved = approval.get('inGameMotionReview', {}).get('status') == 'approved'
    for child in SOURCE.iterdir():
        target = destination / child.name
        if child.name in ('creatures','portraits'):
            if target.is_symlink(): target.unlink()
            target.mkdir(exist_ok=True)
            for asset in child.iterdir():
                if asset.name != 'manifest.json' and not (target/asset.name).exists(): (target/asset.name).symlink_to(asset)
        elif child.name == 'lore':
            if target.is_symlink(): target.unlink()
            target.mkdir(exist_ok=True)
            for asset in child.iterdir():
                if asset.name != 'characters.json' and not (target/asset.name).exists(): (target/asset.name).symlink_to(asset, target_is_directory=asset.is_dir())
            characters=json.loads((child/'characters.json').read_text())
            for character in characters['characters']:
                if character['id']=='grisk':character.update(sprite='monsters.kobold_foreman',portrait='grisk')
            (target/'characters.json').write_text(json.dumps(characters,indent=2)+'\n')
        elif not target.exists(): target.symlink_to(child, target_is_directory=child.is_dir())
    portraits=json.loads((SOURCE/'portraits/manifest.json').read_text())
    for name,keys in [('ashmaw',['ashmaw']),('grisk',['grisk','kobold_foreman'])]:
        image=Image.open(ART/'directional'/(name+'-portrait-source.png')).convert('RGBA')
        image.resize((portraits['size'],portraits['size']),Image.Resampling.NEAREST).save(destination/'portraits'/(name+'_review.png'))
        for key in keys:portraits['presets'][key]={'idle':name+'_review.png'}
    (destination/'portraits/manifest.json').write_text(json.dumps(portraits,indent=2)+'\n')
    manifest = json.loads((SOURCE/'creatures/manifest.json').read_text())
    curated=json.loads((ART/'curation.json').read_text())
    audit = {'reviewOnly': True, 'ownerApprovedMotion': motion_approved, 'coverage': 'Every required action has eight real facings; missing or miscounted clips fail the build.', 'characters': {}}
    for name,folder,size,anchor,scale,groups in [
        ('ashmaw','ashmaw-padded',192,(96,171),.65,{'idle':('ashmaw_idle_pinned_clean',4,1,True),'walk':('ashmaw_walk_template_pilot',6,5.2,True),'attack':('ashmaw_attack_custom_pilot',8,8,False),'hurt':('ashmaw_hurt_pinned_clean',4,8,False),'down':('ashmaw_death_custom_pilot',8,6,False),'roar':('ashmaw_roar_pilot',6,5,False),'shake':('ashmaw_shake_pilot',6,6,False)}),
        ('kobold_foreman','grisk-template',128,(64,110),.34,{'idle':('grisk_idle_template_pilot',4,3,True),'walk':('grisk_walk_template_pilot',6,7.5,True),'attack':('grisk_attack_pilot',6,8,False),'hurt':('grisk_hurt_pilot',6,9,False),'down':('grisk_death_pilot',7,7,False),'shout':('grisk_shout_pilot',4,6,False)})]:
        source = ART/'directional'/folder
        total = sum(n for _,n,_,_ in groups.values()); rows = math.ceil(total/8)
        atlas = Image.new('RGBA',(size*8,size*rows*8))
        anims={}; actual={}; bounds=[]
        offset=0
        for action,(group,n,fps,loop) in groups.items():
            anims[action]={'frames':list(range(offset,offset+n)),'fps':fps,'loop':loop};actual[action]=[]
            for directionIndex,direction in enumerate(DIRECTIONS):
                rotation=Image.open(source/'rotations'/(direction+'.png')).convert('RGBA')
                floor=rotation.getbbox()[3]-1
                placement=((size-rotation.width)//2,anchor[1]-floor)
                selection=curated.get(name,{}).get(action,{}).get(direction)
                chosen=selection['group'] if selection else group
                frames=sorted((source/'animations'/str(chosen)/direction).glob('*.png')) if chosen else []
                if selection: frames=[frames[index] for index in selection['frames']]
                approved_pilot = name=='ashmaw' and action=='idle' and direction=='south'
                if not approved_pilot and len(frames) != n:
                    raise ValueError(f'Expected {n} real frames: {name}/{action}/{direction}, found {len(frames)}')
                if frames: actual[action].append(direction)
                for frame in range(n):
                    im=Image.open(frames[min(frame,len(frames)-1)]).convert('RGBA') if frames else rotation
                    placed=Image.new('RGBA',(size,size))
                    if approved_pilot:
                        pilot=Image.open(ART/'pilots/ashmaw-idle/normalized-atlas.png').convert('RGBA')
                        placed.alpha_composite(pilot.crop((frame*160,0,frame*160+160,160)),(anchor[0]-80,anchor[1]-143));actual[action]=['south']
                    else:
                        # PixelLab pads a custom clip around the original centred reference. Remove that
                        # fixed padding, never re-centre a frame by its moving fur/weapon bounds.
                        xoff=placement[0]-(im.width-rotation.width)//2
                        yoff=placement[1]-(im.height-rotation.height)//2
                        box=im.getbbox()
                        if box and not (0<=box[0]+xoff<box[2]+xoff<=size and 0<=box[1]+yoff<box[3]+yoff<=size):
                            raise ValueError(f'Clipped source frame: {name}/{action}/{direction}/{frame}, {box}, offset {(xoff,yoff)}')
                        placed.alpha_composite(im,(xoff,yoff))
                    x=((offset+frame)%8)*size;y=(directionIndex*rows+(offset+frame)//8)*size
                    atlas.paste(placed,(x,y))
                    if placed.getbbox():bounds.append(placed.getbbox())
            offset+=n
        head=min(b[1] for b in bounds)
        atlas.save(destination/'creatures'/('review_'+name+'.png'))
        manifest['sprites']['review_'+name]={'file':'review_'+name+'.png','family':name,'variant':'review','frame':{'width':size,'height':size,'anchor':list(anchor)},'portrait':[0,0,size,size],'headTop':head,'pixelScale':scale,'columns':8,'rowsPerDirection':rows,'directions':DIRECTIONS,'facingOffsets':{d:[0,0] for d in DIRECTIONS},'footprintWidth':70 if name=='ashmaw' else 14,'footprintHeight':36 if name=='ashmaw' else 12,'keys':['monsters.'+name],'anims':anims,'provenance':'REVIEW PACK: curated eight-direction source; actual TV motion '+('owner-approved.' if motion_approved else 'approval pending.')+' See review-audit.json.'}
        audit['characters'][name]={'canvas':size,'anchor':list(anchor),'pixelScale':scale,'actualAnimatedFacings':actual,'curation':curated.get(name,{})}
    (destination/'creatures/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    (destination/'review-audit.json').write_text(json.dumps(audit,indent=2)+'\n')
    print('Built review pack:',destination)

if __name__=='__main__':build(Path(sys.argv[1]).resolve())
