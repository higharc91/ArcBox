"""Fighter: restrained steel cuts, shields and command standards; no magical emission."""
import math
PALETTES={'steel':'fx/steel','gold':'fx/gold','royal':'cloth/royal_purple','warm':'leather/brown'}
DESIGNS={
 'second_wind':('breath','warm'),'action_surge':('burst_step','gold'),
 'trip_attack':('low_hook','steel'),'pushing_attack':('shield_shove','steel'),
 'menacing_attack':('tall_fangs','steel'),'precision_attack':('needle','steel'),
 'goading_attack':('knock','gold'),'rally':('standard','gold'),'feinting_attack':('false_edge','steel'),
 'charge':('ram','steel'),'challenge':('gauntlet','gold'),'shield_wall':('bastions','steel'),
 'mass_challenge':('line_flags','gold'),'cleave':('halfmoon','steel'),
 'blade_dance':('cross_cuts','steel'),'execution_strike':('split_edge','steel'),
 'commanders_strike':('sword_banner','royal'),'maneuvering_order':('chevron_steps','royal'),
 'rallying_cry':('crest_fans','royal'),'to_arms':('war_standards','royal'),
 'studied_attacks':('exposed','steel'),'indomitable':('break_chain','steel'),
 'hold_the_line':('ankle_brace','steel'),'unbreakable':('rising_shield','gold'),
 'keen_edge':('edge_hone','steel'),'relentless':('momentum','gold'),
 'inspiring_presence':('lone_pennant','royal'),'weapon_hit':('basic_cut','steel')}
PASSIVES={p:f'fx.fighter_{n}.{DESIGNS[n][1]}' for p,n in (
 ('fighter.studied_attacks','studied_attacks'),('fighter.indomitable','indomitable'),
 ('vanguard.hold_the_line','hold_the_line'),('vanguard.unbreakable','unbreakable'),
 ('weaponmaster.keen_edge','keen_edge'),('weaponmaster.relentless','relentless'),
 ('warlord.inspiring_presence','inspiring_presence'))}
SMALL={'studied_attacks','hold_the_line','keen_edge','relentless','weapon_hit'}
LARGE={'bastions','line_flags','halfmoon','cross_cuts','war_standards','crest_fans'}
def dimensions(name):return (208,144) if DESIGNS[name][0] in LARGE else (80,80) if name in SMALL else (112,112)
def frames(name):
 from .fx import Canvas,SLOTS
 shape,material=DESIGNS[name];w,h=dimensions(name);count=8 if name in SMALL else 12;out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);g=min(1,(i+1)/3);shade=max(1,round(4*(1-t)));cx=w/2;cy=h*.72;r=(73 if shape in LARGE else 24 if name in SMALL else 35)*g
  def line(a,b,slot='main',s=None):c.line(*a,*b,slot,shade if s is None else s)
  def path(points,slot='main',s=None,closed=False):
   for a,b in zip(points,points[1:]+points[:1] if closed else points[1:]):line(a,b,slot,s)
  def arc(x,y,rx,ry,start,end,slot='main',s=None):path([(x+math.cos(a)*rx,y+math.sin(a)*ry) for a in [start+(end-start)*j/18 for j in range(19)]],slot,s)
  def shield(x,y,size=17):
   p=[(x-size,y-size),(x,y-size*1.3),(x+size,y-size),(x+size*.8,y+size*.3),(x,y+size),(x-size*.8,y+size*.3)]
   # Solid impact lasts only two frames; the lingering outline leaves the unit readable.
   if i in (2,3):c.polygon(p,'main',max(1,shade-1))
   path(p,'core',min(4,shade),True);line((x,y-size),(x,y+size*.6),'accent',min(4,shade))
  def blade(x,y,length=38,angle=-1.1):
   dx=math.cos(angle);dy=math.sin(angle);nx=-dy;ny=dx
   p=[(x-nx*3,y-ny*3),(x+dx*length,y+dy*length),(x+nx*3,y+ny*3)]
   c.polygon(p,'core',min(4,shade));line((x-nx*7,y-ny*7),(x+nx*7,y+ny*7),'accent',min(4,shade))
   line((x,y),(x-dx*8,y-dy*8),'main',shade)
  def pennant(x,y,height=39,side=1):
   line((x,y),(x,y-height),'core',min(4,shade));p=[(x,y-height),(x+side*(18+2*math.sin(i)),y-height+4),(x+side*13,y-height+15),(x,y-height+12)]
   c.polygon(p,'main',max(1,shade-1));path(p,'accent',min(4,shade),True)
  def slash(x,y,rx,ry,a,b):
   arc(x,y,rx,ry,a,b,'main',shade);arc(x,y-2,rx*.92,ry*.9,a+.06,b-.06,'core',min(4,shade))
  if shape=='breath':
   for side in (-1,1):arc(cx,cy-20,18+9*t,24+3*t,(-math.pi/2 if side==1 else math.pi/2),(math.pi/2 if side==1 else math.pi*1.5),'accent',shade)
   for j in range(3):path([(cx-6+j*6,cy-9),(cx-9+j*6,cy-18-10*t),(cx-5+j*6,cy-28-12*t)],'main',shade)
  elif shape=='burst_step':
   for j in range(3):path([(cx-32-j*4,cy-j*9),(cx-12,cy-7-j*9),(cx-21,cy-16-j*9)],'accent',shade)
   for side in (-1,1):path([(cx+side*7,cy-6),(cx+side*27*g,cy-22),(cx+side*16,cy-37)],'core',min(4,shade))
  elif shape in ('low_hook','halfmoon','basic_cut','false_edge'):
   a=-math.pi*.9+t*.4;b=math.pi*.1+t*.7
   slash(cx,cy-(5 if shape=='low_hook' else 20),r,(10 if shape=='low_hook' else r*.58),a,b)
   if shape=='low_hook':path([(cx+23,cy-3),(cx+29,cy-12),(cx+20,cy-15)],'core',min(4,shade))
   elif shape=='halfmoon':slash(cx,cy-24,r*.85,r*.48,a-.2,b-.2)
   elif shape=='false_edge':slash(cx-9,cy-31,20,15,math.pi*.2,math.pi*1.2);path([(cx+9,cy-28),(cx+27,cy-17),(cx+12,cy-6)],'accent',shade)
  elif shape in ('shield_shove','ram'):
   shield(cx+9*t,cy-23,23 if shape=='ram' else 17)
   for j in range(3):line((cx-32,cy-12-j*10),(cx-16+9*t,cy-12-j*10),'accent',shade)
   path([(cx+24,cy-37),(cx+34,cy-24),(cx+24,cy-11)],'core',min(4,shade))
  elif shape=='tall_fangs':
   for side in (-1,1):path([(cx+side*29,cy-47),(cx+side*19,cy-19),(cx+side*7,cy-8),(cx+side*12,cy-34)],'core',min(4,shade))
   blade(cx,cy-6,42,-math.pi/2)
  elif shape=='needle':
   blade(cx-24,cy-20,54,0)
   for side in (-1,1):path([(cx+side*14,cy-35),(cx+side*20,cy-20),(cx+side*14,cy-5)],'accent',shade)
  elif shape=='knock':
   shield(cx,cy-22,17)
   for j in range(5):
    a=-math.pi*.7+j*.5;line((cx+math.cos(a)*23,cy-22+math.sin(a)*23),(cx+math.cos(a)*34,cy-22+math.sin(a)*34),'accent',shade)
  elif shape in ('standard','lone_pennant','sword_banner'):
   pennant(cx-9,cy-3,48 if shape=='standard' else 39,1)
   if shape=='sword_banner':blade(cx+11,cy-5,43,-math.pi/2)
   elif shape=='standard':shield(cx+12,cy-12,11)
  elif shape=='gauntlet':
   path([(cx-13,cy-5),(cx-19,cy-25),(cx-13,cy-33),(cx-6,cy-23),(cx-6,cy-44),(cx,cy-47),(cx+6,cy-42),(cx+12,cy-33),(cx+14,cy-16),(cx+7,cy-5)],'core',min(4,shade),True)
   for j in range(3):line((cx-3+j*6,cy-37),(cx-3+j*6,cy-23),'accent',shade)
  elif shape=='bastions':
   for j in range(5):shield(cx+(j-2)*30,cy-13-(2-abs(j-2))*8,18)
   arc(cx,cy,r,12,0,math.pi,'accent',shade)
  elif shape in ('line_flags','war_standards'):
   for j in range(5 if shape=='war_standards' else 3):
    n=5 if shape=='war_standards' else 3;x=cx+(j-(n-1)/2)*31;pennant(x,cy-3,46+(j%2)*15,(-1)**j)
   if shape=='war_standards':shield(cx,cy-20,19)
   else:blade(cx,cy-1,34,-math.pi/2)
  elif shape=='cross_cuts':
   for side in (-1,1):slash(cx+side*9,cy-26,r*.82,r*.55,-math.pi*.95+side*t*.6,math.pi*.03+side*t*.6)
   blade(cx-12,cy-6,57,-.9);blade(cx+12,cy-6,57,-2.24)
  elif shape=='split_edge':
   for side in (-1,1):path([(cx+side*26,cy-49),(cx+side*8,cy-28),(cx+side*24,cy-10)],'accent',shade)
   blade(cx,cy-3,53,-math.pi/2);line((cx-28,cy-9),(cx+28,cy-9),'core',min(4,shade))
  elif shape=='chevron_steps':
   for j in range(3):path([(cx-17+j*8,cy-8-j*12),(cx+j*8,cy-17-j*12),(cx-17+j*8,cy-26-j*12)],'main',shade)
   line((cx-29,cy),(cx+25,cy),'accent',shade)
  elif shape=='crest_fans':
   for j in range(5):
    a=-2.5+j*.46;blade(cx+math.cos(a)*19,cy+math.sin(a)*12,31,a)
   shield(cx,cy-16,20)
  elif shape=='exposed':
   for side in (-1,1):path([(cx+side*22,cy-35),(cx+side*11,cy-24),(cx+side*22,cy-13)],'main',shade)
   line((cx-4,cy-28),(cx+4,cy-20),'core',min(4,shade));line((cx+4,cy-28),(cx-4,cy-20),'core',min(4,shade))
  elif shape=='break_chain':
   for side in (-1,1):
    for j in range(3):
     x=cx+side*(12+8*j+12*t);y=cy-21+(j-1)*6;arc(x,y,7,4,0,math.tau,'main',shade)
    line((cx+side*3,cy-34),(cx+side*9,cy-42),'accent',shade)
   shield(cx,cy-24,14)
  elif shape=='ankle_brace':
   for side in (-1,1):path([(cx+side*27,cy),(cx+side*20,cy-13),(cx+side*10,cy-13),(cx+side*5,cy)],'core',min(4,shade))
   line((cx-28,cy+2),(cx+28,cy+2),'accent',shade)
  elif shape=='rising_shield':
   shield(cx,cy-19-15*g,24)
   for side in (-1,1):path([(cx+side*30,cy),(cx+side*30,cy-26),(cx+side*23,cy-18)],'accent',shade)
  elif shape=='edge_hone':
   blade(cx-13,cy-2,41,-.8)
   for j in range(3):line((cx-10+j*10,cy-28-j*2),(cx-3+j*10,cy-36-j*2),'accent',shade)
  elif shape=='momentum':
   for j in range(3):path([(cx-26+j*12,cy-5),(cx-13+j*12,cy-17),(cx-26+j*12,cy-29)],'main',shade)
   arc(cx,cy,26,7,0,math.pi,'accent',shade)
  else:raise ValueError(shape)
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(_,material) in DESIGNS.items():
  w,h=dimensions(name)
  out.append(Fx('fighter_'+name,name.replace('_',' ').title(),'Fighter',lambda n=name:frames(n),
   {material:_v(main=PALETTES[material],core='fx/steel',accent='fx/gold' if material in ('gold','royal','warm') else 'fx/steel')},
   20,(w//2,round(h*.72)),'feet',blend='normal'))
 return out
