# Tactics art style guide

The rules every asset set follows, so characters, terrain, props, UI, icons and effects read as one game.

## Look

- **HD-2D pixel art.** Crisp pixels and no anti-aliasing. Never use filtered scaling: only nearest-neighbour at whole
  multiples.
- **Light comes from the top-left.** The top-left edges of a form get the light shades; the bottom-right edges get
  the dark ones.
- **Hue-shifted ramps.** Every colour comes from a 6-shade ramp (`arcpix/palette.py`, `ramp()`):

  | Shade | Role |
  |---|---|
  | 0 | Deep shadow and outline base |
  | 1–2 | Shadow |
  | 3 | Base (the flat colour) |
  | 4 | Light |
  | 5 | Highlight |

  Shadows drift cool and highlights drift warm. Make a new ramp with `ramp("#hex", shift=..., spread=...)`.
  Don't hand-pick unrelated colours.
- **No pure black or white.** The darkest outline is `OUTLINE = #140f1a`, and highlights stay below pure white.
- **Selective outlines.** Sprites (characters, props, icons) get `Canvas.outline()`. Each outline pixel is the
  neighbour's shade 0, pushed towards `#140f1a`. The result is a dark rim that keeps the hue, not a black line.
  Terrain textures have no outline.
- **Clusters, not noise.** Shade in clumps of 2+ pixels. `tex.despeckle` removes lone pixels. Dither only on
  soft borders (`tex.quantize(..., dither=)`).
- **Readable at 1×, shown at 3–4×.** The TV renders sprites at about 3–4× and phones show icons at 2–3×. Check
  every preview at zoom.

## UI palette

These colours come from the mockups' `docs/superpowers/mockups/tactics/tactics.css`:

| Name | Hex | Use |
|---|---|---|
| ink | `#f4ecd6` | Text, light details |
| muted | `#bdb093` | Secondary text |
| gold | `#e6bd5c` | Panel edges, corner studs, active states |
| gold-2 | `#a07c33` | Gold in shadow |
| panel | `#1a1e3a` → `#0a0b1a` | Navy glass panel, top to bottom |
| hp | `#63d36f` | Ally HP |
| hp-bad / enemy | `#e85148` / `#e2534c` | Enemy HP and enemy markers |
| magic | `#7fc8ff` | Mana and arcane |
| p1–p6 | `#ff8a3d #3ec7ff #c08cff #5fe08a #ffd447 #ff7fb6` | The six player colours |

## Sizes

| Asset | Size |
|---|---|
| Character frame | 32×40, anchor (16, 37) |
| Terrain tile | 32 px, 128 px textures (4×4 tiles), height step 16 px |
| Props | Multiples of 16 px, anchor at the ground contact point (bottom centre) |
| Icons | 16×16 (status, inline), 24×24 (abilities, items) |
| UI | 9-slice sources with the slice insets in the set's manifest |
| FX | 32×32 or 48×48 frames, 6–10 frames per effect |

## Code conventions

- Draw with `arcpix.canvas.Canvas`: indexed `(slot, shade)` pixels, a `SlotTable` per family, and
  `render(palette)` / `ramp_map()`.
- A set is a module `parts/<name>.py` with a `build()` function. `build.py` discovers it. It writes:
  - pack files under `content/tactics/base/<name>/` (`shutil.rmtree` that folder first, so nothing stale is left);
  - a `manifest.json` describing every file (frame sizes, anchors, 9-slice insets, animation frames and fps);
  - review sheets as `previews/NN-<name>*.png` (and `.gif` for animations), using `arcpix.preview`.
- Use `arcpix.out.save` and `write_json`. Output must be deterministic: seed every random source.
- Match the surrounding code: module and function docstrings say *why*, comments are sparse, there are no
  dependencies beyond Pillow and numpy, and lines are at most 120 characters.
