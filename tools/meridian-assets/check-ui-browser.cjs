const {chromium}=require('playwright');
const fs=require('fs'),path=require('path');
(async()=>{
 const report=path.resolve(__dirname,'../../docs/superpowers/reports/meridian-art/ui');
 const origin=process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4187';
 const url=origin+'/docs/superpowers/reports/meridian-art/ui/index.html';
 const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});
 const results=[];const phases=['boarding','explore','breach','endgame','debrief'];
 async function load(page){await page.goto(url);await page.evaluate(()=>document.fonts.ready);await page.waitForFunction(()=>document.images.length>50&&[...document.images].every(i=>i.complete&&i.naturalWidth));}
 for(const width of [320,390,1920]){
  const page=await browser.newPage({viewport:{width,height:width===1920?1080:844},reducedMotion:'reduce'});const errors=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('requestfailed',r=>errors.push(r.url()));page.on('response',r=>{if(r.status()>=400)errors.push(r.status()+' '+r.url())});
  await load(page);
  const state=await page.evaluate(()=>({images:document.images.length,phases:[...document.querySelectorAll('.stage')].map(s=>s.dataset.phase),pageOverflow:document.documentElement.scrollWidth>innerWidth,
    imageScales:[...new Set([...document.images].map(i=>i.width/i.naturalWidth))],
    clipped:[...document.querySelectorAll('.stage,.surface,.phone-device,.lobby-seat')].filter(e=>e.scrollWidth>e.clientWidth+1||(e.classList.contains('stage')||e.classList.contains('surface'))&&e.scrollHeight>e.clientHeight+1).map(e=>({piece:e.dataset.piece,phase:e.dataset.phase,class:e.className,size:[e.clientWidth,e.clientHeight],scroll:[e.scrollWidth,e.scrollHeight]})),
    leakedRole:[...document.querySelectorAll('.stage:not([data-phase=debrief])')].some(s=>s.textContent.includes('The Mimic')),roles:document.querySelectorAll('.role-reveal').length,awards:document.querySelectorAll('.award').length,
    prematureClock:!!document.querySelector('[data-phase=explore] .round-clock'),autoPaths:[...new Set([...document.querySelectorAll('.auto img')].map(i=>i.getAttribute('src')))]}));
  if(state.phases.join()!=phases.join()||state.pageOverflow||state.clipped.length||state.imageScales.some(s=>![1,2].includes(s))||state.leakedRole||state.roles!==6||state.awards!==3||state.prematureClock||state.autoPaths.length!==1)throw Error(JSON.stringify({width,...state}));
  const reduced=await page.locator('.rolling').first().evaluate(e=>({frame:e.dataset.frame,url:e.style.backgroundImage}));
  if(reduced.frame!=='0'||!reduced.url.includes('die-face0'))throw Error('Reduced motion does not settle');
  await page.emulateMedia({reducedMotion:'no-preference'});
  const seen=new Set();for(let i=0;i<8;i++){seen.add(await page.locator('.rolling').first().getAttribute('data-frame'));await page.waitForTimeout(55)}
  if(seen.size<2)throw Error('Roll does not advance');
  await page.locator('#pause').click();const held=await page.locator('.rolling').first().getAttribute('data-frame');await page.waitForTimeout(200);if(held!==await page.locator('.rolling').first().getAttribute('data-frame'))throw Error('Pause moved');
  await page.locator('#still').check();if(!await page.locator('#pause').isDisabled())throw Error('Still leaves motion enabled');
  await page.locator('#ready').click();if(await page.locator('#ready').getAttribute('aria-pressed')!=='true')throw Error('Ready failed');
  await page.locator('#ready').click();await page.evaluate(()=>scrollTo(0,0));
  await page.screenshot({path:report+'/browser-'+width+'.png'});
  if(errors.length)throw Error(errors.join('\n'));results.push({width,...state,errors,motionFramesSeen:seen.size,pause:true,still:true,reduced:true,ready:true});await page.close();
 }
 const geometry={};
 for(const phase of phases){
  const page=await browser.newPage({viewport:{width:1920,height:1080},reducedMotion:'reduce'});await load(page);
  await page.evaluate(p=>{const stage=document.querySelector('[data-phase='+p+']');document.body.replaceChildren(stage);Object.assign(document.body.style,{padding:'0',margin:'0',maxWidth:'none'});},phase);
  await page.evaluate(()=>document.fonts.ready);await page.waitForFunction(()=>[...document.images].every(i=>i.complete&&i.naturalWidth));
  geometry[phase]=await page.evaluate(()=>[...document.querySelectorAll('.surface')].map(e=>{const b=e.getBoundingClientRect();return {piece:e.dataset.piece,x:b.x,y:b.y,width:b.width,height:b.height}}));
  await page.screenshot({path:report+'/'+phase+'-tv.png'});await page.close();
 }
 for(const width of [320,390]){
  const page=await browser.newPage({viewport:{width,height:844},reducedMotion:'reduce'});await load(page);
  await page.evaluate(()=>{const phone=document.querySelector('.phone-device');document.body.replaceChildren(phone);Object.assign(document.body.style,{padding:'0',margin:'0',maxWidth:'none'});});
  await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:report+'/phone-'+width+'.png'});await page.close();
 }
 fs.writeFileSync(report+'/browser.json',JSON.stringify(results,null,2)+'\n');fs.writeFileSync(report+'/geometry.json',JSON.stringify(geometry,null,2)+'\n');await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
