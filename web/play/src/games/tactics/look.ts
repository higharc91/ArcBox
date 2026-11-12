import { packBase, packJson } from "./pack";

/**
 * A character's look, composed in the browser from the pack's paper-doll layers, exactly as tools/tactics-assets composes the baked
 * presets (README "Composing a character"): the race's body, hair, beard, horns, tail and marks and the class's kit, stacked in the
 * manifest's front order, outlined once, then coloured from the palette ramps. The portrait is the same stack without the weapon and
 * off-hand, cropped to the build's head box over the ally backdrop. The recipe follows parts/creator.py's make_character with the race's
 * default look, on the class preset's palette for everything the race doesn't choose.
 */

export interface CreatorRace { build: string; body: string; tail: string | null; default: Record<string, string> }
export interface CreatorClass { gear: Record<string, string>; colours: Record<string, string> }
export interface Creator { races: Record<string, CreatorRace>; classes: Record<string, CreatorClass> }
export interface CharManifest {
  frame: { width: number; height: number };
  order: { front: string[] };
  slotIds: Record<string, number>;
  outline: { colour: string; mix: number };
  path: string;
  layerAliases?: Record<string, string>;
}
export interface Palettes { ramps: Record<string, Record<string, string[]>> }
export interface PortraitManifest { size: number; scale: number; crop: Record<string, [number, number, number, number]>; skipLayers: string[]; backdrops: Record<string, string> }
export interface Recipe { build: string; layers: Record<string, string>; palette: Record<string, string> }

/** The recipe for a race and class with the race's default look (make_character with no Look). */
export function recipe(race: CreatorRace, cls: CreatorClass, presetPalette: Record<string, string>): Recipe {
  const d = race.default;
  const layers: Record<string, string> = { body: race.body, ...cls.gear };
  const extra: [string, string | null | undefined][] = [["hair", d.hair], ["beard", d.beard], ["horns", d.horns], ["tail", race.tail], ["marks", d.marks]];
  for (const [layer, v] of extra) if (v && v !== "none") layers[layer] = v;
  const palette = { ...presetPalette };
  palette.skin = `skin/${d.skin}`;
  palette.hair = `hair/${d.hair_colour ?? "brown"}`;
  palette.eyes = `eyes/${d.eyes ?? "dark"}`;
  palette.horn = `horn/${d.horn ?? "ivory"}`;
  palette.paint = d.paint ? `paint/${d.paint}` : cls.colours.paint ?? "paint/woad";
  return { build: race.build, layers, palette };
}

/** A frame of ramp-map pixels: RGBA, R = slot × 16, G = shade × 32, A = coverage. */
export interface RampFrame { width: number; height: number; data: ArrayLike<number> }
/** A composed frame: a slot and shade per pixel (slot 0 is empty), and which pixels are outline. */
export interface Composite { width: number; height: number; slot: Uint8Array; shade: Uint8Array; outline: Uint8Array }

/** Stacks the layers in `order` (top layer wins), then outlines the silhouette once: right, left, below, above. */
export function compose(order: string[], frames: Record<string, RampFrame | undefined>, width: number, height: number): Composite {
  const n = width * height;
  const slot = new Uint8Array(n), shade = new Uint8Array(n), outline = new Uint8Array(n);
  for (const layer of order) {
    const f = frames[layer];
    if (!f) continue;
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
      const i = (y * f.width + x) * 4;
      if ((f.data[i + 3] ?? 0) === 0) continue;
      const s = Math.round((f.data[i] ?? 0) / 16);
      if (s === 0) continue;
      slot[y * width + x] = s;
      shade[y * width + x] = Math.round((f.data[i + 1] ?? 0) / 32);
    }
  }
  const filled = slot.slice();
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const p = y * width + x;
    if (filled[p]) continue;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) {
      const nx = x + dx, ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
      const q = ny * width + nx;
      if (filled[q]) {
        slot[p] = filled[q]!;
        shade[p] = 0;
        outline[p] = 1;
        break;
      }
    }
  }
  return { width, height, slot, shade, outline };
}

const hexRgb = (h: string) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));

/** The composite in colour: each slot's ramp from the palette, outline pixels mixed `mix` of the way to the outline colour. */
export function paint(c: Composite, slotIds: Record<string, number>, palette: Record<string, string>, ramps: Palettes["ramps"],
  outline: { colour: string; mix: number }): Uint8ClampedArray {
  const names: Record<number, string> = {};
  for (const [name, id] of Object.entries(slotIds)) names[id] = name;
  const ol = hexRgb(outline.colour);
  const out = new Uint8ClampedArray(c.width * c.height * 4);
  for (let p = 0; p < c.slot.length; p++) {
    const s = c.slot[p]!;
    if (!s) continue;
    const ref = palette[names[s] ?? ""]?.split("/");
    const ramp = ref ? ramps[ref[0]!]?.[ref[1]!] : undefined;
    const hex = ramp?.[c.outline[p] ? 0 : c.shade[p]!] ?? "#ff00ff";
    let rgb = hexRgb(hex);
    if (c.outline[p]) rgb = rgb.map((v, i) => Math.trunc(v * (1 - outline.mix) + ol[i]! * outline.mix));
    out.set([rgb[0]!, rgb[1]!, rgb[2]!, 255], p * 4);
  }
  return out;
}

/** The pixels of a look: the idle front frame and the portrait (before scaling), RGBA. */
export interface Look { sprite: { width: number; height: number; data: Uint8ClampedArray }; portrait: { width: number; height: number; data: Uint8ClampedArray } }

/** One frame (the front idle, frame 0) of a layer sheet as ramp-map pixels, read through a canvas. */
async function frameOf(src: string, w: number, h: number): Promise<RampFrame | undefined> {
  const img = new Image();
  img.src = src;
  try {
    await img.decode();
  } catch {
    return undefined;
  }
  const canvas = document.createElement("canvas");
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext("2d");
  if (!ctx) return undefined;
  ctx.drawImage(img, 0, 0, w, h, 0, 0, w, h);
  return ctx.getImageData(0, 0, w, h);
}

const cache = new Map<string, Promise<Look | null>>();

/** True where the browser can compose (a 2D canvas); jsdom and old browsers fall back to the class portrait. */
export function canCompose(): boolean {
  try {
    return typeof document !== "undefined" && !!document.createElement("canvas").getContext("2d");
  } catch {
    return false;
  }
}

/** How long a failed look is remembered before it's tried again (the pack may have been slow, or a request dropped). */
export const RETRY_MS = 3000;

/**
 * The look of a race and class, composed once per pair; null when the pack or a canvas isn't there. A failure is forgotten after
 * RETRY_MS, so the next ask tries again. Each layer sheet is decoded whole to read its first frame: a sheet is one small PNG (3 KB to
 * 42 KB), cached by the browser and shared by every race and class that wears it, so cropping the decode wasn't worth its complexity.
 */
export function lookFor(race: string, cls: string): Promise<Look | null> {
  const key = `${race}/${cls}`;
  let pending = cache.get(key);
  if (!pending) {
    pending = build(race, cls).catch(() => null).then((look) => {
      if (!look) setTimeout(() => { if (cache.get(key) === pending) cache.delete(key); }, RETRY_MS);
      return look;
    });
    cache.set(key, pending);
  }
  return pending;
}

async function build(race: string, cls: string): Promise<Look | null> {
  if (!canCompose()) return null;
  const [base, creator, manifest, palettes, portraits, preset] = await Promise.all([
    packBase(), packJson<Creator>("characters/creator.json"), packJson<CharManifest>("characters/manifest.json"),
    packJson<Palettes>("characters/palettes.json"), packJson<PortraitManifest>("portraits/manifest.json"),
    packJson<{ palette: Record<string, string> }>(`characters/presets/class_${cls}.json`),
  ]);
  const r = creator?.races[race], k = creator?.classes[cls];
  if (!base || !manifest || !palettes || !portraits || !preset || !r || !k) return null;
  const rec = recipe(r, k, preset.palette);
  const { width: w, height: h } = manifest.frame;
  const frames: Record<string, RampFrame | undefined> = {};
  await Promise.all(Object.entries(rec.layers).map(async ([layer, variant]) => {
    const path = layerPath(manifest, rec.build, layer, variant);
    frames[layer] = await frameOf(base + path, w, h);
  }));
  const sprite = paint(compose(manifest.order.front, frames, w, h), manifest.slotIds, rec.palette, palettes.ramps, manifest.outline);
  const bust = Object.fromEntries(Object.entries(frames).filter(([l]) => !portraits.skipLayers.includes(l)));
  const head = paint(compose(manifest.order.front, bust, w, h), manifest.slotIds, rec.palette, palettes.ramps, manifest.outline);
  const portrait = await portraitOf(head, w, h, portraits.crop[rec.build] ?? [6, 4, 26, 24], `${base}portraits/${portraits.backdrops.ally}`, portraits.size, portraits.scale);
  return { sprite: { width: w, height: h, data: sprite }, portrait };
}

/** The bust cropped to the build's head box, scaled `scale`× over the backdrop (already at full size). */
async function portraitOf(head: Uint8ClampedArray, w: number, h: number, crop: [number, number, number, number], backdrop: string,
  size: number, scale: number): Promise<Look["portrait"]> {
  const canvas = document.createElement("canvas");
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext("2d")!;
  ctx.imageSmoothingEnabled = false;
  const img = new Image();
  img.src = backdrop;
  try {
    await img.decode();
    ctx.drawImage(img, 0, 0, size, size);
  } catch { /* no backdrop: the bust alone */ }
  const [x0, y0] = crop;
  const crop20 = size / scale;
  for (let y = 0; y < crop20; y++) for (let x = 0; x < crop20; x++) {
    const sx = x0 + x, sy = y0 + y;
    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
    const i = (sy * w + sx) * 4;
    if (head[i + 3] === 0) continue;
    ctx.fillStyle = `rgb(${head[i]},${head[i + 1]},${head[i + 2]})`;
    ctx.fillRect(x * scale, y * scale, scale, scale);
  }
  const data = ctx.getImageData(0, 0, size, size);
  return { width: size, height: size, data: data.data };
}

/** Resolve the pack's explicit byte-identical layer aliases before requesting a sheet. */
export function layerPath(manifest: CharManifest, build: string, layer: string, variant: string): string {
  const relative = `${build}/${layer}/${variant}.png`;
  const alias = manifest.layerAliases?.[relative];
  return alias ? `characters/${alias}` : manifest.path.replace("{build}", build).replace("{layer}", layer).replace("{variant}", variant);
}
