// Technical packing only: copy complete authored poses, never repaint or deform anatomy/equipment.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const root = process.argv[2] ? path.resolve(process.argv[2]) : __dirname;
  const data = JSON.parse(fs.readFileSync(path.join(root, 'packing.json')));
  const images = {};
  for (const mob of data)
    for (const frame of mob.frames)
      images[frame.source] ??= 'data:image/png;base64,' + fs.readFileSync(path.join(root, frame.source)).toString('base64');
  const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
  try {
    const page = await browser.newPage();
    const results = await page.evaluate(async ({ data, images }) => {
      const sources = {};
      for (const [name, url] of Object.entries(images)) {
        const image = new Image(); image.src = url; await image.decode(); sources[name] = image;
      }
      function canvas(size, height = size) {
        const value = document.createElement('canvas'); value.width = size; value.height = height;
        value.getContext('2d').imageSmoothingEnabled = false;
        return value;
      }
      const defaultStarts = { idle: 0, walk: 4, attack: 8, hurt: 12, down: 13 };
      const output = [];
      for (const mob of data) {
        const rows = mob.rowsPerDirection ?? 2;
        const starts = mob.actionOffsets ?? defaultStarts;
        const atlas = canvas(mob.size * 8, mob.size * rows * 8);
        const atlasContext = atlas.getContext('2d');
        const frames = [], checks = [];
        // Render on the canonical authoring grid before cropping padding. Merely changing the
        // destination canvas origin can alter fractional nearest-neighbour sampling in Chromium.
        const authorSize = mob.id === 'drake' ? 256 : mob.size;
        const anchor = mob.id === 'drake' ? [128, 224] : mob.anchor;
        for (const frame of mob.frames) {
          const painted = canvas(authorSize), context = painted.getContext('2d');
          let x = anchor[0] - frame.foot[0] * frame.scale;
          let y = anchor[1] + 1 - frame.foot[1] * frame.scale;
          if (mob.headRegistration !== false && (frame.action === 'walk' || frame.action === 'idle')) {
            const idle = mob.frames.find(f => f.row === frame.row && f.action === 'idle' && f.frame === 0);
            const headX = anchor[0] + (idle.head[0] - idle.foot[0]) * idle.scale;
            const headY = anchor[1] + 1 + (idle.head[1] - idle.foot[1]) * idle.scale;
            x = headX - frame.head[0] * frame.scale;
            y = headY + (frame.action === 'walk' ? [0, 1, 0, -1][frame.frame] : 0) - frame.head[1] * frame.scale;
          }
          context.drawImage(sources[frame.source], ...frame.cell,
            Math.round(x + frame.cell[0] * frame.scale), Math.round(y + frame.cell[1] * frame.scale),
            frame.cell[2] * frame.scale, frame.cell[3] * frame.scale);
          if (frame.action === 'idle') {
            const pixels = context.getImageData(0, 0, authorSize, authorSize).data;
            let bottom = -1;
            for (let py = 0; py < authorSize; py++)
              for (let px = 0; px < authorSize; px++)
                if (pixels[(py * authorSize + px) * 4 + 3]) bottom = py;
            if (bottom !== anchor[1]) {
              const copy = canvas(authorSize); copy.getContext('2d').drawImage(painted, 0, 0);
              context.clearRect(0, 0, authorSize, authorSize);
              context.drawImage(copy, 0, anchor[1] - bottom);
            }
          }
          const packed = canvas(mob.size), packedContext = packed.getContext('2d');
          packedContext.drawImage(painted, anchor[0] - mob.anchor[0], anchor[1] - mob.anchor[1],
            mob.size, mob.size, 0, 0, mob.size, mob.size);
          const pixels = packedContext.getImageData(0, 0, mob.size, mob.size).data;
          let bounds = [mob.size, mob.size, -1, -1], count = 0;
          for (let py = 0; py < mob.size; py++)
            for (let px = 0; px < mob.size; px++)
              if (pixels[(py * mob.size + px) * 4 + 3] >= 64) {
                bounds = [Math.min(bounds[0], px), Math.min(bounds[1], py), Math.max(bounds[2], px), Math.max(bounds[3], py)]; count++;
              }
          const dx = mob.anchor[0] - anchor[0], dy = mob.anchor[1] - anchor[1];
          const projected = [x + frame.bounds[0] * frame.scale + dx, y + frame.bounds[1] * frame.scale + dy,
            x + frame.bounds[2] * frame.scale + dx, y + frame.bounds[3] * frame.scale + dy];
          if (count < 100 || Math.min(...projected) < 2 || projected[2] > mob.size - 2 || projected[3] > mob.size - 2)
            throw Error(`Clipped ${mob.id}/${frame.direction}/${frame.action}/${frame.frame}: ${projected}`);
          const column = starts[frame.action] + frame.frame;
          atlasContext.drawImage(packed, column % 8 * mob.size, (frame.row * rows + Math.floor(column / 8)) * mob.size);
          frames.push({ name: `${frame.action}/${frame.direction}/${frame.frame}.png`, image: packed.toDataURL() });
          checks.push({ action: frame.action, direction: frame.direction, frame: frame.frame, source: frame.source,
            sourcePose: frame.sourcePose, scale: frame.scale, bounds, count });
        }
        output.push({ id: mob.id, atlas: atlas.toDataURL(), frames, checks });
      }
      return output;
    }, { data, images });
    for (const mob of results) {
      const directory = path.join(root, 'export', mob.id); fs.mkdirSync(directory, { recursive: true });
      fs.writeFileSync(path.join(directory, 'atlas.png'), Buffer.from(mob.atlas.split(',')[1], 'base64'));
      for (const frame of mob.frames) {
        const file = path.join(directory, frame.name); fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, Buffer.from(frame.image.split(',')[1], 'base64'));
      }
      fs.writeFileSync(path.join(directory, 'checks.json'), JSON.stringify(mob.checks, null, 2) + '\n');
      console.log(`${mob.id}: ${mob.checks.length} full poses, no clipping.`);
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
