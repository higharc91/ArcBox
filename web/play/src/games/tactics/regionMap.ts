import { useEffect, useState } from "preact/hooks";
import { packJson } from "./pack";

/**
 * A region of the overworld from the tactics content pack (`rules/world/<id>.json`), decoded for the leader's map: per tile, row by row,
 * its height, material index and flags, from the region's 32-tile chunks (3 bytes a tile, as a battle map's, in each chunk's base64
 * `tiles`). A tile in no chunk is void. The server's `WorldRegion` reads the same file.
 */
export interface RegionData { w: number; h: number; heights: Uint8Array; materials: string[]; tileMaterial: Uint8Array; flags: Uint8Array; present: Uint8Array }

export interface RegionFile { size: [number, number]; materials: string[]; chunks: { x: number; y: number; tiles: string }[] }

/** The tiles along one side of a chunk (`WorldRegion.ChunkSize`). */
export const CHUNK = 32;

/** Tile flags, as `TileFlags` has them. */
export const WATER = 1;
export const IMPASSABLE = 4;
export const DEEP = 16;

export function decodeRegion(file: RegionFile): RegionData {
  const [w, h] = file.size;
  const n = w * h;
  const region: RegionData = {
    w, h, materials: file.materials,
    heights: new Uint8Array(n), tileMaterial: new Uint8Array(n), flags: new Uint8Array(n), present: new Uint8Array(n),
  };
  for (const chunk of file.chunks) {
    const bytes = Uint8Array.from(atob(chunk.tiles), (c) => c.charCodeAt(0));
    for (let i = 0; i < CHUNK * CHUNK; i++) {
      const [x, y] = [chunk.x * CHUNK + (i % CHUNK), chunk.y * CHUNK + Math.floor(i / CHUNK)];
      if (x >= w || y >= h) continue;
      const at = y * w + x;
      region.heights[at] = bytes[i * 3] ?? 0;
      region.tileMaterial[at] = bytes[i * 3 + 1] ?? 0;
      region.flags[at] = bytes[i * 3 + 2] ?? 0;
      region.present[at] = 1;
    }
  }
  return region;
}

const cache = new Map<string, Promise<RegionData | null>>();

/** Fetches a region from the content pack; null if it can't. Cached. */
export function loadRegion(id: string): Promise<RegionData | null> {
  let pending = cache.get(id);
  if (!pending) {
    pending = packJson<RegionFile>(`rules/world/${id}.json`).then((file) => {
      if (!file) cache.delete(id);   // try again next time
      return file ? decodeRegion(file) : null;
    });
    cache.set(id, pending);
  }
  return pending;
}

/** The region, once loaded; null until then (and when there's no id or it can't load). */
export function useRegion(id: string | undefined | null): RegionData | null {
  const [region, setRegion] = useState<RegionData | null>(null);
  useEffect(() => {
    setRegion(null);
    if (!id) return;
    let live = true;
    void loadRegion(id).then((r) => { if (live) setRegion(r); });
    return () => { live = false; };
  }, [id]);
  return region;
}

/** Whether the map draws a tile as ground the party can stand on: in a chunk, and neither impassable nor deep. Buildings and props aren't known here. */
export function open(region: RegionData, x: number, y: number): boolean {
  if (x < 0 || y < 0 || x >= region.w || y >= region.h) return false;
  const at = y * region.w + x;
  return region.present[at] === 1 && ((region.flags[at] ?? 0) & (IMPASSABLE | DEEP)) === 0;
}

/** How far, in tiles each way, a tap off the ground looks for ground (the server's `TravelSnap`). */
export const SNAP = 4;

/**
 * The tile a tap on (`x`, `y`) asks for: that tile if it's ground, else the nearest ground within `SNAP` (nearest by distance, then by x,
 * then y, as the server picks), else the tile itself, for the server to say there's no way there. Without the region, the tile as tapped.
 */
export function snap(region: RegionData | null, x: number, y: number): [number, number] {
  if (!region || open(region, x, y)) return [x, y];
  let best: [number, number] | null = null;
  let bestD = Infinity;
  for (let tx = x - SNAP; tx <= x + SNAP; tx++)
    for (let ty = y - SNAP; ty <= y + SNAP; ty++) {
      const d = (tx - x) ** 2 + (ty - y) ** 2;
      if (d < bestD && open(region, tx, ty)) [best, bestD] = [[tx, ty], d];
    }
  return best ?? [x, y];
}

/** Families drawn as a road or a floor laid by hand. */
const ROAD = new Set(["path", "cobble", "flagstone", "road_ramp", "planks", "warden_deck", "warden_edge", "warden_ramp", "pavers", "mine_boards", "floorboards"]);

/** Ground colours by material family; anything else is plain ground. */
const FAMILY: Record<string, string> = {
  grass: "#4f7a3a", grass_ford: "#6b7a3c", forest_needles: "#2f5a35", forest_floor: "#2f5a35", dirt: "#7a5d3f", dirt_ford: "#7a6a4a",
  ash: "#6d6a66", ash_ford: "#5f6152", rock: "#6a6e73", rock_ford: "#6a6e73", stone: "#6a6e73", snow: "#c9d1d8", sand: "#b39c63",
  cave_floor: "#5a5a63", mine_gravel: "#6b5a48", mud: "#5a4632", bog: "#4a5238",
};

/** The colour of one tile on the map, or null for void: water blue (deep water darker), blocked ground dark, roads tan, the rest by family, lighter with height. */
export function tileColor(region: RegionData, at: number): string | null {
  if (region.present[at] !== 1) return null;
  const flags = region.flags[at] ?? 0;
  const family = (region.materials[region.tileMaterial[at] ?? 0] ?? "").split("/")[0]!;
  if (flags & WATER || family.includes("water")) return flags & (IMPASSABLE | DEEP) ? "#1d4a78" : "#3a7fbf";
  if (flags & (IMPASSABLE | DEEP)) return "#16181d";
  const base = ROAD.has(family) ? "#c2a46a" : FAMILY[family] ?? "#5e6a4a";
  return shade(base, (region.heights[at] ?? 0) * 0.05);
}

/** `hex` lightened by `by` (0..1). */
function shade(hex: string, by: number): string {
  if (by <= 0) return hex;
  const n = parseInt(hex.slice(1), 16);
  const ch = (s: number) => Math.min(255, Math.round(((n >> s) & 255) + (255 - ((n >> s) & 255)) * by));
  return `#${[16, 8, 0].map((s) => ch(s).toString(16).padStart(2, "0")).join("")}`;
}

/**
 * The map is drawn as the TV sees the region, so that up on the map is up on the TV and on the pad: tile +x runs down to the right and +y
 * down to the left, each tile a diamond `2 * HALF` wide and `HALF` high. `screen` and `tileAt` convert between a tile and a point on it.
 */
export const HALF = 4;

/** The drawing's size, for a region `w` by `h`. */
export const drawing = (w: number, h: number) => ({ width: (w + h) * HALF, height: ((w + h) * HALF) / 2 });

/** The centre of tile (`x`, `y`) on the drawing. */
export const screen = (h: number, x: number, y: number) => ({ sx: (x - y + h) * HALF, sy: ((x + y + 1) * HALF) / 2 });

/** The tile under a point on the drawing. */
export function tileAt(h: number, sx: number, sy: number): [number, number] {
  const u = sx / HALF - h;          // x - y
  const v = (2 * sy) / HALF - 1;    // x + y
  return [Math.round((u + v) / 2) + 0, Math.round((v - u) / 2) + 0];     // + 0: never -0
}

/** Paints the region into `ctx`, at the drawing's own size. */
export function paint(ctx: CanvasRenderingContext2D, region: RegionData): void {
  const { width, height } = drawing(region.w, region.h);
  ctx.clearRect(0, 0, width, height);
  for (let y = 0; y < region.h; y++)
    for (let x = 0; x < region.w; x++) {
      const color = tileColor(region, y * region.w + x);
      if (!color) continue;
      const { sx, sy } = screen(region.h, x, y);
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.moveTo(sx, sy - HALF / 2 - 0.3);
      ctx.lineTo(sx + HALF + 0.3, sy);
      ctx.lineTo(sx, sy + HALF / 2 + 0.3);
      ctx.lineTo(sx - HALF - 0.3, sy);
      ctx.closePath();
      ctx.fill();
    }
}
