"""Original card plates, matching kind glyphs and twenty authored item silhouettes."""
from __future__ import annotations
import argparse,hashlib,html,json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import build as style
import compartments

ROOT=style.ROOT
REFERENCE=Path(__file__).parent/'reference/cards'
RULES_HEAD='00d4a52bb9c7e73a5ee72b117b91891e6c0cdd11'
KINDS={'incident':'incidents','salvage':'salvage','anomaly':'anomalies'}
RECIPES={'crowbar','med_kit','calm_tabs','mag_boots','diagnostic_slate','flare_gun','adrenal_stim','hand_lamp',
 'humming_shard','black_mirror','warm_egg','stopped_watch','unknown_chart','singing_wire','bone_key','sealed_vial','drowned_logbook','childs_drawing','cold_coin','pocket_of_quiet'}
INSETS=[12,12,12,12]


def catalogue(reference=REFERENCE):
    return {kind:json.loads((reference/(name+'.json')).read_text())['cards'] for kind,name in KINDS.items()}


def frame(kind):
    c=style.canvas(64,64)
    for i,slot,shade in [(0,'hull',0),(1,'hull',1),(2,'hull',4),(4,'hull',2),(6,'hull',0),(8,'floor',0)]:
        c.rect(i,i,63-i,63-i,slot,shade)
    # Only corners carry motifs. Every stretchable edge is uniform along its length.
    corner=style.canvas(12,12)
    if kind=='incident':
        corner.polygon([(2,2),(10,2),(2,10)],'warning',4)
        corner.thick_line(3,8,8,3,2,'hull',0)
    elif kind=='salvage':
        corner.rect(3,3,10,5,'linen',3);corner.rect(3,3,5,10,'linen',3)
        corner.rect(7,7,10,10,'engineering',1);corner.rect(8,8,9,9,'warning',4)
    elif kind=='anomaly':
        corner.polygon([(6,1),(11,6),(6,11),(1,6)],'anomaly',3)
        corner.polygon([(6,4),(8,6),(6,8),(4,6)],'hull',0)
        corner.rect(8,2,10,3,'anomaly',5)
    else:raise ValueError(kind)
    image=c.render(style.PALETTE);patch=corner.render(style.PALETTE)
    for turn,xy in enumerate([(0,0),(0,52),(52,52),(52,0)]):image.alpha_composite(patch.rotate(turn*90),xy)
    return image


def glyph(kind,size):
    if size==11:return compartments.symbol(kind)
    c=style.canvas(size,size);m=size//2;thick=2 if size==16 else 3
    if kind=='incident':
        c.polygon([(m,1),(size-2,size-2),(1,size-2)],'warning',4)
        c.rect(m-1,size//3,m+1,size*2//3,'hull',0)
        c.rect(m-1,size-5,m+1,size-3,'hull',0)
    elif kind=='salvage':
        c.rect(1,size//3,size-2,size-3,'linen',4)
        c.rect(size//3,1,size*2//3,thick,'linen',4)
        c.rect(size//3-1,1,size//3,size//3,'linen',4)
        c.rect(size*2//3,1,size*2//3+1,size//3,'linen',4)
        c.line(1,size//2+1,size-2,size//2+1,'hull',0)
        c.rect(m-1,size//2-1,m+1,size//2+3,'linen',5)
    elif kind=='anomaly':
        c.polygon([(m,1),(size-2,m),(m,size-2),(1,m)],'anomaly',4)
        c.polygon([(m,1+thick),(size-2-thick,m),(m,size-2-thick),(1+thick,m)],'hull',0)
        c.rect(m-1,m-1,m+1,m+1,'anomaly',5)
    else:raise ValueError(kind)
    return c.render(style.PALETTE)


def icon(key):
    c=style.canvas(24,24)
    if key=='crowbar':
        c.thick_line(5,20,16,7,5,'hull',0);c.thick_line(5,20,16,7,3,'hull',4)
        c.thick_line(16,7,20,5,3,'hull',3);c.line(18,5,20,4,'hull',5)
        c.thick_line(7,16,10,12,3,'red',2)
    elif key=='med_kit':
        c.rect(8,3,16,6,'hull',0);c.rect(10,4,14,6,'linen',3)
        c.polygon([(4,7),(20,7),(22,9),(22,19),(20,21),(4,21),(2,19),(2,9)],'hull',0)
        c.rect(4,8,20,19,'linen',3);c.rect(5,9,19,10,'linen',5)
        c.rect(10,10,14,18,'red',3);c.rect(7,13,17,15,'red',3)
    elif key=='calm_tabs':
        c.rect(7,5,17,21,'hull',0);c.rect(8,7,16,20,'coolant',2)
        c.rect(6,2,18,6,'linen',3);c.line(8,3,16,3,'linen',5)
        c.rect(9,10,15,17,'linen',4);c.line(11,12,13,15,'coolant',1)
        c.ellipse(4,18,2,3,'linen',4)
    elif key=='mag_boots':
        for x,y in [(3,3),(13,5)]:
            c.polygon([(x,y),(x+6,y),(x+6,y+10),(x+9,y+12),(x+9,y+15),(x,y+15)],'hull',0)
            c.rect(x+1,y+1,x+5,y+10,'hull',3)
            c.rect(x+1,y+12,x+8,y+14,'warning',3)
            c.line(x+1,y+14,x+8,y+14,'hull',5)
    elif key=='diagnostic_slate':
        c.rect(3,2,20,21,'hull',0);c.rect(4,3,19,20,'hull',3)
        c.rect(6,5,17,16,'coolant',0)
        for a,b in [((6,12),(9,8)),((9,8),(12,14)),((12,14),(15,10)),((15,10),(17,10))]:c.thick_line(*a,*b,2,'coolant',4)
        c.rect(7,18,9,19,'warning',4);c.rect(13,18,16,19,'hull',1)
    elif key=='flare_gun':
        c.polygon([(2,7),(17,4),(21,6),(21,11),(12,13),(15,18),(11,21),(6,14),(2,14)],'hull',0)
        c.polygon([(3,8),(18,6),(20,7),(20,10),(10,12),(7,13),(3,12)],'warning',3)
        c.line(5,8,17,6,'warning',5);c.rect(18,7,20,10,'red',3)
        c.thick_line(8,13,12,19,3,'hull',2)
    elif key=='adrenal_stim':
        c.thick_line(6,18,17,5,6,'hull',0);c.thick_line(7,17,16,6,4,'linen',3)
        c.thick_line(14,6,17,9,3,'red',3);c.thick_line(5,20,7,17,2,'hull',4)
        c.line(10,12,13,9,'coolant',3)
    elif key=='hand_lamp':
        c.polygon([(4,9),(14,4),(20,8),(20,17),(13,20),(4,17)],'hull',0)
        c.rect(3,14,7,21,'hull',3);c.rect(4,16,6,19,'warning',3)
        c.ellipse(14,11,7,7,'hull',4);c.ellipse(14,11,5,5,'coolant',2)
        c.ellipse(14,11,3,3,'linen',5)
    elif key=='humming_shard':
        c.polygon([(13,2),(18,8),(16,20),(9,22),(5,12)],'anomaly',0)
        c.polygon([(12,4),(16,9),(14,19),(10,20),(7,12)],'anomaly',3)
        c.polygon([(12,4),(12,16),(9,20),(7,12)],'anomaly',5)
        c.line(2,7,2,11,'coolant',3);c.line(21,13,21,17,'coolant',3)
    elif key=='black_mirror':
        c.rect(10,17,13,21,'hull',3)
        c.ellipse(12,10,8,9,'anomaly',3);c.ellipse(12,10,6,7,'hull',0)
        c.line(8,8,10,5,'hull',3);c.line(11,15,16,10,'hull',1)
        c.rect(12,9,14,12,'coolant',1)
    elif key=='warm_egg':
        c.ellipse(12,12,8,10,'linen',1);c.ellipse(12,12,6,9,'linen',3)
        c.ellipse(10,9,3,5,'linen',4)
        c.line(13,5,11,9,'anomaly',1);c.line(11,9,14,12,'anomaly',1)
        c.line(14,12,12,17,'anomaly',1);c.line(12,17,14,20,'anomaly',1)
    elif key=='stopped_watch':
        c.rect(10,2,14,5,'hull',4)
        c.ellipse(12,13,9,9,'engineering',1);c.ellipse(12,13,7,7,'linen',3)
        c.line(12,13,12,7,'hull',0);c.line(10,13,10,8,'hull',0)
        c.rect(11,12,13,14,'hull',1)
        for x,y in [(6,13),(18,13),(12,19)]:c.px(x,y,'hull',1)
    elif key=='unknown_chart':
        c.polygon([(2,4),(8,2),(15,5),(21,3),(21,20),(15,22),(8,19),(2,21)],'linen',1)
        c.polygon([(3,5),(8,4),(15,7),(20,5),(20,19),(15,20),(8,17),(3,19)],'linen',3)
        c.line(8,4,8,17,'linen',1);c.line(15,7,15,20,'linen',1)
        for x,y in [(5,9),(11,13),(18,10)]:c.rect(x-1,y-1,x+1,y+1,'command',2)
        c.line(5,9,11,13,'command',1);c.line(11,13,18,10,'command',1)
    elif key=='singing_wire':
        for r,slot,shade in [(9,'hull',0),(8,'anomaly',4),(6,'hull',0),(5,'anomaly',3),(3,'hull',0)]:c.ellipse(11,13,r,r,slot,shade)
        c.line(18,14,21,8,'anomaly',4);c.line(21,8,18,5,'anomaly',3)
        c.line(3,2,3,5,'coolant',4);c.line(6,1,6,4,'coolant',3)
    elif key=='bone_key':
        c.thick_line(10,10,19,19,5,'linen',1);c.thick_line(10,10,19,19,3,'linen',4)
        c.ellipse(8,8,6,6,'linen',3);c.ellipse(8,8,3,3,'hull',0)
        c.thick_line(15,15,18,12,3,'linen',3);c.thick_line(18,18,21,15,3,'linen',3)
    elif key=='sealed_vial':
        c.polygon([(8,6),(16,6),(16,10),(19,18),(17,22),(7,22),(5,18),(8,10)],'hull',0)
        c.polygon([(9,7),(15,7),(15,11),(17,18),(16,20),(8,20),(7,18),(9,11)],'coolant',2)
        c.polygon([(8,15),(15,12),(17,18),(15,20),(8,20)],'linen',3)
        c.rect(7,2,17,6,'hull',3);c.line(9,3,15,3,'hull',5)
    elif key=='drowned_logbook':
        c.polygon([(3,3),(18,2),(20,18),(17,20),(4,21)],'hull',0)
        c.polygon([(4,4),(17,3),(18,17),(5,19)],'linen',3)
        c.rect(4,4,6,19,'engineering',2)
        for y in [7,11,15]:c.line(9,y,15,y-1,'hull',2)
        c.ellipse(15,15,3,4,'coolant',2);c.line(9,21,9,22,'coolant',4)
    elif key=='childs_drawing':
        c.polygon([(3,2),(16,2),(21,7),(21,21),(3,21)],'linen',1)
        c.polygon([(4,3),(15,3),(15,8),(20,8),(20,20),(4,20)],'linen',4)
        c.polygon([(8,13),(17,13),(15,17),(9,17)],'command',2)
        c.line(11,12,16,10,'command',2);c.rect(12,12,14,14,'linen',5)
        c.ellipse(7,10,2,2,'red',3);c.line(7,12,7,17,'red',3)
        c.line(7,13,5,10,'red',3);c.line(7,13,9,11,'red',3)
    elif key=='cold_coin':
        c.ellipse(12,12,10,10,'hull',1);c.ellipse(12,12,8,8,'linen',3)
        c.line(12,5,12,19,'hull',2)
        c.polygon([(8,7),(10,9),(10,12),(8,14),(10,17),(6,17),(5,13),(6,9)],'hull',2)
        c.polygon([(16,6),(19,10),(18,15),(15,18),(14,15),(16,12),(14,9)],'engineering',1)
    elif key=='pocket_of_quiet':
        c.polygon([(5,3),(20,3),(18,10),(20,21),(5,21),(7,10)],'hull',0)
        c.polygon([(7,5),(18,5),(16,11),(18,19),(7,19),(9,11)],'anomaly',2)
        c.rect(5,3,20,5,'linen',3);c.line(7,7,18,7,'hull',1)
        for x,y in [(10,12),(13,11),(16,10)]:c.rect(x,y,x+1,16,'hull',0)
        c.thick_line(9,18,17,10,2,'linen',4)
    else:raise ValueError('No authored icon for '+key)
    c.outline();return c.render(style.PALETTE)


def effect_text(e):
    k=e['kind'];plural=lambda n,word:word if n==1 else word+'s'
    if k in ['gain','lose']:return f'{k.title()} {e["steps"]} {e["traits"]} {plural(e["steps"],"step")}.'
    if k=='extraDice':return f'+{e["dice"]} {e["trait"].title()} '+('die.' if e['dice']==1 else 'dice.')
    if k=='moves':return f'Gain {e["delta"]} '+plural(e['delta'],'move')+'.'
    if k=='draw':return f'Draw a {e["card"].title()} card.'
    if k=='endTurn':return 'End your turn.'
    if k=='moveTo':
        names={r['id']:r['name'] for r in compartments.rules()}
        return 'Move to '+names[e['tile']]+'.'
    raise ValueError('Unsupported card effect: '+k)


def details(card):
    parts=[]
    if card.get('item'):
        item=card['item']
        for e in item.get('passive',[]):parts.append(('Passive',effect_text(e)))
        if item.get('use'):
            use=item['use'];text=' '.join(effect_text(e) for e in use['effects'])
            parts.append(('Use: '+use['label'],text+(' Single use.' if use.get('single') else '')))
    if card.get('check'):
        check=card['check'];parts.append(('Roll '+check['trait'].title(),''))
        for band in check['bands']:
            score=str(band['min'])+'+' if band['max'] is None else str(band['min']) if band['min']==band['max'] else f'{band["min"]}–{band["max"]}'
            parts.append((score,band['text']+' '+ ' '.join(effect_text(e) for e in band.get('effects',[]))))
    if card.get('choice'):
        for option in card['choice']['options']:
            parts.append((option['label'],option['text']))
            parts.extend(details(option))
            if option.get('effects'):parts.append(('Result',' '.join(effect_text(e) for e in option['effects'])))
    return parts


def build(root=ROOT,reference=REFERENCE):
    data=catalogue(reference)
    ids=[c['id'] for records in data.values() for c in records]
    if len(ids)!=len(set(ids)):raise ValueError('Duplicate card IDs')
    items={c['id']:kind for kind,cards in data.items() if kind!='incident' for c in cards}
    if set(items)!=RECIPES:raise ValueError('Authored icon coverage differs: missing='+str(sorted(set(items)-RECIPES))+', obsolete='+str(sorted(RECIPES-set(items))))
    pack=root/'content/meridian/base';review=root/'docs/superpowers/reports/meridian-art/cards';review.mkdir(parents=True,exist_ok=True)
    common={'version':1,'issue':334,'status':'awaiting-set-review','rulesReference':{'pr':316,'head':RULES_HEAD,'files':{name+'.json':hashlib.sha256((reference/(name+'.json')).read_bytes()).hexdigest() for name in KINDS.values()}}}
    plates={k:{**style.immutable(pack,'cards','frame-'+k,frame(k)),'frameSize':[64,64],'slice':INSETS,'contentInset':[16,16,16,16],'center':'solid stretch','edges':'uniform stretch','cornerScale':'whole integer nearest'} for k in KINDS}
    symbols={k:{str(size):{**style.immutable(pack,'icons','symbol-'+k+'-'+str(size),glyph(k,size)),'frameSize':[size,size]} for size in [16,24]} for k in KINDS}
    icons={key:{**style.immutable(pack,'icons',key,icon(key)),'kind':kind,'frameSize':[24,24]} for key,kind in items.items()}
    entries={c['id']:{'kind':kind,'frameKind':kind,'icon':c['id'] if kind!='incident' else None} for kind,records in data.items() for c in records}
    style.write_json(pack/'cards/manifest.json',{**common,'frames':plates,'cards':entries})
    style.write_json(pack/'icons/manifest.json',{**common,'symbols':symbols,'icons':icons})
    review_page(review,data,plates,symbols,icons)
    return pack,review


def review_page(review,data,plates,symbols,icons):
    prefix='../../../../../content/meridian/base/'
    def card_html(kind,c,scale):
        glyph_url=prefix+'icons/'+symbols[kind]['24' if scale=='tv' else '16']['file']
        item=icons.get(c['id']);image=f'<img class="item" alt="{html.escape(c["title"])} icon" src="{prefix}icons/{item["file"]}" width="{72 if scale=="tv" else 48}" height="{72 if scale=="tv" else 48}">' if item else ''
        rules=''.join(f'<div class="rule"><strong>{html.escape(label)}</strong><p>{html.escape(text)}</p></div>' for label,text in details(c))
        return f'<section class="card {scale} {kind}" data-card="{c["id"]}" data-kind="{kind}" style="border-image-source:url({prefix}cards/{plates[kind]["file"]})"><header>{image}<div><h3>{html.escape(c["title"])}</h3><div class="kind"><img alt="" src="{glyph_url}" width="{48 if scale=="tv" else 32}" height="{48 if scale=="tv" else 32}"><span>{kind.title()}</span></div></div></header><p class="flavour">{html.escape(c["text"])}</p>{rules}</section>'
    articles=[]
    for kind,records in data.items():
        for c in records:
            articles.append(f'<article data-kind="{kind}"><h2>{html.escape(c["title"])}</h2><div class="comparisons"><div class="phone-preview"><h4>Phone</h4>{card_html(kind,c,"phone")}</div><div class="tv-preview"><h4>TV · 30px body text</h4><div class="pan" tabindex="0" aria-label="TV card at full size">{card_html(kind,c,"tv")}</div></div></div></article>')
    selected=[('incident','cold_spot'),('salvage','med_kit'),('anomaly','childs_drawing')]
    tv=''.join(card_html(kind,next(c for c in data[kind] if c['id']==key),'tv') for kind,key in selected)
    long_card=card_html('incident',next(c for c in data['incident'] if c['id']=='drifting_body'),'tv')
    icon_tiles=''.join(f'<figure><img alt="{html.escape(c["title"])}" src="{prefix}icons/{icons[c["id"]]["file"]}" width="72" height="72"><figcaption>{html.escape(c["title"])}</figcaption><span>{kind.title()}</span></figure>' for kind,records in data.items() if kind!='incident' for c in records)
    glyphs=''.join('<figure><figcaption>'+kind.title()+'</figcaption>'+''.join(f'<img alt="{kind}, {size}px source" src="{prefix}icons/{symbols[kind][str(size)]["file"]}" width="{size*2}" height="{size*2}">' for size in [16,24])+'</figure>' for kind in KINDS)
    page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian cards</title><style>
@font-face{font-family:Ship;src:url('../../../../../src/ArcBox.Host/fonts/TitilliumWeb-SemiBold.ttf')}*{box-sizing:border-box}:root{color-scheme:dark;background:#111b2a;color:#d8e1da;font:18px/1.5 Ship,system-ui}body{max-width:1320px;margin:auto;padding:16px}h1{line-height:1.15}a{color:#9ee0ed}img,.card{image-rendering:pixelated}h2{margin-bottom:12px}.card{border:24px solid transparent;border-image-slice:12 fill;border-image-width:24px;border-image-repeat:stretch;padding:16px;color:#d8e1da;flex:none}.card header{display:flex;gap:16px;align-items:start}.card header>div{min-width:0}.card h3{margin:0;line-height:1.15;overflow-wrap:anywhere}.card p{margin:0 0 .55em}.card .flavour{margin-top:16px}.kind{display:flex;align-items:center;gap:8px}.kind img,.item{flex:none}.kind span{font-size:.8em}.card.phone{width:280px;max-width:100%;font-size:18px;line-height:1.45;padding:12px}.phone h3{font-size:25px}.card.tv{width:560px;font-size:30px;line-height:1.25}.tv h3{font-size:38px}.rule{margin-top:12px}.rule strong{font-weight:bold}.rule p{margin:2px 0 0}.comparisons{display:flex;gap:24px;align-items:start;flex-wrap:wrap}.phone-preview{width:280px;max-width:100%}.tv-preview{flex:1;min-width:0;width:560px;max-width:100%}.pan{overflow:auto;max-height:80vh;border:1px solid #657482}article{padding:12px;border:1px solid #657482;margin:20px 0}.tv-stage{width:1920px;height:1080px;padding:32px 60px;background:#111b2a;display:flex;flex-wrap:wrap;gap:24px 60px;align-content:start;align-items:start}.tv-stage>h2{width:100%;font-size:32px;margin:0 0 10px}.tv-stage .card{width:560px;max-height:960px}.tv-long-stage{width:1920px;height:1080px;padding:28px 60px;background:#111b2a}.tv-long-stage>h2{font-size:32px;margin:0 0 16px}.tv-long-stage .card{width:960px;max-height:960px}.icon-stage{width:1920px;height:1080px;background:#111b2a;padding:28px;display:grid;grid-template-columns:repeat(5,1fr);grid-template-rows:52px repeat(4,1fr);gap:20px}.icon-stage h2{grid-column:1/-1;margin:0;font-size:32px}.icon-stage figure{margin:0;display:flex;align-items:center;justify-content:center;flex-direction:column;text-align:center;font-size:28px;line-height:1.2}.icon-stage span{font-size:24px;color:#a2b8c0}.icon-stage img{margin-bottom:12px}.glyphs{display:flex;gap:24px;flex-wrap:wrap}.glyphs img{margin-right:16px}select{font:inherit;min-height:44px;padding:8px;background:#26384b;color:#d8e1da;border:1px solid #657482}[hidden]{display:none!important}
</style><body><h1>Meridian / card frames and item icons</h1><p>Three original nine-slice plates, matching Incident / Salvage / Anomaly symbols at 16px and 24px, and all 20 first-wave item icons at 24px. Text below comes from actual content data; the PNGs contain no titles or rules text.</p><p>Frame corners remain exact 2×; stretchable edge bands and centres are uniform colours. Larger object silhouettes carry item identity; kind names and shapes supplement colour. This is an asset review, not implemented controller/game UI or a physical-TV test.</p><h2>Kind symbols · exact 2×</h2><div class="glyphs">'''+glyphs+'''</div><h2>TV review · native 1920×1080</h2><p>Pan inside each panel on phones. TV cards use a 30px body font with 38px titles; content is live HTML text.</p><div class="pan" tabindex="0" aria-label="Full TV card review"><div class="tv-stage"><h2>DERELICT: ISV MERIDIAN / CARD REVIEW</h2>'''+tv+'''</div></div><h2>Long choice card · full live text</h2><div class="pan" tabindex="0" aria-label="Long choice card at TV size"><div class="tv-long-stage"><h2>LONG CHOICE CARD / SAME 30px FONT</h2>'''+long_card+'''</div></div><h2>All item icons · 3× at 1080p</h2><div class="pan" tabindex="0" aria-label="All item icons at TV size"><div class="icon-stage"><h2>SALVAGE / ANOMALY / TWENTY AUTHORED OBJECTS</h2>'''+icon_tiles+'''</div></div><h2>Every real card at phone and TV sizes</h2><label>Kind <select id="kind"><option value="all">All kinds</option><option>incident</option><option>salvage</option><option>anomaly</option></select></label>'''+''.join(articles)+'''<script>document.querySelector('#kind').onchange=e=>document.querySelectorAll('article').forEach(a=>a.hidden=e.target.value!=='all'&&a.dataset.kind!==e.target.value);</script></body></html>'''
    (review/'index.html').write_text(page)
    # Pixel-only icon and symbol contact sheets; report text is separate from the runtime PNGs.
    sheet=Image.new('RGBA',(800,620),style.INK);draw=ImageDraw.Draw(sheet);draw.fontmode='1';font=ImageFont.load_default()
    draw.text((12,8),'MERIDIAN / SALVAGE + ANOMALY / 3x ICON DETAIL',font=font,fill=style.PAPER)
    index=0
    for kind,records in data.items():
        if kind=='incident':continue
        for c in records:
            x=12+(index%5)*158;y=40+(index//5)*140
            sheet.alpha_composite(icon(c['id']).resize((72,72),Image.Resampling.NEAREST),(x,y))
            words=c['title'].split();lines=['']
            for word in words:
                if len(lines[-1])+len(word)>23:lines.append('')
                lines[-1]+=(' ' if lines[-1] else '')+word
            for n,line in enumerate(lines):draw.text((x,y+80+n*13),line,font=font,fill=style.PAPER)
            index+=1
    sheet.convert('RGB').save(review/'item-icons.png')
    comparison=Image.new('RGBA',(460,180),style.INK);draw=ImageDraw.Draw(comparison);draw.fontmode='1'
    draw.text((12,8),'MATCHING FLOOR / INLINE / CARD KIND SHAPES',font=font,fill=style.PAPER)
    for i,kind in enumerate(KINDS):
        x=12+i*148;draw.text((x,35),kind,font=font,fill=style.PAPER)
        for size,y in [(11,60),(16,90),(24,132)]:comparison.alpha_composite(glyph(kind,size),(x,y))
    comparison.resize((920,360),Image.Resampling.NEAREST).convert('RGB').save(review/'kind-symbols.png')


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=ROOT);parser.add_argument('--rules',type=Path,default=REFERENCE)
    args=parser.parse_args();build(args.root,args.rules)
