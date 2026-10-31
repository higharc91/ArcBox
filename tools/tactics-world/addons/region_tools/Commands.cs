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
          --export=<id or scene>   export a region; --rules=<folder> writes to that pack instead of the base pack
          --swatches               write the palette's images (then run Godot's --import)
          --tilesets               write the palette's tile sets
          --sample=<scene>         write a small test scene with a stamp; --tamper paints over a stamped tile
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
        foreach (var problem in result.Problems)
            GD.Print($"PROBLEM: {problem}");
        if (result.Written is null)
            return 1;
        GD.Print($"Wrote {result.Written}");
        return 0;
    }

    /// <summary>A region of grass around River Crossing, with one beat. <c>--tamper</c> changes a stamped tile, which must be refused.</summary>
    private static int Sample(string path)
    {
        var map = RulesPack.Load([RegionPaths.Rules]).MapFor("river_crossing")!;
        var source = new RegionSource("sample", "Sample", 1, map.Width + 8, map.Height + 8) { Recovery = new Tile(1, 0) };
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                source.Set(new Tile(x, y), 0, "grass/lush", TileFlags.None);
        source.Stamp(map, new Tile(4, 4), 0);
        source.Beats.Add(new BeatSpot("ch1_sample", new Tile(2, 2)));
        if (Flag("tamper"))
            source.Set(new Tile(4, 4), 2, "ash/grey", TileFlags.None);

        var root = RegionScene.Build(source);
        RegionScene.Save(root, path);
        root.Free();
        GD.Print($"Wrote {path}");
        return 0;
    }
}
