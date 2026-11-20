const {chromium}=require('playwright');const fs=require('fs'),path=require('path');
(async()=>{
 const root=path.resolve(__dirname,'../..'),report=path.join(root,'docs/superpowers/reports/meridian-art/tokens');
 const m=JSON.parse(fs.readFileSync(path.join(root,'content/meridian/base/tokens/manifest.json')));
 const origin=process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4187';const url=origin+'/docs/superpowers/reports/meridian-art/tokens/index.html';
 const b=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});const rows=[];
 for(const width of [320,390,1920]){
  const p=await b.newPage({viewport:{width,height:width===1920?1080:844},reducedMotion:'reduce'}),errors=[];
  p.on('pageerror',e=>errors.push(e.message));p.on('response',r=>{if(r.status()>=400)errors.push(r.status()+' '+r.url())});
  await p.goto(url);await p.evaluate(()=>document.fonts.ready);await p.waitForFunction(()=>document.images.length>=20&&[...document.images].every(i=>i.complete&&i.naturalWidth));await p.waitForFunction(()=>[...document.querySelectorAll('.clip')].every(e=>e.dataset.frame==='0'));
  if(await p.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Page overflow '+width);
  if(await p.locator('.clip').count()!==15)throw Error('Missing monster clip');
  if(!await p.locator('#pause').isDisabled())throw Error('Reduced motion enabled playback');
  const facing=[];for(const side of ['n','e','s','w']){await p.locator('#facing').selectOption(side);const src=await p.locator('.clip').first().evaluate(e=>e.style.backgroundImage);if(!src.includes('idle-'+side+'.'))throw Error('Facing did not change '+side);facing.push(side)}
  await p.emulateMedia({reducedMotion:'no-preference'});const seen=new Set();for(let i=0;i<10;i++){seen.add(await p.locator('.clip').first().getAttribute('data-frame'));await p.waitForTimeout(90)}if(seen.size<2)throw Error('No actual animation');
  await p.locator('#pause').click();const held=await p.locator('.clip').first().getAttribute('data-frame');await p.waitForTimeout(200);if(held!==await p.locator('.clip').first().getAttribute('data-frame'))throw Error('Pause moved');
  await p.locator('#pause').click();await p.locator('#replay').click();await p.waitForTimeout(850);const attack=p.locator('.clip[data-count="6"][data-loop="false"]').first();if(await attack.getAttribute('data-frame')!=='5')throw Error('One-shot attack did not settle');
  await p.locator('#replay').click();await p.waitForTimeout(70);if(Number(await attack.getAttribute('data-frame'))>1)throw Error('Attack replay failed');
  await p.locator('#still').check();await p.waitForFunction(()=>[...document.querySelectorAll('.clip')].every(e=>e.dataset.frame==='0'));
  await p.evaluate(()=>scrollTo(0,0));await p.screenshot({path:report+'/browser-'+width+'.png'});
  if(errors.length)throw Error(errors.join('\n'));rows.push({width,images:await p.locator('img').count(),clips:15,facings:facing,normalFramesSeen:seen.size,reduce:true,pause:true,oneShotHold:true,replay:true,still:true,pageOverflow:false,errors});await p.close();
 }
 const pixels=[];const p=await b.newPage({viewport:{width:48,height:48}});
 for(const key of ['brood','drone','sleeper'])for(const side of ['n','e','s','w']){
  const e=m.tokens[key].idle[side],asset=origin+'/content/meridian/base/tokens/'+e.file;
  await p.setContent('<style>html,body{margin:0;padding:0;background:#21243c}div{width:48px;height:48px;image-rendering:pixelated;background-image:url("'+asset+'");background-size:'+e.frames*48+'px 48px}</style><div></div>');
  await p.evaluate(src=>new Promise((resolve,reject)=>{const i=new Image;i.onload=resolve;i.onerror=reject;i.src=src}),asset);
  const file='pixel-'+key+'-'+side+'.png';await p.screenshot({path:report+'/'+file});pixels.push({key,side,source:e.file,capture:file,scale:2});
 }
 await p.close();await b.close();fs.writeFileSync(report+'/browser.json',JSON.stringify({viewports:rows,pixelSamples:pixels},null,2)+'\n');console.log('All three widths, all four facings, motion/still/replay and twelve exact-scale capture cases completed.');
})().catch(e=>{console.error(e);process.exit(1)});
