"""Innate magic: coiling releases, fractured chaos, draconic silhouettes and carved runes."""
import math
from .wizard_fx import PALETTES

DESIGNS={
 'innate_sorcery':('surge','arcane'),'font_of_magic_mana':('mana','arcane'),'font_of_magic_sorcery':('sorcery','psychic'),
 'metamagic_twinned':('twin','force'),'metamagic_extended':('extend','arcane'),'metamagic_careful':('careful','gold'),
 'metamagic_quickened':('quick','storm'),'metamagic_distant':('distant','force'),
 'fire_bolt':('comet','fire'),'ray_of_frost':('icicle','frost'),'shocking_grasp':('knuckles','storm'),
 'acid_splash':('crown_splash','acid'),'chill_touch':('bone','grave'),'poison_spray':('poison_cone','venom'),
 'chromatic_orb':('gem','storm'),'burning_hands':('finger_flames','fire'),'magic_missile':('darts','force'),
 'shield':('shell','arcane'),'thunderwave':('shockwave','thunder'),'sleep':('moons','fey'),
 'scorching_ray':('rays','fire'),'shatter':('fracture','thunder'),'hold_person':('fetters','arcane'),
 'misty_step':('portal','mist'),'web':('net','silk'),'fireball':('sunburst','fire'),
 'lightning_bolt':('fork','storm'),'haste':('fast','gold'),'slow':('drag','frost'),'counterspell':('sever','arcane'),
 'ice_storm':('blizzard','frost'),'wall_of_fire':('wall','fire'),'banishment':('rift','arcane'),
 'greater_invisibility':('cloak','mist'),'cone_of_cold':('glacier','frost'),'hold_monster':('prison','arcane'),
 'dragon_breath':('dragon','fire'),'dragon_wings':('wings','fire'),'tides_of_chaos':('tide','psychic'),
 'wild_surge':('roulette','force'),'bend_luck':('luck','psychic'),'controlled_surge':('controlled','arcane'),
 'ember_rune':('ember_rune','fire'),'frost_rune':('frost_rune','frost'),'grand_sigil':('sigil','arcane'),
 'elemental_affinity':('scales','fire')}
PALETTES=dict(PALETTES,gold='fx/gold')
SHOTS={'fire_bolt','ray_of_frost','chromatic_orb','magic_missile','scorching_ray','fireball'}
BEAMS={'finger_flames','poison_cone','fork','glacier','dragon'}
LARGE={'sunburst','blizzard','wall','shockwave','fracture','controlled','sigil'}
DAMAGE_VARIANTS={'sorcerer_wild_surge':{'force':'force','lightning':'storm','psychic':'psychic'},
                 'sorcerer_controlled_surge':{'force':'force','fire':'fire'}}

def frames(name,material=None,shot=False):
    from .fx import Canvas,SLOTS,_twinkle
    shape,default=DESIGNS[name];material=material or default
    small=name in ('fire_bolt','ray_of_frost','shocking_grasp','acid_splash','chill_touch','poison_spray')
    w,h=(48,32) if shot else (192,112) if shape in BEAMS else (224,144) if shape in LARGE else (72,72) if small else (96,96)
    count=8 if small or shot else 12
    out=[]
    for i in range(count):
        c=Canvas(w,h,SLOTS);t=i/(count-1);rise=min(1,(i+1)/3);fade=1-t
        cx,cy=(8,h/2) if shot or shape in BEAMS else (w/2,h*.64)
        r=(12 if small else 78 if shape in LARGE else 29)*rise
        shade=5 if i in (2,3) else max(1,round(4*(1-t)))
        core='flash' if i==2 else 'core'
        def line(a,b,s=shade,slot='main'):c.line(*a,*b,slot,s)
        def circle(x,y,rx,ry,n=16,angle=0,slot='main',s=shade):
            points=[(x+rx*math.cos(angle+j*math.tau/n),y+ry*math.sin(angle+j*math.tau/n)) for j in range(n)]
            for a,b in zip(points,points[1:]+points[:1]):line(a,b,s,slot)
        def star(x,y,size=3):_twinkle(c,x,y,core,size,5)
        if shot:
            n=3 if name=='magic_missile' else 1
            for j in range(n):
                y=16+(j-(n-1)/2)*7
                points=[(4+k*4,y+math.sin(k*.9+i+j)*2) for k in range(7)]
                for a,b in zip(points,points[1:]):line(a,b,3)
                if name=='ray_of_frost':c.polygon([(24,y-4),(39,y),(24,y+4),(29,y)],'main',5)
                elif name=='chromatic_orb':circle(32,y,6,5,6,t,core,5)
                else:star(33,y,2);line((27,y),(36,y),5,core)
        elif shape in BEAMS:
            if shape=='fork':
                path=[(8,cy)]+[(x*rise,cy+((j*19+i*11)%21-10)) for j,x in enumerate(range(20,183,14))]
                for j,(a,b) in enumerate(zip(path,path[1:])):
                    line(a,b,5,core)
                    if j%2==0:line(b,(b[0]+8,b[1]-13));line((b[0]+8,b[1]-13),(b[0]+18,b[1]-9))
            elif shape=='glacier':
                for j in range(17):
                    u=((i*9+j*13)%160)/160;x=15+u*160*rise;y=cy+(j%7-3)*u*10
                    c.polygon([(x-6,y+3),(x,y-9),(x+4,y-3),(x+11,y)],'main',shade)
                    line((x-2,y),(x,y-6),5,core)
            elif shape=='poison_cone':
                for j in range(18):
                    u=((i*17+j*23)%165)/165;x=10+u*165*rise;y=cy+(j%7-3)*u*11
                    circle(x,y,2+j%4,1+j%3,6,angle=t,s=max(1,shade-1))
                    if j%3==0:c.px(x+1,y-1,'main',shade)
            else:
                for j in range(5 if shape=='finger_flames' else 9):
                    points=[]
                    for k,x in enumerate(range(8,184,7)):
                        spread=x*(.17 if shape=='finger_flames' else .22)
                        y=cy+(j-(2 if shape=='finger_flames' else 4))*spread/(2 if shape=='finger_flames' else 4)+math.sin(k*.7+i*.8+j)*4
                        points.append((x*rise,y))
                    for a,b in zip(points,points[1:]):line(a,b)
                    for x,y in points[2::5]:c.polygon([(x-4,y+2),(x+8,y),(x-2,y-3)],core,5 if i<4 else 3)
                if shape=='dragon':
                    line((8,cy-10),(28,cy-19),5,core);line((28,cy-19),(23,cy-8))
                    line((8,cy+10),(28,cy+19),5,core);line((28,cy+19),(23,cy+8))
        elif shape in ('comet','sunburst','rays','wall'):
            arms=5 if small else 11 if shape=='sunburst' else 7
            for j in range(arms):
                a=j*math.tau/arms+t*1.8;rr=r*(.15+.55*t)
                x=cx+math.cos(a)*rr;y=cy+math.sin(a)*rr*.48
                height=(12 if small else 36)*(1-t*.8)
                c.polygon([(x-4,y),(x-1,y-height),(x+5,y-height*.35),(x+7,y)],'main',shade)
                if i<5:line((x,y),(x+1,y-height*.6),5,core)
            if shape=='sunburst':
                for j in range(2):circle(cx,cy,r*(.4+t*.7),r*(.2+t*.35),18,t*2+j*math.pi, s=max(1,shade-1))
                if i in (2,3,4):star(cx,cy-12,9)
            elif shape=='wall':
                for j in range(7):line((cx-r+j*r/3,cy),(cx-r+j*r/3,cy-25-j%2*14))
        elif shape in ('icicle','blizzard','fracture'):
            n=5 if small else 15
            for j in range(n):
                a=j*2.4;x=cx+math.cos(a)*r*(.2+t*.8);y=cy+math.sin(a)*r*.4
                if shape=='blizzard':y-=((i*9+j*13)%40)
                c.polygon([(x-5,y+3),(x,y-10-j%3*3),(x+3,y-3),(x+8,y)],'main',shade)
                line((x-2,y),(x,y-8),5,core)
            if shape=='fracture':
                for j in range(6):
                    a=j*math.tau/6;line((cx,cy),(cx+math.cos(a)*r,cy+math.sin(a)*r*.6),2)
        elif shape in ('crown_splash','knuckles','bone'):
            for j in range(7 if shape=='crown_splash' else 5):
                a=j*math.tau/7;x=cx+(j-2)*5 if shape!='crown_splash' else cx+math.cos(a)*r
                y=cy-abs(j-2)*2 if shape!='crown_splash' else cy+math.sin(a)*r*.35-10*math.sin(t*math.pi)
                if shape=='crown_splash':c.ellipse(x,y,2+j%2,2,'main',shade);line((cx,cy),(x,y),2)
                elif shape=='knuckles':line((x-3,cy+4),(x,y-13),5,core);star(x,y-15,1)
                else:line((cx,cy+4),(x,y-8));circle(x,y-10,3,3,4,slot=core,s=5)
        elif shape in ('roulette','controlled','tide','luck'):
            n=7 if shape=='roulette' else 11 if shape=='controlled' else 5 if shape=='tide' else 4
            for j in range(n):
                a=j*math.tau/n+(-1 if j%2 else 1)*t*2
                x=cx+math.cos(a)*r*(.3+t*.5);y=cy+math.sin(a)*r*.6
                c.polygon([(x-5,y-4),(x+6,y),(x-3,y+5)],'main',shade)
                if j%2:line((x,y),(cx,cy),2)
                if material=='storm':line((x,y),(x+5,y-8),5,core)
                elif material=='fire':line((x,y),(x-2,y-15*(1-t)),5,core)
                elif material=='psychic':circle(x,y,6,3,4,t,s=3)
            if shape=='controlled':circle(cx,cy,r,r*.5,8,math.pi/8,s=shade)
        elif shape in ('wings','scales'):
            for side in (-1,1):
                tip=cx+side*r*(.6+math.sin(t*math.pi)*.4)
                c.polygon([(cx,cy),(tip,cy-32*rise),(tip-side*4,cy-9),(cx+side*10,cy+4)],'main',max(1,shade-1))
                for j in range(4):line((cx,cy),(cx+side*(10+j*6),cy-20-j*3),5 if j==0 else shade,core if j==0 else 'main')
            if shape=='scales':
                for j in range(4):circle(cx+(j%2)*8-4,cy-j*5,5,3,4,slot=core,s=5)
        elif shape in ('ember_rune','frost_rune','sigil'):
            sides=3 if shape=='ember_rune' else 6 if shape=='frost_rune' else 9
            circle(cx,cy,r,r*.55,sides,math.pi/6)
            for j in range(sides):
                a=j*math.tau/sides;x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.55
                line((x-3,y-4),(x+3,y+4),5,core);line((x-4,y+2),(x+4,y-2))
            if shape=='sigil':circle(cx,cy,r*.6,r*.3,3,t,core,5)
        else:
            n={'surge':9,'mana':6,'sorcery':3,'twin':2,'extend':8,'careful':7,'quick':4,'distant':12,'gem':8,'darts':3,'shell':7,'shockwave':18,'moons':5,'fetters':4,'portal':12,'net':8,'fast':12,'drag':10,'sever':4,'rift':5,'cloak':9,'prison':6}.get(shape,6)
            if shape=='twin':
                for side in (-1,1):circle(cx+side*12,cy,r*.6,r*.4,8,t*side,core,5)
            else:circle(cx,cy,r,r*(.9 if shape in ('shell','portal','rift','cloak') else .5),max(3,n),t*.6)
            if shape in ('fetters','net','prison'):
                for j in range(n):
                    a=j*math.tau/n;x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.5
                    line((x,y),(cx,cy-20*rise),5 if shape=='prison' else shade,core if shape=='prison' else 'main')
            elif shape in ('fast','drag','quick','distant','extend'):
                for j in range(3):
                    x=cx-14+j*12;y=cy-16+(j if shape=='drag' else -j)*4
                    line((x-4,y+5),(x+3,y));line((x+3,y),(x-4,y-5))
                if shape=='drag':line((cx-r,cy+6),(cx+r,cy+6),2)
            elif shape in ('mana','sorcery','surge'):
                for j in range(6):
                    a=j*math.tau/6+t*(3 if shape=='sorcery' else -2);rr=r*(1-t*.7)
                    star(cx+math.cos(a)*rr,cy+math.sin(a)*rr*.6-(i if shape=='mana' else 0),1)
            elif shape in ('sever','rift'):
                line((cx-r*.7,cy-20),(cx+r*.7,cy+8),5,core)
                if shape=='sever':line((cx-r*.7,cy+8),(cx+r*.7,cy-20),5,core)
            elif shape=='moons':
                for j in range(3):circle(cx+(j-1)*12,cy-12-j*7-i*.4,3+j,4+j,8,t,core,5)
            elif shape=='gem':
                for j in range(4):
                    a=j*math.pi/2+t;line((cx,cy),(cx+math.cos(a)*r,cy+math.sin(a)*r*.5),5,core)
            elif shape=='darts':
                for j in range(3):line((cx-15+j*12,cy+5),(cx-9+j*12,cy-14),5,core)
            elif shape=='shockwave':circle(cx,cy,r*.6,r*.3,18,s=2)
            elif shape=='careful':
                line((cx-r*.5,cy),(cx,cy-20),5,core);line((cx,cy-20),(cx+r*.5,cy))
        for j in range(6):
            if 2<=i<count-1 and (i+j)%3==0:
                a=j*2.4+t;rr=r*(.5+t*.4)
                _twinkle(c,cx+math.cos(a)*rr,cy+math.sin(a)*rr*.4-i*.7,'accent',0 if i>5 else 1,max(1,shade))
        out.append(c)
    return out

def catalogue():
    from .fx import Fx,_v
    out=[]
    for name,(shape,material) in DESIGNS.items():
        ident='sorcerer_'+name
        materials=list(dict.fromkeys([material]+list(DAMAGE_VARIANTS.get(ident,{}).values())))
        w,h=(192,112) if shape in BEAMS else (224,144) if shape in LARGE else (72,72) if name in ('fire_bolt','ray_of_frost','shocking_grasp','acid_splash','chill_touch') else (96,96)
        out.append(Fx(ident,name.replace('_',' ').title(),'Sorcerer',lambda n=name:frames(n),
            {m:_v(main=PALETTES[m],core='fx/flash',accent=PALETTES[m]) for m in materials},20,(8,h//2) if shape in BEAMS else (w//2,round(h*.64)), 'body' if shape in BEAMS else 'feet'))
    for name in sorted(SHOTS):
        material=DESIGNS[name][1]
        out.append(Fx('sorcerer_'+name+'_shot',name.replace('_',' ').title()+' flight','Sorcerer',lambda n=name:frames(n,shot=True),
            {material:_v(main=PALETTES[material],core='fx/flash',accent=PALETTES[material])},24,(24,16),'projectile',loop=True))
    return out
