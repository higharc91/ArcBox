const {chromium}=require('playwright');
const fs=require('fs'),path=require('path');
(async()=>{
 const root=path.resolve(__dirname,'../../docs/superpowers/reports/meridian-art/tiles');
 const origin=process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4184';
 const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});
 const results=[];
 for(const width of [320,390,1920]){
  const page=await browser.newPage({viewport:{width,height:width===1920?1080:844},reducedMotion:'reduce'}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('requestfailed',r=>errors.push(r.url()+': '+r.failure()?.errorText));
  await page.goto(origin+'/docs/superpowers/reports/meridian-art/tiles/index.html');
  await page.waitForFunction(()=>document.images.length>460&&[...document.images].every(i=>i.complete&&i.naturalWidth>0));
  const state=await page.evaluate(()=>({images:document.images.length,rooms:document.querySelectorAll('article').length,
   overflow:document.documentElement.scrollWidth>innerWidth,
   scales:[...document.querySelectorAll('article img')].map(i=>i.width/i.naturalWidth),
   animations:[...document.querySelectorAll('.clip')].map(e=>({name:getComputedStyle(e).animationName,position:getComputedStyle(e).backgroundPositionX,last:e.style.getPropertyValue('--last')})),
   still:document.querySelector('#still').checked}));
  if(state.overflow||state.rooms!==57||errors.length||!state.still||state.scales.some(x=>![1,2].includes(x))||state.animations.some(x=>x.name!=='none'||x.position!==x.last)) throw Error(JSON.stringify({width,state,errors}));
  for(const deck of ['command','habitat','engineering']){
   await page.selectOption('#deck',deck);
   if(await page.locator(`article:not([hidden]):not([data-deck="${deck}"])`).count())throw Error('Deck filter leaked another deck');
  }
  await page.selectOption('#deck','all');
  if(await page.locator('article:not([hidden])').count()!==57)throw Error('All filter lost variants');
  await page.screenshot({path:root+'/browser-'+width+'.png'});
  results.push({width,...state,errors});await page.close();
 }
 const page=await browser.newPage({viewport:{width:1920,height:1080},reducedMotion:'no-preference'});
 await page.goto(origin+'/docs/superpowers/reports/meridian-art/tiles/index.html');
 const clip=page.locator('.clip').first();
 if(await clip.evaluate(e=>getComputedStyle(e).animationName)!=='play')throw Error('Normal animation missing');
 const initial=await clip.evaluate(e=>getComputedStyle(e).backgroundPositionX);
 await page.waitForTimeout(180);
 if(await clip.evaluate(e=>getComputedStyle(e).backgroundPositionX)===initial)throw Error('Frame strip did not advance');
 await page.getByRole('button',{name:'Pause previews'}).click();
 if(await clip.evaluate(e=>getComputedStyle(e).animationPlayState)!=='paused')throw Error('Pause failed');
 await page.getByLabel('Still previews').check();
 if(await clip.evaluate(e=>getComputedStyle(e).animationName)!=='none')throw Error('Still preview failed');
 results.push({normalMotionAdvances:true,pauseStops:true,stillShowsRestState:true});
 await page.close();await browser.close();
 fs.writeFileSync(root+'/browser.json',JSON.stringify(results,null,2)+'\n');
})().catch(e=>{console.error(e);process.exit(1)});
