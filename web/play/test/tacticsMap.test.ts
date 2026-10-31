import { describe, expect, it } from "vitest";
import { decodeMap, tileStyle } from "../src/games/tactics/map";

describe("tactics map decoding", () => {
  // A 2×1 map: grass at height 0, then impassable rock at height 2. Bytes: height, material, flags per tile.
  const file = { size: [2, 1] as [number, number], tiles: btoa(String.fromCharCode(0, 0, 0, 2, 1, 4)), materials: ["grass/lush", "rock/grey"] };

  it("reads height, material and flags per tile, row by row", () => {
    const map = decodeMap(file);
    expect(map).toEqual({ w: 2, h: 1, heights: [0, 2], tileMaterial: [0, 1], flags: [0, 4], materials: ["grass/lush", "rock/grey"] });
  });

  it("colours tiles by material family, and impassable ones dark", () => {
    const map = decodeMap(file);
    expect(tileStyle(map, 0).background).toBe("#2f5d2f");
    expect(tileStyle(map, 1)).toEqual({ background: "#111" });
  });
});
