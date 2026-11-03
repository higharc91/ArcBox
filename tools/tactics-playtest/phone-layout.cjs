// Run Vite first, then: node tools/tactics-playtest/phone-layout.cjs [url] [output-directory]
// Needs Playwright with Chromium (`require("playwright")` must resolve), as play.cjs does.
// Real browser geometry checks, including the small-screen overlap that prompted issue #73's phone audit.
const {chromium}=require('playwright');const fs=require('fs');const path=require('path');
const ROOT=path.resolve(__dirname,'../..');const BASE_URL=process.argv[2]??'http://127.0.0.1:5173';
const OUT=path.resolve(process.argv[3]??'/tmp/arcbox-phone-layout');
(async()=>{const browser=await chromium.launch({...(process.env.CHROMIUM?{executablePath:process.env.CHROMIUM}:fs.existsSync('/usr/bin/chromium')?{executablePath:'/usr/bin/chromium'}:{}),headless:true,args:['--no-sandbox']});const page=await browser.newPage({isMobile:true,hasTouch:true});
await page.route('**/content/**',async route=>{
 const url=new URL(route.request().url());
 if(url.pathname==='/content/tactics/index.json') return route.fulfill({contentType:'application/json',body:JSON.stringify([{packId:'base',hash:'qa'}])});
 const prefix='/content/tactics/base/qa/';const root=path.join(ROOT,'content/tactics/base')+path.sep;
 const file=require('path').resolve(root,decodeURIComponent(url.pathname.slice(prefix.length)));
 if(!url.pathname.startsWith(prefix)||!file.startsWith(root)||!fs.existsSync(file))return route.fulfill({status:404,body:'Not found'});
 const ext=require('path').extname(file);await route.fulfill({contentType:({'.json':'application/json','.png':'image/png','.ogg':'audio/ogg'})[ext]??'application/octet-stream',body:fs.readFileSync(file)});
});
const dir=OUT;fs.mkdirSync(dir,{recursive:true});const failures=[],results=[];page.on('pageerror',e=>failures.push('JS '+String(e)));
const screens=fs.readdirSync(path.join(ROOT,'web/play/test/fixtures/tactics')).filter(f=>f.endsWith('.json')&&!f.startsWith('portal-')).map(f=>f.slice(0,-5));
for(const [width,height] of [[320,568],[360,640],[390,844],[430,932],[568,320],[844,390]]){
await page.setViewportSize({width,height});
for(const screen of screens){await page.goto(BASE_URL+'/journal.html?screen='+screen);await page.waitForSelector('.tx');await page.evaluate(()=>document.fonts.ready);await page.waitForTimeout(280);
const result=await page.evaluate(()=>{const rect=s=>{const e=document.querySelector(s);if(!e)return null;const r=e.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height,right:r.right,bottom:r.bottom}};const overlap=(a,b)=>a&&b&&Math.min(a.right,b.right)-Math.max(a.x,b.x)>.5&&Math.min(a.bottom,b.bottom)-Math.max(a.y,b.y)>.5;const modal=document.querySelector('[aria-modal="true"][role="dialog"]');
const controls=[...document.querySelectorAll('button,input,select,textarea,summary')].filter(e=>(!modal||modal.contains(e))&&e.checkVisibility()&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&getComputedStyle(e).opacity!=='0').map(e=>{
 const r=e.getBoundingClientRect();let left=Math.max(0,r.left),top=Math.max(0,r.top),right=Math.min(innerWidth,r.right),bottom=Math.min(innerHeight,r.bottom);
 for(let p=e.parentElement;p;p=p.parentElement){const style=getComputedStyle(p),box=p.getBoundingClientRect();if(/auto|scroll|hidden|clip/.test(style.overflowX)){left=Math.max(left,box.left);right=Math.min(right,box.right)}if(/auto|scroll|hidden|clip/.test(style.overflowY)){top=Math.max(top,box.top);bottom=Math.min(bottom,box.bottom)}}
 return{e,name:e.getAttribute('aria-label')||e.textContent?.trim().slice(0,65)||e.tagName,x:left,y:top,right,bottom,w:right-left,h:bottom-top};
}).filter(r=>r.w>3&&r.h>3);
const collisions=[];for(let i=0;i<controls.length;i++)for(let j=i+1;j<controls.length;j++){const a=controls[i],b=controls[j];if(!a.e.contains(b.e)&&!b.e.contains(a.e)&&overlap(a,b))collisions.push([a.name,b.name])}
const info=rect('.tx-walk-info'),dock=rect('.tx-walk-dock'),pad=rect('.tx-padzone');return {collisions,overflow:document.documentElement.scrollWidth>innerWidth+1,info,dock,pad,overlap:!!(overlap(info,dock)||overlap(dock,pad)||overlap(info,pad)),dockOffscreen:!!(dock&&(dock.y<0||dock.bottom>innerHeight+1)),padOffscreen:!!(pad&&(pad.y<0||pad.bottom>innerHeight+1))};});
results.push({screen,width,height,...result});if(result.collisions.length||result.overflow||result.overlap||result.dockOffscreen||result.padOffscreen){failures.push({screen,width,height,...result});await page.screenshot({path:`${dir}/FAIL-${width}x${height}-${screen}.png`,fullPage:true});}
if(['walk-pinned-done','questlog-leader','talk-vote','talk-check'].includes(screen)&&[320,390,568].includes(width))await page.screenshot({path:`${dir}/${width}x${height}-${screen}.png`,fullPage:true});
if(screen.startsWith('walk-')&&await page.locator('.tx-pad').count()){
 const wheel=await page.locator('.tx-pad').boundingBox(),dock=await page.locator('.tx-walk-dock').count()?await page.locator('.tx-walk-dock').boundingBox():null;
 if(wheel&&dock){const clearance=width>height?wheel.x-(dock.x+dock.width):wheel.y-(dock.y+dock.height);
 if(clearance<18)failures.push({screen,width,height,wheelVisualClearance:clearance});}
}
if(screen==='questlog-leader'){
 const [message]=await Promise.all([page.waitForEvent('console',{predicate:m=>m.text().startsWith('PHONE')}),page.getByRole('button',{name:'Unpin quest',exact:true}).click()]);
 const payload=await message.args()[1].jsonValue();if(payload.action!=='questLog'||payload.data?.action!=='unpin')failures.push({screen,width,height,unpinPayload:payload});
}
if(screen==='walk-pinned'){
 await page.locator('.tx-tracker-chip').click();await page.getByRole('dialog',{name:'Private quest journal'}).waitFor();await page.getByRole('button',{name:'Close',exact:true}).click();
 if(await page.getByRole('dialog',{name:'Private quest journal'}).count())failures.push({screen,width,height,privateJournalDidNotClose:true});
}
if(screen.startsWith('walk-')&&await page.getByLabel('Session actions').count()){
 const summary=page.getByLabel('Session actions');await summary.click();
 const box=await page.locator('.tx-walk-session .tx-walk-host').boundingBox();if(box&&(box.y<0||box.x<0||box.x+box.width>width+1||box.y+box.height>height+1))failures.push({screen,width,height,sessionMenu:box});
 await summary.press('Escape');
}
if(screen==='walk-pinned'&&await page.locator('.tx-padzone').count()){
 const before=await page.locator('.tx-padzone').boundingBox();await page.locator('.tx-walk-info').evaluate(e=>{e.scrollTop=e.scrollHeight});const after=await page.locator('.tx-padzone').boundingBox();
 if(before.x!==after.x||before.y!==after.y)failures.push({screen,width,height,padMovedOnScroll:true});
}
if(screen==='walk-things'){const more=page.getByRole('button',{name:/more|nearby|choose|targets|in reach/i});if(await more.count()){await more.first().click();const list=await page.locator('.tx-interact-pick').boundingBox();if(list&&(list.y<30||list.y+list.height>height))failures.push({screen,width,height,popup:list});}}
}
console.log(`${width}x${height}: ${screens.length} screens checked`);
}
fs.writeFileSync(dir+'/results.json',JSON.stringify({cases:results.length,failures,results},null,2));console.log('FAILURES',JSON.stringify(failures));await browser.close();if(failures.length)process.exitCode=1;
})();
