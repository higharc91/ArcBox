# Tactics assets

Procedural pixel art for the tactics RPG. The art is drawn in Python, not by hand: every sprite is code over a
small set of parts. Re-running the build gives byte-identical output, so the content pack's hash changes only
when the art does.

```bash
python3 tools/tactics-assets/build.py               # every set
python3 tools/tactics-assets/build.py characters    # one set
```

| Output | What |
|---|---|
| `content/tactics/base/…` | What the game loads (served by the content-pack endpoints, cached by the TV's `ContentCache`) |
| `tools/tactics-assets/previews/*.png` | Contact sheets for review |

Needs Python 3.11+ with Pillow and numpy. `dev_preview.py` and `dev_zoom.py` are quick-look helpers used while
drawing.

## How the art is built

- **Indexed drawing.** A canvas pixel is a `(slot, shade)` pair, not a colour. A *slot* is a material: `skin`,
  `hair`, `cloth1`, `metal`, and so on. A *shade* indexes a 6-step, hue-shifted ramp (`arcpix/palette.py`).
  Shadows drift cool, highlights drift warm, so one palette swap recolours the art without flattening it.
- **Ramp maps.** Layers are exported as ramp maps, not colours:

  | Channel | Value |
  |---|---|
  | R | slot × 16 |
  | G | shade × 32 |
  | B | 255 on outline pixels |
  | A | coverage |

  The game recolours a ramp map with a palette lookup texture (LUT).
- **One rig, many parts.** Characters are paper dolls on a 32×40 frame with the ground anchor at (16, 37). Each
  layer variant draws itself from the same keypoints (`parts/rig.py`), so any helmet fits any body, in every
  animation frame and on all three builds:

  | Build | Used for |
  |---|---|
  | `standard` | humans, elves |
  | `short` | dwarves, halflings, goblins |
  | `large` | orcs, knights |

## Characters

`content/tactics/base/characters/`:

| File | What |
|---|---|
| `manifest.json` | Frame size, anchor, animations, slots, layers and variants, draw order. **Start here.** |
| `{build}/{layer}/{variant}.png` | One layer as a ramp-map sheet: 20 columns × 2 rows (front, then back facing) |
| `palettes.json` / `palettes.png` | Every named ramp, every preset's palette, and the LUT (one row per palette) |
| `presets/{id}.json` + `.png` | Ready-made characters: the recipe, plus a baked full-colour sheet |

### Facings

Two facings are drawn: `front` (row 0) and `back` (row 1). Flip them horizontally for the other two isometric
directions. In the front facing the weapon hand is on screen-right.

### Animations

Frame indices are per row. All animations play at the fps given in the manifest.

| Anim | Frames | Notes |
|---|---|---|
| idle | 0–1 | loops |
| walk | 2–5 | loops |
| attack | 6–8 | overhead swing: swords, axes, maces |
| thrust | 9–11 | spears, daggers |
| shoot | 12–14 | bows |
| cast | 15–17 | staffs, wands, tomes, unarmed |
| hurt | 18 | |
| down | 19 | kneeling: the "downed" state |

### Composing a character (do this once per character or gear change, then cache it)

1. For each layer in `manifest.order[facing]`, take the character's variant sheet for its build and alpha-blend
   it over the result. Every pixel is either fully drawn or empty, so "top layer wins" is enough.
2. **Outline the composite once.** For each empty pixel, look at its neighbours in the order right, left, below,
   above. Take the first drawn one and copy its slot into this pixel, with shade 0 and B = 255.

   The layer sheets have no outlines on purpose. Per-layer outlines would paint over the layer beneath them
   (eyes under a fringe, hands on a torso).
3. Keep the composed ramp map as the character's texture. Recolour it at draw time (below), or bake it to
   colours once.

The baked `presets/*.png` sheets are exactly this process, already rendered. Use them when recolouring isn't
needed: enemies, NPCs, previews.

### Recolouring in Godot

A `canvas_item` shader on the character sprite reads the ramp map and looks the colour up in the LUT row for
that character's palette:

```glsl
shader_type canvas_item;
uniform sampler2D lut : filter_nearest;   // palettes.png
uniform float palette_row;                // the character's row in palettes.json "lutRows"
uniform float lut_rows;
const vec3 OUTLINE = vec3(0.078, 0.059, 0.102);   // #140f1a

void fragment() {
    vec4 m = texture(TEXTURE, UV);
    if (m.a < 0.5) discard;
    float slot = floor(m.r * 255.0 / 16.0 + 0.5);
    float shade = floor(m.g * 255.0 / 32.0 + 0.5);
    vec2 uv = vec2((slot * 8.0 + shade + 0.5) / 128.0, (palette_row + 0.5) / lut_rows);
    vec3 c = texture(lut, uv).rgb;
    if (m.b > 0.5) c = mix(c, OUTLINE, 0.6);    // outline pixels: darkened neighbour colour
    COLOR = vec4(c, 1.0);
}
```

Custom palettes made in the character creator don't need a LUT row ahead of time. Write the character's row
into an `Image` at runtime: 16 slots × 8 shades, filled from `palettes.json` ramps.
