"""Bard: musical phrasing, fractured psychic masks and three distinct college motifs."""
import math
PALETTES={'gold':'fx/gold','healing':'magic/nature','psychic':'fx/psychic','thunder':'fx/thunder','illusion':'fx/mist','arcane':'magic/arcane','fey':'magic/nature','force':'fx/force','charm':'fx/psychic'}
DESIGNS={
 'bardic_inspiration':('inspiration','gold'),'song_of_rest':('lullaby','healing'),'countercharm':('countermelody','arcane'),
 'vicious_mockery':('mockery','psychic'),'thunderclap':('clap','thunder'),'minor_illusion':('mask','illusion'),
 'healing_word':('word','healing'),'cure_wounds':('restitch','healing'),'dissonant_whispers':('whispers','psychic'),
 'faerie_fire':('fairylights','fey'),'heroism':('hero','gold'),'thunderwave':('wave','thunder'),
 'shatter':('glass','thunder'),'hold_person':('staves','arcane'),'lesser_restoration':('cleanse','healing'),
 'silence':('mute','illusion'),'hypnotic_pattern':('hypnosis','psychic'),'mass_healing_word':('chorus','healing'),
 'dispel_magic':('unweave','arcane'),'greater_invisibility':('vanish','illusion'),'dimension_door':('door','arcane'),
 'hold_monster':('grand_staves','arcane'),'greater_restoration':('resolve','healing'),
 'combat_inspiration':('standard','gold'),'battle_magic':('crescent_blade','thunder'),'cutting_words':('cut_words','psychic'),
 'counterspell':('cancel_score','arcane'),'magical_secrets':('open_book','force'),'peerless_skill':('perfect_chord','arcane'),
 'alluring_verse':('allure','charm'),'enthralling_aria':('aria','charm'),'sirens_call':('siren','charm'),
 'font_of_inspiration':('font','gold'),'silence_turn':('hush_pulse','thunder')}
PASSIVES={'bard.font_of_inspiration':'fx.bard_font_of_inspiration.gold','bard.silence.turn':'fx.bard_silence_turn.thunder'}
SMALL={'vicious_mockery','thunderclap','minor_illusion','font_of_inspiration','silence_turn'}
LARGE={'fairylights','wave','glass','mute','hypnosis','chorus','grand_staves','standard','open_book','aria','siren'}
DARK={'mask','mute','vanish'}
def frames(name):
 from .fx import Canvas,SLOTS,_twinkle
 shape,material=DESIGNS[name];large=shape in LARGE;w,h=(208,144) if large else (80,80) if name in SMALL else (112,112);count=8 if name in SMALL else 12;out=[]
 for i in range(count):
  c=Canvas(w,h,SLOTS);t=i/(count-1);g=min(1,(i+1)/3);cx,cy=w/2,h*.66;r=(67 if large else 19 if name in SMALL else 32)*g;shade=5 if i in (2,3) else max(1,round(4*(1-t)));hot='flash' if i==2 and shape not in DARK else 'core'
  def line(a,b,slot='main',s=shade):
   if slot=='core' and s>=4:
    for dx,dy in ((-1,0),(1,0),(0,-1),(0,1)):c.line(a[0]+dx,a[1]+dy,b[0]+dx,b[1]+dy,'main',min(3,shade))
   c.line(*a,*b,hot if slot=='core' else slot,s)
  def path(p,slot='main',s=shade,close=False):
   for a,b in zip(p,p[1:]+p[:1] if close else p[1:]):line(a,b,slot,s)
  def ring(x,y,rx,ry,n=20,a=0,slot='main',s=shade):path([(x+math.cos(a+j*math.tau/n)*rx,y+math.sin(a+j*math.tau/n)*ry) for j in range(n)],slot,s,True)
  def star(x,y,size=2):_twinkle(c,x,y,hot,size,shade)
  def note(x,y,size=7,flip=1):
   c.ellipse(x-.5,y,3.5,2,'main',shade);line((x+3,y),(x+3,y-size*2),'core',4);path([(x+3,y-size*2),(x+3+flip*size,y-size*1.4),(x+3+flip*size,y-size*.8)],'accent',3)
  def stave(x,y,width=32,waves=False):
   for k in range(3):
    pts=[(x-width/2+j*width/12,y+k*5+(math.sin(j*.55+t*3)*3 if waves else 0)) for j in range(13)];path(pts,'accent',max(1,shade-1))
  def mask(x,y,size=16,broken=False):
   pts=[(x-size,y-size*.7),(x,y-size),(x+size,y-size*.7),(x+size*.65,y+size*.65),(x,y+size),(x-size*.65,y+size*.65)]
   if i in (2,3,4):c.polygon(pts,'main',1)
   path(pts,'main',shade,True);line((x-size*.65,y-size*.15),(x-size*.15,y-size*.3),'core',4);line((x+size*.15,y-size*.3),(x+size*.65,y-size*.15),'core',4)
   path([(x-size*.4,y+size*.3),(x,y+size*.45),(x+size*.4,y+size*.3)],'accent',3)
   if broken:path([(x+2,y-size),(x-3,y-3),(x+4,y+3),(x,y+size)],'core',5)
  if shape in ('inspiration','lullaby','word','chorus','font','perfect_chord'):
   n={'inspiration':3,'lullaby':4,'word':1,'chorus':7,'font':2,'perfect_chord':6}[shape]
   if shape=='lullaby':stave(cx,cy-17,54,True)
   elif shape=='word':ring(cx,cy-13,12*g,19*g,16,a=t,slot='accent',s=2)
   elif shape=='perfect_chord':
    ring(cx,cy-19,23,23,12,a=t*.3,slot='core',s=4);ring(cx,cy-19,13,13,12,a=-t*.5,slot='accent',s=2)
   else:ring(cx,cy,r,r*.32,24,slot='accent',s=2)
   for j in range(n):
    a=j*math.tau/n;x=cx+(j-(n-1)/2)*(16 if large else 12);y=cy-9-12*t+(math.sin(a+t)*7 if shape!='font' else -j*13)
    note(x,y,6+j%2)
   if shape=='font':
    for side in (-1,1):path([(cx+side*17,cy),(cx+side*11,cy-18),(cx+side*5,cy-34)],'core',4)
  elif shape in ('restitch','cleanse','resolve','countermelody'):
   if shape=='restitch':
    for j in range(5):y=cy-j*7;line((cx-12,y-4),(cx+12,y+4),'core',4);line((cx+12,y-4),(cx-12,y+4),'main',3)
    ring(cx,cy-14,21,26,20,slot='accent',s=2)
   elif shape=='cleanse':
    for j in range(5):a=j*math.tau/5+t*.6;star(cx+math.cos(a)*r,cy+math.sin(a)*r*.4-16-13*t,2)
    path([(cx-8,cy-33),(cx-8,cy-9),(cx,cy-3),(cx+8,cy-9),(cx+8,cy-33)],'core',5)
   elif shape=='resolve':
    for j in range(3):ring(cx,cy-j*13,11+j*7,5+j*3,16,slot='core',s=4)
    for side in (-1,1):note(cx+side*23,cy-13-14*t,6,side)
   else:
    for side in (-1,1):ring(cx+side*10,cy-17,18*g,13*g,16,a=side*t,slot='core',s=4)
    for j in range(3):path([(cx+(j-1)*16,cy-40),(cx+(j-1)*16+6,cy-31),(cx+(j-1)*16-2,cy-23)],'accent',3)
  elif shape in ('mockery','mask','whispers','cut_words','allure'):
   if shape=='mockery':mask(cx,cy-16,17,True)
   elif shape=='mask':mask(cx-10,cy-17,13);mask(cx+12,cy-24,11)
   elif shape=='whispers':
    for j in range(4):path([(cx-27,cy-33+j*10),(cx-11,cy-38+j*10),(cx+3,cy-27+j*8),(cx+24,cy-20+j*5)],'core',4)
   elif shape=='cut_words':
    for j in range(3):stave(cx,cy-30+j*8,35)
    path([(cx-25,cy-36),(cx+25,cy-2)],'core',5);ring(cx,cy,22,7,12,slot='accent',s=2)
   else:
    for j in range(3):
     a=t*2+j*.6;path([(cx-23+j*13,cy),(cx-32+j*13,cy-18),(cx-11+j*9,cy-34)],'main',4);note(cx-11+j*9,cy-34,5)
  elif shape in ('clap','wave','glass','hush_pulse'):
   if shape=='clap':
    for side in (-1,1):path([(cx+side*27*g,cy-29),(cx+side*12,cy-16),(cx+side*26*g,cy-2)],'core',5)
    star(cx,cy-15,5)
   elif shape=='wave':
    for j in range(3):rr=r*(.45+t*.4+j*.18);ring(cx,cy-j*7,rr,rr*.32,24,slot='core' if j==1 else 'main',s=max(2,shade-j%2))
   elif shape=='glass':
    for j in range(11):
     a=j*math.tau/11;x=cx+math.cos(a)*r*(.3+t*.7);y=cy+math.sin(a)*r*.35-14
     c.polygon([(x-5,y+7),(x-3,y-12),(x+5,y-5),(x+4,y+8)],'main',shade);line((x-2,y-7),(x+2,y+4),'core',4)
   else:
    stave(cx,cy-17,34);path([(cx-21,cy-30),(cx-8,cy-16),(cx+5,cy-26),(cx+20,cy-10)],'core',4)
  elif shape in ('fairylights','hero','standard'):
   ring(cx,cy,r,r*.35,24,slot='accent',s=2)
   n=11 if shape=='fairylights' else 3 if shape=='hero' else 5
   for j in range(n):
    a=j*math.tau/n;x=cx+math.cos(a)*r*.8;y=cy+math.sin(a)*r*.3
    if shape=='fairylights':star(x,y-12-8*t,2 if j%3 else 3)
    elif shape=='hero':path([(x-8,y-8),(x-8,y-26),(x+8,y-20),(x,y-14)],'core',4)
    else:
     line((x,y),(x,y-28),'accent',3);c.polygon([(x,y-28),(x+10,y-23),(x,y-17)],'main',shade);note(x+2,y-5,5)
  elif shape in ('staves','grand_staves','mute','unweave','vanish','door','cancel_score','hypnosis'):
   if shape in ('staves','grand_staves'):
    n=3 if shape=='staves' else 6
    for j in range(n):
     a=j*math.tau/n;x=cx+math.cos(a)*r*.65;y=cy+math.sin(a)*r*.25;path([(x,y),(x,y-35*g),(cx+(x-cx)*.45,cy-47*g)],'core',4)
    ring(cx,cy,r*.7,r*.25,16,slot='accent',s=2)
    if shape=='grand_staves':stave(cx,cy-22,68)
   elif shape=='mute':
    ring(cx,cy,r,r*.35,24,slot='accent',s=2)
    for j in range(5):y=cy-29+j*6;line((cx-r*.7,y),(cx-12,y),'main',3);line((cx+12,y),(cx+r*.7,y),'main',3)
    path([(cx-7,cy-34),(cx+7,cy-7)],'core',4)
   elif shape=='unweave':
    for j in range(5):
     y=cy-34+j*7;path([(cx-28-t*10,y-6*t),(cx-8,y),(cx+2,y-4),(cx+29+t*8,y+9*t)],'core',4)
   elif shape=='vanish':
    for j in range(4):ring(cx+(j-1.5)*4,cy-15,9+j*5,18-j*2,16,a=t*.3,slot='accent',s=max(1,shade-j%3))
   elif shape=='door':
    path([(cx-21,cy),(cx-21,cy-37),(cx-12,cy-49),(cx+12,cy-49),(cx+21,cy-37),(cx+21,cy)],'core',5)
    for j in range(4):line((cx-15+j*10,cy-38),(cx-15+j*10,cy-7),'main',2)
    ring(cx,cy,26,8,16,slot='accent',s=2)
   elif shape=='cancel_score':
    ring(cx,cy-18,24*g,24*g,12,slot='accent',s=3);path([(cx-21,cy-41),(cx+21,cy+3)],'core',5)
    for j in range(4):a=j*math.pi/2+t;note(cx+math.cos(a)*31,cy-18+math.sin(a)*22,5)
   else:
    for j in range(4):
     x=cx+(j-1.5)*25;y=cy-20+math.sin(j+t*2)*10;path([(x,y-18),(x+19,y),(x,y+18),(x-19,y)],'core',4,True)
    ring(cx,cy,r,r*.3,24,a=t*.5,slot='accent',s=2)
  elif shape=='crescent_blade':
   path([(cx-25,cy+4),(cx-9,cy-18),(cx+21,cy-35),(cx+9,cy-10),(cx-25,cy+4)],'core',5)
   for j in range(3):note(cx-15+j*15,cy-8-j*6,5)
  elif shape=='open_book':
   c.polygon([(cx-34,cy-8),(cx-29,cy-38),(cx,cy-28),(cx+29,cy-38),(cx+34,cy-8),(cx,cy+2)],'main',2)
   path([(cx-34,cy-8),(cx-29,cy-38),(cx,cy-28),(cx+29,cy-38),(cx+34,cy-8),(cx,cy+2)],'core',5,True)
   line((cx,cy-28),(cx,cy+2),'accent',3)
   for j in range(5):a=j*math.tau/5;x=cx+math.cos(a)*r;y=cy+math.sin(a)*r*.35-23;path([(x,y-6),(x+5,y),(x,y+6),(x-5,y)],'core',4,True)
  elif shape in ('aria','siren'):
   n=5 if shape=='aria' else 9;ring(cx,cy,r,r*.35,24,slot='accent',s=2)
   for j in range(n):
    a=(j/(n-1)-.5)*math.pi;x=cx+math.sin(a)*r;y=cy-math.cos(a)*37*g
    path([(cx,cy),(cx+(x-cx)*.45,cy-20),(x,y)],'main',4);note(x,y-4,6)
   if shape=='siren':
    for side in (-1,1):path([(cx+side*16,cy-3),(cx+side*25,cy-25),(cx+side*14,cy-47)],'core',5)
  else:raise ValueError(shape)
  for j in range(4):
   a=j*math.tau/4+.4;x=cx+math.cos(a)*r*(.5+t*.5);y=cy-13+math.sin(a)*r*.3-9*t
   if i>=2:c.line(x-3,y+2,x,y,'accent',max(1,shade-1));_twinkle(c,x,y,'core',1,max(1,shade-1))
  out.append(c)
 return out

def catalogue():
 from .fx import Fx,_v
 out=[]
 for name,(shape,material) in DESIGNS.items():
  first=frames(name)[0];w,h=first.w,first.h
  out.append(Fx('bard_'+name,name.replace('_',' ').title(),'Bard',lambda n=name:frames(n),
   {material:_v(main=PALETTES[material],core='fx/flash',accent='fx/gold' if material in ('gold','healing') else PALETTES[material])},
   20,(w//2,round(h*.66)),'feet',blend='normal' if shape in DARK else 'add'))
 return out
