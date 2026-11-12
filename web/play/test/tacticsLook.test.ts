import { describe, expect, it } from "vitest";
import { compose, paint, recipe, layerPath, type CharManifest, type Creator, type Palettes } from "../src/games/tactics/look";
import { dataUrlBytes, readPng } from "./png";

// The pack's own files: the creator's options and the class presets the asset tools baked.
const files = import.meta.glob<{ default: unknown }>("../../../content/tactics/base/characters/{creator.json,manifest.json,palettes.json,presets/class_*.json}", { eager: true });
const file = (name: string) => files[`../../../content/tactics/base/characters/${name}`]!.default;
const creator = file("creator.json") as Creator;
const preset = (cls: string) => file(`presets/class_${cls}.json`) as {
  build: string; layers: Record<string, string>; palette: Record<string, string>; race: string;
};

/** A frame of ramp-map pixels from a picture: "." empty, a digit the slot (shade 2). */
const frame = (rows: string[]) => {
  const height = rows.length, width = rows[0]!.length;
  const data = new Uint8ClampedArray(width * height * 4);
  rows.forEach((row, y) => [...row].forEach((ch, x) => {
    if (ch === ".") return;
    data.set([Number(ch) * 16, 2 * 32, 0, 255], (y * width + x) * 4);
  }));
  return { width, height, data };
};

describe("the look", () => {
  /** The class presets were made from their own race with its default look: the recipe built from the pack must be theirs. */
  it("a race and class make the recipe the asset tools made for the class's preset", () => {
    for (const cls of Object.keys(creator.classes)) {
      const p = preset(cls);
      const r = recipe(creator.races[p.race]!, creator.classes[cls]!, p.palette);
      expect(r.build.replace("_f", "")).toBe(p.build.replace("_f", ""));
      for (const layer of ["body", "torso", "legs", "weapon"]) expect(r.layers[layer], `${cls} ${layer}`).toBe(p.layers[layer]);
    }
  });

  it("the race's look and the class's kit go together", () => {
    const r = recipe(creator.races.dwarf!, creator.classes.wizard!, preset("wizard").palette);
    expect(r.build).toBe("short");
    expect(r.layers.beard).toBe("full");
    expect(r.layers.torso).toBe(creator.classes.wizard!.gear.torso);
    expect(r.palette.skin).toBe("skin/tan");
    expect(r.palette.hair).toBe("hair/copper");
    expect(r.palette.cloth1).toBe("cloth/navy");
    // A race with no hair has no hair layer.
    expect(recipe(creator.races.goliath!, creator.classes.fighter!, preset("fighter").palette).layers.hair).toBeUndefined();
  });

  it("layers stack top-last-wins and the silhouette is outlined once", () => {
    const c = compose(["body", "hat"], { body: frame(["...", ".1.", "..."]), hat: frame(["...", ".2.", "..."]) }, 3, 3);
    expect(c.slot[4]).toBe(2);                           // the hat's pixel over the body's
    expect([...c.outline]).toEqual([0, 1, 0, 1, 0, 1, 0, 1, 0]);   // its four neighbours, not the corners
    expect(c.slot[1]).toBe(2);
    expect(c.shade[1]).toBe(0);
  });

  it("paints each slot from its ramp, outline pixels darkened towards the outline colour", () => {
    const c = compose(["body"], { body: frame(["..", "1."]) }, 2, 2);
    const px = paint(c, { skin: 1 }, { skin: "skin/x" }, { skin: { x: ["#000000", "#111111", "#808080", "#ffffff"] } }, { colour: "#000000", mix: 0.5 });
    expect([...px.slice(8, 12)]).toEqual([128, 128, 128, 255]);   // shade 2
    expect([...px.slice(0, 4)]).toEqual([0, 0, 0, 255]);           // above it: outline, shade 0 mixed
    expect([...px.slice(12, 16)]).toEqual([0, 0, 0, 255]);
    expect(px[7]).toBe(0);                                        // the diagonal stays empty
  });

  /** The browser's composition, pixel for pixel, against the wizard preset the asset tools baked (front idle frame). */
  it("composes the wizard preset exactly as the asset tools baked it", async () => {
    const base = "../../../content/tactics/base/characters/";
    const manifest = file("manifest.json") as CharManifest;
    const palettes = file("palettes.json") as Palettes;
    const p = preset("wizard");
    const pngs = import.meta.glob<string>("../../../content/tactics/base/characters/{presets/class_wizard,short/*/*}.png", { eager: true, query: "?inline", import: "default" });
    const { width: w, height: h } = manifest.frame;
    const frames: Record<string, { width: number; height: number; data: Uint8ClampedArray }> = {};
    for (const [layer, variant] of Object.entries(p.layers))
      frames[layer] = { width: w, height: h, data: await readPng(dataUrlBytes(pngs[`${base}${p.build}/${layer}/${variant}.png`]!), w, h) };
    const mine = paint(compose(manifest.order.front, frames, w, h), manifest.slotIds, p.palette, palettes.ramps, manifest.outline);
    const baked = await readPng(dataUrlBytes(pngs[`${base}presets/class_wizard.png`]!), w, h);
    let differ = 0;
    for (let i = 0; i < baked.length; i += 4)
      if (baked[i + 3] !== mine[i + 3] || (baked[i + 3] && [0, 1, 2].some((k) => Math.abs(baked[i + k]! - mine[i + k]!) > 1))) differ++;
    expect(differ).toBe(0);
    expect(baked.filter((_, i) => i % 4 === 3 && baked[i]! > 0).length).toBeGreaterThan(200);    // a figure, not an empty frame
  });
});


describe("character layer storage aliases", () => {
  it("resolves every shared layer in one hop and keeps distinct body sheets", () => {
    const manifest = file("manifest.json") as CharManifest;
    for (const [from, to] of Object.entries(manifest.layerAliases ?? {})) {
      const [build, layer, filename] = from.split("/");
      expect(layerPath(manifest, build!, layer!, filename!.replace(/\.png$/, ""))).toBe(`characters/${to}`);
      expect(manifest.layerAliases?.[to]).toBeUndefined();
    }
    expect(layerPath(manifest, "short_f", "body", "human")).toBe("characters/short_f/body/human.png");
    expect(layerPath(manifest, "short_f", "hair", "bun")).toBe("characters/short/hair/bun.png");
  });
});
