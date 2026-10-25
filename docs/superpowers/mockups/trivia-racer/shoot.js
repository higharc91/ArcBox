// Renders each mockup HTML in this folder to a PNG next to it.
// Usage: node shoot.js [file.html ...]   (defaults to every *.html here)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const dir = __dirname;
  const files = process.argv.length > 2
    ? process.argv.slice(2)
    : fs.readdirSync(dir).filter(f => f.endsWith('.html'));
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1840, height: 900 }, deviceScaleFactor: 1.5 });
  for (const f of files) {
    await page.goto('file://' + path.resolve(dir, f));
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(300);
    const out = path.resolve(dir, f.replace(/\.html$/, '.png'));
    await page.screenshot({ path: out, fullPage: true });
    console.log(out);
  }
  await browser.close();
})();
