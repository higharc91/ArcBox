// Renders each mockup HTML in this folder to a PNG next to it.
// Usage: node shoot.js [file.html ...]   (defaults to every *.html here; uses the preinstalled Playwright)
// Each page declares its viewport on <html data-shot="WIDTHxHEIGHT@SCALE">, and sets window.__ready = true
// once its canvases are drawn. Pages are served over a local HTTP server so ES modules load.
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

  const browser = await chromium.launch();
  for (const f of files) {
    const shot = /data-shot="(\d+)x(\d+)@([\d.]+)"/.exec(fs.readFileSync(path.join(dir, f), 'utf8'));
    const [w, h, scale] = shot ? [+shot[1], +shot[2], +shot[3]] : [1920, 1080, 1];
    const page = await browser.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: scale });
    page.on('pageerror', e => console.error(`[${f}] ${e.message}`));
    await page.goto(base + f);
    await page.evaluate(() => document.fonts.ready);
    await page.waitForFunction(() => window.__ready === true, null, { timeout: 60000 });
    await page.waitForTimeout(200);
    const out = path.resolve(dir, f.replace(/\.html$/, '.png'));
    await page.screenshot({ path: out, fullPage: true });
    console.log(out);
    await page.close();
  }
  await browser.close();
  server.close();
})();
