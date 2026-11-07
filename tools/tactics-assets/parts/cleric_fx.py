"""Native Cleric art: living restoration, sacred geometry and distinct domain emblems."""
import math
from .wizard_fx import PALETTES as SHARED
PALETTES=dict(SHARED,radiant='magic/holy',healing='magic/nature',holy='fx/gold')
DESIGNS={
 'sacred_flame':('falling_sun','radiant'),'toll_the_dead':('bell','grave'),
 'word_of_radiance':('sunburst','radiant'),'resistance':('sixfold','holy'),
 'bless':('benediction','holy'),'bane':('broken_crown','grave'),
 'cure_wounds':('stitch','healing'),'guiding_bolt':('beacon','radiant'),
 'healing_word':('syllable','healing'),'inflict_wounds':('wound','grave'),
 'shield_of_faith':('shield','holy'),'command':('edict','holy'),
 'aid':('three_pillars','healing'),'hold_person':('fetters','holy'),
 'lesser_restoration':('wash','healing'),'spiritual_weapon':('mace','force'),
 'blindness':('shut_eye','grave'),'spirit_guardians':('guardians','radiant'),
 'mass_healing_word':('choir','healing'),'dispel_magic':('unbinding','holy'),
 'bestow_curse':('curse','grave'),'banishment':('exile','holy'),
 'death_ward':('ward','holy'),'flame_strike':('twin_pillars','fire'),
 'mass_cure_wounds':('garden','healing'),'greater_restoration':('renewal','healing'),
 'divine_spark':('spark','healing'),'turn_undead':('repulsion','radiant'),
 'sear_undead':('purge','radiant'),'divine_intervention':('descending_hand','healing'),
 'preserve_life':('chalice','healing'),'word_of_renewal':('rising_soul','healing'),
 'mass_revival':('ascension','healing'),'guided_strike':('sights','holy'),
 'war_priest':('hammer','radiant'),'avatar_of_battle':('panoply','holy'),
 'radiance_of_the_dawn':('horizon','radiant'),'warding_flare':('fan','radiant'),
 'corona_of_light':('corona','radiant'),'divine_strike':('blade','radiant'),
 'burning_luminescence':('afterglow','radiant'),'death_ward_rescue':('phoenix','holy')}
PASSIVES={'cleric.divine_strike':'fx.cleric_divine_strike.radiant',
 'light.burning_luminescence':'fx.cleric_burning_luminescence.radiant','death_ward.save':'fx.cleric_death_ward_rescue.holy'}
SMALL={'sacred_flame','toll_the_dead','word_of_radiance','resistance','divine_strike','burning_luminescence','death_ward_rescue'}
LARGE={'guardians','twin_pillars','garden','renewal','repulsion','purge','descending_hand','ascension','panoply','horizon','corona'}
def dimensions(name,shot=False):return (48,32) if shot else (192,144) if DESIGNS[name][0] in LARGE else (72,72) if name in SMALL else (96,96)
def frames(name,shot=False):
 from .fx import Canvas,SLOTS,_twinkle
 shape,material=DESIGNS[name];w,h=dimensions(name,shot);count=8 if name in SMALL or shot else 12;out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);g=min(1,(i+1)/3);cx,cy=w/2,h*.67
  r=(65 if shape in LARGE else 22 if name in SMALL else 32)*g;shade=max(1,round(5*(1-t*.8)))
  def line(a,b,s=shade,slot='main'):c.line(*a,*b,slot,s)
  def ring(x,y,rx,ry,n=16,a=0,slot='main',s=shade):
   pts=[(x+math.cos(a+j*math.tau/n)*rx,y+math.sin(a+j*math.tau/n)*ry) for j in range(n)]
   for u,v in zip(pts,pts[1:]+pts[:1]):line(u,v,s,slot)
  def star(x,y,size=2,slot='core'):_twinkle(c,x,y,slot,size,5)
  def cross(x,y,size=8):line((x,y-size),(x,y+size),5,'core');line((x-size*.65,y-size*.3),(x+size*.65,y-size*.3),5,'core')
  def ray(a,inner=.55,outer=1,slot='main'):line((cx+math.cos(a)*r*inner,cy+math.sin(a)*r*inner*.5),(cx+math.cos(a)*r*outer,cy+math.sin(a)*r*outer*.5),shade,slot)
  if shot:
   for j in (-1,0,1):line((4,16+j*4),(34,16+j*2),3 if j else 5,'core' if j==0 else 'main')
   c.polygon([(28,9),(44,16),(28,23),(32,16)],'main',5);star(36,16)
  elif shape=='falling_sun':
   y=cy-35+30*min(1,t*2);ring(cx,y,6,6,8,slot='core',s=5)
   for j in (-1,0,1):line((cx+j*5,y-16),(cx+j*3,y-3),3)
   for j in range(6):ray(j*math.pi/3,.25,t)
  elif shape=='bell':
   x=cx+math.sin(i)*2;y=cy-24
   c.polygon([(x-10,y+17),(x-7,y),(x,y-5),(x+7,y),(x+10,y+17)],'main',shade)
   line((x-13,y+18),(x+13,y+18),5,'core');star(x,y+21,1)
   for j in (-1,1):line((cx+j*(14+8*t),cy-14),(cx+j*(16+8*t),cy-2),2,'accent')
  elif shape in ('sunburst','repulsion','purge','horizon','corona'):
   n={'sunburst':8,'repulsion':12,'purge':15,'horizon':18,'corona':24}[shape]
   ring(cx,cy,r*(.3+t*.7),r*(.15+t*.35),n,slot='core',s=5)
   for j in range(n):
    a=j*math.tau/n;ray(a,.55,1+t*.15)
    if shape=='purge':line((cx+math.cos(a)*r,cy+math.sin(a)*r*.5),(cx+math.cos(a)*r,cy+math.sin(a)*r*.5-10*(1-t)),5,'core')
    if shape=='corona':star(cx+math.cos(a)*r*.8,cy+math.sin(a)*r*.4-15,1)
   if shape=='horizon':
    ring(cx,cy-18,r*.45,r*.4,14,slot='core',s=5);line((cx-r,cy),(cx+r,cy),5,'accent')
  elif shape in ('sixfold','benediction','shield','ward','panoply'):
   n={'sixfold':6,'benediction':3,'shield':5,'ward':8,'panoply':10}[shape]
   ring(cx,cy-12,r*.7,r*.85,n,slot='core',s=5)
   if shape=='shield':line((cx,cy-37),(cx,cy+12),3);line((cx-17,cy-17),(cx+17,cy-17),3)
   elif shape=='ward':cross(cx,cy-15,13);ring(cx,cy,r,r*.35,16)
   elif shape=='panoply':
    for side in (-1,1):line((cx+side*15,cy-42),(cx+side*40,cy-20),5,'core');line((cx+side*40,cy-20),(cx+side*26,cy+10))
    cross(cx,cy-24,16)
   else:
    for j in range(n):cross(cx+math.cos(j*math.tau/n)*r*.65,cy-12+math.sin(j*math.tau/n)*r*.65,4)
  elif shape in ('broken_crown','fetters','shut_eye','curse','wound'):
   if shape=='broken_crown':
    for j in range(5):
     x=cx+(j-2)*9;y=cy-9+abs(j-2)*2;line((x-4,y),(x,y-12),4);line((x,y-12),(x+4,y),4)
    line((cx-19,cy),(cx-3,cy+3));line((cx+3,cy+3),(cx+19,cy))
   elif shape=='fetters':
    for side in (-1,1):
     ring(cx+side*13,cy-15,6,9,6,slot='core',s=5)
     for j in range(4):ring(cx+side*(4+j*5),cy-2-j*2,3,2,4)
   elif shape=='shut_eye':
    ring(cx,cy-14,23,9,8);line((cx-25,cy-23),(cx+25,cy-5),5,'accent')
    for j in range(5):line((cx+(j-2)*9,cy-5),(cx+(j-2)*9,cy+3),3)
   elif shape=='curse':
    ring(cx,cy-13,r*.7,r*.7,5,a=-math.pi/2)
    for j in range(5):ray(j*math.tau/5,.3,.9,'accent')
   else:
    for j in range(4):line((cx-13+j*7,cy+5),(cx-9+j*7,cy-28),5,'core')
  elif shape in ('stitch','syllable','three_pillars','choir','garden','spark','rising_soul','ascension','chalice','wash','renewal'):
   n={'stitch':4,'syllable':3,'three_pillars':3,'choir':7,'garden':9,'spark':1,'rising_soul':1,'ascension':7,'chalice':5,'wash':6,'renewal':10}[shape]
   ring(cx,cy,r,r*.35,16,slot='accent',s=max(1,shade-1))
   for j in range(n):
    a=j*math.tau/n;x=cx+(0 if n==1 else math.cos(a)*r*.7);y=cy+(0 if n==1 else math.sin(a)*r*.3)
    rise=(t*22 if shape in ('rising_soul','ascension','syllable','choir') else 7+math.sin(t*math.pi)*8)
    if shape in ('wash','renewal'):
     line((x,y),(cx+math.cos(a+t)*r*.5,cy-20-rise),shade,'main');star(x,y-rise,2)
    elif shape=='chalice':
     line((cx-14,cy-24),(cx-8,cy-9));line((cx-8,cy-9),(cx+8,cy-9));line((cx+8,cy-9),(cx+14,cy-24));line((cx,cy-9),(cx,cy+4));line((cx-9,cy+4),(cx+9,cy+4));star(x,y-rise,1)
    elif shape=='stitch':
     line((cx-18,cy-j*5),(cx+18,cy-j*5),3);cross(cx+(j-1.5)*8,cy-8-rise,4)
    elif shape=='three_pillars':
     line((cx+(j-1)*16,cy),(cx+(j-1)*16,cy-30*g),4);cross(cx+(j-1)*16,cy-32*g,5)
    else:
     cross(x,y-rise,5 if shape!='spark' else 11)
     if shape in ('garden','ascension'):line((x,y),(x,y-rise+5),3)
  elif shape in ('beacon','sights','blade','afterglow'):
   if shape=='beacon':
    for j in range(4):ray(j*math.pi/2,-.4,1)
    ring(cx,cy-12,12,12,4,a=math.pi/4,slot='core',s=5);star(cx,cy-12,5)
   elif shape=='sights':
    ring(cx,cy-12,22,22,16);cross(cx,cy-12,12)
    for side in (-1,1):line((cx+side*32,cy-12),(cx+side*23,cy-12),5,'core')
   elif shape=='blade':
    line((cx-17,cy+3),(cx+16,cy-30),5,'core');line((cx-20,cy-4),(cx-9,cy+7),5,'main')
    for j in range(4):star(cx-12+j*9,cy-3-j*7,1)
   else:
    for j in range(6):
     a=j*math.pi/3;ray(a,.2,.8);star(cx+math.cos(a)*r*.8,cy+math.sin(a)*r*.4,1)
  elif shape in ('mace','hammer'):
   a=-1.1+t*1.9;x=cx+math.sin(a)*22;y=cy-20-math.cos(a)*18
   line((cx,cy),(x,y),4,'accent');c.polygon([(x-10,y-7),(x+10,y-7),(x+10,y+7),(x-10,y+7)],'main',shade)
   if shape=='mace':
    for side in (-1,1):c.polygon([(x+side*10,y-5),(x+side*15,y),(x+side*10,y+5)],'core',5)
   else:cross(x,y,5)
   if t>.4:
    for j in range(5):ray(j*math.tau/5,.15,t)
  elif shape=='guardians':
   ring(cx,cy,r,r*.4,20,slot='accent',s=2)
   for j in range(3):
    a=j*math.tau/3+t*.8;x=cx+math.cos(a)*r*.7;y=cy+math.sin(a)*r*.35
    ring(x,y-21,4,4,8,slot='core',s=5)
    c.polygon([(x,y-15),(x-9,y+4),(x+9,y+4)],'main',shade)
    line((x-7,y-10),(x-14,y-19),5,'core');line((x+7,y-10),(x+14,y-19),5,'core')
  elif shape=='edict':
   c.polygon([(cx-18,cy-32),(cx+18,cy-32),(cx+18,cy+2),(cx-18,cy+2)],'main',max(1,shade-2))
   for j in range(3):line((cx-11,cy-25+j*7),(cx+11-j*3,cy-25+j*7),5,'core')
   for side in (-1,1):line((cx+side*23,cy-30),(cx+side*23,cy),shade,'accent')
   star(cx,cy-40,3)
  elif shape=='unbinding':
   for j in range(8):
    a=j*math.pi/4;ray(a,.4+t*.2,.7+t*.3);star(cx+math.cos(a)*r,cy+math.sin(a)*r*.5-7,1)
   line((cx-20,cy+3),(cx+20,cy-30),5,'core')
  elif shape=='exile':
   for side in (-1,1):
    x=cx+side*(10+15*t);line((x,cy-35),(x,cy+5),5,'core');line((x,cy-35),(x-side*10,cy-28));line((x,cy+5),(x-side*10,cy-2))
   for j in range(3):star(cx+(j-1)*8,cy-25+t*28,1)
  elif shape=='twin_pillars':
   for j in range(9):
    x=cx+(j-4)*9;y=cy+math.sin(j*2)*8;top=65*g*(1-t*.4)
    c.polygon([(x-6,y),(x-2,y-top),(x+2,y-top*.55),(x+6,y)],'main',shade)
    line((x+3,y),(x+3,y-top*.9),5,'accent')
   ring(cx,cy,r,r*.3,18,slot='core',s=5)
  elif shape=='descending_hand':
   y=cy-55+22*min(1,t*2)
   for j in range(5):line((cx-14+j*7,y-13-j%2*5),(cx-14+j*7,y+12),5,'core')
   line((cx-16,y+12),(cx,y+25),5,'core');line((cx,y+25),(cx+16,y+12),5,'core')
   ring(cx,cy,r,r*.3,18,slot='accent',s=3)
   for j in range(5):cross(cx+(j-2)*17,cy-t*17,4)
  elif shape=='fan':
   for j in range(11):
    a=math.pi+j*math.pi/10;x=cx+math.cos(a)*r;y=cy-8+math.sin(a)*r
    line((cx,cy-8),(x,y),5 if j%2 else 3,'core' if j%2 else 'main')
  elif shape=='phoenix':
   cross(cx,cy-13,12)
   for side in (-1,1):
    for j in range(5):
     line((cx+side*3,cy-10),(cx+side*(12+j*4),cy-20-j*4+10*t),5 if j%2 else 3,'core' if j%2 else 'main')
   ring(cx,cy,24,8,16,slot='accent',s=3)
  if material=='healing':
   for j in range(3):star(cx+(j-1)*12,cy-7-t*25-j*3,1,'accent')
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(shape,material) in DESIGNS.items():
  w,h=dimensions(name)
  out.append(Fx('cleric_'+name,name.replace('_',' ').title(),'Cleric',lambda n=name:frames(n),
   {material:_v(main=PALETTES[material],core='magic/holy' if material=='healing' else 'fx/flash',accent='magic/holy' if shape=='twin_pillars' else 'fx/gold' if material=='healing' else PALETTES[material])},
   20,(w//2,round(h*.67)),'feet'))
 out.append(Fx('cleric_guiding_bolt_shot','Guiding Bolt flight','Cleric',lambda:frames('guiding_bolt',shot=True),
  {'radiant':_v(main='magic/holy',core='fx/flash',accent='fx/gold')},24,(24,16),'projectile',loop=True))
 return out
