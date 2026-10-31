# Tactics world: the painting workspace

A small Godot 4.7.2 .NET project for painting overworld regions. The scene in `regions/` is the source of truth, and
`content/tactics/base/rules/world/<id>.json` is always generated from it by the **Export region** button.

The project only reads and calls [`games/Tactics/Tactics.WorldTools`](../../games/Tactics/Tactics.WorldTools), which holds the
logic and its tests. The C# here is just the part that needs Godot.

## Opening it

Build first, because the plugin is C#:

```sh
dotnet build tools/tactics-world/Tactics.World.Editor.csproj
```

Then open `tools/tactics-world/project.godot` in the Godot .NET editor. The **Region** dock, with the Export region button, is on
the right.

## A new region

```sh
"$GODOT" --headless --path tools/tactics-world -- --new=greyfen --chapter=2 --size=160x120 --name="The Greyfen"
```

That writes `regions/greyfen.tscn` with every node in place. It refuses if the scene exists, so painted work can't be replaced. The
scene's file name must match its `Id`, or the export says so: two scenes with one `Id` would overwrite each other's file.

## A region scene

The grid is flat, not isometric: the terrain art is sampled across several tiles, so it can't be painted tile by tile in the
game's view. You paint here and see the isometric result in the game.

| Node | Type | Holds |
|---|---|---|
| root | `Region` | `Id`, `Name`, `Chapter` and `Size` in tiles (drawn as a yellow outline) |
| `Material` | `TileMapLayer` | One swatch per material, each the corner of its real texture. Every tile of a painted chunk needs one |
| `Height` | `TileMapLayer` | A coloured wash with its number, 1 to 9, cold to hot. Blank means 0, and a painted 0 is clear too |
| `Flags` | `TileMapLayer` | Lettered marks. Blank means plain ground |
| `Beats` | `Node2D` | `BeatMarker`s, each naming a beat in `lore/arcs.json` for the region's chapter |
| `Stamps` | `Node2D` | `StampMarker`s, placed at the battle map's corner, drawn as an outline |
| `Spawns` | `Node2D` | One `SpawnMarker` of each `Kind`: `Start` and `Recovery` |

Select a layer and paint it. These six nodes are locked at the origin, because moving one would shift what's drawn but not what's
exported, and the export refuses if one has moved. Markers snap to tiles as you drag them and outline the tile they stand on. A
marker under the wrong parent isn't exported, and the export says so.

A chunk (32x32 tiles) with nothing painted is void, which is fine. A chunk with some paint must have every tile painted: a missed
tile would play as an invisible wall, so the export refuses it. Paint it, with the Impassable flag if a wall is what you want.

**Heights** read as bands of colour when you zoom out, like a contour map: blue is low, then cyan, green, lime, yellow, orange,
red, pink and violet at 9.

**Flags** are combined on one tile, so the palette has all 32 combinations:

| Mark | Flag | Place on the tile |
|---|---|---|
| W | Water | top left |
| R | Difficult (rough) | top right |
| X | Impassable | bottom left |
| H | Hazard | bottom right |
| D | Deep | top middle |

The small dot is the combination with no flags, which is the same as a blank cell.

**Stamps.** A stamped area is a tuned battle, so its tiles must match the map exactly. To place one, add a `StampMarker` under
`Stamps`, set its `Map` and `HeightOffset`, drag it to the map's corner, keep it selected and press **Apply stamp**, which writes the
map to the three layers as one undoable action. To move one, drag the marker, whose outline turns orange, and press Apply stamp
again. Nothing records what was under a stamp, so the dock lists the old tiles it left, for you to repaint. Don't paint inside the
outline: the export refuses a stamp that doesn't match, and Apply stamp puts it back.

The palette has a swatch for every material a battle map uses, such as `dungeon_wall/dark`, which shows the art the game draws for
it. Outside the terrain tops, you can only use one if a map stamped in this region uses it.

**Sides aren't painted.** The game chooses them.

## Exporting

The button saves the scene, then runs every check the pack loader runs, on a temporary copy of the pack, and lists every problem at
once. Each says what to do, and clicking one that names a tile centres the view on it and marks it. The file is written only if
there are no problems, so a refused export leaves the old file as it was. It doesn't change `manifest.json`: a new region is listed
there by hand, once, and the status line says so until it is.

From a command line, the same code runs headless:

```sh
"$GODOT" --headless --path tools/tactics-world -- --export=ashen_ford
```

It exits 0 on success and 1 if the export found problems. `--rules=<folder>` writes to another copy of the pack instead.
`--export` also takes a scene path. To press the plugin's real button with no one there, run the editor:

```sh
"$GODOT" --headless --editor --path tools/tactics-world -- --press-export=ashen_ford
```

`--press-apply=<scene>` presses Apply stamp on the first stamp the same way, moving it first with `--move=x,y`, then undoes it, and
`--click-first-problem` clicks a problem after an export.

These print `Parameter "t" is null` as they save. It's Godot failing to make the scene's thumbnail with no renderer, and it's
harmless.

## Rebuilding the palette

`palette/` is generated and committed. Rebuild it when the terrain manifest gains a material:

```sh
dotnet build tools/tactics-world/Tactics.World.Editor.csproj
"$GODOT" --headless --path tools/tactics-world -- --swatches
"$GODOT" --headless --path tools/tactics-world --import
"$GODOT" --headless --path tools/tactics-world -- --tilesets
```

Scenes store a material's source id, so a rebuild keeps every existing source and adds new ones after them. A material dropped
from the manifest stays in the tile set, and the export names the tiles that still use it. Delete `palette/*.tres` only if no
painted scene exists.

## Trying it on a test scene

`--sample=<scene>` writes a small region around River Crossing, or `--map=<id>`, and adding `--tamper` paints over a stamped tile, which
the export must refuse. Export to a copy of the pack, so the real one isn't touched:

```sh
"$GODOT" --headless --path tools/tactics-world -- --sample=res://regions/_sample.tscn --tamper
"$GODOT" --headless --path tools/tactics-world -- --export=res://regions/_sample.tscn --rules=/tmp/rules-copy
```
