"""Wizard spell identities. Indexed, seeded pixel geometry; no filtered particles or imported art."""
from __future__ import annotations
import math

# Spell, silhouette, material. Related schools keep a palette, never a shared damage sheet.
DESIGNS = {
 'fire_bolt':('ember','fire'), 'ray_of_frost':('crystal','frost'),
 'shocking_grasp':('hand','storm'), 'acid_splash':('splash','acid'),
 'chill_touch':('claw','grave'), 'poison_spray':('spray','venom'),
 'magic_missile':('facets','force'), 'burning_hands':('flame_cone','fire'),
 'shield':('shield','arcane'), 'sleep':('sleep','fey'),
 'thunderwave':('wave','thunder'), 'chromatic_orb':('orb','storm'),
 'mage_armor':('armor','arcane'), 'ray_of_sickness':('sickness','sickness'),
 'grease':('puddle','arcane'), 'scorching_ray':('trident','fire'),
 'hold_person':('chains','arcane'), 'misty_step':('door','mist'),
 'shatter':('shards','thunder'), 'web':('web','silk'),
 'blur':('echo','mist'), 'invisibility':('vanish','mist'),
 'fireball':('inferno','fire'), 'lightning_bolt':('lightning','storm'),
 'counterspell':('cancel','arcane'), 'haste':('clock_fast','haste'),
 'slow':('clock_slow','slow'), 'hypnotic_pattern':('lattice','psychic'),
 'dispel_magic':('unravel','arcane'), 'ice_storm':('hail','frost'),
 'greater_invisibility':('cloak','mist'), 'blight':('roots','grave'),
 'wall_of_fire':('fire_ring','fire'), 'banishment':('banish','arcane'),
 'cone_of_cold':('frost_cone','frost'), 'cloudkill':('gas','venom'),
 'hold_monster':('cage','arcane'), 'arcane_recovery':('well','arcane'),
 'sculpted_nova':('nova','fire'), 'sculpted_inferno':('crown','fire'),
 'time_shift':('hourglass','haste'), 'accelerate':('arrows','haste'),
 'temporal_stasis':('time_cage','slow'), 'raise_earth':('raise','earth'),
 'sink_earth':('sink','earth'), 'tremor':('fault','earth'),
 'stone_spire':('spire','earth'), 'empowered_evocation':('sigil','force'),
}
PALETTES = {'fire':'magic/fire','frost':'magic/frost','storm':'fx/storm',
 'acid':'fx/acid','grave':'fx/grave','venom':'fx/poison','force':'fx/force',
 'arcane':'magic/arcane','fey':'magic/nature','psychic':'fx/psychic','thunder':'fx/thunder',
 'sickness':'fx/miasma','mist':'fx/mist','silk':'fx/silk',
 'haste':'fx/gold','slow':'fx/ice','earth':'fx/dust'}
BEAMS={'flame_cone','frost_cone','lightning','spray'}
LARGE={'inferno','nova','crown','fire_ring','hail','gas','lattice','wave','shards','fault','spire'}

def frames(name, shot=False):
    from .fx import Canvas,SLOTS,_twinkle
    shape,material=DESIGNS[name]
    small=name in list(DESIGNS)[:6]
    w,h=(48,32) if shot else (160,96) if shape in BEAMS else (256,160) if shape in LARGE else (72,72) if small else (96,96)
    count=8 if small or shot else 12
    out=[]
    for i in range(count):
        c=Canvas(w,h,SLOTS);t=i/(count-1);peak=min(1,(i+1)/3);fade=max(.1,1-t)
        cx,cy=(8,h/2) if shape in BEAMS or shot else (w/2,h*.64)
        r=(10 if small else 25 if shape not in LARGE else 90)*peak
        shade=5 if i in (2,3) else max(1,round(4*fade))
        main='main';core='flash' if i==2 else 'core'
        def line(x,y,xx,yy,s=shade,slot=main):c.line(x,y,xx,yy,slot,s)
        def ring(radius,sy=.5,steps=12,angle=0):
            points=[(cx+radius*math.cos(angle+j*math.tau/steps),cy+radius*sy*math.sin(angle+j*math.tau/steps)) for j in range(steps)]
            for a,b in zip(points,points[1:]+points[:1]):line(*a,*b)
        if shot:
            heads=3 if name=='magic_missile' else 1
            for j in range(heads):
                y=cy+(j-(heads-1)/2)*7
                c.polygon([(35,y),(26,y-3),(21,y),(26,y+3)],main,5)
                line(5+(i+j)%4,y,24,y,3);c.px(31,y,core,5)
        elif shape in BEAMS:
            for j in range(7 if 'cone' in shape else 4):
                y=cy+(j-3)*3;points=[]
                for x in range(8,153,8):
                    spread=(x/6 if 'cone' in shape else 3)
                    yy=y+math.sin(x*.4+j*2+i)*spread
                    points.append((x*peak,yy))
                for a,b in zip(points,points[1:]):line(*a,*b)
                for x,y in points[::3]:c.px(x,y,core,5)
        elif shape in ('ember','inferno','nova','crown','fire_ring','trident'):
            tongues=3 if small else 9 if shape!='crown' else 13
            for j in range(tongues):
                a=j*math.tau/tongues;rr=r*(.7 if shape in LARGE else .3)
                x=cx+math.cos(a)*rr;y=cy+math.sin(a)*rr*.45
                height=(22+j%3*8 if shape in LARGE else 12+j%3*5)*peak*fade
                width=8 if shape in LARGE else 4
                c.polygon([(x-width,y),(x-width/2,y-height),(x+width*.6,y-height*.4),(x+width,y)],main,shade)
                if i<5:c.line(x,y,x,y-height*.6,core,5)
            if shape in ('inferno','nova','crown') and 1<i<6:
                # Brief rolling detonation at the centre; the outer flame fronts leave the figures readable.
                for j in range(3):
                    x=cx+(j-1)*18;height=(42+(j==1)*18)*(1-t)
                    c.polygon([(x-10,cy),(x-7,cy-height*.6),(x,cy-height),(x+6,cy-height*.4),(x+12,cy)],main,max(2,shade-1))
                    line(x,cy,x-1,cy-height*.5,5,core)
            if shape in ('inferno','nova','fire_ring'):ring(r,steps=16)
            if shape=='nova':ring(r*.65,steps=8,angle=t)
            if shape=='crown':
                for j in range(5):line(cx-30+j*15,cy,cx-30+j*15,cy-25-(j%2)*12)
        elif shape in ('crystal','hail','shards','spire','raise','sink','fault'):
            n=3 if small else 7
            for j in range(n):
                x=cx+(j-(n-1)/2)*r*.35;y=cy+math.sin(j*2)*r*.25
                height=(9+j%3*6)*peak
                if shape=='hail':y-=((i*7+j*13)%40);height=10
                if shape=='sink':height*=1-t
                if shape=='fault':
                    line(x-6,y+8,x,y-4);line(x,y-4,x+6,y+6);continue
                c.polygon([(x-5,y),(x-3,y-height),(x+3,y-height-4),(x+6,y)],main,shade)
                line(x,y,x+1,y-height,5,core)
            if shape in ('shards','hail'):ring(r,steps=9,angle=t)
        elif shape in ('hand','claw','roots'):
            for j in range(5):
                x=cx+(j-2)*6;y=cy-18-abs(j-2)*-3
                line(cx+(j-2)*2,cy+4,x,y)
                line(x,y,x+(2 if shape=='claw' else -2),y-7*peak,5,core)
            if shape=='hand':
                for j in range(4):line(cx-20+j*12,cy+10,cx-16+j*12,cy-14+(i+j)%3*5)
            if shape=='roots':ring(r,steps=7,angle=-t)
        elif shape in ('splash','puddle','gas','sickness'):
            ring(r,sy=.3,steps=14)
            for j in range(9):
                a=j*2.4;x=cx+math.cos(a)*r*.8;y=cy+math.sin(a)*r*.35
                if shape!='puddle':y-=math.sin(t*math.pi)*(j%4+2)*4
                c.ellipse(x,y,2+j%3,2+j%2,main,shade)
            if shape=='gas':ring(r*.6,sy=.7,steps=9,angle=t)
        elif shape in ('chains','cage','time_cage','web','lattice'):
            sides=4 if shape=='chains' else 6 if shape=='cage' else 8
            ring(r,sy=.55,steps=sides,angle=math.pi/4)
            for j in range(sides):
                a=j*math.tau/sides; x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.55
                line(x,y,x, y-22*peak)
                if shape in ('web','lattice'):line(x,y,cx,cy-12)
                else:_twinkle(c,x,y-22*peak,core,1)
            if shape=='lattice':ring(r*.6,sy=.7,steps=4,angle=t*2)
            if shape=='time_cage':line(cx-r,cy-12,cx+r,cy-12,5,core)
        elif shape in ('door','banish','cloak','vanish','echo'):
            copies=3 if shape=='echo' else 1
            for j in range(copies):
                x=cx+(j-(copies-1)/2)*(7+i)
                for k in range(16):
                    if shape in ('vanish','cloak') and (k+i)%3:continue
                    a=k*math.tau/16
                    c.px(x+math.cos(a)*r*.6,cy-13+math.sin(a)*r,main,shade)
            if shape in ('door','banish'):line(cx-r*.5,cy+8,cx+r*.5,cy+8,5,core)
        elif shape.startswith('clock') or shape in ('hourglass','arrows','well'):
            ring(r,sy=.75,steps=12)
            angle=t*math.tau*(1 if shape!='clock_slow' else -.5)
            line(cx,cy,cx+math.cos(angle)*r*.8,cy+math.sin(angle)*r*.6,5,core)
            if shape=='hourglass':
                for a,b in [((-12,-20),(12,8)),((12,-20),(-12,8))]:line(cx+a[0],cy+a[1],cx+b[0],cy+b[1])
            if shape=='arrows':
                for j in range(3):line(cx-12+j*9,cy-12,cx-6+j*9,cy-21)
            if shape=='well':
                for j in range(4):_twinkle(c,cx+(j-2)*8,cy-((i*3+j*7)%35),core,1)
        else:
            sides={'shield':7,'armor':6,'facets':3,'orb':8,'wave':16,'sleep':5,'cancel':4,'unravel':9,'sigil':3}.get(shape,6)
            ring(r,sy=.65 if shape!='shield' else 1,steps=sides,angle=t if shape in ('orb','unravel') else 0)
            if shape=='armor':ring(r*.7,sy=.9,steps=6)
            if shape=='shield':line(cx-r,cy,cx,cy-r,5,core);line(cx,cy-r,cx+r,cy)
            if shape=='cancel':line(cx-r*.6,cy-r*.6,cx+r*.6,cy+r*.6,5,core);line(cx-r*.6,cy+r*.6,cx+r*.6,cy-r*.6,5,core)
            if shape=='wave':ring(r*.6,steps=16)
            if shape=='orb':
                for j in range(4):line(cx,cy,cx+math.cos(j*math.pi/2+t)*r,cy+math.sin(j*math.pi/2+t)*r*.65,5,core)
            if shape=='sleep':
                for j in range(3):_twinkle(c,cx+(j-1)*10,cy-12-j*6-i,core,1)
            if shape=='sigil':ring(r*.5,steps=3,angle=math.pi)
        # Sparse, travelling residue; never an opaque blanket over the actor/grid.
        for j in range(5 if small else 9):
            a=j*2.399;rr=r*(.6+t*.7);x=cx+math.cos(a)*rr;y=cy+math.sin(a)*rr*.5-i*.7
            if 2<=i<=count-2 and (i+j)%3==0:_twinkle(c,x,y,'accent',0 if i>count*.6 else 1,max(1,shade))
        out.append(c)
    return out

def catalogue():
    from .fx import Fx,_v
    entries=[]
    for name,(shape,material) in DESIGNS.items():
        w,h=(160,96) if shape in BEAMS else (256,160) if shape in LARGE else (72,72) if name in list(DESIGNS)[:6] else (96,96)
        anchor=(8,h//2) if shape in BEAMS else (w//2,round(h*.64))
        entries.append(Fx('wizard_'+name,name.replace('_',' ').title(),'Wizard',lambda n=name:frames(n),
            {material:_v(main=PALETTES[material],core='fx/flash',accent=PALETTES[material])},20,anchor,'body' if shape in BEAMS else 'feet'))
    for name in ['fire_bolt','ray_of_frost','magic_missile','chromatic_orb','ray_of_sickness','scorching_ray','fireball']:
        material=DESIGNS[name][1]
        entries.append(Fx('wizard_'+name+'_shot',name.replace('_',' ').title()+' flight','Wizard',lambda n=name:frames(n,True),
            {material:_v(main=PALETTES[material],core='fx/flash',accent=PALETTES[material])},24,(24,16),'projectile',loop=True))
    return entries
