// Renders each mockup HTML in this folder to a PNG next to it.
// Usage: npm install && node shoot.js [file.html ...]   (defaults to every *.html here)
// Pages are served over a local HTTP server so ES modules (three.js) load; a page that
// renders asynchronously sets window.__ready = true when it is done.
const { chromium } = require('playwright');
const fs = require('fs');
const http = require('http');
const path = require('path');

const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png' };

(async () => {
  const dir = __dirname;
  const files = process.argv.length > 2
    ? process.argv.slice(2)
    : fs.readdirSync(dir).filter(f => f.endsWith('.html'));

  const server = http.createServer((req, res) => {
    const file = path.join(dir, decodeURIComponent(new URL(req.url, 'http://x').pathname));
    if (!file.startsWith(dir) || !fs.existsSync(file)) { res.writeHead(404).end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  }).listen(0);
  const base = `http://127.0.0.1:${server.address().port}/`;

  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const page = await browser.newPage({ viewport: { width: 1840, height: 900 }, deviceScaleFactor: 1.5 });
  page.on('pageerror', e => console.error(`[${files}] ${e.message}`));
  for (const f of files) {
    await page.goto(base + f);
    await page.evaluate(() => document.fonts.ready);
    if (await page.evaluate(() => 'three' in (window.__needs || {}))) {
      await page.waitForFunction(() => window.__ready === true, null, { timeout: 120000 });
    }
    await page.waitForTimeout(300);
    const out = path.resolve(dir, f.replace(/\.html$/, '.png'));
    await page.screenshot({ path: out, fullPage: true });
    console.log(out);
  }
  await browser.close();
  server.close();
})();
