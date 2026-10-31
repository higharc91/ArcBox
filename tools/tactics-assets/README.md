# Wardenflame assets (working name: tactics)

Procedural pixel art for Wardenflame, the tactics RPG. The art is drawn in Python, not by hand: every sprite is code
over a small set of parts. Re-running the build gives byte-identical output, so the content pack's hash changes only
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

### Name, race and class

A player's character is **a name, a race, a class and a look** (`parts/creator.py`):

| Axis | Decides |
|---|---|
| Race (11) | Build, body (ears, tusks, snout), tail, and which skins, hair, beards and horns are on offer |
| Class (12) | The starting kit: torso, legs, feet, hands, weapon, off-hand, cape, headgear, plus starting colours |
| Look | The player's picks, limited to what the race offers: body type, skin, eyes, hair style and colour, facial hair, horns, face markings and paint, plus colour changes to the kit |

The races are Human, Elf, Half-Elf, Dwarf, Halfling, Gnome, Half-Orc, Tiefling, Aasimar, Goliath and
Dragonborn. The classes are the twelve SRD 5.2 classes: Fighter, Rogue, Wizard, Cleric, Ranger, Paladin, Bard,
Druid, Warlock, Monk, Barbarian and Sorcerer. The barbarian goes unarmoured (a fur harness and kilt, a pelt, war
paint and a greataxe); the sorcerer carries no focus at all, just raw magic cupped in a bare hand and a silk robe.
A kit can carry face marks and a paint colour (the barbarian's war paint); the player's own picks win over them.

The options are:

| Option | Choices |
|---|---|
| Body type | Masculine or feminine. The feminine build of each size has a narrower chest, a waist, wider hips, a softer chin, lashes and fuller lips, and all gear follows it |
| Skin | Race-specific: 4–10 ramps |
| Eyes | 7 colours |
| Hair | 12 styles plus bald, in 12 colours |
| Facial hair | Up to 6 styles, depending on race |
| Horns | Tieflings and dragonborn: 4 shapes in 5 colours |
| Markings | Freckles, scar, war paint, tattoo or stripes. Painted markings take 7 paint colours |
| Clothes | The kit's colours, recoloured |

See `previews/08-customisation.png`.

`make_character(name, race, class, look)` merges these into a recipe, and rejects picks the race doesn't offer
(for example, hair on a dragonborn). Any race can take any class, so there are 132 combinations, and
`previews/06-race-class.png` renders every one. `characters/creator.json` exports the races and kits, so the
phone creator offers exactly these options. The TV draws the name on nameplates and the turn order; the sprite
doesn't carry it. The creator mockup is `docs/superpowers/mockups/tactics/07-phone-creator.png`.

### Equipment

`previews/19-equipment.png` shows the equipment layers:

- **Weapons (20):** sword, greatsword, axe, greataxe, mace, warhammer, maul, flail, scimitar, dagger, rapier,
  spear, halberd, staff, quarterstaff, wand, tome, lute, bow, crossbow. Each plays the attack animation that suits
  it (swing, thrust, shoot or cast). Subclass foci (crystal, storm and rune staffs, a sorcerer's spellfire or wild
  motes) cast like the staff.
- **Torso armour (10).**
- **Headgear (9).**

Armour metal (`metal`) and weapon metal (`blade`) are separate slots. A loot tier is a ramp: iron, steel, silver,
gold, bronze, mithril, adamantine, obsidian, elven or dark. Each item upgrades on its own: a mithril sword doesn't
turn the armour mithril.

Store a character as its creator inputs: `{name, race, class, look}`. Gear then changes as loot is equipped, by
swapping that layer's variant.

`content/tactics/base/characters/`:

| File | What |
|---|---|
| `manifest.json` | Frame size, anchor, animations, slots, layers and variants, draw order. **Start here.** |
| `{build}/{layer}/{variant}.png` | One layer as a ramp-map sheet: 20 columns × 2 rows (front, then back facing) |
| `palettes.json` / `palettes.png` | Every named ramp, every preset's palette, and the LUT (one row per palette) |
| `presets/{id}.json` + `.png` | Ready-made characters: name, race, class, recipe, plus a baked full-colour sheet |
| `creator.json` | The creator's options: each race's build, body, tail and offered skins, hair, beards and horns, plus each class kit |

### Facings

Two facings are drawn: `front` (row 0) and `back` (row 1). Flip them horizontally for the other two isometric
directions. In the front facing the weapon hand is on screen-right.

### Animations

There are 38 animations and 189 frames per facing. The manifest gives each animation's frame indices, fps and
whether it loops (indices are per row; the back row is the same indices plus 189). Read indices from the manifest:
never hard-code them. `previews/04-animations.png` shows every one.

| Anim | Frames | Use |
|---|---|---|
| idle | 6, loop | Exploration: breathes, blinks; hands, hair and cloak lag the breath |
| ready | 6, loop | Battle stance: the weapon bobs a beat behind the breath |
| walk | 8, loop | Contact, down, passing, up for each foot, with arm swing |
| run | 6, loop | |
| attack | 7 | Overhead swing (swords, axes, maces, hammers, flails, scimitars): anticipation, wind-up, smear (index 2), held impact, recover |
| thrust | 6 | Spears, daggers, rapiers, halberds. No smear |
| shoot | 6 | Bows and crossbows: raise, draw, hold, release |
| cast | 7 | Generic spell: gather, raise, release |
| point | 4 | Quick single-target bolt (cantrips) |
| pray | 6 | Divine casting (clerics, paladins) |
| channel | 4, loop | Holding concentration |
| summon | 7 | Arms raised: big areas and summons |
| buff | 6 | Fist to chest or weapon raised: self and ally boosts |
| slam | 7 | Heavy leap attack for two-handers |
| spin | 5 | Whirlwind cleave |
| flurry | 7 | Unarmed or dual-wield combo |
| bash | 5 | Shield bash |
| block, dodge | 3, 4 | Dodge slides the body and ends back on the anchor |
| knockback | 5 | Pushed: slides, stumbles, ends back on the anchor |
| stagger | 5 | |
| jump, hop, climb | 5, 4, 5 | Ledges and height steps. Bodies rise at most 4 px; the game supplies the arc |
| item | 5 | Drink a potion, read a scroll |
| hurt | 3 | Frame 0 is the flash-ready recoil |
| down | 4 | Hit, buckle, knee, slump. **Hold the last frame** for the downed state |
| rise | 4 | Starts from the downed pose |
| death | 6 | Topples and ends lying flat, ready to fade |
| victory, cheer | 4, 4, loop | |
| kneel, crouch, sneak | 3, 2 loop, 6 loop | |
| sit, doze, talk, wave | 4, 2, 4, 4, loop | Seated (the game draws the seat), dozing, gesturing, greeting |

Poses carry secondary motion: `drag` trails hair, capes, hems and tails by a frame (`character_parts.loose_rows`
lists which rows hang free), `crouch` drops the hips and bends the knees, `shift` slides the body and `tilt` topples
it (death). Swing frames carry a motion smear, drawn by the weapon layer from the pose's `smear` angle. `build.py`'s
`ATTACK_FOR` maps each weapon to its basic attack animation, and abilities name theirs in `presentation.anim`.

### Subclasses

There are 36 subclasses (`creator.SUBCLASSES`, three per class), picked at level 3. Each overrides parts of the
class kit's gear and colours. Beastmaster, Grovekeeper and Fiendbound also name a companion, which the bosses set
draws. Pass `make_character(..., subclass=id)`.

The barbarian's paths are the Berserker (a wolf-head pelt, an axe in each hand, crimson war paint), the
Earthshaker (a stone maul and a horned helm) and the Spiritwarden (antlers and a bone totem with an ancestor's
wisp). Each sorcerer bloodline has a signature piece that changes the silhouette, so the three read apart at TV
distance:

| Bloodline | Signature | Also |
|---|---|---|
| Draconic | Layered scale pauldrons with horn spines, in the dragon's colour (`shoulders/drake_pauldrons`) | Small horns, scales on the cheeks and neck, a charcoal robe |
| Chaos | An orb of unstable magic hovering off the far shoulder, its core cycling colours as two motes orbit it (`float/chaos_orb`) | A robe split in two clashing colours, wild motes in the hand |
| Runeborn | A carved stone slab floating off each shoulder, glowing runes cut in its face (`shoulders/rune_stones`) | A carved rune staff, rune marks on the face and the backs of the hands |

`shoulders` is drawn over the torso and hair; `float` is drawn first in both facings, so a floating piece passes
behind the body. Floating pieces bob against the body's breath and step.

The previews are `previews/06b-subclasses.png` (front, back and swing for each). The phone's subclass picker reads
`characters/subclasses.png` and `subclasses.json`.

### Townsfolk

17 NPC presets (`recipes.NPCS`) cover exploration and dialogue. Tools such as a pitchfork, smithing hammer, broom,
tankard or scroll ride in the weapon slot, so NPCs animate like anyone else.

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

## Terrain

`content/tactics/base/terrain/` (see `manifest.json`) holds textures for the 3D tile map. A tile is 32 px and a
height step is 16 px.

| Kind | What | Sampling |
|---|---|---|
| `top/` | 128×128 seamless tops: grass (green, lush, dry, autumn), dirt, path, sand, flagstone, cobble, rock, snow, planks, ice, mud, water and lava (8-frame loops) | World-aligned: UV = world XZ / 4 tiles, so neighbouring tiles continue one another |
| `side/` | 128×32 cliff faces: dirt, cliff, brick, ashlar, sandstone, planks | U = distance along the edge / 4 tiles, V = depth below the top / 1 tile |
| `lip/` | 128×12 overhangs: grass, snow (with icicles), moss | Drawn over the top band of a side whose top has one |
| `decal/` | 32×32 ground scatter: flowers, pebbles, leaves, bones, cracks, puddles, blood, scorch | Tile-local, alpha |

Every texture is built from periodic fields (`arcpix/tex.py`: noise, Voronoi, emboss and dithered quantise, all
wrapping), so tiling never shows a seam.

The previews in `12-diorama-*.png` use `arcpix/iso.py`, a small isometric renderer. Maps are written as ASCII in
`parts/dioramas.py`, and a diorama is the quickest way to judge new terrain or props together.

Biome tops: `bog`, `forest_floor`, `forest_needles`, `dunes`, `sandstone`, `ash`, `basalt`, `pavers` and a `frost`
grass. Sides: `lava_side` (a 16-frame crusted cliff with glowing seams), `ice_side`, `basalt_side` and sandstone in
two colours. Water, bog and lava are 16-frame loops. Glowing textures carry `emissive` (a colour plus a rule for
scattering point lights over them). Shore foam is the ambient `shore_foam` decal, laid on each water tile along each
bank edge. Grass and the dirt and cliff sides use the `alt`, `root` and `moss` slots: a hand-made palette must map
them.

## Props

`content/tactics/base/props/` holds 37 billboard props in palette variants, anchored at their ground contact
pixel. Animated props (campfire, torch, banner) are horizontal strips. For each prop the manifest also records:

- `blocks`: whether it stops movement;
- `cover`: `none`, `half` or `full`, for ranged attacks;
- `height`: in height steps, for line of sight.

There are now 68 props, including faceted rocks, boulders and crags in 8 materials, layered-canopy trees, ruins,
fences in four materials, a cart, ladder, cage, barricades, shrine, tomb, lantern, cacti, swamp trees and lily pads.
Kit props are drawn in the map's projection and anchored on the tile centre; `layer: ground` props (lily pads) lie
under units.

**Lights.** Light-source props (campfire, torch, lamp post, crystals, altar, lantern, shrine, vent, will-o'-wisp,
glowing mushrooms) carry `light`: `at` (image px, per frame where it moves), `radius` (tiles), `colour` per variant,
a `flicker` curve name and an `emissive` L8 mask strip. The manifest's `lighting` group documents placement, the
flicker curves (fire, torch, candle, pulse, steady) and the Godot recipe: an OmniLight3D per light, emission from the
mask. This is what makes HD-2D scenes glow.

**Dioramas** (`previews/12-diorama-*.png`): one per biome kit. Grassland, frostpeak (snow), mirewood (swamp),
sunscar (desert), ember-keep (volcanic, lit only by its own lights), greenhollow (forest) and highward (castle
courtyard). Interiors are the dungeon set's hall and crypt.

## Portraits

`content/tactics/base/portraits/` holds head-and-shoulders busts. They're made from the character's own layers
(minus weapon and off-hand) in the front idle or hurt pose. The image is cropped to `crop[build]` from the
manifest, drawn over a backdrop and scaled 2× to 40×40. Baked portraits ship for every preset; the game makes
them for custom heroes the same way.

## UI kit

`content/tactics/base/ui/` holds the HUD from the mockups: navy glass and gold. The manifest gives every rect,
9-slice inset, outset (studs and glows reach outside the box), padding and the text colour to use on each piece.

| File | What |
|---|---|
| `panels.png` | Main panel, card, tooltip (with tail), dialogue (with name tab), ability banner (with crest) and the turn-order strip, all 9-slice |
| `buttons.png` | Primary (gold), secondary (navy) and danger (red), each normal, hover, pressed (`contentOffset` [0, 1]) and disabled |
| `bars.png` | Standard and slim frames. Fills for HP, enemy HP, MP, XP and CT, each with a left cap, a tiling middle and a bright leading edge, plus a pale "ghost" for damage just taken. `compose` in the manifest gives the recipe |
| `markers.png` | 64×32 iso tile markers (move, attack, area, ally, selected) as 4-frame pulses, plus 14 path pieces. The origin is the tile top's centre |
| `cursor_*.png` | Breathing corner brackets and a bobbing gem arrow |
| `turn_order.png` | Portrait frames sized to the portraits set (active, inactive, enemy with spiked corners), a NOW tab, an AI badge and p1–p6 accent stripes |
| `numbers.png` | Bitmap combat numbers (0–9, `+`, `-`, MISS, CRIT) in damage, heal, crit and mana styles, with advances. Draw them at 2× the UI scale |
| `dice.png` | A gold and a dark d20 with the number box left blank (`numberCentre`), plus a crit starburst |

| `hud.png` | Tooltip with a header band, toasts (info, success, warning, danger), objective tracker, reaction prompt panel, minimap frame and compass, map pips, glints |
| `glyphs.png` | Controller and keyboard prompts: Xbox and PlayStation face buttons, bumpers, triggers, d-pad, sticks, keycaps A–Z, 0–9 and named keys, a 9-slice blank key, mouse buttons |

Animated strips: `panel_shimmer` (a gleam that stays in the 9-slice corners), `countdown_ring` (turns ember in the last
quarter), `heal_sweep` and `ghost_drain` (see `bars.compose`). Buttons have a `focused` state for the controller
cursor, and the turn order has `frame_p1..p6` and `frame_active_p1..p6`.

`previews/15-ui-mock.png` is a whole battle screen composed from the kit, terrain, characters and portraits.

## Beasts

`content/tactics/base/beasts/` holds monsters off the humanoid rig: a quadruped rig (wolf, boar, bear) and drawings
of their own (drake, giant spider, slime, giant bat). Frames are 48×48, anchored at (24, 44), with idle 6, walk 8 (a
four-beat gait), attack 7 (coil, lunge, held bite, recover), special 7, hurt 3 and down 4 (hold the last frame).
Row 0 is the front; row 1 is the back, drawn from behind (rump and tail nearest the camera). Bodies are lit masses
(`arcpix/form.py`), with fur as short combed strokes and scales as rows, both anchored to the body so they don't
crawl between frames. Variants include alpha wolf, undead bear, crystal slime, vampire bat and phase spider; some
variants change the drawing (`forms`). `previews/18-beasts-scale.png` sets them beside a hero.

## Bestiary

`content/tactics/base/bestiary/` holds creatures drawn ahead of their stat blocks: warg (with a goblin-rider
variant), hookbeak, giant scorpion, harpy, will-o'-wisp, gargoyle and giant crab. They follow the beasts contract.
No rules refer to them yet. To use one, give it a stat block and move it into `beasts` or `bosses`: a C# test
requires every creature there to have one.

## Icons

`content/tactics/base/icons/` holds four atlases with a 1 px gutter:

| Atlas | Contents |
|---|---|
| Abilities | 34 icons at 24×24, coloured by class |
| Items | 35 icons at 24×24 |
| Status | 18 icons at 16×16 |
| Frames | 26×26 frames: five rarities plus an ability frame |

`manifest.json` maps each icon id to its atlas rect, category and tags. Palette-swapped icons (potions, gems)
share a `shape`.

**Ability icons by id.** `abilities_by_id.png` (24 px) and `abilities_by_id@2x.png` (drawn natively at 48 px, for
UI scales of 2× and up) hold one icon per player ability in the rules pack. The art key is `icons.<ability id>`
(`icons.wizard.fire_bolt`); `manifest.abilityIcons[id]` gives `rect`/`rect2x`, `class`, `tier`, `glyph`, `badge` and
`reaction`. The grammar: the plate is the class colour, the glyph is the effect or damage type, the badge is the
twist, a gold tab is the spell tier, a gold hook marks a reaction and three dots mark an area version. Specs live in
`parts/ability_icons.py` (`SPECS`); a new ability falls back to `_infer`. Contact sheets per class are
`previews/16-icons-abilities-<class>.png`.

## Effects

`content/tactics/base/fx/` holds 41 effects in colour variants. Each is a horizontal strip, and its manifest
entry gives:

- frame size (up to 96–128 px for big spells), fps, `loop` and `loopStart`;
- the anchor, and where it attaches: `feet`, `body`, `overhead` or `projectile` (projectile art faces +x and the
  game rotates it);
- blend mode: `add` for glows, `normal` for smoke and dust;
- layer: `ground` (under units) or `over`.

Big effects are built in layers: anticipation, a 1–2 frame white-hot `flash`, core, body, sparks with motion trails
(a small seeded particle model) and residue. The Elements group gives every damage type its own look (flames,
frost, shock, necrotic, radiant, psychic, thunder, acid, toxin, force); Boons and wards adds restore, ward, bless,
inspire, rage, hold, dispel and web. `signature/` adds 12 school spells (`spell_*`: Fireball, Lightning Bolt, Cone
of Cold, Spirit Guardians and others), whose entries carry `school` instead of `subclass`. Abilities pick their
effect in `presentation.fx`.

## Backgrounds

`content/tactics/base/backgrounds/` holds parallax layers:

- banded sky gradients (day, dusk, night, overcast, ashfall), which tile horizontally;
- tileable far and near mountain ranges for hills, peaks, mesas and volcano (suggested parallax 0.1 and 0.25);
- clouds and a star field.

The manifest pairs each biome with a sky.

## Emotes, status overlays and pickups

| Set | What |
|---|---|
| `emotes/` | 15 speech and thought bubbles (`!`, `?`, `...`, heart, vote check, leader crown, skill-check die ...). Each pops in, then bobs: play 0–3 once, then loop 2–3. The tail tip is the anchor |
| `status/` | 18 looping on-unit effects (stunned, sleeping, poisoned, burning, frozen, marked ...). They use the character frame and anchor, so they draw exactly over the unit. `headOffset` adjusts the overhead ones for short and large builds |
| `pickups/` | Rarity loot beams (additive), a glint, a coin pile and a loot sack. A drop is beam + the item's icon bobbing by `iconBob` + glint |

## World map, titles and the showcase

- **`worldmap/`:** a tileable parchment, 12 location icons, an animated party pin and quest marker, and a generated
  sample campaign map.
- **`title/`:** the game's name lives in `parts/title.py` (`GAME_NAME = "Wardenflame"`, `CAMPAIGN`); a rename is one
  edit and `build.py title`. `logo_gleam` and the `victory_anim`, `defeat_anim` and `level_up_anim` strips animate
  the logo and moment banners. Also: the logo and emblem, moment banners (VICTORY, DEFEAT, LEVEL UP, YOUR TURN, BOSS
  ...), a chapter ribbon, a 5×7 pixel font with advances, and a title-screen key art.
- **`parts/showcase.py`:** renders `previews/anim-battle.gif`, a short battle built only from the pack's files. It's
  the quickest end-to-end check that sets agree on scale, anchors and timing.

## Battle and presentation extras

| Set | What |
|---|---|
| `bosses/` | 2×2 dragon, trolls and ogres, golems, four elementals, wraiths, the mimic, and the subclass companions (imp, hound, sapling) with player-colour accents. Same animation contract as beasts, plus `special` |
| `ambient/` | Environment animation: swaying flora (drop-in replacements for the static props), water, fire and light, interactive objects with named states, particles and weather overlays |
| `projectiles/` | Arrows, bolts, daggers, rocks and spell projectiles drawn natively in 16 headings (row = heading), so nothing is rotated at runtime |
| `hazards/` | Persistent tile effects in the UI markers' diamond, with optional `over` layers and gameplay flags (difficult terrain, damage type) |
| `crests/` | A heraldic shield per class and a medallion per subclass |
| `gear_icons/` | 24×24 bag icons generated from the gear layers, one atlas per metal tier, keyed `layer/variant` |
| `creator/` | Picture chips for the phone's Look step, plus each race's skin range |
| `grades/` | Ten 16-cube colour LUTs (dawn to night, storm, cave, infernal, fey, memory). The manifest includes the post-process shader |
| `transitions/` | Low-res threshold maps for screen wipes. The manifest includes the shader |

## Adding a set

See `STYLE.md` for the art rules. A set is `parts/<name>.py` with a `build()` function. `build.py` finds it, and
it writes under `content/tactics/base/<name>/` plus `previews/NN-<name>*.png`.
