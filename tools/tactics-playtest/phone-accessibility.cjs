// Vite fixture preview required. Usage: node tools/tactics-playtest/phone-accessibility.cjs [url] [report.json]
// External tools: playwright, axe-core, pngjs. Uses real Chromium and screenshot backgrounds for gradient/textured panels.
const { chromium } = require("playwright"),
  { PNG } = require("pngjs"),
  fs = require("fs"),
  path = require("path");
const ROOT = path.resolve(__dirname, "../.."),
  BASE = process.argv[2] ?? "http://localhost:5174",
  OUT = process.argv[3] ?? "/tmp/arcbox-accessibility.json";
const axePath = process.env.AXE_PATH ?? require.resolve("axe-core/axe.min.js");
const linear = (c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
const lum = (c) =>
  c
    .map((x) => linear(x / 255))
    .reduce((n, x, i) => n + x * [0.2126, 0.7152, 0.0722][i], 0);
const contrast = (a, b) => {
  a = lum(a);
  b = lum(b);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
};
(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.CHROMIUM ?? "/usr/bin/chromium",
    headless: true,
    args: ["--no-sandbox"],
  });
  const page = await browser.newPage({
    reducedMotion: "reduce",
    isMobile: true,
    hasTouch: true,
  });
  await page.route("**/content/**", async (route) => {
    const url = new URL(route.request().url()),
      prefix = "/content/tactics/base/qa/";
    if (url.pathname === "/content/tactics/index.json")
      return route.fulfill({ json: [{ packId: "base", hash: "qa" }] });
    const root = path.join(ROOT, "content/tactics/base") + path.sep,
      file = path.resolve(
        root,
        decodeURIComponent(url.pathname.slice(prefix.length)),
      );
    if (
      !url.pathname.startsWith(prefix) ||
      !file.startsWith(root) ||
      !fs.existsSync(file)
    )
      return route.fulfill({ status: 404 });
    return route.fulfill({
      contentType: file.endsWith(".json")
        ? "application/json"
        : file.endsWith(".png")
          ? "image/png"
          : "application/octet-stream",
      body: fs.readFileSync(file),
    });
  });
  const fixtures = fs
      .readdirSync(path.join(ROOT, "web/play/test/fixtures/tactics"))
      .filter(
        (f) =>
          f.endsWith(".json") &&
          (!process.env.FIXTURE || f === process.env.FIXTURE + ".json"),
      )
      .sort(),
    results = [];
  for (const width of [320, 390])
    for (const file of fixtures) {
      await page.setViewportSize({ width, height: 844 });
      await page.goto(BASE + "/journal.html?screen=" + file.slice(0, -5));
      await page.waitForSelector(".tx");
      await page.waitForLoadState("networkidle");
      await page.evaluate(() => document.fonts.ready);
      await page.addScriptTag({ path: axePath });
      const axe = await page.evaluate(
        async () =>
          await axe.run(document, {
            runOnly: { type: "rule", values: ["color-contrast"] },
          }),
      );
      const geometry = await page.evaluate(() => {
        const text = [],
          targets = [];
        for (const e of document.querySelectorAll(".screen *")) {
          if (!e.checkVisibility() || e.closest('[aria-hidden="true"]'))
            continue;
          const s = getComputedStyle(e),
            r = e.getBoundingClientRect();
          if (!r.width || !r.height || s.opacity === "0") continue;
          if (
            [...e.childNodes].some(
              (n) => n.nodeType === 3 && n.textContent.trim(),
            ) &&
            parseFloat(s.fontSize) < 13
          )
            text.push({
              selector: e.className,
              text: e.textContent.slice(0, 60),
              size: s.fontSize,
            });
          if (
            e.matches("button,input,select,textarea,summary,a[href]") &&
            !e.matches(".tx-cell") &&
            (r.width < 23.99 || r.height < 23.99)
          )
            targets.push({
              selector: e.className,
              text: e.textContent.slice(0, 60),
              width: r.width,
              height: r.height,
            });
        }
        const cells = [...document.querySelectorAll(".tx-cell")];
        const dense = cells.some((e) => e.getBoundingClientRect().width < 24);
        const zoom = document.querySelector(".tx-map-tools button:last-child");
        return {
          text,
          targets,
          cssViewport: innerWidth,
          overflow: document.documentElement.scrollWidth > innerWidth + 1,
          denseMap: dense,
          denseAlternative:
            !dense ||
            !!(
              document.querySelector(".tx-dpad") &&
              [...document.querySelectorAll("button")].find(
                (e) => e.textContent === "Enlarge map",
              )
            ),
        };
      });
      geometry.overflow ||= Math.abs(geometry.cssViewport - width) > 1;
      const scrolls = await page.evaluate(() => {
        const all = [
          document.scrollingElement,
          ...document.querySelectorAll(".screen *"),
        ];
        return all.flatMap((e, i) => {
          if (!e.checkVisibility()) return [];
          const style = getComputedStyle(e);
          e.setAttribute("data-audit-scroll", String(i));
          return [
            ["y", "scrollHeight", "clientHeight", "overflowY"],
            ["x", "scrollWidth", "clientWidth", "overflowX"],
          ].flatMap(([axis, extent, client, overflow]) => {
            if (
              e[client] < 24 ||
              e[extent] <= e[client] + 2 ||
              (e !== document.scrollingElement &&
                !/auto|scroll/.test(style[overflow]))
            )
              return [];
            return [
              {
                id: String(i),
                axis,
                height: e[client],
                max: e[extent] - e[client],
              },
            ];
          });
        });
      });
      const states = [
        null,
        ...scrolls.flatMap((s) => {
          const positions = [];
          for (let y = s.height * 0.8; y < s.max; y += s.height * 0.8)
            positions.push(y);
          positions.push(s.max);
          return positions.map((y) => ({ id: s.id, axis: s.axis, y }));
        }),
      ];
      const pixelFailures = [];
      let samples = 0;
      const sampledText = new Set();
      for (const state of states) {
        if (state)
          await page.evaluate((s) => {
            const e = document.querySelector(`[data-audit-scroll="${s.id}"]`);
            if (e !== document.scrollingElement)
              e.scrollIntoView({ block: "start" });
            if (s.axis === "x") e.scrollLeft = s.y;
            else e.scrollTop = s.y;
          }, state);
        const texts = await page.evaluate(() => {
          const items = [];
          const walker = document.createTreeWalker(
            document.querySelector(".screen"),
            NodeFilter.SHOW_TEXT,
          );
          for (let node; (node = walker.nextNode()); ) {
            const e = node.parentElement;
            if (
              !node.textContent.trim() ||
              !e.checkVisibility() ||
              e.closest('[aria-hidden="true"]')
            )
              continue;
            const style = getComputedStyle(e);
            let alpha = 1;
            for (let p = e; p; p = p.parentElement)
              alpha *= parseFloat(getComputedStyle(p).opacity);
            if (!alpha) continue;
            const color = style.color.match(/[\d.]+/g)?.map(Number);
            if (!color) continue;
            alpha *= color[3] ?? 1;
            const range = document.createRange();
            range.selectNodeContents(node);
            for (const r of range.getClientRects()) {
              let left = Math.max(0, r.left),
                right = Math.min(innerWidth, r.right),
                top = Math.max(0, r.top),
                bottom = Math.min(innerHeight, r.bottom);
              for (let p = e.parentElement; p; p = p.parentElement) {
                const s = getComputedStyle(p),
                  box = p.getBoundingClientRect();
                if (/auto|scroll|hidden|clip/.test(s.overflowX)) {
                  left = Math.max(left, box.left + p.clientLeft);
                  right = Math.min(
                    right,
                    box.left + p.clientLeft + p.clientWidth,
                  );
                }
                if (/auto|scroll|hidden|clip/.test(s.overflowY)) {
                  top = Math.max(top, box.top + p.clientTop);
                  bottom = Math.min(
                    bottom,
                    box.top + p.clientTop + p.clientHeight,
                  );
                }
              }
              if (right - left < r.width - 0.5 || bottom - top < r.height - 0.5)
                continue;
              const points = [];
              for (const x of [0.25, 0.5, 0.75])
                for (const y of [0.4, 0.6]) {
                  const px = left + (right - left) * x,
                    py = top + (bottom - top) * y,
                    upper = document.elementFromPoint(px, py);
                  if (
                    upper &&
                    (upper === e || e.contains(upper) || upper.contains(e))
                  )
                    points.push([px, py]);
                }
              if (points.length)
                items.push({
                  text: node.textContent.trim().slice(0, 70),
                  selector: e.className,
                  color: color.slice(0, 3),
                  alpha,
                  points,
                });
            }
          }
          return items;
        });
        const hidden = await page.addStyleTag({
          content:
            ".screen,.screen *{-webkit-text-fill-color:transparent!important;text-shadow:none!important}",
        });
        const png = PNG.sync.read(await page.screenshot());
        await hidden.evaluate((e) => e.remove());
        for (const t of texts) {
          let minimum = Infinity,
            debug;
          for (const [x, y] of t.points) {
            const px = Math.min(png.width - 1, Math.max(0, Math.floor(x))),
              py = Math.min(png.height - 1, Math.max(0, Math.floor(y))),
              index = (py * png.width + px) * 4,
              bg = [...png.data.subarray(index, index + 3)],
              fg = t.color.map((v, i) => v * t.alpha + bg[i] * (1 - t.alpha));
            const ratio = contrast(fg, bg);
            if (ratio < minimum) {
              minimum = ratio;
              debug = { bg, color: t.color, alpha: t.alpha, px, py };
            }
          }
          samples++;
          sampledText.add(t.text);
          if (minimum < 4.5)
            pixelFailures.push({
              selector: t.selector,
              text: t.text,
              ratio: +minimum.toFixed(2),
              ...debug,
            });
        }
      }
      const key = (x) => JSON.stringify(x),
        unique = (a) => [...new Map(a.map((x) => [key(x), x])).values()];
      const result = {
        width,
        file,
        ...geometry,
        axe: axe.violations.flatMap((v) =>
          v.nodes.map((n) => ({ target: n.target, summary: n.failureSummary })),
        ),
        pixelFailures: unique(pixelFailures),
        samples,
        sampledLabels: [...sampledText],
        scrollStates: states.length,
      };
      results.push(result);
      console.log(
        width,
        file,
        "size",
        result.text.length,
        "targets",
        result.targets.length,
        "axe",
        result.axe.length,
        "pixels",
        result.pixelFailures.length,
      );
      fs.writeFileSync(
        OUT,
        JSON.stringify(
          {
            fixtures: fixtures.length,
            viewports: [
              [320, 844],
              [390, 844],
            ],
            results,
          },
          null,
          2,
        ),
      );
    }
  await browser.close();
  const failed = results.filter(
    (r) =>
      r.text.length ||
      r.targets.length ||
      r.axe.length ||
      r.pixelFailures.length ||
      r.overflow ||
      !r.denseAlternative,
  );
  console.log(
    results.length + " fixture/viewport audits; " + failed.length + " failing",
  );
  if (failed.length) process.exitCode = 1;
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
