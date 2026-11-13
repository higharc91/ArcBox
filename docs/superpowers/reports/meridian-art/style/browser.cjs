const {chromium}=require('playwright'),fs=require('fs');
(async()=>{const root=__dirname;
const browser=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});const rows=[];
for(const width of [320,390,1920]){const p=await browser.newPage({viewport:{width,height:width===1920?1080:844},reducedMotion:'reduce'}),errors=[];p.on('pageerror',e=>errors.push(e.message));
await p.goto((process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4182')+'/docs/superpowers/reports/meridian-art/style/index.html');
await p.waitForFunction(()=>document.images.length>60&&[...document.images].every(i=>i.complete&&i.naturalWidth>0));
const state=await p.evaluate(()=>({images:document.images.length,overflow:document.documentElement.scrollWidth>innerWidth,animation:getComputedStyle(document.querySelector('.walker')).animationName,scales:[...document.querySelectorAll('.tile img')].map(i=>i.width/i.naturalWidth)}));
if(state.overflow||errors.length||state.animation!=='none'||state.scales.some(s=>![1,2].includes(s)))throw Error(JSON.stringify({width,state,errors}));
await p.getByRole('button',{name:'Pause walk'}).click();await p.getByRole('button',{name:'Play walk'}).waitFor();
await p.screenshot({path:root+'/browser-'+width+'.png',fullPage:true});rows.push({width,images:state.images,overflow:state.overflow,animation:state.animation,scales:[...new Set(state.scales)],scaledImagesChecked:state.scales.length,errors,reducedMotion:true,pauseControlPresent:true});await p.close();}
const normal=await browser.newPage({viewport:{width:1920,height:1080},reducedMotion:'no-preference'});
await normal.goto((process.env.MERIDIAN_REVIEW_ORIGIN||'http://127.0.0.1:4182')+'/docs/superpowers/reports/meridian-art/style/index.html');
await normal.waitForSelector('.walker');
if(await normal.locator('.walker').evaluate(e=>getComputedStyle(e).animationName)!=='walk')throw Error('Normal motion is missing');
await normal.getByRole('button',{name:'Pause walk'}).click();
if(await normal.locator('.walker').evaluate(e=>getComputedStyle(e).animationPlayState)!=='paused')throw Error('Pause did not stop animation');
rows.push({width:1920,normalMotionStarts:true,pauseStopsAnimation:true});await normal.close();
await browser.close();fs.writeFileSync(root+'/browser.json',JSON.stringify(rows,null,2)+'\n');})().catch(e=>{console.error(e);process.exit(1)});
