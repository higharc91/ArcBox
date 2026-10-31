// A scripted ArcBox Tactics playtest in a real browser, through the dev page (docs/dev-playtest.md).
//
//   node tools/tactics-playtest/play.cjs [server] [shots-dir] [--fight]
//
// Needs a running server with the phone app built (`npm run build` in web/play) and Playwright with Chromium
// (`require('playwright')` must resolve). It creates a room on the dev page with four phones, makes a campaign through
// the VIP's phone, picks classes and River Crossing, then plays: each player ends their turn and passes on reactions
// (with --fight: attacks a foe in reach through the ability and target screens, else walks toward the foes first),
// while the dev page's board stands in for the TV (it sends "shown" after each beat). Screenshots of the key screens,
// and of the board every ~10 s, go to the shots directory. Exits non-zero if the battle doesn't reach its results.
const { chromium } = require('playwright');
const fs = require('fs');

const server = process.argv[2] ?? 'http://localhost:5080';
const out = process.argv[3] ?? 'tactics-shots';
const fight = process.argv.includes('--fight');
let attacks = 0;

/** One player's turn the way a person would play it: attack if they can, else step toward the foes, then end. */
async function playTurn(f) {
  const attack = f.getByRole('button', { name: /^Attack/ });
  if (await attack.count()) {
    await attack.first().click({ timeout: 3000 });
    const target = f.locator('.tx-targets button').first();
    if (await target.count()) await target.click({ timeout: 3000 });
    const use = f.getByRole('button', { name: /^Use/ });
    if (await use.isEnabled().catch(() => false)) {
      await use.click({ timeout: 3000 });
      attacks++;
      await f.waitForTimeout(600);
      return;   // the next view comes back as a new turn screen (Move or End turn next loop)
    }
    await f.getByRole('button', { name: 'Back' }).click({ timeout: 3000 });
  }
  // Walk: the reachable tile nearest a foe, measured on the phone's own map grid.
  const best = await f.evaluate(() => {
    const at = [];
    document.querySelectorAll('.tx-maprow').forEach((row, y) => row.querySelectorAll('.tx-cell').forEach((cell, x) => at.push({ cell, x, y })));
    const foes = at.filter((c) => c.cell.classList.contains('foe'));
    const reach = at.filter((c) => c.cell.classList.contains('reach'));
    let pick = null, bestD = Infinity;
    for (const r of reach)
      for (const foe of foes) {
        const d = Math.abs(r.x - foe.x) + Math.abs(r.y - foe.y);
        if (d < bestD) { bestD = d; pick = r.cell.getAttribute('aria-label'); }
      }
    return pick;
  });
  if (best) {
    await f.getByRole('button', { name: best, exact: true }).click({ timeout: 3000 });
    const move = f.getByRole('button', { name: /^Move here/ });
    if (await move.isEnabled().catch(() => false)) {
      await move.click({ timeout: 3000 });
      await f.waitForTimeout(600);
    }
  }
  await f.getByRole('button', { name: 'End turn' }).click({ timeout: 3000 });
}
fs.mkdirSync(out, { recursive: true });

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  await page.goto(`${server}/dev.html?phones=4`);
  await page.waitForTimeout(2500);
  const phones = page.frames().filter((f) => f !== page.mainFrame());
  const text = async (f) => (await f.locator('body').innerText()).replace(/\s+/g, ' ');
  const shot = (name) => page.screenshot({ path: `${out}/${name}.png` });
  const phoneShot = async (i, name) => (await phones[i].frameElement()).screenshot({ path: `${out}/${name}.png` });

  // The lobby's campaign step, through the phones' own UI.
  await page.getByRole('button', { name: 'Tactics', exact: true }).click();
  await page.waitForTimeout(800);
  await phones[0].getByRole('button', { name: 'New campaign' }).click();
  await phones[0].locator('input').first().fill('Embers');
  await phones[0].locator('input').first().press('Enter');
  await page.waitForTimeout(1200);
  for (const f of phones.slice(1)) {
    await f.getByRole('button', { name: "I'm new here" }).click();   // opens the optional name field
    await page.waitForTimeout(300);
    await f.getByRole('button', { name: "I'm new here" }).click();   // confirms
  }
  await page.waitForTimeout(1200);
  await phones[0].getByRole('button', { name: "Everybody's in" }).click();
  await page.waitForTimeout(2500);

  // Muster and the battle pick.
  await phoneShot(0, '01-pick-class');
  const classes = ['Fighter', 'Cleric', 'Rogue', 'Wizard'];
  for (const [i, f] of phones.entries()) await f.getByRole('button', { name: new RegExp(classes[i]) }).click();
  await page.waitForTimeout(1500);
  await phoneShot(0, '02-pick-encounter');
  await phones[0].getByRole('button', { name: /River Crossing/ }).click();
  await page.waitForTimeout(2500);
  await shot('03-battle');

  const seen = new Set();
  const start = Date.now();
  let loops = 0;
  let over = false;
  while (Date.now() - start < 8 * 60_000) {
    if (/Victory at|Defeat at/.test(await page.locator('body').innerText())) { over = true; break; }
    for (const [i, f] of phones.entries()) {
      const t = await text(f).catch(() => '');
      if (t.includes('End turn')) {
        if (!seen.has('turn')) { seen.add('turn'); await page.waitForTimeout(1500); await phoneShot(i, '04-turn'); }   // the map loads first
        // The view can change under the tap (a new beat); the next loop tries again.
        if (fight) await playTurn(f).catch(() => {});
        else await f.getByRole('button', { name: 'End turn' }).click({ timeout: 3000 }).catch(() => {});
      } else if (t.includes("Don't react")) {
        if (!seen.has('react')) { seen.add('react'); await phoneShot(i, '05-react'); }
        await f.getByRole('button', { name: "Don't react" }).click({ timeout: 3000 }).catch(() => {});
      } else if (t.includes('is acting') && !seen.has('status')) { seen.add('status'); await phoneShot(i, '06-status'); }
    }
    if (++loops % 20 === 0) await shot(`07-board-${String(loops / 20).padStart(2, '0')}`);
    await page.waitForTimeout(500);
  }
  await page.waitForTimeout(1500);
  await shot('08-results');
  const outcome = (await page.locator('.tx-board-banner b').innerText().catch(() => '')).trim();
  console.log(over ? `${outcome} after ${Math.round((Date.now() - start) / 1000)} s; ${attacks} attacks from the phones.`
    : 'The battle never reached its results.');
  await browser.close();
  process.exit(over ? 0 : 1);
})();
