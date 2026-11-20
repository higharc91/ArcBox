const {chromium}=require('playwright');
const fs=require('fs'),path=require('path');
(async()=>{
 const report=path.resolve(__dirname,'../../docs/superpowers/reports/meridian-art/crew');
 const origin=process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4185';
 const url=origin+'/docs/superpowers/reports/meridian-art/crew/index.html';
 const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});const results=[];
 for(const width of [320,390,1920]){
  const page=await browser.newPage({viewport:{width,height:width===1920?1080:844},reducedMotion:'reduce'});const errors=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('requestfailed',r=>errors.push(r.url()+': '+r.failure()?.errorText));
  await page.goto(url);await page.waitForFunction(()=>document.images.length===161&&[...document.images].every(i=>i.complete&&i.naturalWidth));
  const state=await page.evaluate(()=>({images:document.images.length,members:document.querySelectorAll('article').length,
   overflow:document.documentElement.scrollWidth>innerWidth,
   animations:[...new Set([...document.querySelectorAll('.sprite')].map(e=>getComputedStyle(e).animationName))],
   previewScales:[...new Set([...document.querySelectorAll('.sprite')].map(e=>e.clientWidth/24))],
   pauseDisabled:document.querySelector('#pause').disabled,
   replayDisabled:[...document.querySelectorAll('#replay,.replay-pose')].every(e=>e.disabled),
   sourceStripScales:[...new Set([...document.querySelectorAll('.strip')].map(e=>e.width/e.naturalWidth))]}));
  if(state.members!==8||state.overflow||!state.pauseDisabled||!state.replayDisabled||state.animations.some(x=>x!=='none')||state.previewScales.some(x=>x!==2)||state.sourceStripScales.some(x=>x!==1))throw Error(JSON.stringify({width,state}));
  await page.selectOption('#variant','echo');await page.waitForFunction(()=>[...document.querySelectorAll('.strip')].every(e=>e.complete&&e.naturalWidth&&e.src.endsWith(e.dataset.echo.replace(/^.*\//,''))));
  if(await page.locator('.sprite[data-live]').evaluateAll(nodes=>nodes.some(e=>!e.style.backgroundImage.includes(e.dataset.echo))))throw Error('Missing directional echo preview');
  await page.selectOption('#portrait-size','2');
  const portraitState=await page.locator('.portraits img').evaluateAll(nodes=>({count:nodes.length,scales:[...new Set(nodes.map(e=>e.width/e.naturalWidth))]}));
  if(portraitState.count!==32||portraitState.scales.some(s=>s!==2)||await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('TV portraits overflow or scale incorrectly');
  await page.selectOption('#portrait-size','1');await page.selectOption('#variant','live');
  await page.screenshot({path:report+'/browser-'+width+'.png'});
  if(errors.length)throw Error(errors.join('\n'));
  results.push({width,...state,echoSwitchLoads:true,portraitTvScale:portraitState,errors});await page.close();
 }
 const page=await browser.newPage({viewport:{width:1920,height:1080},reducedMotion:'no-preference'});await page.goto(url);
 const walk=page.locator('[aria-label="Ines Okafor, south, walk"]');
 if(await walk.evaluate(e=>getComputedStyle(e).animationName)!=='frames')throw Error('Walk did not start');
 const initial=await walk.evaluate(e=>getComputedStyle(e).backgroundPositionX);await page.waitForTimeout(150);
 if(await walk.evaluate(e=>getComputedStyle(e).backgroundPositionX)===initial)throw Error('Walk did not advance');
 await page.getByRole('button',{name:'Pause motion',exact:true}).click();
 const paused=await walk.evaluate(e=>({position:getComputedStyle(e).backgroundPositionX,state:getComputedStyle(e).animationPlayState}));
 await page.waitForTimeout(160);
 if(paused.state!=='paused'||await walk.evaluate(e=>getComputedStyle(e).backgroundPositionX)!==paused.position)throw Error('Pause failed');
 await page.getByRole('button',{name:'Play motion',exact:true}).click();
 const replay=page.getByRole('button',{name:'Replay Ines Okafor south hurt',exact:true});
 await replay.click();const hurt=page.locator('[aria-label="Ines Okafor, south, hurt"]');
 if(await hurt.evaluate(e=>getComputedStyle(e).backgroundPositionX)!=='0px')throw Error('Hurt did not restart');
 await page.waitForTimeout(250);
 if(await hurt.evaluate(e=>getComputedStyle(e).backgroundPositionX)!=='-48px')throw Error('Hurt did not settle on last frame');
 await page.getByLabel('Still previews').check();
 if(await walk.evaluate(e=>getComputedStyle(e).animationName)!=='none')throw Error('Still motion failed');
 results.push({normalMotionAdvances:true,pauseHoldsFrame:true,hurtReplayStartsAndSettles:true,stillStopsMotion:true});
 await browser.close();fs.writeFileSync(report+'/browser.json',JSON.stringify(results,null,2)+'\n');
})().catch(e=>{console.error(e);process.exit(1)});
