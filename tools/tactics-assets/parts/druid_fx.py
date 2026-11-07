"""Druid spell identities: rooted plants, animal spirits, weather and living restoration."""
import math
from .wizard_fx import PALETTES as SHARED
PALETTES=dict(SHARED,nature='magic/nature',healing='magic/nature',bark='fx/wood',moon='magic/frost',steel='fx/steel')
DESIGNS={
 'wild_shape_wolf':('wolf_spirit','nature'),'wild_shape_bear':('bear_spirit','nature'),
 'wild_shape_dire_wolf':('dire_spirit','moon'),'thorn_whip':('whip','nature'),
 'produce_flame':('seed_flame','fire'),'shillelagh':('staff','nature'),
 'entangle':('snare','nature'),'faerie_fire':('fireflies','fey'),'thunderwave':('shockwave','thunder'),
 'cure_wounds':('sprout','healing'),'healing_word':('leaf_word','healing'),'fog_cloud':('fog','mist'),
 'hold_person':('vine_cage','nature'),'spike_growth':('thorns','nature'),'barkskin':('bark_plates','earth'),
 'moonbeam':('moon_pillar','moon'),'lesser_restoration':('dew','healing'),
 'call_lightning':('fork','storm'),'sleet_storm':('sleet','frost'),'dispel_magic':('unravel','arcane'),
 'conjure_animals':('pack','nature'),'ice_storm':('hail','frost'),'blight':('rot','grave'),
 'mass_cure_wounds':('meadow','healing'),'greater_restoration':('spring','healing'),
 'dire_form':('combat_dire','nature'),'elder_bear_form':('elder_spirit','nature'),'primal_mend':('heartwood','healing'),
 'gale_burst':('gale','thunder'),'chain_tempest':('branches','storm'),'wrath_of_the_tempest':('tempest','storm'),
 'bloom':('blossom','healing'),'sanctuary_grove':('grove','healing'),'ancient_canopy':('canopy','healing'),
 'companion':('sapling','nature'),'spike_growth_step':('thorn_step','nature'),
 'moonbeam_enter':('moon_crescent','moon'),'moonbeam_turn':('moon_tide','moon'),
 'sleet_storm_turn':('ice_slip','frost'),'sanctuary_grove_mend':('new_leaf','healing'),
 'chain_tempest_bounce':('fork_hit','storm'),'form_wolf_bite':('fangs','steel'),
 'form_dire_wolf_bite':('dire_fangs','steel'),'form_bear_claw':('claws','steel'),'form_elder_bear_claw':('elder_claws','steel')}
PASSIVES={'grovekeeper.companion':'fx.druid_companion.nature',
 'druid.spike_growth.step':'fx.druid_spike_growth_step.nature','druid.moonbeam.enter':'fx.druid_moonbeam_enter.moon',
 'druid.moonbeam.turn':'fx.druid_moonbeam_turn.moon','druid.sleet_storm.turn':'fx.druid_sleet_storm_turn.frost',
 'grovekeeper.sanctuary_grove.mend':'fx.druid_sanctuary_grove_mend.healing'}
SMALL={'produce_flame','shillelagh','companion','spike_growth_step','moonbeam_enter','moonbeam_turn','sleet_storm_turn','sanctuary_grove_mend','chain_tempest_bounce','form_wolf_bite','form_dire_wolf_bite','form_bear_claw','form_elder_bear_claw'}
LARGE={'snare','fireflies','shockwave','fog','thorns','moon_pillar','fork','sleet','pack','hail','rot','meadow','spring','elder_spirit','gale','branches','tempest','grove','canopy'}
DARK={'whip','staff','snare','fog','vine_cage','thorns','bark_plates','thorn_step','fangs','dire_fangs','claws','elder_claws'}
def dimensions(name,shot=False):return (48,32) if shot else (160,72) if name=='thorn_whip' else (208,144) if DESIGNS[name][0] in LARGE else (72,72) if name in SMALL else (112,112)
def frames(name,shot=False):
 from .fx import Canvas,SLOTS,_twinkle
 shape,material=DESIGNS[name];w,h=dimensions(name,shot);count=8 if name in SMALL or name=='thorn_whip' or shot else 12;out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);g=min(1,(i+1)/3);cx,cy=w/2,h*.66;r=(70 if shape in LARGE else 18 if name in SMALL else 37)*g;shade=max(1,round(5*(1-t*.85)))
  def line(a,b,s=shade,slot='main'):
   c.line(*a,*b,'flash' if slot=='core' and i in (2,3) else slot,5 if slot=='core' and i in (2,3) else min(s,shade))
  def ring(x,y,rx,ry,n=16,a=0,slot='main',s=shade):
   pts=[(x+math.cos(a+j*math.tau/n)*rx,y+math.sin(a+j*math.tau/n)*ry) for j in range(n)]
   for u,v in zip(pts,pts[1:]+pts[:1]):line(u,v,s,slot)
  def star(x,y,size=2):_twinkle(c,x,y,'flash' if i==2 else 'core',size,shade)
  def leaf(x,y,angle,size=6):
   dx,dy=math.cos(angle)*size,math.sin(angle)*size
   c.polygon([(x-dx*.5,y-dy*.5),(x-dy*.5,y+dx*.5),(x+dx,y+dy),(x+dy*.5,y-dx*.5)],'main',shade)
   line((x-dx*.35,y-dy*.35),(x+dx*.75,y+dy*.75),5,'core')
  if shot:
   for j in range(3):line((5+j*3,16+(j-1)*5),(30,16),3,'accent')
   c.polygon([(25,10),(32,4+3*t),(43,16),(31,24)],'main',shade);star(34,16,3)
  elif shape=='whip':
   cy=h/2
   pts=[(8,cy)]+[(x*g,cy+math.sin(x*.06+t*4)*9*math.sin(x*math.pi/160)) for x in range(20,158,7)]
   for a,b in zip(pts,pts[1:]):line(a,b,4)
   for j,(x,y) in enumerate(pts[2:]):
    if j%2==0:c.polygon([(x-2,y),(x+4,y-5),(x+3,y+1)],'accent',shade)
   ring(148*g,cy,7,9,6,slot='accent')
  elif shape in ('wolf_spirit','bear_spirit','dire_spirit','combat_dire','elder_spirit'):
   n={'wolf_spirit':4,'bear_spirit':5,'dire_spirit':6,'combat_dire':7,'elder_spirit':9}[shape]
   ring(cx,cy,r,r*.4,n*2,a=t*.3,slot='accent',s=2)
   for j in range(n):
    a=j*math.tau/n+t*1.4;leaf(cx+math.cos(a)*r,cy+math.sin(a)*r*.4-i,a+math.pi/2,6 if n<7 else 9)
   if shape in ('wolf_spirit','dire_spirit','combat_dire'):
    # Angular muzzle, swept ears and a coiling mane; stronger forms widen the crest.
    size=18 if shape=='wolf_spirit' else 26
    pts=[(cx-size,cy-10),(cx-size*.7,cy-size*1.8),(cx-4,cy-size*1.35),(cx+size*.6,cy-size*1.6),(cx+size*.9,cy-size*.5),(cx+size*.4,cy+2),(cx,cy+9)]
    for a,b in zip(pts,pts[1:]+pts[:1]):line(a,b,5,'core')
    if shape=='combat_dire':
     for j in (-1,0,1):line((cx-18+j*7,cy+5),(cx+8+j*7,cy-25),5,'accent')
    elif shape=='dire_spirit':ring(cx,cy-20,13,17,10,a=t,slot='core',s=4)
   else:
    ring(cx,cy-17,18 if shape=='bear_spirit' else 28,16,10,slot='core',s=5)
    for side in (-1,1):ring(cx+side*18,cy-31,6,6,8,slot='core',s=5)
    if shape=='elder_spirit':
     for j in range(7):
      x=cx+(j-3)*10;y=cy+abs(j-3)*2;c.polygon([(x-4,y),(x-2,y-25),(x+4,y-36),(x+7,y)],'main',shade)
    else:
     for j in range(4):ring(cx+(j-1.5)*9,cy+4-j%2*4,4,5,8,slot='accent',s=4)
  elif shape in ('snare','vine_cage','thorns','rot','grove','canopy'):
   n={'snare':8,'vine_cage':6,'thorns':13,'rot':10,'grove':7,'canopy':12}[shape]
   ring(cx,cy,r,r*.35,20,slot='accent',s=2)
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r*(.4+.6*g);y=cy+math.sin(a)*r*.4
    mid=(cx+math.cos(a+.35)*r*.6,cy+math.sin(a+.35)*r*.25-12*g)
    line((cx,cy),mid,3);line(mid,(x,y-15*g if shape in ('grove','canopy') else y))
    if shape=='thorns':c.polygon([(x-4,y),(x+1,y-16*g),(x+6,y+2)],'main',shade)
    elif shape=='vine_cage':line((x,y),(cx+math.cos(a)*r*.5,cy-39*g),4);leaf(x,y-12,a)
    elif shape=='rot':line((x,y),(x-6,y-8),3,'accent');line((x,y),(x+5,y-12),3)
    else:leaf(x,y-15*g,a+math.pi/2,10 if shape=='canopy' else 7)
   if shape=='canopy':
    for j in range(5):ring(cx+(j-2)*18,cy-40-abs(j-2)*-7,21,10,12,slot='core',s=3)
  elif shape in ('staff','bark_plates','heartwood'):
   if shape=='staff':
    line((cx-5,cy+8),(cx+6,cy-35),4,'accent')
    for j in range(4):leaf(cx-1+j*2,cy-8-j*7,(-1 if j%2 else 1)*.8,7)
   elif shape=='bark_plates':
    for j in range(6):
     a=j*math.pi/3;x=cx+math.cos(a)*r*.65;y=cy+math.sin(a)*r*.5-12
     c.polygon([(x-6,y+7),(x-5,y-9),(x+4,y-13),(x+7,y+4)],'main',shade);line((x-1,y-7),(x+1,y+5),5,'accent')
   else:
    ring(cx,cy-12,19,23,10,slot='accent',s=3)
    for j in range(5):leaf(cx+(j-2)*7,cy-18+abs(j-2)*5,j*.6,9)
  elif shape in ('seed_flame','fireflies','blossom','sprout','leaf_word','dew','meadow','spring','new_leaf','sapling'):
   n={'seed_flame':7,'fireflies':17,'blossom':8,'sprout':3,'leaf_word':4,'dew':6,'meadow':13,'spring':11,'new_leaf':2,'sapling':5}[shape]
   if shape in ('fireflies','meadow','spring'):ring(cx,cy,r,r*.35,20,slot='accent',s=2)
   for j in range(n):
    a=j*math.tau/n+t*.7;x=cx+(0 if n==2 else math.cos(a)*r*.7);y=cy+math.sin(a)*r*.3
    if shape=='seed_flame':
     c.polygon([(x-4,y),(x,y-10-8*g),(x+3,y-5),(x+5,y)],'main',shade);star(x,y-6,1)
    elif shape=='fireflies':star(x,y-15-12*t,2 if j%4==0 else 1)
    elif shape=='dew':
     y-=t*28;c.polygon([(x,y-5),(x-3,y+2),(x,y+4),(x+3,y+2)],'main',shade);star(x,y+1,1)
    elif shape=='blossom':
     leaf(cx,cy-12,a,17);star(cx,cy-12,4)
    elif shape=='spring':
     ring(cx,cy-8,r*(.2+t*.6),r*(.1+t*.3),16,slot='core',s=3);leaf(x,y-10-t*19,a,7)
    else:
     rise=14*g+(10*t if shape in ('leaf_word','new_leaf') else 0)
     line((x,y),(x,y-rise),3,'accent');leaf(x,y-rise,a+math.pi/2,7 if shape!='sapling' else 10)
     if shape in ('sprout','sapling','meadow'):leaf(x,y-rise+5,a-math.pi/2,6)
  elif shape in ('shockwave','gale','fog','sleet','hail','ice_slip'):
   n={'shockwave':3,'gale':5,'fog':7,'sleet':19,'hail':15,'ice_slip':5}[shape]
   if shape in ('shockwave','gale','fog'):
    for j in range(n):
     rr=r*(.3+t*.6+j*.12);ring(cx+(j-n/2)*3,cy-j*5,rr,rr*.3,18,a=t*.4,slot='accent' if shape=='fog' else 'main',s=max(1,shade-j%3))
   elif shape=='ice_slip':
    for j in range(n):
     x=cx+(j-2)*7;c.polygon([(x-4,cy),(x+4,cy-5),(x+6,cy+2),(x-2,cy+5)],'main',shade)
   else:
    for j in range(n):
     x=cx+math.sin(j*2.7)*r;y=cy+math.cos(j*1.9)*r*.3-(1-t)*48
     if shape=='hail':c.polygon([(x-4,y),(x,y-6),(x+5,y),(x+1,y+5)],'main',shade)
     else:line((x-7,y-11),(x,y),4,'core')
     line((x-3,y-12),(x-5,y-17),2,'accent')
  elif shape in ('moon_pillar','moon_crescent','moon_tide'):
   n=5 if shape=='moon_pillar' else 2 if shape=='moon_crescent' else 3
   for j in range(n):ring(cx,cy-j*7,r*(.3+j*.13),r*(.15+j*.065),20,a=t*.2,slot='core',s=4)
   if shape=='moon_pillar':
    for j in (-1,0,1):line((cx+j*13,cy),(cx+j*10,cy-67*g),4,'main')
    ring(cx,cy-72*g,13,13,16,slot='accent',s=4)
   elif shape=='moon_crescent':
    ring(cx-3,cy-12,13,18,12,slot='core',s=5);line((cx-9,cy-30),(cx-12,cy+3),3,'accent')
   else:
    for j in range(5):star(cx+(j-2)*9,cy-5-t*20-j%2*8,1)
  elif shape in ('fork','branches','tempest','fork_hit'):
   n={'fork':3,'branches':5,'tempest':11,'fork_hit':4}[shape]
   for j in range(n):
    x=cx+(j-(n-1)/2)*(r*1.5/n);top=cy-(48 if shape!='fork_hit' else 23)*g
    pts=[(x-8,top),(x+3,top+15),(x-4,top+27),(x+7,cy)]
    for a,b in zip(pts,pts[1:]):line(a,b,5,'core')
    line((x+3,top+15),(x+15,top+9),3,'accent')
   if shape=='tempest':ring(cx,cy-36,r,r*.23,20,a=t,slot='accent',s=3)
   elif shape=='branches':ring(cx,cy,r,r*.35,12,slot='accent',s=2)
  elif shape=='unravel':
   for j in range(7):
    a=j*math.tau/7+t;line((cx+math.cos(a)*r*.3,cy+math.sin(a)*r*.15),(cx+math.cos(a)*r,cy+math.sin(a)*r*.5-9),4,'core');leaf(cx+math.cos(a)*r,cy+math.sin(a)*r*.5,a,4)
  elif shape=='pack':
   ring(cx,cy,r,r*.4,20,slot='accent',s=3)
   for side in (-1,1):
    x=cx+side*25
    for j in range(4):ring(x+(j-1.5)*6,cy-17-j%2*3,3,4,8,slot='core',s=5)
    ring(x,cy-7,9,6,10,slot='main',s=4)
  elif shape=='thorn_step':
   for j in range(5):
    x=cx+(j-2)*7;c.polygon([(x-4,cy+3),(x+1,cy-12-j%2*7),(x+5,cy+4)],'main',shade)
  elif shape in ('fangs','dire_fangs','claws','elder_claws'):
   n={'fangs':2,'dire_fangs':4,'claws':3,'elder_claws':5}[shape]
   for j in range(n):
    x=cx+(j-(n-1)/2)*7
    if 'fangs' in shape:c.polygon([(x-3,cy-24),(x+4,cy-21),(x,cy+4)],'main',shade)
    else:line((x-10,cy+5),(x+8,cy-25),5,'core');line((x-8,cy+5),(x+10,cy-25),3,'main')
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(shape,material) in DESIGNS.items():
  w,h=dimensions(name)
  out.append(Fx('druid_'+name,name.replace('_',' ').title(),'Druid',lambda n=name:frames(n),
   {material:_v(main=PALETTES[material],core='magic/holy' if material in ('nature','healing') else 'fx/flash',accent='fx/dust' if shape in DARK else PALETTES[material])},
   20,(8,h//2) if shape=='whip' else (w//2,round(h*.66)),'body' if shape=='whip' else 'feet',blend='normal' if shape in DARK else 'add'))
 out.append(Fx('druid_produce_flame_shot','Produce Flame flight','Druid',lambda:frames('produce_flame',shot=True),
  {'fire':_v(main='magic/fire',core='fx/flash',accent='fx/gold')},24,(24,16),'projectile',loop=True))
 return out
