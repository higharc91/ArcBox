"""Ranger: fletched steel, living snares, storm arrows and the bond of the pack."""
import math
from .wizard_fx import PALETTES as SHARED
PALETTES=dict(SHARED,root='magic/nature',wood='wood/oak',steel='fx/steel',healing='magic/nature',gold='fx/gold')
DESIGNS={
 'hunters_mark':('antler_sight','force'),'vanish':('leaf_shadow','mist'),'cure_wounds':('fern_mend','healing'),
 'ensnaring_strike':('thorn_knot','root'),'entangle':('root_lattice','root'),'hail_of_thorns':('barb_burst','wood'),
 'spike_growth':('spike_bed','root'),'barkskin':('bark_scales','wood'),'lesser_restoration':('clear_dew','healing'),
 'lightning_arrow':('storm_splinter','storm'),'conjure_barrage':('force_fan','force'),
 'deadeye':('precision','steel'),'piercing_shot':('piercing_lane','steel'),'volley':('arrow_rain','steel'),
 'sic':('hunt_fangs','gold'),'pack_bond':('pack_link','gold'),'alpha_howl':('pack_howl','gold'),
 'vine_snare':('vine_hook','root'),'natures_veil':('leaf_mantle','root'),'guardian_tree':('hollow_canopy','root'),
 'hunters_mark_bonus':('force_puncture','force'),'high_ground_mastery':('falling_quill','steel'),
 'companion':('paw_wake','gold'),'ferocious_bond':('pack_claws','steel'),'spike_growth_step':('thorn_step','root'),'ensnared_thorns':('thorn_clasp','root')}
PASSIVES={'ranger.hunters_mark_bonus':'fx.ranger_hunters_mark_bonus.force',
 'sharpshooter.high_ground_mastery':'fx.ranger_high_ground_mastery.steel','beastmaster.companion':'fx.ranger_companion.gold',
 'beastmaster.ferocious_bond':'fx.ranger_ferocious_bond.steel','spike_growth.thorns':'fx.ranger_spike_growth_step.root','ensnared.thorns':'fx.ranger_ensnared_thorns.root'}
SHOTS={'ensnaring_strike','hail_of_thorns','lightning_arrow','deadeye','volley'}
SMALL={'hunters_mark_bonus','high_ground_mastery','ferocious_bond','spike_growth_step','ensnared_thorns'}
LARGE={'root_lattice','spike_bed','arrow_rain','pack_howl','hollow_canopy'}
AIMED={'force_fan','piercing_lane','vine_hook'}
DARK={'root','wood','steel','mist'}
def dimensions(name,shot=False):
 shape,_=DESIGNS[name]
 return (64,32) if shot else (240,112) if shape in AIMED else (208,160) if shape in LARGE else (80,80) if name in SMALL else (112,112)
def frames(name,shot=False):
 from .fx import Canvas,SLOTS,_twinkle
 shape,material=DESIGNS[name];w,h=dimensions(name,shot);count=8 if shot or name in SMALL else 12;out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);g=min(1,(i+1)/3);shade=max(1,round(4*(1-t))) if i not in (2,3) else 5;cx=w/2;cy=h*.72;r=(76 if shape in LARGE else 23 if name in SMALL else 34)*g
  core='accent' if material in DARK else 'flash' if i==2 else 'core'
  def line(a,b,slot='main',s=None):c.line(*a,*b,core if slot=='core' else slot,shade if s is None else s)
  def path(points,slot='main',s=None,closed=False):
   for a,b in zip(points,points[1:]+points[:1] if closed else points[1:]):line(a,b,slot,s)
  def ring(x,y,rx,ry,n=20,slot='main',s=None):path([(x+math.cos(j*math.tau/n)*rx,y+math.sin(j*math.tau/n)*ry) for j in range(n)],slot,s,True)
  def leaf(x,y,size=7,side=1):
   c.polygon([(x,y),(x+side*size,y-size),(x+side*size*1.4,y-size*.2),(x,y)],'main',max(1,shade-1));line((x,y),(x+side*size,y-size*.5),'accent',min(4,shade))
  def arrow(x,y,length=23,angle=0,slot='main'):
   dx=math.cos(angle);dy=math.sin(angle);nx=-dy;ny=dx
   line((x-dx*length,y-dy*length),(x,y),slot)
   path([(x-dx*6+nx*4,y-dy*6+ny*4),(x,y),(x-dx*6-nx*4,y-dy*6-ny*4)],'core',min(4,shade))
   for side in (-1,1):line((x-dx*(length-6),y-dy*(length-6)),(x-dx*length+nx*side*4,y-dy*length+ny*side*4),'accent',max(1,shade-1))
  def paw(x,y,size=9):
   c.polygon([(x-size*.7,y+size*.4),(x,y-size*.35),(x+size*.7,y+size*.4),(x+size*.4,y+size*.8),(x-size*.4,y+size*.8)],'main',max(1,shade-1))
   for j in range(4):c.ellipse(x+(j-1.5)*size*.58,y-size*(.65+(.2 if j in (1,2) else 0)),size*.2,size*.3,'core',min(4,shade))
  if shot:
   x=48;y=16;arrow(x,y,34,0)
   if name=='lightning_arrow':
    for j in range(2):path([(7,13+j*7),(20,10+j*7),(27,17+j*7),(38,12+j*7),(52,16)],'core',4)
   elif name in ('ensnaring_strike','hail_of_thorns'):
    for j in range(3):leaf(19+j*8,y,4,(-1)**j)
   elif name=='deadeye':line((5,16),(42,16),'accent',2)
   else:
    for side in (-1,1):arrow(42,16+side*9,21,side*.1)
  elif shape=='antler_sight':
   ring(cx,cy-21,16*g,20*g,12,slot='core',s=4)
   for side in (-1,1):
    path([(cx+side*9,cy-8),(cx+side*23,cy-27),(cx+side*22,cy-43)],'main',shade)
    line((cx+side*22,cy-32),(cx+side*30,cy-37),'accent',3)
   path([(cx,cy-33),(cx-5,cy-22),(cx,cy-11),(cx+5,cy-22)],'core',4,True)
  elif shape=='leaf_shadow':
   for j in range(9):
    a=j*math.tau/9;leaf(cx+math.cos(a)*r*(.4+t),cy-20+math.sin(a)*r*.65-12*t,6,(-1)**j)
   for side in (-1,1):path([(cx+side*12,cy-45),(cx+side*21,cy-19),(cx+side*9,cy)],'accent',max(1,shade-1))
  elif shape=='fern_mend':
   for side in (-1,1):
    path([(cx,cy),(cx+side*12,cy-18),(cx+side*9,cy-37)],'core',4)
    for j in range(4):leaf(cx+side*(j*2+3),cy-j*8-3,8,side)
   ring(cx,cy,19,6,16,slot='accent',s=2)
  elif shape in ('thorn_knot','root_lattice','spike_bed','thorn_step'):
   n=5 if shape=='thorn_knot' else 12 if shape=='root_lattice' else 18 if shape=='spike_bed' else 4
   ring(cx,cy,r,r*.32,20,slot='accent',s=2)
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r*(.6 if shape=='thorn_knot' else .9);y=cy+math.sin(a)*r*.32
    if shape in ('spike_bed','thorn_step'):
     c.polygon([(x-5,y),(x+1,y-(16+j%3*6)*g),(x+5,y)],'main',shade);line((x+1,y-12*g),(x+3,y),'accent',min(4,shade))
    else:
     path([(x,y),(x-6*math.cos(a),y-15*g),(cx+(x-cx)*.55,cy-(27+j%3*8)*g)],'main',shade)
     leaf(x-3, y-13*g,7,(-1)**j)
  elif shape=='barb_burst':
   for j in range(9):
    a=j*math.tau/9;x=cx+math.cos(a)*r*(.25+t);y=cy-20+math.sin(a)*r*.65*(.25+t)
    c.polygon([(x,y),(x-10*math.cos(a)+3,y-10*math.sin(a)),(x-10*math.cos(a)-3,y-10*math.sin(a))],'main',shade)
  elif shape=='bark_scales':
   for j in range(7):
    a=j*math.tau/7;x=cx+math.cos(a)*22;y=cy-24+math.sin(a)*22
    pts=[(x-7,y-10),(x+5,y-12),(x+9,y+4),(x-4,y+10)];c.polygon(pts,'main',max(1,shade-1));path(pts,'accent',min(4,shade),True)
  elif shape=='clear_dew':
   for j in range(5):
    a=j*math.tau/5;x=cx+math.cos(a)*r*.65;y=cy-22+math.sin(a)*r*.6-12*t
    path([(x,y-7),(x+4,y),(x,y+5),(x-4,y)],'core',4,True)
   leaf(cx-2,cy-8,16,-1);leaf(cx+2,cy-8,16,1)
  elif shape=='storm_splinter':
   for j in range(5):
    a=j*math.tau/5;x=cx+math.cos(a)*r;y=cy-19+math.sin(a)*r*.7
    path([(cx,cy-20),(x*.4+cx*.6+3,y*.4+(cy-20)*.6),(x-5,y-8),(x+4,y-5),(x,y)],'core',4)
  elif shape in ('force_fan','piercing_lane','vine_hook'):
   origin=(8,h/2)
   if shape=='force_fan':
    for j in range(9):
     a=(j-4)*.14;reach=(w-30)*g*(.45+t*.55);x=8+math.cos(a)*reach;y=h/2+math.sin(a)*reach*.6
     line((8,h/2),(x-28*math.cos(a),y-28*math.sin(a)),'accent',max(1,shade-3));arrow(x,y,30+j%3*4,a,'core')
   elif shape=='piercing_lane':
    for off in (-5,0,5):line((8,h/2+off),(w-12,h/2+off),'accent' if off else 'main',min(shade,4))
    arrow((w-15)*g,h/2,72,0)
    for j in range(4):path([(34+j*48,h/2-14),(43+j*48,h/2),(34+j*48,h/2+14)],'core',min(shade,4))
   else:
    path([(8,h/2),(50,h/2-12*g),(88,h/2+13*g),(132,h/2-10*g),(183,h/2+6*g),(223,h/2)],'main',shade)
    for j in range(8):leaf(25+j*26,h/2+math.sin(j*2)*10*g,8,(-1)**j)
    path([(w-28,h/2-18),(w-8,h/2-14),(w-7,h/2+9),(w-27,h/2+16)],'accent',shade)
  elif shape=='precision':
   ring(cx,cy-23,25*g,25*g,24,slot='main',s=shade)
   for side in (-1,1):
    line((cx+side*12,cy-23),(cx+side*35,cy-23),'accent',min(4,shade));line((cx,cy-23+side*12),(cx,cy-23+side*35),'accent',min(4,shade))
   arrow(cx+11,cy-23,22,0)
  elif shape=='arrow_rain':
   for j in range(9):
    x=cx+(j-4)*15;y=cy-8-(j%3)*10+(t-.5)*22;arrow(x,y,35,-1.05)
   ring(cx,cy,r,r*.3,24,slot='accent',s=2)
  elif shape=='hunt_fangs':
   for side in (-1,1):path([(cx+side*23,cy-43),(cx+side*18,cy-14),(cx+side*6,cy-5),(cx+side*11,cy-25)],'core',4)
   path([(cx-10,cy-43),(cx,cy-34),(cx+10,cy-43)],'main',shade)
  elif shape=='pack_link':
   for j in range(3):
    a=j*math.tau/3;x=cx+math.cos(a)*r*.7;y=cy-18+math.sin(a)*r*.5;paw(x,y,6)
    line((x,y),(cx,cy-18),'accent',2)
  elif shape=='pack_howl':
   for j in range(3):ring(cx,cy-j*13,r*(.65+t*.25),r*.28,24,slot='core' if j==1 else 'main',s=max(1,shade-j))
   for side in (-1,1):path([(cx+side*21,cy-36),(cx+side*17,cy-58),(cx+side*8,cy-49),(cx,cy-54)],'core',4)
  elif shape in ('leaf_mantle','hollow_canopy'):
   n=6 if shape=='leaf_mantle' else 12
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r;y=cy-35+math.sin(a)*r*.32
    path([(cx+math.cos(a)*r*.6,cy),(x,y-13*g),(cx+math.cos(a)*r*.65,cy-57*g)],'main',shade)
    leaf(x,y-13*g,9,(-1)**j)
   if shape=='hollow_canopy':
    for side in (-1,1):path([(cx+side*52,cy),(cx+side*65,cy-34),(cx+side*48,cy-70),(cx+side*15,cy-87)],'accent',min(shade,4))
    ring(cx,cy,r,r*.32,24,slot='main',s=2)
  elif shape=='thorn_clasp':
   for side in (-1,1):
    x=cx+side*(23-11*t);path([(x,cy-40),(x-side*5,cy-24),(x,cy-7)],'main',shade)
    for j in range(3):path([(x,cy-33+j*9),(x-side*8,cy-29+j*9),(x,cy-26+j*9)],'accent',min(4,shade))
  elif shape=='force_puncture':
   for side in (-1,1):path([(cx+side*22*g,cy-31),(cx+side*7,cy-20),(cx+side*22*g,cy-9)],'core',min(4,shade))
   _twinkle(c,cx,cy-20,core,4,min(5,shade))
  elif shape=='falling_quill':
   for j in range(3):arrow(cx-12+j*10,cy-8-j*5,29,1.1)
  elif shape=='paw_wake':
   paw(cx,cy-20,15)
   for j in range(2):ring(cx,cy-j*9,26+j*9,7+j*3,20,slot='accent',s=2)
  elif shape=='pack_claws':
   for j in range(3):path([(cx-20+j*13,cy-38),(cx-23+j*13,cy-20),(cx-15+j*13,cy-4)],'main',shade)
  else:raise ValueError(shape)
  if not shot:
   for j in range(4):
    a=j*math.tau/4+.5;x=cx+math.cos(a)*r*(.5+t*.6);y=cy-16+math.sin(a)*r*.3-8*t
    if i>=2:_twinkle(c,x,y,'accent',1,max(1,shade-1))
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(shape,material) in DESIGNS.items():
  for shot in (False,True) if name in SHOTS else (False,):
   w,h=dimensions(name,shot);anchor=(w//2,h//2) if shot else (8,h//2) if shape in AIMED else (w//2,round(h*.72))
   palette=_v(main=PALETTES[material],core='fx/flash' if material not in DARK else 'fx/steel',accent=PALETTES[material])
   out.append(Fx('ranger_'+name+('_shot' if shot else ''),name.replace('_',' ').title()+(' flight' if shot else ''),'Ranger',lambda n=name,b=shot:frames(n,b),
    {material:palette},20 if not shot else 24,anchor,'projectile' if shot else 'body' if shape in AIMED else 'feet',blend='normal' if material in DARK else 'add',loop=shot))
 return out
