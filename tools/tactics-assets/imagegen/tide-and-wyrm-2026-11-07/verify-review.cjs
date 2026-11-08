const fs = require('fs'), path = require('path'), { chromium } = require('playwright');
(async () => {
  const out = path.resolve('docs/art/mob-sprite-review/tide-and-wyrm');
  const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
  try {
    const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } }), errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file://' + path.join(out, 'index.html'));
    await page.waitForFunction(() => window.ready);
    const scenes = await page.evaluate(async () => {
      await Promise.all([...document.images].map(i => i.decode()));
      return [...document.images].map(i => ({ title: i.alt, width: i.naturalWidth, height: i.naturalHeight }));
    });
    const hashes = {};
    for (const action of ['idle', 'walk', 'attack', 'special', 'cast', 'buff', 'hurt', 'down']) {
      hashes[action] = await page.evaluate(action => {
        paused = true; time = .55; document.querySelector('#action').value = action; draw();
        const canvas = document.querySelector('#reel');
        let hash = 2166136261;
        for (const v of canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data)
          hash = Math.imul(hash ^ v, 16777619);
        return (hash >>> 0).toString(16);
      }, action);
    }
    const walkMotion = await page.evaluate(() => {
      document.querySelector('#action').value = 'walk';
      const canvas = document.querySelector('#reel');
      return data.map((mob, m) => ({id:mob.id, hashes:[0,1,2,3].map(frame=>{
        time=(frame+.1)/mob.anims.walk.fps;draw();let hash=2166136261;
        for(const v of canvas.getContext('2d').getImageData(0,m*420,canvas.width,420).data) hash=Math.imul(hash^v,16777619);
        return (hash>>>0).toString(16);
      })}));
    });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.selectOption('#facing', '4');
    await page.evaluate(() => draw());
    const singleFacingWidth = await page.locator('#reel').evaluate(c => c.width);
    const phone = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth }));
    const result = { scenes, actionHashes: hashes, walkFrameHashes: walkMotion, pageErrors: errors, phone, singleFacingWidth,
      passed: scenes.length === 5 && new Set(Object.values(hashes)).size === 8 && walkMotion.every(m => new Set(m.hashes).size === 4) && errors.length === 0 && phone.document <= phone.viewport && singleFacingWidth === 640 };
    fs.writeFileSync(path.join(out, 'browser-validation.json'), JSON.stringify(result, null, 2) + '\n');
    console.log(JSON.stringify(result, null, 2));
    if (!result.passed) throw Error('Review validation failed');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
