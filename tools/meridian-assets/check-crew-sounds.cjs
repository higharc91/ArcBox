const {chromium}=require('playwright');const fs=require('fs'),path=require('path'),crypto=require('crypto');
(async()=>{
 const root=path.resolve(__dirname,'../..'),report=path.join(root,'docs/superpowers/reports/meridian-art/audio/crew');
 const pack=path.join(root,'content/meridian/base/audio/crew-lock-in');const m=JSON.parse(fs.readFileSync(path.join(pack,'manifest.json')));
 const ids=JSON.parse(fs.readFileSync(path.join(root,'tools/meridian-assets/reference/crew.json'))).crew.map(c=>c.id).sort();if(ids.join()!==Object.keys(m.sounds).sort().join()||fs.readdirSync(pack).filter(f=>f.endsWith('.ogg')).length!==8)throw Error('Incomplete crew coverage');
 const b=await chromium.launch({headless:true,executablePath:'/usr/bin/chromium',args:['--no-sandbox']});const results=[];
 for(const width of [320,390]){
  const p=await b.newPage({viewport:{width,height:844},reducedMotion:'reduce'});const errors=[];p.on('pageerror',e=>errors.push(e.message));p.on('response',r=>{if(r.status()>=400)errors.push(r.status()+' '+r.url())});
  await p.addInitScript(()=>{window.started=[];const orig=AudioContext.prototype.createOscillator;AudioContext.prototype.createOscillator=function(){const o=orig.call(this),start=o.start.bind(o);o.start=(...args)=>{window.started.push({type:o.type,time:args[0]});return start(...args)};return o}});
  await p.goto('http://127.0.0.1:4187/docs/superpowers/reports/meridian-art/audio/crew/index.html');await p.waitForFunction(()=>[...document.images].every(i=>i.complete&&i.naturalWidth));
  if(await p.evaluate(()=>window.started.length))throw Error('Autoplay');
  if(await p.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Page overflow');
  for(const id of Object.keys(m.sounds)){
   await p.locator('#reset').click();const count=await p.evaluate(()=>window.started.length);
   await p.locator('[data-lock='+id+']').click();await p.waitForTimeout(80);
   if(await p.evaluate(()=>window.started.length)!==count+3||!(await p.locator('#status').textContent()).includes('locked in'))throw Error('Wrong confirmation '+id);
   if(await p.locator('[data-lock='+id+']').textContent()!=='Chosen'||await p.locator('article.chosen').count()!==1)throw Error('Missing visual confirmation');
  }
  await p.locator('#mute').check();await p.locator('#reset').click();const held=await p.evaluate(()=>window.started.length);await p.locator('[data-lock=ines_okafor]').click();if(await p.evaluate(()=>window.started.length)!==held)throw Error('Muted cue sounded');
  await p.locator('#mute').uncheck();await p.waitForTimeout(100);if(await p.evaluate(()=>window.started.length)!==held)throw Error('Mute catch-up');
  await p.locator('#reset').click();await p.locator('[data-lock=tobe_raske]').focus();await p.keyboard.press('Space');if(await p.evaluate(()=>window.started.length)!==held+3)throw Error('Keyboard confirmation failed');
  await p.locator('#reset').click();await p.evaluate(()=>scrollTo(0,0));await p.screenshot({path:path.join(report,'browser-'+width+'.png')});
  if(errors.length)throw Error(errors.join('\n'));results.push({width,autoplay:false,confirmedCues:8,mute:true,unmuteDoesNotReplay:true,keyboard:true,pageOverflow:false,errors});await p.close();
 }
 const p=await b.newPage();await p.goto('http://127.0.0.1:4187/docs/superpowers/reports/meridian-art/audio/crew/index.html');
 const assets=[];
 for(const [id,e] of Object.entries(m.sounds)){
  const bytes=fs.readFileSync(path.join(pack,e.file));if(crypto.createHash('sha256').update(bytes).digest('hex')!==e.sha256)throw Error('Hash '+id);
  const signal=await p.evaluate(async file=>{const c=new AudioContext(),response=await fetch('/content/meridian/base/audio/crew-lock-in/'+file);const a=await c.decodeAudioData(await response.arrayBuffer());const s=a.getChannelData(0);let peak=0,sum=0;for(const x of s){peak=Math.max(peak,Math.abs(x));sum+=x*x}await c.close();return {duration:a.duration,sampleRate:a.sampleRate,channels:a.numberOfChannels,peak,rms:Math.sqrt(sum/s.length)}} ,e.file);
  if(signal.duration>.6||signal.duration<.4||signal.channels!==1||signal.peak>=.5||signal.rms<.005)throw Error('Bad signal '+id+' '+JSON.stringify(signal));assets.push({id,...signal});
 }
 await p.close();
 const silent=await b.newPage();await silent.addInitScript(()=>{window.AudioContext=undefined;window.webkitAudioContext=undefined});await silent.goto('http://127.0.0.1:4187/docs/superpowers/reports/meridian-art/audio/crew/index.html');await silent.locator('[data-lock=ines_okafor]').click();if(!(await silent.locator('#status').textContent()).includes('locked in'))throw Error('Audio absence blocked selection');await silent.close();await b.close();fs.writeFileSync(path.join(report,'browser.json'),JSON.stringify({views:results,decodedAssets:assets},null,2)+'\n');console.log('Chromium: 8 confirmed cues at both phone widths; no autoplay, mute/replay and keyboard checks pass; all 8 Oggs decode.');
})().catch(e=>{console.error(e);process.exit(1)});
