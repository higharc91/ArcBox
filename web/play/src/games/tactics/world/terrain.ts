import { resolveTexture, type ResolvedTexture, type WorldManifests } from "./assets";

/**
 * How one map material looks as a tile column: the texture on top, the cliff face under it, the overhang along its
 * edge, and whether it glows, flows or falls away. Battle maps name materials ("grass/lush", "cave_floor/cool",
 * "dungeon_wall/dark"); the terrain and dungeon sets have tops, sides and lips but don't say which go together, so
 * the pairing lives here.
 */
export interface ColumnLook {
  material: string;
  top: ResolvedTexture | null;
  side: ResolvedTexture | null;
  lip: ResolvedTexture | null;
  /** The side runs the top's own texture (a waterfall), at the top's scale. */
  sideIsTop: boolean;
  /** Below the map height: water sits under its bank. */
  drop: number;
  /** A chasm: its floor is the bottom of the island. */
  pit: boolean;
  /** Glow colour for pixels that light themselves (the manifest's `emissive`, or one given here). */
  glow: string | null;
}

/** Cliff face under each top family: [side family, variant]; "=" keeps the top's own variant. */
const SIDE_FOR: Record<string, [string, string]> = {
  grass: ["dirt_side", "brown"], dirt: ["dirt_side", "dark"], path: ["dirt_side", "brown"], mud: ["dirt_side", "dark"],
  forest_floor: ["dirt_side", "dark"], forest_needles: ["dirt_side", "dark"], bog: ["dirt_side", "dark"],
  rock: ["cliff_side", "grey"], snow: ["cliff_side", "grey"], ice: ["ice_side", "clear"],
  flagstone: ["ashlar_side", "grey"], cobble: ["ashlar_side", "grey"], pavers: ["ashlar_side", "grey"],
  sand: ["sandstone_side", "desert"], dunes: ["sandstone_side", "desert"], sandstone: ["sandstone_side", "ochre"],
  ash: ["basalt_side", "dark"], basalt: ["basalt_side", "dark"], lava: ["lava_side", "molten"],
  planks: ["plank_side", "oak"], mine_boards: ["plank_side", "oak"], floorboards: ["plank_side", "oak"],
  parquet: ["plank_side", "oak"],
  dungeon_floor: ["dungeon_wall", "="], dungeon_floor_mossy: ["dungeon_wall", "="],
  dungeon_floor_cracked: ["dungeon_wall", "="], dungeon_floor_bloody: ["dungeon_wall", "="],
  crypt_tiles: ["dungeon_wall", "dark"],
  cave_floor: ["cave_wall", "="], wet_stone: ["cave_wall", "cool"], mine_gravel: ["mine_wall", "mine"],
  void: ["chasm_side", "cool"],
};

/** Overhangs: grass and snow over their cliffs, moss on forest floor, stalactites under cave floor. */
const LIP_FOR: Record<string, [string, string]> = {
  grass: ["grass_lip", "="], snow: ["snow_lip", "="], forest_floor: ["moss_lip", "="], cave_floor: ["stalactite_lip", "="],
};

/** Ground that glows in the art but carries no `emissive` entry: the colour of its glowing pixels. */
const GLOW_FOR: Record<string, string> = {
  "wet_stone/glow": "#5cc3c8",
  "crypt_tiles/fel": "#91f59a",
  "crypt_tiles/arcane": "#85dbff",
  void: "#8f7ad8",
};

/** Water stands this far under its bank. */
export const WATER_DROP = 0.14;

export function columnLook(m: WorldManifests, material: string, retexture: Record<string, string> = {}): ColumnLook {
  const mat = retexture[material] ?? material;
  const [family, variant = ""] = mat.split("/") as [string, string?];
  let top = resolveTexture(m, family, variant);
  let side: ResolvedTexture | null = null;

  // A wall material (a side texture used as a whole column) is capped with the matching floor.
  if (top?.entry.kind === "side") {
    side = top;
    top = resolveTexture(m, family.replace(/_(wall|side)$/, "_floor"), variant) ?? resolveTexture(m, "dungeon_floor", variant);
  }
  top ??= resolveTexture(m, "rock", "grey");

  const sideIsTop = family === "water";
  if (!side) {
    if (sideIsTop) side = top;
    else {
      const [sf, sv] = SIDE_FOR[family] ?? ["dirt_side", "dark"];
      side = resolveTexture(m, sf, sv === "=" ? variant : sv) ?? resolveTexture(m, "dirt_side", "dark");
    }
  }

  const lipFor = LIP_FOR[family];
  const lip = lipFor ? resolveTexture(m, lipFor[0], lipFor[1] === "=" ? variant : lipFor[1]) : null;

  const glow = (top && top.entry.emissive?.colour[top.variant]) ?? GLOW_FOR[`${family}/${variant}`] ?? GLOW_FOR[family] ?? null;
  return {
    material: mat,
    top,
    side,
    // Lips only fit the families they were drawn for; a fallback variant of another set would look pasted on.
    lip: lip && lip.entry.kind === "lip" ? lip : null,
    sideIsTop,
    drop: family === "water" ? WATER_DROP : 0,
    pit: family === "void",
    glow,
  };
}
