"""Original Meridian monster silhouettes and scenario objects for #336."""
from __future__ import annotations
import argparse,hashlib,html,json,math
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont,ImageOps
import build as style
import crew,compartments

ROOT=style.ROOT
REFERENCE=Path(__file__).parent/'reference/tokens'
HEAD=json.loads((REFERENCE/'provenance.json').read_text())['head']
SIDES=['s','e','n','w']
CLIPS={'idle':(3,3,True),'walk':(6,8,True),'attack':(6,10,False),'stun':(4,4,True),'hurt':(2,6,False)}
MONSTERS={'brood','drone','sleeper'}
NAMES={'brood':'Parasite brood','drone':'Security drone','sleeper':'Cryo-sleeper','command_core':'Command core','relay':'Power relay','repair':'Repair point','sabotage':'Sabotage','charge':'Charge','core':"Tiller’s core",'growth':'Rot growth','rot_source':'Rot source','pods':'Escape pods','engine':'Engine console','suit':'Pressure suit','prize':'Anomaly prize','sample':'Clean sample','dose':'Dose','item':'Dropped gear'}
STATES={'relay':['powered','rerouted'],'repair':['broken','fixed']}
BASE={'relay':'powered','repair':'broken'}
PRIVATE={'sabotage'}
P=dict(style.PALETTE)
from arcpix.palette import ramp
P.update(chitin=ramp('#967b9d',shift=.055,spread=.85),frost=ramp('#a7c9ce',shift=.055,spread=.85),rot=ramp('#81976b',shift=.055,spread=.85))

def requirements(reference=REFERENCE):
    kinds={'item':{'name':NAMES['item'],'monster':False,'states':[],'scenarios':[]}}
    for path in sorted((reference/'scenarios').glob('*.json')):
        d=json.loads(path.read_text())
        for key,spec in d.get('tokens',{}).items():
            e=kinds.setdefault(key,{'name':NAMES.get(key,spec.get('name',key)),'monster':bool(spec.get('monster',False)),'states':[],'scenarios':[]})
            e['scenarios'].append(d['id']);e['states']=sorted(set(e['states'])|set(spec.get('states',{})))
    for key,state in BASE.items():
        if key in kinds:kinds[key]['states']=sorted(set(kinds[key]['states'])|{state})
    return kinds


def token(key,state=None):
    if key not in set(NAMES)-MONSTERS|{'hidden'}:raise ValueError('Unknown authored token '+key)
    if key in STATES and state not in STATES[key]:raise ValueError('Unknown state '+str(state))
    c=style.canvas(24,24)
    # A broad outer silhouette and a small number of large material planes; no painted labels.
    if key=='command_core':
        c.polygon([(12,1),(16,5),(21,5),(21,10),(23,12),(19,15),(19,20),(14,20),(12,23),(8,19),(3,19),(3,14),(1,12),(5,8),(5,3),(10,3)],'hull',0)
        c.ellipse(12,12,8,8,'engineering',3);c.ellipse(12,12,6,6,'engineering',5);c.ellipse(12,12,4,4,'hull',1)
        c.polygon([(12,7),(16,12),(12,17),(8,12)],'command',4);c.line(10,10,13,10,'linen',5)
    elif key=='core':
        c.rect(5,3,18,21,'hull',0);c.polygon([(8,1),(16,1),(19,5),(4,5)],'hull',3)
        c.rect(7,5,16,19,'hull',3);c.rect(9,5,14,18,'command',2)
        for y in [6,10,14]:c.rect(4,y,7,y+2,'linen',3);c.rect(16,y,19,y+2,'hull',4);c.rect(10,y,13,y+1,'coolant',5)
        c.rect(7,20,17,22,'hull',4)
    elif key=='relay':
        c.rect(6,4,17,18,'hull',0);c.rect(7,5,16,17,'engineering',3);c.rect(8,6,15,13,'hull',1)
        c.rect(9,7,14,10,'coolant',4);c.rect(9,11,14,12,'linen',4)
        c.rect(9,1,14,4,'hull',4);c.rect(2,18,6,22,'hull',0);c.rect(17,18,21,22,'hull',0)
        if state=='powered':
            c.thick_line(9,17,4,19,3,'warning',3);c.thick_line(14,17,19,19,3,'warning',3)
            c.rect(3,19,5,21,'linen',4);c.rect(18,19,20,21,'linen',4)
        else:
            c.thick_line(9,17,10,20,3,'coolant',4);c.thick_line(10,20,20,15,3,'coolant',4)
            c.rect(19,12,22,16,'hull',0);c.rect(20,13,21,15,'linen',4)
            c.rect(3,19,5,21,'hull',2);c.rect(18,19,20,21,'hull',2)
    elif key=='repair':
        c.rect(4,5,19,20,'hull',0);c.rect(5,6,18,19,'hull',3)
        if state=='broken':
            c.polygon([(2,3),(8,4),(7,16),(2,15)],'engineering',3);c.polygon([(16,3),(22,5),(22,16),(17,17)],'hull',4)
            c.thick_line(9,8,11,11,2,'warning',4);c.thick_line(15,10,13,15,2,'coolant',4)
            c.rect(10,12,12,14,'hull',0)
        else:
            c.rect(6,5,17,18,'linen',3);c.rect(8,8,15,14,'hull',1)
            c.thick_line(9,12,12,9,2,'coolant',4);c.thick_line(12,9,14,12,2,'coolant',4)
            c.rect(7,19,17,21,'hull',4)
        for x in [5,18]:c.rect(x,7,x,9,'linen',5)
    elif key=='sabotage':
        c.thick_line(3,20,10,14,3,'hull',0);c.thick_line(13,9,19,2,3,'hull',0)
        c.polygon([(5,11),(14,4),(20,11),(12,20)],'red',2);c.polygon([(8,11),(14,7),(17,11),(12,17)],'red',4)
        c.rect(10,10,15,13,'hull',0);c.rect(11,10,14,11,'linen',4)
        c.line(2,18,5,15,'warning',4);c.line(17,7,21,3,'coolant',4)
    elif key=='charge':
        c.rect(3,7,20,20,'hull',0);c.rect(4,8,19,19,'engineering',3)
        for x in [6,13]:c.rect(x,9,x+4,18,'red',3);c.line(x+1,9,x+3,9,'warning',5)
        c.rect(10,7,12,20,'hull',3);c.rect(7,3,16,7,'hull',0);c.rect(8,4,15,6,'linen',3)
        c.line(6,8,4,4,'warning',4);c.line(4,4,8,2,'warning',4)
        c.rect(4,19,7,21,'hull',4);c.rect(16,19,19,21,'hull',4)
    elif key in ['growth','rot_source']:
        for x,y,r in [(5,15,3),(18,15,4),(8,8,4),(15,7,3),(11,16,5)]:
            c.ellipse(x,y,r,r-1,'hull',0);c.ellipse(x,y,r-1,r-2,'rot',3)
        c.thick_line(3,20,18,10,2,'rot',4);c.thick_line(4,5,16,18,2,'rot',2)
        if key=='rot_source':
            c.polygon([(12,1),(19,8),(17,18),(12,22),(6,17),(5,8)],'hull',0)
            c.polygon([(12,3),(17,9),(15,16),(11,19),(8,14),(8,8)],'rot',4)
            c.ellipse(12,10,3,5,'warning',3);c.ellipse(12,9,1,3,'linen',4)
        else:
            c.ellipse(11,14,3,3,'rot',4);c.ellipse(8,7,2,2,'warning',3);c.ellipse(17,12,2,2,'rot',5)
    elif key=='pods':
        for x in [2,13]:
            c.polygon([(x+4,1),(x+8,5),(x+8,18),(x+5,22),(x,19),(x,5)],'hull',0)
            c.rect(x+1,6,x+7,17,'linen',3);c.ellipse(x+4,6,3,4,'linen',4)
            c.rect(x+2,5,x+6,10,'command',2);c.line(x+2,5,x+5,5,'coolant',5)
            c.rect(x+2,15,x+6,18,'engineering',3)
    elif key=='engine':
        c.rect(1,8,5,18,'hull',0);c.rect(19,7,22,18,'hull',0)
        c.ellipse(12,12,9,10,'hull',0);c.ellipse(12,12,8,9,'engineering',3);c.ellipse(12,12,6,7,'hull',1)
        for points in [[(12,5),(15,7),(12,11),(10,10)],[(18,11),(17,15),(13,13),(13,10)],[(12,19),(8,17),(11,13),(13,14)],[(6,13),(7,9),(11,11),(11,14)]]:c.polygon(points,'linen',3)
        c.ellipse(12,12,2,2,'red',3);c.line(2,9,2,16,'hull',4);c.line(21,9,21,16,'hull',4)
    elif key=='suit':
        c.ellipse(12,6,6,5,'hull',0);c.ellipse(12,6,5,4,'linen',4);c.rect(8,4,15,8,'command',2);c.line(9,4,13,4,'coolant',5)
        c.polygon([(7,10),(17,10),(22,15),(20,18),(17,16),(17,22),(7,22),(7,16),(3,18),(1,15)],'hull',0)
        c.rect(8,11,16,19,'linen',3);c.thick_line(5,13,3,16,3,'linen',3);c.thick_line(18,13,20,16,3,'linen',3)
        c.rect(10,12,14,15,'engineering',3);c.line(12,17,12,21,'hull',1)
    elif key=='prize':
        c.polygon([(12,1),(21,10),(17,18),(12,22),(5,18),(2,10)],'hull',0)
        c.polygon([(12,3),(18,10),(12,19),(6,10)],'habitat',3)
        c.polygon([(12,3),(13,10),(6,10)],'linen',4);c.polygon([(13,10),(18,10),(12,19)],'habitat',1)
        c.line(4,19,4,12,'engineering',3);c.line(19,19,19,12,'engineering',3);c.rect(4,19,19,21,'hull',4)
    elif key=='sample':
        c.rect(7,1,16,5,'hull',0);c.rect(8,2,15,4,'linen',4)
        c.polygon([(7,6),(16,6),(18,20),(15,22),(8,22),(5,20)],'hull',0)
        c.rect(8,7,15,19,'frost',3);c.rect(9,12,14,19,'coolant',3);c.line(9,7,9,17,'linen',5)
        c.rect(6,19,17,21,'linen',3)
    elif key=='dose':
        c.thick_line(2,20,7,15,2,'hull',3);c.polygon([(5,13),(14,4),(20,10),(11,19)],'hull',0)
        c.polygon([(7,13),(14,6),(18,10),(11,17)],'linen',4);c.line(10,12,14,8,'coolant',3)
        c.thick_line(16,5,19,2,3,'engineering',3);c.thick_line(16,1,22,7,2,'hull',4)
    elif key=='item':
        c.rect(8,2,15,4,'hull',0);c.line(8,2,15,2,'linen',4)
        c.polygon([(5,5),(18,5),(22,10),(20,21),(3,21),(1,10)],'hull',0)
        c.rect(4,7,19,19,'linen',3);c.rect(9,5,14,20,'engineering',2)
        c.rect(8,11,15,15,'hull',0);c.rect(9,12,14,14,'hull',4)
        c.line(4,8,7,8,'linen',5);c.line(16,8,19,8,'linen',5)
    elif key=='hidden':
        c.polygon([(5,3),(18,3),(22,7),(22,18),(18,22),(5,22),(1,18),(1,7)],'hull',0)
        c.rect(3,6,20,19,'hull',3);c.ellipse(12,9,4,5,'linen',4);c.ellipse(12,9,2,3,'hull',1)
        c.rect(7,10,17,18,'engineering',3);c.rect(11,13,13,16,'hull',0)
    return c.render(P)


def monster(key,side='s',clip='idle',frame=0):
    if key not in MONSTERS or side not in SIDES or clip not in CLIPS or frame not in range(CLIPS[clip][0]):raise ValueError('Unknown monster recipe')
    if side=='w':return ImageOps.mirror(monster(key,'e',clip,frame))
    c=style.canvas(24,24);sideways=side=='e';back=side=='n';stun=clip=='stun'
    bob=([0,1,0][frame%3] if clip=='idle' else [0,0,1,1,-1,-1][frame] if clip=='walk' else 0)
    reach=([0,1,2,2,1,0][frame] if clip=='attack' else 0)
    hurt=frame if clip=='hurt' else 0
    if key=='brood':
        y=1 if stun else -bob
        # Six separated, jointed legs establish a low silhouette at 48px on the TV.
        for i,yy in enumerate([10,14,18]):
            step=([0,1,-1][(frame+i)%3] if clip=='walk' else (frame%2 if stun and i==2 else 0))
            c.thick_line(7,yy+y,3,yy-2+y+step,2,'hull',0);c.thick_line(3,yy-2+y+step,1,yy+1+y,2,'chitin',3)
            c.thick_line(16,yy+y,20,yy-2+y-step,2,'hull',0);c.thick_line(20,yy-2+y-step,22,yy+1+y,2,'chitin',3)
        c.ellipse(12,12+y,7,7,'hull',0);c.ellipse(12,11+y,6,6,'chitin',3)
        for yy,rx in [(7,3),(11,5),(15,4)]:
            c.ellipse(11,yy+y,rx,2,'chitin',4);c.line(9,yy-1+y,13,yy-1+y,'linen',3)
        if sideways:
            c.ellipse(18,13+y,3,4,'chitin',2);c.rect(19,11+y,21,13+y,'linen',4)
            c.thick_line(18,16+y,20+reach,18+y,2,'warning',3)
        elif back:
            c.ellipse(12,6+y,4,3,'chitin',2);c.line(8,4+y,6,2,'warning',3);c.line(15,4+y,18,2,'warning',3)
            if clip=='attack':
                c.thick_line(8,6,5-reach,6-reach,2,'warning',3);c.thick_line(15,6,18+reach,6-reach,2,'warning',3)
        else:
            c.ellipse(12,17+y,4,3,'chitin',2);c.rect(8,16+y,10,17+y,'linen',5);c.rect(14,16+y,16,17+y,'linen',5)
            if not stun:
                c.thick_line(9,19+y,7-reach,21,2,'warning',3);c.thick_line(15,19+y,17+reach,21,2,'warning',3)
        if stun:c.line(7,20,16,20,'hull',3);c.line(8,17,10,18,'hull',0);c.line(14,17,16,18,'hull',0)
        c.line(8,9+y,15,9+y,'chitin',1);c.line(7,13+y,16,13+y,'chitin',1)
        c.line(9,6+y,13,6+y,'linen',4)
        if not back and not sideways:c.rect(10,19+y,13,20+y,'hull',0)
        if hurt:c.rect(7,10,9,13,'chitin',1)
    elif key=='drone':
        y=2 if stun else -bob
        for x,yy in [(4,6),(19,6),(4,17),(19,17)]:
            c.thick_line(12,11+y,x,yy+y,3,'hull',0)
            c.ellipse(x,yy+y,3,2,'hull',0);c.line(x-2,yy+y,x+2,yy+y,'hull',4)
            if not stun:
                phase=frame%3
                if phase==0:c.line(x-2,yy+y,x+2,yy+y,'linen',4)
                elif phase==1:c.line(x-2,yy-1+y,x+2,yy+1+y,'linen',4)
                else:c.line(x,yy-2+y,x,yy+2+y,'linen',4)
        c.polygon([(12,3+y),(18,8+y),(17,16+y),(12,20+y),(6,16+y),(5,8+y)],'hull',0)
        c.polygon([(12,5+y),(16,9+y),(15,15+y),(12,17+y),(8,15+y),(7,9+y)],'engineering',3)
        c.line(8,8+y,12,5+y,'linen',4)
        if back:c.rect(9,8+y,14,13+y,'hull',2);c.line(10,9+y,13,9+y,'hull',4)
        else:
            ex=15 if sideways else 12;c.ellipse(ex,11+y,3,3,'hull',0);c.ellipse(ex,11+y,2,2,'coolant',3 if stun else 5)
        if not stun:
            if back:c.thick_line(12,8+y,12,5-reach,2,'hull',4)
            elif sideways:c.thick_line(16,14+y,19+reach,14+y,2,'hull',4)
            else:c.thick_line(11,17+y,11,20+reach,2,'hull',4)
        else:c.line(9,18+y,15,18+y,'hull',3);c.line(10,20,11+frame%2,22,'hull',4)
        c.rect(10,7+y,13,7+y,'engineering',5);c.rect(9,15+y,10,16+y,'hull',1);c.rect(14,15+y,15,16+y,'hull',1)
        if hurt:c.rect(7,10,8,13,'hull',1)
    else:
        # A hunched, frost-armoured figure with a large cryo collar and hanging hose.
        y=2 if stun else bob
        leg=[1,-1,0,1,-1,0][frame] if clip=='walk' else 0
        c.rect(6,17+y,10,21+leg,'hull',0);c.rect(13,17+y,17,21-leg,'hull',0)
        c.line(7,20+leg,10,20+leg,'frost',2);c.line(14,20-leg,17,20-leg,'frost',2)
        c.polygon([(6,9+y),(15,7+y),(19,12+y),(18,18+y),(6,19+y),(3,14+y)],'hull',0)
        c.polygon([(6,10+y),(15,9+y),(17,13+y),(16,17+y),(7,17+y),(5,13+y)],'frost',2)
        c.rect(7,10+y,9,16+y,'frost',4);c.rect(14,10+y,16,15+y,'linen',3)
        c.ellipse(12,6+y,6,5,'hull',0);c.ellipse(12,6+y,5,4,'frost',4)
        if back:c.rect(8,4+y,15,8+y,'hull',3);c.rect(9,10+y,14,15+y,'hull',1)
        else:
            x=14 if sideways else 11;c.rect(x-3,4+y,x+3,7+y,'command',2);c.line(x-2,4+y,x+1,4+y,'linen',4)
            c.rect(x-2,8+y,x+1,10+y,'hull',3);c.px(x-2,5+y,'frost',5);c.px(x+1,6+y,'frost',5)
        if back:
            c.thick_line(5,12+y,3,9-reach,3,'frost',3);c.thick_line(17,12+y,20,9-reach,3,'frost',2)
        else:
            c.thick_line(5,12+y,3+(reach if sideways else 0),17+(0 if sideways else reach),3,'frost',3)
            c.thick_line(17,12+y,20+reach if sideways else 20,12+y if sideways else 16+reach,3,'frost',2)
        c.line(18,9+y,20,11+y,'coolant',3);c.line(20,11+y,19,15+y,'coolant',3)
        c.rect(5,11+y,7,12+y,'hull',3);c.rect(16,11+y,18,12+y,'hull',3)
        c.rect(9,13+y,11,15+y,'frost',4)
        if stun:c.line(8,10+y,15,11+y,'hull',2);c.line(18,17,19-frame%2,19,'frost',3)
        if hurt:c.rect(4,13,6,15,'frost',1)
    return c.render(P)


def entry(pack,key,image,**extra):return {**style.immutable(pack,'tokens',key,image),'frameSize':[24,24],**extra}


def build(root=ROOT,reference=REFERENCE):
    kinds=requirements(reference)
    if set(kinds)!=set(NAMES):raise ValueError('Scenario token IDs differ from authored recipes')
    for k,v in kinds.items():
        if v['monster']!=(k in MONSTERS):raise ValueError('Monster kind differs: '+k)
        if set(v['states'])!=set(STATES.get(k,[])):raise ValueError('Token states differ: '+k)
    pack=root/'content/meridian/base';report=root/'docs/superpowers/reports/meridian-art/tokens';report.mkdir(parents=True,exist_ok=True)
    result={}
    for key,spec in kinds.items():
        group={'name':NAMES[key],'frameSize':[24,24],'anchor':[12,22] if key in MONSTERS else [12,12],'monster':key in MONSTERS,'scenarios':spec['scenarios']}
        if key in MONSTERS:
            group['default']=entry(pack,key+'-default',monster(key),frames=1,fps=0,loop=False)
            for clip,(frames,fps,loop) in CLIPS.items():
                group[clip]={}
                for side in SIDES:
                    image=crew.strip([monster(key,side,clip,i) for i in range(frames)])
                    group[clip][side]={**style.immutable(pack,'tokens',key+'-'+clip+'-'+side,image),'frameSize':[24,24],'frames':frames,'fps':fps,'loop':loop,'blend':'alpha','reducedMotionFrame':0}
        else:
            if key in STATES:
                for state in STATES[key]:group[state]=entry(pack,key+'-'+state,token(key,state),frames=1,fps=0,loop=False)
                group['idle']=group[BASE[key]]
            else:group['idle']=entry(pack,key,token(key),frames=1,fps=0,loop=False)
            group['visibility']='private' if key in PRIVATE else 'hidden-until-found' if key=='charge' else 'public'
        result[key]=group
    manifest={'version':1,'issue':336,'rulesHead':HEAD,'tokens':result,'extras':{'hiddenMarker':entry(pack,'hidden-marker',token('hidden'),frames=1,fps=0,loop=False,visibility='private-reference-only')},'displayScale':2,'notes':'Labels are live. Private tokens never appear on the public TV. Rot/vent overlays are #337 effects. Monster default entries support review; canonical idle/walk/hurt/stun directions are n/e/s/w; attack clips await consumer mapping.'}
    style.write_json(pack/'tokens/manifest.json',manifest)
    previews(report,manifest)
    return pack,report


def still(m,key,state=None):
    if key in MONSTERS:return monster(key)
    return token(key,state or BASE.get(key))


def font(size):return ImageFont.truetype(str(ROOT/'src/ArcBox.Host/fonts/TitilliumWeb-SemiBold.ttf'),size)


def tile_background():
    r=next(r for r in compartments.rules() if r['id']=='service_corridor')
    return compartments.compose(compartments.tile(r,'engineering'),r).resize((128,128),Image.Resampling.NEAREST)


def previews(report,m):
    bg=tile_background()
    def label(im,pos,text,size=30,colour='#d8e1da'):ImageDraw.Draw(im).text(pos,text,font=font(size),fill=colour)
    def card(im,x,y,key,title=None,state=None,frame=None):
        d=ImageDraw.Draw(im);d.rounded_rectangle((x,y,x+296,y+268),radius=4,fill='#21243c',outline='#657482',width=2)
        label(im,(x+14,y+8),title or NAMES[key],28)
        im.paste(bg,(x+12,y+60));sprite=frame or still(m,key,state)
        im.alpha_composite(sprite.resize((48,48),Image.Resampling.NEAREST),(x+52,y+100))
        im.alpha_composite(sprite.resize((96,96),Image.Resampling.NEAREST),(x+176,y+76))
        label(im,(x+20,y+208),'TV 2×',24,'#9ee0ed');label(im,(x+166,y+208),'Detail 4×',24,'#a2b8c0')
    public=[k for k in NAMES if k not in PRIVATE]
    im=Image.new('RGBA',(1920,1080),'#111b2a');label(im,(32,18),'Meridian / scenario pieces',44);label(im,(32,76),'48px sprites on 128px rooms · larger 96px detail studies at right',30,'#9ee0ed')
    for i,key in enumerate(public):card(im,32+(i%6)*312,144+(i//6)*288,key,'Charge (found)' if key=='charge' else None)
    label(im,(32,1034),'Art review · private pieces stay off the public TV · labels remain live in game',28,'#a2b8c0');im.convert('RGB').save(report/'tokens-tv.png')
    def monster_poster(tick=0):
        out=Image.new('RGBA',(1920,1080),'#111b2a');label(out,(32,18),'Meridian / animated creatures',44);label(out,(32,76),'Game-size 48px figure on each 128px room · 96px detail view',30,'#9ee0ed')
        for row,key in enumerate(['brood','drone','sleeper']):
            for col,clip in enumerate(['idle','walk','attack','stun']):
                count,fps,loop=CLIPS[clip];frame=(tick*fps//8)%count if loop else min(count-1,tick*fps//8)
                x=32+col*472;y=144+row*288
                d=ImageDraw.Draw(out);d.rectangle((x,y,x+454,y+268),fill='#21243c',outline='#657482',width=2)
                label(out,(x+16,y+8),NAMES[key]+' / '+{'walk':'Move','stun':'Stunned'}.get(clip,clip.title()),30)
                out.paste(bg,(x+24,y+66));actor=monster(key,'s',clip,frame)
                out.alpha_composite(actor.resize((48,48),Image.Resampling.NEAREST),(x+64,y+106));out.alpha_composite(actor.resize((96,96),Image.Resampling.NEAREST),(x+220,y+82))
                label(out,(x+26,y+212),'Game 2×',24,'#9ee0ed');label(out,(x+220,y+212),'Detail 4×',24,'#a2b8c0')
        label(out,(32,1034),'Four facings per clip · no flashes or shake · reduced-motion review uses still frames',28,'#a2b8c0');return out.convert('RGB')
    monster_poster(2).save(report/'monsters-tv.png')
    frames=[monster_poster(i) for i in range(24)];frames[0].save(report/'monsters-motion.gif',save_all=True,append_images=frames[1:],duration=125,loop=0,optimize=True)
    out=Image.new('RGBA',(1920,1080),'#111b2a');label(out,(48,20),'State changes / shape first, colour second',44)
    for row,(key,a,b) in enumerate([('repair','broken','fixed'),('relay','powered','rerouted')]):
        y=120+row*392;label(out,(48,y),NAMES[key],38)
        for col,state in enumerate([a,b]):
            x=48+col*880;card(out,x,y+66,key,state.title(),state)
            label(out,(x+330,y+100),{'broken':'Open casing; separated contacts','fixed':'Closed casing; bridged circuit','powered':'Two straight cable feeds','rerouted':'Cable diverted to the side socket'}[state],30)
    label(out,(48,1000),'Charge disappears when disarmed; it does not leave a public “disarmed” marker.',28,'#a2b8c0');out.convert('RGB').save(report/'states-tv.png')
    out=Image.new('RGBA',(1920,1080),'#111b2a');label(out,(48,30),'Private art reference / never a public TV view',44)
    for i,key in enumerate(['sabotage','hidden']):
        x=160+i*880;image=token(key);out.alpha_composite(image.resize((192,192),Image.Resampling.NEAREST),(x,240));label(out,(x,450),'Sabotage' if key=='sabotage' else 'Generic hidden marker',38)
    label(out,(48,850),'Artwork availability does not grant visibility. Audience filtering belongs to the game.',32,'#a2b8c0');out.convert('RGB').save(report/'private-reference.png')
    detail=Image.new('RGBA',(1200,460),'#111b2a');label(detail,(32,16),'Creature detail studies / 6× source',40);label(detail,(32,74),'Actual 1080p game sprites are 48px; see the TV-scale posters.',26,'#9ee0ed')
    for i,key in enumerate(['brood','drone','sleeper']):
        detail.alpha_composite(monster(key,'s','attack',2).resize((144,144),Image.Resampling.NEAREST),(104+i*390,152));label(detail,(64+i*390,334),NAMES[key],34)
    detail.convert('RGB').save(report/'creatures-detail.png')
    review_page(report,m)


def review_page(report,m):
    prefix='../../../../../content/meridian/base/tokens/'
    def image(e,scale=2):return f'<img src="{prefix+e["file"]}" width="{24*scale}" height="{24*scale}" alt="">'
    monster_cards=[]
    for key in ['brood','drone','sleeper']:
        parts=[]
        for clip in ['idle','walk','attack','stun','hurt']:
            v=m['tokens'][key][clip];e=v['s'];count,fps,loop=CLIPS[clip]
            data=' '.join('data-'+s+'="'+prefix+v[s]['file']+'"' for s in SIDES)
            parts.append(f'<figure><figcaption>{ {"walk":"Move","stun":"Stunned"}.get(clip,clip.title())} · {count} frames / {fps}fps · '+('loop' if loop else f'{count/fps:.2f}s once')+f'</figcaption><div class="clip" role="img" aria-label="{NAMES[key]} {clip}" {data} data-count="{count}" data-fps="{fps}" data-loop="{str(loop).lower()}" style="background-image:url({prefix+e["file"]});background-size:{count*48}px 48px"></div></figure>')
        monster_cards.append('<article><h2>'+NAMES[key]+'</h2><div class="clips">'+''.join(parts)+'</div></article>')
    objects=[]
    for key in NAMES:
        if key in MONSTERS:continue
        e=m['tokens'][key];states=STATES.get(key,['idle']);parts=''.join('<figure>'+image(e[s])+f'<figcaption>{s.title()}</figcaption></figure>' for s in states)
        warning=' · private reference only' if key=='sabotage' else ' · public only after discovery' if key=='charge' else ''
        objects.append('<article'+(' class="private"' if key=='sabotage' else '')+'><h2>'+NAMES[key]+warning+'</h2><div class="clips">'+parts+'</div></article>')
    page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian monsters and tokens</title><style>@font-face{font-family:Ship;src:url('../../../../../src/ArcBox.Host/fonts/TitilliumWeb-SemiBold.ttf')}*{box-sizing:border-box}body{margin:auto;max-width:1320px;padding:16px;background:#111b2a;color:#d8e1da;font:18px/1.4 Ship,system-ui}h1{font-size:32px}h2{font-size:26px}a{color:#9ee0ed}article{border:1px solid #657482;padding:16px;margin:16px 0;background:#21243c}.clips,.controls{display:flex;flex-wrap:wrap;gap:18px}figure{margin:0;min-width:156px;max-width:100%}figcaption{font-size:18px;margin:8px 0}.clip{width:48px;height:48px;background-repeat:no-repeat;image-rendering:pixelated}img{image-rendering:pixelated;max-width:none}.pan{overflow:auto;max-height:80vh}.pan>img{width:1920px;height:1080px}.private{border:2px solid #e0bd64}button,select{font:inherit;min-height:44px;padding:8px;background:#657482;color:#e3eadf}input{width:24px;height:24px;vertical-align:middle}.controls{position:sticky;top:0;background:#111b2a;padding:12px 0;z-index:2}button:focus-visible,select:focus-visible{outline:3px solid #e0bd64;outline-offset:2px}</style><h1>Meridian / monsters and scenario pieces</h1><p>Original art for #336, covering the current twelve scenarios. The strips below are at the real 2× TV scale (48px). Native posters also include clearly labelled 4× detail studies. This is an asset review, not a gameplay capture. No text is painted into runtime art.</p><div class="controls"><label>Facing <select id="facing"><option value="s">South</option><option value="e">East</option><option value="n">North</option><option value="w">West</option></select></label><button id="pause">Pause</button><button id="replay">Replay attacks</button><label><input type="checkbox" id="still"> Still frames</label></div>'''+''.join(monster_cards)+'''<h2>TV-scale review</h2><p><a href="monsters-motion.gif">Open the animated creature poster</a> · motion is optional; the page honours reduced motion.</p>'''+''.join('<div class="pan" tabindex="0" aria-label="'+title+'"><img src="'+name+'-tv.png" width="1920" height="1080" alt="'+title+'"></div>' for name,title in [('monsters','Creature states at TV scale'),('tokens','Public scenario token art'),('states','Repair and relay state comparison')])+'''<h2>Objects and states / real 2× size</h2>'''+''.join(objects)+'''<article class="private"><h2>Private reference: hidden marker</h2>'''+image(m['extras']['hiddenMarker'])+'''<p>This marker and sabotage art are private references. Hidden pieces never appear in the public TV examples. A charge is public only after discovery; disarming removes it.</p><a href="private-reference.png">Private reference sheet</a></article><script>
const clips=[...document.querySelectorAll('.clip')],media=matchMedia('(prefers-reduced-motion:reduce)'),still=document.querySelector('#still'),pause=document.querySelector('#pause');let held=false,start=performance.now(),freeze=0;function stopped(){return media.matches||still.checked}function paint(now){const elapsed=(held?freeze:now)-start;clips.forEach(c=>{const n=Number(c.dataset.count),f=stopped()?0:c.dataset.loop==='true'?Math.floor(elapsed*Number(c.dataset.fps)/1000)%n:Math.min(n-1,Math.floor(elapsed*Number(c.dataset.fps)/1000));c.dataset.frame=String(Math.max(0,f));c.style.backgroundPosition=(-48*Math.max(0,f))+'px 0px'});pause.disabled=stopped();requestAnimationFrame(paint)}pause.onclick=()=>{if(held){start+=performance.now()-freeze}else freeze=performance.now();held=!held;pause.textContent=held?'Resume':'Pause'};document.querySelector('#replay').onclick=()=>{start=performance.now();freeze=start};document.querySelector('#facing').onchange=e=>{clips.forEach(c=>c.style.backgroundImage='url('+c.dataset[e.target.value]+')');start=performance.now();freeze=start};requestAnimationFrame(paint);
</script></html>'''
    (report/'index.html').write_text(page)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,default=ROOT);a=p.parse_args();build(a.root)
