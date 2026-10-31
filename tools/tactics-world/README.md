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

## A region scene

The grid is flat, not isometric: the terrain art is sampled across several tiles, so it can't be painted tile by tile in the
game's view. You paint here and see the isometric result in the game.

| Node | Type | Holds |
|---|---|---|
| root | `Region` | `Id`, `Name`, `Chapter` and `Size` in tiles (drawn as a yellow outline) |
| `Material` | `TileMapLayer` | One swatch per material, each the corner of its real texture. Every tile of the region needs one |
| `Height` | `TileMapLayer` | A numbered shade, 0 to 9. Blank means 0 |
| `Flags` | `TileMapLayer` | Lettered marks. Blank means plain ground |
| `Beats` | `Node2D` | `BeatMarker`s, each naming a beat in `lore/arcs.json` |
| `Stamps` | `Node2D` | `StampMarker`s, placed at the battle map's corner, drawn as a locked red outline |
| `Spawns` | `Node2D` | One `SpawnMarker` of each `Kind`: `Start` and `Recovery` |

Select a layer and paint it. A marker stands on whichever tile its position falls in.

**Flags** are combined on one tile, so the palette has all 32 combinations:

| Mark | Flag | Place on the tile |
|---|---|---|
| W | Water | top left |
| R | Difficult (rough) | top right |
| X | Impassable | bottom left |
| H | Hazard | bottom right |
| D | Deep | middle |

The small dot is the combination with no flags, which is the same as a blank cell.

**Stamps.** A stamped area is a tuned battle, so its tiles must match the map exactly. The outline shows where not to paint, and
the export refuses the region if a tile under it has changed. The marker locks itself, because moving it without its tiles can
only fail.

**Sides aren't painted.** The game chooses them.

## Exporting

The button saves the scene, then runs every check the pack loader runs, on a temporary copy of the pack, and lists any problems.
It writes the file only if there are none, so a refused export leaves the old file as it was. It doesn't change `manifest.json`:
a new region is listed there by hand, once.

One export can need a second: a problem that stops the region being built at all, such as an unknown material, shows first, and
the checks that need it built, such as reachability, show on the next export.

From a command line, the same code runs headless:

```sh
"$GODOT" --headless --path tools/tactics-world -- --export=ashen_ford
```

It exits 0 on success and 1 if the export found problems. `--rules=<folder>` writes to another copy of the pack instead.
`--export` also takes a scene path. To press the plugin's real button with no one there, run the editor:

```sh
"$GODOT" --headless --editor --path tools/tactics-world -- --press-export=ashen_ford
```

That one prints `Parameter "t" is null` as it saves. It's Godot failing to make the scene's thumbnail with no renderer, and it's
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

`--sample=<scene>` writes a small region around River Crossing, and adding `--tamper` paints over one of its stamped tiles, which
the export must refuse. Export to a copy of the pack, so the real one isn't touched:

```sh
"$GODOT" --headless --path tools/tactics-world -- --sample=res://regions/_sample.tscn --tamper
"$GODOT" --headless --path tools/tactics-world -- --export=res://regions/_sample.tscn --rules=/tmp/rules-copy
```
