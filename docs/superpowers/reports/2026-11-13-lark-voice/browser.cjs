const fs=require('node:fs'),path=require('node:path');const {chromium}=require('playwright');
(async()=>{
 const root=process.env.ARCBOX_REVIEW_ROOT||'/workspace/ArcBox-art';
 const base=path.join(root,'content/tactics/base'),out=path.join(root,'docs/superpowers/reports/2026-11-13-lark-voice');
 const dialogue=JSON.parse(fs.readFileSync(path.join(out,'dialogue.json')));
 const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});const results=[];
 for(const width of [320,390]){
  const page=await browser.newPage({viewport:{width,height:844},reducedMotion:'reduce'}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/review-dialogue.json',r=>r.fulfill({json:dialogue}));
  await page.route('**/content/tactics/index.json',r=>r.fulfill({json:[{packId:'base',hash:'sound-review'}]}));
  await page.route('**/content/tactics/base/sound-review/**',r=>{
   const rel=new URL(r.request().url()).pathname.split('/sound-review/')[1],file=path.resolve(base,rel);
   if(!file.startsWith(base+'/'))throw Error('Bad pack path');
   return r.fulfill({body:fs.readFileSync(file),contentType:file.endsWith('.json')?'application/json':'image/png'});
  });
  await page.goto('http://127.0.0.1:4180/evidence-lark.html');
  await page.waitForSelector('.tx-say-text');
  await page.waitForFunction(()=>document.images.length>0&&[...document.images].every(i=>i.complete&&i.naturalWidth>0));
  await page.evaluate(()=>document.fonts.ready);
  const text=await page.locator('.tx-say-text').innerText();
  if(!text.includes(dialogue.text))throw Error('Caption mismatch');
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
  const button=page.getByRole('button',{name:'Skip line'}),box=await button.boundingBox();
  if(overflow||errors.length||!box||box.height<44)throw Error(JSON.stringify({width,overflow,errors,box}));
  await page.screenshot({path:path.join(out,`phone-${width}.png`),fullPage:true});
  await page.waitForTimeout(650);
  await page.mouse.click(box.x+box.width/2,box.y+box.height/2);
  await page.mouse.click(box.x+box.width/2,box.y+box.height/2);
  const actions=await page.evaluate(()=>window.reviewActions);
  if(actions.length!==1||actions[0].action!=='skip'||actions[0].args.line!==dialogue.line)throw Error('Skip was not exactly once for the intended line');
  results.push({width,height:844,overflow,pageErrors:errors,captionMatches:true,portraitLoaded:true,skipActions:actions,skipButtonHeight:box.height,reducedMotion:true,scope:'Vite production Dialogue component harness and actual branch pack; no live multiplayer or physical-device test'});
  await page.close();
 }
 await browser.close();fs.writeFileSync(path.join(out,'browser.json'),JSON.stringify(results,null,2)+'\n');
})().catch(e=>{console.error(e);process.exit(1)});
