// ArcBox Tactics mockup kit: an HD-2D style isometric tile world (pixel-art units on a 3D-looking
// diorama, with bloom, warm/cool grading and tilt-shift depth of field) drawn on a 2D canvas.
// Everything is procedural so the mockups stay plain text; the real game builds this in Godot.

export const TW = 112, TH = 56, HS = 26;          // tile width, tile height, pixels per height step

export function rng(seed) {
  return () => {
    seed = (seed + 0x6D2B79F5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ---------- colour helpers ----------
function hex(c) { const n = parseInt(c.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
export function shade(c, f) {
  const [r, g, b] = hex(c);
  const k = v => Math.max(0, Math.min(255, Math.round(f >= 1 ? v + (255 - v) * (f - 1) : v * f)));
  return `rgb(${k(r)},${k(g)},${k(b)})`;
}
export function rgba(c, a) { const [r, g, b] = hex(c); return `rgba(${r},${g},${b},${a})`; }

// ---------- terrain ----------
const TILE = {
  g: { top: '#638f3f', lt: '#7aa94c', dk: '#4f7b33', l: '#6d4b2f', r: '#563a24', tuft: true },
  G: { top: '#4b7a36', lt: '#5f8f40', dk: '#3b6329', l: '#6d4b2f', r: '#563a24', tuft: true },
  d: { top: '#a07d50', lt: '#b5925f', dk: '#876840', l: '#6d4b2f', r: '#563a24' },
  s: { top: '#8f8b84', lt: '#a8a39a', dk: '#73706a', l: '#6e6a63', r: '#56534d', cobble: true },
  b: { top: '#958d80', lt: '#ada493', dk: '#7a7366', l: '#6e665a', r: '#575047', cobble: true, arch: true },
  w: { top: '#2d6b90', lt: '#58a0c6', dk: '#235979', l: '#1e4c69', r: '#173d55', water: true },
  r: { top: '#81776a', lt: '#9a8f80', dk: '#685f54', l: '#5f574c', r: '#4b443b', rock: true },
};

/** Screen point of a tile-grid corner (gx, gy) at height h. */
export function P(m, gx, gy, h) {
  return { x: m.ox + (gx - gy) * TW / 2, y: m.oy + (gx + gy) * TH / 2 - h * HS };
}
/** Screen point at (u, v) inside tile (x, y)'s top face; u runs toward the right corner, v toward the left. */
export function UV(m, x, y, u, v) {
  const h = m.h[y][x];
  const t = P(m, x, y, h), r = P(m, x + 1, y, h), l = P(m, x, y + 1, h);
  return { x: t.x + u * (r.x - t.x) + v * (l.x - t.x), y: t.y + u * (r.y - t.y) + v * (l.y - t.y) };
}
export function center(m, x, y) { return UV(m, x, y, 0.5, 0.5); }

function poly(ctx, pts, fill, stroke, lw = 1) {
  ctx.beginPath(); ctx.moveTo(pts[0].x, pts[0].y);
  for (const p of pts.slice(1)) ctx.lineTo(p.x, p.y);
  ctx.closePath();
  if (fill) { ctx.fillStyle = fill; ctx.fill(); }
  if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = lw; ctx.stroke(); }
}

function speckle(ctx, box, rand, lt, dk, density = 0.3) {
  for (let y = box.y0; y < box.y1; y += 4)
    for (let x = box.x0; x < box.x1; x += 4) {
      const q = rand();
      if (q < density * 0.55) { ctx.fillStyle = lt; ctx.fillRect(x, y, 4, 4); }
      else if (q < density) { ctx.fillStyle = dk; ctx.fillRect(x, y, 4, 4); }
    }
}

function drawTile(ctx, m, x, y, rand) {
  const h = m.h[y][x], T = TILE[m.t[y][x]];
  const top = P(m, x, y, h), r = P(m, x + 1, y, h), b = P(m, x + 1, y + 1, h), l = P(m, x, y + 1, h);
  const depth = (h + m.base) * HS;
  const dn = p => ({ x: p.x, y: p.y + depth });

  // side faces with strata
  poly(ctx, [l, b, dn(b), dn(l)], T.l);
  poly(ctx, [b, r, dn(r), dn(b)], T.r);
  ctx.save();
  ctx.beginPath(); ctx.moveTo(l.x, l.y); ctx.lineTo(b.x, b.y); ctx.lineTo(r.x, r.y);
  ctx.lineTo(r.x, r.y + depth); ctx.lineTo(b.x, b.y + depth); ctx.lineTo(l.x, l.y + depth); ctx.closePath(); ctx.clip();
  speckle(ctx, { x0: l.x, x1: r.x, y0: Math.min(l.y, r.y), y1: b.y + depth }, rand, 'rgba(255,255,255,0.06)', 'rgba(0,0,0,0.12)', 0.25);
  ctx.strokeStyle = 'rgba(0,0,0,0.22)'; ctx.lineWidth = 2;
  for (let k = 1; k <= h + m.base; k++) {
    ctx.beginPath(); ctx.moveTo(l.x, l.y + k * HS); ctx.lineTo(b.x, b.y + k * HS); ctx.lineTo(r.x, r.y + k * HS); ctx.stroke();
  }
  if (T.water) { ctx.fillStyle = 'rgba(120,200,255,0.18)'; ctx.fillRect(l.x, l.y, r.x - l.x, 8); }
  ctx.restore();
  // grass lip hanging over the dirt side
  if (T.tuft) {
    ctx.fillStyle = T.dk;
    for (let i = 0; i < 14; i++) {
      const f = i / 14, side = i < 7, p0 = side ? l : b, p1 = side ? b : r, ff = (f * 2) % 1;
      const px = p0.x + (p1.x - p0.x) * ff, py = p0.y + (p1.y - p0.y) * ff;
      ctx.fillRect(Math.round(px), Math.round(py), 8, 4 + Math.floor(rand() * 8));
    }
  }
  if (T.arch) {
    const a = { x: (l.x + b.x) / 2, y: (l.y + b.y) / 2 + depth };
    ctx.fillStyle = '#1d1a16';
    ctx.beginPath(); ctx.ellipse(a.x, a.y, 30, HS * 1.2, -0.46, Math.PI, 0); ctx.fill();
  }

  // top face
  ctx.save();
  poly(ctx, [top, r, b, l], T.top);
  ctx.clip();
  const box = { x0: l.x, x1: r.x, y0: top.y, y1: b.y };
  if (T.water) {
    ctx.fillStyle = T.lt;
    for (let i = 0; i < 9; i++) ctx.fillRect(box.x0 + rand() * TW, box.y0 + rand() * TH, 8 + rand() * 18, 3);
    ctx.fillStyle = 'rgba(255,255,255,0.8)';
    for (let i = 0; i < 4; i++) ctx.fillRect(box.x0 + rand() * TW, box.y0 + rand() * TH, 3, 3);
  } else {
    speckle(ctx, box, rand, T.lt, T.dk, T.rock ? 0.4 : 0.3);
  }
  if (T.cobble) {
    for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) {
      const pts = [UV(m, x, y, i / 3, j / 3), UV(m, x, y, (i + 1) / 3, j / 3), UV(m, x, y, (i + 1) / 3, (j + 1) / 3), UV(m, x, y, i / 3, (j + 1) / 3)];
      poly(ctx, pts, `rgba(${rand() < 0.5 ? '255,255,255' : '0,0,0'},${0.05 + rand() * 0.08})`, 'rgba(40,36,30,0.55)', 2);
    }
  }
  if (T.tuft) {
    for (let i = 0; i < 6; i++) {
      const p = UV(m, x, y, 0.1 + rand() * 0.8, 0.1 + rand() * 0.8);
      ctx.fillStyle = T.lt; ctx.fillRect(Math.round(p.x), Math.round(p.y), 4, 8);
      ctx.fillRect(Math.round(p.x) - 4, Math.round(p.y) + 4, 4, 4); ctx.fillRect(Math.round(p.x) + 4, Math.round(p.y) + 2, 4, 6);
    }
    if (rand() < 0.35) {                                   // a few flowers
      const p = UV(m, x, y, 0.2 + rand() * 0.6, 0.2 + rand() * 0.6);
      ctx.fillStyle = ['#f6e27a', '#f29bc2', '#ffffff', '#b8a4ff'][Math.floor(rand() * 4)];
      ctx.fillRect(Math.round(p.x), Math.round(p.y), 4, 4); ctx.fillRect(Math.round(p.x) + 8, Math.round(p.y) + 4, 4, 4);
    }
  }
  ctx.restore();
  // rim light on the two back edges, a dark line on the front edges
  ctx.strokeStyle = 'rgba(255,240,200,0.35)'; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(l.x, l.y); ctx.lineTo(top.x, top.y); ctx.lineTo(r.x, r.y); ctx.stroke();
  ctx.strokeStyle = 'rgba(0,0,0,0.25)';
  ctx.beginPath(); ctx.moveTo(l.x, l.y); ctx.lineTo(b.x, b.y); ctx.lineTo(r.x, r.y); ctx.stroke();
}

const MARK = {
  move:   { fill: 'rgba(90,170,255,0.32)',  line: 'rgba(170,220,255,0.95)' },
  attack: { fill: 'rgba(255,90,80,0.26)',   line: 'rgba(255,150,140,0.9)' },
  target: { fill: 'rgba(255,70,60,0.45)',   line: 'rgba(255,230,200,1)' },
  aoe:    { fill: 'rgba(255,190,70,0.30)',  line: 'rgba(255,220,140,0.95)' },
  path:   { fill: 'rgba(255,255,255,0.18)', line: 'rgba(255,255,255,0.7)' },
};
function drawMark(ctx, m, x, y, kind) {
  const k = MARK[kind], i = 0.07;
  const pts = [UV(m, x, y, i, i), UV(m, x, y, 1 - i, i), UV(m, x, y, 1 - i, 1 - i), UV(m, x, y, i, 1 - i)];
  poly(ctx, pts, k.fill, k.line, 2.5);
  if (kind === 'target') {
    const c = center(m, x, y);
    ctx.strokeStyle = '#fff4dc'; ctx.lineWidth = 3;
    for (const [dx, dy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
      ctx.beginPath(); ctx.moveTo(c.x + dx * 30, c.y + dy * 9); ctx.lineTo(c.x + dx * 42, c.y + dy * 15); ctx.stroke();
    }
  }
}

// ---------- pixel sprites ----------
const BODY = [
  '....hhhh....',
  '...hhhhhh...',
  '..hhhhhhhh..',
  '..hSSSSSSh..',
  '..SSkSSkSS..',
  '..SSSSSSSS..',
  '...SSSSSS...',
  '..aaOOOOaa..',
  '.SaOOooOOaS.',
  '.SOOOooOOOS.',
  '.S.OOooOO.S.',
  '...OOOOOO...',
  '...bbbbbb...',
  '...LLLLLL...',
  '...LL..LL...',
  '...LL..LL...',
  '..BBB..BBB..',
];

/** Builds a sprite as a map of "x,y" -> colour (grid units, may extend past the body for hats and weapons). */
export function buildSprite(u) {
  const px = new Map();
  const set = (x, y, c) => px.set(`${x},${y}`, c);
  const pal = {
    h: u.hair || '#5b3a22', S: u.skin || '#f0c49a', k: u.eye || '#1b1726', O: u.outfit || '#4f6d9a',
    o: shade(u.outfit || '#4f6d9a', 0.72), a: u.accent || '#c9a24a', b: u.belt || '#4a3322',
    L: u.legs || '#3b3346', B: u.boots || '#2a2018',
  };
  BODY.forEach((row, y) => [...row].forEach((ch, x) => {
    if (ch === '.') return;
    let c = pal[ch];
    if (ch === 'h' && u.head === 'helmet') c = u.helm || '#a9b3bf';
    if (ch === 'h' && u.head === 'hood') c = u.hood || shade(pal.O, 0.8);
    set(x, y, c);
  }));
  if (u.head === 'hood') { set(2, 4, shade(u.hood || pal.O, 0.8)); set(9, 4, shade(u.hood || pal.O, 0.8)); set(2, 5, shade(u.hood || pal.O, 0.7)); set(9, 5, shade(u.hood || pal.O, 0.7)); }
  if (u.head === 'helmet') { set(5, -1, u.plume || pal.a); set(6, -1, u.plume || pal.a); set(6, -2, u.plume || pal.a); for (let x = 2; x <= 9; x++) set(x, 3, shade(u.helm || '#a9b3bf', 0.8)); }
  if (u.longHair) for (let y = 3; y <= 7; y++) { set(2, y, pal.h); set(9, y, pal.h); }
  if (u.beard) { for (let x = 3; x <= 8; x++) set(x, 6, pal.h); for (let x = 4; x <= 7; x++) set(x, 7, pal.h); set(3, 5, pal.h); set(8, 5, pal.h); }
  if (u.ears) { set(0, 3, pal.S); set(1, 4, pal.S); set(11, 3, pal.S); set(10, 4, pal.S); set(-1, 2, pal.S); set(12, 2, pal.S); }
  if (u.horns) { const hc = u.hornColor || '#2b2233'; set(3, -1, hc); set(2, -2, hc); set(8, -1, hc); set(9, -2, hc); set(3, 0, hc); set(8, 0, hc); }
  if (u.hat) {
    const c = u.hat, d = shade(u.hat, 0.7);
    for (let x = 0; x <= 11; x++) set(x, 1, d);
    for (let x = 2; x <= 9; x++) set(x, 0, c);
    for (let x = 3; x <= 8; x++) set(x, -1, c);
    for (let x = 4; x <= 7; x++) set(x, -2, c);
    for (let x = 5; x <= 7; x++) set(x, -3, c);
    set(6, -4, c); set(7, -4, c); set(7, -5, c); set(8, -5, c); set(8, -6, c);
    set(4, 0, u.hatBand || '#e3c35a'); set(5, 0, u.hatBand || '#e3c35a'); set(6, 0, u.hatBand || '#e3c35a');
  }
  const steel = '#d9e1ea', wood = '#6b4524';
  switch (u.weapon) {
    case 'sword':
      for (let y = 2; y <= 9; y++) set(11, y, steel); set(11, 1, '#ffffff');
      set(10, 10, pal.a); set(11, 10, pal.a); set(12, 10, pal.a); set(11, 11, wood); break;
    case 'dagger':
      set(11, 7, steel); set(11, 8, steel); set(11, 9, '#ffffff'); set(11, 10, wood);
      set(0, 8, steel); set(0, 9, steel); set(0, 10, wood); break;
    case 'staff':
      for (let y = -1; y <= 16; y++) set(0, y, wood);
      set(-1, -3, u.orb || '#7fd6ff'); set(0, -3, u.orb || '#7fd6ff'); set(-1, -2, u.orb || '#7fd6ff'); set(0, -2, '#ffffff'); break;
    case 'mace':
      for (let y = 6; y <= 11; y++) set(11, y, wood);
      for (let x = 10; x <= 12; x++) { set(x, 4, steel); set(x, 5, steel); } set(11, 3, steel); break;
    case 'bow':
      [[11, 3], [12, 4], [12, 5], [13, 6], [13, 7], [13, 8], [13, 9], [12, 10], [12, 11], [11, 12]].forEach(([x, y]) => set(x, y, wood));
      for (let y = 4; y <= 11; y++) set(11, y, '#e8e2cf'); break;
    case 'axe':
      for (let y = 2; y <= 11; y++) set(11, y, wood);
      for (let y = 2; y <= 5; y++) { set(12, y, steel); set(13, y, steel); } set(14, 3, steel); set(14, 4, steel); break;
    case 'tome':
      for (let x = -1; x <= 1; x++) for (let y = 9; y <= 11; y++) set(x, y, u.tome || '#7a2f3f'); set(0, 10, '#e3c35a'); break;
  }
  if (u.shield) {
    const c = u.shield, rim = '#c9cfd8';
    for (let x = -2; x <= 1; x++) for (let y = 7; y <= 12; y++) set(x, y, (x === -2 || x === 1 || y === 7 || y === 12) ? rim : c);
    set(-1, 9, '#f0d27a'); set(0, 9, '#f0d27a'); set(-1, 10, '#f0d27a'); set(0, 10, '#f0d27a');
  }
  return px;
}

/** Draws a sprite with a dark outline and simple top-left lighting; (x, y) is the bottom-centre of the feet. */
export function drawSprite(ctx, u, x, y, s, opts = {}) {
  const px = buildSprite(u);
  const ox = Math.round(x - 6 * s), oy = Math.round(y - 17 * s);
  const outline = opts.outline || '#140f1a';
  ctx.fillStyle = outline;
  for (const key of px.keys()) {
    const [gx, gy] = key.split(',').map(Number);
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]])
      if (!px.has(`${gx + dx},${gy + dy}`)) ctx.fillRect(ox + (gx + dx) * s, oy + (gy + dy) * s, s, s);
  }
  for (const [key, c] of px) {
    const [gx, gy] = key.split(',').map(Number);
    let col = c;
    if (!px.has(`${gx - 1},${gy}`) || !px.has(`${gx},${gy - 1}`)) col = shade(c, 1.18);
    else if (gx >= 7 && gy >= 7) col = shade(c, 0.86);
    ctx.fillStyle = opts.tint ? opts.tint : col;
    ctx.fillRect(ox + gx * s, oy + gy * s, s, s);
  }
}

/** Head-and-shoulders portrait into a canvas element. */
export function portrait(canvas, u, bg) {
  const ctx = canvas.getContext('2d'), w = canvas.width, h = canvas.height;
  ctx.imageSmoothingEnabled = false;
  const g = ctx.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, shade(bg, 1.25)); g.addColorStop(1, shade(bg, 0.55));
  ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
  ctx.fillStyle = 'rgba(255,255,255,0.08)';
  for (let i = 0; i < w; i += 8) ctx.fillRect(i, 0, 4, h);
  const s = Math.floor(w / 12);
  drawSprite(ctx, { ...u, weapon: undefined, shield: undefined }, w / 2, h + 7.2 * s, s);
}

// ---------- props ----------
function pixelBlob(ctx, cx, cy, circles, cell, base, rand) {
  const cells = new Map();
  for (const c of circles)
    for (let gy = Math.floor(c.y - c.r); gy <= c.y + c.r; gy++)
      for (let gx = Math.floor(c.x - c.r); gx <= c.x + c.r; gx++)
        if ((gx - c.x) ** 2 + (gy - c.y) ** 2 <= c.r * c.r) cells.set(`${gx},${gy}`, [gx, gy]);
  ctx.fillStyle = '#16200f';
  for (const [gx, gy] of cells.values())
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]])
      if (!cells.has(`${gx + dx},${gy + dy}`)) ctx.fillRect(cx + (gx + dx) * cell, cy + (gy + dy) * cell, cell, cell);
  for (const [gx, gy] of cells.values()) {
    const lightness = 1.25 - (gx + gy) * 0.035 + (rand() - 0.5) * 0.18;
    ctx.fillStyle = shade(base, Math.max(0.55, lightness));
    ctx.fillRect(cx + gx * cell, cy + gy * cell, cell, cell);
  }
}

function drawTree(ctx, p, rand, kind = 'oak') {
  ctx.fillStyle = 'rgba(0,0,0,0.28)';
  ctx.beginPath(); ctx.ellipse(p.x + 10, p.y + 4, 44, 14, 0, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = '#4a2f1c'; ctx.fillRect(p.x - 7, p.y - 46, 14, 50);
  ctx.fillStyle = '#5f3e25'; ctx.fillRect(p.x - 7, p.y - 46, 5, 50);
  const base = kind === 'pine' ? '#2f6a3a' : kind === 'autumn' ? '#c0702e' : '#4f8a3a';
  const circles = kind === 'pine'
    ? [{ x: 0, y: -4, r: 3 }, { x: 0, y: -1, r: 4.5 }, { x: 0, y: 3, r: 6 }]
    : [{ x: -3, y: -1, r: 5 }, { x: 3, y: -2, r: 5.5 }, { x: 0, y: -6, r: 5 }, { x: 0, y: 2, r: 5 }];
  pixelBlob(ctx, Math.round(p.x), Math.round(p.y - 92), circles, 6, base, rand);
}

function drawLantern(ctx, p) {
  ctx.fillStyle = '#231a14'; ctx.fillRect(p.x - 3, p.y - 74, 6, 76);
  ctx.fillRect(p.x - 3, p.y - 74, 18, 4);
  ctx.fillStyle = '#ffcf6b'; ctx.fillRect(p.x + 8, p.y - 70, 10, 12);
  ctx.fillStyle = '#fff3c4'; ctx.fillRect(p.x + 11, p.y - 67, 4, 6);
  return { x: p.x + 13, y: p.y - 64, r: 150, c: '255,170,80', a: 0.55 };
}

function drawCrate(ctx, m, x, y, rand) {
  const c = center(m, x, y);
  const w = 26, h = 26;
  poly(ctx, [{ x: c.x - w, y: c.y - h }, { x: c.x, y: c.y - h + 13 }, { x: c.x, y: c.y + 13 }, { x: c.x - w, y: c.y }], '#8a5d33', '#2a1a0e', 2);
  poly(ctx, [{ x: c.x, y: c.y - h + 13 }, { x: c.x + w, y: c.y - h }, { x: c.x + w, y: c.y }, { x: c.x, y: c.y + 13 }], '#6c4626', '#2a1a0e', 2);
  poly(ctx, [{ x: c.x - w, y: c.y - h }, { x: c.x, y: c.y - h - 13 }, { x: c.x + w, y: c.y - h }, { x: c.x, y: c.y - h + 13 }], '#a8774a', '#2a1a0e', 2);
}

/** A plastered house with a hipped roof on footprint [x0..x1] x [y0..y1]. Returns its window lights. */
function drawHouse(ctx, m, hs) {
  const { x0, y0, x1, y1, walls = 3, roof = '#9a3d33', plaster = '#e4d6b8' } = hs;
  const g = m.h[y1][x1];
  const c = (gx, gy, h) => P(m, gx, gy, g + h);
  const L = c(x0, y1 + 1, 0), B = c(x1 + 1, y1 + 1, 0), R = c(x1 + 1, y0, 0);
  const up = (p) => ({ x: p.x, y: p.y - walls * HS });
  // walls
  poly(ctx, [L, B, up(B), up(L)], plaster, '#2a2018', 2);
  poly(ctx, [B, R, up(R), up(B)], shade(plaster, 0.78), '#2a2018', 2);
  // timber frame
  ctx.strokeStyle = '#5a3a22'; ctx.lineWidth = 6;
  for (const [p, q] of [[L, B], [B, R]]) {
    ctx.beginPath(); ctx.moveTo(up(p).x, up(p).y + 4); ctx.lineTo(up(q).x, up(q).y + 4); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(p.x, p.y - 3); ctx.lineTo(q.x, q.y - 3); ctx.stroke();
  }
  ctx.beginPath(); ctx.moveTo(B.x, B.y); ctx.lineTo(B.x, B.y - walls * HS); ctx.stroke();
  // windows and a door
  const lights = [];
  const win = (p, q, f, door) => {
    const cx = p.x + (q.x - p.x) * f, cy = p.y + (q.y - p.y) * f, slope = (q.y - p.y) / (q.x - p.x);
    const w = 16, hh = door ? 42 : 22, base = door ? 0 : walls * HS * 0.45;
    const pts = [
      { x: cx - w, y: cy - base - slope * w }, { x: cx + w, y: cy - base + slope * w },
      { x: cx + w, y: cy - base - hh + slope * w }, { x: cx - w, y: cy - base - hh - slope * w }];
    poly(ctx, pts, door ? '#4a2c18' : '#ffc76a', '#2a1a0e', 3);
    if (!door) {
      ctx.fillStyle = '#fff1c2'; ctx.fillRect(cx - 4, cy - base - hh + 6, 8, 8);
      lights.push({ x: cx, y: cy - base - hh / 2, r: 110, c: '255,180,90', a: 0.45 });
    }
  };
  win(L, B, 0.28); win(L, B, 0.72, true); win(B, R, 0.5);
  // roof (hipped): ridge runs along the longer axis
  const o = 0.25, rh = walls + 2.4;
  const E = [c(x0 - o, y0 - o, walls), c(x1 + 1 + o, y0 - o, walls), c(x1 + 1 + o, y1 + 1 + o, walls), c(x0 - o, y1 + 1 + o, walls)];
  const along = (x1 - x0) >= (y1 - y0);
  const mx = (x0 + x1 + 1) / 2, my = (y0 + y1 + 1) / 2;
  const R1 = along ? c(x0 + 0.6, my, rh) : c(mx, y0 + 0.6, rh);
  const R2 = along ? c(x1 + 0.4, my, rh) : c(mx, y1 + 0.4, rh);
  const faces = along
    ? [[E[0], E[1], R2, R1, 0.7], [E[3], E[0], R1, 0.62], [E[1], E[2], R2, 0.8], [E[2], E[3], R1, R2, 1.05]]
    : [[E[0], E[1], R1, 0.7], [E[3], E[0], R1, R2, 0.62], [E[1], E[2], R2, R1, 0.85], [E[2], E[3], R2, 1.05]];
  for (const f of faces) {
    const k = f.pop();
    poly(ctx, f, shade(roof, k), '#24140f', 2.5);
    // shingle lines
    ctx.save(); poly(ctx, f, null); ctx.clip();
    ctx.strokeStyle = 'rgba(0,0,0,0.18)'; ctx.lineWidth = 2;
    const minY = Math.min(...f.map(p => p.y)), maxY = Math.max(...f.map(p => p.y));
    for (let yy = minY; yy < maxY; yy += 10) { ctx.beginPath(); ctx.moveTo(Math.min(...f.map(p => p.x)), yy); ctx.lineTo(Math.max(...f.map(p => p.x)), yy); ctx.stroke(); }
    ctx.restore();
  }
  // chimney
  const ch = c(x0 + 0.6, y0 + 0.6, rh - 0.8);
  ctx.fillStyle = '#6d625a'; ctx.fillRect(ch.x - 9, ch.y - 30, 18, 34);
  ctx.fillStyle = '#4b433d'; ctx.fillRect(ch.x + 1, ch.y - 30, 8, 34);
  return lights;
}

// ---------- scene ----------
/** Parses rows of terrain letters and height digits into a map centred on (cx, cy). */
export function makeMap(types, heights, cx, cy) {
  const t = types.map(r => r.replace(/\s/g, '').split(''));
  const h = heights.map(r => r.replace(/\s/g, '').split('').map(Number));
  const W = t[0].length, H = t.length;
  return { t, h, W, H, base: 2, ox: cx - (W - H) * TW / 4, oy: cy - (W + H) * TH / 4, marks: {}, props: [], units: [] };
}

function sky(ctx, w, h, mood, rand) {
  const g = ctx.createLinearGradient(0, 0, 0, h);
  if (mood === 'dusk') { g.addColorStop(0, '#1e2350'); g.addColorStop(0.45, '#6a4a7a'); g.addColorStop(0.75, '#e08a5c'); g.addColorStop(1, '#f2c27a'); }
  else { g.addColorStop(0, '#5d93c9'); g.addColorStop(0.5, '#a9cbe0'); g.addColorStop(0.85, '#f3dcae'); g.addColorStop(1, '#f6e6c4'); }
  ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
  if (mood === 'dusk') {
    ctx.fillStyle = 'rgba(255,255,255,0.8)';
    for (let i = 0; i < 90; i++) { const s = rand() < 0.2 ? 3 : 2; ctx.fillRect(rand() * w, rand() * h * 0.35, s, s); }
  }
  // pixel clouds
  ctx.fillStyle = mood === 'dusk' ? 'rgba(255,190,170,0.35)' : 'rgba(255,255,255,0.7)';
  for (let i = 0; i < 7; i++) {
    const cx = rand() * w, cy = 60 + rand() * 220, n = 4 + Math.floor(rand() * 5);
    for (let k = 0; k < n; k++) ctx.fillRect(Math.round((cx + k * 34 - rand() * 20) / 4) * 4, Math.round((cy - rand() * 24) / 4) * 4, 60 + rand() * 50, 20 + rand() * 14);
  }
  // layered mountains with haze
  const layers = mood === 'dusk'
    ? [['#4a3d6b', 0.50, 140], ['#3a2f55', 0.58, 110], ['#2b2342', 0.66, 80]]
    : [['#8fa9c4', 0.48, 150], ['#6f8fab', 0.56, 120], ['#56778f', 0.64, 90]];
  for (const [col, base, amp] of layers) {
    ctx.fillStyle = col; ctx.beginPath(); ctx.moveTo(0, h);
    let y = h * base;
    for (let x = 0; x <= w; x += 8) {
      y += (rand() - 0.5) * 22; y = Math.max(h * base - amp, Math.min(h * base + 20, y));
      ctx.lineTo(x, Math.round(y / 4) * 4);
    }
    ctx.lineTo(w, h); ctx.fill();
  }
  const fog = ctx.createLinearGradient(0, h * 0.55, 0, h);
  fog.addColorStop(0, 'rgba(255,255,255,0)'); fog.addColorStop(1, mood === 'dusk' ? 'rgba(80,60,110,0.85)' : 'rgba(225,235,240,0.85)');
  ctx.fillStyle = fog; ctx.fillRect(0, 0, w, h);
}

/**
 * Renders the world. Units are { x, y, sprite, ring?, active?, scale? }; props are { kind, x, y, ... }.
 * Returns screen anchors for each unit id (feet and head) so pages can place DOM overlays.
 */
export function render(canvas, m, opts = {}) {
  const ctx = canvas.getContext('2d'), w = canvas.width, h = canvas.height;
  ctx.imageSmoothingEnabled = false;
  const rand = rng(opts.seed || 7);
  sky(ctx, w, h, opts.mood, rand);

  const lights = [...(opts.lights || [])];
  const anchors = {};
  const order = [];
  for (let y = 0; y < m.H; y++) for (let x = 0; x < m.W; x++) order.push([x, y]);
  order.sort((a, b) => (a[0] + a[1]) - (b[0] + b[1]) || a[0] - b[0]);
  const at = (list, x, y) => list.filter(o => o.x === x && o.y === y);

  for (const [x, y] of order) {
    if (m.t[y][x] === '.') continue;
    drawTile(ctx, m, x, y, rand);
    const mk = m.marks[`${x},${y}`];
    if (mk) drawMark(ctx, m, x, y, mk);
    for (const p of at(m.props, x, y)) {
      const c = center(m, x, y);
      if (p.kind === 'tree') drawTree(ctx, c, rand, p.variant);
      if (p.kind === 'lantern') lights.push(drawLantern(ctx, c));
      if (p.kind === 'crate') drawCrate(ctx, m, x, y, rand);
      if (p.kind === 'house') lights.push(...drawHouse(ctx, m, p));
      if (p.kind === 'well') {
        ctx.fillStyle = '#6f6a62'; ctx.beginPath(); ctx.ellipse(c.x, c.y - 10, 34, 17, 0, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = '#1c2b38'; ctx.beginPath(); ctx.ellipse(c.x, c.y - 14, 24, 11, 0, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = '#4a2f1c'; ctx.fillRect(c.x - 30, c.y - 70, 6, 60); ctx.fillRect(c.x + 24, c.y - 70, 6, 60);
        poly(ctx, [{ x: c.x - 40, y: c.y - 66 }, { x: c.x, y: c.y - 92 }, { x: c.x + 40, y: c.y - 66 }], '#8a3a2e', '#24140f', 2);
      }
    }
    for (const u of at(m.units, x, y)) {
      const c = center(m, x, y);
      const s = u.scale || 5;
      ctx.fillStyle = 'rgba(0,0,0,0.35)';
      ctx.beginPath(); ctx.ellipse(c.x, c.y + 2, 9 * s * 0.6, 3 * s * 0.6, 0, 0, Math.PI * 2); ctx.fill();
      if (u.ring) {
        ctx.strokeStyle = u.ring; ctx.lineWidth = 3;
        ctx.beginPath(); ctx.ellipse(c.x, c.y + 2, 34, 13, 0, 0, Math.PI * 2); ctx.stroke();
      }
      drawSprite(ctx, u.sprite, c.x, c.y + 2, s);
      const head = { x: c.x, y: c.y - 17 * s - (u.sprite.hat ? 6 * s : u.sprite.head === 'helmet' ? 2 * s : 0) };
      if (u.active) {
        const ay = head.y - 26;
        poly(ctx, [{ x: c.x - 14, y: ay - 18 }, { x: c.x + 14, y: ay - 18 }, { x: c.x, y: ay }], '#ffd76a', '#3a2a08', 3);
        lights.push({ x: c.x, y: c.y - 40, r: 120, c: '255,215,120', a: 0.28 });
      }
      if (u.crown) {                                       // the party leader's crown
        const cy = head.y - 6, k = 4;
        ctx.fillStyle = '#3a2a08';
        ctx.fillRect(c.x - 5 * k, cy - 3 * k, 10 * k, 5 * k + 2);
        ctx.fillStyle = '#ffd76a';
        ctx.fillRect(c.x - 4 * k, cy, 8 * k, 1.5 * k);
        for (const dx of [-4, -1, 2]) ctx.fillRect(c.x + dx * k, cy - 2 * k, 2 * k, 2 * k);
        ctx.fillStyle = '#ff6a7a'; ctx.fillRect(c.x - 0.5 * k, cy - k, k, k);
        lights.push({ x: c.x, y: cy, r: 70, c: '255,215,120', a: 0.4 });
      }
      if (u.glow) lights.push({ x: c.x, y: c.y - 40, r: 110, c: u.glow, a: 0.35 });
      if (u.id) anchors[u.id] = { feet: c, head };
    }
  }

  grade(ctx, w, h, opts, lights, rand);
  return anchors;
}

/** HD-2D post: colour grade, point lights, bloom, god rays, tilt-shift depth of field, vignette, motes. */
function grade(ctx, w, h, opts, lights, rand) {
  const dusk = opts.mood === 'dusk';
  ctx.save();
  ctx.globalCompositeOperation = 'multiply';
  ctx.fillStyle = dusk ? 'rgba(120,105,170,1)' : 'rgba(255,244,226,1)';
  ctx.fillRect(0, 0, w, h);
  ctx.globalCompositeOperation = 'lighter';
  for (const l of lights) {
    const g = ctx.createRadialGradient(l.x, l.y, 0, l.x, l.y, l.r);
    g.addColorStop(0, `rgba(${l.c},${l.a})`); g.addColorStop(1, `rgba(${l.c},0)`);
    ctx.fillStyle = g; ctx.fillRect(l.x - l.r, l.y - l.r, l.r * 2, l.r * 2);
  }
  ctx.globalCompositeOperation = 'screen';
  const sun = ctx.createLinearGradient(0, 0, w * 0.7, h * 0.8);
  sun.addColorStop(0, dusk ? 'rgba(255,140,90,0.25)' : 'rgba(255,226,160,0.42)'); sun.addColorStop(1, 'rgba(0,0,0,0)');
  ctx.fillStyle = sun; ctx.fillRect(0, 0, w, h);
  if (!dusk) {                                             // god rays
    ctx.filter = 'blur(18px)';
    for (let i = 0; i < 5; i++) {
      const x = 120 + i * 260 + rand() * 80;
      ctx.fillStyle = 'rgba(255,240,200,0.10)';
      ctx.beginPath(); ctx.moveTo(x, -50); ctx.lineTo(x + 90, -50); ctx.lineTo(x + 520, h); ctx.lineTo(x + 330, h); ctx.fill();
    }
    ctx.filter = 'none';
  }
  ctx.restore();

  // bloom
  const tmp = document.createElement('canvas'); tmp.width = w; tmp.height = h;
  const t = tmp.getContext('2d');
  t.filter = 'blur(16px) brightness(1.15) saturate(1.2)'; t.drawImage(ctx.canvas, 0, 0);
  ctx.save(); ctx.globalCompositeOperation = 'screen'; ctx.globalAlpha = dusk ? 0.32 : 0.26; ctx.drawImage(tmp, 0, 0); ctx.restore();

  // tilt-shift depth of field: blur fades in toward the top and bottom of the frame
  const dof = document.createElement('canvas'); dof.width = w; dof.height = h;
  const d = dof.getContext('2d');
  d.filter = `blur(${opts.blur || 5}px)`; d.drawImage(ctx.canvas, 0, 0); d.filter = 'none';
  d.globalCompositeOperation = 'destination-in';
  const f = opts.focus || [0.3, 0.72];
  const mg = d.createLinearGradient(0, 0, 0, h);
  mg.addColorStop(0, 'rgba(0,0,0,1)'); mg.addColorStop(f[0], 'rgba(0,0,0,0)');
  mg.addColorStop(f[1], 'rgba(0,0,0,0)'); mg.addColorStop(1, 'rgba(0,0,0,1)');
  d.fillStyle = mg; d.fillRect(0, 0, w, h);
  ctx.drawImage(dof, 0, 0);

  if (opts.dim) { ctx.fillStyle = `rgba(6,8,20,${opts.dim})`; ctx.fillRect(0, 0, w, h); }

  // motes / embers
  for (let i = 0; i < 60; i++) {
    const x = rand() * w, y = rand() * h, s = rand() < 0.3 ? 4 : 2;
    ctx.fillStyle = dusk ? `rgba(255,190,110,${0.3 + rand() * 0.5})` : `rgba(255,250,220,${0.25 + rand() * 0.4})`;
    ctx.fillRect(Math.round(x), Math.round(y), s, s);
  }
  const v = ctx.createRadialGradient(w / 2, h / 2, h * 0.45, w / 2, h / 2, h * 1.05);
  v.addColorStop(0, 'rgba(0,0,0,0)'); v.addColorStop(1, 'rgba(0,0,0,0.6)');
  ctx.fillStyle = v; ctx.fillRect(0, 0, w, h);
}

// ---------- UI helpers ----------
/** An icosahedron-ish d20 as SVG markup. */
export function d20(n, { size = 96, face = '#f2e3b8', ink = '#2a1d0a', edge = '#7a5a1e', dim = false } = {}) {
  const o = dim ? 0.38 : 1;
  return `<svg width="${size}" height="${size}" viewBox="0 0 100 100" style="opacity:${o}">
    <defs><linearGradient id="dg${n}${size}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${shade(face, 1.15)}"/><stop offset="1" stop-color="${shade(face, 0.7)}"/></linearGradient></defs>
    <polygon points="50,2 93,27 93,75 50,98 7,75 7,27" fill="url(#dg${n}${size})" stroke="${edge}" stroke-width="3"/>
    <polygon points="50,24 79,70 21,70" fill="${shade(face, 1.08)}" stroke="${edge}" stroke-width="2.5"/>
    <g stroke="${edge}" stroke-width="2"><line x1="50" y1="2" x2="50" y2="24"/><line x1="7" y1="27" x2="50" y2="24"/><line x1="93" y1="27" x2="50" y2="24"/>
    <line x1="93" y1="27" x2="79" y2="70"/><line x1="93" y1="75" x2="79" y2="70"/><line x1="50" y1="98" x2="79" y2="70"/>
    <line x1="7" y1="27" x2="21" y2="70"/><line x1="7" y1="75" x2="21" y2="70"/><line x1="50" y1="98" x2="21" y2="70"/></g>
    <text x="50" y="${n >= 10 ? 61 : 62}" text-anchor="middle" font-family="Cinzel, serif" font-weight="800" font-size="${n >= 10 ? 26 : 30}" fill="${ink}">${n}</text></svg>`;
}

/** Tiny pixel-art item icons for inventories. */
export function itemIcon(canvas, kind) {
  const ctx = canvas.getContext('2d'), s = canvas.width / 12;
  const art = {
    potion: ['....kk......', '....ww......', '...kwwk.....', '...kwwk.....', '..krrrrk....', '.krrRrrrk...', '.krRRrrrk...', '.krrrrrrk...', '..krrrrk....', '...kkkk.....'],
    coin:   ['............', '...kkkk.....', '..kyyyyk....', '.kyYyyyyk...', '.kyYyyyyk...', '.kyyyyyyk...', '.kyyyyyyk...', '..kyyyyk....', '...kkkk.....', '............'],
    cloak:  ['...kkkk.....', '..kggggk....', '..kgyygk....', '.kggggggk...', '.kgGgggggk..', 'kgGggggggk..', 'kgGgggggggk.', 'kggggggggk..', '.kkkkkkkk...', '............'],
  }[kind];
  const pal = { k: '#1a1220', w: '#e8e2cf', r: '#d8394a', R: '#ff8b8b', y: '#f0c43a', Y: '#fff2a8', g: '#3f7d4a', G: '#6fb37a' };
  art.forEach((row, y) => [...row].forEach((ch, x) => { if (pal[ch]) { ctx.fillStyle = pal[ch]; ctx.fillRect((x + 1) * s, (y + 1) * s, s, s); } }));
}
