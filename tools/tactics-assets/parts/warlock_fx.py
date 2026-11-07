"""Warlock pact marks, barbed force, infernal jaws and broken void geometry."""
import math
from .wizard_fx import PALETTES as SHARED
PALETTES=dict(SHARED,shadow='eldritch',hex='hex',hellfire='fel',void='eldritch')
DESIGNS={
 'eldritch_blast':('lance','force'),'hex':('bramble','shadow'),'chill_touch':('talon','grave'),
 'poison_spray':('gouts','venom'),'fiendish_vigor':('carapace','hellfire'),'maddening_hex':('shards','psychic'),
 'one_with_shadows':('folds','shadow'),'shroud_of_shadow':('veil','shadow'),'arms_of_hadar':('tentacles','grave'),
 'armor_of_agathys':('frostplate','frost'),'hellish_rebuke':('pyre','hellfire'),'witch_bolt':('tether','storm'),
 'shatter':('toll','thunder'),'misty_step':('crescent','mist'),'hold_person':('manacles','shadow'),
 'darkness':('eclipse','shadow'),'hunger_of_hadar':('maw','void'),'counterspell':('shears','arcane'),
 'vampiric_touch':('siphon','grave'),'blight':('wither','grave'),'dimension_door':('gateway','arcane'),
 'shadow_of_moil':('smoke','shadow'),'synaptic_static':('neurons','psychic'),'hold_monster':('cage','shadow'),
 'hexblades_curse':('runebrand','hex'),'cursed_slash':('blade','grave'),'armor_of_hexes':('lattice','hex'),
 'hellfire_surge':('inferno','hellfire'),'hurl_through_hell':('pit','hellfire'),'rift_step':('seam','arcane'),
 'gravitational_pull':('vortex','force'),'entropic_ward':('entropy','arcane'),'abyssal_singularity':('eye','psychic'),
 'pact_magic':('pact','force'),'hex_damage':('thorn','grave'),'agonizing_blast':('barbs','force'),
 'repelling_blast':('hammer','force'),'lifedrinker':('fangs','grave'),'companion':('imp','hellfire'),
 'dark_ones_blessing':('crown','hellfire'),'hunger_of_hadar_pulse':('frostbite','frost')}
PASSIVES={k:'fx.warlock_'+k.split('.')[-1]+'.'+DESIGNS[k.split('.')[-1]][1] for k in (
 'warlock.pact_magic','warlock.hex_damage','warlock.agonizing_blast','warlock.repelling_blast','warlock.lifedrinker',
 'fiendbound.companion','fiendbound.dark_ones_blessing')}
PASSIVES['warlock.hunger_of_hadar.turn']='fx.warlock_hunger_of_hadar_pulse.frost'
BEAMS={'gouts','tether'}
LARGE={'tentacles','toll','eclipse','maw','wither','neurons','inferno','pit','vortex','eye'}
DARK={'folds','veil','eclipse','maw','smoke'}
SMALL={'hunger_of_hadar_pulse','eldritch_blast','chill_touch','poison_spray','fiendish_vigor','maddening_hex','one_with_shadows','shroud_of_shadow'}|{k.split('.')[-1] for k in PASSIVES}
def dimensions(name,shot=False):
 shape,_=DESIGNS[name]
 return (48,32) if shot else (176,88) if shape in BEAMS else (224,144) if shape in LARGE else (72,72) if name in SMALL else (96,96)
def frames(name,shot=False):
 from .fx import Canvas,SLOTS,_twinkle
 shape,_=DESIGNS[name];w,h=dimensions(name,shot);count=8 if name in SMALL or shot else 12
 out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);grow=min(1,(i+1)/3);r=(80 if shape in LARGE else 15 if name in SMALL else 31)*grow
  cx,cy=(8,h/2) if shape in BEAMS else (w/2,h*.64)
  shade=5 if i in (2,3) else max(1,round(4*(1-t)));core='flash' if i==2 else 'core'
  def line(a,b,s=shade,slot='main'):c.line(*a,*b,slot,s)
  def ring(x,y,rx,ry,n=12,a=0,slot='main',s=shade):
   pts=[(x+math.cos(a+j*math.tau/n)*rx,y+math.sin(a+j*math.tau/n)*ry) for j in range(n)]
   for u,v in zip(pts,pts[1:]+pts[:1]):line(u,v,s,slot)
  def star(x,y,n=2):_twinkle(c,x,y,core,n,5)
  if shot:
   for j in range(3):
    y=16+(j-1)*5
    line((3,y+math.sin(i+j)*2),(30,y),3,slot='accent')
   c.polygon([(25,10),(43,16),(25,22),(29,16)],'main',5)
   line((30,16),(40,16),5,core)
  elif shape=='tether':
   for j in (-1,1):
    pts=[(8,cy)]+[(x*grow,cy+j*(4+abs(math.sin(x*.12+i))*7)) for x in range(18,170,10)]
    for u,v in zip(pts,pts[1:]):line(u,v,5 if j==1 else 3,core if j==1 else 'accent')
   for j in range(4):ring(25+j*39,cy,4,7,4,a=t+j)
  elif shape=='gouts':
   for j in range(21):
    u=((i*17+j*11)%145)/145;x=8+u*155*grow;y=cy+(j%5-2)*(3+u*8)
    c.polygon([(x-3,y),(x,y-5-j%3),(x+4,y+1),(x,y+3)],'main',shade)
    if j%3==0:line((x-9,y+1),(x-4,y+1),2)
  elif shape in ('lance','barbs','hammer'):
   n=8 if shape=='lance' else 5 if shape=='barbs' else 3
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r*t;y=cy+math.sin(a)*r*t*.6
    line((cx,cy),(x,y),5,core)
    if shape=='hammer':c.polygon([(x-4,y-4),(x+5,y-4),(x+5,y+3),(x-4,y+3)],'main',shade)
    else:line((x,y),(x-4,y-5));line((x,y),(x+3,y-5))
   if shape=='lance':ring(cx,cy,r*.6,r*.35,4,a=math.pi/4)
  elif shape in ('bramble','thorn','manacles','cage','runebrand','lattice'):
   n={'bramble':7,'thorn':3,'manacles':4,'cage':8,'runebrand':6,'lattice':10}[shape]
   ring(cx,cy,r,r*.5,n,a=t*.3)
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.5
    line((x,y),(x+3,y-9),5,core)
    if shape in ('manacles','cage'):line((x,y),(cx+math.cos(a)*r*.7,cy-26*grow))
    elif shape=='lattice':line((x,y),(cx-math.cos(a)*r,cy-math.sin(a)*r*.5),2)
    elif shape=='runebrand':line((cx,cy-20),(x,y),5,core)
    else:line((x,y),(x-6,y-3))
  elif shape=='frostbite':
   for j in range(6):
    a=j*math.pi/3;x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.6
    line((x,y),(cx+math.cos(a)*r*.5,cy+math.sin(a)*r*.3),5,core)
    c.polygon([(x-3,y+3),(x,y-5),(x+3,y+3)],'main',shade)
    if j%2==0:c.ellipse(x,y+4,2,2,'accent',shade)
  elif shape in ('talon','fangs','siphon'):
   for j in range(5 if shape=='talon' else 2 if shape=='fangs' else 7):
    x=cx+(j-(2 if shape=='talon' else .5 if shape=='fangs' else 3))*6;y=cy-10-abs(j-2)*2
    line((cx,cy+7),(x,y));line((x,y),(x+3,y-8),5,core)
    if shape=='siphon':star(cx+(x-cx)*(1-t),cy-15*t,1)
  elif shape in ('pyre','inferno','pit','crown','imp'):
   n=17 if shape=='inferno' else 11 if shape=='pit' else 5 if shape=='imp' else 7
   for j in range(n):
    x=cx+(j-(n-1)/2)*r*1.8/n;y=cy+math.sin(j*2.3)*r*.25
    top=(20 if shape not in LARGE else 47)*(1-t*.6)*(1 if j%2 else .65)
    c.polygon([(x-5,y),(x-1,y-top),(x+4,y-top*.4),(x+6,y)],'main',shade)
    if i<5:line((x,y),(x,y-top*.6),5,core)
   if shape=='pit':ring(cx,cy,r,r*.45,14,a=t)
   if shape=='imp':
    for side in (-1,1):line((cx,cy-13),(cx+side*18,cy-25),5,core);line((cx+side*18,cy-25),(cx+side*14,cy-8))
   if shape=='crown':ring(cx,cy,r,r*.5,7,a=t)
  elif shape in ('eclipse','maw','eye','folds','veil','smoke'):
   n={'eclipse':20,'maw':13,'eye':9,'folds':5,'veil':8,'smoke':11}[shape]
   ring(cx,cy,r,r*.55,n,a=t*.25,slot='accent',s=max(1,shade-1))
   if shape in ('maw','eye'):
    for j in range(n):
     a=j*math.tau/n;x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.55
     line((x,y),(cx+math.cos(a)*r*.65,cy+math.sin(a)*r*.3),5,core)
    if shape=='eye':ring(cx,cy,r*.25,r*.2,5,a=t*2,slot=core,s=5)
   else:
    for j in range(n):
     a=j*math.tau/n+t;line((cx+math.cos(a)*r,cy+math.sin(a)*r*.55),(cx+math.cos(a+.5)*r*.65,cy-18-j%3*4),2,slot='accent')
  elif shape in ('tentacles','wither','neurons','vortex'):
   n={'tentacles':8,'wither':11,'neurons':9,'vortex':15}[shape]
   for j in range(n):
    a=j*math.tau/n+(t*2 if shape=='vortex' else 0);reach=r*(t if shape=='wither' else 1-t*.4)
    x=cx+math.cos(a)*reach;y=cy+math.sin(a)*reach*.5
    mid=(cx+math.cos(a+.5)*reach*.6,cy+math.sin(a+.5)*reach*.3-10*grow)
    line((cx,cy),mid,2);line(mid,(x,y),shade)
    if shape=='tentacles':line((x,y),(x+5,y-13),5,core)
    elif shape=='neurons':star(x,y,2);line((x,y),(x+7,y-6),3)
    elif shape=='wither':line((x,y),(x-5,y-5));line((x,y),(x+3,y-9))
    else:line((x,y),(x+math.sin(a)*7,y-math.cos(a)*4),5,core)
  elif shape in ('crescent','gateway','seam','entropy','pact'):
   n={'crescent':9,'gateway':4,'seam':3,'entropy':6,'pact':5}[shape]
   ring(cx,cy-8,r*.65,r,n,a=t if shape=='entropy' else 0)
   if shape=='crescent':ring(cx+6,cy-10,r*.45,r*.8,9,a=t)
   elif shape=='seam':line((cx-3,cy-r),(cx+3,cy+r*.5),5,core)
   elif shape=='pact':
    for j in range(5):star(cx+(j-2)*6,cy-r+(1-t)*r,1)
   elif shape=='gateway':line((cx-r*.65,cy-r),(cx+r*.65,cy-r),5,core)
   else:line((cx-r*.6,cy-8),(cx+r*.6,cy-8),5,core)
  elif shape=='blade':
   for j in range(3):
    x=cx-25+50*t+j*3;y=cy-20+j*5
    line((x-17,y+21),(x+18,y-21),5 if j==0 else 3,core if j==0 else 'main')
   ring(cx,cy,r,r*.35,7,a=t,slot='accent',s=2)
  elif shape in ('carapace','frostplate'):
   for j in range(6):
    a=j*math.tau/6;x=cx+math.cos(a)*r*.7;y=cy+math.sin(a)*r*.5-7
    c.polygon([(x-5,y),(x,y-12),(x+6,y),(x,y+5)],'main',shade)
    line((x,y-10),(x,y+3),5,core)
   if shape=='carapace':ring(cx,cy,r,r*.5,6,a=t,slot='accent',s=2)
  elif shape=='toll':
   for j in range(3):ring(cx,cy,r*(.4+j*.25),r*(.2+j*.12),18,a=t+j*.1,s=max(1,shade-j))
   for j in range(8):
    a=j*math.pi/4;line((cx+math.cos(a)*r*.5,cy+math.sin(a)*r*.3),(cx+math.cos(a)*r,cy+math.sin(a)*r*.6),5,core)
  elif shape=='shears':
   line((cx-r*.8,cy-r*.7),(cx+r*.8,cy+r*.2),5,core)
   line((cx-r*.8,cy+r*.2),(cx+r*.8,cy-r*.7),5,core)
   for j in (-1,1):ring(cx+j*r*.5,cy-r*.4,6,5,6,a=t)
  for j in range(5):
   if 2<=i<count-1 and (j+i)%3==0:
    a=j*2.4+t;_twinkle(c,cx+math.cos(a)*r*(.5+t*.3),cy+math.sin(a)*r*.4-i,'accent',1,max(1,shade))
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(shape,material) in DESIGNS.items():
  w,h=dimensions(name)
  out.append(Fx('warlock_'+name,name.replace('_',' ').title(),'Warlock',lambda n=name:frames(n),
   {material:_v(main=PALETTES[material],core='fx/flash',accent=PALETTES['acid'] if name=='hunger_of_hadar_pulse' else 'eldritch' if material in ('force','grave','storm') else PALETTES[material])},
   20,(8,h//2) if shape in BEAMS else (w//2,round(h*.64)),'body' if shape in BEAMS else 'feet',blend='normal' if shape in DARK else 'add'))
 out.append(Fx('warlock_eldritch_blast_shot','Eldritch Blast flight','Warlock',lambda:frames('eldritch_blast',shot=True),
  {'force':_v(main=PALETTES['force'],core='fx/flash',accent='eldritch')},24,(24,16),'projectile',loop=True))
 return out
