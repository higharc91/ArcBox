const fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
 const root=process.env.ARCBOX_REVIEW_ROOT||'/workspace/ArcBox-art-maps';
 const base=path.join(root,'content/tactics/base'),out=path.join(root,'docs/superpowers/reports/2026-11-13-story-map-art');
 const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});
 const results=[];
 for(const width of [320,390]) for(const id of ['forest_ruins','warlord_bridge','crystal_cave','rime_tower']){
  const page=await browser.newPage({viewport:{width,height:844},reducedMotion:'reduce'}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/review-pack/**',r=>{
   const rel=new URL(r.request().url()).pathname.split('/review-pack/')[1],file=path.resolve(base,rel);
   if(!file.startsWith(base+'/'))throw Error('Invalid pack path');
   return r.fulfill({body:fs.readFileSync(file),contentType:'application/json'});
  });
  await page.goto('http://127.0.0.1:4178/evidence-map.html?map='+id);
  await page.waitForSelector('.tx-cell');
  const plate=await page.locator('.tx-cell-label').first().evaluate(e=>getComputedStyle(e).backgroundColor);
  if(plate!=='rgb(11, 13, 28)')throw Error('Missing production tactics theme');
  const count=await page.locator('.tx-cell').count();
  if(count!==168)throw Error('Map did not load all tiles');
  await page.getByRole('button',{name:'Enlarge map',exact:true}).click();
  await page.waitForSelector('.tx-map-pan');
  const cells=page.locator('.tx-map-pan .tx-cell');
  await cells.first().click();
  const detail=await page.locator('.tx-sheet .tx-tile-details').allTextContents();
  const minCell=await cells.evaluateAll(es=>Math.min(...es.map(e=>e.getBoundingClientRect().width)));
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
  if(minCell<44||overflow||errors.length)throw Error(JSON.stringify({width,id,minCell,overflow,errors}));
  if(id==='forest_ruins'&&!detail.join(' ').includes('Water'))throw Error('Pool water flag missing');
  await page.screenshot({path:path.join(out,`phone-${width}-${id}.png`),fullPage:true});
  results.push({width,height:844,map:id,labelPlate:plate,tiles:count,enlargedMinCell:minCell,overflow,errors,selectedTile:detail,reducedMotion:true,scope:'Production BattleMap component and branch map JSON; review harness, not live multiplayer'});
  await page.close();
 }
 await browser.close();fs.writeFileSync(path.join(out,'browser.json'),JSON.stringify(results,null,2)+'\n');
})().catch(e=>{console.error(e);process.exit(1)});
