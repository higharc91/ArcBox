"""Paladin: consecrated steel, protective azure, oath marks and measured holy light."""
import math
PALETTES={'holy':'magic/holy','gold':'fx/gold','azure':'magic/frost','fire':'magic/fire','ember':'magic/fire','arcane':'magic/arcane'}
DESIGNS={
 'lay_on_hands':('hands','holy'),'divine_smite':('charge1','gold'),'divine_smite_2':('charge2','gold'),'divine_smite_3':('charge3','gold'),
 'bless':('benediction','gold'),'command':('edict','holy'),'compelled_duel':('duel','gold'),'cure_wounds':('mend','holy'),
 'shield_of_faith':('faith','azure'),'aid':('standards','gold'),'lesser_restoration':('purify','holy'),'dispel_magic':('sever','arcane'),
 'elemental_weapon':('fire_blade','fire'),'sanctuary_aegis':('aegis','azure'),'righteous_challenge':('challenge','gold'),
 'divine_intervention':('intercept','azure'),'aegis_of_faith':('citadel','azure'),'searing_smite':('searing','fire'),
 'consecrated_ground':('consecrate','gold'),'dawnbreaker':('dawn','holy'),'vow_of_enmity':('vow','ember'),
 'abjure_enemy':('abjure','ember'),'relentless_step':('step','ember'),'soul_of_vengeance':('vengeance','ember'),
 'smite_hit':('smite1','gold'),'smite_hit_2':('smite2','gold'),'smite_hit_3':('smite3','gold'),
 'aura_start':('oath_ring','azure'),'aura_refresh':('oath_echo','azure'),'radiant_blades':('radiant_edge','holy'),
 'vengeful_strikes':('avenger_edge','ember'),'consecrated_tick':('altar_mend','holy'),'elemental_strike':('flame_edge','fire')}
PASSIVES={'paladin.smite_damage':'fx.paladin_smite_hit.gold','paladin.smite_damage_2':'fx.paladin_smite_hit_2.gold',
 'paladin.smite_damage_3':'fx.paladin_smite_hit_3.gold','paladin.aura_of_protection_start':'fx.paladin_aura_start.azure',
 'paladin.aura_of_protection':'fx.paladin_aura_refresh.azure','crusader.radiant_blades':'fx.paladin_radiant_blades.holy',
 'avenger.vengeful_strikes':'fx.paladin_vengeful_strikes.ember','crusader.consecrated.tick':'fx.paladin_consecrated_tick.holy'}
SMALL=set(DESIGNS)-set(list(DESIGNS)[:24])
LARGE={'benediction','standards','challenge','citadel','consecrate','dawn','vengeance','oath_ring','oath_echo'}
def frames(name):
 from .fx import Canvas,SLOTS,_twinkle
 shape,material=DESIGNS[name];large=shape in LARGE;w,h=(208,144) if large else (80,80) if name in SMALL else (112,112);count=8 if name in SMALL else 12;out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);g=min(1,(i+1)/3);cx,cy=w/2,h*.66;r=(65 if large else 29)*g;shade=5 if i in (2,3) else max(1,round(4*(1-t)));core='flash' if i in (2,3) else 'core'
  def line(a,b,s=shade,slot='main'):
   # A coloured body supports the hot core; held geometry remains clustered pixels.
   if slot=='core' and s>=4:
    for dx,dy in ((-1,0),(1,0),(0,-1),(0,1)):c.line(a[0]+dx,a[1]+dy,b[0]+dx,b[1]+dy,'main',min(3,shade))
   c.line(*a,*b,core if slot=='core' else slot,s)
   if slot=='main' and s>=4:c.line(a[0]+1,a[1],b[0]+1,b[1],slot,max(1,s-2))
  def path(points,slot='main',s=shade,close=False):
   for a,b in zip(points,points[1:]+points[:1] if close else points[1:]):line(a,b,s,slot)
  def ring(x,y,rx,ry,n=16,a=0,slot='main',s=shade):path([(x+math.cos(a+j*math.tau/n)*rx,y+math.sin(a+j*math.tau/n)*ry) for j in range(n)],slot,s,True)
  def star(x,y,size=2):_twinkle(c,x,y,core,size,shade)
  def blade(x,y,size=24,slant=0):
   c.polygon([(x-3,y+5),(x-3+slant,y-size+5),(x+slant,y-size),(x+3+slant,y-size+5),(x+3,y+5)],'main',shade)
   line((x,y+3),(x+slant,y-size+4),5,'core');line((x-7,y+5),(x+7,y+5),4,'accent');line((x,y+5),(x,y+12),3,'accent')
  def shield(x,y,size=22,slot='main'):
   size*=.5+.5*g
   pts=[(x-size,y-size*.9),(x,y-size*1.15),(x+size,y-size*.9),(x+size*.8,y+size*.3),(x,y+size),(x-size*.8,y+size*.3)]
   if i in (2,3,4):c.polygon(pts,'main',2)
   path(pts,slot,shade,True);path([(x-size*.65,y-size*.6),(x,y-size*.8),(x+size*.65,y-size*.6),(x+size*.5,y+size*.15),(x,y+size*.65),(x-size*.5,y+size*.15)],'accent',3,True)
  if shape.startswith('charge'):
   tier=int(shape[-1]);ring(cx,cy,18+tier*5,(18+tier*5)*.4,12,slot='accent',s=2)
   for j in range(tier):blade(cx+(j-(tier-1)/2)*13,cy-8*g,18+tier*7,j-(tier-1)/2)
   for j in range(tier*3):a=j*math.tau/(tier*3)+t*.4;star(cx+math.cos(a)*(20+tier*4),cy+math.sin(a)*10,1)
  elif shape.startswith('smite') or shape in ('searing','vengeance','radiant_edge','avenger_edge','flame_edge'):
   tier=int(shape[-1]) if shape.startswith('smite') else {'searing':3,'vengeance':5,'radiant_edge':1,'avenger_edge':2,'flame_edge':2}[shape]
   for j in range(tier):
    x=cx+(j-(tier-1)/2)*(12 if large else 8);blade(x,cy-6*g,22+(12 if large else tier*3),(-1 if j%2 else 1)*8)
   ring(cx,cy,(45 if large else 13+tier*3)*g,(17 if large else 7+tier),12,a=t*.3,slot='accent',s=2)
   if shape in ('searing','flame_edge'):
    for j in range(7):x=cx+(j-3)*7;c.polygon([(x-3,cy),(x,cy-10-12*g-j%2*8),(x+4,cy)],'accent',shade)
   elif shape=='avenger_edge':path([(cx-18,cy-24),(cx,cy),(cx+18,cy-24)],'core',5)
   elif shape=='radiant_edge':star(cx,cy-15,7)
   elif shape=='vengeance':
    for j in range(6):a=j*math.tau/6;path([(cx+math.cos(a)*r,cy+math.sin(a)*r*.4),(cx+math.cos(a)*r*.65,cy+math.sin(a)*r*.25-10)],'core',5)
  elif shape in ('hands','mend','purify','altar_mend'):
   if shape=='hands':
    for side in (-1,1):path([(cx+side*20,cy+3),(cx+side*14,cy-14),(cx+side*7,cy-18),(cx+side*4,cy-8)],'main',4)
    star(cx,cy-14,6)
   elif shape=='mend':
    for k in range(3):ring(cx,cy-k*10,8+k*5,4+k*2,12,slot='accent',s=3)
    line((cx,cy-35),(cx,cy-8),5,'core');line((cx-9,cy-22),(cx+9,cy-22),5,'core')
   elif shape=='purify':
    for j in range(5):a=j*math.tau/5;path([(cx+math.cos(a)*r*.4,cy+math.sin(a)*r*.2-15),(cx+math.cos(a)*r,cy+math.sin(a)*r*.4-15-12*t)],'core',4)
    ring(cx,cy-15,9,11,8,slot='accent')
   else:
    path([(cx-12,cy),(cx-12,cy-5),(cx+12,cy-5),(cx+12,cy)],'accent',3);star(cx,cy-16-10*t,4)
   for j in range(3):star(cx+(j-1)*10,cy-9-j%2*8-10*t,1)
  elif shape in ('faith','aegis','intercept','citadel'):
   if shape=='citadel':
    ring(cx,cy,r,r*.35,24,slot='accent',s=2)
    for j in range(5):shield(cx+(j-2)*22,cy-14-abs(j-2)*-4,15)
    for j in range(4):star(cx+(j-1.5)*27,cy-51,2)
   else:
    shield(cx,cy-19,24 if shape=='faith' else 30 if shape=='aegis' else 22)
    if shape=='faith':path([(cx,cy-55),(cx,cy-8)],'core',4);line((cx-9,cy-35),(cx+9,cy-35),4,'core')
    elif shape=='aegis':ring(cx,cy,31,11,16,slot='accent',s=2);star(cx,cy-21,4)
    else:
     for k in range(3):path([(cx-34+k*8,cy-50),(cx-18+k*8,cy-32)],'accent',3)
     path([(cx+5,cy-43),(cx-6,cy-23),(cx+7,cy-4)],'core',5)
  elif shape in ('benediction','standards','challenge','consecrate','dawn','oath_ring','oath_echo'):
   n={'benediction':3,'standards':4,'challenge':6,'consecrate':8,'dawn':11,'oath_ring':5,'oath_echo':7}[shape]
   ring(cx,cy,r,r*.35,24,a=t*.2,slot='accent',s=2)
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r*.8;y=cy+math.sin(a)*r*.28
    if shape=='standards':line((x,y),(x,y-26*g),3,'accent');c.polygon([(x,y-26*g),(x+9,y-23*g),(x,y-15*g)],'main',shade)
    elif shape=='dawn':path([(x,y),(cx+(x-cx)*.55,cy-54*g)],'core',5);star(x,y-12,2)
    elif shape=='consecrate':path([(x-5,y),(x,y-8),(x+5,y),(x,y+8)],'main',shade,True)
    elif shape=='challenge':shield(x,y-7,7);line((x,y+3),(cx+(x-cx)*.4,cy+(y-cy)*.4),3,'core')
    elif shape=='oath_echo':path([(x-5,y),(x,y-7),(x+5,y)],'main',shade)
    else:star(x,y-15*g,4 if shape=='benediction' else 2)
   if shape=='dawn':blade(cx,cy-6,64);ring(cx,cy-40,27,27,24,slot='core',s=4)
   elif shape=='consecrate':line((cx-14,cy-17),(cx+14,cy-17),4,'core');line((cx,cy-34),(cx,cy+2),4,'core')
   elif shape=='oath_ring':shield(cx,cy-15,11)
  elif shape in ('edict','duel','vow','abjure','sever','step','fire_blade'):
   if shape=='edict':
    for k in range(3):line((cx-13+k*12,cy-37),(cx-13+k*12,cy-5),4,'core')
    line((cx-23,cy-22),(cx+23,cy-22),5,'main');ring(cx,cy,23,8,12,slot='accent',s=2)
   elif shape=='duel':
    for side in (-1,1):path([(cx+side*25,cy-33),(cx+side*12,cy-33),(cx+side*9,cy-10),(cx+side*22,cy-10)],'core',4)
    blade(cx,cy-8,23)
   elif shape=='vow':
    for side in (-1,1):path([(cx+side*24,cy-39),(cx+side*24,cy-4),(cx+side*13,cy+2)],'main',4)
    path([(cx,cy-43),(cx+9,cy-24),(cx,cy-9),(cx-9,cy-24)],'core',5,True)
   elif shape=='abjure':
    for side in (-1,1):path([(cx+side*27,cy-34),(cx+side*8,cy-20),(cx+side*27,cy-6)],'core',5)
    ring(cx,cy-20,13,18,6,a=t*.5,slot='accent')
   elif shape=='sever':
    for k in range(4):a=k*math.pi/2+t;path([(cx+math.cos(a)*13,cy+math.sin(a)*13-18),(cx+math.cos(a)*r,cy+math.sin(a)*r-18)],'core',4)
    path([(cx-19,cy-34),(cx+19,cy-2)],'main',5)
   elif shape=='step':
    for k in range(4):x=cx+(k-1.5)*12;path([(x-7,cy-8-k*4),(x+2,cy-17-k*4),(x+7,cy-8-k*4)],'main',max(1,shade-k%2))
    ring(cx,cy,26,8,12,slot='accent',s=2)
   else:
    blade(cx,cy-8,34)
    for k in range(7):y=cy-8-k*5;line((cx-5,y),(cx-11+(k%2)*4,y-9-6*g),4,'accent');line((cx+5,y),(cx+12-(k%2)*4,y-10),3,'main')
  else:raise ValueError(shape)
  # Sparse outward sparks and short trails give release/decay without filtered particles.
  for j in range(4 if name in SMALL else 6):
   a=j*math.tau/(4 if name in SMALL else 6)+.27
   distance=(13 if name in SMALL else 24 if not large else 45)*(0.45+t*.7)
   x=cx+math.cos(a)*distance;y=cy-12+math.sin(a)*distance*.45-8*t
   if i>=2:
    c.line(x-math.cos(a)*4,y-math.sin(a)*2,x,y,'accent',max(1,shade-1))
    _twinkle(c,x,y,'core',1 if j%2 else 2,max(1,shade-1))
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(shape,material) in DESIGNS.items():
  first=frames(name)[0];w,h=first.w,first.h
  out.append(Fx('paladin_'+name,name.replace('_',' ').title(),'Paladin',lambda n=name:frames(n),
   {material:_v(main=PALETTES[material],core='fx/flash',accent='fx/gold' if material in ('holy','gold','fire','ember') else PALETTES[material])},
   20,(w//2,round(h*.66)),'feet'))
 return out
