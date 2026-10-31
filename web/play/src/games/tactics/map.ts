import { useEffect, useState } from "preact/hooks";
import { packJson } from "./pack";

/**
 * A battle map from the tactics content pack (`rules/maps/<id>.json`), decoded for drawing: per tile, row by row, its
 * height, material index and flags (3 bytes a tile in the file's base64 `tiles`). Shared by the phone's turn map and the
 * dev page's board.
 */
export interface MapData { w: number; h: number; heights: number[]; flags: number[]; materials: string[]; tileMaterial: number[] }

export interface MapFile { size: [number, number]; tiles: string; materials: string[] }

/** Decodes a map file's tiles. */
export function decodeMap(file: MapFile): MapData {
  const bytes = Uint8Array.from(atob(file.tiles), (c) => c.charCodeAt(0));
  const n = file.size[0] * file.size[1];
  const pick = (k: number) => Array.from({ length: n }, (_, i) => bytes[i * 3 + k] ?? 0);
  return { w: file.size[0], h: file.size[1], heights: pick(0), tileMaterial: pick(1), flags: pick(2), materials: file.materials };
}

const cache = new Map<string, Promise<MapData | null>>();

/** Fetches a map from the content pack (pack.ts); null if it can't. Cached. */
export function loadMap(id: string): Promise<MapData | null> {
  let pending = cache.get(id);
  if (!pending) {
    pending = packJson<MapFile>(`rules/maps/${id}.json`).then((file) => {
      if (!file) cache.delete(id);   // try again next time
      return file ? decodeMap(file) : null;
    });
    cache.set(id, pending);
  }
  return pending;
}

/** The map, once loaded; null until then (and when there's no id or it can't load). */
export function useMap(id: string | undefined | null): MapData | null {
  const [map, setMap] = useState<MapData | null>(null);
  useEffect(() => {
    if (!id) return;
    let live = true;
    void loadMap(id).then((m) => { if (live) setMap(m); });
    return () => { live = false; };
  }, [id]);
  return map;
}

const MATERIALS: Record<string, string> = {
  grass: "#2f5d2f", dirt: "#5a4632", water: "#1f4f7a", rock: "#555b61", stone: "#4b4f55", sand: "#7a6a45", wood: "#5b3d22",
  snow: "#9aa4ad",
};

/** A tile's colour: its material's family ("water/clear" → water), darker when impassable, lighter with height. */
export function tileStyle(map: MapData, i: number): { background: string; filter?: string } {
  if ((map.flags[i] ?? 0) & 4) return { background: "#111" };   // impassable
  const material = map.materials[map.tileMaterial[i] ?? 0] ?? "";
  const height = map.heights[i] ?? 0;
  return { background: MATERIALS[material.split("/")[0]!] ?? "#3a3a3a", filter: height ? `brightness(${1 + height * 0.12})` : undefined };
}
