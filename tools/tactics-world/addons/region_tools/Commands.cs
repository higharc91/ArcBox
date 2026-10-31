using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The workspace's main scene, which runs a command given after <c>--</c> and quits: see <see cref="Usage"/>. It exits 0 on success,
/// 1 if the export found problems and 2 if the command couldn't run.
/// </summary>
public partial class Commands : Node
{
    private const string Usage = """
        Commands, after -- on Godot's command line:
          --draft=<id>             write Claude's draft of a region into its scene; refuses if the scene exists
          --new=<id>               make an empty region scene in regions/; --chapter=<n> --size=<w>x<h> --name=<name> are optional
          --export=<id or scene>   export a region; --rules=<folder> writes to that pack instead of the base pack
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
        catch (Exception ex)
        {
            GD.PrintErr(ex.ToString());
            code = 2;
        }
        GetTree().Quit(code);
    }

    private static int Run()
    {
        if (Arg("export") is { } export)
            return Export(export);
        if (Arg("draft") is { } draft)
            return DraftScene.Run(draft);
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
        foreach (var warning in result.Warnings)
            GD.Print($"WARNING: {warning}");
        foreach (var problem in result.Problems)
            GD.Print($"PROBLEM: {problem}");
        GD.Print(result.Status);
        return result.Written is null ? 1 : 0;
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
        if (File.Exists(ProjectSettings.GlobalizePath(path)))
        {
            GD.PrintErr($"{path} already exists, and nothing has been changed.");
            return 2;
        }

        var size = (Arg("size") ?? "64x64").Split('x');
        var source = new RegionSource(id, Arg("name") ?? id, int.Parse(Arg("chapter") ?? "1"), int.Parse(size[0]), int.Parse(size[1]));
        var root = RegionScene.Build(source);
        RegionScene.Save(root, path);
        root.Free();
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

        var root = RegionScene.Build(source);
        RegionScene.Save(root, path);
        root.Free();
        GD.Print($"Wrote {path}");
        return 0;
    }
}
