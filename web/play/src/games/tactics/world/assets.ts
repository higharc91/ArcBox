import * as THREE from "three";
import { packBase, packJson } from "../pack";

/**
 * Pack art for the 3D world (the title flyover and the lobby backdrop): the manifests it reads, and textures made the
 * pixel-art way: nearest filtering, no mipmaps, no colour conversion (the pipeline works in the art's own sRGB values,
 * so an unlit pixel is exactly the colour the artist drew). Images are cached for the page; textures belong to the scene
 * that made them and are disposed with it.
 */

export interface Emissive {
  slot: string;
  light: { every: number; radius: number; lift: number; flicker: string; power: number };
  colour: Record<string, string>;
}

export interface TerrainTexture {
  kind: "top" | "side" | "lip" | "decal" | "wall";
  variants: Record<string, string>;
  frames: number;
  fps: number;
  size: [number, number];
  emissive?: Emissive;
}

export interface TerrainManifest { tile: number; span: number; heightStep: number; textures: Record<string, TerrainTexture> }

export interface PropLight {
  /** Frame 0's light points, or one list of points per frame (flames sway). */
  at: number[][] | number[][][];
  radius: number;
  flicker: string;
  colour: Record<string, string>;
  emissive?: string;
}

export interface PropEntry {
  variants: Record<string, string>;
  size: [number, number];
  anchor: [number, number];
  frames: number;
  fps: number;
  layer?: "ground";
  light?: PropLight;
}

export interface PropsManifest { props: Record<string, PropEntry>; lighting: { flicker: Record<string, number[]> } }
export interface DungeonManifest { textures: Record<string, TerrainTexture>; props: Record<string, PropEntry> }

export interface BackgroundsManifest {
  sky: { files: Record<string, string> };
  ranges: { size: [number, number]; files: Record<string, string>; parallax: { far: number; near: number } };
  clouds: string[];
  pairings: Record<string, [string, string]>;
}

export interface GradesManifest { size: number; grades: Record<string, string> }

/** Everything the world reads, loaded once. Null when the pack can't be reached. */
export interface WorldManifests {
  base: string;
  terrain: TerrainManifest;
  props: PropsManifest;
  dungeon: DungeonManifest | null;
  backgrounds: BackgroundsManifest;
  grades: GradesManifest | null;
}

let manifests: Promise<WorldManifests | null> | null = null;

export function loadManifests(): Promise<WorldManifests | null> {
  manifests ??= (async () => {
    const [base, terrain, props, dungeon, backgrounds, grades] = await Promise.all([
      packBase(),
      packJson<TerrainManifest>("terrain/manifest.json"),
      packJson<PropsManifest>("props/manifest.json"),
      packJson<DungeonManifest>("dungeon/manifest.json"),
      packJson<BackgroundsManifest>("backgrounds/manifest.json"),
      packJson<GradesManifest>("grades/manifest.json"),
    ]);
    if (!base || !terrain || !props || !backgrounds) {
      manifests = null;   // try again next time
      return null;
    }
    return { base, terrain, props, dungeon, backgrounds, grades };
  })();
  return manifests;
}

const images = new Map<string, Promise<HTMLImageElement | null>>();

/** A pack image by its full URL; null if it doesn't load. Cached for the page. */
export function loadImage(url: string): Promise<HTMLImageElement | null> {
  let pending = images.get(url);
  if (!pending) {
    pending = new Promise((resolve) => {
      const img = new Image();
      img.decoding = "async";
      img.onload = () => resolve(img);
      img.onerror = () => {
        console.warn("Couldn't load", url);
        images.delete(url);
        resolve(null);
      };
      img.src = url;
    });
    images.set(url, pending);
  }
  return pending;
}

/** Nearest-filtered, unmipped, colour-as-drawn: how every texture in the world is set up. */
export function pixelTexture<T extends THREE.Texture>(tex: T, repeat = true): T {
  tex.magFilter = THREE.NearestFilter;
  tex.minFilter = THREE.NearestFilter;
  tex.generateMipmaps = false;
  tex.colorSpace = THREE.NoColorSpace;
  tex.wrapS = tex.wrapT = repeat ? THREE.RepeatWrapping : THREE.ClampToEdgeWrapping;
  tex.needsUpdate = true;
  return tex;
}

/** A horizontal strip cut into one canvas per frame (frames sit left to right). */
export function sliceStrip(img: CanvasImageSource & { width: number; height: number }, frames: number): HTMLCanvasElement[] {
  const fw = Math.floor(img.width / Math.max(1, frames));
  return Array.from({ length: Math.max(1, frames) }, (_, i) => {
    const c = document.createElement("canvas");
    c.width = fw;
    c.height = img.height;
    c.getContext("2d")!.drawImage(img, i * fw, 0, fw, img.height, 0, 0, fw, img.height);
    return c;
  });
}

/**
 * The glowing pixels of a frame, as a mask: the pixels whose colour is the glow's (same hue, saturated, about as bright).
 * Glow is the brightest shades of the emissive `colour` (the manifest's rule). Matching hue as well as brightness keeps
 * pale stone and sky reflections, which can be brighter than the glow, out of it.
 */
export function glowMask(frame: HTMLCanvasElement, colour: string): HTMLCanvasElement {
  const target = new THREE.Color(colour);
  const th = hsv(target.r * 255, target.g * 255, target.b * 255);
  const out = document.createElement("canvas");
  out.width = frame.width;
  out.height = frame.height;
  const src = frame.getContext("2d")!.getImageData(0, 0, frame.width, frame.height);
  const d = src.data;
  for (let i = 0; i < d.length; i += 4) {
    const p = hsv(d[i]!, d[i + 1]!, d[i + 2]!);
    const dh = Math.min(Math.abs(p.h - th.h), 360 - Math.abs(p.h - th.h));
    const on = d[i + 3]! > 127 && p.s > 0.28 && dh < 40 && p.v >= th.v * 0.62;
    d[i] = d[i + 1] = d[i + 2] = on ? 255 : 0;
    d[i + 3] = 255;
  }
  out.getContext("2d")!.putImageData(src, 0, 0);
  return out;
}

function hsv(r: number, g: number, b: number): { h: number; s: number; v: number } {
  const max = Math.max(r, g, b), min = Math.min(r, g, b), c = max - min;
  let h = 0;
  if (c > 0) {
    if (max === r) h = ((g - b) / c + 6) % 6;
    else if (max === g) h = (b - r) / c + 2;
    else h = (r - g) / c + 4;
  }
  return { h: h * 60, s: max ? c / max : 0, v: max / 255 };
}

/**
 * A mountain range with its streaks combed out: each opaque pixel takes the most common colour within seven pixels
 * along its row. The ranges' slope shading is drawn as one-pixel columns, which at 1:1 behind a moving camera read as
 * vertical banding; the mode keeps the broad shapes (lit faces, snow caps) as clean flat facets.
 */
export function destreak(img: HTMLImageElement, radius = 7): HTMLCanvasElement {
  const c = document.createElement("canvas");
  c.width = img.width;
  c.height = img.height;
  const ctx = c.getContext("2d")!;
  ctx.drawImage(img, 0, 0);
  const src = ctx.getImageData(0, 0, c.width, c.height);
  const out = ctx.createImageData(c.width, c.height);
  const s = src.data, o = out.data, w = c.width;
  const counts = new Map<number, number>();
  for (let y = 0; y < c.height; y++) {
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      if (s[i + 3]! === 0) continue;
      counts.clear();
      let best = 0, bestN = 0;
      for (let k = -radius; k <= radius; k++) {
        const j = (y * w + ((x + k + w) % w)) * 4;   // the ranges tile horizontally: wrap
        if (s[j + 3]! === 0) continue;
        const key = (s[j]! << 16) | (s[j + 1]! << 8) | s[j + 2]!;
        const n = (counts.get(key) ?? 0) + 1;
        counts.set(key, n);
        if (n > bestN) { bestN = n; best = key; }
      }
      o[i] = (best >> 16) & 255;
      o[i + 1] = (best >> 8) & 255;
      o[i + 2] = best & 255;
      o[i + 3] = s[i + 3]!;
    }
  }
  ctx.putImageData(out, 0, 0);
  return c;
}

/** The colour of one pixel of an image (for sampling a sky's horizon band). */
export function pixelColour(img: CanvasImageSource & { width: number; height: number }, x: number, y: number): THREE.Color {
  const c = document.createElement("canvas");
  c.width = img.width;
  c.height = img.height;
  const ctx = c.getContext("2d")!;
  ctx.drawImage(img, 0, 0);
  const [r, g, b] = ctx.getImageData(Math.min(x, img.width - 1), Math.min(y, img.height - 1), 1, 1).data;
  return new THREE.Color(r! / 255, g! / 255, b! / 255);
}

export interface ResolvedTexture { entry: TerrainTexture; url: string; variant: string }

/**
 * A map material ("grass/lush", "dungeon_floor/dark") to its texture: the terrain set first, then the dungeon set.
 * A variant the set doesn't have falls back to its first one ("flagstone/mossy" → flagstone grey), so a map never
 * draws a hole.
 */
export function resolveTexture(m: WorldManifests, family: string, variant: string): ResolvedTexture | null {
  const sets: [string, Record<string, TerrainTexture> | undefined][] = [
    ["terrain/", m.terrain.textures],
    ["dungeon/", m.dungeon?.textures],
  ];
  for (const [dir, textures] of sets) {
    const entry = textures?.[family];
    if (!entry) continue;
    const picked = entry.variants[variant] ? variant : Object.keys(entry.variants)[0];
    if (!picked) continue;
    return { entry, url: m.base + dir + entry.variants[picked], variant: picked };
  }
  return null;
}

/** A prop by id from the props set or the dungeon set, with its image URLs resolved. */
export function resolveProp(
  m: WorldManifests, id: string, variant?: string,
): { entry: PropEntry; url: string; variant: string; emissiveUrl: string | null } | null {
  const sets: [string, Record<string, PropEntry> | undefined][] = [["props/", m.props.props], ["dungeon/", m.dungeon?.props]];
  for (const [dir, props] of sets) {
    const entry = props?.[id];
    if (!entry) continue;
    const picked = variant && entry.variants[variant] ? variant : Object.keys(entry.variants)[0]!;
    const emissive = entry.light?.emissive;
    return { entry, url: m.base + dir + entry.variants[picked], variant: picked, emissiveUrl: emissive ? m.base + dir + emissive : null };
  }
  return null;
}
