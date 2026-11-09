using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The workspace's main scene, which runs a command given after <c>--</c> and quits: see <see cref="Usage"/>. It exits 0 on success,
/// 1 if the export found problems and 2 if the command couldn't run, which includes a scene that's already there.
/// </summary>
public partial class Commands : Node
{
    private const string Usage = """
        Commands, after -- on Godot's command line:
          --draft=<id>             write Claude's draft of a region into its scene; refuses if the scene exists
          --dress=<id>             dress Claude's draft of a region with props; refuses if props already exist
          --new=<id>               make an empty region scene in regions/; --chapter=<n> --size=<w>x<h> --name=<name> are optional
          --export=<id or scene>   export a region; --rules=<folder> writes to that pack instead of the base pack
          --remap-props=<id>       remap a scene's prop atlas after a rebuild; requires --old-props=<manifest>; repeat --replace-prop=old@variant=new@variant to change painted variants
          --paint-region=<json>   paint explicit material/height/flags cells, preserving markers and unrelated paint
          --edit-props=<id>        change single props in a scene and leave its paint alone; repeat --clear-prop=<x>,<y> and --place-prop=<id>@<variant>@<x>,<y>
          --import-placements=<id> add interactable/mob markers and bind shopkeeper labels, refusing existing placement groups
          --check-placement-markers verify native position edits and deterministic exports in temporary files
          --check-person-variants   verify native editor variant save/load/export in a temporary scene
          --swatches               write the palette's images (then run Godot's --import)
          --tilesets               write the palette's tile sets
          --sample=<scene>         write a small test scene with a stamp; --map=<id> picks the map, --tamper paints over a stamped tile
        """;

    /// <summary>The value of <c>--name=value</c> after <c>--</c>, or null.</summary>
    public static string? Arg(string name)
    {
        var prefix = $"--{name}=";
        return OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    private static bool Flag(string name) => OS.GetCmdlineUserArgs().Contains($"--{name}");

    /// <inheritdoc />
    public override void _Ready()
    {
        int code;
        try
        {
            code = Run();
        }
        catch (SceneExistsException ex)
        {
            GD.PrintErr(ex.Message);
            code = 2;
        }
        catch (Exception ex)
        {
            GD.PrintErr(ex.ToString());
            code = 2;
        }
        GetTree().Quit(code);
    }

    private static int Run()
    {
        if (Flag("check-placement-markers")) return PlacementMarkerCheck.Run();
        if (Arg("import-placements") is { } placements)
        {
            var path = RegionPaths.Scene(placements);
            var root = ResourceLoader.Load<PackedScene>(path).Instantiate<Region>();
            try { Placements.Import(root, Arg("rules") ?? RegionPaths.Rules); RegionScene.Save(root, path, overwrite: true); }
            finally { root.Free(); }
            GD.Print("Imported placement markers: " + path);
            return 0;
        }
        if (Flag("check-person-variants"))
            return PersonVariantCheck.Run();

        if (Arg("paint-region") is { } edits) return ApplyMobArenas(edits);
        if (Arg("apply-mob-arenas") is { } mobEdits) return ApplyMobArenas(mobEdits);
        if (Arg("export") is { } export)
            return Export(export);
        if (Arg("remap-props") is { } remap)
            return RemapProps(remap);
        if (Arg("edit-props") is { } edit)
            return EditProps(edit);
        if (Arg("draft") is { } draft)
            return DraftScene.Run(draft);
        if (Arg("dress") is { } dress)
            return DraftScene.Dress(dress);
        if (Arg("new") is { } id)
            return New(id);
        if (Arg("sample") is { } sample)
            return Sample(sample);
        if (Flag("swatches"))
            Palette.WriteImages();
        else if (Flag("tilesets"))
            Palette.WriteTileSets();
        else
            GD.Print(Usage);
        return 0;
    }

    private static int Export(string idOrPath)
    {
        var path = RegionPaths.Scene(idOrPath);
        if (ResourceLoader.Load<PackedScene>(path)?.Instantiate() is not Region root)
        {
            GD.PrintErr($"{path} isn't a region scene.");
            return 2;
        }
        var result = RegionScene.Export(root, Arg("rules"));
        root.Free();
        foreach (var warning in result.Warnings.Concat(result.StoryWarnings))
            GD.Print($"WARNING: {warning}");
        foreach (var problem in result.Problems)
            GD.Print($"PROBLEM: {problem}");
        GD.Print(result.Status);
        return result.Written is null ? 1 : 0;
    }

    private static int ApplyMobArenas(string instructions)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(instructions));
        foreach (var edit in doc.RootElement.GetProperty("regions").EnumerateArray())
        {
            var path = RegionPaths.Scene(edit.GetProperty("region").GetString()!);
            if (ResourceLoader.Load<PackedScene>(path)?.Instantiate() is not Region root) return 2;
            try
            {
                var material = root.GetNode<TileMapLayer>(RegionScene.MaterialLayer);
                var height = root.GetNode<TileMapLayer>(RegionScene.HeightLayer);
                var flags = root.GetNode<TileMapLayer>(RegionScene.FlagsLayer);
                foreach (var paint in edit.GetProperty("paint").EnumerateArray())
                {
                    var at = paint.GetProperty("at"); var tile = new Vector2I(at[0].GetInt32(), at[1].GetInt32());
                    var name = paint.GetProperty("material").GetString();
                    var source = Enumerable.Range(0, material.TileSet.GetSourceCount()).Select(material.TileSet.GetSourceId)
                        .Single(id => material.TileSet.GetSource(id).ResourceName == name);
                    material.SetCell(tile, source, Vector2I.Zero);
                    height.EraseCell(tile); flags.EraseCell(tile);
                    if (paint.TryGetProperty("height", out var level))
                    {
                        var value = level.GetInt32();
                        if (value is < 0 or > WorldRegion.MaxHeight) throw new ArgumentOutOfRangeException("height");
                        height.SetCell(tile, 0, new Vector2I(value, 0));
                    }
                    if (paint.TryGetProperty("flags", out var bits))
                    {
                        var value = bits.GetInt32();
                        if (value is < 0 or > 31) throw new ArgumentOutOfRangeException("flags");
                        var atlas = PaletteBuilder.FlagsAtlas((TileFlags)value);
                        flags.SetCell(tile, 0, new Vector2I(atlas.X, atlas.Y));
                    }
                }
                var stamps = root.GetNode(RegionScene.Stamps);
                foreach (var stamp in edit.GetProperty("stamps").EnumerateArray())
                {
                    var name = stamp.GetProperty("map").GetString()!;
                    if (stamps.GetChildren().OfType<StampMarker>().Any(s => s.Map == name)) continue;
                    var at = stamp.GetProperty("at"); var tile = new Tile(at[0].GetInt32(), at[1].GetInt32());
                    var marker = new StampMarker { Name = name, Map = name, AppliedMap = name, AppliedAt = new Vector2I(tile.X, tile.Y),
                        HeightOffset = stamp.GetProperty("height").GetInt32(), AppliedHeightOffset = stamp.GetProperty("height").GetInt32() };
                    marker.Position = marker.PlaceOn(tile); stamps.AddChild(marker); marker.Owner = root;
                }
                // Extracted map props own the dressing inside a stamp; retaining scene props too would draw them twice.
                var props = root.GetNodeOrNull<TileMapLayer>(RegionScene.PropsLayer);
                if (props is not null)
                    foreach (var stamp in edit.GetProperty("stamps").EnumerateArray())
                    {
                        var at = stamp.GetProperty("at");
                        for (var y = 0; y < 12; y++)
                            for (var x = 0; x < 14; x++) props.EraseCell(new Vector2I(at[0].GetInt32() + x, at[1].GetInt32() + y));
                    }
                var packed = new PackedScene(); var error = packed.Pack(root);
                if (error != Error.Ok || ResourceSaver.Save(packed, path) != Error.Ok) return 2;
                GD.Print($"Updated mob arenas in {path}; existing paint preserved.");
            }
            finally { root.Free(); }
        }
        return 0;
    }

    private static int EditProps(string idOrPath)
    {
        var path = RegionPaths.Scene(idOrPath);
        if (ResourceLoader.Load<PackedScene>(path)?.Instantiate() is not Region root)
        {
            GD.PrintErr($"{path} isn't a region scene.");
            return 2;
        }
        try
        {
            var layer = root.GetNodeOrNull<TileMapLayer>(RegionScene.PropsLayer);
            if (layer is null)
            {
                GD.PrintErr($"{path} has no Props layer.");
                return 2;
            }
            var args = OS.GetCmdlineUserArgs();
            foreach (var value in args.Where(a => a.StartsWith("--clear-prop=", StringComparison.Ordinal)))
                layer.EraseCell(ParseCell(value["--clear-prop=".Length..]));
            foreach (var value in args.Where(a => a.StartsWith("--place-prop=", StringComparison.Ordinal)))
            {
                var cut = value.LastIndexOf('@');
                var (id, variant) = ParsePropKey(value["--place-prop=".Length..cut]);
                var (x, y) = PaletteBuilder.PropAtlas(id, variant);
                layer.SetCell(ParseCell(value[(cut + 1)..]), 0, new Vector2I(x, y));
            }
            var packed = new PackedScene();
            var error = packed.Pack(root);
            if (error == Error.Ok)
                error = ResourceSaver.Save(packed, path);
            if (error != Error.Ok)
            {
                GD.PrintErr($"Couldn't save {path}: {error}.");
                return 2;
            }
            GD.Print($"{path} now has {layer.GetUsedCells().Count} prop cells.");
            return 0;
        }
        finally
        {
            root.Free();
        }
    }

    private static Vector2I ParseCell(string value)
    {
        var parts = value.Split(',', 2);
        return new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static int RemapProps(string idOrPath)
    {
        var oldManifest = Arg("old-props");
        if (oldManifest is null || !File.Exists(oldManifest))
        {
            GD.PrintErr("--remap-props requires --old-props=<path to the previous props/manifest.json>.");
            return 2;
        }
        var path = RegionPaths.Scene(idOrPath);
        if (ResourceLoader.Load<PackedScene>(path)?.Instantiate() is not Region root)
        {
            GD.PrintErr($"{path} isn't a region scene.");
            return 2;
        }
        try
        {
            var replacements = OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--replace-prop=", StringComparison.Ordinal))
                .Select(a => a["--replace-prop=".Length..].Split('=', 2))
                .ToDictionary(parts => ParsePropKey(parts[0]), parts => ParsePropKey(parts[1]));
            var oldSwatches = PaletteBuilder.PropSwatches(oldManifest)
                .Select(swatch => replacements.TryGetValue((swatch.Id, swatch.Variant), out var target)
                    ? swatch with { Id = target.Id, Variant = target.Variant }
                    : swatch)
                .ToArray();
            var newSwatches = PaletteBuilder.AllPropSwatches(RegionPaths.Content);
            var remap = PaletteBuilder.PropAtlasRemap(oldSwatches, newSwatches);
            var layer = root.GetNodeOrNull<TileMapLayer>(RegionScene.PropsLayer);
            if (layer is null)
            {
                GD.PrintErr($"{path} has no Props layer.");
                return 2;
            }
            foreach (var cell in layer.GetUsedCells())
            {
                var old = layer.GetCellAtlasCoords(cell);
                if (!remap.TryGetValue((old.X, old.Y), out var next))
                {
                    GD.PrintErr($"The old props palette has no swatch at ({old.X},{old.Y}).");
                    return 2;
                }
                layer.SetCell(cell, layer.GetCellSourceId(cell), new Vector2I(next.X, next.Y), layer.GetCellAlternativeTile(cell));
            }
            var packed = new PackedScene();
            var error = packed.Pack(root);
            if (error == Error.Ok)
                error = ResourceSaver.Save(packed, path);
            if (error != Error.Ok)
            {
                GD.PrintErr($"Couldn't save {path}: {error}.");
                return 2;
            }
            GD.Print($"Remapped {layer.GetUsedCells().Count} prop cells in {path}.");
            return 0;
        }
        finally
        {
            root.Free();
        }
    }

    private static (string Id, string? Variant) ParsePropKey(string value)
    {
        var parts = value.Split('@', 2);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Prop keys must be written as id@variant, not '{value}'.");
        return (parts[0], parts[1]);
    }

    /// <summary>An empty scene for a new region, which refuses to replace one that exists, as painted work is never overwritten.</summary>
    private static int New(string id)
    {
        var path = RegionPaths.Scene(id);
        if (!RegionExport.ValidId(id))
        {
            GD.PrintErr($"The region id '{id}' must be lower-case letters, digits and underscores.");
            return 2;
        }
        var size = (Arg("size") ?? "64x64").Split('x');
        var source = new RegionSource(id, Arg("name") ?? id, int.Parse(Arg("chapter") ?? "1"), int.Parse(size[0]), int.Parse(size[1]));
        RegionScene.Write(source, path);
        GD.Print($"Wrote {path}. Paint it, and move its Start and Recovery markers.");
        return 0;
    }

    /// <summary>
    /// A region of grass around a stamped map, River Crossing unless <c>--map</c> says otherwise, starting on the map's party spawn.
    /// Its id is the scene's file name. <c>--tamper</c> changes a stamped tile, which must be refused.
    /// </summary>
    private static int Sample(string path)
    {
        var map = RulesPack.Load([RegionPaths.Rules]).MapFor(Arg("map") ?? "river_crossing")!;
        var corner = new Tile(4, 4);
        var start = map.Zones.TryGetValue("party_spawn", out var zone) && zone.Length > 0 ? new Tile(zone[0].X + corner.X, zone[0].Y + corner.Y) : new Tile(0, 0);
        var source = new RegionSource(Path.GetFileNameWithoutExtension(path), "Sample", 1, map.Width + 8, map.Height + 8) { Start = start, Recovery = start };
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                source.Set(new Tile(x, y), 0, "grass/lush", TileFlags.None);
        source.Stamp(map, corner, 0);
        source.Beats.Add(new BeatSpot("ch1_mayors_request", start));
        if (Flag("tamper"))
            source.Set(corner, 2, "ash/grey", TileFlags.None);

        RegionScene.Write(source, path);
        GD.Print($"Wrote {path}");
        return 0;
    }
}
