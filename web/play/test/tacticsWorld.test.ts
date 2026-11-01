import { describe, expect, it } from "vitest";
import type { WorldManifests, TerrainTexture } from "../src/games/tactics/world/assets";
import { columnLook, WATER_DROP } from "../src/games/tactics/world/terrain";
import { AREAS } from "../src/games/tactics/world/areas";
import { AREA_SECONDS, captionAlpha } from "../src/games/tactics/world/flyover";

const tex = (kind: TerrainTexture["kind"], variants: string[], extra: Partial<TerrainTexture> = {}): TerrainTexture => ({
  kind, variants: Object.fromEntries(variants.map((v) => [v, `${kind}/${v}.png`])), frames: 1, fps: 0, size: [128, 128], ...extra,
});

const manifests = {
  base: "/pack/",
  terrain: {
    tile: 32, span: 4, heightStep: 16,
    textures: {
      grass: tex("top", ["lush", "green"]),
      water: tex("top", ["clear"], { frames: 16, fps: 8 }),
      lava: tex("top", ["molten"], { emissive: { slot: "glow", light: { every: 2, radius: 2.5, lift: 4, flicker: "pulse", power: 0.8 }, colour: { molten: "#ffa94b" } } }),
      rock: tex("top", ["grey"]),
      sand: tex("top", ["desert"]),
      dirt_side: tex("side", ["brown", "dark"]),
      lava_side: tex("side", ["molten"]),
      plank_side: tex("side", ["oak"]),
      grass_lip: tex("lip", ["lush"]),
    },
  },
  dungeon: {
    props: {},
    textures: {
      dungeon_floor: tex("top", ["grey", "dark", "warm"]),
      dungeon_wall: tex("side", ["grey", "dark", "warm"]),
      cave_floor: tex("top", ["brown", "cool"]),
      cave_wall: tex("side", ["brown", "cool"]),
      wet_stone: tex("top", ["cave", "glow"], { frames: 8, fps: 8 }),
      mine_gravel: tex("top", ["mine"]),
      mine_wall: tex("side", ["mine"]),
      mine_boards: tex("top", ["mine"]),
      void: tex("top", ["abyss"], { frames: 8, fps: 8 }),
      chasm_side: tex("side", ["brown", "cool"]),
    },
  },
} as unknown as WorldManifests;

const look = (material: string, retexture?: Record<string, string>) => columnLook(manifests, material, retexture);
const variantOf = (url: string | undefined) => url?.split("/").pop();

describe("the flyover's terrain pairing", () => {
  it("stands grass on earth with its overhang", () => {
    const l = look("grass/lush");
    expect(l.top?.url).toBe("/pack/terrain/top/lush.png");
    expect(l.side?.url).toBe("/pack/terrain/side/brown.png");
    expect(l.lip?.variant).toBe("lush");
    expect(l.pit).toBe(false);
  });

  it("caps a wall material with the matching floor and keeps the wall as its sides", () => {
    const l = look("dungeon_wall/dark");
    expect(l.top?.url).toBe("/pack/dungeon/top/dark.png");
    expect(l.side?.url).toBe("/pack/dungeon/side/dark.png");
  });

  it("maps the cave materials to cave walls, mine walls and a chasm", () => {
    expect(variantOf(look("cave_floor/cool").side?.url)).toBe("cool.png");
    expect(look("mine_gravel/brown").top?.variant).toBe("mine");
    expect(look("mine_gravel/brown").side?.url).toBe("/pack/dungeon/side/mine.png");
    const chasm = look("void/deep");
    expect(chasm.pit).toBe(true);
    expect(chasm.top?.variant).toBe("abyss");
    expect(chasm.side?.url).toBe("/pack/dungeon/side/cool.png");
  });

  it("finds glow: the manifest's emissive, or the known glowing variants", () => {
    expect(look("lava/molten").glow).toBe("#ffa94b");
    expect(look("lava/molten").side?.url).toBe("/pack/terrain/side/molten.png");
    expect(look("wet_stone/glow").glow).not.toBeNull();
    expect(look("wet_stone/cave").glow).toBeNull();
    expect(look("grass/lush").glow).toBeNull();
  });

  it("runs water down its own sides, under its bank", () => {
    const l = look("water/clear");
    expect(l.sideIsTop).toBe(true);
    expect(l.side).toBe(l.top);
    expect(l.drop).toBe(WATER_DROP);
  });

  it("applies an area's retexturing and falls back instead of leaving a hole", () => {
    expect(look("grass/lush", { "grass/lush": "sand/desert" }).top?.url).toBe("/pack/terrain/top/desert.png");
    expect(look("unknown/thing").top?.url).toBe("/pack/terrain/top/grey.png");
  });
});

describe("the tour", () => {
  it("visits a map per region in story order", () => {
    expect(AREAS.map((a) => a.map)).toEqual([
      "river_crossing", "forest_ruins", "warlord_bridge", "crystal_cave", "keep_gate", "hall_of_the_flame",
    ]);
    const chapters = AREAS.map((a) => a.chapter);
    expect([...chapters].sort((a, b) => a - b)).toEqual(chapters);
  });

  it("shows a caption once the fly-in settles and hides it before the next area", () => {
    expect(captionAlpha(0.5, true)).toBe(0);
    expect(captionAlpha(AREA_SECONDS / 2, true)).toBe(1);
    expect(captionAlpha(AREA_SECONDS - 0.5, true)).toBe(0);
    expect(captionAlpha(1000, false)).toBe(1);
  });
});
