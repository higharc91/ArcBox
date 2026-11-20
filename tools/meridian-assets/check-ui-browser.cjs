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
  const lobby=await page.locator('.phone-device').evaluate(e=>({seats:[...e.querySelectorAll('.seat-meta>span:first-child')].map(n=>n.textContent),portraits:e.querySelectorAll('.lobby-portrait img').length,you:e.querySelectorAll('.you-tag').length,bots:[...e.querySelectorAll('.bot-tag')].map(n=>n.textContent),abstractBadges:e.querySelectorAll('.auto,img[src*="ui/seat-"]').length}));
  if(lobby.seats.join()!=='Seat 1,Seat 2,Seat 3,Seat 4,Seat 5,Seat 6'||lobby.portraits!==6||lobby.you!==1||lobby.bots.join()!=='Bot'||lobby.abstractBadges)throw Error('Lobby identity unclear: '+JSON.stringify(lobby));
  await page.locator('#ready').click();
  if(await page.locator('#ready').getAttribute('aria-pressed')!=='true'||await page.locator('#your-status').textContent()!=='Ready'||await page.locator('#ready-count').textContent()!=='4 of 6 ready'||!(await page.locator('#ready-status').textContent()).includes('Waiting for 2 others'))throw Error('Ready state disagrees');
  await page.locator('#ready').focus();await page.keyboard.press('Space');
  if(await page.locator('#ready').getAttribute('aria-pressed')!=='false'||await page.locator('#your-status').textContent()!=='Not ready'||await page.locator('#ready-count').textContent()!=='3 of 6 ready')throw Error('Keyboard undo failed');
  await page.evaluate(()=>scrollTo(0,0));
  await page.screenshot({path:report+'/browser-'+width+'.png'});
  if(errors.length)throw Error(errors.join('\n'));results.push({width,...state,errors,motionFramesSeen:seen.size,pause:true,still:true,reduced:true,ready:true,keyboardUndo:true,lobby});await page.close();
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
  await page.evaluate(()=>document.fonts.ready);
  const fit=await page.locator('.phone-device').evaluate(e=>({width:e.scrollWidth<=e.clientWidth,height:e.scrollHeight<=e.clientHeight+1,buttonHeight:e.querySelector('#ready').getBoundingClientRect().height,clippedNames:[...e.querySelectorAll('.lobby-person strong')].filter(n=>n.scrollWidth>n.clientWidth+1).length}));
  if(!fit.width||!fit.height||fit.buttonHeight<44||fit.clippedNames)throw Error('Phone does not fit: '+JSON.stringify({width,...fit}));
  await page.screenshot({path:report+'/phone-'+width+'.png'});
  await page.locator('#ready').click();if(!await page.locator('#ready-status').textContent().then(t=>t.includes('Tap again to cancel.')))throw Error('Missing undo explanation');if(await page.locator('.phone-device').evaluate(e=>e.scrollHeight>e.clientHeight+1))throw Error('Ready state no longer fits');await page.screenshot({path:report+'/phone-'+width+'-ready.png'});await page.close();
 }
 fs.writeFileSync(report+'/browser.json',JSON.stringify(results,null,2)+'\n');fs.writeFileSync(report+'/geometry.json',JSON.stringify(geometry,null,2)+'\n');await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
