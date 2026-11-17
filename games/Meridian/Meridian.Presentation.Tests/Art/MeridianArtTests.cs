using System.Security.Cryptography;
using Meridian.Presentation.Art;
using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Art;

public sealed class MeridianArtTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-art-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string set, string file, byte[] bytes)
    {
        Directory.CreateDirectory(Path.Combine(_root, set));
        File.WriteAllBytes(Path.Combine(_root, set, file), bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    [Fact]
    public void Art_with_a_matching_hash_is_used_and_anything_else_falls_back()
    {
        var good = Write("tiles", "bridge.aaaa.png", [1, 2, 3]);
        Write("tiles", "med_bay.bbbb.png", [4, 5, 6]);
        var open = Write("hatches", "open.cccc.png", [7]);
        File.WriteAllText(Path.Combine(_root, "tiles", "manifest.json"), $$"""
            { "version": 1, "tiles": {
                "bridge": { "file": "bridge.aaaa.png", "sha256": "{{good}}" },
                "med_bay": { "file": "med_bay.bbbb.png", "sha256": "{{new string('0', 64)}}" },
                "airlock": { "file": "airlock.dddd.png", "sha256": "{{new string('0', 64)}}" } } }
            """);
        File.WriteAllText(Path.Combine(_root, "hatches", "manifest.json"), $$"""
            { "states": { "open": { "file": "open.cccc.png", "sha256": "{{open}}", "edge": "north" } } }
            """);

        var art = MeridianArt.Load(_root);

        Assert.EndsWith("bridge.aaaa.png", art.Tile("bridge")!.Path);
        Assert.Null(art.Tile("med_bay"));     // its bytes don't match
        Assert.Null(art.Tile("airlock"));     // its file is missing
        Assert.Null(art.Tile("galley"));      // never drawn yet
        Assert.NotNull(art.Hatch(HatchState.Open));
        Assert.Null(art.Hatch(HatchState.Sealed));
        Assert.Null(art.Crew("ines_okafor")); // no crew set at all
        Assert.Equal(2, art.Problems.Count);
    }

    [Fact]
    public void The_shipped_pack_loads_without_problems()
    {
        var art = MeridianArt.Load(Path.GetDirectoryName(BasePack.Root)!);
        Assert.Empty(art.Problems);
    }

    [Fact]
    public void The_art_samples_manifest_format_is_read()
    {
        // The format of draft PR #357's crew manifest.
        var idle = Write("crew", "ines-idle.a.png", [1]);
        var walk = Write("crew", "ines-walk.b.png", [2]);
        File.WriteAllText(Path.Combine(_root, "crew", "manifest.json"), $$"""
            { "version": 1, "crew": { "ines_okafor": { "frameSize": [24, 24], "anchor": [12, 22],
                "idle": { "file": "ines-idle.a.png", "sha256": "{{idle}}", "frames": 1, "fps": 0 },
                "walk": { "file": "ines-walk.b.png", "sha256": "{{walk}}", "frames": 4, "fps": 6 } } } }
            """);
        var crew = MeridianArt.Load(_root).Crew("ines_okafor")!;
        Assert.Equal((4, 6, 12, 22), (crew.WalkFrames, crew.Fps, crew.AnchorX, crew.AnchorY));
    }
}
