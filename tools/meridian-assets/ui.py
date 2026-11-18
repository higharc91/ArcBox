"""Original industrial UI kit and live-text phase reviews for Meridian #335."""
from __future__ import annotations
import argparse,hashlib,html,json
from pathlib import Path
from PIL import Image,ImageDraw
import build as style
import cards,crew,compartments

ROOT=style.ROOT
REFERENCE=Path(__file__).parent/'reference/ui/one_of_us_isnt.json'
RULES_HEAD='3b272cb6c0f3b7cb3a53efa35a0d34e9f4dd330d'
IDS=['logo','title','panel','seatSlot','anomalyCounter','roundClock','logPanel','cardPanel','diceTray','die','miniMap','breachPlate','debriefPlate']
PANELS=[k for k in IDS if k not in ['logo','title','die']]
VALUES=[0,0,1,1,1,2]
PHASES=['boarding','explore','breach','endgame','debrief']


def panel(key):
    if key not in PANELS:raise ValueError(key)
    kind='incident' if key=='breachPlate' else 'anomaly' if key=='anomalyCounter' else 'salvage'
    image=cards.frame(kind)
    # Function-specific, fixed-corner instrumentation; never decorate stretch bands.
    d=ImageDraw.Draw(image)
    colour='#9ee0ed' if key in ['panel','miniMap','roundClock'] else '#e0bd64'
    n=PANELS.index(key)
    for x,y in [(8,8),(55,55)]:
        d.rectangle((x-2,y-2,x+2,y+2),fill='#353b51')
        if key=='roundClock':d.ellipse((x-2,y-2,x+2,y+2),outline=colour);d.line((x,y-1,x,y+1),fill=colour)
        elif key=='miniMap':d.rectangle((x-2,y-2,x+2,y+2),outline=colour);d.point((x,y),fill=colour)
        else:d.rectangle((x-2,y-1,x+2,y),fill=colour);d.point((x-1+(n%3),y+2),fill='#e3eadf')
    return image


def logo():
    c=style.canvas(128,48)
    c.ellipse(68,24,23,21,'hull',0);c.ellipse(68,24,21,19,'command',3)
    c.ellipse(68,24,18,16,'floor',0)
    for y in [3,40]:c.rect(63,y,73,y+4,'floor',0)
    c.polygon([(6,24),(35,15),(75,15),(112,24),(75,33),(35,33)],'hull',0)
    c.polygon([(13,24),(38,18),(76,18),(103,24),(76,30),(38,30)],'hull',3)
    c.line(32,20,75,20,'hull',5);c.rect(46,21,77,27,'floor',1)
    c.rect(56,21,63,26,'command',4);c.rect(66,21,73,26,'command',3)
    for x in [22,30]:c.rect(x,21,x+3,27,'coolant',4)
    c.polygon([(80,17),(98,22),(90,22),(75,18)],'linen',3)
    return c.render(style.PALETTE)


def title():
    im=Image.new('RGBA',(640,128),'#21243c');d=ImageDraw.Draw(im)
    for y in [0,4,120,124]:d.rectangle((0,y,639,y+2),fill='#657482' if y in [4,120] else '#353b51')
    d.polygon([(0,12),(348,12),(380,42),(380,87),(348,115),(0,115)],fill='#2a3046')
    for x in range(16,624,64):d.rectangle((x,124,x+28,126),fill='#617485')
    for x,y in [(402,14),(590,30),(548,108),(365,89),(612,97)]:d.rectangle((x,y,x+1,y+1),fill='#8396a1')
    im.alpha_composite(logo().resize((256,96),Image.Resampling.NEAREST),(368,16))
    return im


def die(value,phase=None):
    if value not in [0,1,2]:raise ValueError(value)
    c=style.canvas(24,24)
    outlines=[[(3,3),(20,3),(22,5),(22,20),(20,22),(3,22),(1,20),(1,5)],
              [(6,1),(21,6),(18,22),(2,17)],[(4,2),(21,4),(20,21),(2,19)],[(2,7),(17,1),(22,16),(7,22)]]
    points=outlines[0 if phase is None else phase]
    c.polygon(points,'hull',0)
    if phase is None:
        c.rect(4,4,19,19,'linen',3);c.line(4,4,19,4,'linen',5);c.line(19,5,19,19,'hull',3)
        dots={0:[],1:[(12,12)],2:[(8,8),(16,16)]}[value]
    else:
        c.polygon([(int((x-12)*.72+12),int((y-12)*.72+12)) for x,y in points],'linen',3)
        dots=[(12,12)] if phase%2 else [(8,9),(15,15)]
    for x,y in dots:c.ellipse(x,y,2,2,'floor',0)
    return c.render(style.PALETTE)


def badge(seat,size=24):
    if seat not in range(6):raise ValueError(seat)
    im=Image.new('RGBA',(size,size));d=ImageDraw.Draw(im)
    # Seat colours are the exact platform exceptions; pip count also identifies seats.
    d.polygon([(3,0),(size-4,0),(size-1,3),(size-1,size-4),(size-4,size-1),(3,size-1),(0,size-4),(0,3)],fill=style.PLAYERS[seat])
    d.rectangle((3,3,size-4,size-4),fill='#21243c')
    for i in range(seat+1):
        x=4+(i%3)*(4 if size==16 else 6);y=5+(i//3)*(5 if size==16 else 9)
        d.rectangle((x,y,x+1,y+2),fill='#e3eadf')
    return im


def auto_badge():
    c=style.canvas(16,12)
    c.rect(2,3,13,10,'hull',0);c.rect(3,4,12,9,'hull',4)
    c.rect(5,5,6,6,'floor',0);c.rect(9,5,10,6,'floor',0)
    c.line(6,8,9,8,'floor',0);c.line(7,1,7,3,'hull',4);c.rect(6,0,8,1,'linen',4)
    return c.render(style.PALETTE)


def backdrop():
    im=Image.new('RGBA',(128,128),'#21243c');d=ImageDraw.Draw(im)
    d.rectangle((3,3,124,124),outline='#343e52',width=2)
    for x,y in [(8,8),(116,8),(8,116),(116,116)]:d.rectangle((x,y,x+3,y+3),fill='#424b5f')
    d.rectangle((12,60,115,66),fill='#2a3046');d.line((16,62,110,62),fill='#343e52',width=1)
    return im


def alarm():
    im=Image.new('RGBA',(960,540),'#21243c');d=ImageDraw.Draw(im)
    d.rectangle((16,16,943,523),outline='#872e52',width=6)
    d.rectangle((27,27,932,512),outline='#b17437',width=2)
    for y in range(0,540,68):
        for right in [False,True]:
            x=928 if right else 0
            d.polygon([(x,y),(x+31,y+24),(x+31,y+48),(x,y+24)],fill='#cf9b48')
    return im


def entry(pack,name,image,**meta):
    return {**style.immutable(pack,'ui',name,image),'frameSize':list(image.size),**meta}


def build(root=ROOT):
    pack=root/'content/meridian/base';report=root/'docs/superpowers/reports/meridian-art/ui';report.mkdir(parents=True,exist_ok=True)
    pieces={key:entry(pack,key,panel(key),insets=[12]*4,contentInset=[16]*4) for key in PANELS}
    pieces.update(logo=entry(pack,'logo',logo()),title=entry(pack,'title',title()))
    roll=Image.new('RGBA',(96,24))
    for i in range(4):roll.alpha_composite(die([0,1,2,1][i],i),(i*24,0))
    pieces['die']={'values':VALUES,'faces':['face'+str(v) for v in VALUES],
       'roll':{**style.immutable(pack,'ui','die-roll',roll),'frameSize':[24,24],'frames':4,'fps':12,'loop':True,'reducedMotion':'show face0 without animation'},
       **{'face'+str(v):entry(pack,'die-face'+str(v),die(v)) for v in [0,1,2]}}
    lobby={'backdrop':entry(pack,'lobby-backdrop',backdrop()),'banner':pieces['logo'],
      'badges':{'seat'+str(i+1):entry(pack,'seat-'+str(i+1),badge(i)) for i in range(6)}}
    extras={'auto':entry(pack,'auto-neutral',auto_badge(),roleNeutral=True),
       'miniMarkers':{'seat'+str(i+1):entry(pack,'mini-marker-'+str(i+1),badge(i,16)) for i in range(6)},
       'breachBackdrop':entry(pack,'breach-backdrop',alarm(),reducedMotion='same static image; no flash or shake')}
    manifest={'version':1,'issue':335,'rulesHead':RULES_HEAD,'displayScale':2,'ui':pieces,'lobby':lobby,'extras':extras}
    style.write_json(pack/'ui/manifest.json',manifest)
    board_reviews(report)
    review_page(report,manifest)
    return pack,report


def board_reviews(report):
    """Concept boards use genuine 64px tiles at whole 2x; no game-state claim."""
    roster=crew.rules()
    for deck in compartments.DECKS:
        eligible=[r for r in compartments.rules() if deck in r['decks']][:24]
        image=Image.new('RGBA',(768,512),'#21243c')
        for i,r in enumerate(eligible):
            tile=compartments.compose(compartments.tile(r,deck),r)
            image.alpha_composite(tile.resize((128,128),Image.Resampling.NEAREST),((i%6)*128,(i//6)*128))
        cm=json.loads((ROOT/'content/meridian/base/crew/manifest.json').read_text())['crew']
        for i,c in enumerate(roster[:6]):
            actor=Image.open(ROOT/'content/meridian/base/crew'/cm[c['id']]['idle']['file']).convert('RGBA')
            image.alpha_composite(actor.resize((48,48),Image.Resampling.NEAREST),(40+i*128,168+(i%2)*128))
        image.convert('RGB').save(report/f'board-{deck}.png')
    for deck in compartments.DECKS:
        im=Image.new('RGBA',(128,48),'#21243c');d=ImageDraw.Draw(im)
        for i in range(21):
            x=4+(i%7)*17;y=3+(i//7)*15
            d.rectangle((x,y,x+13,y+11),fill='#343e52',outline='#657482')
        # Topology glyphs, not shrunken room bitmaps. Markers stay independent.
        im.save(report/f'mini-{deck}.png')


def review_page(report,m):
    prefix='../../../../../content/meridian/base/'
    roster=crew.rules();cm=json.loads((ROOT/'content/meridian/base/crew/manifest.json').read_text())['crew']
    pm=json.loads((ROOT/'content/meridian/base/portraits/manifest.json').read_text())['portraits']
    scenario=json.loads(REFERENCE.read_text());out=[]
    def asset(e,w=None,alt='',cls=''):
        size=e['frameSize'];w=w or size[0]*2;h=w*size[1]//size[0]
        return f'<img class="{cls}" src="{prefix}ui/{e["file"]}" width="{w}" height="{h}" alt="{html.escape(alt)}">'
    def surface(key,body,cls=''):
        return f'<div class="surface {cls}" data-piece="{key}" style="border-image-source:url({prefix}ui/{m["ui"][key]["file"]})">{body}</div>'
    def auto():return '<span class="auto">'+asset(m['extras']['auto'])+'<span>auto</span></span>'
    def actor(c):return f'<img src="{prefix}crew/{cm[c["id"]]["idle"]["file"]}" width="48" height="48" alt="">'
    def hud(end=False):
        seats=''.join(surface('seatSlot',asset(m['lobby']['badges']['seat'+str(i+1)])+f'<div><strong>{html.escape(c["name"])}</strong>'+ (auto() if i==2 else '')+'</div>','seat') for i,c in enumerate(roster[:6]))
        counter=surface('anomalyCounter','<span>Anomalies</span><strong>4</strong>','stat')
        clock=surface('roundClock','<span>Round</span><strong>7 / 10</strong>','stat round-clock') if end else ''
        return '<header class="hud">'+seats+counter+clock+'</header>'
    def minimap(deck,index):
        marker=asset(m['extras']['miniMarkers']['seat'+str(index+1)],cls='mini-marker')
        return surface('miniMap',f'<h3>{deck.title()} / {"I"*(compartments.DECKS.index(deck)+1)}</h3><div class="mini-scene"><img src="mini-{deck}.png" width="256" height="96" alt="{deck} deck topology">{marker}</div>','mini-map')
    def dice():
        faces=''.join('<figure>'+asset(m['ui']['die']['face'+str(v)])+f'<figcaption>{v}</figcaption></figure>' for v in VALUES)
        roll=m['ui']['die']['roll'];url=prefix+'ui/'+roll['file']
        return surface('diceTray','<h3>Signal dice</h3><div class="dice-faces">'+faces+'</div><div class="rolling" role="img" aria-label="Cosmetic rolling die" data-src="'+url+'" data-still="'+prefix+'ui/'+m['ui']['die']['face0']['file']+'" style="background-image:url('+url+')"></div>','dice-tray')
    def brand():return '<div class="brand" style="background-image:url('+prefix+'ui/'+m['ui']['title']['file']+')"><h1>DERELICT<span>ISV MERIDIAN</span></h1></div>'
    boarding=[]
    for i,c in enumerate(roster):
        portrait=pm[c['id']]['neutral']
        image=f'<img src="{prefix}portraits/{portrait["file"]}" width="192" height="224" alt="{html.escape(c["name"])} portrait">'
        status='Seat '+str(i+1) if i<6 else 'Available'
        boarding.append(surface('seatSlot',image+'<div><h2>'+html.escape(c['name'])+'</h2><p>'+html.escape(c['role'])+'</p><strong class="status">'+status+'</strong></div>','roster-card'))
    out.append('<section class="stage boarding" data-phase="boarding">'+brand()+'<div class="phase-heading"><h2>Boarding</h2><span>6 crew selected · Room AC42</span></div><div class="roster">'+''.join(boarding)+'</div><p class="boarding-note">Choose your crew on your phone. Unclaimed seats remain available.</p></section>')
    med=next(c for c in cards.catalogue()['salvage'] if c['id']=='med_kit')
    for phase,deck in [('explore','command'),('endgame','engineering')]:
        end=phase=='endgame';inactive=[d for d in compartments.DECKS if d!=deck]
        heading=('Endgame / '+scenario['title']) if end else 'Explore / Command deck'
        side=surface('cardPanel','<div class="kicker">Salvage</div><h2>'+med['title']+'</h2><p class="flavour">'+med['text']+'</p>'+''.join('<div class="rule"><strong>'+html.escape(a)+'</strong><p>'+html.escape(b)+'</p></div>' for a,b in cards.details(med)),'current-card')+dice()
        board='<main class="board-area"><h2>'+html.escape(heading)+'</h2><div class="map-row"><img src="board-'+deck+'.png" width="768" height="512" alt="Concept '+deck+' board at actual 128px room scale"><aside>'+''.join(minimap(d,i) for i,d in enumerate(inactive))+'</aside></div>'+surface('logPanel','<h3>Ship log</h3><p>Ines entered the Med Bay. A Salvage card was drawn.</p><p>'+('Repair points: 2 / 6. The scanner is ready.' if end else 'Tiller: The next compartment is waiting.')+'</p>','log')+'</main>'
        out.append('<section class="stage play" data-phase="'+phase+'">'+hud(end)+'<div class="play-content">'+board+'<aside class="event-column">'+side+'</aside></div></section>')
    breach=surface('breachPlate','<div class="kicker">Breach / Public announcement</div><h1>'+html.escape(scenario['title'])+'</h1><p>'+html.escape(scenario['announcement'])+'</p><div class="briefing-status">Read your briefing on your phone · 90 seconds</div>','breach-copy')
    out.append('<section class="stage breach" data-phase="breach" style="background-image:url('+prefix+'ui/'+m['extras']['breachBackdrop']['file']+')"><div class="alarm-label">BREACH</div>'+breach+'</section>')
    roles=''.join('<div class="role-reveal">'+actor(c)+'<div><h3>'+html.escape(c['name'])+'</h3><p>'+('The Mimic' if i==5 else 'Crew')+'</p>'+ (auto() if i in [2,5] else '')+'</div></div>' for i,c in enumerate(roster[:6]))
    awards=''.join(surface('panel','<h3>'+label+'</h3><p>'+html.escape(roster[i]['name'])+'</p>','award') for i,label in enumerate(['Pathfinder: most compartments found','Heavy hitter: most damage dealt','Last one standing']))
    summary=surface('debriefPlate','<div class="kicker">Debrief / All roles revealed</div><h1>The crew secured the ship.</h1><p class="winners">Winners: Ines, Tobe, Sable, Anselm and Pell</p><div class="roles">'+roles+'</div>','results')
    fullmap='<aside class="full-map"><h2>Full ship / All compartments</h2>'+''.join(minimap(d,i) for i,d in enumerate(compartments.DECKS))+'</aside>'
    out.append('<section class="stage debrief" data-phase="debrief"><div class="debrief-top">'+summary+fullmap+'</div><div class="awards">'+awards+'</div></section>')
    badges=''.join('<div class="lobby-seat">'+asset(m['lobby']['badges']['seat'+str(i+1)])+'<span>'+html.escape(c['name'])+'</span>'+(auto() if i==2 else '')+'</div>' for i,c in enumerate(roster[:6]))
    phone='<section class="phone-device" aria-label="Phone lobby design"><div class="phone-lobby" style="background-image:url('+prefix+'ui/'+m['lobby']['backdrop']['file']+')"><div class="phone-brand">'+asset(m['lobby']['banner'])+'<h1>DERELICT<span>ISV MERIDIAN</span></h1></div><h2>Room AC42</h2><p>Boarding crew · 6 players</p><div class="lobby-seats">'+badges+'</div><button id="ready" type="button" aria-pressed="false">Ready</button><p id="ready-status" role="status">Waiting for your crew.</p></div></section>'
    gallery=''.join('<figure><figcaption>'+k+'</figcaption>'+asset(v,128)+'</figure>' for k,v in m['ui'].items() if k not in ['die','logo','title'])
    page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian UI kit review</title><style>'''+CSS+'''</style><body><h1>Meridian / ship interface kit</h1><p>Original asset review for #335. All five phases at native 1920×1080; room/crew art remains whole 2×. Titles, names, counters, roles, awards and the auto label are live text. These are illustrative layouts, not gameplay sessions or a connected platform lobby.</p><div class="controls"><button id="pause" type="button">Pause dice</button><label><input id="still" type="checkbox"> Still / reduced motion</label></div>'''+''.join('<h2>'+p.title()+'</h2><div class="pan" tabindex="0" aria-label="'+p+' native-size review">'+next(x for x in out if 'data-phase="'+p+'"' in x)+'</div>' for p in PHASES)+'''<h2>Phone lobby / 390px</h2>'''+phone+'''<h2>Pieces / exact 2× source</h2><div class="gallery">'''+gallery+'''</div><script>'''+JS+'''</script></body></html>'''
    (report/'index.html').write_text(page)


CSS='''@font-face{font-family:Ship;src:url('../../../../../src/ArcBox.Host/fonts/TitilliumWeb-SemiBold.ttf')}*{box-sizing:border-box}:root{color-scheme:dark;background:#111b2a;color:#d8e1da;font:18px/1.4 Ship,system-ui}body{margin:auto;max-width:1400px;padding:16px}h1,h2,h3,p{margin:0}body>h1,body>h2,body>p{margin:16px 0}a{color:#9ee0ed}img,.rolling,.stage{image-rendering:pixelated}img{flex:none;max-width:none}.pan{overflow:auto;max-height:80vh;border:1px solid #657482}.stage{width:1920px;height:1080px;padding:32px;background:#111b2a;font-size:30px;line-height:1.22}.stage h1{font-size:64px;line-height:1.1}.stage h2{font-size:36px;line-height:1.15}.stage h3{font-size:30px;line-height:1.15}.surface{border:24px solid transparent;border-image-slice:12 fill;border-image-width:24px;padding:12px;min-width:0}.kicker{font-size:26px;text-transform:uppercase;letter-spacing:.12em;color:#e0bd64;margin-bottom:18px}.brand{width:1280px;height:256px;background-size:1280px 256px;margin:auto;display:flex;align-items:center;padding:36px}.brand h1{font-size:76px;letter-spacing:.1em}.brand h1 span{display:block;font-size:42px;letter-spacing:.15em;margin-top:12px}.boarding{display:flex;flex-direction:column;gap:18px}.phase-heading{height:44px;flex:none;display:flex;align-items:center;justify-content:space-between}.phase-heading span{font-size:30px;color:#a2b8c0}.roster{display:grid;grid-template-columns:repeat(4,1fr);gap:16px}.roster-card{display:flex;align-items:center;gap:12px;height:304px;padding:16px}.roster-card h2{font-size:30px}.roster-card p{font-size:24px;margin:10px 0}.status{font-size:26px;color:#9ee0ed}.boarding-note{text-align:center;font-size:30px}.hud{display:grid;grid-template-columns:repeat(6,minmax(0,1fr)) 200px;gap:10px;height:180px}.hud:has(.round-clock){grid-template-columns:repeat(6,minmax(0,1fr)) 200px 200px}.seat{display:flex;align-items:center;gap:8px;padding:8px}.hud:has(.round-clock) .seat{position:relative;padding:8px 4px;display:block}.hud:has(.round-clock) .seat>img{position:absolute;top:4px;right:4px;width:24px;height:24px}.hud:has(.round-clock) .seat>div{padding-top:28px}.seat strong{font-size:26px;line-height:1.12;display:block;overflow-wrap:normal}.auto{display:inline-flex;align-items:center;gap:4px;background:#343e52;color:#ced7ca;padding:2px 6px;font-size:24px;line-height:1.1;margin-top:6px;white-space:nowrap}.stat{display:flex;align-items:center;justify-content:center;flex-direction:column;text-align:center}.stat span{font-size:24px}.stat strong{font-size:42px;color:#b1bf7a;white-space:nowrap}.round-clock strong{font-size:36px;color:#e0bd64}.play-content{display:grid;grid-template-columns:minmax(0,1fr) 600px;gap:24px;margin-top:20px;height:816px}.board-area{display:flex;flex-direction:column;gap:16px;align-items:center}.map-row{display:flex;align-items:start;gap:24px}.map-row aside{display:grid;gap:20px}.mini-map{width:320px;height:204px;flex:none;padding:8px}.mini-map h3{font-size:26px;margin-bottom:12px}.mini-scene{width:256px;height:96px;position:relative}.mini-marker{position:absolute;top:32px;left:78px}.log{width:100%;min-height:170px}.log h3{color:#9ee0ed;margin-bottom:10px}.log p{font-size:30px;margin:4px 0}.event-column{display:flex;flex-direction:column;gap:16px}.current-card{padding:20px;flex:1}.current-card h2{font-size:38px;margin-bottom:18px}.current-card .flavour{font-style:italic;border-left:4px solid #ced7ca;padding-left:16px;margin-bottom:24px}.rule{background:#111b2a;padding:14px;margin-top:16px}.rule strong{display:block;color:#ced7ca;margin-bottom:10px}.dice-tray{padding:16px;position:relative;height:224px;flex:none}.dice-tray .rolling{position:absolute;right:18px;top:14px;margin:0}.dice-tray h3{margin-bottom:16px}.dice-faces{display:flex;justify-content:space-between}.dice-faces figure{margin:0;text-align:center}.dice-faces figcaption{font-size:26px;margin-top:6px}.rolling{width:48px;height:48px;background-size:192px 48px;margin:16px auto 0}.breach{background-size:1920px 1080px;display:flex;align-items:center;justify-content:center;flex-direction:column;gap:28px}.alarm-label{height:120px;line-height:120px;font-size:96px;letter-spacing:.2em;color:#e0bd64}.breach-copy{width:1536px;height:720px;padding:48px}.breach-copy h1{margin-bottom:32px}.breach-copy p{font-size:34px;line-height:1.4}.briefing-status{margin-top:36px;padding-top:24px;border-top:4px solid #975429;color:#e0bd64}.debrief{display:flex;flex-direction:column;gap:24px}.debrief-top{display:grid;grid-template-columns:1fr 400px;gap:24px;flex:1}.results{padding:30px}.results h1{margin-bottom:20px}.winners{font-size:34px;color:#e0bd64;margin-bottom:28px}.roles{display:grid;grid-template-columns:repeat(2,1fr);gap:16px}.role-reveal{display:flex;align-items:center;gap:18px;padding:12px;background:#111b2a;min-height:112px}.role-reveal h3{font-size:30px}.role-reveal p{font-size:28px;margin-top:8px}.full-map{display:flex;flex-direction:column;align-items:center;gap:16px}.full-map>h2{font-size:30px;height:36px}.awards{display:grid;grid-template-columns:repeat(3,1fr);gap:22px;height:210px}.award{display:flex;flex-direction:column;justify-content:center}.award h3{color:#e0bd64;margin-bottom:12px}.award p{font-size:32px}.phone-device{width:390px;max-width:100%;height:844px;overflow:auto;border:2px solid #657482}.phone-lobby{padding:12px;background-size:256px 256px;min-height:100%;font-size:18px}.phone-brand{text-align:center}.phone-brand>img{display:block;margin:auto}.phone-brand h1{font-size:32px;letter-spacing:.07em;line-height:1.1;margin:10px 0 16px}.phone-brand h1 span{display:block;font-size:24px;margin-top:4px}.phone-lobby h2{font-size:26px}.phone-lobby>p{margin:8px 0 14px}.lobby-seats{display:grid;gap:8px}.lobby-seat{display:flex;align-items:center;gap:10px;min-height:64px;padding:8px;background:#21243c;border:1px solid #657482}.lobby-seat>span:not(.auto){flex:1}.phone-lobby .auto{font-size:16px;padding:2px;margin:0}.phone-lobby .auto img{width:32px;height:24px}.phone-lobby label{display:block;margin:16px 0 10px}.phone-lobby input{display:block;max-width:100%;width:100%;margin-top:6px}.phone-lobby button{width:100%;background:#657482;color:#e3eadf}.phone-lobby button[aria-pressed=true]{background:#446830}.gallery{display:flex;gap:20px;flex-wrap:wrap}.gallery figure{margin:0}.gallery figcaption{margin:10px 0}button,input{font:inherit;min-height:44px;padding:8px}button{cursor:pointer}.controls{display:flex;gap:16px;align-items:center;flex-wrap:wrap;margin:16px 0}button:disabled{opacity:.6}.controls input{min-height:0}'''
JS='''const media=matchMedia('(prefers-reduced-motion: reduce)'),pause=document.querySelector('#pause'),still=document.querySelector('#still');let paused=false,last=0,frame=0;const dice=[...document.querySelectorAll('.rolling')];function stopped(){return media.matches||still.checked}function sync(){pause.disabled=stopped();dice.forEach(d=>{d.style.backgroundImage="url("+(stopped()?d.dataset.still:d.dataset.src)+")";d.style.backgroundSize=stopped()?"48px 48px":"192px 48px"});if(stopped()){frame=0;dice.forEach(d=>{d.style.backgroundPosition='0px 0px';d.dataset.frame='0'})}}media.addEventListener('change',sync);still.onchange=sync;pause.onclick=()=>{paused=!paused;pause.textContent=paused?'Resume dice':'Pause dice'};function tick(now){if(!paused&&!stopped()&&now-last>=1000/12){last=now;frame=(frame+1)%4;dice.forEach(d=>{d.style.backgroundPosition=(-48*frame)+'px 0px';d.dataset.frame=String(frame)})}requestAnimationFrame(tick)}sync();requestAnimationFrame(tick);document.querySelector('#ready').onclick=e=>{const ready=e.target.getAttribute('aria-pressed')!=='true';e.target.setAttribute('aria-pressed',String(ready));document.querySelector('#ready-status').textContent=ready?'You are ready. Waiting for your crew.':'Waiting for your crew.'};'''

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,default=ROOT);a=p.parse_args();build(a.root)
