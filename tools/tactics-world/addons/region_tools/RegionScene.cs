using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The scene convention. A region scene has a <see cref="Region"/> root, three <see cref="TileMapLayer"/>s on one flat grid and
/// three nodes of markers, all six at the origin. On the layers, a Material cell's source is named after the material, a Height
/// cell's atlas column is the height, and a Flags cell's atlas position is <see cref="PaletteBuilder.FlagsAtlas"/>. A tile can be left
/// blank on Height, meaning 0, and on Flags, meaning none.
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

    private static readonly string[] Layers = [MaterialLayer, HeightLayer, FlagsLayer];

    private static readonly string[] Groups = [Beats, Stamps, Spawns];

    /// <summary>
    /// Exports the scene. The plugin's button and the headless <c>--export</c> command both call this. It writes
    /// <c>world/&lt;id&gt;.json</c> into the base pack, or into <paramref name="rulesDirectory"/> if given.
    /// </summary>
    public static ExportResult Export(Region root, string? rulesDirectory = null)
    {
        var missing = MissingParts(root);
        return missing.Count > 0 ? new ExportResult(null, [.. missing]) : RegionExport.Export(Read(root), rulesDirectory ?? RegionPaths.Rules, RegionPaths.Content);
    }

    /// <summary>What's in the scene, without judging it. Every part must be there: see <see cref="MissingParts"/>.</summary>
    public static PaintedRegion Read(Region root)
    {
        var material = root.GetNode<TileMapLayer>(MaterialLayer);
        var height = root.GetNode<TileMapLayer>(HeightLayer);
        var flags = root.GetNode<TileMapLayer>(FlagsLayer);
        var spawns = root.GetNode(Spawns).GetChildren().OfType<SpawnMarker>().ToList();
        var scene = root.SceneFilePath;

        return new PaintedRegion(root.Id, root.Name, root.Chapter, root.Size.X, root.Size.Y)
        {
            Materials = material.GetUsedCells().ToDictionary(ToTile, cell => MaterialName(material, cell)),
            Heights = height.GetUsedCells().ToDictionary(ToTile, cell => height.GetCellAtlasCoords(cell).X),
            Flags = flags.GetUsedCells().ToDictionary(ToTile, cell =>
            {
                var at = flags.GetCellAtlasCoords(cell);
                return PaletteBuilder.FlagsAt(at.X, at.Y);
            }),
            Beats = [.. root.GetNode(Beats).GetChildren().OfType<BeatMarker>().Select(b => new BeatSpot(b.Beat, b.Tile))],
            Stamps = [.. root.GetNode(Stamps).GetChildren().OfType<StampMarker>().Select(s => new RegionStamp(s.Map, s.Tile, s.HeightOffset))],
            Starts = [.. spawns.Where(s => s.Kind == SpawnMarker.SpawnKind.Start).Select(s => s.Tile)],
            Recoveries = [.. spawns.Where(s => s.Kind == SpawnMarker.SpawnKind.Recovery).Select(s => s.Tile)],
            OldStamps = [.. root.GetNode(Stamps).GetChildren().OfType<StampMarker>().SelectMany(s => s.Pending)],
            MovedNodes = [.. Layers.Concat(Groups).Where(name => root.GetNode<Node2D>(name).Transform != Transform2D.Identity)],
            Strays = Strays(root),
            SceneFile = scene.Length > 0 ? Path.GetFileNameWithoutExtension(scene) : null,
        };
    }

    /// <summary>What the scene lacks of the convention, as lines for the problem list.</summary>
    public static IReadOnlyList<string> MissingParts(Region root)
    {
        var layers = Layers.Where(name => root.GetNodeOrNull<TileMapLayer>(name) is null)
            .Select(name => $"The scene has no {name} tile layer. Add a TileMapLayer called {name} under the Region node, or make the scene with --new.");
        var groups = Groups.Where(name => root.GetNodeOrNull<Node2D>(name) is null)
            .Select(name => $"The scene has no {name} node. Add a Node2D called {name} under the Region node, or make the scene with --new.");
        return [.. layers, .. groups];
    }

    /// <summary>
    /// Builds a whole scene from a grid: every tile on its layers, and a marker per beat, stamp and spawn. A stamp's tiles must
    /// already be in the grid, as <see cref="RegionSource.Stamp"/> leaves them. Throws if a tile has no way to be shown: a height
    /// outside 0 to 9, or a material with no source in the palette. Save the result with <see cref="Save"/>.
    /// </summary>
    public static Region Build(RegionSource source)
    {
        var root = new Region { Id = source.Id, Name = source.Name, Chapter = source.Chapter, Size = new Vector2I(source.Width, source.Height) };
        ((Node)root).Name = nameof(Region);

        var materialSet = GD.Load<TileSet>(Palette.Materials);
        var sources = MaterialSources(materialSet);
        var material = AddLayer(root, MaterialLayer, materialSet);
        var height = AddLayer(root, HeightLayer, GD.Load<TileSet>(Palette.Heights));
        var flags = AddLayer(root, FlagsLayer, GD.Load<TileSet>(Palette.Flags));

        // Zoomed out, the mipmaps blur a height's number into its wash, so the shape of the land shows and the digits don't speckle.
        height.TextureFilter = CanvasItem.TextureFilterEnum.NearestWithMipmaps;

        foreach (var (tile, info) in source.Tiles)
        {
            var cell = new Vector2I(tile.X, tile.Y);
            if (info.Height is < 0 or > WorldRegion.MaxHeight)
                throw new ArgumentException($"The tile {tile} has height {info.Height}, but a layer can only hold 0 to {WorldRegion.MaxHeight}.", nameof(source));
            if (!sources.TryGetValue(info.Material, out var id))
                throw new ArgumentException($"The tile {tile} uses '{info.Material}', which has no swatch. Rebuild the palette: see the README.", nameof(source));
            material.SetCell(cell, id, Vector2I.Zero);
            if (HeightAtlas(info.Height) is { } heightTile)
                height.SetCell(cell, 0, heightTile);
            if (FlagsAtlas(info.Flags) is { } flagsTile)
                flags.SetCell(cell, 0, flagsTile);
        }

        var beats = AddGroup(root, Beats);
        foreach (var beat in source.Beats)
            AddMarker(root, beats, new BeatMarker { Beat = beat.Beat }, beat.Beat, beat.At);
        var stamps = AddGroup(root, Stamps);
        foreach (var stamp in source.Stamps)
            AddMarker(root, stamps, new StampMarker { Map = stamp.Map, HeightOffset = stamp.Height, AppliedAt = new Vector2I(stamp.At.X, stamp.At.Y), AppliedMap = stamp.Map, AppliedHeightOffset = stamp.Height }, stamp.Map, stamp.At);
        var spawns = AddGroup(root, Spawns);
        AddMarker(root, spawns, new SpawnMarker { Kind = SpawnMarker.SpawnKind.Start }, "Start", source.Start);
        AddMarker(root, spawns, new SpawnMarker { Kind = SpawnMarker.SpawnKind.Recovery }, "Recovery", source.Recovery);
        return root;
    }

    /// <summary>
    /// Saves a scene <see cref="Build"/> made. Painted work is never overwritten, so this is the one place that checks: it throws
    /// <see cref="SceneExistsException"/> if <paramref name="path"/> exists, unless <paramref name="overwrite"/> says the caller means to
    /// replace it. The plugin's saves of the open scene go through the editor's own <c>SaveScene</c>, not here.
    /// </summary>
    public static void Save(Region root, string path, bool overwrite = false)
    {
        SceneFile.Refuse(ProjectSettings.GlobalizePath(path), overwrite);
        var scene = new PackedScene();
        var error = scene.Pack(root);
        if (error == Error.Ok)
            error = ResourceSaver.Save(scene, path);
        if (error != Error.Ok)
            throw new IOException($"Couldn't save {path}: {error}.");
    }

    /// <summary>Builds a scene from a grid and saves it to a new file, as <see cref="Build"/> and <see cref="Save"/> do.</summary>
    public static void Write(RegionSource source, string path)
    {
        var root = Build(source);
        try
        {
            Save(root, path);
        }
        finally
        {
            root.Free();
        }
    }

    /// <summary>Each material's source in the Material layer's tile set.</summary>
    public static Dictionary<string, int> MaterialSources(TileSet set)
        => Enumerable.Range(0, set.GetSourceCount()).Select(set.GetSourceId).ToDictionary(id => set.GetSource(id).ResourceName, id => id, StringComparer.Ordinal);

    /// <summary>The Height layer's tile for a height, or null for 0, which is left blank.</summary>
    public static Vector2I? HeightAtlas(int height) => height == 0 ? null : new Vector2I(height, 0);

    /// <summary>The Flags layer's tile for some flags, or null for none, which is left blank.</summary>
    public static Vector2I? FlagsAtlas(TileFlags flags)
    {
        if (flags == TileFlags.None)
            return null;
        var (x, y) = PaletteBuilder.FlagsAtlas(flags);
        return new Vector2I(x, y);
    }

    private static List<StrayNode> Strays(Region root)
    {
        var strays = new List<StrayNode>();
        var groups = Groups.Select(name => root.GetNode(name)).ToList();
        foreach (var marker in root.FindChildren("*", "", recursive: true, owned: false).OfType<TileMarker>())
        {
            var parent = marker.GetParent();
            if (parent != root.GetNode(marker.Group))
                strays.Add(new StrayNode($"{marker.GetType().Name} '{((Node)marker).Name}'", parent == root ? "the root" : ((Node)parent).Name, $"Move it under {marker.Group}", marker.Tile));
        }
        foreach (var group in groups)
            foreach (var child in group.GetChildren().Where(c => c is not TileMarker))
                strays.Add(new StrayNode($"node '{child.Name}'", group.Name, $"Give it a marker type, or delete it", child is Node2D node ? PaintedRegion.TileAt(node.Position.X, node.Position.Y) : default));
        return strays;
    }

    private static Tile ToTile(Vector2I cell) => new(cell.X, cell.Y);

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

    private static void AddMarker(Region root, Node parent, TileMarker marker, string name, Tile tile)
    {
        marker.Position = marker.PlaceOn(tile);
        Add(root, parent, marker, name);
    }

    private static void Add(Region root, Node parent, Node child, string name)
    {
        child.Name = name;
        parent.AddChild(child);
        child.Owner = root;
    }
}
