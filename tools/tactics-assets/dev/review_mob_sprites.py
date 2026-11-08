"""Build a faithful mob-art inventory, a single PNG, and an embedded searchable HTML review.

Uses UnitArt itself to resolve production overrides. Pillow only inspects alpha bounds;
Chromium renders the original atlases through CSS. No artwork is regenerated or retouched.
Requires dotnet, Pillow, Node and Playwright/Chromium (NODE_PATH is supported).
"""
import base64
import html
import json
import math
import subprocess
import tempfile
from collections import defaultdict
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / "content/tactics/base"
OUT = ROOT / "docs/art/mob-sprite-review"
CW, CH, COLS = 600, 350, 6
WIDTH = CW * COLS + 96


def pretty(key):
    parts = key.split(".")
    return " ".join(p.replace("_", " ").title() for p in parts[1:])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    inventory = OUT / "inventory.json"
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "tools/tactics-mob-review/MobReview.csproj"),
                    "--", str(PACK), str(inventory)], check=True)
    data = json.loads(inventory.read_text())
    used = defaultdict(list)
    for m in data["rules"]:
        used[m["key"]].append(m)
    active = [s for s in data["sprites"] if s["key"] in used]
    library = [s for s in data["sprites"] if s["key"] not in used]
    # Older combat sprites first; index numbers stay in both deliverables.
    active.sort(key=lambda s: (s["path"].startswith("creatures/"), s["key"]))
    library.sort(key=lambda s: (s["key"].split(".")[0], s["key"]))
    sources, cards, sections = {}, [], []
    atlas_ids = {}
    y = 230
    for label, entries in [("01 / COMBAT ROSTER", active), ("02 / ADDITIONAL CREATURE VARIANTS", library)]:
        sections.append((label, len(entries), y))
        y += 70
        for i, sprite in enumerate(entries):
            path = sprite["path"]
            image = Image.open(PACK / path)
            alpha = image.convert("RGBA").getchannel("A")
            if path not in atlas_ids:
                aid = len(sources)
                atlas_ids[path] = aid
                sources[str(aid)] = "data:image/png;base64," + base64.b64encode((PACK / path).read_bytes()).decode()
            aid = atlas_ids[path]
            poses = []
            for direction in ["front", "back"]:
                r = sprite[direction]
                assert r["x"] + r["width"] <= image.width and r["y"] + r["height"] <= image.height, path
                bounds = alpha.crop((r["x"], r["y"], r["x"] + r["width"], r["y"] + r["height"])).getbbox()
                assert bounds, (path, direction)
                poses.append((r, bounds))
            limit = min(192 / (b[3] - b[1]) for _, b in poses)
            limit = min(limit, *(260 / (b[2] - b[0]) for _, b in poses))
            zoom = min(6, math.floor(limit)) if limit >= 1 else limit
            names = sorted({m["name"] for m in used[sprite["key"]]})
            title = " / ".join(names) if names else pretty(sprite["key"])
            kind = "DEDICATED ATLAS" if path.startswith("creatures/") else "OLDER ATLAS"
            x, cy = 48 + i % COLS * CW, y + i // COLS * CH
            number = len(cards) + 1
            card = {"number": number, "name": title, "key": sprite["key"], "path": path,
                    "category": "combat" if names else "library", "style": kind,
                    "names": names, "ids": [m["id"] for m in used[sprite["key"]]],
                    "x": x, "y": cy, "width": CW, "height": CH,
                    "directions": sprite["directions"]}
            html_poses = []
            for direction, (r, b) in zip(["FRONT", "REAR"], poses):
                w, h = (b[2] - b[0]) * zoom, (b[3] - b[1]) * zoom
                html_poses.append(f'<div class="pose"><div class="sprite" style="width:{w}px;height:{h}px;overflow:hidden;position:relative">'
                    f'<img data-atlas="{aid}" style="position:absolute;left:-{(r["x"] + b[0]) * zoom}px;top:-{(r["y"] + b[1]) * zoom}px;width:{image.width * zoom}px;height:{image.height * zoom}px;max-width:none"></div><span>{direction}</span></div>')
            title_size = 22 if len(title) > 33 else 26
            card["markup"] = (f'<article style="left:{x}px;top:{cy}px"><header><b>{number:02}</b>'
                f'<h3 style="font-size:{title_size}px">{html.escape(title)}</h3></header>'
                f'<div class="poses">{"".join(html_poses)}</div>'
                f'<footer><span class="{"old" if kind == "OLDER ATLAS" else "new"}">{kind}</span>'
                f'<span>{sprite["directions"]} directions</span></footer><code>{html.escape(sprite["key"])}</code></article>')
            cards.append(card)
        y += math.ceil(len(entries) / COLS) * CH + 30
    height = y + 90
    metadata = [{k: v for k, v in c.items() if k != "markup"} for c in cards]
    (OUT / "cards.json").write_text(json.dumps(metadata, indent=2) + "\n")
    old_count = sum(c["category"] == "combat" and c["style"] == "OLDER ATLAS" for c in cards)
    section_html = "".join(f'<h2 style="top:{sy}px">{label}<small>{count} distinct looks</small></h2>' for label, count, sy in sections)
    raw = f'''<!doctype html><html><meta charset="utf-8"><style>
*{{box-sizing:border-box}}body{{display:flow-root;position:relative;margin:0;background:#111923;color:#eee9da;font-family:Arial,sans-serif;width:{WIDTH}px;height:{height}px}}
h1{{margin:40px 48px 12px;font-size:48px;letter-spacing:1px}}.subtitle{{margin:0 48px;font-size:24px;color:#b7c4cc;line-height:1.6}}
h2{{position:absolute;left:48px;right:48px;margin:0;font-size:26px;letter-spacing:2px;color:#e0c585}}h2 small{{font-size:20px;color:#9eafb8;float:right}}
article{{position:absolute;width:{CW-14}px;height:{CH-14}px;border:1px solid #364654;border-radius:9px;background:#1c2733;padding:15px 18px}}
header{{height:38px;display:flex;gap:12px;align-items:center}}header b{{font-size:18px;color:#8397a7}}h3{{margin:0;white-space:nowrap}}
.poses{{display:flex;height:207px;gap:20px;margin-top:3px}}.pose{{width:260px;height:207px;display:flex;align-items:center;justify-content:flex-end;flex-direction:column;position:relative}}
.sprite{{image-rendering:pixelated;background-repeat:no-repeat;flex:none}}.pose span{{font-size:11px;color:#758a98;margin-top:4px}}
footer{{display:flex;justify-content:space-between;font-size:16px;margin-top:4px;color:#9cadb9}}.old{{color:#efbd74}}.new{{color:#87d4b4}}
code{{display:block;color:#9dafbb;font-size:15px;margin-top:9px}}.end{{position:absolute;left:48px;bottom:30px;color:#a9b8c1;font-size:20px}}
</style><h1>WARDENFLAME / MOB SPRITE REVIEW</h1><p class="subtitle">{len(data['rules'])} stat blocks · {len(active)} combat appearances · {len(library)} additional creature variants · {len(cards)} total looks<br>
Current production assets · front & rear idle poses · enlarged per card for detail, not a world-size comparison<br>
Amber = older atlas · Mint = dedicated atlas · {old_count} combat looks still use older sheets · Reference numbers match the interactive catalog</p>
{section_html}{''.join(c['markup'] for c in cards)}<p class="end">Branch: mob-sprite-refresh · baseline main 50f4a0d7 · source artwork is unchanged · duplicate stat-block appearances share one card</p>
<script>const assets={json.dumps(sources)};window.ready=Promise.all([...document.querySelectorAll("img[data-atlas]")].map(im=>{{im.src=assets[im.dataset.atlas];return im.decode()}}));</script></html>'''
    node = '''const {chromium}=require('playwright');(async()=>{const b=await chromium.launch({headless:true,args:['--no-sandbox']});try{const p=await b.newPage({viewport:{width:Number(process.argv[3]),height:1000},deviceScaleFactor:1});await p.goto('file://'+process.argv[1]);await p.evaluate(()=>window.ready);await p.evaluate(()=>document.fonts.ready);const bad=await p.locator('h3').evaluateAll(es=>es.filter(e=>e.scrollWidth>e.parentElement.clientWidth-45).map(e=>e.textContent));if(bad.length)throw new Error('Overflowing labels: '+bad.join(', '));await p.screenshot({path:process.argv[2],fullPage:true});}finally{await b.close()}})().catch(e=>{console.error(e);process.exit(1)});'''
    with tempfile.TemporaryDirectory(prefix="mob-review-") as tmp:
        source = Path(tmp) / "render.html"
        source.write_text(raw)
        subprocess.run(["node", "-e", node, str(source), str(OUT / "all-mobs.png"), str(WIDTH)], check=True)
    build_browser(metadata, WIDTH, height)
    print(f"Built {len(cards)} cards: {len(active)} combat looks, {len(library)} library variants; {old_count} older combat looks.")


def build_browser(cards, width, height):
    image = "data:image/png;base64," + base64.b64encode((OUT / "all-mobs.png").read_bytes()).decode()
    data = json.dumps(cards).replace("</", "<\\/")
    page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Wardenflame · Mob sprite review</title><style>
*{box-sizing:border-box}body{background:#111923;color:#eee9da;margin:24px;font:16px system-ui}h1{font-size:28px}p{color:#b7c4cc;line-height:1.6}.controls{display:flex;gap:12px;flex-wrap:wrap;position:sticky;top:0;padding:12px 0;background:#111923;z-index:2}input,select,button,a{padding:10px;background:#1c2733;color:#eee9da;border:1px solid #536878;border-radius:6px;font:inherit}input{min-width:240px}#grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,340px),1fr));gap:12px}.card{display:block;width:100%;padding:0;aspect-ratio:600/350;background-image:var(--sheet);background-size:calc(SHEETWIDTH / 600 * 100%) auto;image-rendering:pixelated;cursor:zoom-in;border:0;border-radius:5px}dialog{background:#111923;border:1px solid #536878;color:#eee9da;max-width:95vw;width:1000px}dialog::backdrop{background:#000b}dialog .card{margin-top:16px;cursor:auto}#detail{overflow-wrap:anywhere}.count{margin:12px 0}#full{display:none;width:100%;height:auto}body.whole #full{display:block}body.whole #grid{display:none}a{text-decoration:none;display:inline-block}</style>
<h1>Wardenflame · All mob sprites</h1><p>98 distinct looks covering 72 combat stat blocks and the additional creature library. Each card shows front and rear idle poses from the production atlas. Enlarged individually for detail; sizes are not compared. Click a card to inspect it. Everything is embedded—no server or external image links.</p>
<div class="controls"><input id="search" placeholder="Search name, ID, sprite key…" aria-label="Search mobs"><select id="group"><option value="all">All 98 looks</option><option value="combat">Combat roster · 41 looks</option><option value="library">Library variants · 57 looks</option><option value="older">Older combat art · 28 looks</option></select><button id="mode">Show single contact sheet</button><a id="download" download="Wardenflame-All-Mobs.png">Download PNG</a></div><p id="count" class="count"></p><main id="grid"></main><img id="full" alt="All 98 Wardenflame mob sprites"><dialog id="dialog"><button id="close">Close</button><div id="large"></div><p id="detail"></p></dialog>
<script>const cards=CARDS,src=IMAGE;document.documentElement.style.setProperty('--sheet',`url("${src}")`);document.querySelector('#full').src=src;document.querySelector('#download').href=src;const grid=document.querySelector('#grid'),dialog=document.querySelector('#dialog');function tile(c){const e=document.createElement('button');e.className='card';e.setAttribute('aria-label',c.number+'. '+c.name);e.style.backgroundPosition=`${c.x/(SHEETWIDTH-600)*100}% ${c.y/(SHEETHEIGHT-350)*100}%`;return e}function update(){const q=document.querySelector('#search').value.toLowerCase(),g=document.querySelector('#group').value;grid.replaceChildren();const found=cards.filter(c=>(g==='all'||g===c.category||g==='older'&&c.category==='combat'&&c.style==='OLDER ATLAS')&&[c.number,c.name,c.key,...c.ids,...c.names].join(" ").toLowerCase().includes(q));for(const c of found){const e=tile(c);e.onclick=()=>{document.querySelector('#large').replaceChildren(tile(c));document.querySelector('#detail').textContent=`#${c.number} · ${c.name} · ${c.key} · ${c.directions} directions · ${c.style}. ${c.ids.length?'Stat blocks: '+c.ids.join(', '):'Creature library variant; no current combat stat block.'}`;dialog.showModal()};grid.append(e)}document.querySelector('#count').textContent=found.length+' matching looks · amber labels identify older atlases; mint labels identify dedicated atlases'}document.querySelector('#search').oninput=update;document.querySelector('#group').onchange=update;document.querySelector('#close').onclick=()=>dialog.close();document.querySelector('#mode').onclick=e=>{const whole=document.body.classList.toggle('whole');e.target.textContent=whole?'Show searchable cards':'Show single contact sheet'};update();</script></html>'''
    page = page.replace('SHEETWIDTH',str(width)).replace('SHEETHEIGHT',str(height)).replace('CARDS',data).replace('IMAGE',json.dumps(image))
    (OUT / "index.html").write_text(page)


if __name__ == "__main__":
    main()
