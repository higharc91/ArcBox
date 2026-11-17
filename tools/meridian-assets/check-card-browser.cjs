const {chromium}=require('playwright');
const fs=require('fs'),path=require('path');
(async()=>{
 const report=path.resolve(__dirname,'../../docs/superpowers/reports/meridian-art/cards');
 const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});
 const origin=process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4186';const rows=[];
 for(const width of [320,390,1920]){
  const page=await browser.newPage({viewport:{width,height:width===1920?1080:844},reducedMotion:'reduce'});const errors=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('requestfailed',r=>errors.push(r.url()+': '+r.failure()?.errorText));
  await page.goto(origin+'/docs/superpowers/reports/meridian-art/cards/index.html');await page.evaluate(()=>document.fonts.ready);
  await page.waitForFunction(()=>document.images.length>100&&[...document.images].every(e=>e.complete&&e.naturalWidth));
  const state=await page.evaluate(()=>({images:document.images.length,cards:document.querySelectorAll('article').length,
    pageOverflow:document.documentElement.scrollWidth>innerWidth,
    imageScales:[...new Set([...document.images].map(e=>e.width/e.naturalWidth))],
    clippedText:[...document.querySelectorAll('.card')].filter(e=>e.scrollWidth>e.clientWidth+1||e.scrollHeight>e.clientHeight+1).map(e=>({id:e.dataset.card,class:e.className,client:[e.clientWidth,e.clientHeight],scroll:[e.scrollWidth,e.scrollHeight]})),
    tvFonts:[...new Set([...document.querySelectorAll('.card.tv')].map(e=>getComputedStyle(e).fontSize))],
    choiceTitle:document.querySelector('.tv-long-stage .card h3').textContent}));
  if(state.cards!==32||state.pageOverflow||state.clippedText.length||state.imageScales.some(s=>![1,2,3].includes(s))||state.tvFonts.some(s=>s!=='30px'))throw Error(JSON.stringify({width,state}));
  for(const kind of ['incident','salvage','anomaly']){
   await page.selectOption('#kind',kind);
   if(await page.locator(`article:not([hidden]):not([data-kind="${kind}"])`).count())throw Error('Kind filter leaked');
  }
  await page.selectOption('#kind','all');
  for(const kind of ['incident','salvage','anomaly']){
    await page.locator(`article[data-kind="${kind}"] .card.phone`).first().screenshot({path:report+`/phone-${kind}-${width}.png`});
  }
  if(width===1920){
   // Detach each fixed-size review from its deliberately scrollable phone wrapper.
   // Locator screenshots otherwise retain ancestor clipping and capture adjacent content.
   for(const [selector,file] of [['.tv-stage','cards-tv'],['.tv-long-stage','choice-tv'],['.icon-stage','icons-tv']]){
    const capture=await browser.newPage({viewport:{width:1920,height:1080},reducedMotion:'reduce'});
    await capture.goto(origin+'/docs/superpowers/reports/meridian-art/cards/index.html');
    await capture.evaluate(sel=>{const stage=document.querySelector(sel);document.body.replaceChildren(stage);Object.assign(document.body.style,{margin:'0',padding:'0',maxWidth:'none'});},selector);
    await capture.evaluate(()=>document.fonts.ready);
    await capture.waitForFunction(()=>[...document.images].every(e=>e.complete&&e.naturalWidth));
    const bounds=await capture.locator(selector).boundingBox();
    if(bounds.x!==0||bounds.y!==0||bounds.width!==1920||bounds.height!==1080)throw Error('Incorrect TV capture bounds');
    await capture.screenshot({path:report+'/'+file+'.png'});await capture.close();
   }
  }
  await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:report+'/browser-'+width+'.png'});
  if(errors.length)throw Error(errors.join('\n'));
  rows.push({width,...state,errors});await page.close();
 }
 await browser.close();fs.writeFileSync(report+'/browser.json',JSON.stringify(rows,null,2)+'\n');
})().catch(e=>{console.error(e);process.exit(1)});
