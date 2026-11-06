# Compare old and current material renders at world scale.
"""Compare two baked asset packs at the same world density.

Usage: python3 tools/tactics-assets/dev/review_materials.py --before /path/to/snapshot
Snapshot must contain tactical/ and presets/ (or characters/presets/).
Software-composed world previews use the game assets, not the Godot light rig.
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import argparse
import json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
from arcpix import iso
from arcpix.out import PACK,PREVIEWS
from parts import dioramas as D
from parts.recipes import ENEMIES

FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
BOLD='/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
BG='#121b2b'


def font(size,bold=False):
    try:return ImageFont.truetype(BOLD if bold else FONT,size)
    except OSError:return ImageFont.load_default()


def hero(root,race,role,back=False,index=0):
    m=json.loads((root/'tactical/manifest.json').read_text())
    w,h=m['frame']['width'],m['frame']['height'];cols=m['columns']
    rows=(m['framesPerDirection']+cols-1)//cols
    x=index%cols*w;y=(index//cols+int(back)*rows)*h
    with Image.open(root/f'tactical/{race}_{role}.png') as sheet:
        return sheet.crop((x,y,x+w,y+h)).resize((w//2,h//2),Image.Resampling.NEAREST)


def enemy(root,key,back=False):
    folder=root/'characters/presets' if (root/'characters/presets').exists() else root/'presets'
    with Image.open(folder/f'{key}.png') as sheet:
        return sheet.crop((0,40*int(back),32,40*(1+int(back))))


def world(root):
    scene=iso.Scene(8,6)
    for j in range(6):
        for i in range(8):
            tile=D.tex('cobble','grey') if j in (2,3,4) and i in (2,3,4,5) else D.tex('grass','green')
            scene.cells[i,j]=iso.Cell(2,tile,D.tex('dirt_side','brown'))
    for name,i,j in [('crate',2,2),('barrel',3,1),('tree',6,2),('rock_medium',5,4),('bush',6,4)]:
        prop=D.prop(name,i,j);prop.scale=1;scene.sprites.append(prop)
    for race,role,i,j,back in [('human','fighter',2,3,False),
            ('dragonborn','wizard',3,3,False),('dwarf','cleric',3,4,True),
            ('tiefling','rogue',4,4,False)]:
        scene.sprites.append(iso.Sprite(hero(root,race,role,back),(40,61.5),i,j,scale=1))
    for key,i,j in [('goblin_cutthroat',4,2),('hobgoblin',5,3)]:
        scene.sprites.append(iso.Sprite(enemy(root,key),(16,37),i,j,scale=1))
    im=iso.render(scene,margin=48,backdrop=lambda w,h:Image.new('RGBA',(w,h),BG))
    return im.crop((100,80,460,320)).resize((720,480),Image.Resampling.NEAREST)


def review(before,output):
    output.mkdir(parents=True,exist_ok=True)
    picks=[('human','fighter'),('dragonborn','wizard'),('dwarf','cleric'),
        ('tiefling','rogue'),('elf','ranger'),('half_orc','barbarian')]
    board=Image.new('RGB',(1200,790),BG);d=ImageDraw.Draw(board)
    d.text((20,14),'MATERIAL REFINEMENT · BEFORE / AFTER',font=font(25,True),fill='#f4ecd6')
    d.text((20,52),'Gameplay pixel density · front enlarged 4×, rear enlarged 2× · identical poses',font=font(17),fill='#b9c6db')
    for n,(race,role) in enumerate(picks):
        x=n%3*400;y=100+n//3*340
        d.text((x+16,y),f'{race.replace("_"," ").title()} {role.title()}',font=font(19,True),fill='#e6bd5c')
        for col,root in enumerate((before,PACK)):
            xx=x+32+col*190
            d.text((xx,y+30),'Before' if col==0 else 'After',font=font(16),fill='#b9c6db')
            for back,scale,yy in ((False,4,y+55),(True,2,y+235)):
                # Crop the padded runtime sprite to its original authoring canvas.
                im=hero(root,race,role,back).crop((24,24,56,64))
                im=im.resize((32*scale,40*scale),Image.Resampling.NEAREST)
                board.paste(im,(xx,yy),im)
    board.save(output/'material-refinement.png')
    board=Image.new('RGB',(1468,600),BG);d=ImageDraw.Draw(board)
    d.text((18,14),'WORLD CONTEXT · BEFORE / AFTER',font=font(25,True),fill='#f4ecd6')
    d.text((18,51),'Same terrain, props, positions and scale · composed asset preview enlarged 2×',font=font(18),fill='#b9c6db')
    for col,root in enumerate((before,PACK)):
        x=10+col*734
        d.text((x+8,86),'BEFORE' if col==0 else 'AFTER',font=font(22,True),fill='#e6bd5c')
        board.paste(world(root),(x,118))
    board.save(output/'material-world-comparison.png')
    board=Image.new('RGB',(1360,100+((len(ENEMIES)+3)//4)*190),BG);d=ImageDraw.Draw(board)
    d.text((18,14),'ENEMY MATERIALS · BEFORE / AFTER',font=font(25,True),fill='#f4ecd6')
    d.text((18,51),f'All {len(ENEMIES)} humanoid enemies · existing 32×40 sprites enlarged 3× · identical poses',font=font(17),fill='#b9c6db')
    for n,recipe in enumerate(ENEMIES):
        x=n%4*340;y=100+n//4*190
        d.text((x+14,y),recipe.name,font=font(17,True),fill='#e6bd5c')
        for col,root in enumerate((before,PACK)):
            xx=x+20+col*160
            d.text((xx,y+27),'Before' if col==0 else 'After',font=font(14),fill='#b9c6db')
            im=enemy(root,recipe.id).resize((96,120),Image.Resampling.NEAREST)
            board.paste(im,(xx,y+48),im)
    board.save(output/'material-enemies.png')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--before',type=Path,required=True)
    p.add_argument('--output',type=Path,default=PREVIEWS)
    args=p.parse_args();review(args.before,args.output)
