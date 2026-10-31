using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The scene convention. A region scene has a <see cref="Region"/> root, three <see cref="TileMapLayer"/>s on one flat grid and
/// three nodes of markers. On the layers, a Material cell's source is named after the material, a Height cell's atlas column is
/// the height, and a Flags cell's atlas position is <see cref="PaletteBuilder.FlagsAtlas"/>. A tile can be left blank on Height,
/// meaning 0, and on Flags, meaning none.
/// </summary>
public static class RegionScene
{
    /// <summary>The Material layer's node name.</summary>
    public const string MaterialLayer = "Material";

    /// <summary>The Height layer's node name.</summary>
    public const string HeightLayer = "Height";

    /// <summary>The Flags layer's node name.</summary>
    public const string FlagsLayer = "Flags";

    /// <summary>The node holding <see cref="BeatMarker"/>s.</summary>
    public const string Beats = "Beats";

    /// <summary>The node holding <see cref="StampMarker"/>s.</summary>
    public const string Stamps = "Stamps";

    /// <summary>The node holding <see cref="SpawnMarker"/>s.</summary>
    public const string Spawns = "Spawns";

    /// <summary>
    /// Exports the scene. The plugin's button and the headless <c>--export</c> command both call this. It writes
    /// <c>world/&lt;id&gt;.json</c> into the base pack, or into <paramref name="rulesDirectory"/> if given.
    /// </summary>
    public static ExportResult Export(Region root, string? rulesDirectory = null)
    {
        var missing = MissingParts(root);
        return missing.Count > 0 ? new ExportResult(null, [.. missing]) : RegionExport.Export(Read(root), rulesDirectory ?? RegionPaths.Rules, RegionPaths.TerrainManifest);
    }

    /// <summary>What's in the scene, without judging it. Every part must be there: see <see cref="MissingParts"/>.</summary>
    public static PaintedRegion Read(Region root)
    {
        var material = root.GetNode<TileMapLayer>(MaterialLayer);
        var height = root.GetNode<TileMapLayer>(HeightLayer);
        var flags = root.GetNode<TileMapLayer>(FlagsLayer);
        var spawns = root.GetNode(Spawns).GetChildren().OfType<SpawnMarker>().ToList();

        return new PaintedRegion(root.Id, root.Name, root.Chapter, root.Size.X, root.Size.Y)
        {
            Materials = material.GetUsedCells().ToDictionary(ToTile, cell => MaterialName(material, cell)),
            Heights = height.GetUsedCells().ToDictionary(ToTile, cell => height.GetCellAtlasCoords(cell).X),
            Flags = flags.GetUsedCells().ToDictionary(ToTile, cell =>
            {
                var at = flags.GetCellAtlasCoords(cell);
                return PaletteBuilder.FlagsAt(at.X, at.Y);
            }),
            Beats = [.. root.GetNode(Beats).GetChildren().OfType<BeatMarker>().Select(b => new BeatSpot(b.Beat, TileOf(b)))],
            Stamps = [.. root.GetNode(Stamps).GetChildren().OfType<StampMarker>().Select(s => new RegionStamp(s.Map, TileOf(s), s.HeightOffset))],
            Starts = [.. spawns.Where(s => s.Kind == SpawnMarker.SpawnKind.Start).Select(TileOf)],
            Recoveries = [.. spawns.Where(s => s.Kind == SpawnMarker.SpawnKind.Recovery).Select(TileOf)],
        };
    }

    /// <summary>What the scene lacks of the convention, as lines for the problem list.</summary>
    public static IReadOnlyList<string> MissingParts(Region root)
    {
        var layers = new[] { MaterialLayer, HeightLayer, FlagsLayer }.Where(name => root.GetNodeOrNull<TileMapLayer>(name) is null)
            .Select(name => $"The scene has no {name} tile layer.");
        var groups = new[] { Beats, Stamps, Spawns }.Where(name => root.GetNodeOrNull(name) is null)
            .Select(name => $"The scene has no {name} node.");
        return [.. layers, .. groups];
    }

    /// <summary>
    /// Builds a whole scene from a grid: every tile on its layers, and a marker per beat, stamp and spawn. A stamp's tiles must
    /// already be in the grid, as <see cref="RegionSource.Stamp"/> leaves them. Throws if a height has no tile to show it, since a
    /// layer can only hold 0 to 9. Save the result with <see cref="Save"/>.
    /// </summary>
    public static Region Build(RegionSource source)
    {
        var root = new Region { Id = source.Id, Name = source.Name, Chapter = source.Chapter, Size = new Vector2I(source.Width, source.Height) };
        ((Node)root).Name = nameof(Region);

        var materialSet = GD.Load<TileSet>(Palette.Materials);
        var sources = Enumerable.Range(0, materialSet.GetSourceCount()).Select(materialSet.GetSourceId)
            .ToDictionary(id => materialSet.GetSource(id).ResourceName, id => id, StringComparer.Ordinal);
        var material = AddLayer(root, MaterialLayer, materialSet);
        var height = AddLayer(root, HeightLayer, GD.Load<TileSet>(Palette.Heights));
        var flags = AddLayer(root, FlagsLayer, GD.Load<TileSet>(Palette.Flags));
        foreach (var (tile, info) in source.Tiles)
        {
            var cell = new Vector2I(tile.X, tile.Y);
            if (info.Height is < 0 or > WorldRegion.MaxHeight)
                throw new ArgumentException($"The tile {tile} has height {info.Height}, but a layer can only hold 0 to {WorldRegion.MaxHeight}.", nameof(source));
            material.SetCell(cell, sources[info.Material], Vector2I.Zero);
            if (info.Height != 0)
                height.SetCell(cell, 0, new Vector2I(info.Height, 0));
            if (info.Flags != TileFlags.None)
            {
                var (x, y) = PaletteBuilder.FlagsAtlas(info.Flags);
                flags.SetCell(cell, 0, new Vector2I(x, y));
            }
        }

        var beats = AddGroup(root, Beats);
        foreach (var beat in source.Beats)
            Add(root, beats, new BeatMarker { Beat = beat.Beat, Position = Centre(beat.At) }, beat.Beat);
        var stamps = AddGroup(root, Stamps);
        foreach (var stamp in source.Stamps)
            Add(root, stamps, new StampMarker { Map = stamp.Map, HeightOffset = stamp.Height, Position = Corner(stamp.At) }, stamp.Map);
        var spawns = AddGroup(root, Spawns);
        Add(root, spawns, new SpawnMarker { Kind = SpawnMarker.SpawnKind.Start, Position = Centre(source.Start) }, "Start");
        Add(root, spawns, new SpawnMarker { Kind = SpawnMarker.SpawnKind.Recovery, Position = Centre(source.Recovery) }, "Recovery");
        return root;
    }

    /// <summary>Saves a scene <see cref="Build"/> made. It overwrites <paramref name="path"/>, so a caller holding painted work checks first.</summary>
    public static void Save(Region root, string path)
    {
        var scene = new PackedScene();
        var error = scene.Pack(root);
        if (error == Error.Ok)
            error = ResourceSaver.Save(scene, path);
        if (error != Error.Ok)
            throw new IOException($"Couldn't save {path}: {error}.");
    }

    private static Tile ToTile(Vector2I cell) => new(cell.X, cell.Y);

    private static Tile TileOf(Marker2D marker) => PaintedRegion.TileAt(marker.Position.X, marker.Position.Y);

    private static Vector2 Corner(Tile tile) => new Vector2(tile.X, tile.Y) * PaintedRegion.TilePixels;

    private static Vector2 Centre(Tile tile) => Corner(tile) + Vector2.One * PaintedRegion.TilePixels / 2;

    /// <summary>The material a cell shows. A source the tile set no longer has keeps a name, so the export can name the tile.</summary>
    private static string MaterialName(TileMapLayer layer, Vector2I cell)
    {
        var id = layer.GetCellSourceId(cell);
        return layer.TileSet.HasSource(id) ? layer.TileSet.GetSource(id).ResourceName : $"source {id}";
    }

    private static TileMapLayer AddLayer(Region root, string name, TileSet tiles)
    {
        // Behind the root, so its outline of the region's size stays on top of the paint.
        var layer = new TileMapLayer { TileSet = tiles, ShowBehindParent = true };
        Add(root, root, layer, name);
        return layer;
    }

    private static Node2D AddGroup(Region root, string name)
    {
        var group = new Node2D();
        Add(root, root, group, name);
        return group;
    }

    private static void Add(Region root, Node parent, Node child, string name)
    {
        child.Name = name;
        parent.AddChild(child);
        child.Owner = root;
    }
}
